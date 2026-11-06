using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ItemFinder;

/// <summary>Small icon library for the top of the beam: symbols drawn from a Windows symbol font into a white
/// texture (the colour comes from the material, so one texture serves every colour). Saved as uncompressed
/// 32-bit DDS with mipmaps - the format the Spooky Beacons pumpkin proved in game.</summary>
public static class Icons
{
    /// <param name="Emoji">Drawn from the Segoe UI Emoji outlines (plain shape, coloured by the material).</param>
    /// <param name="Custom">Drawn by hand instead of from a font.</param>
    public sealed record Icon(string Id, string Name, string Glyph, bool Emoji = false, Func<GraphicsPath>? Custom = null);

    static Icon E(string id, string name, int codepoint) => new(id, name, char.ConvertFromUtf32(codepoint), Emoji: true);

    public static readonly Icon[] All =
    {
        new("star", "Star", "★"),
        new("diamond", "Diamond", "◆"),
        new("circle", "Dot", "●"),
        new("arrow", "Arrow", "▼"),
        new("exclaim", "Alert", "!"),
        new("question", "Question", "?"),
        new("skull", "Skull", "☠"),
        new("warning", "Warning", "⚠"),
        new("cross", "Cross", "✚"),
        new("heart", "Heart", "♥"),
        new("bolt", "Lightning", "⚡"),
        new("dollar", "Dollar", "$"),
        new("bobblehead", "Bobblehead", "", Custom: Bobblehead),
        E("magazine", "Magazine", 0x1F4F0), E("scroll", "Plan", 0x1F4DC), E("book", "Book", 0x1F4D6),
        E("apple", "Apple", 0x1F34E), E("drumstick", "Drumstick", 0x1F357), E("meat", "Meat", 0x1F356),
        E("cherries", "Cherries", 0x1F352), E("can", "Canned food", 0x1F96B), E("herb", "Herb", 0x1F33F),
        E("leaf", "Leaf", 0x1F343), E("seedling", "Seedling", 0x1F331), E("flower", "Flower", 0x1F33C),
        E("mushroom", "Mushroom", 0x1F344), E("key", "Key", 0x1F511), E("moneybag", "Money bag", 0x1F4B0),
        E("gift", "Gift", 0x1F381), E("crown", "Crown", 0x1F451), E("target", "Target", 0x1F3AF),
        E("bone", "Bone", 0x1F9B4), E("bug", "Bug", 0x1F41B), E("gear", "Gear", 0x2699),
        E("radiation", "Radiation", 0x2622), E("pumpkin", "Pumpkin", 0x1F383), E("ghost", "Ghost", 0x1F47B),
    };

    /// <summary>A generic bobblehead: big round head on a small body standing on a base.</summary>
    static GraphicsPath Bobblehead()
    {
        // overlapping parts merge (winding fill); the face is cut out by the same shapes drawn the other way round
        var p = new GraphicsPath(FillMode.Winding);
        p.AddEllipse(18, 0, 64, 58);                   // big head
        p.AddRectangle(new RectangleF(45, 54, 10, 8));  // neck
        p.AddEllipse(33, 58, 34, 26);                  // body
        p.AddRectangle(new RectangleF(38, 80, 24, 8));  // stand
        p.AddEllipse(20, 85, 60, 13);                  // base
        var face = new GraphicsPath(FillMode.Winding);
        face.AddEllipse(34, 18, 9, 12);                // eyes
        face.AddEllipse(57, 18, 9, 12);
        var smile = new List<PointF>();
        for (int a = 25; a <= 155; a += 10) smile.Add(Arc(50, 30, 18, a));
        for (int a = 155; a >= 25; a -= 10) smile.Add(Arc(50, 30, 12, a));
        face.AddPolygon(smile.ToArray());
        face.Reverse();
        p.AddPath(face, false);
        return p;
    }

    static PointF Arc(float cx, float cy, float r, int degrees) =>
        new(cx + r * (float)Math.Cos(degrees * Math.PI / 180), cy + r * (float)Math.Sin(degrees * Math.PI / 180));

    public static Icon? Get(string id) => All.FirstOrDefault(i => i.Id == id);

    public static string TexturePath(string id) => $@"textures\itemfinder\icon_{id}.dds";
    /// <summary>The same path as material files write it (inside textures\).</summary>
    public static string MaterialTexture(string id) => $"itemfinder/icon_{id}.dds";

