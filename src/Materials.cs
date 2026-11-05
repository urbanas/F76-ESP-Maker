using System.Drawing;
using System.Globalization;

namespace ItemFinder;

/// <summary>Effect materials (.bgem v22) made from the game's own AmbDefaultFX.bgem, with the changes
/// proven in game for Spooky Scorched Beacons (Oct 2026): depth test off + soft-particle fade off =
/// visible through walls; lighting off and light influence 0 = same brightness day and night.</summary>
public static class Materials
{
    public const string Template = @"materials\effects\ambient\ambdefaultfx.bgem";
    const string TemplateTexture = "Effects/AmbientBeams_d.dds";

    // header (before the texture string; same layout in .bgsm and .bgem v22)
    const int BlendSrc = 0x21, BlendDst = 0x25, AlphaTestRef = 0x29, AlphaTest = 0x2A, ZWrite = 0x2B, ZTest = 0x2C, Alpha = 0x1C, TextureLength = 0x3C, TextureStart = 0x40;
    // offsets in the stock file (texture string ends at 0x5B); shifted by the texture length change
    const int Lighting = 0x8F, Falloff = 0x90, Soft = 0x93, Color = 0x94, Scale = 0xA0, LightInfluence = 0xB4;
    // falloff: start angle, stop angle (cosines of the view angle), start opacity, stop opacity
    const int FalloffAngles = 0xA4;

    /// <summary>Outline instead of a full cover: almost clear where the surface faces the camera, full colour towards
    /// the edges (the Pint-Sized outline mods use the same falloff idea).</summary>
    /// <summary>The outline's opacity on faces that point at you.</summary>
    public const float OutlineFace = 0.18f;

    static void Outline(byte[] m)
    {
        int d = BitConverter.ToInt32(m, TextureLength) - TemplateTexture.Length - 1;
        m[Falloff + d] = 1;
        // facing you: a faint tint (18 %) so flat faces - a wall safe seen head-on - still show; full colour at the edges
        foreach (var (i, v) in new[] { (0, 0.6f), (1, 0.1f), (2, OutlineFace), (3, 1f) })
            BitConverter.TryWriteBytes(m.AsSpan(FalloffAngles + d + 4 * i), v);
    }

    /// <summary>Icon on the beam: its own white texture tinted with the colour, no falloff - like the tested
    /// Item Finder cookie icon.</summary>
    public static byte[] Icon(byte[] template, Color c, bool throughWalls, string texture) =>
        Make(template, texture, c, 1.3f, falloff: false, throughWalls, alpha: null);

    /// <summary>3D icon: untextured, lit only by its vertex colours (darker walls), added light like the glow.</summary>
    public static byte[] Icon3D(byte[] template, Color c, bool throughWalls)
    {
        var m = Make(template, "", c, 1.4f, falloff: false, throughWalls, alpha: 1f);
        Additive(m);
        return m;
    }

    /// <summary>3D model icon: its own texture (times the colour - white for models in their own colours), as the
    /// Spooky Scorched Beacons pumpkin: alpha 0.75, normal blending, colour scale 1.3, one-sided.</summary>
    public static byte[] Model(byte[] template, Color c, bool throughWalls, string texture)
    {
        var m = Make(template, texture, c, 1.3f, falloff: false, throughWalls, alpha: 0.75f);
        BitConverter.TryWriteBytes(m.AsSpan(BlendSrc), 6); BitConverter.TryWriteBytes(m.AsSpan(BlendDst), 7);
        m[0x30] = 0; // one-sided
        if (!throughWalls) m[ZWrite] = 1; // hides its own back (the clown's hat behind the head)
        return m;
    }

    /// <param name="strengthPercent">How visible: 100 % = the tested Spooky beam (alpha 0.75, colour scale 1.5).
    /// Below 100 the beam gets more see-through (the material blends normally, so dimming the colour only made
    /// it a darker stripe); above 100 first fully opaque, then brighter.</param>
    public static byte[] Beam(byte[] template, Color c, bool throughWalls, int strengthPercent = 100)
    {
        float s = Math.Clamp(strengthPercent, 1, 300) / 100f;
        if (strengthPercent == 100) return Make(template, TemplateTexture, c, 1.5f, falloff: true, throughWalls, alpha: null);
        float alpha = Math.Min(1f, 0.75f * s);
        float scale = 1.5f * Math.Max(1f, s * 0.75f);
        return Make(template, TemplateTexture, c, scale, falloff: true, throughWalls, alpha: alpha);
    }

