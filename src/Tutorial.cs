namespace ItemFinder;

/// <summary>The tour: a few screenshots of the app with the part that matters framed, and what to do there. Shown
/// once on the first start, and from More - Show the tour. The pictures are made from the real window by the dev
/// command --tutorial-shots and built into the exe (src\Tutorial\step*.png).</summary>
sealed class Tutorial : Form
{
    /// <summary>One step: its picture (resource name), title and text.</summary>
    public sealed record Step(string Picture, string Title, string Text);

    public static readonly Step[] Steps =
    {
        new("step1.png", "Pick a category",
            "Items, events and enemies are sorted into categories on the left. Click one to see what's in it.\n" +
            "The search box above the list searches everything at once."),
        new("step2.png", "Select what you want to find",
            "Select the things you want to highlight in the game - just those, nothing else changes.\n" +
            "Things made of several parts (robots, costumes...) open with ► so you can pick parts."),
        new("step3.png", "Choose the look",
            "With nothing clicked in the list, the panel on the right sets the look for the whole category: a Quick look,\n" +
            "the colour, and an icon or 3D model above each thing. \"More settings\" has the details."),
        new("step4.png", "Give one thing its own look",
            "Click an item, then \"Own look\" to make it different from its category.\n" +
            "Parts of a thing (here the Guardian's base) can have their own look too."),
        new("step5.png", "Preview it in 3D",
            "The preview shows the real model from the game the way the mod will show it.\n" +
            "Drag to turn it, use the mouse wheel to zoom."),
        new("step6.png", "Build & save",
            "Close the game, click \"Build & save\", name your mod, then start the game. \"Import .ba2\" opens a mod to change it -\n" +
            "yours or someone else's. \"More\" has ready-made presets, Save / Open project and Uninstall."),
    };

    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(240, 240, 240) };
    readonly Label title = new() { UseMnemonic = false, Dock = DockStyle.Top, Height = 38, Font = new Font("Segoe UI", 15f, FontStyle.Bold), Padding = new Padding(16, 6, 0, 0) };
    readonly Label text = new() { UseMnemonic = false, Dock = DockStyle.Top, Height = 56, Padding = new Padding(18, 2, 16, 0) };
    readonly Label count = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 9, 16, 0) };
    readonly Button back = new() { Text = "Back", AutoSize = true, Height = 32 };
    readonly Button next = new() { Text = "Next", AutoSize = true, Height = 32 };
    readonly Button close = new() { Text = "Close", AutoSize = true, Height = 32 };
    int at;

    public Tutorial(Font font)
    {
        Text = "ESP Maker - how it works";
        Font = font;
        ShowIcon = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
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
        count.Text = $"Step {at + 1} of {Steps.Length}";
        back.Enabled = at > 0;
        next.Text = at == Steps.Length - 1 ? "Done" : "Next";
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
        using var f = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, number.ToString(), f, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
