using System.Drawing.Imaging;

namespace ItemFinder;

/// <summary>A grid of all icons, drawn in the chosen colour on a dark background (roughly as in game); click one.</summary>
public static class IconPicker
{
    /// <summary>The icon drawn in a colour on the dark "night" background.</summary>
    public static Bitmap Tile(Icons.Icon icon, Color colour, int size)
    {
        using var white = Icons.Draw(icon, 128);
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(24, 28, 34));
        var tint = new ColorMatrix(new[]
        {
            new[] { colour.R / 255f, 0, 0, 0, 0 }, new[] { 0, colour.G / 255f, 0, 0, 0 }, new[] { 0, 0, colour.B / 255f, 0, 0 },
            new[] { 0f, 0, 0, 1, 0 }, new[] { 0f, 0, 0, 0, 1 },
        });
        using var attr = new ImageAttributes();
        attr.SetColorMatrix(tint);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(white, new Rectangle(0, 0, size, size), 0, 0, 128, 128, GraphicsUnit.Pixel, attr);
        return bmp;
    }

    /// <summary>The 3D version as a tile: the shape pushed back in darker layers, front in full colour.</summary>
    public static Bitmap Tile3D(Icons.Icon icon, Color colour, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(24, 28, 34));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int inner = (int)(size * 0.86f);
        using var path = Icons.Outline(icon, inner);
        float depth = Math.Max(2, size * 0.14f);
        int steps = (int)Math.Ceiling(depth);
        for (int i = steps; i >= 1; i--)
        {
            float k = 0.25f + 0.25f * (steps - i) / steps; // the back is darkest
            using var side = new SolidBrush(Color.FromArgb((int)(colour.R * k), (int)(colour.G * k), (int)(colour.B * k)));
            var st = g.Save();
            g.TranslateTransform((size - inner) / 2f + i * 0.7f, (size - inner) / 2f + i * 0.9f);
            g.FillPath(side, path);
            g.Restore(st);
        }
        var front = g.Save();
        g.TranslateTransform((size - inner) / 2f, (size - inner) / 2f);
        using (var b = new SolidBrush(colour)) g.FillPath(b, path);
        g.Restore(front);
        return bmp;
    }

    /// <summary>Two tabs: flat icons (always turn to face you) and 3D shapes (solid, turn with the thing).</summary>
    /// <param name="only3D">Creatures: the game drops flat camera-facing icons from animated bodies, so only the 3D tab.</param>
    /// <returns>The picked icon id ("" = no icon, "model:..." = a 3D model) and whether it is the 3D shape, or null when
    /// closed without picking.</returns>
    internal static string? CaptureTo;

    public static (string Id, bool ThreeD)? Pick(IWin32Window owner, string current, bool current3D, Color colour, bool only3D = false)
    {
        (string, bool)? result = null;
        using var dlg = new Form
        {
            Text = "Pick an icon", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 9f), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8),
        };
        var tips = new ToolTip();
        FlowLayoutPanel Grid(bool threeD, bool models = false)
        {
            var grid = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(8 * 92, 0), Dock = DockStyle.Fill, BackColor = SystemColors.Window };
            Button Choice(string id, string name, Image? img)
            {
                bool now = id == current && (id.Length == 0 || models || threeD == current3D);
                var b = new Button
                {
                    Width = 84, Height = 92, Text = name, Image = img, TextImageRelation = TextImageRelation.ImageAboveText,
                    FlatStyle = FlatStyle.Flat, Margin = new Padding(4), BackColor = Color.FromArgb(24, 28, 34), ForeColor = Color.Gainsboro,
                    Font = new Font("Segoe UI", 8f),
                };
                b.FlatAppearance.BorderSize = now ? 3 : 1;
                b.FlatAppearance.BorderColor = now ? Color.Orange : Color.FromArgb(70, 76, 86);
                b.Click += (_, _) => { result = (id, threeD); dlg.Close(); };
                tips.SetToolTip(b, models && id.Length > 0 ? name + " (3D model)" : threeD && id.Length > 0 ? name + " (3D shape)" : name);
                return b;
            }
            grid.Controls.Add(Choice("", "No icon", null));
            if (models) foreach (var m in Models3D.All) grid.Controls.Add(Choice(m.Id, m.Name, Models3D.Tile(m, colour, 52)));
            else foreach (var ic in Icons.All) grid.Controls.Add(Choice(ic.Id, ic.Name, threeD ? Tile3D(ic, colour, 52) : Tile(ic, colour, 52)));
            return grid;
        }
        var tabs = new TabControl { Dock = DockStyle.Fill, AutoSize = true };
        TabPage Page(string title, bool threeD, string hint, bool models = false)
        {
            var page = new TabPage(title) { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4) };
            var box = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            box.Controls.Add(new Label { Text = hint, AutoSize = true, MaximumSize = new Size(8 * 92, 0), Padding = new Padding(4, 2, 0, 4) });
            box.Controls.Add(Grid(threeD, models));
            page.Controls.Add(box);
            return page;
        }
        // (the extruded "3D shapes" are gone - the 3D models replaced them; old setups keep theirs, see MainForm.MigrateSetup)
        if (!only3D) tabs.TabPages.Add(Page("Flat icons", false, "A flat picture that always turns to face you - easy to read from any side."));
        tabs.TabPages.Add(Page("3D models", false, (only3D ? "Creatures and worn outfits can only show 3D models: the game drops flat camera-facing icons from animated bodies. " : "") +
                                                    "A small textured model, like the Spooky Scorched pumpkin - turns with the item or creature. " +
                                                    "Models in their own colours ignore the look's colour; the white ones take it.", models: true));
        if (current.StartsWith(Models3D.Prefix)) tabs.SelectedIndex = tabs.TabPages.Count - 1;
        // the tab control does not grow to its pages by itself
        int w = 0, h = 0;
        foreach (TabPage p in tabs.TabPages) { var sz = p.Controls[0].GetPreferredSize(Size.Empty); w = Math.Max(w, sz.Width); h = Math.Max(h, sz.Height); }
        tabs.Dock = DockStyle.None;
        tabs.Size = new Size(w + 24, h + 44);
        dlg.Controls.Add(tabs);
        if (CaptureTo != null) // dev: picture of the dialog
            dlg.Shown += (_, _) => { using var bmp = new Bitmap(dlg.Width, dlg.Height); dlg.DrawToBitmap(bmp, new Rectangle(Point.Empty, dlg.Size)); bmp.Save(CaptureTo); dlg.Close(); };
        dlg.ShowDialog(owner);
        return result;
    }
}
