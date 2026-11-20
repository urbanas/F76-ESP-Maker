using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ItemFinder;

/// <summary>The Look column as small icons (user, 2026-11-20: the words got too long): an "OWN" badge for an own look,
/// then one icon per feature, coloured like the look. The words stay in the cell's text (tooltip, sorting).</summary>
static class LookIcons
{
    /// <summary>One look in a cell: its style, the entry it belongs to, and whether it is the entry's own.</summary>
    public sealed record Look(Style Style, Entry Entry, bool Own);

    /// <summary>What a list cell holds in its Tag: one look, or several (an ingredient's bush + item).</summary>
    public sealed record Cell(List<Look> Looks);

    static readonly Dictionary<(string, int, int), Bitmap?> pictures = new();

    /// <summary>Draws the looks from <paramref name="x"/> on; returns where it stopped.</summary>
    public static int Draw(Graphics g, Cell cell, int x, int right, Rectangle row, Font font, bool lit, Color litText)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int size = Math.Min(16, row.Height - 6), y = row.Top + (row.Height - size) / 2;
        if (cell.Looks.Any(l => l.Own))
        {
            // the "OWN" badge: the first thing to see on an item with its own look
            using var small = new Font(font.FontFamily, Math.Max(6.5f, font.Size - 2f), FontStyle.Bold);
            var text = Loc.Tr("OWN");
            var ts = TextRenderer.MeasureText(g, text, small, Size.Empty, TextFormatFlags.NoPadding);
            var pill = new Rectangle(x, row.Top + (row.Height - ts.Height - 2) / 2, ts.Width + 8, ts.Height + 2);
            using (var bg = new SolidBrush(lit ? litText : Ui.Accent)) using (var path = Round(pill, 3)) g.FillPath(bg, path);
            TextRenderer.DrawText(g, text, small, pill, Ui.Dark ? Color.FromArgb(28, 32, 30) : Color.White,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x = pill.Right + 7;
        }
        for (int i = 0; i < cell.Looks.Count && x < right; i++)
        {
            if (i > 0)
            {
                TextRenderer.DrawText(g, "/", font, new Point(x, row.Top + (row.Height - font.Height) / 2), lit ? litText : Ui.Muted);
                x += TextRenderer.MeasureText("/", font).Width;
            }
            foreach (var (kind, colour, extra) in Parts(cell.Looks[i]))
            {
                if (x + size > right) break;
                var c = lit ? litText : colour ?? Ui.Muted;
                DrawOne(g, kind, new Rectangle(x, y, size, size), c, cell.Looks[i]);
                x += size + 2;
                if (extra != null)
                {
                    using var small = new Font(font.FontFamily, Math.Max(6.5f, font.Size - 1.5f));
                    var w = TextRenderer.MeasureText(g, extra, small, Size.Empty, TextFormatFlags.NoPadding).Width;
                    TextRenderer.DrawText(g, extra, small, new Rectangle(x - 1, row.Top, w + 2, row.Height), lit ? litText : Ui.Muted,
                                          TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += w;
                }
                x += 5;
            }
        }
        return x;
    }

    /// <summary>The icons of one look, in the order the words used: kind, colour (null = plain), small text after it.</summary>
    static IEnumerable<(string Kind, Color? Colour, string? Extra)> Parts(Look l)
    {
        var st = l.Style;
        var any = false;
        if (st.Glow) { any = true; yield return ("glow", Col(st.GlowColor), null); }
        if (st.Beam) { any = true; yield return ("beam", Col(st.BeamColor), Math.Round(st.BeamLength / StyleEditor.UnitsPerMetre).ToString()); }
        if (st.Arrow) { any = true; yield return ("arrow", Col(st.ArrowColour), null); }
        if (st.Copy) { any = true; yield return ("copy", null, null); }
        if (Icons.Get(st.Icon) != null || Models3D.Get(st.Icon) != null) { any = true; yield return ("icon", Col(st.IconColour), null); }
        if (st.Brighten) { any = true; yield return ("bright", null, null); }
        if (st.ShowsOwnXray && !st.Brighten) { any = true; yield return ("xray", null, null); }
        if (st.Invisible && l.Entry.CanHide) { any = true; yield return ("hidden", null, null); }
        if (st.Label) { any = true; yield return ("name", Col(st.LabelColour), null); }
        if (st.Timed && any) yield return ("timer", null, null);
        // (no icon for "moving" - user, 2026-11-20: not needed here; the hover words still say it)
        if (!any) yield return ("none", null, null);
    }

    /// <summary>The look colour; on the light list very light colours are darkened in the same hue until they stand out.</summary>
    static Color Col(string hex)
    {
        var c = Materials.Parse(hex);
        // (0.45, was 0.62: white looks turned light grey and vanished on the white list once faded - user, 2026-11-20)
        for (int i = 0; i < 8 && !Ui.Dark && (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.45; i++)
            c = Color.FromArgb((int)(c.R * 0.85), (int)(c.G * 0.85), (int)(c.B * 0.85));
        return c;
    }

    static void DrawOne(Graphics g, string kind, Rectangle r, Color c, Look l)
    {
        float s = r.Width / 16f;
        PointF P(float px, float py) => new(r.Left + px * s, r.Top + py * s);
        using var pen = new Pen(c, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(c);
        switch (kind)
        {
            case "glow":
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(r);
                    using var glow = new PathGradientBrush(path) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } };
                    glow.FocusScales = new PointF(0.35f, 0.35f);
                    g.FillEllipse(glow, r);
                }
                // a thin ring so a pale glow still has an edge on the white list
                if (!Ui.Dark) using (var ring = new Pen(Color.FromArgb(150, c), 1f)) g.DrawEllipse(ring, r.Left + 1.5f * s, r.Top + 1.5f * s, 13 * s, 13 * s);
                break;
            case "beam":
                g.FillRectangle(fill, r.Left + 6 * s, r.Top, 4 * s, 13 * s);
                using (var foot = new SolidBrush(Color.FromArgb(128, c))) g.FillEllipse(foot, r.Left + 3 * s, r.Top + 12.4f * s, 10 * s, 3.2f * s);
                break;
            case "arrow":
                g.FillPolygon(fill, new[] { P(2, 4), P(14, 4), P(8, 13) });
                break;
            case "icon":
                if (Picture(l.Style, c, r.Width) is Bitmap bmp) g.DrawImage(bmp, r);
                break;
            case "copy":
                g.DrawRectangle(pen, r.Left + 1.5f * s, r.Top + 4.5f * s, 9 * s, 9 * s);
                using (var back = new SolidBrush(Color.FromArgb(140, c))) g.FillRectangle(back, r.Left + 5.5f * s, r.Top + 1.5f * s, 9 * s, 9 * s);
                break;
            case "bright":
                g.FillEllipse(fill, r.Left + 4.8f * s, r.Top + 4.8f * s, 6.4f * s, 6.4f * s);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, P(8 + 5.2f * (float)Math.Cos(a), 8 + 5.2f * (float)Math.Sin(a)), P(8 + 7f * (float)Math.Cos(a), 8 + 7f * (float)Math.Sin(a)));
                }
                break;
            case "xray":
            case "hidden":
                using (var eye = new GraphicsPath())
                {
                    eye.AddBezier(P(1, 8), P(4, 3), P(12, 3), P(15, 8));
                    eye.AddBezier(P(15, 8), P(12, 13), P(4, 13), P(1, 8));
                    g.DrawPath(pen, eye);
                }
                g.FillEllipse(fill, r.Left + 5.8f * s, r.Top + 5.8f * s, 4.4f * s, 4.4f * s);
                if (kind == "hidden") g.DrawLine(pen, P(2.5f, 13.5f), P(13.5f, 2.5f));
                break;
            case "name":
                using (var f = new Font("Segoe UI", 10f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "Aa", f, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;
            case "timer":
                g.DrawEllipse(pen, r.Left + 2 * s, r.Top + 3 * s, 12 * s, 12 * s);
                g.DrawLines(pen, new[] { P(8, 5.5f), P(8, 9), P(10.4f, 10.6f) });
                g.DrawLine(pen, P(6, 1.3f), P(10, 1.3f));
                break;
            case "move":
                g.DrawArc(pen, r.Left + 3 * s, r.Top + 3 * s, 10 * s, 10 * s, -40, 300);
                g.DrawLines(pen, new[] { P(12.6f, 1.4f), P(12.6f, 5f), P(9f, 5f) });
                break;
            case "none":
                g.DrawLine(pen, P(4, 8), P(12, 8));
                break;
        }
    }

