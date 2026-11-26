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
    const int TileFlags = 0x08, VScale = 0x18;
    const int BlendSrc = 0x21, BlendDst = 0x25, AlphaTestRef = 0x29, AlphaTest = 0x2A, ZWrite = 0x2B, ZTest = 0x2C, Alpha = 0x1C, TextureLength = 0x3C, TextureStart = 0x40;
    // offsets in the stock file (texture string ends at 0x5B); shifted by the texture length change
    const int Lighting = 0x8F, Falloff = 0x90, Soft = 0x93, Color = 0x94, Scale = 0xA0, LightInfluence = 0xB4;
    // falloff: start angle, stop angle (cosines of the view angle), start opacity, stop opacity
    const int FalloffAngles = 0xA4;

    /// <summary>Outline instead of a full cover: almost clear where the surface faces the camera, full colour towards
    /// the edges (the Pint-Sized outline mods use the same falloff idea).</summary>
    /// <summary>The outline's opacity on faces that point at you.</summary>
    /// <remarks>0.18 up to 59 % (the edges are already full there); above it the tint spreads over the faces, up to 0.68 at
    /// 100 %, so 60-100 % still gets stronger (user, 2026-11-09: no change above 60 %).</remarks>
    public static float OutlineFace(float s) => s <= 0.59f ? 0.18f : 0.18f + Math.Min(1f, (s - 0.59f) / 0.41f) * 0.5f;

    /// <summary>Edge opacity of a normal-blend outline: 0.51 at the default 30 % (as before), full from 59 %,
    /// and fading to almost nothing at 5 % (the old 0.3 floor kept 5 % bright in game).</summary>
    public static float OutlineAlpha(float s) => Math.Min(1f, 1.7f * s);

    /// <summary>The tight outline's falloff (GlowVariant tight): clear from this facing (cos) on, full colour below the stop.</summary>
    public const float TightStart = 0.3f, TightStop = 0.05f;

    static void Outline(byte[] m, float s)
    {
        int d = BitConverter.ToInt32(m, TextureLength) - TemplateTexture.Length - 1;
        m[Falloff + d] = 1;
        // facing you: a faint tint (18 %) so flat faces - a wall safe seen head-on - still show; full colour at the edges
        foreach (var (i, v) in new[] { (0, 0.6f), (1, 0.1f), (2, OutlineFace(s)), (3, 1f) })
            BitConverter.TryWriteBytes(m.AsSpan(FalloffAngles + d + 4 * i), v);
    }

    /// <summary>Icon on the beam: its own white texture tinted with the colour, no falloff - like the tested
    /// Item Finder cookie icon.</summary>
    /// <param name="strength">1 = as before; below 1 more see-through, above 1 brighter (the look's icon / text strength).</param>
    public static byte[] Icon(byte[] template, Color c, bool throughWalls, string texture, float strength = 1) =>
        Make(template, texture, c, 1.3f * Math.Max(1, strength), falloff: false, throughWalls, alpha: strength < 1 ? strength : null);

    /// <summary>3D icon: untextured, lit only by its vertex colours (darker walls), added light like the glow.</summary>
    public static byte[] Icon3D(byte[] template, Color c, bool throughWalls, float strength = 1)
    {
        var m = Make(template, "", c, 1.4f * Math.Max(1, strength), falloff: false, throughWalls, alpha: Math.Min(1, strength));
        Additive(m);
        return m;
    }

    /// <summary>3D model icon: its own texture (times the colour - white for models in their own colours), as the
    /// Spooky Scorched Beacons pumpkin: alpha 0.75, normal blending, colour scale 1.3, one-sided.</summary>
    public static byte[] Model(byte[] template, Color c, bool throughWalls, string texture, float strength = 1)
    {
        var m = Make(template, texture, c, 1.3f * Math.Max(1, strength), falloff: false, throughWalls, alpha: Math.Min(1, 0.75f * strength));
        BitConverter.TryWriteBytes(m.AsSpan(BlendSrc), 6); BitConverter.TryWriteBytes(m.AsSpan(BlendDst), 7);
        m[0x30] = 0; // one-sided
        if (!throughWalls) m[ZWrite] = 1; // hides its own back (the clown's hat behind the head)
        return m;
    }

    /// <param name="strengthPercent">How visible: 100 % = the tested Spooky beam (alpha 0.75, colour scale 1.5).
    /// Below 100 the beam gets more see-through (the material blends normally, so dimming the colour only made
    /// it a darker stripe); above 100 first fully opaque, then brighter.</param>
    public static byte[] Beam(byte[] template, Color c, bool throughWalls, int strengthPercent = 100, string? flowTexture = null, float flowTiles = 1)
    {
        float s = Math.Clamp(strengthPercent, 1, 300) / 100f;
        string tex = flowTexture ?? TemplateTexture;
        var m = strengthPercent == 100 ? Make(template, tex, c, 1.5f, falloff: true, throughWalls, alpha: null)
                                       : Make(template, tex, c, 1.5f * Math.Max(1f, s * 0.75f), falloff: true, throughWalls, alpha: Math.Min(1f, 0.75f * s));
        if (flowTexture != null)
        {
            // flowing beam: the banded texture repeats up the beam (tile U + V), the V offset is animated in the model
            BitConverter.TryWriteBytes(m.AsSpan(TileFlags), 3u);
            BitConverter.TryWriteBytes(m.AsSpan(VScale), flowTiles);
        }
        return m;
    }

    /// <summary>The banded texture of the flowing beam: soft bright bands along V (white, alpha = brightness).</summary>
    public static Bitmap FlowTexture()
    {
        var bmp = new Bitmap(32, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < 128; y++)
        {
            double band = 0.25 + 0.75 * Math.Pow(0.5 + 0.5 * Math.Sin(2 * Math.PI * y / 128), 3);
            int a = (int)Math.Round(255 * band);
            for (int x = 0; x < 32; x++) bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(a, 255, 255, 255));
        }
        return bmp;
    }

    /// <summary>Game units per repeat of the scanline texture (57 cm): NifPatch writes the glow copy's V as -height / this.</summary>
    public const float ScanTile = 40f;
    /// <summary>Seconds for the scanlines to move one repeat up (V offset 0 -> 1): 7 cm/s, slow enough that the fine lines
    /// don't flicker.</summary>
    public const float ScanPeriod = 8f;
    /// <summary>Lines per repeat: one every 5 units (7 cm; 2.4 cm lines only showed up really close in game, user 2026-11-25).</summary>
    const int ScanLines = 8;

    /// <summary>Scanlines: added light through the banded <see cref="ScanTexture"/> (white, alpha = brightness), tiled; the
    /// glow copy's UVs follow its height (NifPatch.HeightUvs) and the V offset is animated, so the lines run up.
    /// Brightness like a cut-out glow (the texture is dark between the lines): 100 % = colour scale 2.0.</summary>
    public static byte[] ScanGlow(byte[] template, Color c, int strengthPercent, bool throughWalls, string texture)
    {
        float s = Math.Clamp(strengthPercent, 5, 100) / 100f;
        var m = Make(template, texture, c, ScanScale(s), falloff: false, throughWalls, alpha: 1f);
        Additive(m);
        BitConverter.TryWriteBytes(m.AsSpan(TileFlags), 3u);
        return m;
    }

    /// <summary>Colour scale of the scanline material at strength s (0.05..1): 2.5 x (the texture is mostly dark).</summary>
    public static float ScanScale(float s) => 2.5f * s;

    /// <summary>Brightness of the scanline texture at v (0..1, one repeat): <see cref="ScanLines"/> even lines (at 0.6,
    /// softer beside it, a faint tint between) with a slight slow ripple, as the Slasher mod's bands.dds. The preview uses the
    /// same curve.</summary>
    public static float ScanBand(float v)
    {
        v -= MathF.Floor(v);
        // where in its step, in repeats: the line and its soft edges keep a fixed width (3/256 of a repeat each, 0.47 units =
        // 7 mm; 1/256 was too thin to see beyond arm's length, user 2026-11-25)
        float at = v * ScanLines % 1f / ScanLines;
        const float w = 3f / 256;
        float line = at < w ? 0.6f : at < 2 * w || at > 1f / ScanLines - w ? 0.2f : 0.12f;
        return line * (0.9f + 0.1f * MathF.Cos(2 * MathF.PI * v * 4));
    }

    public static Bitmap ScanTexture()
    {
        const int h = ScanLines * 32; // 32 rows per step: 3 rows of line, 3 soft rows each side
        var bmp = new Bitmap(16, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
        {
            int a = (int)Math.Round(255 * ScanBand((y + 0.5f) / h));
            for (int x = 0; x < 16; x++) bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(a, 255, 255, 255));
        }
        return bmp;
    }

    /// <summary>Sonar ping disc: the ring texture (PingTexture) running from the middle out by the V offset (like the
    /// flowing beam), added light, no falloff (seen almost edge-on from standing height), through walls.</summary>
    public static byte[] Ping(byte[] template, Color c, string texture)
    {
        var m = Make(template, texture, c, 1.4f, falloff: false, throughWalls: true, alpha: 1f);
        Additive(m);
        BitConverter.TryWriteBytes(m.AsSpan(TileFlags), 3u);
        BitConverter.TryWriteBytes(m.AsSpan(VScale), 1f);
        return m;
    }

    /// <summary>The ping's ring along V: one bright line with a soft glow either side, clear everywhere else.</summary>
    public static Bitmap PingTexture()
    {
        var bmp = new Bitmap(16, 256, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < 256; y++)
        {
            double d = Math.Abs(y - 128) / 128.0; // 0 at the line
            double k = Math.Max(Math.Exp(-d * d / 0.0008), 0.45 * Math.Exp(-d * d / 0.006));
            int a = (int)Math.Round(255 * k);
            for (int x = 0; x < 16; x++) bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(a, 255, 255, 255));
        }
        return bmp;
    }

    /// <summary>The colour strength (base colour scale) a material was written with - the pulse animates around it.</summary>
    public static float BaseScale(byte[] m)
    {
        int d = BitConverter.ToInt32(m, TextureLength) - TemplateTexture.Length - 1;
        return Scale + d + 4 <= m.Length ? BitConverter.ToSingle(m, Scale + d) : 1f;
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
    /// <param name="tight">Outline only on the outermost edge, faces clear (a solid area inside a cut-out, e.g. the
    /// carrot's root: small and round, the normal outline covered all of it - user, 2026-11-26).</param>
    public static byte[] GlowVariant(byte[] template, Color c, int strengthPercent, bool throughWalls, bool normalBlend, bool outline = false, bool tight = false)
    {
        float s = Math.Clamp(strengthPercent, 5, 100) / 100f;
        if (normalBlend)
        {
            // an outline only colours the edges, so it can be more opaque there
            // filled: a see-through overlay (opacity 0.28, colour strength 0.52) at the default 30 %
            var b = Make(template, "", c, outline ? 1.0f : 0.52f, falloff: false, throughWalls, alpha: outline ? OutlineAlpha(s) : Math.Min(1f, 0.28f * s / 0.3f));
            BitConverter.TryWriteBytes(b.AsSpan(BlendSrc), 6);
            BitConverter.TryWriteBytes(b.AsSpan(BlendDst), 7);
            if (outline)
            {
                Outline(b, s);
                if (tight)
                {
                    int d = BitConverter.ToInt32(b, TextureLength) - TemplateTexture.Length - 1;
                    foreach (var (i, v) in new[] { (0, TightStart), (1, TightStop), (2, 0f), (3, 1f) })
                        BitConverter.TryWriteBytes(b.AsSpan(FalloffAngles + d + 4 * i), v);
                }
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
        if (outline) Outline(m, s);
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

    /// <summary>The colour texture of a game material (.bgsm/.bgem v22); "" when it has none.</summary>
    public static string Diffuse(byte[] mat)
    {
        if (mat.Length < TextureStart || BitConverter.ToInt32(mat, 4) != 22) return "";
        int len = BitConverter.ToInt32(mat, TextureLength);
        return len <= 1 || TextureStart + len > mat.Length ? "" : System.Text.Encoding.ASCII.GetString(mat, TextureStart, len - 1);
    }

    /// <summary>X-ray copy for the Bright look: the part's own colour texture, unlit, seen through walls, added light like
    /// the cut-out glow (proven through walls). Normal blending without an alpha block on the shape did NOT show through
    /// walls in game (2026-11-14).</summary>
    /// <param name="scale">Colour scale; 2 washed a brahmin out to plain white (2026-11-14) - now 0.35 x the shine strength.</param>
    public static byte[] Xray(byte[] template, string texture, byte? cutRef, float scale)
    {
        var m = Make(template, texture, System.Drawing.Color.White, scale, falloff: false, throughWalls: true, alpha: 1f);
        Additive(m);
        if (cutRef is byte r) { m[AlphaTest] = 1; m[AlphaTestRef] = r; }
        return m;
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
    /// <remarks>Not for rules.json "brightGlow" materials (the builder gives those a fill glow instead).</remarks>
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

    // Razorgrain (2026-11-17): Bright draws it as shattered mirror shards. Its material has the "tree" (wind-sway) flag
    // (after the glow map flag: hair 1 byte, hair tint 12, then tree). Tree off = invisible (the wind data in the vertex
    // alpha becomes see-through); tree + emissive without a map = no effect. Corn has the flag too and is fine with
    // Bright, so it isn't the flag alone: the broken ones are listed in rules.json "brightGlow" (fill glow instead).

    /// <summary>The colour texture's string (for the average colour of a "brightGlow" material).</summary>
    public static string DiffuseOf(byte[] m) => System.Text.Encoding.ASCII.GetString(m, TextureStart, Math.Max(0, BitConverter.ToInt32(m, TextureLength) - 1));

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
        // soft-particle depth fade off always: the copy lies on the item's own surface (depth gap 0), so the fade erased
        // it completely when visible-through-walls was off (salt / pepper report, 2026-11-18), and behind walls when on
        m[Soft + d] = 0;
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
