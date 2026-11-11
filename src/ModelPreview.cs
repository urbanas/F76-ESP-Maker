using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace ItemFinder;

/// <summary>How a part is drawn: the item's own parts, and what this app adds (glow, beam, icons).</summary>
enum PartKind { Solid, Blend, Effect, Glow, Beam, Icon, Icon3D, Model }

/// <summary>One drawable part, in model space (z up). A camera-facing icon keeps its local quad in
/// <see cref="Local"/> and turns to the camera around <see cref="Anchor"/>.</summary>
sealed class PreviewPart
{
    public PartKind Kind;
    public string Name = "";
    public float[] P = Array.Empty<float>();   // x, y, z per vertex
    public float[]? N;                         // normals
    public float[]? Uv;                        // u, v per vertex
    public byte[]? Col;                        // r, g, b, a per vertex
    public int[] Tri = Array.Empty<int>();
    public Rgba? Tex;
    public int TestRef = -1;                   // alpha test threshold, -1 = none
    public bool Billboard;
    public (float X, float Y, float Z) Anchor;
    public bool Skinned;                       // animated part (placed by its bones)
    public bool Filled;                        // glow drawn as a fill whatever the look says (animated objects, as built)
}

sealed class PreviewScene
{
    public List<PreviewPart> Parts = new();
    /// <summary>Box of the item itself (not the beam or icon).</summary>
    public (float X, float Y, float Z) Min, Max;
    /// <summary>Box of everything, beam and icon included.</summary>
    public (float X, float Y, float Z) AllMin, AllMax;
    /// <summary>The ground the game buries part of the model under (a plant built around its middle): nothing
    /// below it is drawn, except what shows through walls. NaN: the model stands on its lowest point.</summary>
    public float Ground = float.NaN;
    public string Note = "";
    /// <summary>A hidden worn outfit shown as the bare body / hands that replace it: drawn even with an invisible look.</summary>
    public bool Bare;
}

/// <summary>Builds a picture of an item as the mod would show it: the game's model patched exactly like a build
/// (glow copies, beam tube, icons), with placeholder materials that the renderer colours from the look.</summary>
static class ModelPreview
{
    /// <summary>A creature's dismemberment cap shape: HeadCap, BodyCap, radRat_headCap, LegCap01... (+ the preview's glow copy).</summary>
    static readonly System.Text.RegularExpressions.Regex DismemberCap =
        new(@"(head|body|neck|torso|arm|leg|limb|tail|wing|eye|tentacle|jaw)_?\w{0,3}cap\d*(:\d+)?(_IFGlow)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    const string GlowTag = "itemfinder-preview-glow", BeamTag = "itemfinder-preview-beam", IconTag = "itemfinder-preview-icon", Icon3DTag = "itemfinder-preview-icon3d",
                 ModelTag = "itemfinder-preview-model", LabelTag = "itemfinder-preview-label";
    /// <summary>The label picture of the scene being loaded (previews load on several threads).</summary>
    [ThreadStatic] static Rgba? labelTex;
    static readonly Dictionary<string, Rgba?> textures = new(StringComparer.OrdinalIgnoreCase);
    const int MaxTexture = 512;

    /// <summary>An entry's model as the build makes it: a hidden worn outfit part is its bare body / hands.</summary>
    public static PreviewScene LoadAs(GameData game, Entry e, string model, Style st, bool beamHere, bool keepOtherMods)
    {
        if (e.HideAs(model, st) is not string bare) return Load(game, model, st, e.NoIcon, e.Harvestable, beamHere, keepOtherMods, e.Name);
        var s = Load(game, bare.Length > 0 ? bare : model, st, e.NoIcon, e.Harvestable, beamHere, keepOtherMods, e.Name);
        s.Bare = true;
        return s;
    }

    /// <param name="beamHere">The model carries the beam and icon (an entry's main model).</param>
    public static PreviewScene Load(GameData game, string model, Style st, bool noIcon, bool harvestable, bool beamHere, bool keepOtherMods,
                                    string? name = null)
    {
        // the name label: drawn like the build draws it (a flat camera-facing card with the text picture)
        labelTex = null;
        float labelRatio = 0;
        if (beamHere && st.Label && !noIcon && !string.IsNullOrEmpty(name))
            using (var lb = Icons.DrawLabel(st.LabelText.Trim().Length > 0 ? st.LabelText.Trim() : Icons.LabelText(name))) { labelTex = Bitmaps.ToRgba(lb); labelRatio = lb.Width / (float)lb.Height; }
        var path = @"meshes\" + model;
        var winner = game.Find(path);
        var src = (keepOtherMods ? winner : game.FindOriginal(path) ?? winner) ?? throw new FileNotFoundException("this model isn't in the game files");
        var data = src.Archive.ReadFile(src);
        var chosen = Icons.Get(st.Icon);
        bool use3D = noIcon || st.Icon3D;
        var opts = new PatchOptions(st.Glow ? p => new GlowMat(GlowTag, p.Cutout ? p.Threshold : -1) : null,
                                    beamHere && st.Beam ? (st.BeamArrow ? Icon3DTag : BeamTag) : null,
                                    st.BeamArrow ? NifPatch.ArrowSize(st.BeamWidth) is var ar ? ar.Gap + ar.Height : 0 : st.BeamLength, st.BeamWidth, harvestable,
                                    beamHere && !use3D && chosen != null ? IconTag : null, st.IconSize,
                                    beamHere && chosen != null ? Icons.Mask(chosen) : null, Icon3DTag, st.BeamArrow, // solid where flat can't show
                                    beamHere && Models3D.Get(st.Icon) is Model3D m3 ? Models3D.MeshOf(m3) : null, ModelTag,
                                    labelTex != null ? LabelTag : null, st.LabelSize * labelRatio, st.LabelSize);
        var scene = new PreviewScene();
        byte[] patched = data;
        try { patched = NifPatch.Apply(data, opts).Data; }
        catch (Exception) when (game.FindOriginal(path) is { } orig && orig != src)
        {
            // another mod's version this app can't read: the build starts from the game's own model then - so does this
            data = orig.Archive.ReadFile(orig);
            patched = data;
            try
            {
                patched = NifPatch.Apply(data, opts).Data;
                scene.Note = $"{src.Archive.FileName}'s version of this model can't be read - built from the game's own model (that mod's change is replaced).";
            }
            catch (Exception ex) { scene.Note = "The mod can't change this model (" + ex.Message + ") - shown as in the game."; }
        }
        catch (Exception ex) { scene.Note = "The mod can't change this model (" + ex.Message + ") - shown as in the game."; }
        var f = NifFile.Parse(patched);
        Rgba? iconTex = null;
        if (chosen != null && !use3D) { using var bmp = Icons.Draw(chosen, 128); iconTex = Bitmaps.ToRgba(bmp); }
        if (Models3D.Get(st.Icon) is Model3D mt) iconTex = Models3D.TextureOf(mt); // the model's own texture
        var worlds = new Dictionary<int, Xf>();
        NodeWorlds(f, 0, Xf.Identity, worlds);
        Walk(f, 0, Xf.Identity, null, scene, game, iconTex, new HashSet<int>(), worlds);

        // animated objects (not creatures or worn outfits) get a fill glow, as Builder makes them
        if (!noIcon) foreach (var p in scene.Parts.Where(p => p.Kind == PartKind.Glow && p.Skinned)) p.Filled = true;

        // cut-out glow copies take the cut-out of the part they copy
        var byName = scene.Parts.Where(p => p.Kind is PartKind.Solid or PartKind.Blend).GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());
        foreach (var p in scene.Parts.Where(p => p.Kind == PartKind.Glow && p.TestRef >= 0))
            p.Tex = byName.GetValueOrDefault(p.Name.Replace("_IFGlow", ""))?.Tex;

        var item = scene.Parts.Where(p => p.Kind is PartKind.Solid or PartKind.Blend).ToList();
        if (item.Count == 0) item = scene.Parts.Where(p => p.Kind != PartKind.Beam && !p.Billboard).ToList();
        if (item.Count == 0 && src != (game.FindOriginal(path) ?? src))
            throw new InvalidDataException($"{src.Archive.FileName} removes this model (empty in that mod) - nothing to show");
        if (item.Count == 0) throw new InvalidDataException("this model has nothing that can be drawn");
        (scene.Min, scene.Max) = Bounds(item);
        (scene.AllMin, scene.AllMax) = Bounds(scene.Parts);
        // a plant built around its middle (Blight: 49 cm below to 49 cm above) is placed with that middle on the
        // ground - the lower half is buried in the game. Stand it on its origin and cut off what's underground
        // A landscape piece (a deathclaw nest: branches 40 cm into the ground; a disturbed grave: mostly underground,
        // its hidden skirt has a "TEMP" placeholder texture) stands on its origin the same way
        bool landscape = model.StartsWith(@"landscape\", StringComparison.OrdinalIgnoreCase);
        if ((harvestable || landscape) && scene.Min.Z < -2 && scene.Max.Z > 2)
        {
            scene.Ground = 0;
            scene.Min.Z = 0;
            scene.AllMin.Z = Math.Max(0, scene.AllMin.Z);
        }
        return scene;
    }

    /// <summary>A weapon put together from its parts (receiver, barrel, grip...): every model in one picture, the beam
    /// and icon on the first. Parts that can't be shown are left out.</summary>
    public static PreviewScene LoadAll(GameData game, IList<string> models, Style st, bool noIcon, bool harvestable, bool beamHere, bool keepOtherMods)
    {
        // a part that names a connect point in its BSConnectPoint::Children sits on that point of a part before it
        // (BSConnectPoint::Parents: node, point name, rotation, place, scale); most guns are modelled in place
        var points = new Dictionary<string, Xf>(StringComparer.OrdinalIgnoreCase);
        // the part names "C-Barrel", the gun has "P-Barrel"
        static string Point(string s) => s.Length > 2 && s[1] == '-' ? s[2..] : s;
        Xf Joints(string model, Xf at)
        {
            if (game.Find(@"meshes\" + model) is not { } src) return at;
            var f = NifFile.Parse(src.Archive.ReadFile(src));
            var worlds = new Dictionary<int, Xf>();
            NodeWorlds(f, 0, Xf.Identity, worlds);
            for (int i = 0; i < f.Blocks.Count; i++)
            {
                var b = f.Blocks[i].Data;
                try
                {
                    if (f.TypeOf(i) == "BSConnectPoint::Children" && BitConverter.ToInt32(b, 5) > 0 &&
                        points.TryGetValue(Point(Encoding.ASCII.GetString(b, 13, BitConverter.ToInt32(b, 9)).TrimEnd('\0')), out var p)) at = p;
                }
                catch (ArgumentException) { }
            }
            for (int i = 0; i < f.Blocks.Count; i++)
            {
                if (f.TypeOf(i) != "BSConnectPoint::Parents") continue;
                var b = f.Blocks[i].Data;
                int o = 8;
                string Text() { int n = BitConverter.ToInt32(b, o); var s = Encoding.ASCII.GetString(b, o + 4, n).TrimEnd('\0'); o += 4 + n; return s; }
                try
                {
                    for (int k = 0, count = BitConverter.ToInt32(b, 4); k < count; k++)
                    {
                        string node = Text(), name = Text();
                        float F(int j) => BitConverter.ToSingle(b, o + 4 * j);
                        float w = F(0), x = F(1), y = F(2), z = F(3);
                        var r = new[] { 1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y),
                                        2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x),
                                        2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y) };
                        var local = new Xf(r, F(4), F(5), F(6), F(7) is > 0 and var s && float.IsFinite(s) ? s : 1);
                        o += 32;
                        int nodeIdx = worlds.Keys.FirstOrDefault(n => f.Strings.ElementAtOrDefault(BitConverter.ToInt32(f.Blocks[n].Data, 0)) == node, 0);
                        points.TryAdd(Point(name.StartsWith("P-") ? name : node), at.Then(worlds.GetValueOrDefault(nodeIdx, Xf.Identity)).Then(local));
                    }
                }
                catch (ArgumentException) { }
            }
            return at;
        }
        var scene = Load(game, models[0], st, noIcon, harvestable, beamHere, keepOtherMods);
        Joints(models[0], Xf.Identity);
        var (min, max) = (scene.Min, scene.Max);
        foreach (var m in models.Skip(1))
        {
            PreviewScene s;
            Xf at;
            try { s = Load(game, m, st, noIcon, harvestable, false, keepOtherMods); at = Joints(m, Xf.Identity); }
            catch { continue; }
            if (!at.Equals(Xf.Identity))
            {
                foreach (var p in s.Parts)
                {
                    for (int i = 0; i + 2 < p.P.Length; i += 3) (p.P[i], p.P[i + 1], p.P[i + 2]) = at.Apply(p.P[i], p.P[i + 1], p.P[i + 2]);
                    if (p.N != null) for (int i = 0; i + 2 < p.N.Length; i += 3) (p.N[i], p.N[i + 1], p.N[i + 2]) = at.Rotate(p.N[i], p.N[i + 1], p.N[i + 2]);
                    p.Anchor = at.Apply(p.Anchor.X, p.Anchor.Y, p.Anchor.Z);
                }
                var (a, b) = (at.Apply(s.Min.X, s.Min.Y, s.Min.Z), at.Apply(s.Max.X, s.Max.Y, s.Max.Z));
                (s.Min, s.Max) = Bounds(new[] { new PreviewPart { P = new[] { a.Item1, a.Item2, a.Item3, b.Item1, b.Item2, b.Item3 } } });
            }
            scene.Parts.AddRange(s.Parts);
            (min, max) = Bounds(new[] { new PreviewPart { P = new[] { min.X, min.Y, min.Z, max.X, max.Y, max.Z, s.Min.X, s.Min.Y, s.Min.Z, s.Max.X, s.Max.Y, s.Max.Z } } });
        }
        (scene.Min, scene.Max) = (min, max);
        (scene.AllMin, scene.AllMax) = Bounds(scene.Parts);
        return scene;
    }

