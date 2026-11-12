using static ItemFinder.Loc;
namespace ItemFinder;

/// <summary>The tour: a few screenshots of the app with the part that matters framed, and what to do there. Shown
/// once on the first start, and from Help - Show the tour. The pictures are made from the real window by the dev
/// command --tutorial-shots and built into the exe (src\Tutorial\step*.png).</summary>
sealed class Tutorial : Form
{
    /// <summary>One step: its picture (resource name), title and text.</summary>
    public sealed record Step(string Picture, string Title, string Text);

    public static readonly Step[] Steps =
    {
        new("step1.png", Tr("Pick a category"),
            Tr("The bar on the far left switches between your picks, items, junk, events and enemies; next to it are that\nsection's categories. The search box searches everything at once, Ctrl+K jumps to any category or item.")),
        new("step2.png", Tr("Select what you want to find"),
            Tr("Select the things you want to highlight in the game - just those, nothing else changes.\nThings made of several parts (robots, costumes...) open with ► so you can pick parts.")),
        new("step3.png", Tr("Choose the look"),
            Tr("Click the category to set the look for all of it: a ready-made look, the colour, and an icon or\n3D model above each thing. The Glow, Marker, Icon and Shine cards fine-tune it.")),
        new("step4.png", Tr("Give one thing its own look"),
            Tr("Click an item in the list. It uses its category's look until you change something - then it has its own.\nParts of a thing (the Guardian's base, a robot's weapons) can have their own look too.")),
        new("step5.png", Tr("Preview it in 3D"),
            Tr("The bottom of the look window shows the real model from the game the way the mod will show it.\nDrag to turn it, mouse wheel to zoom. \"Bigger view\" lets you walk around it, add a person or walls, and compare items.")),
        new("step6.png", Tr("Build & save"),
            Tr("Close the game, click \"Build & save\", name your mod, then start the game. \"Import .ba2\" opens a mod to change it -\n" +
            "yours or someone else's. The menu bar on top has ready-made presets (Edit), Save / Open project and Uninstall (File).")),
    };

    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(240, 240, 240) };
    readonly Label title = new() { UseMnemonic = false, Dock = DockStyle.Top, Height = 38, Font = new Font(Loc.UiFont, 15f, FontStyle.Bold), Padding = new Padding(16, 6, 0, 0) };
    readonly Label text = new() { UseMnemonic = false, Dock = DockStyle.Top, Height = 56, Padding = new Padding(18, 2, 16, 0) };
    readonly Label count = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 9, 16, 0) };
    readonly Button back = new() { Text = Tr("Back"), AutoSize = true, Height = 32 };
    readonly Button next = new() { Text = Tr("Next"), AutoSize = true, Height = 32 };
    readonly Button close = new() { Text = Tr("Close"), AutoSize = true, Height = 32 };
    int at;

    public Tutorial(Font font)
    {
        Text = Tr("ESP Maker - how it works");
        Font = font;
        ShowIcon = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Ui.Dialog(this);
        ClientSize = new Size(1040, 760);
        MinimumSize = new Size(700, 520);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8) };
        next.Font = new Font(font, FontStyle.Bold);
        buttons.Controls.AddRange(new Control[] { next, back, count, close });
        Controls.Add(picture);
        Controls.Add(text);
        Controls.Add(title);
        Controls.Add(buttons);
        back.Click += (_, _) => Go(at - 1);
        next.Click += (_, _) => { if (at == Steps.Length - 1) Close(); else Go(at + 1); };
        close.Click += (_, _) => Close();
        CancelButton = close;
        AcceptButton = next;
        Go(0);
    }

    /// <summary>Dev (--tour-pages): every step of this window as a picture (for the mod page).</summary>
    internal void SavePages(string dir)
    {
        Directory.CreateDirectory(dir);
        for (int i = 0; i < Steps.Length; i++)
        {
            Go(i);
            Refresh();
            using var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(Point.Empty, Size));
            bmp.Save(Path.Combine(dir, $"Tour {i + 1} - {Steps[i].Title.Replace("&", "and")}.png"));
        }
    }

    void Go(int i)
    {
        at = Math.Clamp(i, 0, Steps.Length - 1);
        var s = Steps[at];
        title.Text = $"{at + 1}. {s.Title}";
        text.Text = s.Text;
        picture.Image?.Dispose();
        picture.Image = Load(s.Picture);
        count.Text = Tf("Step {0} of {1}", at + 1, Steps.Length);
        back.Enabled = at > 0;
        next.Text = at == Steps.Length - 1 ? Tr("Done") : Tr("Next");
    }

    static Image? Load(string name)
    {
        using var stream = typeof(Tutorial).Assembly.GetManifestResourceStream("ItemFinder.Tutorial." + name);
        if (stream == null) return null;
        using var img = Image.FromStream(stream);
        return new Bitmap(img); // a copy: the stream closes
    }

    /// <summary>Dev: draws an orange frame and a number around <paramref name="area"/> on a window picture.</summary>
    public static void Frame(Graphics g, Rectangle area, int number)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var orange = Color.FromArgb(255, 140, 0);
        var r = Rectangle.Inflate(area, 4, 4);
        using (var glow = new Pen(Color.FromArgb(80, orange), 10)) g.DrawRectangle(glow, r);
        using (var pen = new Pen(orange, 4)) g.DrawRectangle(pen, r);
        var badge = new Rectangle(r.Left - 16, r.Top - 16, 34, 34);
        using (var fill = new SolidBrush(orange)) g.FillEllipse(fill, badge);
        using var f = new Font(Loc.UiFont, 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, number.ToString(), f, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