    /// <summary>Untextured see-through glow laid over the item's own shapes.</summary>
    /// <param name="additive">Light added on top (item stays visible); strength sets the brightness
    /// (100 % = colour scale 1.0 - in game already very bright). False = normal blending with strength as opacity, as in the tested Spooky
    /// body glow (kept for the self-test comparison).</param>
    public static byte[] Glow(byte[] template, Color c, int strengthPercent, bool throughWalls, bool additive = true)
    {
        float s = Math.Clamp(strengthPercent, 5, 100) / 100f;
        if (!additive) return Make(template, "", c, 1.0f, falloff: false, throughWalls, alpha: s);
        var m = Make(template, "", c, 1.0f * s, falloff: false, throughWalls, alpha: 1f);
        Additive(m);
        return m;
    }

    /// <summary>Test variants of the glow, both untextured. B: normal blending (source alpha, inverse source alpha) with
    /// the strength as opacity, like other ESP mods' overlays. C: added light, set in the material only.</summary>
    public static byte[] GlowVariant(byte[] template, Color c, int strengthPercent, bool throughWalls, bool normalBlend, bool outline = false)
    {
        float s = Math.Clamp(strengthPercent, 5, 100) / 100f;
        if (normalBlend)
        {
            // an outline only colours the edges, so it can be more opaque there
            // filled: a see-through overlay (opacity 0.28, colour strength 0.52) at the default 30 %
            var b = Make(template, "", c, outline ? 1.0f : 0.52f, falloff: false, throughWalls, alpha: outline ? 0.3f + 0.7f * s : Math.Min(1f, 0.28f * s / 0.3f));
            BitConverter.TryWriteBytes(b.AsSpan(BlendSrc), 6);
            BitConverter.TryWriteBytes(b.AsSpan(BlendDst), 7);
            if (outline)
            {
                Outline(b);
                // one-sided: the far side's faces point away from you, so the falloff makes them fully opaque - drawn
                // through the item (visible through walls) they washed the whole thing out white (bobblehead in game)
                b[0x30] = 0;
            }
            return b;
        }
        var m = Make(template, "", c, 1.0f * s, falloff: false, throughWalls, alpha: 1f);
        Additive(m);
        return m;
    }

    /// <summary>Blend source alpha + destination one (blend functions use the NiAlphaProperty numbering:
    /// 0 one, 6 source alpha, 7 inverse source alpha).</summary>
    static void Additive(byte[] m)
    {
        BitConverter.TryWriteBytes(m.AsSpan(BlendSrc), 6);
        BitConverter.TryWriteBytes(m.AsSpan(BlendDst), 0);
    }

    /// <summary>Glow for a cut-out part (leaf cards, grilles...): uses the part's own colour texture and its alpha
    /// test, so only the cut-out shape lights up instead of the whole rectangle. The texture darkens the colour,
    /// so it is brightened twice as much as a plain glow (100 % = colour scale 2.0).</summary>
    /// <param name="white">The texture is a white cut-out mask (not the part's own colours, which darken it): less boost.</param>
    public static byte[] CutoutGlow(byte[] template, Color c, int strengthPercent, bool throughWalls, string texture, byte testRef, bool outline = false,
                                    bool white = false)
    {
        float s = Math.Clamp(strengthPercent, 5, 100) / 100f;
        var m = Make(template, texture, c, (white ? 1.4f : 2f) * s, falloff: false, throughWalls, alpha: 1f);
        Additive(m);
        m[AlphaTest] = 1;
        m[AlphaTestRef] = testRef;
        if (outline) Outline(m);
        return m;
    }

    /// <summary>Colour texture and alpha-test threshold of a game material (.bgsm/.bgem v22) whose alpha test is
    /// on; null when it has none. Throws if it can't be read.</summary>
    public static (string Texture, byte Ref)? Cutout(byte[] mat)
    {
        if (mat.Length < 0x40 || BitConverter.ToInt32(mat, 4) != 22) throw new InvalidDataException("unexpected material version");
        if (mat[AlphaTest] == 0) return null;
        int len = BitConverter.ToInt32(mat, TextureLength);
        var tex = System.Text.Encoding.ASCII.GetString(mat, TextureStart, Math.Max(0, len - 1));
        if (tex.Length == 0) throw new InvalidDataException("cut-out material without a texture");
        return (tex, mat[AlphaTestRef]);
    }

    // .bgsm v22 after the header: 10 texture strings (slot 4 = glow map), 75 fixed bytes, the root material's name, one byte,
    // "emissive on"; its colour (3 floats) only exists when it's on; then the emissive strength, the glow map flag 28 bytes on
    const int BgsmSlots = 10, GlowSlot = 4, BgsmRoot = 75, GlowMapFlag = 28;

    /// <summary>Where a .bgsm keeps its glow settings; null for anything but a readable v22 .bgsm.</summary>
    /// <param name="GlowAt">The glow map's texture string.</param>
    /// <param name="Flag">"Emissive on".</param>
    internal sealed record Emissive(int GlowAt, int GlowLength, int Flag, int Strength, int GlowMap);

