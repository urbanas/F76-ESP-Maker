using static ItemFinder.Loc;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ItemFinder;

/// <summary>What Import made of a .ba2. Unknown: models it changes that no entry has.</summary>
public sealed record ImportResult(Project Project, string Source, string Kind, int Models, List<string> Unknown);

/// <summary>Reads a .ba2 back into a project. ESP Maker builds carry their setup inside; older builds and other mods
/// are worked out from the models they change (the same model -> entry link as linked ticking) and their materials.</summary>
public static class Importer
{
    /// <summary>The setup inside a built mod. The game never asks for this path, so it just sits in the archive.</summary>
    public const string SetupPath = @"itemfinder\setup.json";
    public const string Exact = "ESP Maker mod", OlderBuild = "older ESP Maker mod", OtherMod = "another mod";

    // in every .bgem seen (v20 to v22, any number of texture paths) the block after the base colour has one size:
    // scale, 4 falloff values, light influence, min LOD, soft depth, emittance, 3 more floats and 2 flags
    const int ColourFromEnd = 67, FalloffBeforeColour = 4, ZTest = 0x2C;

    sealed record Mat(string Name, string? Colour, bool Falloff, bool DepthTest);

    public static ImportResult Read(string path, List<Category> cats)
    {
        var file = MainArchive(path);
        if (Path.GetFileName(file).StartsWith("SeventySix - ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("that is one of the game's own archives.");
        var main = Ba2Archive.Open(file);
        if (main.Type != "GNRL") throw new InvalidDataException("it only holds textures - pick the mod's main .ba2.");
        string stem = Path.GetFileNameWithoutExtension(file);
        if (main.Entries.FirstOrDefault(e => e.Name == SetupPath) is Ba2Entry saved)
        {
            var kept = JsonSerializer.Deserialize<Project>(main.ReadFile(saved), Json.Options) ?? throw new InvalidDataException("its saved setup is empty.");
            kept.ModName = stem;
            return new ImportResult(kept, file, Exact, main.Entries.Count(IsModel), new());
        }
        bool own = GameData.IsOwn(main);
        var archives = own ? new List<Ba2Archive> { main } : Siblings(file, main);
        var entries = archives.SelectMany(a => a.Entries).GroupBy(e => e.Name).Select(g => g.First()).ToList();
        var models = entries.Where(IsModel).ToDictionary(e => e.Name[@"meshes\".Length..]);
        if (models.Count == 0) throw new InvalidDataException("it changes no models, so there is nothing to select.");
        var mats = entries.Where(e => e.Name.EndsWith(".bgem")).ToDictionary(e => e.Name);

        var p = Project.Fresh();
        p.ModName = own ? stem : stem + Tr(" edited"); // never the other mod's own file name
        if (!own) p.ImportedFrom = archives.Select(a => a.FileName).ToList();
        var found = Select(p, cats, models.Keys.ToHashSet(), out var unknown);
        var read = new Dictionary<string, Mat?>();
        var used = models.Keys.Except(unknown).ToDictionary(m => m, m => MaterialsIn(models[m].Archive.ReadFile(models[m]), mats, read));
        var looks = found.Select(f => (f.Cat, f.Entry, Look: own ? OwnLook(f, used) : OtherLook(f, used))).Where(x => x.Look != null).ToList();
        foreach (var g in looks.GroupBy(x => x.Cat))
        {
            // the look most of the category has goes on the category, the rest on their items
            var key = (Style s) => JsonSerializer.Serialize(s, Json.Options);
            var common = g.GroupBy(x => key(x.Look!)).OrderByDescending(x => x.Count()).First().First().Look!;
            p.CategoryStyles[g.Key.Name] = common;
            foreach (var x in g.Where(x => key(x.Look!) != key(common))) p.Overrides[x.Entry.Id] = x.Look!;
        }
        return new ImportResult(p, file, own ? OlderBuild : OtherMod, models.Count, unknown.OrderBy(m => m).ToList());
    }

    static bool IsModel(Ba2Entry e) => e.Name.StartsWith(@"meshes\") && e.Name.EndsWith(".nif");

    /// <summary>The main archive when its texture archive was picked.</summary>
    static string MainArchive(string path)
    {
        const string tail = " - Textures.ba2";
        if (!path.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) return path;
        var main = path[..^tail.Length] + ".ba2";
        return File.Exists(main) ? main : path;
    }

    /// <summary>The archive and the rest of its mod: "X - Main.ba2" next to "X - Settings.ba2" (materials can be in the other one).</summary>
    static List<Ba2Archive> Siblings(string file, Ba2Archive main)
    {
        var list = new List<Ba2Archive> { main };
        var stem = Path.GetFileNameWithoutExtension(file);
        int dash = stem.IndexOf(" - ", StringComparison.Ordinal);
        var name = dash > 0 ? stem[..dash] : stem;
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        foreach (var other in Directory.GetFiles(folder, name + " - *.ba2").Append(Path.Combine(folder, name + ".ba2")))
        {
            if (!File.Exists(other) || string.Equals(other, file, StringComparison.OrdinalIgnoreCase)) continue;
            try { var a = Ba2Archive.Open(other); if (a.Type == "GNRL") list.Add(a); } catch { }
        }
        return list;
    }

    /// <summary>Selects what lights exactly these models: each entry with one of them, its other parts left out. Entries
    /// that share a model light up together anyway, so they all go in - with their own parts left out, nothing extra shows.</summary>
    static List<(Category Cat, Entry Entry, List<string> Models)> Select(Project p, List<Category> cats, HashSet<string> models, out HashSet<string> unknown)
    {
        var found = new List<(Category, Entry, List<string>)>();
        var left = new HashSet<string>(models);
        var all = cats.SelectMany(c => c.Entries.Where(e => e.Group == null).Select(e => (Cat: c, Entry: e))).ToList();
        void Take(Category c, Entry e, IEnumerable<string> pool)
        {
            var on = e.Models.Where(m => models.Contains(Ba2Path.Normalize(m))).ToList();
            if (!pool.Intersect(on.Select(Ba2Path.Normalize)).Any()) return;
            p.Selected.Add(e.Id);
            if (on.Count < e.Models.Count) p.OffParts[e.Id] = e.Models.Except(on).ToHashSet();
            left.ExceptWith(on.Select(Ba2Path.Normalize));
            found.Add((c, e, on));
        }
        foreach (var (c, e) in all.Where(x => !x.Entry.NoLink)) Take(c, e, models);
        foreach (var (c, e) in all.Where(x => x.Entry.NoLink)) Take(c, e, left); // never linked: only for what is still open
        unknown = left;
        return found;
    }

    /// <summary>The materials of this archive that a model uses. Strings in a .nif have their length in front, which
    /// tells a real name from whatever bytes happen to end in ".bgem".</summary>
    static List<Mat> MaterialsIn(byte[] nif, Dictionary<string, Ba2Entry> mats, Dictionary<string, Mat?> read)
    {
        var list = new List<Mat>();
        var end = Encoding.ASCII.GetBytes(".bgem");
        for (int i = nif.AsSpan().IndexOf(end); i >= 0;)
        {
            if (NameEndingAt(nif, i + end.Length) is string name)
            {
                int at = name.IndexOf(@"materials\", StringComparison.Ordinal);
                var key = at >= 0 ? name[at..] : @"materials\" + name;
                if (mats.TryGetValue(key, out var entry))
                {
                    if (!read.TryGetValue(key, out var mat)) read[key] = mat = ReadMaterial(key, entry.Archive.ReadFile(entry));
                    if (mat != null && !list.Contains(mat)) list.Add(mat);
                }
            }
            int next = nif.AsSpan(i + 1).IndexOf(end);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return list;
    }

    static string? NameEndingAt(byte[] d, int end)
    {
        for (int start = end - 6; start >= 4 && end - start <= 260; start--)
            if (BitConverter.ToUInt32(d, start - 4) is uint n && (n == end - start || n == end - start + 1))
                return Ba2Path.Normalize(Encoding.Latin1.GetString(d, start, end - start));
        return null;
    }

    static Mat? ReadMaterial(string name, byte[] m)
    {
        int at = m.Length - ColourFromEnd;
        if (at < 0x40 || Encoding.ASCII.GetString(m, 0, 4) != "BGEM") return null;
        var rgb = new[] { BitConverter.ToSingle(m, at), BitConverter.ToSingle(m, at + 4), BitConverter.ToSingle(m, at + 8) };
        float max = rgb.Max();
        bool usable = rgb.All(v => float.IsFinite(v) && v >= 0 && v <= 64) && max > 0.001f;
        string? colour = usable ? Materials.Hex(Color.FromArgb(Byte(rgb[0], max), Byte(rgb[1], max), Byte(rgb[2], max))) : null;
        return new Mat(name, colour, m[at - FalloffBeforeColour] != 0, m[ZTest] != 0);
    }

    /// <summary>A colour value as a byte; brighter than white keeps its hue.</summary>
    static int Byte(float v, float max) => (int)Math.Round(255 * Math.Clamp(max > 1 ? v / max : v, 0, 1));

    static float Chroma(Mat m)
    {
        if (m.Colour == null) return -1;
        var c = Materials.Parse(m.Colour);
        return Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
    }

    /// <summary>Another mod's look: the most common highlight on the item's models. Its strength and beam size can't be
    /// compared with ours, so those stay at the category's; the user can change them.</summary>
    static Style? OtherLook((Category Cat, Entry Entry, List<string> Models) f, Dictionary<string, List<Mat>> used)
    {
        var picks = f.Models.Select(m => used.GetValueOrDefault(Ba2Path.Normalize(m))?.OrderByDescending(Chroma).FirstOrDefault()).OfType<Mat>().Where(m => m.Colour != null).ToList();
        if (picks.Count == 0) return null;
        var mat = picks.GroupBy(m => m).OrderByDescending(g => g.Count()).First().Key;
        var st = f.Cat.DefaultStyle.Clone();
        var file = Path.GetFileNameWithoutExtension(mat.Name);
        bool arrow = file.Contains("arrow"), beam = file.Contains("beam");
        st.GlowColor = st.BeamColor = mat.Colour!;
        st.GlowFill = !mat.Falloff;
        st.ThroughWalls = !mat.DepthTest;
        st.Glow = !arrow && !beam;
        st.Beam = arrow || beam;
        st.BeamArrow = arrow;
        return st;
    }

    static readonly Regex OwnMaterial = new(@"\\itemfinder\\(?:(?<glow>glow[ab])_(?<hex>[0-9a-f]{6})_(?<power>\d+)(?<x>_x)?(?<o>_o)?(?:_cut\w+)?" +
        @"|beam_(?<beam>[0-9a-f]{6})_(?<power>\d+)(?<x>_x)?|icon3d_(?<solid>[0-9a-f]{6})(?<x>_x)?|icon_(?<icon>.+?)_(?<hex>[0-9a-f]{6})(?<x>_x)?" +
        @"|model_(?<model>.+?)(?:_(?<hex>[0-9a-f]{6}))?(?<x>_x)?)\.bgem$");

    /// <summary>An older build's look: our material names spell it out. Beam length and width are in the models, so they
    /// stay at the category's.</summary>
    static Style? OwnLook((Category Cat, Entry Entry, List<string> Models) f, Dictionary<string, List<Mat>> used)
    {
        var names = f.Models.SelectMany(m => used.GetValueOrDefault(Ba2Path.Normalize(m)) ?? new()).Select(m => OwnMaterial.Match(m.Name)).Where(m => m.Success).ToList();
        if (names.Count == 0) return null;
        var st = f.Cat.DefaultStyle.Clone();
        st.Glow = st.Beam = st.BeamArrow = st.ThroughWalls = false;
        st.Icon = "";
        string? solid = null;
        foreach (var m in names)
        {
            if (m.Groups["x"].Success) st.ThroughWalls = true;
            if (m.Groups["glow"].Success && !st.Glow)
            {
                st.Glow = true;
                st.GlowColor = "#" + m.Groups["hex"].Value.ToUpperInvariant();
                st.GlowOpacity = int.Parse(m.Groups["power"].Value);
                st.GlowFill = !m.Groups["o"].Success;
            }
            else if (m.Groups["beam"].Success)
            {
                st.Beam = true;
                st.BeamColor = "#" + m.Groups["beam"].Value.ToUpperInvariant();
                st.BeamStrength = int.Parse(m.Groups["power"].Value);
            }
            else if (m.Groups["solid"].Success) solid = "#" + m.Groups["solid"].Value.ToUpperInvariant();
            else if (m.Groups["icon"].Success && Icons.Get(m.Groups["icon"].Value) != null) st.Icon = m.Groups["icon"].Value;
            else if (m.Groups["model"].Success && Models3D.Get(Models3D.Prefix + m.Groups["model"].Value) != null) st.Icon = Models3D.Prefix + m.Groups["model"].Value;
        }
        // the solid material without an icon of its own is the arrow marker
        if (solid != null && st.Icon == "" && !st.Beam) { st.Beam = st.BeamArrow = true; st.BeamColor = solid; }
        return st;
    }
}
