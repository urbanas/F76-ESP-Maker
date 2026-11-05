using System.IO.Compression;
using System.Text;

namespace ItemFinder;

/// <summary>BA2 name hashes: CRC32 (reflected, poly EDB88320) with init 0 and no final xor,
/// over the lower-case file stem and directory (backslashes).</summary>
public static class Ba2Hash
{
    static readonly uint[] Table = Build();

    static uint[] Build()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Crc(string s)
    {
        uint c = 0;
        foreach (byte b in Encoding.Latin1.GetBytes(s)) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c;
    }

    public static (uint Name, uint Dir, string Ext) Of(string path)
    {
        string p = Ba2Path.Normalize(path);
        int slash = p.LastIndexOf('\\');
        string dir = slash >= 0 ? p[..slash] : "";
        string file = p[(slash + 1)..];
        int dot = file.LastIndexOf('.');
        return (Crc(dot >= 0 ? file[..dot] : file), Crc(dir), dot >= 0 ? file[(dot + 1)..] : "");
    }
}

public static class Ba2Path
{
    public static string Normalize(string path) => path.Replace('/', '\\').Trim().TrimStart('\\').ToLowerInvariant();
}

public sealed class Ba2Chunk
{
    public long Offset;
    public uint Packed, Size;
    public ushort StartMip, EndMip;
}

public sealed class Ba2Entry
{
    public string Name = "";
    public Ba2Archive Archive = null!;
    // GNRL
    public long Offset;
    public uint Packed, Size;
    // DX10
    public ushort Width, Height;
    public byte Mips, Format;
    public List<Ba2Chunk> Chunks = new();
}

public sealed class Ba2Archive
{
    public string FilePath = "";
    public string Type = "";
    public List<Ba2Entry> Entries = new();

    public string FileName => Path.GetFileName(FilePath);

    public static Ba2Archive Open(string path)
    {
        var a = new Ba2Archive { FilePath = path };
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        using var br = new BinaryReader(fs);
        if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "BTDX") throw new InvalidDataException($"{path} is not a BA2 archive");
        br.ReadUInt32();
        a.Type = Encoding.ASCII.GetString(br.ReadBytes(4));
        uint count = br.ReadUInt32();
        long nameOffset = br.ReadInt64();
        if (a.Type != "GNRL" && a.Type != "DX10") return a;
        for (int i = 0; i < count; i++)
        {
            var e = new Ba2Entry { Archive = a };
            br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
            if (a.Type == "GNRL")
            {
                br.ReadUInt32();
                e.Offset = br.ReadInt64();
                e.Packed = br.ReadUInt32();
                e.Size = br.ReadUInt32();
                br.ReadUInt32();
            }
            else
            {
                br.ReadByte();
                int chunks = br.ReadByte();
                br.ReadUInt16();
                e.Height = br.ReadUInt16();
                e.Width = br.ReadUInt16();
                e.Mips = br.ReadByte();
                e.Format = br.ReadByte();
                br.ReadByte(); br.ReadByte();
                for (int c = 0; c < chunks; c++)
                {
                    e.Chunks.Add(new Ba2Chunk
                    {
                        Offset = br.ReadInt64(), Packed = br.ReadUInt32(), Size = br.ReadUInt32(),
                        StartMip = br.ReadUInt16(), EndMip = br.ReadUInt16()
                    });
                    br.ReadUInt32();
                }
            }
            a.Entries.Add(e);
        }
        fs.Position = nameOffset;
        foreach (var e in a.Entries)
        {
            int len = br.ReadUInt16();
            e.Name = Ba2Path.Normalize(Encoding.Latin1.GetString(br.ReadBytes(len)));
        }
        return a;
    }

    byte[] ReadBlock(long offset, uint packed, uint size)
    {
        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Position = offset;
        var raw = new byte[packed != 0 ? packed : size];
        fs.ReadExactly(raw);
        return packed != 0 ? Zlib.Decompress(raw, (int)size) : raw;
    }

    public byte[] ReadFile(Ba2Entry e) => ReadBlock(e.Offset, e.Packed, e.Size);

    /// <summary>One chunk of a DX10 texture: the mips StartMip..EndMip, one after the other.</summary>
    public byte[] ReadChunk(Ba2Entry e, int chunk)
    {
        var c = e.Chunks[chunk];
        return ReadBlock(c.Offset, c.Packed, c.Size);
    }

    /// <summary>Top mip of a DX10 texture (the first chunk always starts at mip 0).</summary>
    public byte[] ReadTopMip(Ba2Entry e)
    {
        var c = e.Chunks[0];
        return ReadBlock(c.Offset, c.Packed, c.Size);
    }
}

