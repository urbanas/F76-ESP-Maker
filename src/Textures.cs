using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ItemFinder;

/// <summary>A decoded texture: RGBA bytes, row-major.</summary>
public sealed record Rgba(int Width, int Height, byte[] Pixels);

public static class Dxgi
{
    public const byte B8G8R8A8 = 87, B8G8R8A8Srgb = 91;

    static readonly HashSet<int> Srgb = new() { 29, 72, 75, 78, 91, 93, 99 };

    public static bool IsSrgb(int fmt) => Srgb.Contains(fmt);

    public static string Name(int fmt) => fmt switch
    {
        28 or 29 => "RGBA8", 61 => "R8", 71 or 72 => "BC1", 74 or 75 => "BC2", 77 or 78 => "BC3",
        80 or 81 => "BC4", 83 or 84 => "BC5", 87 or 91 => "BGRA8", 98 or 99 => "BC7", _ => $"format {fmt}"
    } + (IsSrgb(fmt) ? " sRGB" : "");

    public static Rgba Decode(byte[] data, int w, int h, int fmt)
    {
        var px = new byte[w * h * 4];
        switch (fmt)
        {
            case 28: case 29: Buffer.BlockCopy(data, 0, px, 0, Math.Min(px.Length, data.Length)); break;
            case 87: case 91:
                for (int i = 0; i + 3 < px.Length && i + 3 < data.Length; i += 4)
                { px[i] = data[i + 2]; px[i + 1] = data[i + 1]; px[i + 2] = data[i]; px[i + 3] = data[i + 3]; }
                break;
            case 61:
                for (int i = 0; i < w * h && i < data.Length; i++)
                { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = data[i]; px[i * 4 + 3] = 255; }
                break;
            default: DecodeBlocks(data, w, h, fmt, px); break;
        }
        return new Rgba(w, h, px);
    }

    static void DecodeBlocks(byte[] data, int w, int h, int fmt, byte[] px)
    {
        int blockSize = fmt is 71 or 72 or 80 or 81 ? 8 : 16;
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        Parallel.For(0, bh, by =>
        {
            Span<byte> block = stackalloc byte[64];
            for (int bx = 0; bx < bw; bx++)
            {
                int o = (by * bw + bx) * blockSize;
                if (o + blockSize > data.Length) return;
                var src = data.AsSpan(o, blockSize);
                switch (fmt)
                {
                    case 71: case 72: Bc.Bc1(src, block, false); break;
                    case 77: case 78: Bc.Bc1(src[8..], block, true); Bc.Bc4(src, block, 3); break;
                    case 74: case 75: Bc.Bc1(src[8..], block, true); Bc.Bc2Alpha(src, block); break;
                    case 80: case 81: Bc.Bc4(src, block, 0); for (int i = 0; i < 16; i++) { block[i * 4 + 1] = block[i * 4 + 2] = block[i * 4]; block[i * 4 + 3] = 255; } break;
                    case 83: case 84: Bc.Bc4(src, block, 0); Bc.Bc4(src[8..], block, 1); for (int i = 0; i < 16; i++) { block[i * 4 + 2] = 255; block[i * 4 + 3] = 255; } break;
                    case 98: case 99: Bc7.Decode(src, block); break;
                    default: throw new NotSupportedException($"Texture format {fmt} is not supported");
                }
                for (int i = 0; i < 16; i++)
                {
                    int x = bx * 4 + i % 4, y = by * 4 + i / 4;
                    if (x < w && y < h) block.Slice(i * 4, 4).CopyTo(px.AsSpan((y * w + x) * 4, 4));
                }
            }
        });
    }

    /// <summary>Full mip chain (box filter), each level as BGRA bytes.</summary>
    public static List<byte[]> MipsBgra(Rgba img)
    {
        var levels = new List<byte[]>();
        int w = img.Width, h = img.Height;
        var cur = img.Pixels;
        while (true)
        {
            var bgra = new byte[cur.Length];
            for (int i = 0; i < cur.Length; i += 4)
            { bgra[i] = cur[i + 2]; bgra[i + 1] = cur[i + 1]; bgra[i + 2] = cur[i]; bgra[i + 3] = cur[i + 3]; }
            levels.Add(bgra);
            if (w == 1 && h == 1) break;
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
            var next = new byte[nw * nh * 4];
            var src = cur; int sw = w, sh = h;
            Parallel.For(0, nh, y =>
            {
                int y0 = Math.Min(2 * y, sh - 1), y1 = Math.Min(2 * y + 1, sh - 1);
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Math.Min(2 * x, sw - 1), x1 = Math.Min(2 * x + 1, sw - 1);
                    for (int c = 0; c < 4; c++)
                        next[(y * nw + x) * 4 + c] = (byte)((src[(y0 * sw + x0) * 4 + c] + src[(y0 * sw + x1) * 4 + c]
                            + src[(y1 * sw + x0) * 4 + c] + src[(y1 * sw + x1) * 4 + c] + 2) >> 2);
                }
            });
            cur = next; w = nw; h = nh;
        }
        return levels;
    }
}