    /// <summary>The icon's outline, centred in a size x size square and filling 78 % of it.</summary>
    public static GraphicsPath Outline(Icon icon, int size)
    {
        string family = icon.Emoji ? "Segoe UI Emoji" : "Segoe UI Symbol";
        if (!FontFamily.Families.Any(f => f.Name == family)) family = "Segoe UI";
        using var font = new Font(family, size * 0.62f, icon.Emoji ? FontStyle.Regular : FontStyle.Bold, GraphicsUnit.Pixel);
        var path = icon.Custom?.Invoke() ?? new GraphicsPath();
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        if (icon.Custom == null) path.AddString(icon.Glyph, font.FontFamily, (int)font.Style, font.Size, new RectangleF(0, 0, size, size), fmt);
        // centre the glyph's real outline (symbol fonts sit high or low in their box)
        var b = path.GetBounds();
        using (var m = new Matrix())
        {
            float scale = Math.Min(size * 0.78f / Math.Max(b.Width, 1), size * 0.78f / Math.Max(b.Height, 1));
            m.Translate(size / 2f, size / 2f);
            m.Scale(scale, scale);
            m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
            path.Transform(m);
        }
        return path;
    }

    /// <summary>White symbol on transparent, with a soft halo so it reads at a distance.</summary>
    public static Bitmap Draw(Icon icon, int size = 128)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Outline(icon, size);
        for (int w = 14; w >= 2; w -= 4)
            using (var halo = new Pen(Color.FromArgb(40, Color.White), w) { LineJoin = LineJoin.Round })
                g.DrawPath(halo, path);
        g.FillPath(Brushes.White, path);
        return bmp;
    }

    /// <summary>The icon as a filled / empty grid (row 0 = top) - the cookie cutter for the 3D version.</summary>
    public static bool[,] Mask(Icon icon, int n = 64)
    {
        using var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Outline(icon, n);
            g.FillPath(Brushes.White, path);
        }
        var m = new bool[n, n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                m[y, x] = bmp.GetPixel(x, y).A >= 128;
        return m;
    }

    public static string LabelTexture(string key) => $@"textures\itemfinder\label_{key}.dds";
    public static string LabelMaterialTexture(string key) => $"itemfinder/label_{key}.dds";

    /// <summary>The text a label shows: an entry name without its "Bobblehead: " style prefix.</summary>
    public static string LabelText(string name)
    {
        int c = name.IndexOf(": ", StringComparison.Ordinal);
        return (c > 0 && c < name.Length - 2 ? name[(c + 2)..] : name).Trim();
    }

    /// <summary>White text with a soft halo on transparent, 128 px tall, width a power of two (256..1024) that fits it;
    /// long names are squeezed to fit 1024. Returns the picture and its width / height ratio.</summary>
    public static Bitmap DrawLabel(string text)
    {
        const int H = 128;
        using var path = new GraphicsPath();
        using (var font = new Font("Segoe UI", 80, FontStyle.Bold, GraphicsUnit.Pixel))
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size, new PointF(0, 0), StringFormat.GenericTypographic);
        var b = path.GetBounds();
        float need = b.Width + 28;
        int width = need <= 256 ? 256 : need <= 512 ? 512 : 1024;
        float sx = Math.Min(1, (width - 28) / Math.Max(1, b.Width));
        float sy = Math.Min(1, (H - 28) / Math.Max(1, b.Height));
        using (var m = new Matrix())
        {
            m.Translate(width / 2f, H / 2f);
            m.Scale(sx, sy);
            m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
            path.Transform(m);
        }
        var bmp = new Bitmap(width, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int w = 14; w >= 2; w -= 4)
            using (var halo = new Pen(Color.FromArgb(40, Color.White), w) { LineJoin = LineJoin.Round })
                g.DrawPath(halo, path);
        g.FillPath(Brushes.White, path);
        return bmp;
    }

    /// <summary>Uncompressed BGRA DDS (classic header) with a full mip chain (any power-of-two size).</summary>
    public static byte[] Dds(Bitmap top)
    {
        var levels = new List<Bitmap> { top };
        while (levels[^1].Width > 1 || levels[^1].Height > 1)
        {
            int nw = Math.Max(1, levels[^1].Width / 2), nh = Math.Max(1, levels[^1].Height / 2);
            var next = new Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(next))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(levels[^1], 0, 0, nw, nh);
            }
            levels.Add(next);
        }
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int size = top.Width;
        w.Write(0x20534444u); // "DDS "
        w.Write(124); w.Write(0x1 | 0x2 | 0x4 | 0x8 | 0x1000 | 0x20000); w.Write(top.Height); w.Write(size); w.Write(size * 4); w.Write(0); w.Write(levels.Count);
        for (int i = 0; i < 11; i++) w.Write(0);
        w.Write(32); w.Write(0x41); w.Write(0); w.Write(32);
        w.Write(0x00FF0000); w.Write(0x0000FF00); w.Write(0x000000FF); unchecked { w.Write((int)0xFF000000); }
        w.Write(0x1000 | 0x400000 | 0x8); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        foreach (var lv in levels)
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    var c = lv.GetPixel(x, y);
                    // additive blending: the colour must fade with alpha, or the transparent border would show
                    w.Write((byte)(c.B * c.A / 255)); w.Write((byte)(c.G * c.A / 255)); w.Write((byte)(c.R * c.A / 255)); w.Write(c.A);
                }
        foreach (var lv in levels.Skip(1)) lv.Dispose();
        return ms.ToArray();
    }
}
