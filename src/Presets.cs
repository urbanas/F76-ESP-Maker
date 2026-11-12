using static ItemFinder.Loc;
namespace ItemFinder;

/// <summary>Recommended presets: big tiles with a picture, several can be picked; each ticks a set of things and gives
/// them a look of their own (More - Add presets). Nothing gets unticked.</summary>
sealed class PresetPicker : Form
{
    /// <summary>One preset as a tile.</summary>
    public sealed record Tile(string Key, string Title, string Note, Image Picture);

    readonly List<CheckBox> tiles = new();
    readonly Button add = new() { Text = Tr("Add"), AutoSize = true, Height = 34, Enabled = false };
    readonly Button addInstall = new() { Text = Tr("Add, then build && save"), AutoSize = true, Height = 34, Enabled = false };
    readonly Button cancel = new() { Text = Tr("Cancel"), AutoSize = true, Height = 34 };

    /// <summary>The picked presets' keys.</summary>
    public List<string> Picked => tiles.Where(t => t.Checked).Select(t => (string)t.Tag!).ToList();
    /// <summary>"Add, then build & save" was clicked.</summary>
    public bool Install { get; private set; }

    public PresetPicker(IEnumerable<Tile> choices, Font font)
    {
        Text = Tr("ESP Maker - add presets");
        Font = font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 560);

        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 10, 20, 0) };
        var title = new Label { Text = Tr("Recommended presets"), Dock = DockStyle.Top, Height = 40, Font = new Font(font.FontFamily, 16f, FontStyle.Bold) };
        var sub = new Label
        {
            Text = Tr("Pick one or more. Each selects its things and gives them its look - what you selected before stays as it is."),
            Dock = DockStyle.Top, Height = 30, ForeColor = Color.DimGray,
        };
        var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 6, 0, 0) };
        foreach (var c in choices)
        {
            var box = new CheckBox
            {
                Appearance = Appearance.Button, Tag = c.Key, Width = 265, Height = 100, Margin = new Padding(6),
                Image = c.Picture, ImageAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 0, 4, 0),
                BackColor = Color.White, AccessibleName = c.Title,
            };
            // the title bold, the note under it
            var bold = new Font(font, FontStyle.Bold);
            box.Paint += (_, e) =>
            {
                int left = 8 + c.Picture.Width + 12, w = box.Width - left - 8;
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
                var th = TextRenderer.MeasureText(e.Graphics, c.Title, bold, new Size(w, 100), flags).Height;
                var nh = TextRenderer.MeasureText(e.Graphics, c.Note, font, new Size(w, 100), flags).Height;
                int top = (box.Height - th - nh - 2) / 2;
                TextRenderer.DrawText(e.Graphics, c.Title, bold, new Rectangle(left, top, w, th), Color.Black, flags);
                TextRenderer.DrawText(e.Graphics, c.Note, font, new Rectangle(left, top + th + 2, w, nh), Color.FromArgb(80, 80, 80), flags);
            };
            box.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            box.FlatAppearance.CheckedBackColor = Color.FromArgb(214, 234, 255);
            box.CheckedChanged += (_, _) =>
            {
                box.FlatAppearance.BorderColor = box.Checked ? Color.FromArgb(0, 102, 204) : Color.FromArgb(200, 204, 210);
                box.FlatAppearance.BorderSize = box.Checked ? 2 : 1;
                add.Enabled = addInstall.Enabled = tiles.Any(t => t.Checked);
            };
            tiles.Add(box);
            grid.Controls.Add(box);
        }
        page.Controls.Add(grid);
        page.Controls.Add(sub);
        page.Controls.Add(title);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 10, 16, 10) };
        add.Font = new Font(font, FontStyle.Bold);
        buttons.Controls.AddRange(new Control[] { add, addInstall, cancel });
        Controls.Add(page);
        Controls.Add(buttons);
        add.Click += (_, _) => DialogResult = DialogResult.OK;
        addInstall.Click += (_, _) => { Install = true; DialogResult = DialogResult.OK; };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = add;
        CancelButton = cancel;
    }

    /// <summary>Dev (UI test): pick the first tile.</summary>
    internal void TestPickFirst() => tiles[0].Checked = true;
}