    static ((float, float, float), (float, float, float)) Bounds(IEnumerable<PreviewPart> parts)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
        void Add(float x, float y, float z) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z); }
        foreach (var p in parts)
        {
            if (p.Billboard)
            {
                float r = 0;
                for (int i = 0; i < p.P.Length; i += 3) r = Math.Max(r, Math.Max(Math.Abs(p.P[i]), Math.Abs(p.P[i + 1])));
                Add(p.Anchor.X - r, p.Anchor.Y - r, p.Anchor.Z - r); Add(p.Anchor.X + r, p.Anchor.Y + r, p.Anchor.Z + r);
                continue;
            }
            for (int i = 0; i < p.P.Length; i += 3) Add(p.P[i], p.P[i + 1], p.P[i + 2]);
        }
        if (x0 > x1) return ((0, 0, 0), (1, 1, 1));
        return ((x0, y0, z0), (x1, y1, z1));
    }

    /// <summary>Translation, rotation (row-major, applied to column vectors) and scale of a node or shape.</summary>
    readonly record struct Xf(float[] R, float Tx, float Ty, float Tz, float S)
    {
        public static readonly Xf Identity = new(new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 0, 0, 0, 1);

        public static Xf Of(byte[] b)
        {
            int o = 8 + 4 * BitConverter.ToInt32(b, 4) + 4 + 4;
            var r = new float[9];
            for (int i = 0; i < 9; i++) r[i] = BitConverter.ToSingle(b, o + 12 + 4 * i);
            float s = BitConverter.ToSingle(b, o + 48);
            return new Xf(r, BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8), float.IsFinite(s) && s != 0 ? s : 1);
        }

        public (float, float, float) Rotate(float x, float y, float z) =>
            (R[0] * x + R[1] * y + R[2] * z, R[3] * x + R[4] * y + R[5] * z, R[6] * x + R[7] * y + R[8] * z);

        public (float, float, float) Apply(float x, float y, float z)
        {
            var (a, b, c) = Rotate(x, y, z);
            return (Tx + S * a, Ty + S * b, Tz + S * c);
        }

        /// <summary>This (parent) then the child's own transform.</summary>
        public Xf Then(Xf c)
        {
            var r = new float[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i * 3 + j] = R[i * 3] * c.R[j] + R[i * 3 + 1] * c.R[3 + j] + R[i * 3 + 2] * c.R[6 + j];
            var (tx, ty, tz) = Apply(c.Tx, c.Ty, c.Tz);
            return new Xf(r, tx, ty, tz, S * c.S);
        }
    }

    static bool Hidden(byte[] b) => (BitConverter.ToUInt32(b, 8 + 4 * BitConverter.ToInt32(b, 4) + 4) & 1) != 0;

    /// <summary>Every node's place in the model in the standing pose (the bones of animated parts).</summary>
    static void NodeWorlds(NifFile f, int n, Xf parent, Dictionary<int, Xf> worlds)
    {
        if (!f.IsNode(n) || worlds.ContainsKey(n)) return;
        var xf = parent.Then(Xf.Of(f.Blocks[n].Data));
        worlds[n] = xf;
        foreach (var k in f.Children(n)) NodeWorlds(f, k, xf, worlds);
    }

    static void Walk(NifFile f, int n, Xf parent, (float, float, float)? billboard, PreviewScene scene, GameData game, Rgba? iconTex, HashSet<int> seen,
                     Dictionary<int, Xf> worlds)
    {
        if (n < 0 || n >= f.Blocks.Count || !seen.Add(n)) return;
        var b = f.Blocks[n].Data;
        if (f.IsNode(n))
        {
            if (Hidden(b)) return;
            var xf = parent.Then(Xf.Of(b));
            var type = f.TypeOf(n);
            if (type == "NiBillboardNode") billboard = (xf.Tx, xf.Ty, xf.Tz);
            var kids = f.Children(n);
            // switches (plants: 0 = not harvested) and LOD nodes show one child
            if (type == "NiSwitchNode" && kids.Count > 0)
            {
                int end = NifFile.AvObjectEnd(b) + 4 + 4 * kids.Count;
                int index = end + 6 <= b.Length ? BitConverter.ToInt32(b, end + 2) : 0;
                kids = new() { kids[Math.Clamp(index, 0, kids.Count - 1)] };
            }
            else if (type == "NiLODNode" && kids.Count > 0) kids = new() { kids[0] };
            foreach (var k in kids) Walk(f, k, xf, billboard, scene, game, iconTex, seen, worlds);
            return;
        }
        if (!f.IsShape(n) || Hidden(b) || NifPatch.IsEditorOnly(f.Strings.ElementAtOrDefault(BitConverter.ToInt32(b, 0)) ?? "")) return;
        try { if (Shape(f, b, parent, billboard, game, iconTex, worlds) is PreviewPart p) scene.Parts.Add(p); }
        catch { } // a part that can't be read is left out of the picture
    }

    /// <summary>An animated part's bones: each one's place in the standing pose times the skin's own offset for it
    /// (BSSkin::BoneData: per bone a bound sphere, rotation, translation, scale - the inverse of the bone's rest place
    /// in the part's own frame). Null when the part has no usable skin.</summary>
    static Xf[]? Bones(NifFile f, int skin, Dictionary<int, Xf> worlds)
    {
        if (f.TypeOf(skin) != "BSSkin::Instance") return null;
        var s = f.Blocks[skin].Data;
        int data = BitConverter.ToInt32(s, 4), count = BitConverter.ToInt32(s, 8);
        if (f.TypeOf(data) != "BSSkin::BoneData") return null;
        var d = f.Blocks[data].Data;
        if (BitConverter.ToInt32(d, 0) != count || 4 + 68 * count > d.Length || 12 + 4 * count > s.Length) return null;
        var bones = new Xf[count];
        for (int i = 0; i < count; i++)
        {
            int o = 4 + 68 * i + 16;
            var r = new float[9];
            for (int k = 0; k < 9; k++) r[k] = BitConverter.ToSingle(d, o + 4 * k);
            float sc = BitConverter.ToSingle(d, o + 48);
            var bind = new Xf(r, BitConverter.ToSingle(d, o + 36), BitConverter.ToSingle(d, o + 40), BitConverter.ToSingle(d, o + 44), float.IsFinite(sc) && sc != 0 ? sc : 1);
            bones[i] = worlds.GetValueOrDefault(BitConverter.ToInt32(s, 12 + 4 * i), Xf.Identity).Then(bind);
        }
        return bones;
    }

    static PreviewPart? Shape(NifFile f, byte[] b, Xf parent, (float X, float Y, float Z)? billboard, GameData game, Rgba? iconTex, Dictionary<int, Xf> worlds)
    {
        int r = NifFile.ShapeRefs(b);
        int skin = BitConverter.ToInt32(b, r), shader = BitConverter.ToInt32(b, r + 4), alpha = BitConverter.ToInt32(b, r + 8);
        ulong desc = BitConverter.ToUInt64(b, r + 12);
        int triCount = BitConverter.ToInt32(b, r + 20), vertCount = BitConverter.ToUInt16(b, r + 24), dataSize = BitConverter.ToInt32(b, r + 26);
        int q = r + 30;
        int stride = (int)(desc & 0xF) * 4, attrs = (int)((desc >> 44) & 0xFFF);
        if (dataSize == 0 || stride == 0 || vertCount == 0 || (attrs & 1) == 0) return null;
        if (q + vertCount * stride + triCount * 6 > b.Length) return null;

        var shaderType = f.TypeOf(shader);
        if (shaderType is not ("BSLightingShaderProperty" or "BSEffectShaderProperty")) return null;
        var material = f.Strings.ElementAtOrDefault(BitConverter.ToInt32(f.Blocks[shader].Data, 0)) ?? "";
        var part = new PreviewPart { Name = f.Strings.ElementAtOrDefault(BitConverter.ToInt32(b, 0)) ?? "" };
        if (NifPatch.IsRefraction(part.Name, material)) return null;
        // creatures' dismemberment caps (the gore disc at a cut neck or limb): the game only shows them once that part is
        // shot off - drawn on the whole body they looked like missing skin (Behemoth, Radrat, Attack Dog - user, 2026-11-11)
        if (DismemberCap.IsMatch(part.Name)) return null;
        bool blend = false;
        if (f.TypeOf(alpha) == "NiAlphaProperty")
        {
            var ab = f.Blocks[alpha].Data;
            int af = NifFile.AlphaFlags(ab);
            int flags = BitConverter.ToUInt16(ab, af);
            if ((flags & 0x200) != 0) part.TestRef = ab[af + 2];
            blend = (flags & 1) != 0;
        }
        part.Kind = material switch
        {
            GlowTag => PartKind.Glow,
            BeamTag => PartKind.Beam,
            IconTag or LabelTag => PartKind.Icon,
            Icon3DTag => PartKind.Icon3D,
            ModelTag => PartKind.Model,
            _ => shaderType == "BSEffectShaderProperty" ? PartKind.Effect : blend && part.TestRef < 0 ? PartKind.Blend : PartKind.Solid,
        };
        if (part.Kind is PartKind.Solid or PartKind.Blend or PartKind.Effect)
        {
            part.Tex = MaterialTexture(game, material);
            if (part.Tex == null && f.TypeOf(shader + 1) == "BSShaderTextureSet") part.Tex = Texture(game, NifFile.FirstTexture(f.Blocks[shader + 1].Data));
            if (part.Kind == PartKind.Effect && part.Tex == null) return null; // a bare effect (particles, light cones)
        }
        else if (material == LabelTag) part.Tex = labelTex;
        else if (part.Kind is PartKind.Icon or PartKind.Model) part.Tex = iconTex;

        bool full = (attrs & 0x400) != 0;
        int uvOff = (int)((desc >> 8) & 0xF) * 4, nOff = (int)((desc >> 16) & 0xF) * 4, cOff = (int)((desc >> 24) & 0xF) * 4;
        // animated parts are placed by their bones (standing pose); others follow their nodes
        var xf = skin >= 0 ? Xf.Identity : parent.Then(Xf.Of(b));
        var bones = skin >= 0 && (attrs & 0x40) != 0 ? Bones(f, skin, worlds) : null;
        int sOff = (int)((desc >> 28) & 0xF) * 4;
        bool bill = billboard != null && skin < 0;
        part.Skinned = skin >= 0;
        part.P = new float[vertCount * 3];
        if ((attrs & 2) != 0) part.Uv = new float[vertCount * 2];
        if ((attrs & 8) != 0) part.N = new float[vertCount * 3];
        if ((attrs & 0x20) != 0) part.Col = new byte[vertCount * 4];
        for (int v = 0; v < vertCount; v++)
        {
            int p = q + v * stride;
            float x, y, z;
            if (full) { x = BitConverter.ToSingle(b, p); y = BitConverter.ToSingle(b, p + 4); z = BitConverter.ToSingle(b, p + 8); }
            else { x = H(b, p); y = H(b, p + 2); z = H(b, p + 4); }
            if (!float.IsFinite(x + y + z)) x = y = z = 0;
            float nx = 0, ny = 0, nz = 0;
            if (part.N != null) { nx = b[p + nOff] / 127.5f - 1; ny = b[p + nOff + 1] / 127.5f - 1; nz = b[p + nOff + 2] / 127.5f - 1; }
            if (bones != null)
            {
                // weighted sum over up to four bones (weights: 4 halves, then 4 bone indices)
                float px = 0, py = 0, pz = 0, qx = 0, qy = 0, qz = 0, wsum = 0;
                for (int k = 0; k < 4; k++)
                {
                    float w = H(b, p + sOff + 2 * k);
                    int bi = b[p + sOff + 8 + k];
                    if (!(w > 0) || bi >= bones.Length) continue;
                    var (ax, ay, az) = bones[bi].Apply(x, y, z);
                    var (bx2, by2, bz2) = bones[bi].Rotate(nx, ny, nz);
                    px += w * ax; py += w * ay; pz += w * az; qx += w * bx2; qy += w * by2; qz += w * bz2; wsum += w;
                }
                if (wsum > 0) { x = px / wsum; y = py / wsum; z = pz / wsum; nx = qx; ny = qy; nz = qz; }
                part.P[v * 3] = x; part.P[v * 3 + 1] = y; part.P[v * 3 + 2] = z;
            }
            else if (bill) { part.P[v * 3] = x * xf.S; part.P[v * 3 + 1] = y * xf.S; part.P[v * 3 + 2] = z * xf.S; }
            else (part.P[v * 3], part.P[v * 3 + 1], part.P[v * 3 + 2]) = xf.Apply(x, y, z);
            if (part.Uv != null) { part.Uv[v * 2] = H(b, p + uvOff); part.Uv[v * 2 + 1] = H(b, p + uvOff + 2); }
            if (part.N != null)
                (part.N[v * 3], part.N[v * 3 + 1], part.N[v * 3 + 2]) = bones != null ? (nx, ny, nz) : xf.Rotate(nx, ny, nz);
            if (part.Col != null) Buffer.BlockCopy(b, p + cOff, part.Col, v * 4, 4);
        }
        if (bill) { part.Billboard = true; part.Anchor = billboard!.Value; }
        int t0 = q + vertCount * stride;
        part.Tri = new int[triCount * 3];
        for (int k = 0; k < triCount * 3; k++)
        {
            int i = BitConverter.ToUInt16(b, t0 + 2 * k);
            part.Tri[k] = i < vertCount ? i : 0;
        }
        return part;
    }

    static float H(byte[] b, int p) => (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b, p));

    /// <summary>Colour texture named in a .bgsm / .bgem material (v22: the first texture string at 0x3C).</summary>
    static Rgba? MaterialTexture(GameData game, string material)
    {
        if (!material.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase) && !material.EndsWith(".bgem", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Ba2Path.Normalize(material);
        if (!path.StartsWith(@"materials\")) path = @"materials\" + path;
        lock (textures) if (textures.TryGetValue("mat:" + path, out var hit)) return hit;
        Rgba? tex = null;
        try
        {
            if (game.Find(path) is Ba2Entry e)
            {
                var m = e.Archive.ReadFile(e);
                int len = m.Length > 0x40 ? BitConverter.ToInt32(m, 0x3C) : 0;
                if (len > 1 && 0x40 + len <= m.Length) tex = Texture(game, Encoding.ASCII.GetString(m, 0x40, len - 1));
            }
        }
        catch { }
        lock (textures) textures["mat:" + path] = tex;
        return tex;
    }

    internal static Rgba? LoadTexture(GameData game, string file) => Texture(game, file);

    static Rgba? Texture(GameData game, string file)
    {
        if (file.Length == 0) return null;
        var path = Ba2Path.Normalize(file);
        if (!path.StartsWith(@"textures\")) path = @"textures\" + path;
        lock (textures) if (textures.TryGetValue(path, out var hit)) return hit;
        Rgba? tex = null;
        try { if (game.Find(path) is Ba2Entry e) tex = e.Archive.Type == "DX10" ? Dx10(e) : DdsFile.Read(e.Archive.ReadFile(e)); }
        catch { }
        lock (textures) textures[path] = tex;
        return tex;
    }

    /// <summary>The first mip no bigger than <see cref="MaxTexture"/> (read from the chunk that holds it).</summary>
    static Rgba Dx10(Ba2Entry e)
    {
        int fmt = e.Format;
        long MipSize(int w, int h) => fmt switch
        {
            71 or 72 or 80 or 81 => (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8,
            74 or 75 or 77 or 78 or 83 or 84 or 98 or 99 => (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16,
            61 => (long)w * h,
            _ => (long)w * h * 4,
        };
        int want = 0;
        while (want + 1 < Math.Max(1, (int)e.Mips) && Math.Max(e.Width >> want, e.Height >> want) > MaxTexture) want++;
        for (int c = e.Chunks.Count - 1; c >= 0; c--)
        {
            var ch = e.Chunks[c];
            if (want < ch.StartMip || want > ch.EndMip) continue;
            var data = e.Archive.ReadChunk(e, c);
            long off = 0;
            for (int m = ch.StartMip; m < want; m++) off += MipSize(Math.Max(1, e.Width >> m), Math.Max(1, e.Height >> m));
            int w = Math.Max(1, e.Width >> want), h = Math.Max(1, e.Height >> want);
            return Dxgi.Decode(data[(int)off..], w, h, fmt);
        }
        return Dxgi.Decode(e.Archive.ReadTopMip(e), e.Width, e.Height, fmt);
    }
}

/// <summary>Software renderer for a <see cref="PreviewScene"/>: z-buffered textured triangles with one light; the
/// app's glow, beam and 3D icon add light like the game's additive materials do.</summary>
/// <summary>Things drawn with the item: a floor grid under it (when <paramref name="Grid"/>) and solid props - a 1.8 m
/// person, a house to compare sizes, walls around the item. Props hide what is behind them like any wall in the game.</summary>
sealed record SizeGuide(float FloorZ, float CX, float CY, float Step, float Radius, bool Grid, PreviewPart[] Props, int Backdrop = 0)
{
    /// <summary>Plain boxes and roofs in flat colours, made into one solid part.</summary>
    sealed class Mesh
    {
        readonly List<float> p = new();
        readonly List<int> tri = new();
        readonly List<byte> col = new();
        readonly float ox, oy, oz;
        public Mesh(float x, float y, float z) => (ox, oy, oz) = (x, y, z);

        void Faces(int b, (int, int, int, int)[] quads, Color c, int corners)
        {
            for (int i = 0; i < corners; i++) col.AddRange(new[] { c.R, c.G, c.B, (byte)255 });
            foreach (var (a, d, e, f) in quads) tri.AddRange(e == f ? new[] { b + a, b + d, b + e } : new[] { b + a, b + d, b + e, b + a, b + e, b + f });
        }

        public void Box(float x0, float x1, float y0, float y1, float z0, float z1, Color c)
        {
            int b = p.Count / 3;
            for (int i = 0; i < 8; i++) p.AddRange(new[] { ox + ((i & 1) == 0 ? x0 : x1), oy + ((i & 2) == 0 ? y0 : y1), oz + ((i & 4) == 0 ? z0 : z1) });
            Faces(b, new[] { (0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3) }, c, 8);
        }

        /// <summary>A gable roof over x0..x1 / y0..y1 from z0, its ridge along x, <paramref name="rise"/> high.</summary>
        public void Roof(float x0, float x1, float y0, float y1, float z0, float rise, Color c)
        {
            int b = p.Count / 3;
            float ym = (y0 + y1) / 2;
            foreach (var (x, y, z) in new[] { (x0, y0, z0), (x1, y0, z0), (x0, y1, z0), (x1, y1, z0), (x0, ym, z0 + rise), (x1, ym, z0 + rise) })
                p.AddRange(new[] { ox + x, oy + y, oz + z });
            Faces(b, new[] { (0, 1, 5, 4), (2, 3, 5, 4), (0, 2, 4, 4), (1, 3, 5, 5), (0, 1, 3, 2) }, c, 6);
        }

        public PreviewPart Part(string name) =>
            new() { Kind = PartKind.Solid, Name = name, P = p.ToArray(), Tri = tri.ToArray(), Col = col.ToArray() };

        public int Corners => p.Count / 3;

        /// <summary>Turns the corners from <paramref name="from"/> on around the upright line through x, y (mesh space).</summary>
        public void Turn(int from, float a, float x, float y)
        {
            float c = MathF.Cos(a), s = MathF.Sin(a);
            for (int i = from * 3; i < p.Count; i += 3)
            {
                float dx = p[i] - ox - x, dy = p[i + 1] - oy - y;
                p[i] = ox + x + dx * c - dy * s; p[i + 1] = oy + y + dx * s + dy * c;
            }
        }

        /// <summary>A thin upright blade from x, y: <paramref name="h"/> high, leaning along angle <paramref name="a"/>.</summary>
        public void Blade(float x, float y, float h, float a, Color c)
        {
            int b = p.Count / 3;
            float w = Math.Max(0.6f, h * 0.06f), lean = h * 0.3f;
            float cx = MathF.Cos(a), sy = MathF.Sin(a);
            p.AddRange(new[] { ox + x - w * sy, oy + y + w * cx, oz, ox + x + w * sy, oy + y - w * cx, oz, ox + x + lean * cx, oy + y + lean * sy, oz + h });
            for (int i = 0; i < 3; i++) col.AddRange(new[] { (byte)(c.R * (i == 2 ? 1.25f : 0.8f)), (byte)(c.G * (i == 2 ? 1.15f : 0.8f)), (byte)(c.B * (i == 2 ? 1.1f : 0.8f)), (byte)255 });
            tri.AddRange(new[] { b, b + 1, b + 2 });
        }
    }

    /// <summary>Rubble and grass on the floor around the item (outside its footprint), always the same for one item:
    /// does the look still stand out in a messy place?</summary>
    public static PreviewPart JunkAround((float X, float Y, float Z) min, (float X, float Y, float Z) max, int seed)
    {
        var rnd = new Random(seed);
        float cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2;
        float inner = 0.5f * MathF.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y)) + 6;
        float outer = inner + Math.Max(120, inner * 1.5f);
        var m = new Mesh(cx, cy, min.Z);
        (float X, float Y) Spot()
        {
            float a = (float)(rnd.NextDouble() * MathF.Tau), r = inner + (outer - inner) * MathF.Sqrt((float)rnd.NextDouble());
            return (r * MathF.Cos(a), r * MathF.Sin(a));
        }
        Color[] stone = { Color.FromArgb(120, 116, 108), Color.FromArgb(96, 90, 82), Color.FromArgb(138, 128, 112), Color.FromArgb(104, 84, 66) };
        for (int i = 0; i < 40; i++) // broken concrete, bricks, bits of wood
        {
            var (x, y) = Spot();
            float sx = 2 + (float)rnd.NextDouble() * 10, sy = 2 + (float)rnd.NextDouble() * 7, sz = 1 + (float)rnd.NextDouble() * 5;
            int from = m.Corners;
            m.Box(x - sx, x + sx, y - sy, y + sy, 0, sz, stone[rnd.Next(stone.Length)]);
            m.Turn(from, (float)(rnd.NextDouble() * MathF.PI), x, y);
        }
        Color[] grass = { Color.FromArgb(92, 108, 52), Color.FromArgb(118, 112, 60), Color.FromArgb(78, 92, 48) };
        for (int i = 0; i < 70; i++) // tufts of dry grass
        {
            var (x, y) = Spot();
            var c = grass[rnd.Next(grass.Length)];
            for (int b = 0; b < 5; b++)
                m.Blade(x + (float)(rnd.NextDouble() - 0.5) * 6, y + (float)(rnd.NextDouble() - 0.5) * 6, 8 + (float)rnd.NextDouble() * 22, (float)(rnd.NextDouble() * MathF.Tau), c);
        }
        return m.Part("junk");
    }

    /// <summary>A plain 1.8 m figure (126 units) standing at x, y, z, its front facing -y.</summary>
    public static PreviewPart PersonAt(float x, float y, float z)
    {
        var m = new Mesh(x, y, z);
        var c = Color.FromArgb(140, 150, 165);
        m.Box(-11, -2, -5, 5, 0, 59, c); m.Box(2, 11, -5, 5, 0, 59, c);       // legs (84 cm)
        m.Box(-15, 15, -7, 7, 57, 103, c);                                    // body
        m.Box(-22, -16, -4, 4, 60, 102, c); m.Box(16, 22, -4, 4, 60, 102, c); // arms
        m.Box(-3, 3, -3, 3, 103, 108, c);                                     // neck
        m.Box(-7, 7, -8, 7, 108, 126, c);                                     // head
        return m.Part("person");
    }

    /// <summary>Half the house's width (x) and depth (y): 7 m × 6 m.</summary>
    public const float HouseHalfX = 245, HouseHalfY = 210;

    /// <summary>A two-storey house (7 m × 6 m, 8.4 m to the ridge) centred on x, y, its door facing -y.</summary>
    public static PreviewPart HouseAt(float x, float y, float z)
    {
        var m = new Mesh(x, y, z);
        float hx = HouseHalfX, hy = HouseHalfY, wall = 392; // 5.6 m: two floors
        m.Box(-hx, hx, -hy, hy, 0, wall, Color.FromArgb(196, 186, 166));
        m.Roof(-hx - 20, hx + 20, -hy - 20, hy + 20, wall, 182, Color.FromArgb(128, 64, 52));
        m.Box(-hx * 0.85f, -hx * 0.85f + 50, -hy * 0.4f, -hy * 0.4f + 50, wall + 40, wall + 170, Color.FromArgb(110, 100, 95)); // chimney
        var glass = Color.FromArgb(62, 82, 104);
        m.Box(-35, 35, -hy - 3, -hy, 0, 147, Color.FromArgb(96, 64, 44)); // door, 2.1 m
        foreach (var wx in new[] { -165f, 105f })
            m.Box(wx, wx + 60, -hy - 3, -hy, 63, 133, glass);              // ground floor windows
        foreach (var wx in new[] { -165f, -30f, 105f })
            m.Box(wx, wx + 60, -hy - 3, -hy, 255, 325, glass);             // first floor windows
        foreach (var wy in new[] { -110f, 50f })
        {
            m.Box(-hx - 3, -hx, wy, wy + 60, 63, 133, glass); m.Box(hx, hx + 3, wy, wy + 60, 63, 133, glass);
            m.Box(-hx - 3, -hx, wy, wy + 60, 255, 325, glass); m.Box(hx, hx + 3, wy, wy + 60, 255, 325, glass);
        }
        return m.Part("house");
    }

    /// <summary>A closed shed around min..max (walls, roof): the item can't be seen in it - only what shows through
    /// walls. At least 3 m high.</summary>
    public static PreviewPart ShedAround((float X, float Y, float Z) min, (float X, float Y, float Z) max)
    {
        float size = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        float gap = Math.Max(25, size * 0.15f), t = 8;
        float x0 = min.X - gap, x1 = max.X + gap, y0 = min.Y - gap, y1 = max.Y + gap;
        float top = Math.Max(min.Z + 210, max.Z + gap);
        var m = new Mesh(0, 0, min.Z);
        var wall = Color.FromArgb(150, 148, 140);
        float h = top - min.Z;
        // the front wall has an open doorway (90 cm x 2.1 m, smaller on a small shed): look in at the item, or
        // stand to the side to see it through the wall
        float dx = (x0 + x1) / 2, dw = Math.Min(63, (x1 - x0) * 0.6f) / 2, dh = Math.Min(147, h - 15);
        m.Box(x0 - t, dx - dw, y0 - t, y0, 0, h, wall);
        m.Box(dx + dw, x1 + t, y0 - t, y0, 0, h, wall);
        m.Box(dx - dw, dx + dw, y0 - t, y0, dh, h, wall);
        m.Box(x0 - t, x1 + t, y1, y1 + t, 0, h, wall);
        m.Box(x0 - t, x0, y0, y1, 0, h, wall);
        m.Box(x1, x1 + t, y0, y1, 0, h, wall);
        m.Roof(x0 - t - 15, x1 + t + 15, y0 - t - 15, y1 + t + 15, h, Math.Min(140, (y1 - y0) * 0.3f), Color.FromArgb(96, 98, 104));
        return m.Part("shed");
    }
}

/// <summary>The light in the big view. Studio: the plain dark backdrop of the small view.</summary>
enum Sky { Studio, Day, Dusk, Night, Fog }

/// <summary>Colour vision to check a look with: normal, or the picture as someone with colour blindness sees it.</summary>
enum Vision { Normal, Deuteranopia, Protanopia, Tritanopia }

/// <summary>What the big view adds to a picture: the light, colour vision, other items beside it (each in its own look)
/// and, when <see cref="Pick"/>, which item part is seen at each pixel.</summary>
sealed class RenderExtras
{
    public Sky Sky;
    public Vision Vision;
    public List<(List<PreviewPart> Parts, Style Look)> Others = new();
    public bool Pick;
    /// <summary>Per pixel of the last picture (its width × height): the solid or see-through part drawn there.</summary>
    public PreviewPart?[]? Hit;
}

static class PreviewRenderer
{
    public static Bitmap Render(PreviewScene scene, Style st, int w, int h, float yaw, float pitch, (float X, float Y, float Z) target, float dist,
                                SizeGuide? guide = null, RenderExtras? x = null)
    {
        var (eye, look) = Orbit(yaw, pitch, target, dist);
        return Render(scene, st, w, h, eye, look, 40, Math.Max(0.5f, dist * 0.005f), guide, x);
    }

    /// <summary>The turning camera's eye and look.</summary>
    public static ((float X, float Y, float Z) Eye, (float X, float Y, float Z) Look) Orbit(float yaw, float pitch, (float X, float Y, float Z) target, float dist)
    {
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw), cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        var eye = (X: target.X + dist * cp * cy, Y: target.Y + dist * cp * sy, Z: target.Z + dist * sp);
        return (eye, (target.X - eye.X, target.Y - eye.Y, target.Z - eye.Z));
    }

    /// <summary>Where a point lands in a w × h picture from that camera; null behind it.</summary>
    public static PointF? ToScreen((float X, float Y, float Z) eye, (float X, float Y, float Z) look, float fov, int w, int h, (float X, float Y, float Z) p)
    {
        var fwd = Norm(look);
        var right = Norm(Cross(fwd, (0, 0, 1)));
        var up = Cross(right, fwd);
        float focal = 0.5f * h / MathF.Tan(fov / 2 * MathF.PI / 180);
        var v = ToView(new Cam(eye, right, up, fwd, focal, w, h, 1, float.NaN, SkyLook.Of(Sky.Studio)), p);
        return v.Z < 1 ? null : new PointF(w / 2f + v.X * focal / v.Z, h / 2f - v.Y * focal / v.Z);
    }

    /// <summary>A sky: its colour at the top, at the horizon, the ground; the sunlight's colour (strength in it) and the fog
    /// (0 = none: how far in game units until only a third of the colour is left).</summary>
    sealed record SkyLook(Sky Sky, (float R, float G, float B) Top, (float R, float G, float B) Horizon, (float R, float G, float B) Floor,
                          (float R, float G, float B) Sun, float FogDist)
    {
        public static SkyLook Of(Sky s) => s switch
        {
            Sky.Day => new(s, (0.36f, 0.53f, 0.80f), (0.74f, 0.80f, 0.86f), (0.31f, 0.30f, 0.25f), (1.02f, 0.99f, 0.92f), 0),
            Sky.Dusk => new(s, (0.16f, 0.14f, 0.30f), (0.86f, 0.48f, 0.28f), (0.13f, 0.10f, 0.09f), (0.66f, 0.45f, 0.32f), 0),
            Sky.Night => new(s, (0.008f, 0.012f, 0.03f), (0.035f, 0.045f, 0.075f), (0.015f, 0.016f, 0.02f), (0.12f, 0.14f, 0.20f), 0),
            Sky.Fog => new(s, (0.56f, 0.58f, 0.59f), (0.63f, 0.64f, 0.64f), (0.42f, 0.43f, 0.42f), (0.70f, 0.71f, 0.73f), 1000),
            _ => new(s, default, default, default, (1, 1, 1), 0),
        };
        /// <summary>How much of a colour that far away is left (1 without fog).</summary>
        public float Clear(float z) => FogDist <= 0 ? 1 : MathF.Exp(-z / FogDist);
    }

    /// <summary>The look's colours and strengths, worked out once per picture.</summary>
    sealed record Paint(Color Glow, Color Beam, Color IconCol, Color ModelCol, float GlowK, float FillA, float OutlineA, float BeamA, float Shine, bool Fill, bool GlowX, bool BeamX, bool IconX, bool Arrow, bool Invisible = false, float Face = 0.18f)
    {
        public static Paint Of(Style st)
        {
            var iconCol = Materials.Parse(st.Beam ? st.BeamColor : st.GlowColor);
            float gs = Math.Clamp(st.GlowOpacity / 100f, 0.05f, 1f);
            return new(Materials.Parse(st.GlowColor), Materials.Parse(st.BeamColor), iconCol,
                       Models3D.Get(st.Icon) is { Tinted: false } ? Color.White : iconCol, // own colours, or the look's
                       gs * 0.36f,
                       Math.Min(1f, 0.28f * gs / 0.3f), Materials.OutlineAlpha(gs), // fill = 0.28 at 30 %; an outline is stronger at the edges
                       Math.Min(1f, 0.75f * st.BeamStrength / 100f) * (st.BeamStrength > 100 ? st.BeamStrength / 100f : 1f),
                       st.Brighten ? Math.Clamp(st.BrightenStrength, 25, 300) / 100f : 0, // the colour texture as a glow map
                       st.GlowFill, st.GlowFx.Walls, st.BeamFx.Walls, st.IconFx.Walls, st.BeamArrow, st.Invisible, Materials.OutlineFace(gs));
        }
    }

    /// <summary>Colour blindness (Machado et al. 2009, full strength), on linear colour.</summary>
    static readonly Dictionary<Vision, float[]> VisionMatrix = new()
    {
        [Vision.Protanopia] = new[] { 0.152286f, 1.052583f, -0.204868f, 0.114503f, 0.786281f, 0.099216f, -0.003882f, -0.048116f, 1.051998f },
        [Vision.Deuteranopia] = new[] { 0.367322f, 0.860646f, -0.227968f, 0.280085f, 0.672501f, 0.047413f, -0.011820f, 0.042940f, 0.968881f },
        [Vision.Tritanopia] = new[] { 1.255528f, -0.076749f, -0.178779f, -0.078411f, 0.930809f, 0.147602f, 0.004733f, 0.691367f, 0.303900f },
    };

    /// <summary>A camera standing at <paramref name="eye"/>, looking along <paramref name="look"/> (first person).</summary>
    /// <param name="fov">The view's height in degrees.</param>
    /// <param name="time">Seconds on the look's animation clock (pulse, rainbow, spin, bob, flowing beam, hide / blink);
    /// below 0 = a still picture.</param>
    public static Bitmap Render(PreviewScene scene, Style st, int w, int h, (float X, float Y, float Z) eye, (float X, float Y, float Z) look, float fov,
                                float near, SizeGuide? guide = null, RenderExtras? x = null, float time = -1)
    {
        w = Math.Max(16, w); h = Math.Max(16, h);
        var color = new float[w * h * 3];
        var depth = new float[w * h];
        Array.Fill(depth, float.MaxValue);
        var fwd = Norm(look);
        var right = Norm(Cross(fwd, (0, 0, 1)));
        var up = Cross(right, fwd);
        float focal = 0.5f * h / MathF.Tan(fov / 2 * MathF.PI / 180);
        var sky = SkyLook.Of(x?.Sky ?? Sky.Studio);
        for (int y = 0; y < h; y++)
        {
            (float R, float G, float B) c;
            if (sky.Sky == Sky.Studio)
            {
                float k = 0.10f + 0.10f * y / h; // dark, a little lighter towards the bottom
                c = (k * 0.9f, k * 0.95f, k * 1.05f);
            }
            else
            {
                // the ray through this row (no roll: the same for the whole row) - above or below the horizon
                float dy = h / 2f - y - 0.5f, rz = fwd.Z * focal + up.Z * dy;
                float el = rz / MathF.Sqrt(focal * focal + dy * dy);
                if (el >= 0) { float t = MathF.Min(1, MathF.Sqrt(el * 1.6f)); c = Mix(sky.Horizon, sky.Top, t); }
                else { float t = MathF.Min(1, -el * 6); c = Mix(Mix(sky.Horizon, sky.Floor, 0.75f), sky.Floor, t); } // haze where the ground meets the sky
            }
            for (int col = 0; col < w; col++) { int i = (y * w + col) * 3; color[i] = c.R; color[i + 1] = c.G; color[i + 2] = c.B; }
        }
        var light = Norm((-fwd.X * 0.5f + up.X * 0.7f - right.X * 0.5f, -fwd.Y * 0.5f + up.Y * 0.7f - right.Y * 0.5f, -fwd.Z * 0.5f + up.Z * 0.7f - right.Z * 0.5f));
        var cam = new Cam(eye, right, up, fwd, focal, w, h, near, scene.Ground, sky);
        var hit = x is { Pick: true } ? new PreviewPart?[w * h] : null;
        if (x != null) x.Hit = hit;

        if (guide is { Grid: true }) DrawGrid(guide, cam, color);
        var plain = Paint.Of(new Style()) with { Shine = 0 };
        var looks = new List<(IEnumerable<PreviewPart> Parts, Paint Paint, Style Look)> { (scene.Parts, Paint.Of(st), st) };
        if (x != null) looks.AddRange(x.Others.Select(o => ((IEnumerable<PreviewPart>)o.Parts, Paint.Of(o.Look), o.Look)));
        // drawn in bands of rows on all cores (each band draws every part, clipped to its rows; user, 2026-11-09: the
        // view in the big pop-up lagged on one core)
        int bands = w * h >= 40_000 ? Math.Clamp(Math.Min(Environment.ProcessorCount * 2, h / 16), 1, 32) : 1;
        if (bands == 1) DrawBand(cam);
        else Parallel.For(0, bands, i => DrawBand(cam with { Y0 = h * i / bands, Y1 = h * (i + 1) / bands - 1 }));
        void DrawBand(Cam cam)
        {
        if (guide != null)
            foreach (var prop in guide.Props) Draw(prop, cam, color, depth, hit, light, Color.White, 1f, false, plain);
        // the items first (fill the depth buffer), then everything that adds or blends light
        foreach (var kind in new[] { PartKind.Solid, PartKind.Effect, PartKind.Blend, PartKind.Glow, PartKind.Icon3D, PartKind.Model, PartKind.Beam, PartKind.Icon })
            foreach (var (parts, pt, lk) in looks)
                foreach (var p0 in parts.Where(p => p.Kind == kind))
                {
                    var p = p0;
                    float bright = 1, flowT = -1;
                    Color? rainbow = null;
                    if (time >= 0 && SectionOf(lk, p0) is { } fx)
                    {
                        float shown = Shown(fx, time);
                        if (shown <= 0.001f) continue;
                        if (fx.Pulse) bright = 0.625f + 0.375f * MathF.Cos(time * MathF.PI); // a breath every 2 s, as built
                        if (fx.Rainbow) rainbow = RainbowAt(time);
                        if (fx.Flow && p0.Kind == PartKind.Beam) flowT = time;
                        bool spinOk = fx.Spin && (p0.Kind == PartKind.Model || p0.Kind == PartKind.Icon3D && !IsArrow(p0));
                        bool bobOk = fx.Bob && p0.Kind is PartKind.Icon or PartKind.Icon3D or PartKind.Model;
                        p = Moved(p0, spinOk ? time / 4 * 2 * MathF.PI : 0, bobOk ? MathF.Sin(time * MathF.PI) : 0, shown);
                    }
                    // an invisible look: the item's own parts aren't drawn (the build hides them; the scene keeps them for size and picking)
                    if (pt.Invisible && !(scene.Bare && ReferenceEquals(parts, scene.Parts)) && kind is PartKind.Solid or PartKind.Effect or PartKind.Blend) continue;
                    bool xray = kind switch { PartKind.Glow => pt.GlowX, PartKind.Beam => pt.BeamX, PartKind.Icon3D => pt.Arrow ? pt.BeamX : pt.IconX, PartKind.Icon or PartKind.Model => pt.IconX, _ => false };
                    var col = kind switch { PartKind.Beam => pt.Beam, PartKind.Glow => pt.Glow, PartKind.Model => pt.ModelCol, _ => pt.IconCol };
                    if (rainbow is Color rc) col = rc;
                    bool fill = pt.Fill || p.Filled;
                    Draw(p, cam, color, depth, hit, light, col, kind switch { PartKind.Glow => pt.GlowK, PartKind.Beam => pt.BeamA, _ => 1f }, xray, pt,
                         fill ? pt.FillA : pt.OutlineA, !fill, bright, flowT);
                }
        }

        if (x != null && VisionMatrix.TryGetValue(x.Vision, out var m))
            for (int i = 0; i < w * h * 3; i += 3)
            {
                float r = Math.Clamp(color[i], 0, 1), g = Math.Clamp(color[i + 1], 0, 1), b = Math.Clamp(color[i + 2], 0, 1);
                color[i] = m[0] * r + m[1] * g + m[2] * b;
                color[i + 1] = m[3] * r + m[4] * g + m[5] * b;
                color[i + 2] = m[6] * r + m[7] * g + m[8] * b;
            }

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4 + 2] = ToByte(color[i * 3]); px[i * 4 + 1] = ToByte(color[i * 3 + 1]); px[i * 4] = ToByte(color[i * 3 + 2]); px[i * 4 + 3] = 255;
        }
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, bd.Scan0 + y * bd.Stride, w * 4);
        bmp.UnlockBits(bd);
        return bmp;
    }

    /// <summary>Floor lines one square long each, fading out away from the item (the background, under everything).</summary>
    static void DrawGrid(SizeGuide g, Cam c, float[] color)
    {
        int n = (int)(g.Radius / g.Step);
        float r = n * g.Step;
        for (int i = -n; i <= n; i++)
        {
            float o = i * g.Step;
            for (int j = -n; j < n; j++)
            {
                float a0 = j * g.Step, a1 = a0 + g.Step, mid = (a0 + a1) / 2;
                float fade = 1 - MathF.Sqrt(o * o + mid * mid) / r;
                if (fade <= 0) continue;
                float k = 0.35f * fade;
                Line(c, color, (g.CX + a0, g.CY + o, g.FloorZ), (g.CX + a1, g.CY + o, g.FloorZ), k);
                Line(c, color, (g.CX + o, g.CY + a0, g.FloorZ), (g.CX + o, g.CY + a1, g.FloorZ), k);
            }
        }
    }

    static (float X, float Y, float Z) ToView(Cam c, (float X, float Y, float Z) p)
    {
        float dx = p.X - c.Eye.X, dy = p.Y - c.Eye.Y, dz = p.Z - c.Eye.Z;
        return (dx * c.Right.X + dy * c.Right.Y + dz * c.Right.Z, dx * c.Up.X + dy * c.Up.Y + dz * c.Up.Z, dx * c.Fwd.X + dy * c.Fwd.Y + dz * c.Fwd.Z);
    }

    static void Line(Cam c, float[] color, (float X, float Y, float Z) from, (float X, float Y, float Z) to, float k)
    {
        var a = ToView(c, from);
        var b = ToView(c, to);
        if (a.Z < c.Near && b.Z < c.Near) return;
        if (a.Z < c.Near) { float t = (c.Near - a.Z) / (b.Z - a.Z); a = (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, c.Near); }
        else if (b.Z < c.Near) { float t = (c.Near - b.Z) / (a.Z - b.Z); b = (b.X + (a.X - b.X) * t, b.Y + (a.Y - b.Y) * t, c.Near); }
        float sx0 = c.W / 2f + a.X * c.Focal / a.Z, sy0 = c.H / 2f - a.Y * c.Focal / a.Z;
        float sx1 = c.W / 2f + b.X * c.Focal / b.Z, sy1 = c.H / 2f - b.Y * c.Focal / b.Z;
        float dx = sx1 - sx0, dy = sy1 - sy0;
        int steps = (int)MathF.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
        if (steps == 0 || steps > 4 * (c.W + c.H)) return; // (a square right at the camera)
        // the lines in the light of the sky (dark at night), fading into fog
        var (lr, lg, lb) = c.Sky.Sky == Sky.Studio ? (0.62f, 0.68f, 0.64f) : (0.55f * c.Sky.Sun.R, 0.58f * c.Sky.Sun.G, 0.52f * c.Sky.Sun.B);
        for (int s = 0; s < steps; s++) // the end point is the next square's start
        {
            int x = (int)(sx0 + dx * s / steps), y = (int)(sy0 + dy * s / steps);
            if (x < 0 || y < 0 || x >= c.W || y >= c.H) continue;
            int ci = (y * c.W + x) * 3;
            float kk = k * c.Sky.Clear(a.Z + (b.Z - a.Z) * s / steps);
            color[ci] += (lr - color[ci]) * kk; color[ci + 1] += (lg - color[ci + 1]) * kk; color[ci + 2] += (lb - color[ci + 2]) * kk;
        }
    }

    /// <summary>Bilinear, wrapping.</summary>
    static (float, float, float, float) Sample(Rgba t, float u, float v)
    {
        float fx = (u - MathF.Floor(u)) * t.Width - 0.5f, fy = (v - MathF.Floor(v)) * t.Height - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        int X(int x) => ((x % t.Width) + t.Width) % t.Width;
        int Y(int y) => ((y % t.Height) + t.Height) % t.Height;
        int i00 = (Y(y0) * t.Width + X(x0)) * 4, i10 = (Y(y0) * t.Width + X(x0 + 1)) * 4, i01 = (Y(y0 + 1) * t.Width + X(x0)) * 4, i11 = (Y(y0 + 1) * t.Width + X(x0 + 1)) * 4;
        var px = t.Pixels;
        float C(int k) => ((px[i00 + k] * (1 - ax) + px[i10 + k] * ax) * (1 - ay) + (px[i01 + k] * (1 - ax) + px[i11 + k] * ax) * ay) / 255f;
        return (C(0), C(1), C(2), C(3));
    }

    static byte ToByte(float v)
    {
        v = v <= 0 ? 0 : v < 1 ? MathF.Pow(v, 1 / 1.15f) : 1; // a touch brighter mid-tones
        return (byte)(v * 255 + 0.5f);
    }

    readonly record struct Cam((float X, float Y, float Z) Eye, (float X, float Y, float Z) Right, (float X, float Y, float Z) Up, (float X, float Y, float Z) Fwd, float Focal, int W, int H, float Near, float Ground, SkyLook Sky,
                                     int Y0 = 0, int Y1 = int.MaxValue); // Y0..Y1: the band of rows this one draws (drawn in parallel)

    static (float R, float G, float B) Mix((float R, float G, float B) a, (float R, float G, float B) b, float t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    static (float X, float Y, float Z) Norm((float X, float Y, float Z) a)
    {
        float l = MathF.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
        return l > 1e-9f ? (a.X / l, a.Y / l, a.Z / l) : (0, 0, 1);
    }

    static bool IsArrow(PreviewPart p) => p.Name.StartsWith("ItemFinderBeam", StringComparison.Ordinal);

    /// <summary>The look section a drawn part belongs to (null: the item's own parts).</summary>
    static PartFx? SectionOf(Style st, PreviewPart p) => p.Kind switch
    {
        PartKind.Glow => st.GlowFx,
        PartKind.Beam => st.BeamFx,
        PartKind.Icon3D => IsArrow(p) ? st.BeamFx : st.IconFx,
        PartKind.Model => st.IconFx,
        PartKind.Icon => p.Name.StartsWith("ESPMakerLabel", StringComparison.Ordinal) ? st.LabelFx : st.IconFx,
        _ => null,
    };

    /// <summary>How much of a section shows at time t (1 = all, 0 = gone, between = shrinking / growing), as the hide timer builds it.</summary>
    static float Shown(PartFx fx, float t)
    {
        if (fx.HideAfter <= 0) return 1;
        float T = fx.HideAfter;
        if (fx.BlinkOff > 0)
        {
            float per = MathF.Round(T + Math.Max(1, fx.BlinkOff)), q = Math.Min(0.5f, T / 2);
            t %= per;
            return t < T - q ? 1 : t < T ? 1 - (t - (T - q)) / q : t < per - q ? 0 : (t - (per - q)) / q;
        }
        float fade = Math.Min(4, T);
        return t < T - fade ? 1 : t < T ? 1 - (t - (T - fade)) / fade : 0;
    }

    /// <summary>Red, yellow, green, cyan, blue, magenta and back, 1 s each - the built colour cycle.</summary>
    static Color RainbowAt(float t)
    {
        (float R, float G, float B)[] k = { (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1), (1, 0, 1), (1, 0, 0) };
        t %= 6; int i = (int)t; float f = t - i;
        var (a, b) = (k[i], k[Math.Min(6, i + 1)]);
        return Color.FromArgb((int)(255 * (a.R + (b.R - a.R) * f)), (int)(255 * (a.G + (b.G - a.G) * f)), (int)(255 * (a.B + (b.B - a.B) * f)));
    }

    /// <summary>A part turned round its own centre line, bobbed up / down (share of 20 % of its height) and shrunk to
    /// <paramref name="scale"/> - a copy; the part itself when nothing changes.</summary>
    static PreviewPart Moved(PreviewPart p, float angle, float bob, float scale)
    {
        if (angle == 0 && bob == 0 && scale >= 0.999f) return p;
        int n = p.P.Length / 3;
        if (n == 0) return p;
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int v = 0; v < n; v++)
        {
            minX = Math.Min(minX, p.P[v * 3]); maxX = Math.Max(maxX, p.P[v * 3]);
            minY = Math.Min(minY, p.P[v * 3 + 1]); maxY = Math.Max(maxY, p.P[v * 3 + 1]);
            minZ = Math.Min(minZ, p.P[v * 3 + 2]); maxZ = Math.Max(maxZ, p.P[v * 3 + 2]);
        }
        // a camera-facing card: its points are offsets in the card (x right, y up) around its anchor
        float cx = p.Billboard ? 0 : (minX + maxX) / 2, cy = p.Billboard ? 0 : (minY + maxY) / 2, cz = p.Billboard ? 0 : (minZ + maxZ) / 2;
        float h = p.Billboard ? maxY - minY : maxZ - minZ;
        float lift = bob * Math.Max(4f, h * 0.2f);
        float co = MathF.Cos(angle), si = MathF.Sin(angle);
        var pos = new float[p.P.Length];
        for (int v = 0; v < n; v++)
        {
            float x = (p.P[v * 3] - cx) * scale, y = (p.P[v * 3 + 1] - cy) * scale, z = (p.P[v * 3 + 2] - cz) * scale;
            if (!p.Billboard) (x, y) = (x * co - y * si, x * si + y * co);
            pos[v * 3] = cx + x; pos[v * 3 + 1] = cy + y; pos[v * 3 + 2] = cz + z + (p.Billboard ? 0 : lift);
        }
        float[]? nor = p.N;
        if (p.N != null && angle != 0 && !p.Billboard)
        {
            nor = new float[p.N.Length];
            for (int v = 0; v + 2 < p.N.Length; v += 3)
            {
                float x = p.N[v], y = p.N[v + 1];
                nor[v] = x * co - y * si; nor[v + 1] = x * si + y * co; nor[v + 2] = p.N[v + 2];
            }
        }
        return new PreviewPart
        {
            Kind = p.Kind, Name = p.Name, P = pos, N = nor, Uv = p.Uv, Col = p.Col, Tri = p.Tri, Tex = p.Tex, TestRef = p.TestRef,
            Billboard = p.Billboard, Anchor = p.Billboard ? (p.Anchor.X, p.Anchor.Y, p.Anchor.Z + lift) : p.Anchor, Skinned = p.Skinned, Filled = p.Filled,
        };
    }

    /// <param name="bright">Pulse / fade: the added light's share (not the item's own parts).</param>
    /// <param name="flowT">Flowing beam: the animation time (bands of light run up, one per 2 m in 1.5 s); below 0 = none.</param>
    static void Draw(PreviewPart p, Cam c, float[] color, float[] depth, PreviewPart?[]? hit, (float X, float Y, float Z) light, Color col, float strength, bool xray,
                     Paint pt, float glowA = 0, bool outline = false, float bright = 1, float flowT = -1)
    {
        float shine = pt.Shine;
        var sun = c.Sky.Sun;
        var fogC = c.Sky.Horizon;
        int n = p.P.Length / 3;
        var vxs = new float[n]; var vys = new float[n]; var vzs = new float[n];
        var wx = new float[n]; var wy = new float[n]; var wz = new float[n];
        for (int v = 0; v < n; v++)
        {
            float x = p.P[v * 3], y = p.P[v * 3 + 1], z = p.P[v * 3 + 2];
            if (p.Billboard) // the quad turns to face the camera
                (x, y, z) = (p.Anchor.X + c.Right.X * x + c.Up.X * y, p.Anchor.Y + c.Right.Y * x + c.Up.Y * y, p.Anchor.Z + c.Right.Z * x + c.Up.Z * y);
            wx[v] = x; wy[v] = y; wz[v] = z;
            float dx = x - c.Eye.X, dy = y - c.Eye.Y, dz = z - c.Eye.Z;
            float vx = dx * c.Right.X + dy * c.Right.Y + dz * c.Right.Z;
            float vy = dx * c.Up.X + dy * c.Up.Y + dz * c.Up.Z;
            float vz = dx * c.Fwd.X + dy * c.Fwd.Y + dz * c.Fwd.Z;
            vxs[v] = vx; vys[v] = vy; vzs[v] = vz;
        }
        // a triangle cut at the near plane: up to 4 corners, each with its weights of the triangle's own corners
        var qx = new float[4]; var qy = new float[4]; var qz = new float[4]; var qa = new float[4]; var qb = new float[4]; var qd = new float[4];
        var ox = new float[3]; var oy = new float[3]; var oz = new float[3]; var oa = new float[3]; var ob = new float[3]; var od = new float[3];
        float cr = col.R / 255f, cg = col.G / 255f, cb = col.B / 255f;
        var tex = p.Tex;
        for (int t = 0; t + 2 < p.Tri.Length; t += 3)
        {
            int a = p.Tri[t], b = p.Tri[t + 1], d = p.Tri[t + 2];
            if (vzs[a] < c.Near && vzs[b] < c.Near && vzs[d] < c.Near) continue; // behind the camera
            // the face normal (lighting when the model has none, and how much a beam faces you)
            var fn = Norm(Cross((wx[b] - wx[a], wy[b] - wy[a], wz[b] - wz[a]), (wx[d] - wx[a], wy[d] - wy[a], wz[d] - wz[a])));
            // a 3D model is one-sided like its material: its back faces aren't drawn
            if (p.Kind == PartKind.Model && fn.X * (c.Eye.X - wx[a]) + fn.Y * (c.Eye.Y - wy[a]) + fn.Z * (c.Eye.Z - wz[a]) < 0) continue;
            ox[0] = vxs[a]; oy[0] = vys[a]; oz[0] = vzs[a]; oa[0] = 1; ob[0] = 0; od[0] = 0;
            ox[1] = vxs[b]; oy[1] = vys[b]; oz[1] = vzs[b]; oa[1] = 0; ob[1] = 1; od[1] = 0;
            ox[2] = vxs[d]; oy[2] = vys[d]; oz[2] = vzs[d]; oa[2] = 0; ob[2] = 0; od[2] = 1;
            int m = 0;
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                bool inI = oz[i] >= c.Near, inJ = oz[j] >= c.Near;
                if (inI) { qx[m] = ox[i]; qy[m] = oy[i]; qz[m] = oz[i]; qa[m] = oa[i]; qb[m] = ob[i]; qd[m] = od[i]; m++; }
                if (inI != inJ)
                {
                    float k = (c.Near - oz[i]) / (oz[j] - oz[i]);
                    qx[m] = ox[i] + (ox[j] - ox[i]) * k; qy[m] = oy[i] + (oy[j] - oy[i]) * k; qz[m] = c.Near;
                    qa[m] = oa[i] + (oa[j] - oa[i]) * k; qb[m] = ob[i] + (ob[j] - ob[i]) * k; qd[m] = od[i] + (od[j] - od[i]) * k; m++;
                }
            }
            for (int f = 1; f + 1 < m; f++)
            {
            int i0 = 0, i1 = f, i2 = f + 1;
            float iz0 = 1 / qz[i0], iz1 = 1 / qz[i1], iz2 = 1 / qz[i2];
            float sx0 = c.W / 2f + qx[i0] * c.Focal * iz0, sy0 = c.H / 2f - qy[i0] * c.Focal * iz0;
            float sx1 = c.W / 2f + qx[i1] * c.Focal * iz1, sy1 = c.H / 2f - qy[i1] * c.Focal * iz1;
            float sx2 = c.W / 2f + qx[i2] * c.Focal * iz2, sy2 = c.H / 2f - qy[i2] * c.Focal * iz2;
            float area = (sx1 - sx0) * (sy2 - sy0) - (sx2 - sx0) * (sy1 - sy0);
            if (MathF.Abs(area) < 1e-6f) continue;
            int x0 = Math.Max(0, (int)MathF.Floor(Math.Min(sx0, Math.Min(sx1, sx2)))), x1 = Math.Min(c.W - 1, (int)MathF.Ceiling(Math.Max(sx0, Math.Max(sx1, sx2))));
            int y0 = Math.Max(c.Y0, (int)MathF.Floor(Math.Min(sy0, Math.Min(sy1, sy2)))), y1 = Math.Min(Math.Min(c.H - 1, c.Y1), (int)MathF.Ceiling(Math.Max(sy0, Math.Max(sy1, sy2))));
            if (x0 > x1 || y0 > y1) continue;
            float inv = 1 / area;
            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((sx1 - px) * (sy2 - py) - (sx2 - px) * (sy1 - py)) * inv;
                    float w1 = ((sx2 - px) * (sy0 - py) - (sx0 - px) * (sy2 - py)) * inv;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float izp = w0 * iz0 + w1 * iz1 + w2 * iz2;
                    float z = 1 / izp;
                    int pi = y * c.W + x;
                    bool solid = p.Kind == PartKind.Solid;
                    if (!xray && z > depth[pi] * (solid ? 1f : 1.002f)) continue;
                    float e0 = w0 * iz0 / izp, e1 = w1 * iz1 / izp, e2 = w2 * iz2 / izp;
                    float pa = e0 * qa[i0] + e1 * qa[i1] + e2 * qa[i2];
                    float pb = e0 * qb[i0] + e1 * qb[i1] + e2 * qb[i2];
                    float pd = e0 * qd[i0] + e1 * qd[i1] + e2 * qd[i2];
                    if (!xray && pa * wz[a] + pb * wz[b] + pd * wz[d] < c.Ground) continue; // buried (false when no ground: NaN)

                    float tr = 1, tg = 1, tb = 1, ta = 1;
                    if (tex != null && p.Uv != null)
                    {
                        float u = pa * p.Uv[a * 2] + pb * p.Uv[b * 2] + pd * p.Uv[d * 2];
                        float v = pa * p.Uv[a * 2 + 1] + pb * p.Uv[b * 2 + 1] + pd * p.Uv[d * 2 + 1];
                        (tr, tg, tb, ta) = Sample(tex, u, v);
                    }
                    if (p.TestRef >= 0 && ta * 255 <= p.TestRef) continue;
                    float vr = 1, vg = 1, vb = 1;
                    if (p.Col != null)
                    {
                        vr = (pa * p.Col[a * 4] + pb * p.Col[b * 4] + pd * p.Col[d * 4]) / 255f;
                        vg = (pa * p.Col[a * 4 + 1] + pb * p.Col[b * 4 + 1] + pd * p.Col[d * 4 + 1]) / 255f;
                        vb = (pa * p.Col[a * 4 + 2] + pb * p.Col[b * 4 + 2] + pd * p.Col[d * 4 + 2]) / 255f;
                    }
                    int ci = pi * 3;
                    float clear = c.Sky.Clear(z); // fog: 1 = none
                    if (p.Kind >= PartKind.Glow) clear *= bright;
                    switch (p.Kind)
                    {
                        case PartKind.Solid:
                        case PartKind.Blend:
                        {
                            float nx = fn.X, ny = fn.Y, nz = fn.Z;
                            if (p.N != null)
                            {
                                nx = pa * p.N[a * 3] + pb * p.N[b * 3] + pd * p.N[d * 3];
                                ny = pa * p.N[a * 3 + 1] + pb * p.N[b * 3 + 1] + pd * p.N[d * 3 + 1];
                                nz = pa * p.N[a * 3 + 2] + pb * p.N[b * 3 + 2] + pd * p.N[d * 3 + 2];
                                float l = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                                if (l > 1e-6f) { nx /= l; ny /= l; nz /= l; }
                            }
                            float lit = 0.32f + 0.78f * MathF.Abs(nx * light.X + ny * light.Y + nz * light.Z);
                            // the sky's light; Shine: its own colours as added light (the same day or night)
                            float r = tr * vr * (lit * sun.R + shine), g = tg * vg * (lit * sun.G + shine), bl = tb * vb * (lit * sun.B + shine);
                            if (clear < 1) { r = fogC.R + (r - fogC.R) * clear; g = fogC.G + (g - fogC.G) * clear; bl = fogC.B + (bl - fogC.B) * clear; }
                            if (p.Kind == PartKind.Solid) { color[ci] = r; color[ci + 1] = g; color[ci + 2] = bl; depth[pi] = z; if (hit != null) hit[pi] = p; }
                            else
                            {
                                color[ci] += (r - color[ci]) * ta; color[ci + 1] += (g - color[ci + 1]) * ta; color[ci + 2] += (bl - color[ci + 2]) * ta;
                                if (hit != null && ta > 0.3f) hit[pi] = p;
                            }
                            break;
                        }
                        case PartKind.Effect:
                        {
                            float k = 0.7f * ta * clear;
                            color[ci] += tr * vr * k; color[ci + 1] += tg * vg * k; color[ci + 2] += tb * vb * k;
                            break;
                        }
                        case PartKind.Glow:
                            if (p.TestRef >= 0) { float k = strength * clear; color[ci] += cr * k; color[ci + 1] += cg * k; color[ci + 2] += cb * k; } // cut-out: added light
                            else
                            {
                                float ga = glowA * clear;
                                if (outline) // clear where the surface faces you, full colour towards the edges
                                {
                                    var vd = Norm((c.Eye.X - wx[a], c.Eye.Y - wy[a], c.Eye.Z - wz[a]));
                                    float fa = MathF.Abs(fn.X * vd.X + fn.Y * vd.Y + fn.Z * vd.Z);
                                    float face = pt.Face;
                                    ga *= fa >= 0.6f ? face : fa <= 0.1f ? 1f : 1f - (1f - face) * (fa - 0.1f) / 0.5f;
                                }
                                color[ci] += (cr - color[ci]) * ga; color[ci + 1] += (cg - color[ci + 1]) * ga; color[ci + 2] += (cb - color[ci + 2]) * ga;
                            }
                            break;
                        case PartKind.Beam:
                        {
                            // brightest where the tube faces you, soft at its edges
                            var vd = Norm((c.Eye.X - wx[a], c.Eye.Y - wy[a], c.Eye.Z - wz[a]));
                            float facing = MathF.Abs(fn.X * vd.X + fn.Y * vd.Y + fn.Z * vd.Z);
                            float k = strength * (0.15f + 0.6f * facing * facing) * clear;
                            if (flowT >= 0)
                            {
                                float pz = pa * wz[a] + pb * wz[b] + pd * wz[d];
                                float band = 0.5f + 0.5f * MathF.Cos(2 * MathF.PI * (pz / 140f - flowT / 1.5f));
                                k *= 0.3f + 1.2f * band * band * band;
                            }
                            color[ci] += cr * k; color[ci + 1] += cg * k; color[ci + 2] += cb * k;
                            break;
                        }
                        case PartKind.Icon:
                        {
                            float k = ta * 1.15f * clear;
                            color[ci] += (cr * 1.1f - color[ci]) * Math.Min(1, k); color[ci + 1] += (cg * 1.1f - color[ci + 1]) * Math.Min(1, k); color[ci + 2] += (cb * 1.1f - color[ci + 2]) * Math.Min(1, k);
                            break;
                        }
                        case PartKind.Icon3D:
                        {
                            float k = 0.9f * clear;
                            color[ci] += cr * vr * k; color[ci + 1] += cg * vg * k; color[ci + 2] += cb * vb * k;
                            break;
                        }
                        case PartKind.Model:
                        {
                            // the material: texture x vertex colour x colour x 1.3, blended at 0.75
                            float k = 0.75f * ta * clear;
                            color[ci] += (Math.Min(1, tr * vr * cr * 1.3f) - color[ci]) * k;
                            color[ci + 1] += (Math.Min(1, tg * vg * cg * 1.3f) - color[ci + 1]) * k;
                            color[ci + 2] += (Math.Min(1, tb * vb * cb * 1.3f) - color[ci + 2]) * k;
                            break;
                        }
                    }
                }
            }
            }
        }
    }
}