public static class Zlib
{
    public static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }

    public static byte[] Decompress(byte[] data, int size)
    {
        var outBuf = new byte[size];
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        z.ReadExactly(outBuf);
        return outBuf;
    }
}

public sealed record TextureOut(string Path, int Width, int Height, byte Format, List<byte[]> Mips);

public static class Ba2Writer
{
    /// <summary>Same chunk layout as the game's own archives: one chunk per mip while the mip is
    /// larger than 256 px, then one chunk holding the rest. A single chunk for everything makes
    /// the game skip the texture (tested: the object turned invisible).</summary>
    static List<(int First, int Last)> Chunking(int width, int mips)
    {
        var list = new List<(int, int)>();
        int i = 0;
        while (i < mips - 1 && (width >> i) > 256) { list.Add((i, i)); i++; }
        list.Add((i, mips - 1));
        return list;
    }

    public static void WriteTextures(string file, IReadOnlyList<TextureOut> textures)
    {
        var plan = textures.Select(t => (Tex: t, Chunks: Chunking(t.Width, t.Mips.Count))).ToList();
        long dataStart = 24 + plan.Sum(p => 24 + 24L * p.Chunks.Count);
        var records = new MemoryStream();
        var rw = new BinaryWriter(records);
        var blobs = new List<byte[]>();
        long pos = dataStart;
        foreach (var (tex, chunks) in plan)
        {
            var h = Ba2Hash.Of(tex.Path);
            rw.Write(h.Name); rw.Write(Encoding.ASCII.GetBytes("dds\0")); rw.Write(h.Dir);
            rw.Write((byte)0); rw.Write((byte)chunks.Count); rw.Write((ushort)24);
            rw.Write((ushort)tex.Height); rw.Write((ushort)tex.Width);
            rw.Write((byte)tex.Mips.Count); rw.Write(tex.Format); rw.Write((byte)0); rw.Write((byte)8);
            foreach (var (first, last) in chunks)
            {
                var raw = Concat(tex.Mips.Skip(first).Take(last - first + 1));
                var packed = Zlib.Compress(raw);
                rw.Write(pos); rw.Write((uint)packed.Length); rw.Write((uint)raw.Length);
                rw.Write((ushort)first); rw.Write((ushort)last); rw.Write(0xBAADF00Du);
                blobs.Add(packed);
                pos += packed.Length;
            }
        }
        WriteArchive(file, "DX10", textures.Count, records.ToArray(), blobs, pos, textures.Select(t => t.Path));
    }

    public static void WriteGeneral(string file, IReadOnlyList<(string Path, byte[] Data)> files)
    {
        long dataStart = 24 + 36L * files.Count;
        var records = new MemoryStream();
        var rw = new BinaryWriter(records);
        var blobs = new List<byte[]>();
        long pos = dataStart;
        foreach (var (path, data) in files)
        {
            var h = Ba2Hash.Of(path);
            var packed = Zlib.Compress(data);
            rw.Write(h.Name); rw.Write(Encoding.ASCII.GetBytes(h.Ext.PadRight(4, '\0')[..4])); rw.Write(h.Dir);
            rw.Write(0x00100100u); rw.Write(pos); rw.Write((uint)packed.Length); rw.Write((uint)data.Length); rw.Write(0xBAADF00Du);
            blobs.Add(packed);
            pos += packed.Length;
        }
        WriteArchive(file, "GNRL", files.Count, records.ToArray(), blobs, pos, files.Select(f => f.Path));
    }

    static void WriteArchive(string file, string type, int count, byte[] records, List<byte[]> blobs, long nameOffset, IEnumerable<string> names)
    {
        string tmp = file + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new BinaryWriter(fs))
        {
            w.Write(Encoding.ASCII.GetBytes("BTDX")); w.Write(1u); w.Write(Encoding.ASCII.GetBytes(type));
            w.Write((uint)count); w.Write(nameOffset);
            w.Write(records);
            foreach (var b in blobs) w.Write(b);
            foreach (var n in names)
            {
                var bytes = Encoding.Latin1.GetBytes(Ba2Path.Normalize(n));
                w.Write((ushort)bytes.Length); w.Write(bytes);
            }
        }
        File.Move(tmp, file, true);
    }

    static byte[] Concat(IEnumerable<byte[]> parts)
    {
        var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p);
        return ms.ToArray();
    }
}