    internal static Emissive? ReadEmissive(byte[] m)
    {
        if (m.Length < 0x40 || m[0] != 'B' || m[1] != 'G' || m[2] != 'S' || m[3] != 'M' || BitConverter.ToInt32(m, 4) != 22) return null;
        int at = TextureLength, glowAt = 0, glowLength = 0;
        for (int i = 0; i < BgsmSlots; i++)
        {
            if (at + 4 > m.Length) return null;
            int n = BitConverter.ToInt32(m, at);
            if (n < 0 || at + 4 + n > m.Length) return null;
            if (i == GlowSlot) (glowAt, glowLength) = (at, n);
            at += 4 + n;
        }
        int root = at + BgsmRoot;
        if (root + 4 > m.Length) return null;
        int rootLength = BitConverter.ToInt32(m, root);
        int flag = root + 4 + rootLength + 1;
        if (rootLength < 0 || flag >= m.Length || m[flag] > 1) return null;
        int strength = flag + 1 + (m[flag] == 1 ? 12 : 0);
        int map = strength + GlowMapFlag;
        return map < m.Length && m[map] <= 1 ? new Emissive(glowAt, glowLength, flag, strength, map) : null;
    }

    /// <summary>A game material whose own colour texture lights it up, like the game's own Rad Mothman eggs (their glow
    /// map is their colour texture), at <paramref name="strength"/> x full. A material with a glow map of its own
    /// keeps it, only stronger. Null when the material can't be read or has no colour texture.</summary>
    public static byte[]? Brighten(byte[] src, float strength)
    {
        if (ReadEmissive(src) is not Emissive e) return null;
        int diffuse = BitConverter.ToInt32(src, TextureLength);
        if (diffuse <= 1) return null;
        // (some have it all set up but at strength 0: switched off, those are treated as not glowing)
        if (src[e.Flag] == 1 && src[e.GlowMap] == 1 && e.GlowLength > 1 && BitConverter.ToSingle(src, e.Strength) > 0)
        {
            var same = (byte[])src.Clone();
            BitConverter.TryWriteBytes(same.AsSpan(e.Strength), BitConverter.ToSingle(src, e.Strength) * strength);
            return same;
        }
        var b = new List<byte>(src.Length + diffuse + 16);
        b.AddRange(src[..e.GlowAt]);
        b.AddRange(src[TextureLength..(TextureStart + diffuse)]); // the colour texture's string again
        b.AddRange(src[(e.GlowAt + 4 + e.GlowLength)..e.Flag]);
        b.Add(1);
        for (int i = 0; i < 3; i++) b.AddRange(BitConverter.GetBytes(1f)); // white: the texture's own colours
        b.AddRange(BitConverter.GetBytes(strength));
        b.AddRange(src[(e.Strength + 4)..]);
        var m = b.ToArray();
        m[ReadEmissive(m)!.GlowMap] = 1;
        return m;
    }

    static byte[] Make(byte[] src, string texture, Color c, float scale, bool falloff, bool throughWalls, float? alpha)
    {
        if (src.Length < 0x40 || src[0] != 'B' || src[1] != 'G' || src[2] != 'E' || src[3] != 'M' || BitConverter.ToInt32(src, 4) != 22)
            throw new InvalidDataException("AmbDefaultFX.bgem is not the expected version 22 effect material");
        int oldLen = BitConverter.ToInt32(src, TextureLength);
        var tex = System.Text.Encoding.ASCII.GetBytes(texture + "\0");
        var b = new List<byte>(src[..TextureLength]);
        b.AddRange(BitConverter.GetBytes(tex.Length));
        b.AddRange(tex);
        b.AddRange(src[(TextureStart + oldLen)..]);
        var m = b.ToArray();
        int d = tex.Length - oldLen;
        m[ZTest] = (byte)(throughWalls ? 0 : 1);
        m[ZWrite] = 0;
        if (throughWalls) m[Soft + d] = 0; // soft-particle depth fade would erase it behind walls
        m[Lighting + d] = 0;
        if (!falloff) m[Falloff + d] = 0;
        if (alpha is float a) BitConverter.TryWriteBytes(m.AsSpan(Alpha), a);
        BitConverter.TryWriteBytes(m.AsSpan(Color + d), c.R / 255f);
        BitConverter.TryWriteBytes(m.AsSpan(Color + d + 4), c.G / 255f);
        BitConverter.TryWriteBytes(m.AsSpan(Color + d + 8), c.B / 255f);
        BitConverter.TryWriteBytes(m.AsSpan(Scale + d), scale);
        BitConverter.TryWriteBytes(m.AsSpan(LightInfluence + d), 0f);
        return m;
    }

    public static Color Parse(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        return hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, null, out int v)
            ? System.Drawing.Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255)
            : System.Drawing.Color.Cyan;
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
