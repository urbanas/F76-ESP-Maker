using System.Numerics;
using System.Text;

namespace ItemFinder;

/// <summary>Block-level view of a Fallout 76 .nif (Gamebryo 20.2.0.7, Bethesda stream 155). Original
/// blocks are kept byte for byte; new blocks are appended and linked in as extra node children.</summary>
public sealed class NifFile
{
    public int BsVersion;
    public List<(string Type, byte[] Data)> Blocks = new();
    public List<string> Strings = new();

    static readonly HashSet<string> NodeTypes = new()
    {
        "NiNode", "BSFadeNode", "BSLeafAnimNode", "NiSwitchNode", "BSMultiBoundNode", "BSOrderedNode", "BSValueNode",
        "NiBillboardNode", "BSBlastNode", "BSDamageStage", "BSDebrisNode", "NiLODNode", "BSRangeNode", "BSTreeNode", "BSMasterParticleSystem",
    };
    public static readonly HashSet<string> ShapeTypes = new() { "BSTriShape", "BSSubIndexTriShape", "BSMeshLODTriShape" };

    public static NifFile Parse(byte[] d)
    {
        int p = Array.IndexOf(d, (byte)'\n') + 1;
        if (p <= 0 || !Encoding.ASCII.GetString(d, 0, p).StartsWith("Gamebryo File Format, Version 20.2.0.7"))
            throw new InvalidDataException("not a Fallout 76 model");
        p += 4 + 1 + 4;
        int blocks = BitConverter.ToInt32(d, p); p += 4;
        var f = new NifFile { BsVersion = BitConverter.ToInt32(d, p) }; p += 4;
        if (f.BsVersion != 155) throw new InvalidDataException($"model version {f.BsVersion} (expected 155)");
        p += 1 + d[p];          // author
        p += 4;                 // Fallout 76: extra value after the author
        p += 1 + d[p];          // process script
        p += 1 + d[p];          // export script
        int typeCount = BitConverter.ToUInt16(d, p); p += 2;
        var types = new string[typeCount];
        for (int i = 0; i < typeCount; i++)
        {
            int len = BitConverter.ToInt32(d, p);
            types[i] = Encoding.ASCII.GetString(d, p + 4, len);
            p += 4 + len;
        }
        var blockType = new int[blocks];
        for (int i = 0; i < blocks; i++) { blockType[i] = BitConverter.ToUInt16(d, p) & 0x7FFF; p += 2; }
        var blockSize = new int[blocks];
        for (int i = 0; i < blocks; i++) { blockSize[i] = BitConverter.ToInt32(d, p); p += 4; }
        int stringCount = BitConverter.ToInt32(d, p); p += 8;
        for (int i = 0; i < stringCount; i++)
        {
            int len = BitConverter.ToInt32(d, p);
            f.Strings.Add(Encoding.Latin1.GetString(d, p + 4, len));
            p += 4 + len;
        }
        int groups = BitConverter.ToInt32(d, p); p += 4;
        if (groups != 0) throw new InvalidDataException("model uses block groups");
        for (int i = 0; i < blocks; i++)
        {
            if (p + blockSize[i] > d.Length) throw new InvalidDataException("block sizes don't add up");
            f.Blocks.Add((types[blockType[i]], d[p..(p + blockSize[i])]));
            p += blockSize[i];
        }
        // footer: one root, block 0
        if (p + 8 != d.Length || BitConverter.ToInt32(d, p) != 1 || BitConverter.ToInt32(d, p + 4) != 0)
            throw new InvalidDataException("block sizes / footer not consistent");
        if (!NodeTypes.Contains(f.Blocks[0].Type)) throw new InvalidDataException($"root is {f.Blocks[0].Type}");
        return f;
    }

    public byte[] Write()
    {
        var types = new List<string>();
        foreach (var (t, _) in Blocks) if (!types.Contains(t)) types.Add(t);
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        w.Write(0x14020007u); w.Write((byte)1); w.Write(12u); w.Write((uint)Blocks.Count); w.Write((uint)BsVersion);
        Short(w, "ItemFinder"); w.Write(0u); Short(w, "merged"); Short(w, "");
        w.Write((ushort)types.Count);
        foreach (var t in types) { w.Write(t.Length); w.Write(Encoding.ASCII.GetBytes(t)); }
        foreach (var (t, _) in Blocks) w.Write((ushort)types.IndexOf(t));
        foreach (var (_, b) in Blocks) w.Write(b.Length);
        w.Write(Strings.Count); w.Write(Strings.Count == 0 ? 0 : Strings.Max(s => s.Length));
        foreach (var s in Strings) { w.Write(s.Length); w.Write(Encoding.Latin1.GetBytes(s)); }
        w.Write(0);
        foreach (var (_, b) in Blocks) w.Write(b);
        w.Write(1); w.Write(0);
        return ms.ToArray();
    }

    static void Short(BinaryWriter w, string s)
    {
        var b = Encoding.ASCII.GetBytes(s + "\0");
        w.Write((byte)b.Length); w.Write(b);
    }

    public int AddString(string s)
    {
        int i = Strings.IndexOf(s);
        if (i >= 0) return i;
        Strings.Add(s);
        return Strings.Count - 1;
    }

    public int Add(string type, byte[] data) { Blocks.Add((type, data)); return Blocks.Count - 1; }

    public bool IsNode(int i) => i >= 0 && i < Blocks.Count && NodeTypes.Contains(Blocks[i].Type);
    public bool IsShape(int i) => i >= 0 && i < Blocks.Count && ShapeTypes.Contains(Blocks[i].Type);
    public string TypeOf(int i) => i >= 0 && i < Blocks.Count ? Blocks[i].Type : "";

    /// <summary>NiAVObject: name, extra data list, controller, flags, transform (52), collision.</summary>
    public static int AvObjectEnd(byte[] b) => 8 + 4 * BitConverter.ToInt32(b, 4) + 4 + 4 + 52 + 4;

    public List<int> Children(int node)
    {
        var b = Blocks[node].Data;
        int c = AvObjectEnd(b), n = BitConverter.ToInt32(b, c);
        return Enumerable.Range(0, n).Select(k => BitConverter.ToInt32(b, c + 4 + 4 * k)).ToList();
    }

    /// <summary>Takes a block out of a node's children: its slot becomes empty (-1), the block stays in the file unused.</summary>
    public void DropChild(int node, int kid)
    {
        var b = (byte[])Blocks[node].Data.Clone();
        int c = AvObjectEnd(b), n = BitConverter.ToInt32(b, c);
        for (int k = 0; k < n; k++)
            if (BitConverter.ToInt32(b, c + 4 + 4 * k) == kid) BitConverter.TryWriteBytes(b.AsSpan(c + 4 + 4 * k), -1);
        Blocks[node] = (Blocks[node].Type, b);
    }

    public void AddChildren(int node, IReadOnlyList<int> kids)
    {
        if (kids.Count == 0) return;
        var (t, b) = Blocks[node];
        int c = AvObjectEnd(b), n = BitConverter.ToInt32(b, c), end = c + 4 + 4 * n;
        var nb = new List<byte>(b[..c]);
        nb.AddRange(BitConverter.GetBytes(n + kids.Count));
        nb.AddRange(b[(c + 4)..end]);
        foreach (var k in kids) nb.AddRange(BitConverter.GetBytes(k));
        nb.AddRange(b[end..]);
        Blocks[node] = (t, nb.ToArray());
    }

    /// <summary>Shape refs (skin, shader, alpha) follow the bound sphere (16) and, in stream 155, a 24-byte box.</summary>
    public static int ShapeRefs(byte[] b) => AvObjectEnd(b) + 16 + 24;

    /// <summary>NiAVObject translation (x, y, z).</summary>
    public static (float X, float Y, float Z) Translation(byte[] b)
    {
        int t = 8 + 4 * BitConverter.ToInt32(b, 4) + 8;
        return (BitConverter.ToSingle(b, t), BitConverter.ToSingle(b, t + 4), BitConverter.ToSingle(b, t + 8));
    }

    /// <summary>Shape bounding box in its own space: centre and half extents (stream 155).</summary>
    public static (float CX, float CY, float CZ, float HX, float HY, float HZ) Box(byte[] b)
    {
        int o = AvObjectEnd(b) + 16;
        return (BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8),
                BitConverter.ToSingle(b, o + 12), BitConverter.ToSingle(b, o + 16), BitConverter.ToSingle(b, o + 20));
    }

    /// <summary>NiAlphaProperty (NiObjectNET + flags + threshold): offset of the flags.</summary>
    public static int AlphaFlags(byte[] b) => 8 + 4 * BitConverter.ToInt32(b, 4) + 4;

    /// <summary>First texture (the colour map) of a BSShaderTextureSet, or "".</summary>
    public static string FirstTexture(byte[] b)
    {
        if (b.Length < 8 || BitConverter.ToInt32(b, 0) < 1) return "";
        int len = BitConverter.ToInt32(b, 4);
        return len > 0 && 8 + len <= b.Length ? Encoding.Latin1.GetString(b, 8, len).TrimEnd('\0') : "";
    }
}

