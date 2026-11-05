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
    public string Note = "";
}

/// <summary>Builds a picture of an item as the mod would show it: the game's model patched exactly like a build
/// (glow copies, beam tube, icons), with placeholder materials that the renderer colours from the look.</summary>
static class ModelPreview
{
    const string GlowTag = "itemfinder-preview-glow", BeamTag = "itemfinder-preview-beam", IconTag = "itemfinder-preview-icon", Icon3DTag = "itemfinder-preview-icon3d",
                 ModelTag = "itemfinder-preview-model";
    static readonly Dictionary<string, Rgba?> textures = new(StringComparer.OrdinalIgnoreCase);
    const int MaxTexture = 512;

    /// <param name="beamHere">The model carries the beam and icon (an entry's main model).</param>
    public static PreviewScene Load(GameData game, string model, Style st, bool noIcon, bool harvestable, bool beamHere, bool keepOtherMods)
    {
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
                                    beamHere && Models3D.Get(st.Icon) is Model3D m3 ? Models3D.MeshOf(m3) : null, ModelTag);
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
        if (item.Count == 0) throw new InvalidDataException("this model has nothing that can be drawn");
        (scene.Min, scene.Max) = Bounds(item);
        (scene.AllMin, scene.AllMax) = Bounds(scene.Parts);
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
            IconTag => PartKind.Icon,
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
static class PreviewRenderer
{
    public static Bitmap Render(PreviewScene scene, Style st, int w, int h, float yaw, float pitch, (float X, float Y, float Z) target, float dist)
    {
        w = Math.Max(16, w); h = Math.Max(16, h);
        var color = new float[w * h * 3];
        var depth = new float[w * h];
        Array.Fill(depth, float.MaxValue);
        for (int y = 0; y < h; y++)
        {
            float k = 0.10f + 0.10f * y / h; // dark, a little lighter towards the bottom
            for (int x = 0; x < w; x++) { int i = (y * w + x) * 3; color[i] = k * 0.9f; color[i + 1] = k * 0.95f; color[i + 2] = k * 1.05f; }
        }
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw), cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        var eye = (X: target.X + dist * cp * cy, Y: target.Y + dist * cp * sy, Z: target.Z + dist * sp);
        var fwd = Norm((target.X - eye.X, target.Y - eye.Y, target.Z - eye.Z));
        var right = Norm(Cross(fwd, (0, 0, 1)));
        var up = Cross(right, fwd);
        float focal = 0.5f * h / MathF.Tan(20 * MathF.PI / 180); // 40 degree view
        var light = Norm((-fwd.X * 0.5f + up.X * 0.7f - right.X * 0.5f, -fwd.Y * 0.5f + up.Y * 0.7f - right.Y * 0.5f, -fwd.Z * 0.5f + up.Z * 0.7f - right.Z * 0.5f));
        var cam = new Cam(eye, right, up, fwd, focal, w, h, Math.Max(0.5f, dist * 0.005f));

