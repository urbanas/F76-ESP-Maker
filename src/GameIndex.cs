using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItemFinder;

/// <summary>A record from SeventySix.esm, with just the fields this app needs.</summary>
public sealed class EsmRecord
{
    public string Type { get; set; } = "";
    public uint FormId { get; set; }
    public string EditorId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Model path relative to meshes\ (world model).</summary>
    public string Model { get; set; } = "";
    /// <summary>FLOR: what harvesting gives (an item or a leveled list).</summary>
    public uint Harvest { get; set; }
    /// <summary>ALCH: magic effects.</summary>
    public List<uint>? Effects { get; set; }
    /// <summary>COBJ: the item it makes.</summary>
    public uint Product { get; set; }
    /// <summary>COBJ: components. LVLI: entries. ARMO: its armor addons (ARMA).</summary>
    public List<uint>? Parts { get; set; }
    /// <summary>RACE / NPC_: keywords.</summary>
    public List<uint>? Keywords { get; set; }
    /// <summary>RACE: skin (ARMO). ARMA: race.</summary>
    public uint Link { get; set; }
    /// <summary>ARMA: third-person models (male, female); ARMO: ground models (male, female). Relative to meshes\.</summary>
    public List<string>? Models { get; set; }
    /// <summary>ARMA: the body slots it takes (BOD2 biped flags: bit 0 = slot 30 hair top, 1 = 31 hair long, 2 = 32 head,
    /// 3 = 33 body, 4/5 = 34/35 hands).</summary>
    public uint Slots { get; set; }
    /// <summary>ARMA: index of the female model in <see cref="Models"/>, -1 = none.</summary>
    public int FemaleAt { get; set; } = -1;
    /// <summary>WEAP while reading: Parts already hold the default object template's mods.</summary>
    [JsonIgnore] public bool DefaultTemplate { get; set; }
}

/// <summary>Everything read from the game's item database, cached in game-index.json until
/// SeventySix.esm changes (so it is rebuilt automatically after a game update).</summary>
public sealed class GameIndex
{
    const int CacheVersion = 13; // 13: ARMA slots (BOD2) + female model index // 5: ARMA additional races (Parts); 6: NPC_ (race, default outfit) and OTFT (items); 7: MISC components (MCQP) + CMPO; 8: WEAP first FULL; 9: WEAP default mods (Parts) + OMOD; 10: FURN; 11: ARMO ground models; 12: DOOR + MSTT (Everything else)
    /// <summary>Record types that are things lying or standing in the world.</summary>
    public static readonly HashSet<string> WorldTypes = new() { "MISC", "ALCH", "INGR", "BOOK", "NOTE", "FLOR", "AMMO", "KEYM", "WEAP", "CONT", "ACTI" };
    static readonly HashSet<string> OtherTypes = new() { "LVLI", "COBJ", "MGEF", "RACE", "ARMA", "ARMO", "KYWD", "NPC_", "OTFT", "CMPO", "OMOD", "FURN", "DOOR", "MSTT" };

    public int Version { get; set; }
    public long EsmSize { get; set; }
    public long EsmTime { get; set; }
    public List<EsmRecord> Records { get; set; } = new();
    /// <summary>How many times each base object is placed in the world (REFR records), by form id.</summary>
    public Dictionary<uint, int> Placed { get; set; } = new();

    [JsonIgnore] public Dictionary<uint, EsmRecord> ById { get; private set; } = new();

    static string CachePath => AppPaths.File("game-index.json");