/// <summary>Whole .dds files (UI icons are stored like this in general archives).</summary>
public static class DdsFile
{
    public static Rgba Read(byte[] d)
    {
        if (d.Length < 128 || d[0] != 'D' || d[1] != 'D' || d[2] != 'S') throw new InvalidDataException("Not a DDS file");
        int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
        string fourcc = System.Text.Encoding.ASCII.GetString(d, 84, 4);
        int fmt, start = 128;
        switch (fourcc)
        {
            case "DX10": fmt = BitConverter.ToInt32(d, 128); start = 148; break;
            case "DXT1": fmt = 71; break;
            case "DXT3": fmt = 74; break;
            case "DXT5": fmt = 77; break;
            case "ATI1": case "BC4U": fmt = 80; break;
            case "ATI2": case "BC5U": fmt = 83; break;
            default:
                if (BitConverter.ToInt32(d, 88) != 32) throw new NotSupportedException("Unsupported DDS pixel format");
                fmt = BitConverter.ToUInt32(d, 92) == 0xFF0000 ? 87 : 28;
                break;
        }
        return Dxgi.Decode(d[start..], w, h, fmt);
    }

    /// <summary>Uncompressed 32-bit BGRA with a classic header, one mip (same as the game's icons).</summary>
    public static byte[] WriteBgra(Rgba img)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("DDS "));
        bw.Write(124); bw.Write(0x100F); bw.Write(img.Height); bw.Write(img.Width); bw.Write(img.Width * 4);
        bw.Write(0); bw.Write(1);
        for (int i = 0; i < 11; i++) bw.Write(0);
        bw.Write(32); bw.Write(0x41); bw.Write(0); bw.Write(32);
        bw.Write(0x00FF0000u); bw.Write(0x0000FF00u); bw.Write(0x000000FFu); bw.Write(0xFF000000u);
        bw.Write(0x1000); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        bw.Write(Dxgi.MipsBgra(img)[0]);
        return ms.ToArray();
    }
}

public static class Bitmaps
{
    public static Bitmap FromRgba(Rgba img, bool opaque)
    {
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var bgra = new byte[img.Pixels.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = img.Pixels[i + 2]; bgra[i + 1] = img.Pixels[i + 1]; bgra[i + 2] = img.Pixels[i];
            bgra[i + 3] = opaque ? (byte)255 : img.Pixels[i + 3];
        }
        for (int y = 0; y < img.Height; y++)
            Marshal.Copy(bgra, y * img.Width * 4, data.Scan0 + y * data.Stride, img.Width * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    public static Rgba ToRgba(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w * 4, w * 4);
        bmp.UnlockBits(data);
        for (int i = 0; i < px.Length; i += 4) (px[i], px[i + 2]) = (px[i + 2], px[i]);
        return new Rgba(w, h, px);
    }

    /// <summary>Loads an image without keeping the file locked.</summary>
    public static Bitmap Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var img = Image.FromStream(fs);
        return new Bitmap(img);
    }
}

static class Bc
{
    static void Rgb565(ushort c, Span<int> o)
    {
        o[0] = ((c >> 11) & 31) * 255 / 31; o[1] = ((c >> 5) & 63) * 255 / 63; o[2] = (c & 31) * 255 / 31;
    }

    public static void Bc1(ReadOnlySpan<byte> b, Span<byte> outRgba, bool alwaysFourColors)
    {
        ushort c0 = (ushort)(b[0] | b[1] << 8), c1 = (ushort)(b[2] | b[3] << 8);
        uint bits = (uint)(b[4] | b[5] << 8 | b[6] << 16 | b[7] << 24);
        Span<int> a = stackalloc int[3], c = stackalloc int[3];
        Rgb565(c0, a); Rgb565(c1, c);
        Span<int> pal = stackalloc int[16];
        bool four = alwaysFourColors || c0 > c1;
        for (int k = 0; k < 3; k++)
        {
            pal[k] = a[k]; pal[4 + k] = c[k];
            pal[8 + k] = four ? (2 * a[k] + c[k]) / 3 : (a[k] + c[k]) / 2;
            pal[12 + k] = four ? (a[k] + 2 * c[k]) / 3 : 0;
        }
        pal[3] = pal[7] = pal[11] = 255; pal[15] = four ? 255 : 0;
        for (int i = 0; i < 16; i++)
        {
            int idx = (int)(bits >> (2 * i)) & 3;
            for (int k = 0; k < 4; k++) outRgba[i * 4 + k] = (byte)pal[idx * 4 + k];
        }
    }