/// <summary>Glow material for one part; TestRef >= 0 = cut-out part (alpha test with that threshold).</summary>
/// <param name="AlphaMode">0 = its own additive blending block, 1 = none (the material blends), 2 = the part's own block.</param>
public sealed record GlowMat(string Material, int TestRef = -1, int AlphaMode = 0);

/// <summary>What the patcher knows about a part that should glow.</summary>
/// <param name="Material">The part's material file path from the model ("" if none).</param>
/// <param name="Cutout">Its alpha property cuts it out (alpha test).</param>
/// <param name="InlineTexture">Colour texture written in the model (texture set), used when the material file is missing.</param>
/// <param name="Threshold">Alpha-test threshold of its alpha property.</param>
/// <param name="Skinned">An animated part (creature body, worn outfit).</param>
public sealed record GlowPart(string Material, bool Cutout, string InlineTexture, byte Threshold, bool Skinned = false);

/// <param name="GlowFor">Glow for a part; returns null for no glow on that part. A null function = no glow at all.</param>
/// <param name="BeamArrow">The beam is a solid arrow pointing down at the item (BeamLength = how high its top is).</param>
/// <param name="Model">A 3D model icon (Models3D) with its textured material, instead of an icon.</param>
public sealed record PatchOptions(Func<GlowPart, GlowMat?>? GlowFor, string? BeamMaterial, float BeamLength, float BeamWidth, bool Harvestable,
                                  string? IconMaterial = null, float IconSize = 40, bool[,]? Icon3D = null, string? Icon3DMaterial = null,
                                  bool BeamArrow = false, Mesh? Model = null, string? ModelMaterial = null,
                                  string? LabelMaterial = null, float LabelHalfWidth = 0, float LabelHalfHeight = 0, float HideAfter = 0);

public sealed record PatchResult(byte[] Data, int GlowShapes, bool Beam, string Note);