    public static GameIndex Load(GameData game, Action<string> status)
    {
        var esm = new FileInfo(Path.Combine(game.DataFolder, "SeventySix.esm"));
        if (!esm.Exists) throw new FileNotFoundException("SeventySix.esm not found in " + game.DataFolder);
        GameIndex? idx = null;
        if (File.Exists(CachePath))
            try
            {
                var cached = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(CachePath));
                if (cached != null && cached.Version == CacheVersion && cached.EsmSize == esm.Length && cached.EsmTime == esm.LastWriteTimeUtc.Ticks)
                    idx = cached;
            }
            catch { }
        if (idx == null)
        {
            idx = Build(game, esm, status);
            try { File.WriteAllText(CachePath, JsonSerializer.Serialize(idx)); } catch { }
        }
        idx.ById = idx.Records.GroupBy(r => r.FormId).ToDictionary(g => g.Key, g => g.First());
        return idx;
    }

    static GameIndex Build(GameData game, FileInfo esm, Action<string> status)
    {
        status("Reading item names...");
        var names = ReadStrings(game);
        var idx = new GameIndex { Version = CacheVersion, EsmSize = esm.Length, EsmTime = esm.LastWriteTimeUtc.Ticks };
        status("Reading the item database (first time after a game update, ~15 s)...");
        using var fs = new FileStream(esm.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
        var head = new byte[24];
        while (fs.Position + 24 <= fs.Length)
        {
            fs.ReadExactly(head);
            string type = Encoding.ASCII.GetString(head, 0, 4);
            if (type == "GRUP") continue;
            uint size = BitConverter.ToUInt32(head, 4), flags = BitConverter.ToUInt32(head, 8), fid = BitConverter.ToUInt32(head, 12);
            bool world = WorldTypes.Contains(type);
            if (type == "REFR")
            {
                // placed objects: only the base object (NAME) is needed - count them
                var rb = new byte[size];
                fs.ReadExactly(rb);
                try
                {
                    if ((flags & 0x40000) != 0) rb = Zlib.Decompress(rb[4..], BitConverter.ToInt32(rb, 0));
                    foreach (var (tag, val) in Subrecords(rb))
                        if (tag == "NAME" && val.Length == 4) { var b = BitConverter.ToUInt32(val); idx.Placed[b] = idx.Placed.GetValueOrDefault(b) + 1; break; }
                }
                catch { }
                continue;
            }
            if (!world && !OtherTypes.Contains(type)) { fs.Seek(size, SeekOrigin.Current); continue; }
            var body = new byte[size];
            fs.ReadExactly(body);
            if ((flags & 0x40000) != 0) body = Zlib.Decompress(body[4..], BitConverter.ToInt32(body, 0));
            var r = new EsmRecord { Type = type, FormId = fid };
            foreach (var (tag, val) in Subrecords(body))
                switch (tag)
                {
                    case "EDID": r.EditorId = ZString(val); break;
                    case "MODL" when type == "ARMO": if (val.Length == 4) (r.Parts ??= new()).Add(BitConverter.ToUInt32(val)); break;
                    case "MODL" when type == "ARMA": if (val.Length == 4) (r.Parts ??= new()).Add(BitConverter.ToUInt32(val)); break; // additional races
                    // apparel lying on the ground: male / female world models (often *_go.nif, not the worn ones)
                    case "MOD2" or "MOD4" when type == "ARMO":
                    case "MOD2" or "MOD3" when type == "ARMA":
                        if (ZString(val) is { Length: > 0 } m)
                        {
                            if (tag == "MOD3") r.FemaleAt = r.Models?.Count ?? 0;
                            (r.Models ??= new()).Add(m);
                        }
                        break;
                    case "BOD2" when type == "ARMA" && val.Length >= 4: r.Slots = BitConverter.ToUInt32(val); break;
                    case "WNAM" when type == "RACE" && val.Length == 4: r.Link = BitConverter.ToUInt32(val); break;
                    case "RNAM" when type is "ARMA" or "NPC_" && val.Length == 4: r.Link = BitConverter.ToUInt32(val); break;
                    case "DOFT" when type == "NPC_" && val.Length == 4: r.Product = BitConverter.ToUInt32(val); break; // default outfit
                    case "INAM" when type == "OTFT":
                        for (int k = 0; k + 4 <= val.Length; k += 4) (r.Parts ??= new()).Add(BitConverter.ToUInt32(val, k));
                        break;
                    // weapons: object templates (include count, property count, levels, addon index i16, default flag,
                    // keyword count u8 + keywords, 2 bytes, then 7-byte includes starting with the OMOD) - the default
                    // template's mods are the parts the gun is assembled from (its MODL is often an empty dummy receiver)
                    case "OBTS" when type == "WEAP" && val.Length >= 18 && (r.Parts == null || !r.DefaultTemplate):
                    {
                        int inc = BitConverter.ToInt32(val, 0), p = 16 + 4 * val[15] + 2;
                        if (inc < 0 || p + 7 * inc > val.Length) break;
                        r.Parts = Enumerable.Range(0, inc).Select(k => BitConverter.ToUInt32(val, p + 7 * k)).ToList();
                        r.DefaultTemplate = val[14] != 0;
                        break;
                    }
                    case "KWDA" when type == "RACE":
                        for (int k = 0; k + 4 <= val.Length; k += 4) (r.Keywords ??= new()).Add(BitConverter.ToUInt32(val, k));
                        break;
                    // weapons: later FULLs name their object templates ("Default", "Simple Epic") - keep the first
                    case "FULL" when val.Length == 4 && (type != "WEAP" || r.Name == ""): r.Name = names.GetValueOrDefault(BitConverter.ToUInt32(val)) ?? ""; break;
                    case "MODL" when r.Model == "": r.Model = ZString(val); break;
                    case "PFIG" when val.Length == 4: r.Harvest = BitConverter.ToUInt32(val); break;
                    case "EFID" when val.Length == 4: (r.Effects ??= new()).Add(BitConverter.ToUInt32(val)); break;
                    case "CNAM" when type == "COBJ" && val.Length == 4: r.Product = BitConverter.ToUInt32(val); break;
                    // components: form id, count, curve table (12 bytes each)
                    case "FVPA":
                        for (int k = 0; k + 12 <= val.Length; k += 12) (r.Parts ??= new()).Add(BitConverter.ToUInt32(val, k));
                        break;
                    case "LVLO" when val.Length >= 4: (r.Parts ??= new()).Add(BitConverter.ToUInt32(val, 0)); break;
                    // junk scrap: component (CMPO), quantity keyword (8 bytes each)
                    case "MCQP" when type == "MISC":
                        for (int k = 0; k + 8 <= val.Length; k += 8) (r.Parts ??= new()).Add(BitConverter.ToUInt32(val, k));
                        break;
                }
            if (world && (r.Model.Length == 0 || r.Name.Trim().Length == 0)) continue;
            idx.Records.Add(r);
        }
        return idx;
    }

    /// <summary>True if the leveled list (or item) can give the item, following nested lists.</summary>
    /// <summary>Spots in the world where each item can be found: placed directly, or placed as a leveled list
    /// (a random-spawn spot) that can give it. Things only in inventories (crafted, looted) have none.</summary>
    public Dictionary<uint, int> WorldSpots()
    {
        var spots = new Dictionary<uint, int>(Placed);
        void Spread(uint list, int count, int depth, HashSet<uint> seen)
        {
            if (depth > 6 || !seen.Add(list) || !ById.TryGetValue(list, out var r) || r.Parts == null) return;
            foreach (var p in r.Parts.Distinct())
            {
                if (ById.TryGetValue(p, out var x) && x.Type == "LVLI") Spread(p, count, depth + 1, seen);
                else spots[p] = spots.GetValueOrDefault(p) + count;
            }
        }
        foreach (var (fid, n) in Placed)
            if (ById.TryGetValue(fid, out var r) && r.Type == "LVLI") Spread(fid, n, 0, new());
        return spots;
    }

    public bool Gives(uint source, uint item, int depth = 0)
    {
        if (source == item) return true;
        if (depth > 4 || !ById.TryGetValue(source, out var r) || r.Type != "LVLI" || r.Parts == null) return false;
        return r.Parts.Any(p => Gives(p, item, depth + 1));
    }

    static string ZString(ReadOnlySpan<byte> v)
    {
        int end = v.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? v[..end] : v);
    }

    static IEnumerable<(string Tag, byte[] Value)> Subrecords(byte[] data)
    {
        int i = 0;
        int? big = null;
        while (i + 6 <= data.Length)
        {
            string tag = Encoding.ASCII.GetString(data, i, 4);
            int size = BitConverter.ToUInt16(data, i + 4);
            i += 6;
            if (tag == "XXXX") { big = BitConverter.ToInt32(data, i); i += size; continue; }
            if (big is int b) { size = b; big = null; }
            if (i + size > data.Length) yield break;
            yield return (tag, data[i..(i + size)]);
            i += size;
        }
    }

    /// <summary>English names from the game's own strings file (inside SeventySix - Localization.ba2).</summary>
    static Dictionary<uint, string> ReadStrings(GameData game)
    {
        var e = game.Find(@"strings\seventysix_en.strings") ?? throw new FileNotFoundException("English strings file not found");
        var d = e.Archive.ReadFile(e);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1252 = Encoding.GetEncoding(1252);
        int count = BitConverter.ToInt32(d, 0), baseOff = 8 + 8 * count;
        var map = new Dictionary<uint, string>(count);
        for (int k = 0; k < count; k++)
        {
            uint id = BitConverter.ToUInt32(d, 8 + 8 * k);
            int off = baseOff + BitConverter.ToInt32(d, 12 + 8 * k);
            int end = Array.IndexOf(d, (byte)0, off);
            if (end > off) map[id] = cp1252.GetString(d, off, end - off);
        }
        return map;
    }
}