    public static void Bc4(ReadOnlySpan<byte> b, Span<byte> outRgba, int channel)
    {
        int a0 = b[0], a1 = b[1];
        Span<int> pal = stackalloc int[8];
        pal[0] = a0; pal[1] = a1;
        if (a0 > a1) for (int i = 1; i < 7; i++) pal[i + 1] = ((7 - i) * a0 + i * a1) / 7;
        else { for (int i = 1; i < 5; i++) pal[i + 1] = ((5 - i) * a0 + i * a1) / 5; pal[6] = 0; pal[7] = 255; }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)b[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) outRgba[i * 4 + channel] = (byte)pal[(int)(bits >> (3 * i)) & 7];
    }

    public static void Bc2Alpha(ReadOnlySpan<byte> b, Span<byte> outRgba)
    {
        for (int i = 0; i < 16; i++) outRgba[i * 4 + 3] = (byte)(((b[i / 2] >> (4 * (i % 2))) & 15) * 17);
    }
}

static class Bc7
{
    static readonly ushort[] P2 = { 0xcccc,0x8888,0xeeee,0xecc8,0xc880,0xfeec,0xfec8,0xec80,0xc800,0xffec,0xfe80,0xe800,0xffe8,0xff00,0xfff0,0xf000,0xf710,0x008e,0x7100,0x08ce,0x008c,0x7310,0x3100,0x8cce,0x088c,0x3110,0x6666,0x366c,0x17e8,0x0ff0,0x718e,0x399c,0xaaaa,0xf0f0,0x5a5a,0x33cc,0x3c3c,0x55aa,0x9696,0xa55a,0x73ce,0x13c8,0x324c,0x3bdc,0x6996,0xc33c,0x9966,0x0660,0x0272,0x04e4,0x4e40,0x2720,0xc936,0x936c,0x39c6,0x639c,0x9336,0x9cc6,0x817e,0xe718,0xccf0,0x0fcc,0x7744,0xee22 };
    static readonly string[] P3 = { "0011001102210222","0001001122112221","0000200122112211","0222002200110111","0000000011221122","0011001100220022","0022002211111111","0011001122112211","0000000011112222","0000111111112222","0000111122222222","0012001200120012","0112011201120112","0122012201220122","0011011211221222","0011200122002220","0001001101120122","0111001200220001","0000000111221122","0011000100220122","0002000210021122","0002000122012211","0002000222222122","0022002200221122","0011012211221222","0111011122222222","0000000011112222","0000111111222222","0012001200220022","0112011201220122","0122012212221222","0000001101120112","0000220000001111","0222022200000000","0022111122111122","0011001100000022","0011002200220011","0000100022000000","0200020002000200","0011112211221122","0012001201120112","0222022202220222","0022011102220111","0000000000002121","0002002200221222","0001000101112111","0022012101220121","0120120120120120","0000111122220000","0012001201200120","0012001200200012","0120201201202012","0011220011220011","0011112211001122","0101010122222222","0000000021212121","0022110022110022","0022002201010101","0000000012121212","0020002200210022","0002000200010002","0120011201200112","0002021202120002","0002002200020022" };
    static readonly byte[] A2 = { 15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,2,8,2,2,8,8,15,2,8,2,2,8,8,2,2,15,15,6,8,2,8,15,15,2,8,2,2,2,15,15,6,6,2,6,8,15,15,2,2,15,15,15,15,15,2,2,15 };
    static readonly byte[] A3a = { 3,3,15,15,8,3,15,15,8,8,6,6,6,5,3,3,3,3,8,15,3,3,6,10,5,8,8,6,8,5,15,15,8,15,3,5,6,10,8,15,15,3,15,5,15,15,15,15,3,15,5,5,5,8,5,10,5,10,8,13,15,12,3,3 };
    static readonly byte[] A3b = { 15,8,8,3,15,15,3,8,15,15,15,15,15,15,15,8,15,8,15,3,15,8,15,8,3,15,6,10,15,15,10,8,15,3,15,10,10,8,9,10,6,15,8,15,3,6,6,8,15,3,15,15,15,15,15,15,15,15,15,15,3,15,15,8 };
    static readonly int[][] Weights = { Array.Empty<int>(), Array.Empty<int>(), new[] { 0, 21, 43, 64 },
        new[] { 0, 9, 18, 27, 37, 46, 55, 64 }, new[] { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 } };
    // subsets, partition bits, rotation bits, index-select bits, colour bits, alpha bits, endpoint p-bits, shared p-bits, index bits, index2 bits
    static readonly int[,] Modes = { { 3,4,0,0,4,0,1,0,3,0 }, { 2,6,0,0,6,0,0,1,3,0 }, { 3,6,0,0,5,0,0,0,2,0 }, { 2,6,0,0,7,0,1,0,2,0 },
        { 1,0,2,1,5,6,0,0,2,3 }, { 1,0,2,0,7,8,0,0,2,2 }, { 1,0,0,0,7,7,1,0,4,0 }, { 2,6,0,0,5,5,1,0,2,0 } };