/// <summary>Adds the highlight to a model: a see-through copy of every shape (glow) and/or a beam of light.</summary>
public static class NifPatch
{
    /// <summary>Flat effect cards (sparks, billboards, particles): never glowed - a glow would show the whole card.</summary>
    static readonly System.Text.RegularExpressions.Regex EffectCard = new("(billboard|particle|electric|spark|fx|flare|beam|cone)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    /// <summary>Glass effect parts named after the item, not their material (the Arktos chem tank's "Container01"; the
    /// word in a material path would also catch the light cards of the stash boxes in atx_stashboxcontainers).</summary>
    static readonly System.Text.RegularExpressions.Regex GlassPart = new("^container", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    /// <summary>Effect parts that are glass (bottles, jars, crystals): part of the item, they glow with it.</summary>
    static readonly System.Text.RegularExpressions.Regex GlassLike = new("(glass|bottle|jar|beaker|flask|vial|crystal|jug|carafe|decanter)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    /// <summary>Heat-haze shapes (a minigun's or railway rifle's BaseRefractionMesh): invisible in the game, so never
    /// glowed (a glow would show a blob around the barrel) and not drawn in the preview.</summary>
    public static bool IsRefraction(string part, string material) =>
        part.Contains("refraction", StringComparison.OrdinalIgnoreCase) || material.Contains("refraction", StringComparison.OrdinalIgnoreCase);
    /// <summary>The level designers' placement guides (EditorMarker: a whole resource collector on the wood piles, blocks
    /// on crop plants and dig mounds). Not in the game normally, but one showed on a patched wood pile.</summary>
    public static bool IsEditorOnly(string part) => part.StartsWith("EditorMarker", StringComparison.OrdinalIgnoreCase);

    const ulong BeamVertexDesc = 0x0003B00005430206;

    public static PatchResult Apply(byte[] src, PatchOptions o)
    {
        var f = NifFile.Parse(src);
        int originalBlocks = f.Blocks.Count;

        // Walk the tree from the root. For a harvestable plant the first switch node holds the
        // states (child 0 = unharvested, 1 = harvested): glow and beam go into child 0 only.
        var parent = new Dictionary<int, int>();
        var skip = new HashSet<int>();
        int beamParent = 0, switchNode = -1;
        // placement guides out of the tree, in every harvest state: they showed in game on a patched wood pile
        for (int n = 0; n < f.Blocks.Count; n++)
            if (f.IsNode(n))
                foreach (var k in f.Children(n).Where(k => f.IsShape(k) && IsEditorOnly(f.Strings.ElementAtOrDefault(BitConverter.ToInt32(f.Blocks[k].Data, 0)) ?? "")))
                    f.DropChild(n, k);
        var stack = new Stack<int>();
        stack.Push(0);
        var seen = new HashSet<int>();
        var shapes = new List<int>();
        // position of each node's origin in root space (translations only - rotated sub-nodes are rare in items)
        var origin = new Dictionary<int, (float X, float Y, float Z)> { [0] = (0, 0, 0) };
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if (!seen.Add(n)) continue;
            if (f.IsShape(n)) { shapes.Add(n); continue; }
            if (!f.IsNode(n)) continue;
            var kids = f.Children(n);
            var here = origin.GetValueOrDefault(n);
            foreach (var k in kids)
                if (f.IsNode(k) && !origin.ContainsKey(k))
                {
                    var t = NifFile.Translation(f.Blocks[k].Data);
                    origin[k] = (here.X + t.X, here.Y + t.Y, here.Z + t.Z);
                }
            if (o.Harvestable && switchNode < 0 && f.TypeOf(n) == "NiSwitchNode" && kids.Count >= 2)
            {
                switchNode = n;
                if (f.IsNode(kids[0])) beamParent = kids[0];
                for (int k = 1; k < kids.Count; k++) MarkSubtree(f, kids[k], skip);
            }
            foreach (var k in kids)
                if (k >= 0 && k < f.Blocks.Count && !skip.Contains(k)) { parent[k] = n; stack.Push(k); }
        }

        var glowAdded = new Dictionary<int, List<int>>();
        int glowCount = 0, skippedCutouts = 0;
        // Glow copies get their own additive blending (light added on top, the item stays visible underneath).
        // Keeping the part's own alpha property drew leaf glows opaque (blending off) and hid the plant.
        var glowAlpha = new Dictionary<int, int>();
        int GlowAlpha(int testRef)
        {
            if (!glowAlpha.TryGetValue(testRef, out int i))
                glowAlpha[testRef] = i = f.Add("NiAlphaProperty", testRef < 0 ? AlphaAdditive() : AlphaAdditive(true, (byte)testRef));
            return i;
        }
        if (o.GlowFor != null)
        {
            // effect parts (already glowing: lights, sparks) are left alone - unless the model is nothing but effects
            // (glassware, flux crystals, kaleidopore pods): then its solid effect parts glow too
            int ShaderOf(int s) { var sb = f.Blocks[s].Data; return BitConverter.ToInt32(sb, NifFile.ShapeRefs(sb) + 4); }
            bool effectsOnly = shapes.Where(s => s < originalBlocks).All(s => f.TypeOf(ShaderOf(s)) != "BSLightingShaderProperty");
            foreach (int s in shapes.Where(s => s < originalBlocks).OrderBy(s => s))
            {
                var b = f.Blocks[s].Data;
                int r = NifFile.ShapeRefs(b);
                int skin = BitConverter.ToInt32(b, r), shader = BitConverter.ToInt32(b, r + 4);
                if (f.TypeOf(shader) == "") continue; // a part with no shader at all draws nothing
                bool effectPart = f.TypeOf(shader) == "BSEffectShaderProperty";
                string partName = f.Strings.ElementAtOrDefault(BitConverter.ToInt32(b, 0)) ?? "";
                string effectMat = effectPart ? f.Strings.ElementAtOrDefault(BitConverter.ToInt32(f.Blocks[shader].Data, 0)) ?? "" : "";
                if (IsRefraction(partName, f.Strings.ElementAtOrDefault(BitConverter.ToInt32(f.Blocks[shader].Data, 0)) ?? "")) continue;
                // effect parts glow when they are the item itself: all of it is effects, or the part is glass (a Nuka-Cola
                // bottle: labels + glass)
                if (!(f.TypeOf(shader) == "BSLightingShaderProperty" ||
                      effectPart && skin < 0 && !EffectCard.IsMatch(partName) && (effectsOnly || GlassLike.IsMatch(partName + " " + effectMat) || GlassPart.IsMatch(partName))))
                    continue;
                int alphaProp = BitConverter.ToInt32(b, r + 8);
                bool cutout = false;
                byte threshold = 128;
                if (f.TypeOf(alphaProp) == "NiAlphaProperty")
                {
                    var ab = f.Blocks[alphaProp].Data;
                    int af = NifFile.AlphaFlags(ab);
                    cutout = !effectPart && (BitConverter.ToUInt16(ab, af) & 0x200) != 0; // an effect's own alpha isn't a cut-out
                    threshold = ab[af + 2];
                }
                // the texture set written after the shader is what the game uses when the material file is missing
                string inline = f.TypeOf(shader + 1) == "BSShaderTextureSet" ? NifFile.FirstTexture(f.Blocks[shader + 1].Data) : "";
                var glowMat = o.GlowFor(new GlowPart(f.Strings.ElementAtOrDefault(BitConverter.ToInt32(f.Blocks[shader].Data, 0)) ?? "", cutout, inline, threshold, skin >= 0));
                if (glowMat == null) { skippedCutouts++; continue; }
                int mat = f.AddString(glowMat.Material);
                var copy = (byte[])b.Clone();
                Put(copy, 0, f.AddString(f.Strings.ElementAtOrDefault(BitConverter.ToInt32(b, 0)) + "_IFGlow"));
                int av = NifFile.AvObjectEnd(copy);
                Put(copy, av - 4, -1);                               // no collision
                Put(copy, 8 + 4 * BitConverter.ToInt32(copy, 4), -1); // no controller
                int attachTo = parent.GetValueOrDefault(s, 0);
                if (skin >= 0)
                {
                    if (f.TypeOf(skin) != "BSSkin::Instance") continue;
                    var sk = (byte[])f.Blocks[skin].Data.Clone();
                    int bone = BitConverter.ToInt32(sk, 4);
                    if (f.TypeOf(bone) != "BSSkin::BoneData") continue;
                    int newBone = f.Add("BSSkin::BoneData", f.Blocks[bone].Data);
                    Put(sk, 4, newBone);
                    Put(copy, r, f.Add("BSSkin::Instance", sk));
                    attachTo = 0; // skinned shapes follow their bones wherever they hang (as tested on bodies)
                }
                Put(copy, r + 4, f.Add("BSEffectShaderProperty", EffectShader(mat)));
                Put(copy, r + 8, glowMat.AlphaMode switch { 1 => -1, 2 => alphaProp, _ => GlowAlpha(glowMat.TestRef) });
                int shape = f.Add(f.TypeOf(s), copy);
                if (!glowAdded.TryGetValue(attachTo, out var list)) glowAdded[attachTo] = list = new();
                list.Add(shape);
                glowCount++;
            }
        }

        bool beam = false;
        if (o.BeamMaterial != null)
        {
            int mat = f.AddString(o.BeamMaterial);
            int shader = f.Add("BSEffectShaderProperty", EffectShader(mat));
            int alpha = f.Add("NiAlphaProperty", AlphaAdditive());
            int name = f.AddString("ItemFinderBeam");
            var (bx, by, bz) = BeamBase(f, shapes, parent, origin, beamParent);
            int shape = f.Add("BSTriShape", o.BeamArrow ? ArrowShape(name, shader, alpha, o.BeamWidth, (bx, by, bz))
                                                         : TubeShape(name, shader, alpha, o.BeamLength, o.BeamWidth, (bx, by, bz)));
            if (!glowAdded.TryGetValue(beamParent, out var list)) glowAdded[beamParent] = list = new();
            list.Add(shape);
            beam = true;
        }
        var notes = new List<string>();
        // icon: a square in a camera-facing node at the top of the beam (or just above the item). Works on
        // ordinary models (tested on the cookie jar with BSX Animated|NeedsTransformUpdates); animated (skinned)
        // models drop the node, so creatures get none.
        bool icon = false;
        // a camera-facing node sets its own world transform and ignores a shrinking parent (in game, 2026-11-06: the name
        // card stayed after the timer) - with a timer, its card goes into a node of its own that the timer shrinks too
        var inner = new List<(int Node, string Name)>();
        int Inner(int kid, string name)
        {
            if (o.HideAfter <= 0) return kid;
            int n = f.Add("NiNode", PlainNode(f.AddString(name), new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 }, new[] { kid }));
            inner.Add((n, name));
            return n;
        }
        bool skinned = shapes.Any(s => BitConverter.ToInt32(f.Blocks[s].Data, NifFile.ShapeRefs(f.Blocks[s].Data)) >= 0);
        if (o.IconMaterial != null && !skinned)
        {
            var (bx, by, bz) = BeamBase(f, shapes, parent, origin, beamParent);
            float S = o.IconSize;
            float z = bz + (o.BeamMaterial != null ? o.BeamLength : 20) + S;
            int mat = f.AddString(o.IconMaterial);
            int shader = f.Add("BSEffectShaderProperty", EffectShader(mat));
            int alpha = f.Add("NiAlphaProperty", AlphaAdditive());
            int quad = f.Add("BSTriShape", IconShape(f.AddString("ItemFinderIcon:0"), shader, alpha, S));
            int bill = f.Add("NiBillboardNode", BillboardNode(f.AddString("ItemFinderIcon"), (bx, by, z), Inner(quad, "ESPMakerTimedIcon")));
            if (!glowAdded.TryGetValue(beamParent, out var list)) glowAdded[beamParent] = list = new();
            list.Add(bill);
            OrBsx(f, 0x101); // Animated | NeedsTransformUpdates: billboards turn on statics
            icon = true;
        }
        // name label: a camera-facing card with the item's name, above the icon (or where the icon would go). Like the
        // flat icon, only on models the game doesn't animate
        bool label = false;
        if (o.LabelMaterial != null && !skinned)
        {
            var (bx, by, bz) = BeamBase(f, shapes, parent, origin, beamParent);
            float z = bz + (o.BeamMaterial != null ? o.BeamLength : 20) + (icon ? 2 * o.IconSize + 4 : 0) + o.LabelHalfHeight;
            int mat = f.AddString(o.LabelMaterial);
            int shader = f.Add("BSEffectShaderProperty", EffectShader(mat));
            int alpha = f.Add("NiAlphaProperty", AlphaAdditive());
            int quad = f.Add("BSTriShape", IconShape(f.AddString("ESPMakerLabel:0"), shader, alpha, o.LabelHalfWidth, o.LabelHalfHeight));
            int bill = f.Add("NiBillboardNode", BillboardNode(f.AddString("ESPMakerLabel"), (bx, by, z), Inner(quad, "ESPMakerTimedLabel")));
            if (!glowAdded.TryGetValue(beamParent, out var list)) glowAdded[beamParent] = list = new();
            list.Add(bill);
            OrBsx(f, 0x101);
            label = true;
        }
        // 3D icon: a solid shape needs no camera-facing node, so it also works on animated (skinned) models like the
        // Spooky pumpkin did; it turns with the creature or item
        bool icon3d = false;
        if (o.Icon3D != null && o.Icon3DMaterial != null && (o.IconMaterial == null || skinned)) // the flat icon wins where it works
        {
            var (bx, by, bz) = BeamBase(f, shapes, parent, origin, beamParent);
            float S = o.IconSize;
            // creatures: clear of heads that are separate parts; items: just above
            float z = bz + (o.BeamMaterial != null ? o.BeamLength : skinned ? 70 : 20) + S;
            int mat = f.AddString(o.Icon3DMaterial);
            int shader = f.Add("BSEffectShaderProperty", EffectShader(mat));
            int alpha = f.Add("NiAlphaProperty", AlphaAdditive());
            int shape = f.Add("BSTriShape", Icon3DShape(f.AddString("ItemFinderIcon3D"), shader, alpha, o.Icon3D, S, (bx, by, z)));
            if (!glowAdded.TryGetValue(beamParent, out var list)) glowAdded[beamParent] = list = new();
            list.Add(shape);
            icon3d = true;
        }
        // 3D model icon (pumpkin, skull...): real geometry with its own texture, placed like the 3D icon
        bool model = false;
        if (o.Model != null && o.ModelMaterial != null)
        {
            var (bx, by, bz) = BeamBase(f, shapes, parent, origin, beamParent);
            float S = o.IconSize;
            float z = bz + (o.BeamMaterial != null ? o.BeamLength : skinned ? 70 : 20);
            int mat = f.AddString(o.ModelMaterial);
            int shader = f.Add("BSEffectShaderProperty", EffectShader(mat));
            int alpha = f.Add("NiAlphaProperty", AlphaAdditive());
            int shape = f.Add("BSTriShape", ModelShape(f.AddString("ItemFinderModel"), shader, alpha, o.Model, S, (bx, by, z)));
            if (!glowAdded.TryGetValue(beamParent, out var list)) glowAdded[beamParent] = list = new();
            list.Add(shape);
            model = true;
        }
        // a part that hangs on a connection point (robot parts: "connect at C-BotCore") is not placed like a worn piece:
        // markers on its root showed at the hip, then 3 m away (in game). Its skinned parts are placed right (the glow), so
        // the markers become skinned too - bound to its highest bone, the game places them with the same bones
        if (skinned && glowAdded.TryGetValue(beamParent, out var rootKids) && ConnectFrame(f, originalBlocks) != null &&
            shapes.FirstOrDefault(sh => sh < originalBlocks && f.TypeOf(BitConverter.ToInt32(f.Blocks[sh].Data, NifFile.ShapeRefs(f.Blocks[sh].Data))) == "BSSkin::Instance") is int body && body > 0)
        {
            int made = 0;
            foreach (var k in rootKids.Where(k => IsMarker(f, k) && f.TypeOf(k) == "BSTriShape").ToList())
                if (SkinMarker(f, k, body)) made++;
            if (made > 0) notes.Add("markers bound to the bones");
        }
        // hide after N seconds: everything added goes into its own node(s), and an animation that starts when the model
        // loads (the game's autoplay behaviour, as the BobbleBanner mod does it) shrinks them to nothing
        bool timed = false;
        if (o.HideAfter > 0 && glowAdded.Count > 0)
        {
            var rb = f.Blocks[0].Data;
            int rootCtl = BitConverter.ToInt32(rb, 8 + 4 * BitConverter.ToInt32(rb, 4));
            if (skinned) notes.Add("no timer (animated model)");
            else if (rootCtl >= 0 || f.Blocks.Any(b => b.Type == "BSBehaviorGraphExtraData")) notes.Add("no timer (model has its own animation)");
            else
            {
                var wrapped = new Dictionary<int, List<int>>();
                var targets = new List<(int Node, string Name)>();
                foreach (var (node, kids) in glowAdded)
                {
                    string nm = "ESPMakerTimed" + targets.Count;
                    int w = f.Add("NiNode", PlainNode(f.AddString(nm), new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 }, kids));
                    targets.Add((w, nm));
                    wrapped[node] = new List<int> { w };
                }
                glowAdded = wrapped;
                targets.AddRange(inner);
                AddHideTimer(f, targets, o.HideAfter);
                timed = true;
            }
        }
        foreach (var (node, kids) in glowAdded) f.AddChildren(node, kids);
        if (label) notes.Add("label");
        if (timed) notes.Add($"hides after {o.HideAfter:0} s");


        if (o.Harvestable && switchNode < 0) notes.Add("no harvest states found - beam stays after harvesting");
        if (o.Harvestable && switchNode >= 0 && beamParent == 0) notes.Add("harvest state is not a node - beam stays after harvesting");
        if (skippedCutouts > 0) notes.Add($"{skippedCutouts} cut-out part(s) without a readable material not glowed");
        if (icon) notes.Add("icon");
        else if (o.IconMaterial != null && skinned && !icon3d) notes.Add("no icon (animated model)");
        if (icon3d) notes.Add("3D icon");
        if (model) notes.Add("3D model");
        string note = string.Join(", ", notes);
        return new PatchResult(f.Write(), glowCount, beam, note);
    }