    /// <summary>The chosen icon / 3D model, tinted, on a clear background (made once per icon, colour and size).</summary>
    static Bitmap? Picture(Style st, Color c, int size)
    {
        var key = (st.Icon, c.ToArgb(), size);
        if (pictures.TryGetValue(key, out var done)) return done;
        Bitmap? bmp = null;
        try
        {
            if (Icons.Get(st.Icon) is Icons.Icon ic)
            {
                using var white = Icons.Draw(ic, 64);
                bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                using var attr = new ImageAttributes();
                attr.SetColorMatrix(new ColorMatrix(new[]
                {
                    new[] { c.R / 255f, 0, 0, 0, 0 }, new[] { 0, c.G / 255f, 0, 0, 0 }, new[] { 0, 0, c.B / 255f, 0, 0 },
                    new[] { 0f, 0, 0, 1, 0 }, new[] { 0f, 0, 0, 0, 1 },
                }));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(white, new Rectangle(0, 0, size, size), 0, 0, 64, 64, GraphicsUnit.Pixel, attr);
            }
            else if (Models3D.Get(st.Icon) is Model3D m) bmp = Models3D.Tile(m, c, size, Color.Transparent);
        }
        catch { bmp = null; } // a broken model just shows no picture here
        pictures[key] = bmp;
        return bmp;
    }

    static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