    static int Subset(int ns, int part, int i) => ns == 1 ? 0 : ns == 2 ? (P2[part] >> i) & 1 : P3[part][i] - '0';

    static int Anchor(int ns, int part, int s) => s == 0 ? 0 : ns == 2 ? A2[part] : s == 1 ? A3a[part] : A3b[part];

    static int Interp(int e0, int e1, int idx, int bits) { int w = Weights[bits][idx]; return (e0 * (64 - w) + e1 * w + 32) >> 6; }

    public static void Decode(ReadOnlySpan<byte> b, Span<byte> outRgba)
    {
        ulong lo = BitConverter.ToUInt64(b), hi = BitConverter.ToUInt64(b[8..]);
        int pos = 0;
        int Take(int n)
        {
            if (n == 0) return 0;
            ulong r = pos >= 64 ? hi >> (pos - 64) : pos + n <= 64 ? lo >> pos : (lo >> pos) | (hi << (64 - pos));
            pos += n;
            return (int)(r & ((1UL << n) - 1));
        }
        int mode = 0;
        while (mode < 8 && Take(1) == 0) mode++;
        if (mode == 8) { outRgba.Clear(); return; }
        int ns = Modes[mode, 0], pb = Modes[mode, 1], rb = Modes[mode, 2], isb = Modes[mode, 3], cb = Modes[mode, 4],
            ab = Modes[mode, 5], epb = Modes[mode, 6], spb = Modes[mode, 7], ib = Modes[mode, 8], ib2 = Modes[mode, 9];
        int part = Take(pb), rot = Take(rb), isel = Take(isb);
        int ne = ns * 2;
        var ep = new int[6, 4];
        for (int e = 0; e < ne; e++) ep[e, 3] = 255;
        for (int c = 0; c < 3; c++) for (int e = 0; e < ne; e++) ep[e, c] = Take(cb);
        if (ab > 0) for (int e = 0; e < ne; e++) ep[e, 3] = Take(ab);
        var pbits = new int[6];
        bool hasP = false;
        if (epb == 1) { for (int e = 0; e < ne; e++) pbits[e] = Take(1); hasP = true; }
        else if (spb == 1) { var sp = new int[ns]; for (int s = 0; s < ns; s++) sp[s] = Take(1); for (int e = 0; e < ne; e++) pbits[e] = sp[e / 2]; hasP = true; }
        for (int e = 0; e < ne; e++)
            for (int c = 0; c < 4; c++)
            {
                if (c == 3 && ab == 0) continue;
                int bits = c < 3 ? cb : ab, v = ep[e, c];
                if (hasP) { v = (v << 1) | pbits[e]; bits++; }
                ep[e, c] = bits < 8 ? (v << (8 - bits)) | (v >> (2 * bits - 8)) : v;
            }
        Span<int> idx1 = stackalloc int[16], idx2 = stackalloc int[16];
        for (int i = 0; i < 16; i++) { int s = Subset(ns, part, i); idx1[i] = Take(ib - (i == Anchor(ns, part, s) ? 1 : 0)); }
        if (ib2 > 0) for (int i = 0; i < 16; i++) idx2[i] = Take(ib2 - (i == 0 ? 1 : 0));
        Span<int> px = stackalloc int[4];
        for (int i = 0; i < 16; i++)
        {
            int s = Subset(ns, part, i), e0 = 2 * s, e1 = 2 * s + 1;
            int ci, ai, cbits, abits;
            if (ib2 > 0) { if (isel == 0) { ci = idx1[i]; ai = idx2[i]; cbits = ib; abits = ib2; } else { ci = idx2[i]; ai = idx1[i]; cbits = ib2; abits = ib; } }
            else { ci = ai = idx1[i]; cbits = abits = ib; }
            for (int c = 0; c < 3; c++) px[c] = Interp(ep[e0, c], ep[e1, c], ci, cbits);
            px[3] = Interp(ep[e0, 3], ep[e1, 3], ai, abits);
            if (rot > 0) (px[3], px[rot - 1]) = (px[rot - 1], px[3]);
            for (int c = 0; c < 4; c++) outRgba[i * 4 + c] = (byte)px[c];
        }
    }
}