        var glow = Materials.Parse(st.GlowColor);
        var beam = Materials.Parse(st.BeamColor);
        var iconCol = Materials.Parse(st.Beam ? st.BeamColor : st.GlowColor);
        float glowK = Math.Clamp(st.GlowOpacity / 100f, 0.05f, 1f) * 0.36f;
        float gs = Math.Clamp(st.GlowOpacity / 100f, 0.05f, 1f);
        float fillA = Math.Min(1f, 0.28f * gs / 0.3f), outlineA = 0.3f + 0.7f * gs; // fill = 0.28 at 30 %; an outline is stronger at the edges
        float beamA = Math.Min(1f, 0.75f * st.BeamStrength / 100f) * (st.BeamStrength > 100 ? st.BeamStrength / 100f : 1f);
        // the item first (fills the depth buffer), then everything that adds or blends light
        var modelCol = Models3D.Get(st.Icon) is { Tinted: false } ? Color.White : iconCol; // own colours, or the look's
        foreach (var kind in new[] { PartKind.Solid, PartKind.Effect, PartKind.Blend, PartKind.Glow, PartKind.Icon3D, PartKind.Model, PartKind.Beam, PartKind.Icon })
            foreach (var p in scene.Parts.Where(p => p.Kind == kind))
            {
                bool xray = kind is PartKind.Glow or PartKind.Beam or PartKind.Icon or PartKind.Icon3D or PartKind.Model && st.ThroughWalls;
                var col = kind switch { PartKind.Beam => beam, PartKind.Glow => glow, PartKind.Model => modelCol, _ => iconCol };
                bool fill = st.GlowFill || p.Filled;
                Draw(p, cam, color, depth, light, col, kind switch { PartKind.Glow => glowK, PartKind.Beam => beamA, _ => 1f }, xray, fill ? fillA : outlineA, !fill);
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

    readonly record struct Cam((float X, float Y, float Z) Eye, (float X, float Y, float Z) Right, (float X, float Y, float Z) Up, (float X, float Y, float Z) Fwd, float Focal, int W, int H, float Near);

    static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    static (float X, float Y, float Z) Norm((float X, float Y, float Z) a)
    {
        float l = MathF.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
        return l > 1e-9f ? (a.X / l, a.Y / l, a.Z / l) : (0, 0, 1);
    }

    static void Draw(PreviewPart p, Cam c, float[] color, float[] depth, (float X, float Y, float Z) light, Color col, float strength, bool xray, float glowA = 0, bool outline = false)
    {
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
            int y0 = Math.Max(0, (int)MathF.Floor(Math.Min(sy0, Math.Min(sy1, sy2)))), y1 = Math.Min(c.H - 1, (int)MathF.Ceiling(Math.Max(sy0, Math.Max(sy1, sy2))));
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
                            float r = tr * vr * lit, g = tg * vg * lit, bl = tb * vb * lit;
                            if (p.Kind == PartKind.Solid) { color[ci] = r; color[ci + 1] = g; color[ci + 2] = bl; depth[pi] = z; }
                            else { color[ci] += (r - color[ci]) * ta; color[ci + 1] += (g - color[ci + 1]) * ta; color[ci + 2] += (bl - color[ci + 2]) * ta; }
                            break;
                        }
                        case PartKind.Effect:
                            color[ci] += tr * ta * vr * 0.7f; color[ci + 1] += tg * ta * vg * 0.7f; color[ci + 2] += tb * ta * vb * 0.7f;
                            break;
                        case PartKind.Glow:
                            if (p.TestRef >= 0) { color[ci] += cr * strength; color[ci + 1] += cg * strength; color[ci + 2] += cb * strength; } // cut-out: added light
                            else
                            {
                                float ga = glowA;
                                if (outline) // clear where the surface faces you, full colour towards the edges
                                {
                                    var vd = Norm((c.Eye.X - wx[a], c.Eye.Y - wy[a], c.Eye.Z - wz[a]));
                                    float fa = MathF.Abs(fn.X * vd.X + fn.Y * vd.Y + fn.Z * vd.Z);
                                    float face = Materials.OutlineFace;
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
                            float k = strength * (0.15f + 0.6f * facing * facing);
                            color[ci] += cr * k; color[ci + 1] += cg * k; color[ci + 2] += cb * k;
                            break;
                        }
                        case PartKind.Icon:
                        {
                            float k = ta * 1.15f;
                            color[ci] += (cr * 1.1f - color[ci]) * Math.Min(1, k); color[ci + 1] += (cg * 1.1f - color[ci + 1]) * Math.Min(1, k); color[ci + 2] += (cb * 1.1f - color[ci + 2]) * Math.Min(1, k);
                            break;
                        }
                        case PartKind.Icon3D:
                            color[ci] += cr * vr * 0.9f; color[ci + 1] += cg * vg * 0.9f; color[ci + 2] += cb * vb * 0.9f;
                            break;
                        case PartKind.Model:
                        {
                            // the material: texture x vertex colour x colour x 1.3, blended at 0.75
                            float k = 0.75f * ta;
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
    readonly Label hint = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText,
                                  Text = "Drag to turn · wheel to zoom · double-click to reset" };
    readonly CheckBox whole = new() { Text = "Show the whole beam", AutoSize = true };
    /// <summary>Preview only: off = the plain game model, to compare with the look (the mod doesn't change).</summary>
    readonly CheckBox lookOn = new() { Text = "Show the look", AutoSize = true, Checked = true };
    readonly FlowLayoutPanel previewRow = new() { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false };
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
    float yaw = -0.9f, pitch = 0.35f, zoom = StartZoom;
    Point? drag;
    int version;
    readonly System.Windows.Forms.Timer reload = new() { Interval = 250 };

    sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

    public ModelView()
    {
        Controls.Add(canvas);
        Controls.Add(partsSplit);
        Controls.Add(partsBox);
        partsBox.VisibleChanged += (_, _) => partsSplit.Visible = partsBox.Visible;
        parts.Resize += (_, _) => FitPartsColumn();
        parts.HandleCreated += (_, _) => BeginInvoke(FitPartsColumn);
        previewRow.Controls.Add(lookOn);
        previewRow.Controls.Add(whole);
        Controls.Add(previewRow);
        Controls.Add(hint);
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
        parts.SelectedIndexChanged += (_, _) => { if (!filling && parts.Created && parts.SelectedItems.Count > 0) { zoom = StartZoom; Load(); } };
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
        canvas.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) drag = e.Location; };
        canvas.MouseUp += (_, _) => drag = null;
        canvas.MouseMove += (_, e) =>
        {
            if (drag is not Point d) return;
            yaw -= (e.X - d.X) * 0.01f;
            pitch = Math.Clamp(pitch + (e.Y - d.Y) * 0.01f, -1.45f, 1.45f);
            drag = e.Location;
            Redraw();
        };
        canvas.MouseWheel += (_, e) => { zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f), 0.02f, 20f); Redraw(); };
        canvas.MouseEnter += (_, _) => canvas.Focus();
        canvas.DoubleClick += (_, _) => { yaw = -0.9f; pitch = 0.35f; zoom = StartZoom; Redraw(); };
        whole.CheckedChanged += (_, _) => { zoom = StartZoom; Redraw(); };
        lookOn.CheckedChanged += (_, _) => Load();
        reload.Tick += (_, _) => { reload.Stop(); Load(); };
    }

    /// <summary>Shows an entry (its main model first) with a look.</summary>
    /// <param name="off">Parts left out of the mod.</param>
    /// <param name="shared">Other entries using a model (for the note when a shared part is unticked).</param>
    /// <param name="ticked">The entry is ticked: unticked, nothing of it lights up (shown plain).</param>
    public void Show(GameData game, Entry e, Style st, bool keepOtherMods, IReadOnlySet<string>? off = null, Func<string, List<string>>? shared = null,
                     bool ticked = true, string? focusPart = null, IReadOnlyDictionary<string, Style>? partLooks = null)
    {
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

    string? Selected() => parts.SelectedItems.Count > 0 ? parts.SelectedItems[0].Tag as string : null;

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

    async void Load()
    {
        if (game == null || entry == null) return;
        var model = parts.SelectedItems.Count > 0 ? parts.SelectedItems[0].Tag as string : entry.Models.FirstOrDefault();
        if (model == null)
        {
            // only recolours a texture (an invisible enemy's shimmer): nothing to turn around
            scene = null;
            message = "Nothing to show in 3D - this one recolours the shimmer of invisible enemies (see Details in the list).";
            whole.Visible = false;
            Redraw();
            return;
        }
        int v = ++version;
        var (g, e, st, keep) = (game, entry, style.Clone(), keepOtherMods);
        bool ownLook = partLooks.TryGetValue(model, out var ownStyle);
        if (ownLook) st = ownStyle!.Clone(); // a part with its own look: that look, with its own beam / icon
        if (!ticked || !lookOn.Checked || parts.SelectedItems.Count > 0 && !parts.SelectedItems[0].Checked) { st.Glow = st.Beam = false; st.Icon = ""; } // as in the game
        bool beamHere = ownLook || e.BeamModels == null || e.BeamModels.Contains(model);
        message = scene == null ? "Loading..." : message;
        canvas.Invalidate();
        try
        {
            // a weapon with no part picked: the whole gun (its parts left in the mod)
            var off = OffParts;
            var gun = e.Assembled && parts.SelectedItems.Count == 0 ? e.Models.Where(m => !off.Contains(m)).ToList() : null;
            var s = await Task.Run(() => gun is { Count: > 0 } ? ModelPreview.LoadAll(g, gun, st, e.NoIcon, e.Harvestable, beamHere, keep)
                                                       : ModelPreview.Load(g, model, st, e.NoIcon, e.Harvestable, beamHere, keep));
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
        whole.Visible = scene != null && st.Beam && !st.BeamArrow;
        whole.Text = "Show the whole beam";
        Redraw();
    }

    void Redraw()
    {
        frame?.Dispose();
        frame = null;
        canvas.Invalidate();
    }

    void PaintCanvas(object? sender, PaintEventArgs pe)
    {
        var g = pe.Graphics;
        if (scene != null && frame == null && canvas.Width > 16 && canvas.Height > 16)
        {
            var (min, max) = frameAll || whole.Checked && whole.Visible ? (scene.AllMin, scene.AllMax) : (scene.Min, scene.Max);
            var target = ((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
            float radius = Math.Max(1f, 0.5f * MathF.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y) + (max.Z - min.Z) * (max.Z - min.Z)));
            float dist = radius / MathF.Sin(18 * MathF.PI / 180) / zoom;
            try { frame = PreviewRenderer.Render(scene, shown, canvas.Width, canvas.Height, yaw, pitch, target, dist); }
            catch (Exception ex) { message = "Can't draw this model: " + ex.Message; }
        }
        if (frame != null) g.DrawImageUnscaled(frame, 0, 0);
        else g.Clear(canvas.BackColor);
        if (message.Length > 0)
        {
            // on a dark band, readable over a bright beam
            var box = new Rectangle(8, 8, canvas.Width - 16, canvas.Height - 16);
            var size = TextRenderer.MeasureText(g, message, Font, box.Size, TextFormatFlags.WordBreak);
            using (var band = new SolidBrush(Color.FromArgb(190, 20, 22, 26))) g.FillRectangle(band, 4, 4, size.Width + 8, size.Height + 8);
            TextRenderer.DrawText(g, message, Font, box, Color.Gainsboro, TextFormatFlags.WordBreak);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { frame?.Dispose(); reload.Dispose(); }
        base.Dispose(disposing);
    }
}