    /// <summary>Where the beam starts, in the space of the node it hangs from: centred over the item's visible
    /// parts (lit shapes, so editor markers don't count), at the top of them - so it rises out of the item
    /// instead of running through it. (0,0,0) if nothing sensible is found.</summary>
    static (float X, float Y, float Z) BeamBase(NifFile f, List<int> shapes, Dictionary<int, int> parent,
                                                Dictionary<int, (float X, float Y, float Z)> origin, int beamParent)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, top = float.MinValue;
        foreach (int s in shapes)
        {
            var b = f.Blocks[s].Data;
            int r = NifFile.ShapeRefs(b);
            if (f.TypeOf(BitConverter.ToInt32(b, r + 4)) != "BSLightingShaderProperty") continue;
            var o = origin.GetValueOrDefault(parent.GetValueOrDefault(s, 0));
            var t = NifFile.Translation(b);
            var (cx, cy, cz, hx, hy, hz) = NifFile.Box(b);
            bool posed = false;
            if (BitConverter.ToInt32(b, r) >= 0 && SkinnedBox(f, b) is { } sb)
            {
                // an animated part: placed by its bones (a mask sits at the head, not where its raw vertices are)
                (cx, cy, cz, hx, hy, hz) = sb;
                posed = true;
            }
            else if (hx == 0 && hy == 0 && hz == 0 || !(Math.Abs(cz) + hz < 1e6f)) // skinned parts: empty or "infinite" box
            {
                // skinned parts leave the box empty: measure the vertices (stored in the resting pose)
                if (VertexBox(b) is { } vb) (cx, cy, cz, hx, hy, hz) = vb;
                else
                {
                    int sp = NifFile.AvObjectEnd(b);
                    (cx, cy, cz) = (BitConverter.ToSingle(b, sp), BitConverter.ToSingle(b, sp + 4), BitConverter.ToSingle(b, sp + 8));
                    hx = hy = hz = BitConverter.ToSingle(b, sp + 12);
                    if (hz <= 0) continue;
                }
            }
            if (!float.IsFinite(cz + hz + cx + cy + hx + hy) || hx < 0 || hy < 0 || hz < 0 || Math.Abs(cz) + hz > 5000) continue;
            float x = posed ? cx : o.X + t.X + cx, y = posed ? cy : o.Y + t.Y + cy, z = posed ? cz : o.Z + t.Z + cz;
            minX = Math.Min(minX, x - hx); maxX = Math.Max(maxX, x + hx);
            minY = Math.Min(minY, y - hy); maxY = Math.Max(maxY, y + hy);
            top = Math.Max(top, z + hz);
        }
        if (top == float.MinValue || top > 2000 || maxX - minX > 4000 || maxY - minY > 4000) return (0, 0, 0); // nothing sensible (effects etc.)
        var p = origin.GetValueOrDefault(beamParent);
        return ((minX + maxX) / 2 - p.X, (minY + maxY) / 2 - p.Y, Math.Max(0, top) - p.Z);
    }

    /// <summary>Box (centre, half extents) of an animated part in the model's standing pose: each vertex placed by its
    /// bones (node rest transforms x BSSkin::BoneData), as the game does. Null when the skin data can't be read.</summary>
    static (float, float, float, float, float, float)? SkinnedBox(NifFile f, byte[] b)
    {
        try
        {
            int r = NifFile.ShapeRefs(b), skin = BitConverter.ToInt32(b, r);
            if (f.TypeOf(skin) != "BSSkin::Instance") return null;
            var si = f.Blocks[skin].Data;
            int data = BitConverter.ToInt32(si, 4), count = BitConverter.ToInt32(si, 8);
            if (f.TypeOf(data) != "BSSkin::BoneData") return null;
            var bd = f.Blocks[data].Data;
            if (BitConverter.ToInt32(bd, 0) != count || 4 + 68 * count > bd.Length || 12 + 4 * count > si.Length) return null;
            // every node's rest place in the model
            var world = new Dictionary<int, float[]>();
            void Walk(int n, float[] parent)
            {
                if (!f.IsNode(n) || world.ContainsKey(n)) return;
                var m = Mul(parent, Local(f.Blocks[n].Data));
                world[n] = m;
                foreach (var k in f.Children(n)) Walk(k, m);
            }
            Walk(0, Identity);
            var bones = new float[count][];
            for (int i = 0; i < count; i++)
            {
                int o = 4 + 68 * i + 16;
                var bind = Matrix(bd, o, o + 36, BitConverter.ToSingle(bd, o + 48));
                bones[i] = Mul(world.GetValueOrDefault(BitConverter.ToInt32(si, 12 + 4 * i), Identity), bind);
            }
            ulong desc = BitConverter.ToUInt64(b, r + 12);
            int vc = BitConverter.ToUInt16(b, r + 24), q = r + 30, stride = (int)(desc & 0xF) * 4, sOff = (int)((desc >> 28) & 0xF) * 4;
            int attrs = (int)((desc >> 44) & 0xFFF);
            if ((attrs & 0x40) == 0 || stride == 0 || q + vc * stride > b.Length) return null;
            bool full = (attrs & 0x400) != 0;
            float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
            for (int v = 0; v < vc; v++)
            {
                int p = q + v * stride;
                float x = full ? BitConverter.ToSingle(b, p) : (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b, p));
                float y = full ? BitConverter.ToSingle(b, p + 4) : (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b, p + 2));
                float z = full ? BitConverter.ToSingle(b, p + 8) : (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b, p + 4));
                float px = 0, py = 0, pz = 0, ws = 0;
                for (int k = 0; k < 4; k++)
                {
                    float w = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b, p + sOff + 2 * k));
                    int bi = b[p + sOff + 8 + k];
                    if (!(w > 0) || bi >= count) continue;
                    var m = bones[bi];
                    px += w * (m[0] * x + m[1] * y + m[2] * z + m[3]);
                    py += w * (m[4] * x + m[5] * y + m[6] * z + m[7]);
                    pz += w * (m[8] * x + m[9] * y + m[10] * z + m[11]);
                    ws += w;
                }
                if (ws <= 0 || !float.IsFinite(px + py + pz)) continue;
                px /= ws; py /= ws; pz /= ws;
                x0 = Math.Min(x0, px); x1 = Math.Max(x1, px); y0 = Math.Min(y0, py); y1 = Math.Max(y1, py); z0 = Math.Min(z0, pz); z1 = Math.Max(z1, pz);
            }
            if (x0 > x1) return null;
            return ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, (x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2);
        }
        catch { return null; }
    }

    // 3x4 transforms (rotation rows with the translation in the 4th column, scale folded in)
    static readonly float[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };

    static float[] Matrix(byte[] d, int rot, int trans, float scale)
    {
        if (!float.IsFinite(scale) || scale == 0) scale = 1;
        var m = new float[12];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++) m[i * 4 + j] = BitConverter.ToSingle(d, rot + 4 * (i * 3 + j)) * scale;
            m[i * 4 + 3] = BitConverter.ToSingle(d, trans + 4 * i);
        }
        return m;
    }

    /// <summary>A node's own transform (NiAVObject: translation, rotation, scale).</summary>
    static float[] Local(byte[] b)
    {
        int o = 8 + 4 * BitConverter.ToInt32(b, 4) + 4 + 4;
        return Matrix(b, o + 12, o, BitConverter.ToSingle(b, o + 48));
    }

    static float[] Mul(float[] a, float[] c)
    {
        var m = new float[12];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++) m[i * 4 + j] = a[i * 4] * c[j] + a[i * 4 + 1] * c[4 + j] + a[i * 4 + 2] * c[8 + j];
            m[i * 4 + 3] = a[i * 4] * c[3] + a[i * 4 + 1] * c[7] + a[i * 4 + 2] * c[11] + a[i * 4 + 3];
        }
        return m;
    }

    /// <summary>A marker shape this patch added (beam, arrow, icons, 3D model) - not a glow copy.</summary>
    static bool IsMarker(NifFile f, int k) =>
        f.Strings.ElementAtOrDefault(BitConverter.ToInt32(f.Blocks[k].Data, 0)) is "ItemFinderBeam" or "ItemFinderIcon3D" or "ItemFinderModel" or "ItemFinderIcon";

    /// <summary>Where the game puts this model's root when it hangs it on a connection point: the transform of the node
    /// named in its BSConnectPoint::Children (in the model's own rest pose). Null when it has none.</summary>
    static float[]? ConnectFrame(NifFile f, int originalBlocks)
    {
        for (int i = 0; i < originalBlocks; i++)
        {
            if (f.TypeOf(i) != "BSConnectPoint::Children") continue;
            var b = f.Blocks[i].Data;
            // NiExtraData name, skinned flag, count, then sized strings
            if (b.Length < 13 || BitConverter.ToInt32(b, 5) < 1) return null;
            int len = BitConverter.ToInt32(b, 9);
            if (len <= 0 || 13 + len > b.Length) return null;
            string target = System.Text.Encoding.ASCII.GetString(b, 13, len).TrimEnd('\0');
            float[]? found = null;
            void Walk(int n, float[] world, HashSet<int> seen)
            {
                if (found != null || n < 0 || n >= originalBlocks || !f.IsNode(n) || !seen.Add(n)) return;
                var w = n == 0 ? Identity : Mul(world, Local(f.Blocks[n].Data));
                if (f.Strings.ElementAtOrDefault(BitConverter.ToInt32(f.Blocks[n].Data, 0)) == target) { found = w; return; }
                foreach (var k in f.Children(n)) Walk(k, w, seen);
            }
            Walk(0, Identity, new HashSet<int>());
            return found;
        }
        return null;
    }

    /// <summary>Turns a marker shape (made in the creature's space, under the root) into a skinned one bound fully to the
    /// highest bone of <paramref name="body"/>'s skin: its points go back through that bone's rest transform, so the game,
    /// moving the bone, puts them where they were meant to be. Own copies of the skin blocks, as the glow copies have.</summary>
    static bool SkinMarker(NifFile f, int shape, int body)
    {
        var bb = f.Blocks[body].Data;
        int skin = BitConverter.ToInt32(bb, NifFile.ShapeRefs(bb));
        var si = (byte[])f.Blocks[skin].Data.Clone();
        int data = BitConverter.ToInt32(si, 4), count = BitConverter.ToInt32(si, 8);
        if (f.TypeOf(data) != "BSSkin::BoneData" || count <= 0 || 12 + 4 * count > si.Length) return false;
        var bd = f.Blocks[data].Data;
        if (BitConverter.ToInt32(bd, 0) != count || 4 + 68 * count > bd.Length) return false;
        var world = new Dictionary<int, float[]>();
        void Walk(int n, float[] parent)
        {
            if (!f.IsNode(n) || world.ContainsKey(n)) return;
            var m = Mul(parent, Local(f.Blocks[n].Data));
            world[n] = m;
            foreach (var k in f.Children(n)) Walk(k, m);
        }
        Walk(0, Identity);
        // the highest bone (in rest pose) carries it
        int pick = -1; float top = float.MinValue; float[]? bone = null;
        for (int i = 0; i < count; i++)
        {
            int node = BitConverter.ToInt32(si, 12 + 4 * i);
            if (!world.TryGetValue(node, out var w) || w[11] <= top) continue;
            int o = 4 + 68 * i + 16;
            top = w[11]; pick = i;
            bone = Mul(w, Matrix(bd, o, o + 36, BitConverter.ToSingle(bd, o + 48)));
        }
        if (bone == null || pick > 255) return false;
        var inv = Inverse(bone);

        var b = f.Blocks[shape].Data;
        int r = NifFile.ShapeRefs(b);
        ulong desc = BitConverter.ToUInt64(b, r + 12);
        int tris = BitConverter.ToInt32(b, r + 20), vc = BitConverter.ToUInt16(b, r + 24), q = r + 30;
        int stride = (int)(desc & 0xF) * 4;
        if (stride != 24 || ((desc >> 44) & 0x400) != 0 || q + vc * stride + tris * 6 > b.Length) return false; // only the markers' own format
        int t = 8 + 4 * BitConverter.ToInt32(b, 4) + 4 + 4;
        float ax = BitConverter.ToSingle(b, t), ay = BitConverter.ToSingle(b, t + 4), az = BitConverter.ToSingle(b, t + 8);
        static float H(byte[] d, int o) => (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(d, o));
        static float Unpack(byte v) => v / 127.5f - 1;
        var ms = new MemoryStream();
        var w2 = new BinaryWriter(ms);
        for (int v = 0; v < vc; v++)
        {
            int o = q + v * stride;
            float x = H(b, o) + ax, y = H(b, o + 2) + ay, z = H(b, o + 4) + az;
            float sx = inv[0] * x + inv[1] * y + inv[2] * z + inv[3], sy = inv[4] * x + inv[5] * y + inv[6] * z + inv[7], sz = inv[8] * x + inv[9] * y + inv[10] * z + inv[11];
            w2.Write((Half)sx); w2.Write((Half)sy); w2.Write((Half)sz); w2.Write(BitConverter.ToUInt16(b, o + 6));
            w2.Write(b, o + 8, 4); // uv
            foreach (int at in new[] { o + 12, o + 16 }) // normal, tangent: turned the same way
            {
                float nx = Unpack(b[at]), ny = Unpack(b[at + 1]), nz = Unpack(b[at + 2]);
                float rx = inv[0] * nx + inv[1] * ny + inv[2] * nz, ry = inv[4] * nx + inv[5] * ny + inv[6] * nz, rz = inv[8] * nx + inv[9] * ny + inv[10] * nz;
                float len = MathF.Sqrt(rx * rx + ry * ry + rz * rz);
                if (len > 1e-6f) { rx /= len; ry /= len; rz /= len; }
                w2.Write(NormalByte(rx)); w2.Write(NormalByte(ry)); w2.Write(NormalByte(rz)); w2.Write(b[at + 3]);
            }
            w2.Write(b, o + 20, 4); // colour
            w2.Write((Half)1f); w2.Write((Half)0f); w2.Write((Half)0f); w2.Write((Half)0f); // all on one bone
            w2.Write((byte)pick); w2.Write((byte)0); w2.Write((byte)0); w2.Write((byte)0);
        }
        w2.Write(b, q + vc * stride, tris * 6);
        var vdata = ms.ToArray();
        // new layout: 36 bytes a vertex, skin data at byte 24, "skinned" flag
        ulong nd = (desc & ~0xFUL) | 9UL;
        nd = (nd & ~(0xFUL << 28)) | (6UL << 28);
        nd |= 0x40UL << 44;
        var nb = new byte[q + vdata.Length];
        Array.Copy(b, nb, q);
        Put(nb, t, 0); Put(nb, t + 4, 0); Put(nb, t + 8, 0); // the place is in the points now
        int newData = f.Add("BSSkin::BoneData", bd);
        Put(si, 4, newData);
        Put(nb, r, f.Add("BSSkin::Instance", si));
        BitConverter.TryWriteBytes(nb.AsSpan(r + 12), nd);
        Put(nb, r + 26, vdata.Length);
        Array.Copy(vdata, 0, nb, q, vdata.Length);
        f.Blocks[shape] = (f.TypeOf(shape), nb);
        return true;
    }

    /// <summary>The inverse of a 3x4 transform made of a rotation, a uniform scale and a translation.</summary>
    static float[] Inverse(float[] m)
    {
        float s2 = m[0] * m[0] + m[4] * m[4] + m[8] * m[8];
        if (s2 < 1e-12f) return Identity;
        var r = new float[12];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++) r[i * 4 + j] = m[j * 4 + i] / s2; // transpose / scale^2
        for (int i = 0; i < 3; i++) r[i * 4 + 3] = -(r[i * 4] * m[3] + r[i * 4 + 1] * m[7] + r[i * 4 + 2] * m[11]);
        return r;
    }

    /// <summary>A plain NiNode with a transform (3x4, uniform scale) and children.</summary>
    static byte[] PlainNode(int name, float[] m, IReadOnlyList<int> children)
    {
        float scale = MathF.Sqrt(m[0] * m[0] + m[4] * m[4] + m[8] * m[8]);
        if (scale < 1e-6f) scale = 1;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(name); w.Write(0u); w.Write(-1); w.Write(0x0Eu);
        w.Write(m[3]); w.Write(m[7]); w.Write(m[11]);
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) w.Write(m[i * 4 + j] / scale);
        w.Write(scale); w.Write(-1);
        w.Write(children.Count);
        foreach (var c in children) w.Write(c);
        return ms.ToArray();
    }

    /// <summary>NiBillboardNode (mode 2, as tested): NiAVObject + one child + mode.</summary>
    static byte[] BillboardNode(int name, (float X, float Y, float Z) at, int child)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(name); w.Write(0u); w.Write(-1); w.Write(0x0Eu);
        w.Write(at.X); w.Write(at.Y); w.Write(at.Z);
        foreach (var v in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) w.Write(v);
        w.Write(1f); w.Write(-1);
        w.Write(1); w.Write(child);
        w.Write((ushort)2);
        return ms.ToArray();
    }

    /// <summary>A square of half size <paramref name="s"/> in the node's XY plane, both sides (as tested).</summary>
    static byte[] IconShape(int name, int shader, int alpha, float s) => IconShape(name, shader, alpha, s, s);

    /// <summary>A rectangle (half width sx, half height sy) in the node's XY plane, both sides.</summary>
    static byte[] IconShape(int name, int shader, int alpha, float sx, float sy)
    {
        var verts = new List<Vert>();
        var corners = new[] { (-sx, -sy, 0f), (sx, -sy, 0f), (sx, sy, 0f), (-sx, sy, 0f) };
        var uvs = new[] { (0f, 1f), (1f, 1f), (1f, 0f), (0f, 0f) };
        for (int c = 0; c < 4; c++) verts.Add(new Vert(corners[c], uvs[c], (0.0, 0.0, 1.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), new byte[] { 255, 255, 255, 255 }));
        var tris = new List<(int, int, int)> { (0, 1, 2), (0, 2, 3), (0, 2, 1), (0, 3, 2) };
        return Pack(name, shader, alpha, verts, tris, -sy, sy, Math.Max(sx, sy));
    }

    static void OrBsx(NifFile f, int bits)
    {
        for (int k = 0; k < f.Blocks.Count; k++)
            if (f.TypeOf(k) == "BSXFlags")
            {
                var b = (byte[])f.Blocks[k].Data.Clone();
                Put(b, 4, BitConverter.ToInt32(b, 4) | bits);
                f.Blocks[k] = ("BSXFlags", b);
            }
    }

    /// <summary>The BobbleBanner mod's timer (Nexus 4494, decoded 2026-11-06): the root gets a behaviour graph extra data
    /// pointing at the game's Autoplay.hkx, which plays the sequence "AutoPlay" of the root's controller manager as soon
    /// as the model loads. The sequence scales each target node 1 -> 0 over the last 4 seconds and holds (clamp).</summary>
    static void AddHideTimer(NifFile f, List<(int Node, string Name)> targets, float t)
    {
        // As in BobbleBanner (works in game), the root stays bare: the original parts and their collision go into a child
        // node. Done here before the markers are attached, so the root's children are the originals.
        var root = (byte[])f.Blocks[0].Data.Clone();
        int colAt = NifFile.AvObjectEnd(root) - 4, col = BitConverter.ToInt32(root, colAt);
        if (col >= 0 && col < f.Blocks.Count)
        {
            var kids = f.Children(0);
            int flags = BitConverter.ToInt32(root, 8 + 4 * BitConverter.ToInt32(root, 4) + 4);
            var nb = PlainNode(f.AddString("ESPMakerItem"), new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 }, kids);
            Put(nb, 12, flags);
            Put(nb, NifFile.AvObjectEnd(nb) - 4, col);
            int item = f.Add("NiNode", nb);
            // the collision's owner pointer stays on the root, as in BobbleBanner's (working) models
            Put(root, colAt, -1);
            int c = NifFile.AvObjectEnd(root), n = BitConverter.ToInt32(root, c);
            var rebuilt = new List<byte>(root[..c]);
            rebuilt.AddRange(BitConverter.GetBytes(1));
            rebuilt.AddRange(BitConverter.GetBytes(item));
            rebuilt.AddRange(root[(c + 4 + 4 * n)..]);
            f.Blocks[0] = (f.Blocks[0].Type, rebuilt.ToArray());
        }
        float fade = Math.Min(4, t);
        BinaryWriter W(out MemoryStream ms) { ms = new MemoryStream(); return new BinaryWriter(ms); }
        // scale keys (quadratic, flat tangents, as in the mod)
        var w = W(out var ms);
        w.Write(0u); // no rotation keys
        w.Write(0u); // no translation keys
        const float gone = 0.001f; // not 0: a zero scale can't be inverted (suspected in a stash-view crash, 2026-11-06)
        var keys = fade < t ? new[] { (0f, 1f), (t - fade, 1f), (t, gone) } : new[] { (0f, 1f), (t, gone) };
        w.Write((uint)keys.Length); w.Write(2u);
        foreach (var (time, v) in keys) { w.Write(time); w.Write(v); w.Write(0f); w.Write(0f); }
        var dataBytes = ms.ToArray();
        // Block order matters: the stash view crashed when the data came before the blocks using it (manager last).
        // BobbleBanner's order, each block before the ones it points at (in-game round 6, 2026-11-06):
        // BGED, manager, controller, sequence, interpolators, data, text keys, palette.
        int bged = f.Blocks.Count, manager = bged + 1, mtc = bged + 2, seq = bged + 3;
        var interps = Enumerable.Range(seq + 1, targets.Count).ToList();
        int data = seq + 1 + targets.Count, textKeys = data + 1, palette = data + 2;
        var interpBytes = new List<byte[]>();
        foreach (var _ in targets)
        {
            w = W(out ms);
            w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); w.Write(data);
            interpBytes.Add(ms.ToArray());
        }
        w = W(out ms);
        w.Write(-1); w.Write(2u); w.Write(0f); w.Write(f.AddString("start")); w.Write(t + 1); w.Write(f.AddString("end"));
        var textKeyBytes = ms.ToArray();
        w = W(out ms);
        w.Write(f.AddString("AutoPlay")); w.Write((uint)targets.Count); w.Write(1u);
        int ctlType = f.AddString("NiTransformController");
        for (int i = 0; i < targets.Count; i++)
        {
            w.Write(interps[i]); w.Write(-1); w.Write((byte)0);
            w.Write(f.AddString(targets[i].Name)); w.Write(-1); w.Write(ctlType); w.Write(-1); w.Write(-1);
        }
        w.Write(1f); w.Write(textKeys); w.Write(2u); w.Write(1f); w.Write(0f); w.Write(t + 1); w.Write(manager);
        w.Write(BitConverter.ToInt32(f.Blocks[0].Data, 0)); w.Write((ushort)0);
        var seqBytes = ms.ToArray();
        w = W(out ms);
        w.Write(-1); w.Write((ushort)0x6C); w.Write(1f); w.Write(0f); w.Write(float.MaxValue); w.Write(-float.MaxValue); w.Write(0);
        w.Write((ushort)targets.Count); foreach (var tg in targets) w.Write(tg.Node);
        var mtcBytes = ms.ToArray();
        w = W(out ms);
        w.Write(mtc); w.Write((ushort)0x4C); w.Write(1f); w.Write(0f); w.Write(float.MaxValue); w.Write(-float.MaxValue); w.Write(0);
        w.Write((byte)0); w.Write(1u); w.Write(seq); w.Write(palette);
        var managerBytes = ms.ToArray();
        w = W(out ms);
        w.Write(0); w.Write((uint)targets.Count);
        foreach (var tg in targets) { w.Write(tg.Name.Length); w.Write(Encoding.ASCII.GetBytes(tg.Name)); w.Write(tg.Node); }
        var paletteBytes = ms.ToArray();
        w = W(out ms);
        w.Write(f.AddString("BGED")); w.Write(f.AddString(@"GenericBehaviors\Autoplay\Autoplay.hkx")); w.Write((byte)0);
        if (f.Blocks.Count != bged) throw new InvalidOperationException("hide timer: block indices shifted");
        f.Add("BSBehaviorGraphExtraData", ms.ToArray());
        f.Add("NiControllerManager", managerBytes);
        f.Add("NiMultiTargetTransformController", mtcBytes);
        f.Add("NiControllerSequence", seqBytes);
        foreach (var ib in interpBytes) f.Add("NiTransformInterpolator", ib);
        f.Add("NiTransformData", dataBytes);
        f.Add("NiTextKeyExtraData", textKeyBytes);
        f.Add("NiDefaultAVObjectPalette", paletteBytes);
        if (!f.Blocks.Any(b => b.Type == "BSXFlags"))
        {
            w = W(out ms); w.Write(f.AddString("BSX")); w.Write(0);
            AddRootExtra(f, f.Add("BSXFlags", ms.ToArray()));
        }
        AddRootExtra(f, bged);
        OrBsx(f, 0x1); // Animated
        // Animated + Articulated (bobbleheads: 0xC2) crashed the game the moment the item was shown in the stash
        // (2026-11-06). BobbleBanner turns the same bobbleheads into 0x0B: Dynamic and Articulated off, Complex on.
        for (int k = 0; k < f.Blocks.Count; k++)
            if (f.TypeOf(k) == "BSXFlags" && (BitConverter.ToInt32(f.Blocks[k].Data, 4) & 0x80) != 0)
            {
                var b = (byte[])f.Blocks[k].Data.Clone();
                Put(b, 4, BitConverter.ToInt32(b, 4) & ~0xC0 | 0x8);
                f.Blocks[k] = ("BSXFlags", b);
            }
        var rb =(byte[])f.Blocks[0].Data.Clone();
        Put(rb, 8 + 4 * BitConverter.ToInt32(rb, 4), manager);
        f.Blocks[0] = (f.Blocks[0].Type, rb);
    }

    static void AddRootExtra(NifFile f, int extra)
    {
        var (t, b) = f.Blocks[0];
        int n = BitConverter.ToInt32(b, 4), end = 8 + 4 * n;
        var nb = new List<byte>(b[..4]);
        nb.AddRange(BitConverter.GetBytes(n + 1));
        nb.AddRange(b[8..end]);
        nb.AddRange(BitConverter.GetBytes(extra));
        nb.AddRange(b[end..]);
        f.Blocks[0] = (t, nb.ToArray());
    }

    /// <summary>The icon pushed out into a slab (like a cookie cutter): front and back faces from the filled cells of
    /// the mask (merged per row), walls where filled meets empty (merged runs). Upright in the XZ plane, half a
    /// side thick. Walls get darker vertex colours so the shape reads as 3D. Both windings (seen from any side).</summary>
    static byte[] Icon3DShape(int name, int shader, int alpha, bool[,] m, float s, (float X, float Y, float Z) at)
    {
        int n = m.GetLength(0);
        float cell = 2 * s / n, t = s * 0.25f;
        float X(int c) => -s + c * cell;
        float Z(int r) => s - r * cell;
        bool F(int r, int c) => r >= 0 && c >= 0 && r < n && c < n && m[r, c];
        var verts = new List<Vert>();
        var tris = new List<(int, int, int)>();
        void Quad((float, float, float) a, (float, float, float) b, (float, float, float) c, (float, float, float) d, (double, double, double) nrm, byte shade)
        {
            int i = verts.Count;
            var tan = Math.Abs(nrm.Item3) > 0.5 ? (1.0, 0.0, 0.0) : (0.0, 0.0, 1.0);
            var bit = (nrm.Item2 * tan.Item3 - nrm.Item3 * tan.Item2, nrm.Item3 * tan.Item1 - nrm.Item1 * tan.Item3, nrm.Item1 * tan.Item2 - nrm.Item2 * tan.Item1);
            foreach (var p in new[] { a, b, c, d }) verts.Add(new Vert(p, (0f, 0f), nrm, tan, bit, new byte[] { shade, shade, shade, 255 }));
            tris.AddRange(new[] { (i, i + 1, i + 2), (i, i + 2, i + 3), (i, i + 2, i + 1), (i, i + 3, i + 2) });
        }
        // front and back: one quad per run of filled cells in a row
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                if (!F(r, c) || F(r, c - 1)) continue;
                int e = c; while (F(r, e + 1)) e++;
                float x0 = X(c), x1 = X(e + 1), z0 = Z(r + 1), z1 = Z(r);
                Quad((x0, -t, z0), (x1, -t, z0), (x1, -t, z1), (x0, -t, z1), (0, -1, 0), 255);
                Quad((x0, t, z0), (x1, t, z0), (x1, t, z1), (x0, t, z1), (0, 1, 0), 255);
            }
        // top / bottom walls: runs along a row where the row above / below is empty
        for (int r = 0; r < n; r++)
            foreach (var (dr, z, shade, nz) in new[] { (-1, Z(r), (byte)200, 1.0), (1, Z(r + 1), (byte)110, -1.0) })
                for (int c = 0; c < n; c++)
                {
                    bool edge(int cc) => F(r, cc) && !F(r + dr, cc);
                    if (!edge(c) || edge(c - 1)) continue;
                    int e = c; while (edge(e + 1)) e++;
                    Quad((X(c), -t, z), (X(e + 1), -t, z), (X(e + 1), t, z), (X(c), t, z), (0, 0, nz), shade);
                }
        // left / right walls: runs down a column where the cell beside is empty
        for (int c = 0; c < n; c++)
            foreach (var (dc, x, nx) in new[] { (-1, X(c), -1.0), (1, X(c + 1), 1.0) })
                for (int r = 0; r < n; r++)
                {
                    bool edge(int rr) => F(rr, c) && !F(rr, c + dc);
                    if (!edge(r) || edge(r - 1)) continue;
                    int e = r; while (edge(e + 1)) e++;
                    Quad((x, -t, Z(e + 1)), (x, t, Z(e + 1)), (x, t, Z(r)), (x, -t, Z(r)), (nx, 0, 0), 150);
                }
        if (verts.Count > 65000) throw new InvalidDataException("3D icon too detailed");
        return Pack(name, shader, alpha, verts, tris, -s, s, s, at);
    }

    /// <summary>A 3D model icon scaled so it is 2 x size tall (like the 3D icon), standing at <paramref name="at"/>.</summary>
    static byte[] ModelShape(int name, int shader, int alpha, Mesh m, float size, (float X, float Y, float Z) at)
    {
        if (m.V.Count > 65000) throw new InvalidDataException("3D model too detailed");
        var verts = new List<Vert>(m.V.Count);
        float half = 0, top = 0;
        foreach (var v in m.V)
        {
            var n = Vector3.Normalize(v.N);
            var t = Vector3.Normalize(Vector3.Cross(Math.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX, n));
            var b = Vector3.Cross(n, t);
            var p = v.P * size;
            half = Math.Max(half, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
            top = Math.Max(top, p.Z);
            verts.Add(new Vert((p.X, p.Y, p.Z), (v.U, v.V), (n.X, n.Y, n.Z), (t.X, t.Y, t.Z), (b.X, b.Y, b.Z),
                               new[] { v.C.R, v.C.G, v.C.B, (byte)255 }));
        }
        return Pack(name, shader, alpha, verts, m.T, 0, top, half, at);
    }

    /// <summary>Box (centre, half extents) of a shape's vertices; null if it has none stored in the shape.</summary>
    static (float, float, float, float, float, float)? VertexBox(byte[] b)
    {
        int q = NifFile.ShapeRefs(b) + 12;
        ulong desc = BitConverter.ToUInt64(b, q); q += 8;
        q += 4; // triangles
        int count = BitConverter.ToUInt16(b, q); q += 2;
        int size = BitConverter.ToInt32(b, q); q += 4;
        int stride = (int)(desc & 0xF) * 4;
        if (size == 0 || stride == 0 || count == 0 || q + count * stride > b.Length) return null;
        bool full = ((desc >> 44) & 0x400) != 0;
        float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
        for (int v = 0; v < count; v++)
        {
            int p = q + v * stride;
            float x, y, z;
            if (full) { x = BitConverter.ToSingle(b, p); y = BitConverter.ToSingle(b, p + 4); z = BitConverter.ToSingle(b, p + 8); }
            else { x = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b, p)); y = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b, p + 2)); z = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b, p + 4)); }
            if (!float.IsFinite(x + y + z)) continue;
            x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z);
        }
        if (x0 > x1) return null;
        return ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, (x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2);
    }

    static void MarkSubtree(NifFile f, int n, HashSet<int> set)
    {
        if (n < 0 || n >= f.Blocks.Count || !set.Add(n)) return;
        if (f.IsNode(n)) foreach (var k in f.Children(n)) MarkSubtree(f, k, set);
    }

    static void Put(byte[] b, int offset, int value) => BitConverter.TryWriteBytes(b.AsSpan(offset), value);

    /// <summary>FO76 BSEffectShaderProperty with a material file: name (= material path), no extra data, no controller.</summary>
    static byte[] EffectShader(int materialString)
    {
        var b = new byte[12];
        Put(b, 0, materialString); Put(b, 4, 0); Put(b, 8, -1);
        return b;
    }

    /// <summary>Additive blending (src alpha, one), threshold 128 - as in the tested beacons. With
    /// <paramref name="test"/>, also alpha test (bit 0x200, pass when greater than the threshold).</summary>
    static byte[] AlphaAdditive(bool test = false, byte threshold = 0x80)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(-1); w.Write(0u); w.Write(-1); w.Write((ushort)(test ? 0x120D : 0x100D)); w.Write(threshold);
        return ms.ToArray();
    }

    static byte NormalByte(double x) => (byte)Math.Clamp((int)Math.Round((x + 1.0) * 127.5), 0, 255);

    record struct Vert((float X, float Y, float Z) P, (float U, float V) Uv, (double, double, double) N, (double, double, double) T, (double, double, double) B, byte[] Col);

    /// <summary>Three crossed vertical quads (both sides), bottom at 0, top at <paramref name="height"/>,
    /// fading to 35% at the top. Same geometry as the Spooky Scorched Beacons beam (kept for the self-test).</summary>
    public static byte[] BeamShape(int name, int shader, int alpha, float height, float halfWidth, float z0 = 0, int planes = 3, float topAlpha = 0.35f)
    {
        var verts = new List<Vert>();
        var tris = new List<(int, int, int)>();
        byte ta = (byte)Math.Round(255 * topAlpha);
        for (int k = 0; k < planes; k++)
        {
            double ang = Math.PI * k / planes, ux = Math.Cos(ang), uy = Math.Sin(ang);
            float W = halfWidth;
            var corners = new[] { ((float)(-ux * W), (float)(-uy * W), z0), ((float)(ux * W), (float)(uy * W), z0), ((float)(ux * W), (float)(uy * W), height), ((float)(-ux * W), (float)(-uy * W), height) };
            var uvs = new[] { (0f, 1f), (1f, 1f), (1f, 0f), (0f, 0f) };
            var alphas = new byte[] { 255, 255, ta, ta };
            int b0 = verts.Count;
            for (int c = 0; c < 4; c++)
                verts.Add(new Vert(corners[c], uvs[c], (-uy, ux, 0.0), (ux, uy, 0.0), (0.0, 0.0, 1.0), new byte[] { 255, 255, 255, alphas[c] }));
            tris.AddRange(new[] { (b0, b0 + 1, b0 + 2), (b0, b0 + 2, b0 + 3), (b0, b0 + 2, b0 + 1), (b0, b0 + 3, b0 + 2) });
        }
        return Pack(name, shader, alpha, verts, tris, z0, height, halfWidth);
    }

    /// <summary>A round beam: a soft outer tube plus a thinner inner core (both two-sided, radial normals).
    /// The beam material's falloff fades surfaces seen edge-on, so the tube reads as a soft glowing column from
    /// every angle - no visible planes/X like the crossed quads. Fades to <paramref name="topAlpha"/> at the top.</summary>
    public static byte[] TubeShape(int name, int shader, int alpha, float height, float radius, (float X, float Y, float Z) at = default,
                                   float z0 = 0, int sides = 16, float topAlpha = 0.25f)
    {
        var verts = new List<Vert>();
        var tris = new List<(int, int, int)>();
        byte ta = (byte)Math.Round(255 * topAlpha);
        void Tube(float r, byte bottomAlpha)
        {
            int b0 = verts.Count;
            for (int j = 0; j <= sides; j++)
            {
                double a = 2 * Math.PI * j / sides, cx = Math.Cos(a), cy = Math.Sin(a);
                float u = (float)j / sides;
                var n = (cx, cy, 0.0);
                var t = (-cy, cx, 0.0);
                var up = (0.0, 0.0, 1.0);
                verts.Add(new Vert(((float)(cx * r), (float)(cy * r), z0), (u, 1f), n, t, up, new byte[] { 255, 255, 255, bottomAlpha }));
                verts.Add(new Vert(((float)(cx * r), (float)(cy * r), height), (u, 0f), n, t, up, new byte[] { 255, 255, 255, (byte)(bottomAlpha * ta / 255) }));
            }
            for (int j = 0; j < sides; j++)
            {
                int v0 = b0 + 2 * j, v1 = v0 + 1, v2 = v0 + 2, v3 = v0 + 3;
                // both windings: the far side of the tube shows through the near side
                tris.AddRange(new[] { (v0, v2, v3), (v0, v3, v1), (v0, v3, v2), (v0, v1, v3) });
            }
        }
        Tube(radius, 200);
        Tube(radius * 0.35f, 255);
        return Pack(name, shader, alpha, verts, tris, z0, height, radius, at);
    }

    /// <summary>Gap between the item's top and the arrow's tip, and the arrow's height, for a given width (radius).</summary>
    public static (float Gap, float Height) ArrowSize(float width) { float r = ArrowRadius(width); return (8, 5 * r); }
    static float ArrowRadius(float width) => Math.Max(6f, width * 2.5f);

    /// <summary>A solid arrow pointing down: a cone (tip at the bottom, just above the item) under a round shaft. Shaded
    /// through vertex colours (lit from above) for the 3D-icon material, so it reads as a solid shape.</summary>
    static byte[] ArrowShape(int name, int shader, int alpha, float width, (float X, float Y, float Z) at, int sides = 20)
    {
        float r = ArrowRadius(width), shaft = r * 0.4f;
        var (gap, height) = ArrowSize(width);
        float tip = gap, headTop = gap + 2 * r, top = gap + height;
        var verts = new List<Vert>();
        var tris = new List<(int, int, int)>();
        byte Shade(double nx, double ny, double nz)
        {
            double l = Math.Max(0, nx * -0.4 + ny * -0.3 + nz * 0.87); // light from above, a little from the side
            return (byte)Math.Clamp(110 + 145 * l, 0, 255);
        }
        void V((float, float, float) p, (double, double, double) n)
        {
            var t = Math.Abs(n.Item3) > 0.9 ? (1.0, 0.0, 0.0) : (-n.Item2, n.Item1, 0.0);
            var b = (n.Item2 * t.Item3 - n.Item3 * t.Item2, n.Item3 * t.Item1 - n.Item1 * t.Item3, n.Item1 * t.Item2 - n.Item2 * t.Item1);
            byte s = Shade(n.Item1, n.Item2, n.Item3);
            verts.Add(new Vert(p, (0f, 0f), n, t, b, new byte[] { s, s, s, 255 }));
        }
        double slope = r / (2.0 * r); // the cone's side normal tilts by its radius / length
        for (int j = 0; j < sides; j++)
        {
            double a0 = 2 * Math.PI * j / sides, a1 = 2 * Math.PI * (j + 1) / sides, am = (a0 + a1) / 2;
            float c0 = (float)Math.Cos(a0), s0 = (float)Math.Sin(a0), c1 = (float)Math.Cos(a1), s1 = (float)Math.Sin(a1);
            int i = verts.Count;
            // cone side: tip, two base corners
            var cn = (Math.Cos(am), Math.Sin(am), -slope);
            double cl = Math.Sqrt(cn.Item1 * cn.Item1 + cn.Item2 * cn.Item2 + cn.Item3 * cn.Item3);
            cn = (cn.Item1 / cl, cn.Item2 / cl, cn.Item3 / cl);
            V((0, 0, tip), cn); V((c0 * r, s0 * r, headTop), cn); V((c1 * r, s1 * r, headTop), cn);
            tris.Add((i, i + 2, i + 1));
            // the head's flat top (ring around the shaft) and the shaft's side and top cap
            i = verts.Count;
            V((c0 * r, s0 * r, headTop), (0, 0, 1)); V((c1 * r, s1 * r, headTop), (0, 0, 1)); V((c1 * shaft, s1 * shaft, headTop), (0, 0, 1)); V((c0 * shaft, s0 * shaft, headTop), (0, 0, 1));
            tris.AddRange(new[] { (i, i + 1, i + 2), (i, i + 2, i + 3) });
            i = verts.Count;
            var sn = (Math.Cos(am), Math.Sin(am), 0.0);
            V((c0 * shaft, s0 * shaft, headTop), sn); V((c1 * shaft, s1 * shaft, headTop), sn); V((c1 * shaft, s1 * shaft, top), sn); V((c0 * shaft, s0 * shaft, top), sn);
            tris.AddRange(new[] { (i, i + 1, i + 2), (i, i + 2, i + 3) });
            i = verts.Count;
            V((0, 0, top), (0, 0, 1)); V((c0 * shaft, s0 * shaft, top), (0, 0, 1)); V((c1 * shaft, s1 * shaft, top), (0, 0, 1));
            tris.Add((i, i + 1, i + 2));
        }
        // both windings: seen from any side even where the material culls back faces
        int n = tris.Count;
        for (int k = 0; k < n; k++) tris.Add((tris[k].Item1, tris[k].Item3, tris[k].Item2));
        return Pack(name, shader, alpha, verts, tris, tip, top, r, at);
    }

    static byte[] Pack(int name, int shader, int alpha, List<Vert> verts, List<(int, int, int)> tris, float z0, float height, float halfWidth,
                       (float X, float Y, float Z) at = default)
    {
        var data = new MemoryStream();
        var dw = new BinaryWriter(data);
        foreach (var (p, uv, n, t, bt, col) in verts)
        {
            dw.Write((Half)p.X); dw.Write((Half)p.Y); dw.Write((Half)p.Z); dw.Write((Half)bt.Item1);
            dw.Write((Half)uv.U); dw.Write((Half)uv.V);
            dw.Write(new[] { NormalByte(n.Item1), NormalByte(n.Item2), NormalByte(n.Item3), NormalByte(bt.Item2) });
            dw.Write(new[] { NormalByte(t.Item1), NormalByte(t.Item2), NormalByte(t.Item3), NormalByte(bt.Item3) });
            dw.Write(col);
        }
        foreach (var (a, b, c) in tris) { dw.Write((ushort)a); dw.Write((ushort)b); dw.Write((ushort)c); }
        var vdata = data.ToArray();

        float hb = (height - z0) / 2;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        // NiAVObject: name, 0 extra, no controller, flags 0x0E, transform (translation only), no collision
        w.Write(name); w.Write(0u); w.Write(-1); w.Write(0x0Eu);
        w.Write(at.X); w.Write(at.Y); w.Write(at.Z);
        foreach (var v in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) w.Write(v);
        w.Write(1f); w.Write(-1);
        // bound sphere + box (centre, half extents)
        w.Write(0f); w.Write(0f); w.Write(z0 + hb); w.Write((float)Math.Sqrt(hb * hb + halfWidth * halfWidth));
        w.Write(0f); w.Write(0f); w.Write(z0 + hb); w.Write(halfWidth); w.Write(halfWidth); w.Write(hb);
        w.Write(-1); w.Write(shader); w.Write(alpha);
        w.Write(BeamVertexDesc);
        w.Write((uint)tris.Count); w.Write((ushort)verts.Count); w.Write((uint)vdata.Length);
        w.Write(vdata);
        return ms.ToArray();
    }
}