/// <summary>The item as the mod shows it: drag to turn, wheel to zoom, double-click to reset. "Whole beam" frames the
/// beam and icon too. An entry with several models gets a parts list: click a part to see it, untick it to leave it
/// out of the mod (a part another ticked entry also uses still lights up through that entry).</summary>
sealed class ModelView : UserControl
{
    readonly Panel partsBox = new() { Dock = DockStyle.Left, Width = 330, Padding = new Padding(0, 0, 2, 0) };
    // drag to make the parts list wider (long names) or the picture bigger
    readonly Splitter partsSplit = new() { Dock = DockStyle.Left, Width = 6, MinSize = 200, MinExtra = 200 };
    readonly ListView parts = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false,
                                      HeaderStyle = ColumnHeaderStyle.None, MultiSelect = false, ShowItemToolTips = true };
    readonly Label partsNote = new() { Dock = DockStyle.Bottom, Height = 64, ForeColor = SystemColors.GrayText };
    Func<string, List<string>> sharedWith = _ => new();
    bool filling;
    readonly Panel canvas = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(26, 28, 32) };
    /// <summary>A toggle button: on = pressed-in (soft accent), the same in both themes.</summary>
    static CheckBox Toggle(string text, Padding margin)
    {
        // sized by hand: AutoSize cuts a flat toggle's text short
        var b = new CheckBox { Text = text, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, Margin = margin,
                               TextAlign = ContentAlignment.MiddleCenter };
        void Colour() => b.FlatAppearance.CheckedBackColor = Ui.AccentSoft;
        void Fit() => b.Size = new Size(TextRenderer.MeasureText(text, b.Font).Width + 24, TextRenderer.MeasureText(text, b.Font).Height + 12);
        Colour();
        Fit();
        b.FontChanged += (_, _) => Fit();
        b.BackColorChanged += (_, _) => Colour(); // the theme changed
        return b;
    }
    /// <summary>Camera menu: frame the tall beam too (only with a tall beam).</summary>
    readonly ToolStripMenuItem whole = new("Fit the whole beam in the picture") { CheckOnClick = true };
    /// <summary>Preview only, a two-button switch: "Apply look" or the plain game model ("Original"), to compare (the mod doesn't change).</summary>
    readonly CheckBox lookOn = Toggle("Apply look", new Padding(3, 3, 0, 3));
    readonly CheckBox lookOff = Toggle("Original", new Padding(0, 3, 12, 3));
    /// <summary>The switch shown for <see cref="lookOn"/> at the top of the picture (user, 2026-11-09: the two buttons looked like Save).</summary>
    readonly ItemFinder.Toggle lookSwitch = new() { Checked = true, Margin = new Padding(6, 8, 2, 3) };
    readonly Label lookSwitchText = new() { Text = "Show the look", AutoSize = true, Margin = new Padding(0, 7, 14, 3), Cursor = Cursors.Hand };
    /// <summary>The big window: walking, the keys box, the toolbar and the size line. The small view only turns and zooms.</summary>
    bool isBig;
    /// <summary>Things to put in the picture: a person, a house (sizes) and a shed around the item (through walls) -
    /// the toolbar's "Scene" menu.</summary>
    readonly ContextMenuStrip addMenu = new();
    /// <summary>The big view's toolbar menus (Camera, Light, Colours) and the buttons that open them and show their setting.</summary>
    readonly ContextMenuStrip cameraMenu = new(), lightMenu = new(), visionMenu = new();
    Button? sceneButton, lightButton, visionButton;
    static readonly string[] SkyNames = { "Studio", "Day", "Dusk", "Night", "Fog" };
    static readonly string[] VisionNames = { "Normal", "Red-green (deuteranopia)", "Red-green (protanopia)", "Blue-yellow (tritanopia)" };
    readonly ToolStripMenuItem addPerson = new("Person and floor grid (1.8 m)") { CheckOnClick = true };
    readonly ToolStripMenuItem addHouse = new("A house around it - the item on its floor") { CheckOnClick = true };
    readonly ToolStripMenuItem addShed = new("Shed around it, with a door and windows - test X-ray") { CheckOnClick = true };
    /// <summary>One wall behind the item (as the view starts): turn round to see the look through it - on by default in the look pop-up.</summary>
    readonly ToolStripMenuItem addWall = new("A wall behind it - turn the view to test X-ray") { CheckOnClick = true };
    readonly ToolStripMenuItem addJunk = new("Scatter junk around it (rubble, grass, a few game items)") { CheckOnClick = true };
    readonly ToolStripMenuItem hideLabels = new("Hide the name labels") { CheckOnClick = true };
    bool AnyProps => addPerson.Checked || addHouse.Checked || addShed.Checked || addWall.Checked || addJunk.Checked || compares.Count > 0;

    /// <summary>A selected item with its look, for "Compare with other items".</summary>
    public sealed record CompareItem(Entry Entry, string Category, Style Look, IReadOnlySet<string> Off);
    /// <summary>Set by the main window: the selected items with their looks.</summary>
    public static Func<List<CompareItem>>? TickedItems;
    /// <summary>Set by the main window: plain game items for "Scatter junk".</summary>
    public static Func<IEnumerable<Entry>>? JunkItems;
    const int MaxCompare = 12;
    /// <summary>Other items standing in a row beside this one, each in its own look (they stay while you click through the list).</summary>
    readonly List<(CompareItem Item, PreviewScene Scene)> compares = new();
    /// <summary>Game items lying around for "Scatter junk" (loaded once).</summary>
    List<PreviewScene>? junkScenes;
    bool junkLoading;
    /// <summary>The game's own models for the Add menu: a shop mannequin, a pre-war house, CAMP barn walls (plain, with a
    /// door, with a big and a small window) and a flat barn roof. Loaded once for the app; one that can't be read is
    /// drawn as the plain shape instead.</summary>
    static Dictionary<string, PreviewScene>? propModels;
    static bool propsLoading;
    internal const string PropPerson = @"setdressing\mannequin\shroudmalemannequinclothes.nif", PropHouse = @"architecture\covenantsettlement\covenanthouse02.nif",
                 PropWall = @"architecture\workshop\barn\workshop_barnwalla01.nif", PropDoor = @"architecture\workshop\barn\workshop_barnwallb01.nif",
                 PropWindow = @"architecture\workshop\barn\workshop_barnwallc01.nif", PropSmallWindow = @"architecture\workshop\barn\workshop_barnwallf01.nif",
                 PropRoof = @"architecture\workshop\barn\workshop_barnroofflata01.nif";
    /// <summary>The compared items and junk as placed around the current item; rebuilt only when something moves.</summary>
    string placedKey = "";
    readonly List<(List<PreviewPart> Parts, string Name, (float X, float Y, float Z) Top)> placed = new();
    List<PreviewPart> junkPlaced = new();
    (float X, float Y, float Z) othersMin, othersMax;
    readonly RenderExtras extras = new();
    /// <summary>The camera of the last picture: the name labels use it.</summary>
    (float X, float Y, float Z) shotEye, shotLook;
    readonly ToolStripMenuItem spinOn = new("Spin slowly around the item") { CheckOnClick = true };
    readonly ToolStripMenuItem showKeys = new("Show the keys box (mouse and keys)") { CheckOnClick = true };
    readonly System.Windows.Forms.Timer spin = new() { Interval = 33 };
    readonly System.Diagnostics.Stopwatch spinClock = new();
    // the look's animations (pulse, rainbow, spin, bob, flowing beam, hide / blink): redrawn while it has any; the clock
    // starts again with every look change, like the game's starts when the item loads
    readonly System.Windows.Forms.Timer anim = new() { Interval = 50 };
    readonly System.Diagnostics.Stopwatch animClock = new();

    void Animate()
    {
        static bool Lively(Style st) => st.Moves || st.Timed;
        bool on = scene != null && (Lively(shown) || extras.Others.Any(o => Lively(o.Look)));
        if (on) { animClock.Restart(); if (!anim.Enabled) anim.Start(); }
        else { anim.Stop(); animClock.Reset(); }
    }

    static Style Plain()
    {
        var s = new Style();
        s.Glow = s.Beam = s.Brighten = false;
        s.Icon = "";
        return s;
    }
    readonly Button bigger = new() { Text = "⤢ Bigger view", AutoSize = true };
    /// <summary>The controls box at the top left: open in the big view, a small "? Controls" tag in the small one;
    /// a click on it (or Camera > Show the keys box) opens / folds it.</summary>
    bool legendOpen { get => showKeys.Checked; set => showKeys.Checked = value; }
    Rectangle legendBox;
    /// <summary>The toolbar drawn on the picture (full view): where each button is, its tooltip and what a click does.</summary>
    readonly List<(Rectangle R, string Tip, Action<Rectangle> Click)> barHits = new();
    readonly List<(Func<string> Text, Action Click, string Tip)> extraTools = new();
    Rectangle barHover;
    int barBottom;
    string barTipShown = "";
    ToolTip? barTips;
    Button? cameraButton, saveButton;
    bool legendClick;
    (string Key, string What)[] Legend() => new[]
    {
        ("Drag", free ? "Circle the item" : "Turn the item"),
        ("Right-drag", "Look around"),
        ("Wheel", free ? "Step forward / back" : "Zoom"),
        ("W A S D / arrows", "Walk (Shift: faster)"),
        ("Q / E", "Down / up"),
        ("Double-click", "Back to the start"),
    };
    /// <summary>You walked (keys) or stood back (10 / 30 / 100 m): a camera of your own at <see cref="eye"/>;
    /// yaw / pitch are where you look. Off: the camera turns around the item.</summary>
    bool free;
    (float X, float Y, float Z) eye;
    /// <summary>The turning camera as last drawn - walking starts from there.</summary>
    (float X, float Y, float Z) orbitAt;
    float orbitDist;
    const float EyeHeight = 112; // eyes at 1.6 m
    readonly HashSet<Keys> held = new();
    readonly System.Windows.Forms.Timer walk = new() { Interval = 30 };
    readonly System.Diagnostics.Stopwatch walkClock = new();
    readonly FlowLayoutPanel previewRow = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
    enum CamView { Item, Whole }
    CamView camView = CamView.Item;
    /// <summary>This is the bigger view: it shows what the small one loaded (no parts list of its own).</summary>
    (string? Model, List<string>? Gun, bool PartOff)? fixedTarget;
    (string? Model, List<string>? Gun, bool PartOff) lastTarget;
    SizeGuide? guide;
    GameData? game;
    Entry? entry;
    Style style = new();
    Style shown = new();
    /// <summary>The picture shows the marker too (an arrow or icon - not a tall beam unless "Show the whole beam").</summary>
    bool frameAll;
    bool keepOtherMods;
    PreviewScene? scene;
    string message = "";
    Bitmap? frame;
    const float StartZoom = 0.8f;
    /// <summary>Straight in front of the item: game models face -y (the gravestone loot bag's "J. DOE"), and the Wall
    /// behind stands on the +y side (it stood on -y, between the old -0.9 start and the item - user, 2026-11-11).</summary>
    const float StartYaw = -MathF.PI / 2;
    float yaw = StartYaw, pitch = 0.35f, zoom = StartZoom;    Point? drag;
    /// <summary>The drag is with the right button: look around from where you stand.</summary>
    bool lookDrag;
    int version;
    readonly System.Windows.Forms.Timer reload = new() { Interval = 250 };

    /// <summary>The picture: takes the keyboard (arrow keys too) for walking.</summary>
    sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
        protected override bool IsInputKey(Keys k) => (k & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(k);
    }

    /// <summary>A text box / number / list in this window has the keyboard (the settings beside the big view).</summary>
    bool Typing()
    {
        Control? c = FindForm();
        while (c is ContainerControl { ActiveControl: { } a }) c = a;
        return c is TextBoxBase or NumericUpDown or ComboBox || c?.Parent is NumericUpDown;
    }

    static bool WalkKey(Keys k) => k is Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E or Keys.Up or Keys.Down or Keys.Left or Keys.Right;

    (float X, float Y, float Z) ItemCenter => scene == null ? default
        : ((scene.Min.X + scene.Max.X) / 2, (scene.Min.Y + scene.Max.Y) / 2, (scene.Min.Z + scene.Max.Z) / 2);

    /// <summary>Leave the turning camera: stand where it was, looking the same way.</summary>
    void GoFree()
    {
        if (free || scene == null || orbitDist <= 0) return;
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw), cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        eye = (orbitAt.X + orbitDist * cp * cy, orbitAt.Y + orbitDist * cp * sy, Math.Max(scene.Min.Z + 20, orbitAt.Z + orbitDist * sp));
        (yaw, pitch, free) = (yaw + MathF.PI, -pitch, true);
    }

    /// <summary>Back to turning around the item, as first shown.</summary>
    void ResetView()
    {
        (free, yaw, pitch, zoom) = (false, StartYaw, 0.35f, StartZoom);
        held.Clear();
        MakeGuide();
        Redraw();
    }

    /// <summary>Stand that far from the item (game units), eyes at 1.6 m, facing it - on the side you're on now.</summary>
    void StandBack(float d)
    {
        if (scene == null) return;
        GoFree();
        if (!free) return;
        var c = ItemCenter;
        float a = MathF.Atan2(eye.Y - c.Y, eye.X - c.X);
        eye = (c.X + d * MathF.Cos(a), c.Y + d * MathF.Sin(a), scene.Min.Z + EyeHeight);
        FaceItem();
        MakeGuide();
        Redraw();
    }

    /// <summary>One step of walking with the held keys: faster far from the item, Shift three times faster.</summary>
    void WalkStep()
    {
        float dt = Math.Min(0.1f, (float)walkClock.Elapsed.TotalSeconds);
        walkClock.Restart();
        if (held.Count == 0 || scene == null) { walk.Stop(); Redraw(); return; } // sharp again
        GoFree();
        var c = ItemCenter;
        float dist = MathF.Sqrt((eye.X - c.X) * (eye.X - c.X) + (eye.Y - c.Y) * (eye.Y - c.Y) + (eye.Z - c.Z) * (eye.Z - c.Z));
        float speed = Math.Max(250, dist * 0.6f) * ((ModifierKeys & Keys.Shift) != 0 ? 3 : 1) * dt; // 250 units ≈ 3.6 m a second
        int fwd = (held.Contains(Keys.W) || held.Contains(Keys.Up) ? 1 : 0) - (held.Contains(Keys.S) || held.Contains(Keys.Down) ? 1 : 0);
        int side = (held.Contains(Keys.D) || held.Contains(Keys.Right) ? 1 : 0) - (held.Contains(Keys.A) || held.Contains(Keys.Left) ? 1 : 0);
        int rise = (held.Contains(Keys.E) ? 1 : 0) - (held.Contains(Keys.Q) ? 1 : 0);
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        // ground-level walking: forward is where you look, flattened; right is to its side
        eye = (eye.X + speed * (fwd * cy + side * sy), eye.Y + speed * (fwd * sy - side * cy),
               Math.Clamp(eye.Z + speed * 0.6f * rise, scene.Min.Z + 20, scene.Min.Z + 7000)); // 30 cm to 100 m up
        MakeGuide();
        Redraw();
    }

    public ModelView()
    {
        Controls.Add(canvas);
        Controls.Add(partsSplit);
        Controls.Add(partsBox);
        partsBox.VisibleChanged += (_, _) => partsSplit.Visible = partsBox.Visible;
        parts.Resize += (_, _) => FitPartsColumn();
        parts.HandleCreated += (_, _) => BeginInvoke(FitPartsColumn);
        lookOn.Checked = true;
        lookOn.AutoCheck = lookOff.AutoCheck = false; // a switch: clicking the lit one keeps it lit
        lookOn.Click += (_, _) => lookOn.Checked = true;
        lookOff.Click += (_, _) => lookOn.Checked = false;
        lookSwitch.Toggled += () => lookOn.Checked = lookSwitch.Checked;
        lookSwitchText.Click += (_, _) => lookOn.Checked = !lookOn.Checked;
        previewRow.Controls.AddRange(new Control[] { lookSwitch, lookSwitchText, bigger });
        addMenu.Items.AddRange(new ToolStripItem[] { addPerson, addHouse, addShed, addJunk });
        addMenu.Closing += (_, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; }; // tick several
        addMenu.Opening += (_, _) => FillAddMenu();
        var onlyOne = new[] { addPerson, addHouse, addShed, addWall }; // one scene prop at a time
        foreach (var it in onlyOne) it.CheckedChanged += (_, _) =>
        {
            if (filling || !it.Checked) return;
            filling = true;
            foreach (var other in onlyOne) if (other != it) other.Checked = false;
            filling = false;
        };
        foreach (var it in new[] { addPerson, addHouse, addShed, addWall, addJunk, hideLabels }) it.CheckedChanged += (_, _) => { if (!filling) { MakeGuide(); Redraw(); } };
        addJunk.CheckedChanged += (_, _) => { if (addJunk.Checked) LoadJunk(); };
        // Light and Colours: pick one (a dot on the current one); the toolbar button says which
        for (int i = 0; i < SkyNames.Length; i++)
        {
            int n = i;
            lightMenu.Items.Add(new ToolStripMenuItem(SkyNames[i], null, (_, _) => { extras.Sky = (Sky)n; Redraw(); canvas.Focus(); }) { Checked = i == 0 });
        }
        for (int i = 0; i < VisionNames.Length; i++)
        {
            int n = i;
            visionMenu.Items.Add(new ToolStripMenuItem(VisionNames[i], null, (_, _) => { extras.Vision = (Vision)n; Redraw(); canvas.Focus(); }) { Checked = i == 0 });
        }
        lightMenu.Items[1].ToolTipText = "The sky by day, at dusk, at night or in fog: sky colour, light level and haze";
        visionMenu.Items[0].ToolTipText = "See the picture as someone with colour blindness does - does your colour still stand out?";
        lightMenu.Opening += (_, _) => { for (int i = 0; i < lightMenu.Items.Count; i++) ((ToolStripMenuItem)lightMenu.Items[i]).Checked = i == (int)extras.Sky; };
        visionMenu.Opening += (_, _) => { for (int i = 0; i < visionMenu.Items.Count; i++) ((ToolStripMenuItem)visionMenu.Items[i]).Checked = i == (int)extras.Vision; };
        spinOn.CheckedChanged += (_, _) => { if (spinOn.Checked) { spinClock.Restart(); spin.Start(); } else spin.Stop(); canvas.Focus(); };
        showKeys.CheckedChanged += (_, _) => canvas.Invalidate();
        whole.ToolTipText = "Move the camera back so the whole tall beam fits in the picture (only with a tall beam)";
        cameraMenu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Reset - back to turning around the item", null, (_, _) => { ResetView(); canvas.Focus(); }) { ShortcutKeyDisplayString = "Double-click" },
            new ToolStripSeparator(),
            new ToolStripMenuItem("Stand back 10 m", null, (_, _) => { StandBack(700); canvas.Focus(); }) { ToolTipText = "Stand 10 m from the item, eyes at 1.6 m, facing it" },
            new ToolStripMenuItem("Stand back 30 m", null, (_, _) => { StandBack(2100); canvas.Focus(); }) { ToolTipText = "Stand 30 m from the item, eyes at 1.6 m, facing it" },
            new ToolStripMenuItem("Stand back 100 m", null, (_, _) => { StandBack(7000); canvas.Focus(); }) { ToolTipText = "Stand 100 m from the item, eyes at 1.6 m, facing it" },
            new ToolStripSeparator(),
            spinOn, whole, showKeys,
        });
        spin.Tick += (_, _) => SpinStep();
        anim.Tick += (_, _) => { if (canvas.Visible && drag == null && !spin.Enabled && !walk.Enabled) Redraw(); }; // spinning / walking redraw anyway
        var tips = new ToolTip { AutoPopDelay = 20000 };
        tips.SetToolTip(bigger, "Open the picture in a big window (Esc closes it)");
        tips.SetToolTip(lookSwitch, "On: the item with your look (glow, beam, icon). Off: the original game model, to compare (preview only - the mod doesn't change)");
        tips.SetToolTip(lookSwitchText, "On: the item with your look (glow, beam, icon). Off: the original game model, to compare (preview only - the mod doesn't change)");
        tips.SetToolTip(lookOff, "Show the original game model, to compare (preview only - the mod doesn't change)");
        Controls.Add(previewRow);
        parts.Columns.Add("Part", 220);
        var header = new Label { Dock = DockStyle.Top, Height = 20, Text = "Parts - deselect to leave out" };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, WrapContents = false };
        Button B(string t, bool on) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => SetAll(on); return b; }
        partLook.Click += (_, _) => EditPartLook();
        buttons.Controls.AddRange(new Control[] { B("Select all", true), B("Deselect all", false), partLook });
        var partMenu = new ContextMenuStrip();
        var ownItem = new ToolStripMenuItem("Own look for this part...", null, (_, _) => EditPartLook());
        ownItem.Font = new Font(partMenu.Font, FontStyle.Bold);
        var sameItem = new ToolStripMenuItem("Use the item's look", null, (_, _) => ClearPartLook());
        partMenu.Items.AddRange(new ToolStripItem[] { ownItem, sameItem });
        partMenu.Opening += (_, e) =>
        {
            if (Selected() is not string m) { e.Cancel = true; return; }
            sameItem.Enabled = partLooks.ContainsKey(m);
        };
        parts.ContextMenuStrip = partMenu;
        // a double-click opens the part's look; Windows would also flip its box - only a click on the box should
        bool partDoubleClick = false;
        parts.MouseDown += (_, e) => partDoubleClick = e.Clicks > 1;
        parts.ItemCheck += (_, e) => { if (partDoubleClick && !filling) e.NewValue = e.CurrentValue; };
        parts.MouseUp += (_, _) => partDoubleClick = false;
        parts.DoubleClick += (_, _) => { partDoubleClick = false; EditPartLook(); };
        new ToolTip { AutoPopDelay = 20000 }.SetToolTip(partLook, "Give the selected part its own glow, beam and icon (also: double-click it).\n" +
                                                                 "E.g. the Pint-Sized Slasher mask inside Spooky Scorched.");
        partsBox.Controls.Add(parts);
        partsBox.Controls.Add(header);
        partsBox.Controls.Add(buttons);
        partsBox.Controls.Add(partsNote);
        parts.SelectedIndexChanged += (_, _) =>
        {
            if (filling || !parts.Created) return;
            picked = parts.SelectedItems.Count > 0 ? parts.SelectedItems[0].Tag as string : null;
            if (picked != null) { zoom = StartZoom; Load(); }
        };
        parts.ItemChecked += (_, e) =>
        {
            // the list re-sends these while Windows creates it - only real clicks count
            if (filling || !parts.Created || e.Item?.Tag is not string m) return;
            if (e.Item.Selected) { reload.Stop(); reload.Start(); } // redraw it lit or plain
            FollowParts();
            var others = sharedWith(m);
            partsNote.Text = e.Item.Checked || others.Count == 0 || !ticked ? CountText()
                : $"{e.Item.Text} is also part of {string.Join(", ", others.Take(3))}{(others.Count > 3 ? "..." : "")} - " +
                  "if that is selected, the part still lights up (one model, one look).";
        };
        canvas.Paint += PaintCanvas;
        canvas.Resize += (_, _) => Redraw();
        canvas.MouseDown += (_, e) =>
        {
            canvas.Focus();
            if (barHits.LastOrDefault(b => b.R.Contains(e.Location)) is { Click: not null } hit)
            {
                legendClick = true; // not a drag, and a double-click doesn't reset the view
                if (e.Button == MouseButtons.Left) { hit.Click(hit.R); canvas.Invalidate(); }
                return;
            }
            legendClick = legendBox.Contains(e.Location);
            if (legendClick) { if (e.Button == MouseButtons.Left) { legendOpen = !legendOpen; canvas.Invalidate(); } return; }
            if (e.Button == MouseButtons.Left || isBig && e.Button == MouseButtons.Right) (drag, lookDrag) = (e.Location, e.Button == MouseButtons.Right);
        };
        canvas.MouseUp += (_, _) => { if (drag == null) return; drag = null; if (frame != null && frame.Width != canvas.Width) Redraw(); }; // sharp again
        canvas.KeyDown += (_, e) =>
        {
            if (!isBig || !WalkKey(e.KeyCode) || scene == null) return;
            e.Handled = e.SuppressKeyPress = true;
            if (held.Add(e.KeyCode) && !walk.Enabled) { walkClock.Restart(); walk.Start(); WalkStep(); }
        };
        canvas.KeyUp += (_, e) => held.Remove(e.KeyCode);
        canvas.LostFocus += (_, _) => held.Clear();
        walk.Tick += (_, _) => WalkStep();
        canvas.MouseMove += (_, e) =>
        {
            if (drag is not Point d) return;
            float mx = e.X - d.X, my = e.Y - d.Y;
            if (lookDrag) // right button: turn your head as in a game - mouse right looks right, up looks up
            {
                GoFree();
                yaw -= mx * 0.005f;
                pitch = Math.Clamp(pitch - my * 0.005f, -1.45f, 1.45f);
            }
            else if (free && scene != null) // left button after walking: circle the item at the same distance, facing it
            {
                var c = ItemCenter;
                float dx = eye.X - c.X, dy = eye.Y - c.Y, dz = eye.Z - c.Z, r = Math.Max(1, MathF.Sqrt(dx * dx + dy * dy + dz * dz));
                float a = MathF.Atan2(dy, dx) - mx * 0.01f, el = Math.Clamp(MathF.Asin(Math.Clamp(dz / r, -1, 1)) + my * 0.01f, -1.45f, 1.45f);
                eye = (c.X + r * MathF.Cos(el) * MathF.Cos(a), c.Y + r * MathF.Cos(el) * MathF.Sin(a), Math.Max(scene.Min.Z + 20, c.Z + r * MathF.Sin(el)));
                FaceItem();
                MakeGuide();
            }
            else // left button: turn the item (the camera circles it)
            {
                yaw -= mx * 0.01f;
                pitch = Math.Clamp(pitch + my * 0.01f, -1.45f, 1.45f);
            }
            drag = e.Location;
            Redraw();
        };
        canvas.MouseWheel += (_, e) =>
        {
            if (free && scene != null) // a step where you look: an eighth of the way to the item, at least 30 cm
            {
                var c = ItemCenter;
                float dist = MathF.Sqrt((eye.X - c.X) * (eye.X - c.X) + (eye.Y - c.Y) * (eye.Y - c.Y) + (eye.Z - c.Z) * (eye.Z - c.Z));
                float k = Math.Max(20, dist / 8) * (e.Delta > 0 ? 1 : -1), cp = MathF.Cos(pitch);
                eye = (eye.X + k * cp * MathF.Cos(yaw), eye.Y + k * cp * MathF.Sin(yaw), Math.Clamp(eye.Z + k * MathF.Sin(pitch), scene.Min.Z + 20, scene.Min.Z + 7000));
                MakeGuide();
            }
            else zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f), 0.02f, 20f);
            Redraw();
        };
        // the big window takes the keys as soon as the mouse is over the picture; the small one on a click
        // (it sits beside the look's text boxes - passing over it mustn't steal the typing)
        canvas.MouseEnter += (_, _) => { if (fixedTarget != null && !Typing()) canvas.Focus(); };
        canvas.DoubleClick += (_, _) => { if (!legendClick) ResetView(); };
        canvas.MouseMove += (_, e) =>
        {
            var over = drag == null ? barHits.LastOrDefault(b => b.R.Contains(e.Location)) : default;
            canvas.Cursor = drag == null && (over.Click != null || legendBox.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
            if (over.R != barHover) { barHover = over.R; canvas.Invalidate(); }
            var tip = over.Tip ?? "";
            if (tip != barTipShown && barTips != null) { barTipShown = tip; barTips.SetToolTip(canvas, tip); }
        };
        canvas.MouseLeave += (_, _) => { if (barHover != Rectangle.Empty) { barHover = Rectangle.Empty; canvas.Invalidate(); } };
        whole.CheckedChanged += (_, _) => { if (filling) return; camView = whole.Checked ? CamView.Whole : CamView.Item; ResetView(); };
        bigger.Click += (_, _) => OpenBig();
        lookOn.CheckedChanged += (_, _) => { lookOff.Checked = !lookOn.Checked; lookSwitch.Checked = lookOn.Checked; if (!filling) Load(); };
        reload.Tick += (_, _) => { reload.Stop(); Load(); };
    }

    /// <summary>Shows an entry (its main model first) with a look.</summary>
    /// <param name="off">Parts left out of the mod.</param>
    /// <param name="shared">Other entries using a model (for the note when a shared part is unticked).</param>
    /// <param name="ticked">The entry is ticked: unticked, nothing of it lights up (shown plain).</param>
    public void Show(GameData game, Entry e, Style st, bool keepOtherMods, IReadOnlySet<string>? off = null, Func<string, List<string>>? shared = null,
                     bool ticked = true, string? focusPart = null, IReadOnlyDictionary<string, Style>? partLooks = null)
    {
        if (e != entry && free) free = false; // another item: turn around it again
        this.game = game; entry = e; style = st.Clone(); this.keepOtherMods = keepOtherMods; this.ticked = ticked;
        this.partLooks = partLooks?.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()) ?? new();
        savedOff = off?.ToHashSet() ?? new();
        sharedWith = shared ?? (_ => new());
        filling = true;
        parts.BeginUpdate();
        parts.Items.Clear();
        // the main model first: the one carrying the beam, else the biggest file (a body, not a blade or an eye);
        // then the rest, biggest first
        var main = e.BeamModels?.FirstOrDefault(e.Models.Contains);
        // human enemies: by name, so an outfit's pieces stay together
        var order = e.Category == Enemies.Humans
            ? e.Models.OrderBy(Label, StringComparer.OrdinalIgnoreCase)
            : e.Models.OrderByDescending(m => m == main).ThenByDescending(m => game.Find(@"meshes\" + m)?.Size ?? 0);
        foreach (var m in order.OrderByDescending(m => this.partLooks.ContainsKey(m))) // parts with their own look first
        {
            var others = sharedWith(m);
            parts.Items.Add(Marked(new ListViewItem(ItemText(m))
            {
                Tag = m, Checked = ticked && off?.Contains(m) != true, // unticked entry: nothing of it lights up
                ToolTipText = Label(m) + "\n" + m + (others.Count > 0 ? "\nAlso part of: " + string.Join(", ", others) : ""),
            }, m));
        }
        parts.EndUpdate();
        FitPartsColumn();
        filling = false;
        partsBox.Visible = ShowParts && e.Models.Count > 1;
        partsNote.Text = CountText();
        filling = true;
        // the part asked for (an ingredient double-clicked in the list), else the first
        var first = parts.Items.Cast<ListViewItem>().FirstOrDefault(i => focusPart != null && (string?)i.Tag == focusPart) ?? (parts.Items.Count > 0 ? parts.Items[0] : null);
        if (first != null) { first.Selected = true; first.Focused = true; first.EnsureVisible(); }
        // no part list on screen and no part asked for: a weapon shows whole
        picked = ShowParts || focusPart != null || !e.Assembled ? first?.Tag as string : null;
        filling = false;
        Load(); // the first part (before the window exists the list sends no selection event)
    }

    bool ticked = true;
    HashSet<string> savedOff = new();
    Dictionary<string, Style> partLooks = new();
    readonly Button partLook = new() { Text = "Part look...", AutoSize = true };

    /// <summary>Parts given their own look in this window.</summary>
    public Dictionary<string, Style> PartLooks => partLooks;

    string Label(string m) => entry?.PartNames?.GetValueOrDefault(m) ?? Path.GetFileNameWithoutExtension(m);

    /// <summary>"◆ name  *": ◆ = its own look, * = also part of another entry.</summary>
    string ItemText(string m) => (partLooks.ContainsKey(m) ? "◆ " : "") + Label(m) + (sharedWith(m).Count > 0 ? "  *" : "");

    /// <summary>The part shown. Kept here, not read from the list: the hidden list (pop-up, ShowParts off) has no
    /// window, and a ListView without one reports no selection - every part would show the main model.</summary>
    string? picked;
    string? Selected() => picked;

    /// <summary>The selected part's own look: the look settings and a picture of just that part.</summary>
    void EditPartLook()
    {
        if (game == null || entry == null || Selected() is not string m) return;
        var single = new Entry
        {
            Id = entry.Id + "|" + m, Name = Label(m), Category = entry.Category, Models = new() { m },
            NoIcon = entry.NoIcon, Harvestable = entry.Harvestable,
        };
        using var dlg = MainForm.CategoryLookDialog(game, "Own look for: " + Label(m), new() { single }, partLooks.GetValueOrDefault(m) ?? style,
                                                    entry.NoIcon, keepOtherMods, Font, out var ed);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        partLooks[m] = ed.Value;
        RefreshPart(m);
    }

    void ClearPartLook()
    {
        if (Selected() is string m && partLooks.Remove(m)) RefreshPart(m);
    }

    /// <summary>A part with its own look stands out: bold, in its own colour.</summary>
    ListViewItem Marked(ListViewItem i, string m)
    {
        if (partLooks.TryGetValue(m, out var own))
        {
            var c = Materials.Parse(own.Glow ? own.GlowColor : own.BeamColor);
            // light colours darkened until they read on the white list
            for (int k = 0; k < 6 && (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.62; k++)
                c = Color.FromArgb((int)(c.R * 0.85), (int)(c.G * 0.85), (int)(c.B * 0.85));
            i.ForeColor = c;
            i.Font = new Font(parts.Font, FontStyle.Bold);
        }
        else { i.ForeColor = parts.ForeColor; i.Font = parts.Font; }
        return i;
    }

    /// <summary>The names column as wide as the longest name (a bold own-look name included), at least the list's width:
    /// longer names scroll sideways instead of being cut off.</summary>
    void FitPartsColumn()
    {
        if (parts.Columns.Count == 0) return;
        // Items(): the list hands out empty slots while Windows creates it
        int widest = Items().Select(i => TextRenderer.MeasureText(i.Text, i.Font ?? parts.Font).Width).DefaultIfEmpty(0).Max();
        parts.Columns[0].Width = Math.Max(widest + 26, parts.ClientSize.Width - 4); // + the tick box
    }

    void RefreshPart(string m)
    {
        foreach (var i in Items().Where(i => (string)i.Tag! == m)) { i.Text = ItemText(m); Marked(i, m); }
        FitPartsColumn();
        partsNote.Text = CountText();
        reload.Stop(); reload.Start();
    }

    /// <summary>Off for a category look: just the picture of the item's main model, no parts list.</summary>
    public bool ShowParts { get; set; } = true;

    /// <summary>The parts decide the entry's tick (like a folder tree): any part ticked = the entry is ticked, none = not.</summary>
    public event Action<CheckState>? ParentTick;

    /// <summary>The entry's own tick changed (the box above): ticked brings back the saved part choice (all parts if that
    /// left none), unticked clears the parts. Unticked, the picture shows the item plain.</summary>
    public bool Ticked
    {
        set
        {
            if (value != ticked)
            {
                ticked = value;
                filling = true;
                foreach (var i in Items()) i.Checked = value; // ticking the entry ticks all its parts
                filling = false;
            }
            partsNote.Text = CountText();
            reload.Stop(); reload.Start();
        }
    }

    /// <summary>After a part was ticked / unticked: the entry is ticked (all parts), partly ticked (some) or not (none).</summary>
    void FollowParts()
    {
        int on = Items().Count(i => i.Checked), all = Items().Count();
        ticked = on > 0;
        ParentTick?.Invoke(on == 0 ? CheckState.Unchecked : on == all ? CheckState.Checked : CheckState.Indeterminate);
    }

    /// <summary>Parts the user unticked.</summary>
    public HashSet<string> OffParts => Items().Where(i => !i.Checked).Select(i => (string)i.Tag!).ToHashSet();

    IEnumerable<ListViewItem> Items() => parts.Items.Cast<ListViewItem?>().OfType<ListViewItem>().Where(i => i.Tag is string);

    string CountText()
    {
        if (!ticked)
            return "Not selected - nothing of this lights up. Select a part (or the box at the top) to light it up.";
        int off = Items().Count(i => !i.Checked);
        bool anyShared = Items().Any(i => i.Text.EndsWith("*"));
        int own = partLooks.Count(kv => Items().Any(i => (string)i.Tag! == kv.Key));
        return (off == 0 ? $"All {parts.Items.Count} parts light up." : $"{parts.Items.Count - off} of {parts.Items.Count} parts light up.") +
               (own > 0 ? $" {own} with their own look (◆)." : "") +
               (anyShared ? "\n* = also part of another entry" : "");
    }

    void SetAll(bool on)
    {
        filling = true;
        foreach (ListViewItem i in parts.Items) i.Checked = on;
        filling = false;
        FollowParts();
        partsNote.Text = CountText();
        reload.Stop(); reload.Start();
    }

    /// <summary>The look changed: rebuild the picture shortly (the beam and icons change the model).</summary>
    public Style Look
    {
        set { style = value.Clone(); reload.Stop(); reload.Start(); }
    }

    /// <summary>What to load: the picked part (or the main model), the whole gun when no part is picked, and whether
    /// the picked part is left out of the mod. The bigger view keeps what the small one showed.</summary>
    (string? Model, List<string>? Gun, bool PartOff) Target()
    {
        if (fixedTarget is { } t) return t;
        var model = picked ?? entry!.Models.FirstOrDefault();
        var off = OffParts;
        // a weapon with no part picked: the whole gun (its parts left in the mod)
        var gun = entry!.Assembled && picked == null ? entry.Models.Where(m => !off.Contains(m)).ToList() : null;
        return (model, gun, picked != null && parts.Items.Cast<ListViewItem>().FirstOrDefault(i => (string?)i.Tag == picked)?.Checked == false);
    }

    /// <summary>The picture in a big window: same item and look, a camera choice, the size guide on.</summary>
    /// <summary>The open bigger view: not modal, it follows every change made here (look edits, another part).</summary>
    ModelView? big;
    Form? bigForm;
    /// <summary>The look settings beside this preview: the bigger view borrows them while it is open.</summary>
    public Control? Settings { get; set; }

    void OpenBig()
    {
        if (game == null || entry == null || scene == null) return;
        if (bigForm != null) { bigForm.Activate(); return; }
        var owner = FindForm();
        var area = Screen.FromControl(this).WorkingArea;
        var f = new Form
        {
            Text = "3D view - " + entry.Name, StartPosition = FormStartPosition.CenterScreen, Size = new Size(area.Width * 3 / 4, area.Height * 3 / 4),
            MinimizeBox = false, ShowIcon = false, ShowInTaskbar = false, KeyPreview = true, Font = owner?.Font ?? Font, BackColor = SystemColors.Control,
        };
        var view = new ModelView { Dock = DockStyle.Fill, ShowParts = false };
        view.CopyFrom(this);
        f.Controls.Add(view);
        // the look settings move in on the right (one place to look and edit); they go back when it closes
        Action giveBack = () => { };
        if (Settings is { Parent: { } home } s)
        {
            int at = home.Controls.GetChildIndex(s);
            var dock = s.Dock;
            var stand = new Label
            {
                Dock = dock, Size = s.Size, TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText, Padding = new Padding(16),
                Text = "The look settings are in the 3D view window - close it to edit them here.",
            };
            var side = new Panel { Dock = DockStyle.Right, Width = Math.Max(360, s.Width) + 12, Padding = new Padding(8, 4, 4, 4) };
            home.Controls.Add(stand);
            home.Controls.SetChildIndex(stand, at);
            s.Dock = DockStyle.Fill;
            side.Controls.Add(s);
            f.Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 6, MinSize = 300, MinExtra = 300 });
            f.Controls.Add(side);
            f.Width = Math.Min(area.Width, f.Width + side.Width);
            giveBack = () =>
            {
                side.Controls.Remove(s);
                s.Dock = dock;
                home.Controls.Remove(stand);
                stand.Dispose();
                home.Controls.Add(s);
                home.Controls.SetChildIndex(s, at);
            };
        }
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
        f.HandleCreated += (_, _) => Ui.TitleBar(f);
        // the window it came from closes (the look pop-up hides): this one goes too
        EventHandler gone = (_, _) => { if (owner is { Visible: false }) f.Close(); };
        if (owner != null) owner.VisibleChanged += gone;
        f.FormClosed += (_, _) => { if (owner != null) owner.VisibleChanged -= gone; giveBack(); big = null; bigForm = null; f.Dispose(); };
        Ui.Apply(f);
        (big, bigForm) = (view, f);
        f.Show(owner);
    }

    /// <summary>The bigger view takes the small one's new item / look; its own camera stays unless the model changed.</summary>
    void Follow(ModelView o)
    {
        bool same = entry == o.entry && fixedTarget?.Model == o.lastTarget.Model && fixedTarget?.Gun == null == (o.lastTarget.Gun == null);
        (game, entry, style, shown, scene, keepOtherMods, ticked, partLooks) = (o.game, o.entry, o.style.Clone(), o.shown, o.scene, o.keepOtherMods, o.ticked, o.partLooks);
        (fixedTarget, message, frameAll) = (o.lastTarget, o.message, o.frameAll);
        BeamCheck();
        if (!same) (free, yaw, pitch, zoom) = (false, o.free ? StartYaw : o.yaw, o.free ? 0.35f : o.pitch, StartZoom);
        if (FindForm() is { } f && o.entry != null) f.Text = "3D view - " + o.entry.Name;
        MakeGuide();
        Animate();
        Redraw();
    }

    void CopyFrom(ModelView o)
    {
        filling = true;
        isBig = true;
        (game, entry, style, shown, scene, keepOtherMods, ticked, partLooks) = (o.game, o.entry, o.style.Clone(), o.shown, o.scene, o.keepOtherMods, o.ticked, o.partLooks);
        (fixedTarget, message, frameAll, yaw, pitch) = (o.lastTarget, o.message, o.frameAll, o.yaw, o.pitch);
        (free, eye, zoom) = (o.free, o.eye, o.zoom);
        lookOn.Checked = o.lookOn.Checked;
        whole.Checked = o.whole.Checked;
        legendOpen = true; // the controls on show
        (addHouse.Checked, addShed.Checked) = (o.addHouse.Checked, o.addShed.Checked);
        addPerson.Checked = !addHouse.Checked && !addShed.Checked; // else the big view starts with the person (one prop at a time)
        filling = false;
        BeamCheck();
        partsBox.Visible = false;
        Animate(); // its own clock (the small view's look may move)
        BuildToolbar();
    }

    /// <summary>The full view in place - the look pop-up (user, 2026-11-09: the pop-up can be made big, so no separate
    /// window): the toolbar, walking, the scene, light and colours, name labels.</summary>
    public void MakeFull()
    {
        if (isBig) return;
        isBig = true;
        addWall.Checked = true; // a wall behind the item from the start: turn round to check X-ray (user, 2026-11-09)
        BuildToolbar();
        BeamCheck();
        Redraw();
    }

    /// <summary>Another button at the end of the toolbar on the picture (the pop-up's "Hide settings").</summary>
    public void AddToolAction(Func<string> text, Action click, string tip) { extraTools.Add((text, click, tip)); canvas.Invalidate(); }

    /// <summary>The big view's toolbar in place of the small view's switch + "Bigger view".</summary>
    void BuildToolbar()
    {
        previewRow.Controls.Clear();
        var tips = barTips = new ToolTip { AutoPopDelay = 20000 };
        Button B(string t, string tip, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => { a(); canvas.Focus(); }; tips.SetToolTip(b, tip); return b; }
        // the toolbar at the top: the two switches, then a menu per topic (each button says its setting), then Save
        Button Drop(ContextMenuStrip m, string tip)
        {
            var b = new Button { AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
            b.Click += (_, _) => m.Show(b, new Point(0, b.Height));
            tips.SetToolTip(b, tip);
            return b;
        }
        sceneButton = Drop(addMenu, "Put things in the picture: a person for size, a house, walls around it, junk, or other items to compare");
        lightButton = Drop(lightMenu, "The light: studio, or the sky by day, at dusk, at night or in fog");
        visionButton = Drop(visionMenu, "See the picture as someone with colour blindness does - does your colour still stand out?");
        var camera = Drop(cameraMenu, "Reset the view, stand back 10 / 30 / 100 m, spin, fit a tall beam, the keys box");
        camera.Text = "Camera ▾";
        camera.Margin = new Padding(12, 3, 3, 3);
        var save = B("Save picture...", "Save the picture as it is now as a PNG file", SavePicture);
        save.Margin = new Padding(12, 3, 3, 3);
        previewRow.Dock = DockStyle.Top;
        previewRow.Padding = new Padding(4, 4, 4, 2);
        // drawn on the picture (top right, like the controls box) instead of a strip above it - user, 2026-11-09
        (cameraButton, saveButton) = (camera, save);
        previewRow.Visible = false;
        UpdateBar();
        MakeGuide();
    }

    /// <summary>The toolbar buttons say what is set: how many things are in the scene, the light, the colours.</summary>
    void UpdateBar()
    {
        if (sceneButton == null || lightButton == null || visionButton == null) return;
        int n = new[] { addPerson, addHouse, addShed, addJunk }.Count(i => i.Checked) + compares.Count;
        string scene = n > 0 ? $"Scene ({n}) ▾" : "Scene ▾", light = $"Light: {SkyNames[(int)extras.Sky]} ▾",
               vision = extras.Vision == Vision.Normal ? "Colours: Normal ▾" : $"Colours: {VisionNames[(int)extras.Vision]} ▾";
        if (sceneButton.Text != scene) sceneButton.Text = scene;
        if (lightButton.Text != light) lightButton.Text = light;
        if (visionButton.Text != vision) visionButton.Text = vision;
        canvas.Invalidate(); // the toolbar on the picture shows the new texts
    }

    async void Load()
    {
        if (game == null || entry == null) return;        var (model, gun, partOff) = Target();
        lastTarget = (model, gun, partOff);
        if (model == null)
        {
            // only recolours a texture (an invisible enemy's shimmer): nothing to turn around
            scene = null;
            message = "Nothing to show in 3D - this one recolours the shimmer of invisible enemies (see Details in the list).";
            whole.Enabled = false;
            Redraw();
            big?.Follow(this);
            return;
        }
        int v = ++version;
        var (g, e, st, keep) = (game, entry, style.Clone(), keepOtherMods);
        bool ownLook = partLooks.TryGetValue(model, out var ownStyle);
        if (ownLook) st = ownStyle!.Clone(); // a part with its own look: that look, with its own beam / icon
        bool plain = !ticked || partOff;
        if (plain || !lookOn.Checked) { st.Glow = st.Beam = st.Brighten = st.Label = st.Invisible = false; st.Icon = ""; } // as in the game
        if (!e.CanHide) st.Invisible = false; // creatures are never hidden
        bool beamHere = ownLook || e.BeamModels == null || e.BeamModels.Contains(model);
        message = scene == null ? "Loading..." : message;
        canvas.Invalidate();
        try
        {
            var s = await Task.Run(() => gun is { Count: > 0 } ? ModelPreview.LoadAll(g, gun, st, e.NoIcon, e.Harvestable, beamHere, keep)
                                                       : ModelPreview.LoadAs(g, e, model, st, beamHere, keep));
            if (v != version) return;
            scene = s;
            shown = st; // the colours to draw it in: a part's own look, if it has one
            message = !ticked ? "Not selected - shown as it is in the game. Select it to see the look."
                    : !lookOn.Checked ? "The look is hidden here (preview only) - the plain game model."
                    : ownLook ? $"◆ {Label(model)} has its own look (the settings on the left are the rest's) - \"Part look...\" changes it." + (s.Note.Length > 0 ? "\n" + s.Note : "")
                    : s.Note;
        }
        catch (Exception ex)
        {
            if (v != version) return;
            scene = null;
            message = "Can't show this model: " + ex.Message;
        }
        // an arrow or an icon sits close above the item: always in the picture. Only a tall beam would shrink the item
        // to a dot - that one is in view on request
        frameAll = !(st.Beam && !st.BeamArrow);
        BeamCheck();
        MakeGuide();
        Animate();
        Redraw();
        big?.Follow(this);
    }

    /// <summary>"Fit the whole beam" only in the big view (the small one is too small for it) and only with a tall beam.</summary>
    void BeamCheck()
    {
        whole.Enabled = isBig && scene != null && shown.Beam && !shown.BeamArrow;
        camView = whole.Enabled && whole.Checked ? CamView.Whole : CamView.Item;
    }

    /// <summary>The floor grid and what was added (none when nothing is, except from 30 m - you stand on the grid there):
    /// 10 cm squares under a small item, else 1 m. As first seen, the person stands half a metre to the item's right,
    /// the house to its left; the shed closes around it.</summary>
    void MakeGuide()
    {
        guide = null;
        Place();
        if (scene == null || !AnyProps && !free) return;
        var (min, max) = (scene.Min, scene.Max);
        float cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2;
        float size = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        float d = 0.5f * MathF.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y)) + 35;
        if (addShed.Checked) d += Math.Max(25, size * 0.15f) + 40; // the others stand outside the shed
        if ((addPerson.Checked || addHouse.Checked || addShed.Checked || addWall.Checked) && propModels == null) LoadProps();
        var house = propModels?.GetValueOrDefault(PropHouse);
        var person = propModels?.GetValueOrDefault(PropPerson);
        var walls = propModels != null && propModels.ContainsKey(PropWall) ? WallsAround(propModels, min, max) : null;
        if (walls != null) d = Math.Max(d, (Math.Max(walls.Value.HalfX, walls.Value.HalfY) + 45) / 0.78f); // the person stands outside
        // a house's corner from its middle (the drawn one: 3.2 m)
        float houseR = house == null ? 322 : MathF.Sqrt(MathF.Pow(Math.Max(-house.Min.X, house.Max.X), 2) + MathF.Pow(Math.Max(-house.Min.Y, house.Max.Y), 2));
        float hd = d + houseR + 10;
        var props = new List<PreviewPart>();
        // the compared items stand in a row to the right (+x): the person moves to the left then
        // the person stands right beside the item's side (outside the walls), level with its middle
        float side = walls != null ? walls.Value.HalfX + 45 : addShed.Checked ? 0.5f * (max.X - min.X) + Math.Max(25, size * 0.15f) + 70 : 0.5f * (max.X - min.X) + 45;
        float px = cx + (placed.Count > 0 ? -side : side), py = cy;
        if (addPerson.Checked)
        {
            if (person != null) props.AddRange(Stand(person, px, py, min.Z, MathF.PI)); // the mannequin faces +y: turn him round
            else props.Add(SizeGuide.PersonAt(px, py, min.Z));
        }
        if (addHouse.Checked && house == null) props.Add(SizeGuide.HouseAt(cx - 0.78f * hd, cy - 0.62f * hd, min.Z));
        if (addShed.Checked)
        {
            if (walls != null) props.AddRange(walls.Value.Parts);
            else props.Add(SizeGuide.ShedAround(min, max));
        }
        if (addJunk.Checked) { props.Add(SizeGuide.JunkAround(min, max, (entry?.Id ?? "").Aggregate(17, (h, ch) => h * 31 + ch))); props.AddRange(junkPlaced); }
        // the house (the item on its floor) and the wall go last and don't widen the picture (backdrops: the item stays
        // as big as without them)
        int backdrop = 0;
        if (addHouse.Checked && house != null)
        {
            // the item stands inside the house, on its ground floor (user, 2026-11-09: how it glows inside a building);
            // the camera stays close to the item, inside
            float fx = (house.Min.X + house.Max.X) / 2, fy = (house.Min.Y + house.Max.Y) / 2, floor = FloorAt(house, fx, fy);
            var inside = Stand(house, cx - fx, cy - fy, min.Z - floor).ToList();
            props.AddRange(inside);
            backdrop += inside.Count;
        }
        else if (addWall.Checked && !addShed.Checked && WallBehind(propModels, min, max) is { } back) { props.AddRange(back); backdrop += back.Count; }
        // small squares under a small item seen close; 1 m once you walk (you stand on the grid)
        float step = camView == CamView.Item && !free && size < 42 && !addHouse.Checked && !addShed.Checked ? 7 : 70; // 60 cm = 42 units
        float radius = free ? MathF.Sqrt((eye.X - cx) * (eye.X - cx) + (eye.Y - cy) * (eye.Y - cy)) + 700
                     : camView == CamView.Whole ? Math.Max(700, (scene.AllMax.Z - min.Z) * 0.6f)
                     : Math.Max(d + 60, size * 1.5f);
        if (addHouse.Checked) radius = Math.Max(radius, hd + 400);
        if (placed.Count > 0) radius = Math.Max(radius, othersMax.X - cx + 150);
        guide = new SizeGuide(min.Z, cx, cy, step, Math.Min(radius, step * 50), true, props.ToArray(), backdrop);
    }

    /// <summary>Loads the Add menu's game models (<see cref="propModels"/>) once, then redraws with them.</summary>
    async void LoadProps()
    {
        if (propModels != null || propsLoading || game == null) return;
        propsLoading = true;
        var g = game;
        propModels = await Task.Run(() => LoadPropModels(g));
        propsLoading = false;
        if (IsDisposed) return;
        MakeGuide();
        Redraw();
    }

    internal static Dictionary<string, PreviewScene> LoadPropModels(GameData g)
    {
        var r = new Dictionary<string, PreviewScene>();
        foreach (var m in new[] { PropPerson, PropHouse, PropWall, PropDoor, PropWindow, PropSmallWindow, PropRoof })
            try { r[m] = ModelPreview.Load(g, m, Plain(), true, false, false, false); } catch (Exception) { }
        return r;
    }

    /// <summary>A game model's own parts standing with its origin at x, y, z, turned by <paramref name="turn"/> around it;
    /// what lies under the origin (a house's cellar wall) is cut off.</summary>
    internal static IEnumerable<PreviewPart> Stand(PreviewScene s, float x, float y, float z, float turn = 0) =>
        s.Parts.Where(p => p.Kind == PartKind.Solid).Select(p => CutBelow(Moved(p, (x, y, z), turn, default, 0), z)).Where(p => p.Tri.Length > 0);

    /// <summary>The part with everything under height <paramref name="g"/> cut away: triangles that cross it are cut along
    /// it (the floor draws nothing, so a cellar wall reaching into the ground would show under it).</summary>
    static PreviewPart CutBelow(PreviewPart p, float g)
    {
        bool any = false;
        for (int i = 2; i < p.P.Length; i += 3) if (p.P[i] < g) { any = true; break; }
        if (!any) return p;
        var pos = new List<float>(p.P);
        var nor = p.N != null && p.N.Length == p.P.Length ? new List<float>(p.N) : null;
        var uv = p.Uv != null && p.Uv.Length / 2 == p.P.Length / 3 ? new List<float>(p.Uv) : null;
        var col = p.Col != null && p.Col.Length / 4 == p.P.Length / 3 ? new List<byte>(p.Col) : null;
        var tri = new List<int>(p.Tri.Length);
        int Between(int a, int b)
        {
            float za = pos[a * 3 + 2], zb = pos[b * 3 + 2], t = (g - za) / (zb - za);
            int n = pos.Count / 3;
            for (int k = 0; k < 3; k++) pos.Add(pos[a * 3 + k] + (pos[b * 3 + k] - pos[a * 3 + k]) * t);
            if (nor != null) for (int k = 0; k < 3; k++) nor.Add(nor[a * 3 + k] + (nor[b * 3 + k] - nor[a * 3 + k]) * t);
            if (uv != null) for (int k = 0; k < 2; k++) uv.Add(uv[a * 2 + k] + (uv[b * 2 + k] - uv[a * 2 + k]) * t);
            if (col != null) for (int k = 0; k < 4; k++) col.Add((byte)(col[a * 4 + k] + (col[b * 4 + k] - col[a * 4 + k]) * t));
            return n;
        }
        var poly = new List<int>(4);
        for (int t = 0; t + 2 < p.Tri.Length; t += 3)
        {
            poly.Clear();
            for (int e = 0; e < 3; e++)
            {
                int a = p.Tri[t + e], b = p.Tri[t + (e + 1) % 3];
                bool ain = pos[a * 3 + 2] >= g, bin = pos[b * 3 + 2] >= g;
                if (ain) poly.Add(a);
                if (ain != bin) poly.Add(Between(a, b));
            }
            for (int k = 1; k + 1 < poly.Count; k++) tri.AddRange(new[] { poly[0], poly[k], poly[k + 1] });
        }
        return new PreviewPart
        {
            Kind = p.Kind, Name = p.Name, P = pos.ToArray(), N = nor?.ToArray() ?? p.N, Uv = uv?.ToArray() ?? p.Uv, Col = col?.ToArray() ?? p.Col,
            Tri = tri.ToArray(), Tex = p.Tex, TestRef = p.TestRef, Billboard = p.Billboard, Anchor = p.Anchor, Skinned = p.Skinned, Filled = p.Filled,
        };
    }

    /// <summary>A closed CAMP barn room around min..max, of whole wall panels (3.7 m): a door in the middle of the front
    /// (-y), a big window in the middle of each side, a small one at the back, a flat roof. Half its width and depth.</summary>
    internal static (List<PreviewPart> Parts, float HalfX, float HalfY)? WallsAround(Dictionary<string, PreviewScene>? models, (float X, float Y, float Z) min, (float X, float Y, float Z) max)
    {
        if (models == null || !models.TryGetValue(PropWall, out var wall)) return null;
        float w = wall.Max.X - wall.Min.X, h = wall.Max.Z - wall.Min.Z;
        if (w < 50 || h < 50) return null;
        float size = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)), gap = Math.Max(25, size * 0.15f);
        int nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X + 2 * gap) / w)), ny = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y + 2 * gap) / w));
        int nz = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z + gap) / h));
        float cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2, hx = nx * w / 2, hy = ny * w / 2;
        var parts = new List<PreviewPart>();
        PreviewScene Pick(string model) => models.GetValueOrDefault(model) ?? wall;
        // a panel stands on its origin, centred on it, its outside to -y; turned, it lines another side
        void Side(int n, Func<int, (float X, float Y)> at, float turn, string middle)
        {
            for (int row = 0; row < nz; row++)
                for (int i = 0; i < n; i++)
                {
                    var (x, y) = at(i);
                    parts.AddRange(Stand(row == 0 && i == n / 2 ? Pick(middle) : wall, x, y, min.Z + row * h, turn));
                }
        }
        Side(nx, i => (cx - hx + w * (i + 0.5f), cy - hy), 0, PropDoor);
        Side(nx, i => (cx + hx - w * (i + 0.5f), cy + hy), MathF.PI, PropSmallWindow);
        Side(ny, i => (cx + hx, cy - hy + w * (i + 0.5f)), MathF.PI / 2, PropWindow);
        Side(ny, i => (cx - hx, cy + hy - w * (i + 0.5f)), -MathF.PI / 2, PropWindow);
        if (models.TryGetValue(PropRoof, out var roof))
        {
            float rw = Math.Max(50, roof.Max.X - roof.Min.X), rd = Math.Max(50, roof.Max.Y - roof.Min.Y);
            for (float x = cx - hx + rw / 2; x < cx + hx; x += rw)
                for (float y = cy - hy + rd / 2; y < cy + hy; y += rd)
                    parts.AddRange(roof.Parts.Where(p => p.Kind == PartKind.Solid).Select(p => Moved(p, (x, y, min.Z + nz * h - roof.Min.Z))));
        }
        return (parts, hx, hy);
    }

    /// <summary>Barn wall panels behind the item, seen from the starting camera (the far, +y side, turned to face the item); a small item gets a
    /// small wall (1.6 x its height, at least 30 cm - a 3 m barn wall behind a bobblehead filled the whole picture), big
    /// items the real size. Null without the wall model.</summary>
    internal static List<PreviewPart>? WallBehind(Dictionary<string, PreviewScene>? models, (float X, float Y, float Z) min, (float X, float Y, float Z) max)
    {
        if (models == null || !models.TryGetValue(PropWall, out var wall)) return null;
        float w = wall.Max.X - wall.Min.X, h = wall.Max.Z - wall.Min.Z;
        if (w < 50 || h < 50) return null;
        float size = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        float k = Math.Min(1f, Math.Max(21f, (max.Z - min.Z) * 1.6f) / h), sw = w * k, sh = h * k, gap = Math.Max(10, size * 0.4f);
        int nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X + 2 * gap) / sw)), nz = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z + gap) / sh));
        float cx = (min.X + max.X) / 2, hx = nx * sw / 2, y = max.Y + gap;
        var parts = new List<PreviewPart>();
        for (int row = 0; row < nz; row++)
            for (int i = 0; i < nx; i++)
            {
                float px = cx - hx + sw * (i + 0.5f), pz = min.Z + row * sh;
                foreach (var p in Stand(wall, px, y, pz, MathF.PI))
                {
                    if (k < 1) // shrunk around the panel's foot
                    {
                        var q = new float[p.P.Length];
                        for (int v = 0; v + 2 < q.Length; v += 3)
                            (q[v], q[v + 1], q[v + 2]) = (px + (p.P[v] - px) * k, y + (p.P[v + 1] - y) * k, pz + (p.P[v + 2] - pz) * k);
                        p.P = q;
                    }
                    parts.Add(p);
                }
            }
        return parts;
    }

    /// <summary>The height of a model's ground floor at (x, y), in its own units: the highest surface there up to 2.1 m
    /// above the ground (its origin, or its bottom when that is higher) - the foundation's top / the floor boards, not a
    /// cellar floor below the ground; its bottom when nothing is there.</summary>
    static float FloorAt(PreviewScene s, float x, float y)
    {
        float best = float.NaN, limit = Math.Max(0, s.Min.Z) + 150;
        foreach (var p in s.Parts.Where(p => p.Kind == PartKind.Solid))
            for (int t = 0; t + 2 < p.Tri.Length; t += 3)
            {
                int a = p.Tri[t] * 3, b = p.Tri[t + 1] * 3, c = p.Tri[t + 2] * 3;
                float ax = p.P[a], ay = p.P[a + 1], bx = p.P[b], by = p.P[b + 1], cx = p.P[c], cy = p.P[c + 1];
                float d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
                if (MathF.Abs(d) < 1e-6f) continue; // upright: not a floor
                float u = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / d, v = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / d;
                if (u < 0 || v < 0 || u + v > 1) continue;
                float z = u * p.P[a + 2] + v * p.P[b + 2] + (1 - u - v) * p.P[c + 2];
                if (z <= limit && (float.IsNaN(best) || z > best)) best = z;
            }
        return float.IsNaN(best) ? s.Min.Z : best;
    }

    /// <summary>A copy of a part turned by <paramref name="turn"/> around the upright line through <paramref name="pivot"/>,
    /// then moved; triangles wholly under <paramref name="ground"/> (a plant's buried half) are left out.</summary>
    static PreviewPart Moved(PreviewPart p, (float X, float Y, float Z) by, float turn = 0, (float X, float Y) pivot = default, float ground = float.NaN)
    {
        float c = MathF.Cos(turn), s = MathF.Sin(turn);
        (float, float) T(float x, float y) => turn == 0 ? (x, y) : (pivot.X + (x - pivot.X) * c - (y - pivot.Y) * s, pivot.Y + (x - pivot.X) * s + (y - pivot.Y) * c);
        var q = new PreviewPart
        {
            Kind = p.Kind, Name = p.Name, N = p.N, Uv = p.Uv, Col = p.Col, Tri = p.Tri, Tex = p.Tex, TestRef = p.TestRef,
            Billboard = p.Billboard, Skinned = p.Skinned, Filled = p.Filled, P = p.P,
        };
        if (p.Billboard)
        {
            var (ax, ay) = T(p.Anchor.X, p.Anchor.Y);
            q.Anchor = (ax + by.X, ay + by.Y, p.Anchor.Z + by.Z);
            return q;
        }
        q.P = new float[p.P.Length];
        for (int i = 0; i + 2 < p.P.Length; i += 3)
        {
            var (x, y) = T(p.P[i], p.P[i + 1]);
            q.P[i] = x + by.X; q.P[i + 1] = y + by.Y; q.P[i + 2] = p.P[i + 2] + by.Z;
        }
        if (turn != 0 && p.N != null)
        {
            q.N = new float[p.N.Length];
            for (int i = 0; i + 2 < p.N.Length; i += 3)
            {
                q.N[i] = p.N[i] * c - p.N[i + 1] * s; q.N[i + 1] = p.N[i] * s + p.N[i + 1] * c; q.N[i + 2] = p.N[i + 2];
            }
        }
        if (!float.IsNaN(ground))
        {
            float g = ground + by.Z;
            var keep = new List<int>(p.Tri.Length);
            for (int t = 0; t + 2 < p.Tri.Length; t += 3)
                if (q.P[p.Tri[t] * 3 + 2] >= g || q.P[p.Tri[t + 1] * 3 + 2] >= g || q.P[p.Tri[t + 2] * 3 + 2] >= g)
                    keep.AddRange(new[] { p.Tri[t], p.Tri[t + 1], p.Tri[t + 2] });
            q.Tri = keep.ToArray();
        }
        return q;
    }

    /// <summary>The compared items in a row to the right of this one (on its floor, centred on its depth, real sizes),
    /// and the junk lying around it. Redone only when the item, the list or "Apply look" changed.</summary>
    void Place()
    {
        var others = compares.Where(c => c.Item.Entry != entry).ToList();
        string key = scene == null ? "" : $"{scene.Min}{scene.Max}|{lookOn.Checked}|{string.Join(",", others.Select(o => o.Item.Entry.Id))}|{addJunk.Checked}{junkScenes?.Count}|{entry?.Id}";
        if (key == placedKey) return;
        placedKey = key;
        placed.Clear();
        extras.Others.Clear();
        junkPlaced = new();
        if (scene == null) return;
        var (min, max) = (scene.Min, scene.Max);
        float cy = (min.Y + max.Y) / 2;
        float biggest = others.Select(o => Math.Max(o.Scene.Max.X - o.Scene.Min.X, o.Scene.Max.Y - o.Scene.Min.Y)).Append(max.X - min.X).Max();
        float gap = Math.Max(35, biggest * 0.3f), x = max.X + gap;
        (othersMin, othersMax) = (min, max);
        // with "Original" only the game's own model (no glow, beam or icon)
        bool Own(PreviewPart p) => lookOn.Checked || p.Kind is PartKind.Solid or PartKind.Blend or PartKind.Effect;
        foreach (var (item, s) in others)
        {
            var by = (x - s.Min.X, cy - (s.Min.Y + s.Max.Y) / 2, min.Z - s.Min.Z);
            var parts = s.Parts.Where(Own).Select(p => Moved(p, by, ground: s.Ground)).ToList();
            float w = s.Max.X - s.Min.X;
            placed.Add((parts, item.Entry.Name, (x + w / 2, cy, s.Max.Z + by.Item3)));
            extras.Others.Add((parts, lookOn.Checked ? item.Look : Plain()));
            othersMax = (Math.Max(othersMax.X, x + w), Math.Max(othersMax.Y, cy + (s.Max.Y - s.Min.Y) / 2), Math.Max(othersMax.Z, s.Max.Z + by.Item3));
            othersMin = (othersMin.X, Math.Min(othersMin.Y, cy - (s.Max.Y - s.Min.Y) / 2), othersMin.Z);
            x += w + gap;
        }
        if (!anim.Enabled && extras.Others.Any(o => o.Look.Moves || o.Look.Timed)) Animate(); // a compared item's look moves
        if (addJunk.Checked && junkScenes != null)
        {
            // a few game items lying around, each turned its own way, outside the item's footprint
            var rnd = new Random(7 + junkScenes.Count);
            float cx = (min.X + max.X) / 2;
            float inner = 0.5f * MathF.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y)) + 20;
            foreach (var j in junkScenes)
            {
                float a = (float)(rnd.NextDouble() * MathF.Tau), r = inner + 15 + (float)rnd.NextDouble() * Math.Max(90, inner);
                var jc = ((j.Min.X + j.Max.X) / 2, (j.Min.Y + j.Max.Y) / 2);
                var by = (cx + r * MathF.Cos(a) - jc.Item1, cy + r * MathF.Sin(a) - jc.Item2, min.Z - j.Min.Z);
                junkPlaced.AddRange(j.Parts.Where(p => p.Kind == PartKind.Solid).Select(p => Moved(p, by, (float)(rnd.NextDouble() * MathF.Tau), jc, j.Ground)));
            }
        }
    }

    /// <summary>The Add menu's lower half: "Other items...", each compared item (click removes it), "Remove all", labels.</summary>
    void FillAddMenu()
    {
        while (addMenu.Items.Count > 4) addMenu.Items.RemoveAt(4);
        addMenu.Items.Add(new ToolStripSeparator());
        var pick = new ToolStripMenuItem("Compare with other items...", null, (_, _) => { addMenu.Close(); BeginInvoke(PickOthers); });
        pick.Enabled = TickedItems != null;
        addMenu.Items.Add(pick);
        foreach (var c in compares.ToList())
        {
            var it = new ToolStripMenuItem("    ✕  " + c.Item.Entry.Name) { ToolTipText = "Remove it from the picture" };
            it.Click += (_, _) => { compares.Remove(c); addMenu.Items.Remove(it); MakeGuide(); Redraw(); };
            addMenu.Items.Add(it);
        }
        if (compares.Count > 0)
        {
            addMenu.Items.Add(new ToolStripMenuItem("    Remove all", null, (_, _) => { compares.Clear(); addMenu.Close(); MakeGuide(); Redraw(); }));
            addMenu.Items.Add(hideLabels);
        }
    }

    /// <summary>Selected items to stand beside this one: search, a category, the look's colour; up to 12.</summary>
    void PickOthers()
    {
        if (TickedItems == null || game == null) return;
        var all = TickedItems().Where(c => c.Entry != entry).ToList();
        if (all.Count == 0) { MessageBox.Show(FindForm(), "Select some other items first (tick them in the list) - then they can stand beside this one.", "Compare"); return; }
        var on = compares.Select(c => c.Item.Entry.Id).ToHashSet();
        using var dlg = Ui.Dialog(new Form
        {
            Text = "Compare with other items", StartPosition = FormStartPosition.CenterParent, Size = new Size(620, 560), MinimumSize = new Size(480, 360),
            Font = Font, ShowIcon = false, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
        });
        var search = new TextBox { PlaceholderText = "Search", Width = 200, Margin = new Padding(3, 5, 3, 3) };
        var cat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(3, 5, 3, 3) };
        cat.Items.Add("All categories");
        cat.Items.AddRange(all.Select(c => c.Category).Distinct().OrderBy(c => c).Cast<object>().ToArray());
        cat.SelectedIndex = 0;
        var oneEach = new Button { Text = "One of each look", AutoSize = true };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        top.Controls.AddRange(new Control[] { search, cat, oneEach });
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        list.Columns.Add("Item", 300);
        list.Columns.Add("Category", 220);
        var dots = new ImageList { ImageSize = new Size(14, 14), ColorDepth = ColorDepth.Depth32Bit };
        list.SmallImageList = dots;
        static string Colour(Style s) => s.Glow ? s.GlowColor : s.Beam ? s.BeamColor : s.GlowColor;
        string Dot(Style s)
        {
            var k = Colour(s);
            if (!dots.Images.ContainsKey(k))
            {
                var b = new Bitmap(14, 14);
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using var br = new SolidBrush(Materials.Parse(k));
                    g.FillEllipse(br, 1, 1, 12, 12);
                    g.DrawEllipse(Pens.Gray, 1, 1, 12, 12);
                }
                dots.Images.Add(k, b);
            }
            return k;
        }
        var count = new Label { AutoSize = true, Padding = new Padding(0, 9, 12, 0) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        buttons.Controls.AddRange(new Control[] { cancel, ok, count });
        void Count() => count.Text = $"{on.Count} of at most {MaxCompare} picked";
        bool refill = false;
        void Fill()
        {
            refill = true;
            list.BeginUpdate();
            list.Items.Clear();
            string q = search.Text.Trim();
            foreach (var c in all.Where(c => (cat.SelectedIndex <= 0 || c.Category == (string)cat.SelectedItem!) &&
                                             (q.Length == 0 || c.Entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).OrderBy(c => c.Entry.Name))
                list.Items.Add(new ListViewItem(new[] { c.Entry.Name, c.Category }) { Tag = c, Checked = on.Contains(c.Entry.Id), ImageKey = Dot(c.Look) });
            list.EndUpdate();
            refill = false;
            Count();
        }
        list.ItemCheck += (_, e) =>
        {
            if (refill || list.Items[e.Index].Tag is not CompareItem c) return;
            if (e.NewValue == CheckState.Checked && !on.Contains(c.Entry.Id) && on.Count >= MaxCompare) { e.NewValue = CheckState.Unchecked; count.Text = $"At most {MaxCompare} - untick one first"; return; }
            if (e.NewValue == CheckState.Checked) on.Add(c.Entry.Id); else on.Remove(c.Entry.Id);
            BeginInvoke(Count);
        };
        oneEach.Click += (_, _) =>
        {
            // one item for each look not shown yet (by colour and kind), from what the list shows now
            var looks = all.Where(c => on.Contains(c.Entry.Id)).Select(c => System.Text.Json.JsonSerializer.Serialize(c.Look, Json.Options)).ToHashSet();
            foreach (ListViewItem i in list.Items)
                if (on.Count < MaxCompare && i.Tag is CompareItem c && looks.Add(System.Text.Json.JsonSerializer.Serialize(c.Look, Json.Options)))
                    on.Add(c.Entry.Id);
            Fill();
        };
        search.TextChanged += (_, _) => Fill();
        cat.SelectedIndexChanged += (_, _) => Fill();
        dlg.Controls.Add(list);
        dlg.Controls.Add(top);
        dlg.Controls.Add(buttons);
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        Fill();
        Ui.Apply(dlg);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        compares.RemoveAll(c => !on.Contains(c.Item.Entry.Id));
        AddOthers(all.Where(c => on.Contains(c.Entry.Id) && compares.All(k => k.Item.Entry.Id != c.Entry.Id)).ToList());
    }

    /// <summary>The model the small preview shows first: the whole gun, else the one carrying the beam, else the biggest.</summary>
    static PreviewScene LoadMain(GameData g, Entry e, Style st, IReadOnlySet<string> off, bool keep)
    {
        var models = e.Models.Where(m => !off.Contains(m)).ToList();
        if (models.Count == 0) models = e.Models;
        if (e.Assembled && models.Count > 1) return ModelPreview.LoadAll(g, models, st, e.NoIcon, e.Harvestable, true, keep);
        var main = e.BeamModels?.FirstOrDefault(models.Contains) ?? models.OrderByDescending(m => g.Find(@"meshes\" + m)?.Size ?? 0).First();
        return ModelPreview.LoadAs(g, e, main, st, true, keep);
    }

    async void AddOthers(List<CompareItem> add)
    {
        if (game == null) { MakeGuide(); Redraw(); return; }
        if (add.Count == 0) { MakeGuide(); Redraw(); return; }
        var (g, keep) = (game, keepOtherMods);
        var old = message;
        message = $"Loading {add.Count} item(s)...";
        canvas.Invalidate();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var got = await Task.Run(() =>
        {
            var r = new List<(CompareItem, PreviewScene)>();
            foreach (var c in add) // one by one: the texture cache isn't shared safely
                try { r.Add((c, LoadMain(g, c.Entry, c.Look, c.Off, keep))); } catch (Exception) { }
            return r;
        });
        compares.AddRange(got);
        message = old;
        if (got.Count < add.Count) message = $"{add.Count - got.Count} of the items can't be shown in 3D." + (old.Length > 0 ? "\n" + old : "");
        System.Diagnostics.Debug.WriteLine($"compare: {got.Count} loaded in {clock.ElapsedMilliseconds} ms");
        MakeGuide();
        Redraw();
    }

    /// <summary>"Scatter junk": a few plain game items (always the same ones), loaded once.</summary>
    async void LoadJunk()
    {
        if (junkScenes != null || junkLoading || game == null || JunkItems == null) return;
        junkLoading = true;
        var g = game;
        var pool = JunkItems().ToList();
        var rnd = new Random(76);
        var pick = pool.OrderBy(_ => rnd.Next()).Take(10).ToList();
        junkScenes = await Task.Run(() =>
        {
            var r = new List<PreviewScene>();
            foreach (var e in pick)
            {
                if (r.Count == 6) break;
                try { var s = LoadMain(g, e, Plain(), new HashSet<string>(), false); if (s.Max.Z - s.Min.Z < 120) r.Add(s); } catch (Exception) { } // small things only
            }
            return r;
        });
        junkLoading = false;
        MakeGuide();
        Redraw();
    }

    /// <summary>"Spin": a slow turn around the item (about 20 s a round).</summary>
    void SpinStep()
    {
        float dt = Math.Min(0.1f, (float)spinClock.Elapsed.TotalSeconds);
        spinClock.Restart();
        if (drag != null || scene == null) return;
        float a = 0.3f * dt;
        if (free)
        {
            var c = ItemCenter;
            float dx = eye.X - c.X, dy = eye.Y - c.Y, co = MathF.Cos(a), si = MathF.Sin(a);
            eye = (c.X + dx * co - dy * si, c.Y + dx * si + dy * co, eye.Z);
            yaw += a;
            MakeGuide();
        }
        else yaw += a;
        Redraw();
    }

    /// <summary>The picture as it is now (with the name labels) as a PNG.</summary>
    void SavePicture()
    {
        if (scene == null) return;
        if (frame == null || frame.Width != canvas.Width) { Redraw(); canvas.Update(); } // sharp
        if (frame == null) return;
        string name = string.Concat((entry?.Name ?? "item").Split(Path.GetInvalidFileNameChars())).Trim();
        using var dlg = new SaveFileDialog { Filter = "PNG picture|*.png", FileName = (name.Length > 0 ? name : "item") + ".png" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            using var bmp = new Bitmap(frame.Width, frame.Height);
            using (var g = Graphics.FromImage(bmp)) { g.DrawImageUnscaled(frame, 0, 0); DrawLabels(g); }
            bmp.Save(dlg.FileName, ImageFormat.Png);
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), "Can't save the picture: " + ex.Message, "Save picture"); }
    }

    /// <summary>The names floating above the items when others stand beside this one.</summary>
    void DrawLabels(Graphics g)
    {
        if (placed.Count == 0 || hideLabels.Checked || scene == null || entry == null) return;
        var all = placed.Select(p => (p.Name, p.Top)).Prepend((entry.Name, ((scene.Min.X + scene.Max.X) / 2, (scene.Min.Y + scene.Max.Y) / 2, scene.Max.Z)));
        foreach (var (name, top) in all)
        {
            if (PreviewRenderer.ToScreen(shotEye, shotLook, 40, canvas.Width, canvas.Height, (top.Item1, top.Item2, top.Item3 + 6)) is not PointF at) continue;
            var sz = TextRenderer.MeasureText(g, name, Font);
            var box = new Rectangle((int)at.X - sz.Width / 2 - 4, (int)at.Y - sz.Height - 8, sz.Width + 8, sz.Height + 4);
            using (var band = new SolidBrush(Color.FromArgb(170, 20, 22, 26))) g.FillRectangle(band, box);
            TextRenderer.DrawText(g, name, Font, new Point(box.X + 4, box.Y + 2), Color.Gainsboro);
        }
    }

    /// <summary>Where you stand: look straight at the item.</summary>
    void FaceItem()
    {
        var c = ItemCenter;
        yaw = MathF.Atan2(c.Y - eye.Y, c.X - eye.X);
        pitch = MathF.Atan2(c.Z - eye.Z, MathF.Sqrt((c.X - eye.X) * (c.X - eye.X) + (c.Y - eye.Y) * (c.Y - eye.Y)));
    }

    /// <summary>Game units as cm or m (1 unit ≈ 1.43 cm).</summary>
    static string Len(float units)
    {
        float cm = units * 1.43f;
        return cm >= 100 ? $"{cm / 100:0.#} m" : $"{Math.Max(1, cm):0} cm";
    }

    string SizeText()
    {
        if (scene == null) return "";
        var (a, b) = (scene.Min, scene.Max);
        string t = $"Item {Len(b.X - a.X)} × {Len(b.Y - a.Y)}, {Len(b.Z - a.Z)} high" + (float.IsNaN(scene.Ground) ? "" : " above the ground (the rest is buried)");
        if (shown.Beam && !shown.BeamArrow) t += $" · beam {Len(shown.BeamLength)}";
        if (addPerson.Checked && guide != null) t += " · person 1.8 m";
        if (addHouse.Checked && guide != null) t += " · house 8.4 m";
        if (guide != null) t += $" · grid {Len(guide.Step)} squares";
        if (free)
        {
            var c = ItemCenter;
            t += $" · you: {Len(MathF.Sqrt((eye.X - c.X) * (eye.X - c.X) + (eye.Y - c.Y) * (eye.Y - c.Y)))} away, eyes {Len(eye.Z - a.Z)} up";
        }
        return t;
    }

    void Redraw()
    {
        frame?.Dispose();
        frame = null;
        canvas.Invalidate();
        UpdateBar();
    }

    void PaintCanvas(object? sender, PaintEventArgs pe)
    {
        var g = pe.Graphics;
        if (scene != null && frame == null && canvas.Width > 16 && canvas.Height > 16)
        {
            // the item with room above it for an arrow / icon / name - the same for every look, so switching a part on or
            // off doesn't move the camera (user, 2026-11-09: it zoomed out for an arrow and ended up too close after)
            var (min, max) = camView == CamView.Whole ? (scene.AllMin, scene.AllMax) : (scene.Min, scene.Max);
            if (camView != CamView.Whole)
            {
                float size = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
                max.Z += Math.Clamp(size * 0.4f, 10f, 60f);
            }
            // it turns around the item, framed on the item alone: adding or removing a house, person, wall or compared item
            // never moves the camera (user, 2026-11-09: no auto zoom - use the wheel)
            var target = ((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
            float fx = (max.X - min.X) / 2, fy = (max.Y - min.Y) / 2, fz = (max.Z - min.Z) / 2;
            float radius = Math.Max(1f, MathF.Sqrt(fx * fx + fy * fy + fz * fz));
            float dist = radius / MathF.Sin(18 * MathF.PI / 180) / zoom;
            // a big picture is drawn at half size while it turns, then sharp
            (orbitAt, orbitDist) = (target, dist); // walking starts from here
            // (coarser with several items beside it)
            // (spinning: only with several items - one item spins sharp)
            int s = (drag != null || walk.Enabled) && (canvas.Width * canvas.Height > 250_000 || placed.Count > 3)
                    || spin.Enabled && placed.Count > 3 ? (placed.Count > 6 ? 3 : 2) : 1;
            try
            {
                // free: you stand somewhere, looking where you turn (the same lens as the turning camera: no jump)
                var (at, look) = free ? (eye, (MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch)))
                                      : PreviewRenderer.Orbit(yaw, pitch, target, dist);
                (shotEye, shotLook) = (at, look);
                var took = System.Diagnostics.Stopwatch.StartNew();
                frame = PreviewRenderer.Render(scene, shown, canvas.Width / s, canvas.Height / s, at, look, 40, free ? 2 : Math.Max(0.5f, dist * 0.005f), guide,
                                               isBig ? extras : null, anim.Enabled ? (float)animClock.Elapsed.TotalSeconds : -1);
                // animating: full quality, fewer pictures a second when one takes long (a big view)
                if (anim.Enabled && s == 1) anim.Interval = Math.Clamp((int)(took.ElapsedMilliseconds * 1.5), 50, 400);
            }
            catch (Exception ex) { message = "Can't draw this model: " + ex.Message; }
        }
        if (frame != null && frame.Width == canvas.Width) g.DrawImageUnscaled(frame, 0, 0);
        else if (frame != null)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(frame, 0, 0, canvas.Width, canvas.Height);
        }
        else g.Clear(canvas.BackColor);
        if (scene != null && isBig) DrawLabels(g);
        if (scene != null && isBig)
        {
            var size = SizeText();
            var sz = TextRenderer.MeasureText(g, size, Font);
            using (var band = new SolidBrush(Color.FromArgb(160, 20, 22, 26))) g.FillRectangle(band, 4, canvas.Height - sz.Height - 10, sz.Width + 8, sz.Height + 6);
            TextRenderer.DrawText(g, size, Font, new Point(8, canvas.Height - sz.Height - 7), Color.Gainsboro);
        }
        int top = 4;
        if (scene != null && isBig)
        {
            DrawLegend(g);
            top = legendBox.Bottom + 6;
        }
        else legendBox = Rectangle.Empty;
        barHits.Clear();
        barBottom = 0;
        if (isBig) { DrawBar(g); if (barBottom > 0 && legendBox == Rectangle.Empty) top = barBottom + 6; }
        if (message.Length > 0)
        {
            // on a dark band, readable over a bright beam
            var box = new Rectangle(8, top + 4, canvas.Width - 16, Math.Max(20, canvas.Height - top - 12));
            var size = TextRenderer.MeasureText(g, message, Font, box.Size, TextFormatFlags.WordBreak);
            using (var band = new SolidBrush(Color.FromArgb(190, 20, 22, 26))) g.FillRectangle(band, 4, top, size.Width + 8, size.Height + 8);
            TextRenderer.DrawText(g, message, Font, box, Color.Gainsboro, TextFormatFlags.WordBreak);
        }
    }

    /// <summary>The "View options" box is open (remembered while the app runs).</summary>
    static bool optionsOpen;
    static readonly string[] VisionShort = { "Normal", "Deuteranopia", "Protanopia", "Tritanopia" };

    /// <summary>The toolbar on the picture, top right: Show the look (lit while on), Save, any extra button and "View
    /// options", which opens a box below with every camera / scene / light / colour setting as one-click buttons
    /// (lit = on; user, 2026-11-09: the drop-down menus took two clicks for everything).</summary>
    void DrawBar(Graphics g)
    {
        const int pad = 9, gap = 5;
        int h = Font.Height + 8;
        void Chip(Rectangle r, string text, bool lit, string tip, Action<Rectangle> click)
        {
            bool hover = r == barHover;
            using (var band = new SolidBrush(Color.FromArgb(hover ? 235 : 200, hover ? 44 : 20, hover ? 48 : 22, hover ? 56 : 26))) g.FillRectangle(band, r);
            using (var edge = new Pen(lit ? Color.FromArgb(240, 200, 120) : Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(edge, r);
            TextRenderer.DrawText(g, text, Font, r, lit ? Color.FromArgb(240, 200, 120) : Color.Gainsboro,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            barHits.Add((r, tip, click));
        }
        int W(string t) => TextRenderer.MeasureText(g, t, Font).Width + 2 * pad;

        // the top row, right-aligned
        var top = new List<(string Text, bool Lit, string Tip, Action<Rectangle> Click)>
        {
            ((lookOn.Checked ? "● " : "○ ") + "Show the look", lookOn.Checked, "On: the item with your look. Off: the original game model, to compare (preview only)", _ => lookOn.Checked = !lookOn.Checked),
            ("Save picture...", false, "Save the picture as it is now as a PNG file", _ => SavePicture()),
        };
        foreach (var (text, click, tip) in extraTools) top.Add((text(), false, tip, _ => click()));
        top.Add((optionsOpen ? "View options  ▴" : "View options  ▾", optionsOpen, "Camera, scene, light and colours", _ => optionsOpen = !optionsOpen));
        int left = legendBox == Rectangle.Empty ? 6 : legendBox.Right + 12, y = 6;
        int rowW = top.Sum(t => W(t.Text)) + gap * (top.Count - 1);
        if (canvas.Width - 6 - left < rowW) { left = 6; y = legendBox.Bottom + 6; } // no room beside the controls box: under it
        int x = Math.Max(left, canvas.Width - 6 - rowW);
        foreach (var t in top)
        {
            int w = W(t.Text);
            if (x + w > canvas.Width - 4 && x > left) { x = left; y += h + gap; }
            Chip(new Rectangle(x, y, w, h), t.Text, t.Lit, t.Tip, t.Click);
            x += w + gap;
        }
        y += h;
        barBottom = y;
        if (!optionsOpen) return;

        // the options box: a group name on the left, its buttons wrapping on the right
        var groups = new List<(string Name, List<(string Text, bool Lit, string Tip, Action<Rectangle> Click)> Items)>
        {
            ("Camera", new()
            {
                ("Reset", false, "Back to turning around the item (or double-click the picture)", _ => ResetView()),
                ("10 m", false, "Stand 10 m from the item, eyes at 1.6 m, facing it", _ => StandBack(700)),
                ("30 m", false, "Stand 30 m from the item, eyes at 1.6 m, facing it", _ => StandBack(2100)),
                ("100 m", false, "Stand 100 m from the item, eyes at 1.6 m, facing it", _ => StandBack(7000)),
                ("Spin", spinOn.Checked, "Turn slowly around the item", _ => spinOn.Checked = !spinOn.Checked),
            }),
            ("Scene", new()
            {
                ("Person", addPerson.Checked, "A person and a floor grid, for size (1.8 m)", _ => addPerson.Checked = !addPerson.Checked),
                ("House", addHouse.Checked, "Put the item inside a house, on its floor - how it glows inside a building", _ => addHouse.Checked = !addHouse.Checked),
                ("Wall behind", addWall.Checked, "One wall behind the item - turn the view round to see your look through it (X-ray)", _ => addWall.Checked = !addWall.Checked),
                ("Shed", addShed.Checked, "Walls all around it, with a door, windows and a roof - to test X-ray", _ => addShed.Checked = !addShed.Checked),
                ("Junk", addJunk.Checked, "Rubble, grass and a few game items scattered around it", _ => addJunk.Checked = !addJunk.Checked),
                ("Compare...", compares.Count > 0, "Stand other selected items beside it, each in its own look", _ => BeginInvoke(PickOthers)),
            }),
            ("Light", SkyNames.Select((n, i) => (n, (int)extras.Sky == i, i == 0 ? "Even studio light" : "The sky " + n.ToLowerInvariant() + ": sky colour, light level and haze",
                                                  (Action<Rectangle>)(_ => { extras.Sky = (Sky)i; Redraw(); UpdateBar(); }))).ToList()),
            ("Colours", VisionShort.Select((n, i) => (n, (int)extras.Vision == i, i == 0 ? "Normal colour vision" : "See it as someone with " + VisionNames[i] + " does - does your colour still stand out?",
                                                     (Action<Rectangle>)(_ => { extras.Vision = (Vision)i; Redraw(); UpdateBar(); }))).ToList()),
        };
        if (whole.Enabled) groups[0].Items.Add(("Whole beam", whole.Checked, "Move back so the whole tall beam fits in the picture", _ => whole.Checked = !whole.Checked));
        if (compares.Count > 0)
        {
            groups[1].Items.Add(("Hide labels", hideLabels.Checked, "Hide the compared items' name labels", _ => hideLabels.Checked = !hideLabels.Checked));
            foreach (var c in compares.ToList())
                groups[1].Items.Add(("✕ " + c.Item.Entry.Name, false, "Remove it from the picture", _ => { compares.Remove(c); MakeGuide(); Redraw(); }));
        }
        using var bold = new Font(Font, FontStyle.Bold);
        int nameW = groups.Max(gr => TextRenderer.MeasureText(g, gr.Name, bold).Width) + 12;
        int boxW = Math.Min(canvas.Width - 12, 470), inner = boxW - 2 * pad - nameW;
        // lay it out first (for the box's height), then draw
        var spots = new List<(Rectangle R, string Text, bool Lit, string Tip, Action<Rectangle> Click)>();
        var names = new List<(string Name, int Y)>();
        int bx = canvas.Width - 6 - boxW, by = y + gap, cy = by + pad;
        foreach (var (name, items) in groups)
        {
            names.Add((name, cy));
            int cx = 0;
            foreach (var it in items)
            {
                int w = Math.Min(W(it.Text), inner);
                if (cx > 0 && cx + w > inner) { cx = 0; cy += h + gap; }
                spots.Add((new Rectangle(bx + pad + nameW + cx, cy, w, h), it.Text, it.Lit, it.Tip, it.Click));
                cx += w + gap;
            }
            cy += h + gap + 4;
        }
        var box = new Rectangle(bx, by, boxW, cy - by + pad - gap - 4);
        using (var band = new SolidBrush(Color.FromArgb(215, 20, 22, 26))) g.FillRectangle(band, box);
        using (var edge = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(edge, box);
        barHits.Add((box, "", _ => { })); // a click on the box's background does nothing (no turning)
        foreach (var (name, ny) in names)
            TextRenderer.DrawText(g, name, bold, new Rectangle(bx + pad, ny, nameW, h), Color.FromArgb(240, 200, 120), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        foreach (var s in spots) Chip(s.R, s.Text, s.Lit, s.Tip, s.Click);
        barBottom = box.Bottom;
    }

    /// <summary>The controls box (a legend): keys in bold on the left, what they do on the right; folded, just its title.</summary>
    void DrawLegend(Graphics g)
    {
        using var bold = new Font(Font, FontStyle.Bold);
        const int pad = 8, gapCol = 14;
        var title = legendOpen ? "Controls  ▴" : "? Controls";
        var ts = TextRenderer.MeasureText(g, title, bold);
        int lineH = ts.Height + 3, w = ts.Width, h = ts.Height, keyW = 0;
        var rows = legendOpen ? Legend() : Array.Empty<(string Key, string What)>();
        foreach (var (k, _) in rows) keyW = Math.Max(keyW, TextRenderer.MeasureText(g, k, bold).Width);
        foreach (var (_, d) in rows) w = Math.Max(w, keyW + gapCol + TextRenderer.MeasureText(g, d, Font).Width);
        if (rows.Length > 0) h += 6 + rows.Length * lineH;
        legendBox = new Rectangle(4, 4, w + 2 * pad, h + 2 * pad - 4);
        using (var band = new SolidBrush(Color.FromArgb(200, 20, 22, 26))) g.FillRectangle(band, legendBox);
        using (var edge = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(edge, legendBox);
        int x = legendBox.X + pad, y = legendBox.Y + pad - 2;
        TextRenderer.DrawText(g, title, bold, new Point(x, y), Color.Gainsboro);
        y += ts.Height + 6;
        foreach (var (k, d) in rows)
        {
            TextRenderer.DrawText(g, k, bold, new Point(x, y), Color.FromArgb(240, 200, 120));
            TextRenderer.DrawText(g, d, Font, new Point(x + keyW + gapCol, y), Color.Gainsboro);
            y += lineH;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { addMenu.Dispose(); cameraMenu.Dispose(); lightMenu.Dispose(); visionMenu.Dispose(); frame?.Dispose(); reload.Dispose(); walk.Dispose(); spin.Dispose(); anim.Dispose(); hideLabels.Dispose(); }
        base.Dispose(disposing);
    }
}
