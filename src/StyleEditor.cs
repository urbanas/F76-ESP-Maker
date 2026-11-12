using static ItemFinder.Loc;
using System.Drawing.Drawing2D;

namespace ItemFinder;

/// <summary>Edits one <see cref="Style"/>: glow and beam on/off, colours, opacity, beam length and
/// thickness, see-through walls; with a small picture of the result.</summary>
public sealed class StyleEditor : UserControl
{
    public const float UnitsPerMetre = 70f;
    /// <summary>Game units per centimetre (1 unit ≈ 1.43 cm).</summary>
    const float UnitsPerCm = 0.7f;

    readonly CheckBox glow = new() { Text = Tr("Glow (item shines)"), AutoSize = true };
    readonly Button glowColor = new() { Width = 70, Height = 26, FlatStyle = FlatStyle.Flat };
    readonly NumericUpDown opacity = new() { Minimum = 5, Maximum = 100, Increment = 5, Width = 55 };
    readonly CheckBox beam = new() { Text = Tr("Marker above the item"), AutoSize = true };
    readonly ComboBox marker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Items = { Tr("Beam of light into the sky"), Tr("Arrow pointing down") } };
    readonly Button beamColor = new() { Width = 70, Height = 26, FlatStyle = FlatStyle.Flat };
    readonly NumericUpDown length = new() { Minimum = 1, Maximum = 150, Width = 55 };
    readonly NumericUpDown width = new() { Minimum = 2, Maximum = 300, Increment = 2, Width = 55 };
    readonly NumericUpDown beamStrength = new() { Minimum = 1, Maximum = 300, Increment = 5, Width = 55 };
    // grows with the text (larger Windows text scaling) instead of cutting the picture off
    readonly Button iconButton = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(130, 26), Padding = new Padding(2), Margin = new Padding(3, 3, 3, 3), FlatStyle = FlatStyle.Flat, TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft };
    readonly NumericUpDown iconSize = new() { Minimum = 20, Maximum = 600, Increment = 10, Width = 55 };
    readonly NumericUpDown labelSize = new() { Minimum = 10, Maximum = 500, Increment = 10, Width = 55 };
    readonly TextBox labelText = new() { Width = 170, PlaceholderText = Tr("the item's name") };
    readonly CheckBox labelCat = new() { Text = Tr("Start with the category"), AutoSize = true };
    readonly CheckBox walls = new() { Text = Tr("Visible through walls"), AutoSize = true };
    readonly CheckBox brighten = new() { Text = Tr("Brighten its own colours"), AutoSize = true };
    readonly NumericUpDown brightStrength = new() { Minimum = 25, Maximum = 300, Increment = 25, Width = 55 };
    readonly CheckBox fill = new() { Text = Tr("Fill the whole item"), AutoSize = true };
    readonly CheckBox sameColor = new() { Text = Tr("Same colour as the glow"), AutoSize = true };
    readonly Panel preview = new() { Width = 110, BackColor = Color.FromArgb(24, 28, 34) };
    readonly SwatchRow swatches = new() { Margin = new Padding(0, 0, 0, 2) };
    bool compact;

    // Compact (the look pop-up): presets on top, then three sections side by side - The item | Beam / arrow | Icon / text
    // (stacked when narrow, e.g. the part look dialog). They drive the grid's controls (the grid itself stays hidden).
    readonly Panel board = new() { Dock = DockStyle.Fill, Visible = false };
    readonly List<Section> sections = new();
    readonly List<Tile> presetTiles = new();
    readonly List<FlowLayoutPanel> fxFlows = new();
    /// <summary>Joined buttons for a number box's usual values: Soft 15 / Medium 30 / Strong 60 ...</summary>
    readonly List<(Seg Seg, NumericUpDown Box, string[] Names, int[] Values)> steps = new();
    readonly Seg itemSeg = new(Tr("Outline"), Tr("Fill"), Tr("Bright"), Tr("Off")), beamSeg = new(Tr("Beam"), Tr("Arrow"), Tr("Off")), topSeg = new(Tr("Icon"), Tr("Text"), Tr("Off"));
    readonly SwatchRow markerSwatches = new() { Margin = new Padding(0, 0, 0, 2), Small = true };
    readonly Label markerSame = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 0, 0) };
    readonly LinkLabel markerOwn = new() { AutoSize = true, Margin = new Padding(0, 6, 3, 0) };
    Control[] heightRow = Array.Empty<Control>(), ownColourRow = Array.Empty<Control>();
    readonly List<Control> glowRows = new(), shineRows = new(), beamRows = new(), iconRows = new(), textRows = new();
    /// <summary>The icon before Text / Off was picked: Icon brings it back.</summary>
    string lastIcon = "";
    int arrangedWidth;

    /// <summary>One section of the look: a bold title (its hint as a tooltip) and its settings under it, framed.</summary>
    sealed class Section : Panel
    {
        public readonly TableLayoutPanel Body = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Padding = new Padding(8, 0, 6, 8) };
        readonly Label title = new() { Dock = DockStyle.Top, Height = 32, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };

        public Section(string name, string what)
        {
            title.Text = name;
            Padding = new Padding(1);
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            new ToolTip { AutoPopDelay = 30000 }.SetToolTip(title, what);
            Controls.Add(Body);
            Controls.Add(title); // docked last: on top
            BackColor = Color.White;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnFontChanged(EventArgs e) { title.Font = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold); base.OnFontChanged(e); }
        protected override void OnPaint(PaintEventArgs e) { using var p = new Pen(Ui.Line); e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }

        /// <summary>The height its content needs at this width.</summary>
        public int Wanted(int width) => title.Height + Body.GetPreferredSize(new Size(width - 2, 0)).Height + 2;
    }

    /// <summary>A preset button: a small picture of the look and its name; lit while the look matches it.</summary>
    sealed class Tile : Control
    {
        public readonly int Index;
        bool hover, selected;
        public bool Clickable = true;
        public bool Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }
        public event Action<int>? Clicked;

        public Tile(int index, string text)
        {
            Index = index;
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = index < 3 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (Clickable) Clicked?.Invoke(Index); base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Ui.Panel);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var bg = new SolidBrush(selected ? Ui.AccentSoft : hover && Clickable ? Ui.Hover : Ui.Window)) g.FillRectangle(bg, r);
            using (var pen = new Pen(selected ? Ui.Accent : Ui.Line, selected ? 2f : 1f)) g.DrawRectangle(pen, r);
            int textH = Font.Height + 8;
            Picture(g, new Rectangle(r.Left, r.Top + 6, r.Width, r.Height - textH - 6));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(r.Left + 2, r.Bottom - textH, r.Width - 4, textH), selected ? Ui.Accent : Clickable ? Ui.Fg : Ui.Muted,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        /// <summary>The item as a glowing box; a beam over it (Glow + beam); a name bar on top (+ name); Custom: a star.</summary>
        void Picture(Graphics g, Rectangle p)
        {
            var c = Ui.Accent;
            float cx = p.Left + p.Width / 2f, s = Math.Min(p.Height, 44);
            if (Index == 3)
            {
                float cy = p.Top + p.Height / 2f;
                var pts = Enumerable.Range(0, 10).Select(i =>
                {
                    double a = -Math.PI / 2 + i * Math.PI / 5;
                    float rr = i % 2 == 0 ? s * 0.42f : s * 0.18f;
                    return new PointF(cx + (float)(Math.Cos(a) * rr), cy + (float)(Math.Sin(a) * rr));
                }).ToArray();
                using var b = new SolidBrush(selected ? c : Ui.Muted);
                g.FillPolygon(b, pts);
                return;
            }
            var box = new RectangleF(cx - s * 0.32f, p.Bottom - s * 0.4f, s * 0.64f, s * 0.36f);
            using (var halo = new Pen(Color.FromArgb(80, c), 6)) g.DrawRectangle(halo, box.X, box.Y, box.Width, box.Height);
            using (var fill = new SolidBrush(Ui.Line)) g.FillRectangle(fill, box);
            using (var edge = new Pen(c, 2)) g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
            float top = p.Top + (Index == 2 ? 11 : 0);
            if (Index >= 1) using (var beam = new SolidBrush(Color.FromArgb(170, c))) g.FillRectangle(beam, cx - 2.5f, top, 5, box.Top - 4 - top);
            if (Index == 2) using (var bar = new SolidBrush(Ui.Fg)) g.FillRectangle(bar, cx - s * 0.55f, p.Top, s * 1.1f, 6);
        }
    }

    /// <summary>A section's extras: through walls, motion, hide timer - bound to one <see cref="PartFx"/> of the look.</summary>
    sealed class FxRows
    {
        public readonly Func<Style, PartFx> Of;
        public readonly Chip Walls = new(Tr("X-ray"));
        public readonly Chip Pulse = new(Tr("Pulse"));
        public readonly Chip Rainbow = new(Tr("Rainbow"));
        public readonly Chip Spin = new(Tr("Spin"));
        public readonly Chip Bob = new(Tr("Bob"));
        public readonly Chip Flow = new(Tr("Light runs up"));
        public readonly NumericUpDown Hide = new() { Minimum = 0, Maximum = 600, Increment = 5, Width = 50 };
        /// <summary>Every row (shown while its part is on).</summary>
        public readonly List<Control> Rows = new();
        public Chip[] Boxes => new[] { Walls, Pulse, Rainbow, Spin, Bob, Flow };
        public FxRows(Func<Style, PartFx> of) => Of = of;
    }
    readonly List<FxRows> fxRows = new();
    Section itemSection = null!, beamSection = null!, topSection = null!;
    FxRows glowFx = null!, beamFx = null!, topFx = null!;
    readonly CheckBox invisibleBox = new() { Text = Tr("Invisible - only what you add shows"), AutoSize = true };

    void BuildCards()
    {
        Label L(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 8, 0) };
        Label Unit(string t) => new() { Text = t, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 7, 3, 0) };
        Seg Steps(NumericUpDown box, string[] names, int[] values)
        {
            var s = new Seg(names);
            s.Picked += i => box.Value = Math.Clamp(values[i], box.Minimum, box.Maximum);
            steps.Add((s, box, names, values));
            box.Margin = new Padding(3, 4, 0, 3);
            box.Width = 48;
            return s;
        }
        Control[] Row(Section c, string label, params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            f.Controls.AddRange(cs);
            var l = L(label);
            int r = c.Body.RowCount++;
            c.Body.Controls.Add(l, 0, r);
            c.Body.Controls.Add(f, 1, r);
            return new Control[] { l, f };
        }
        Control[] Wide(Section c, Control x)
        {
            x.Margin = new Padding(0, 2, 0, 6);
            int r = c.Body.RowCount++;
            c.Body.Controls.Add(x, 0, r);
            c.Body.SetColumnSpan(x, 2);
            return new[] { x };
        }
        void Edited() { RefreshCards(); Changed?.Invoke(this, EventArgs.Empty); }
        var tips = new ToolTip { AutoPopDelay = 20000 };
        // the extras of a section: ticks (through walls and the motions that fit - RefreshCards shows those), then the hide timer
        FxRows Fx(Section c, Func<Style, PartFx> of)
        {
            var x = new FxRows(of);
            var ticks = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 4, 0, 2) };
            foreach (var b in x.Boxes)
            {
                b.Margin = new Padding(0, 2, 6, 2);
                ticks.Controls.Add(b);
            }
            tips.SetToolTip(x.Walls, Tr("X-ray: seen through walls, rocks and the ground."));
            tips.SetToolTip(x.Pulse, Tr("Brighter and dimmer, every 2 seconds."));
            tips.SetToolTip(x.Rainbow, Tr("Colours run through the rainbow (instead of its own colour)."));
            tips.SetToolTip(x.Spin, Tr("Turns round (3D icon or model only)."));
            tips.SetToolTip(x.Bob, Tr("Floats up and down."));
            tips.SetToolTip(x.Flow, Tr("Bands of light run up the beam."));
            fxFlows.Add(ticks);
            x.Rows.AddRange(Row(c, Tr("Effects"), ticks));
            var hideRow = Row(c, Tr("Hide after"), Steps(x.Hide, new[] { Tr("Never"), "30 s", Tr("1 min"), Tr("2 min") }, new[] { 0, 30, 60, 120 }), x.Hide, Unit("s"));
            ((FlowLayoutPanel)hideRow[1]).WrapContents = true; // the seconds box goes under the buttons in a narrow column
            fxFlows.Add((FlowLayoutPanel)hideRow[1]);
            x.Rows.AddRange(hideRow);
            foreach (var b in x.Boxes)
                b.CheckedChanged += (_, _) =>
                {
                    if (loading) return;
                    var fx = x.Of(style);
                    fx.Walls = x.Walls.Checked; fx.Pulse = x.Pulse.Checked; fx.Rainbow = x.Rainbow.Checked;
                    fx.Spin = x.Spin.Checked; fx.Bob = x.Bob.Checked; fx.Flow = x.Flow.Checked;
                    Edited();
                };
            x.Hide.ValueChanged += (_, _) =>
            {
                if (loading) return;
                var fx = x.Of(style);
                fx.HideAfter = (int)x.Hide.Value;
                fx.BlinkOff = 0; // blink is gone (user, 2026-11-09: pulse covers it)
                Edited();
            };
            fxRows.Add(x);
            return x;
        }
        string[] strength = { Tr("Soft"), Tr("Medium"), Tr("Strong") };
        const string timerNote = "\nA hide timer starts when the game loads the item, so it starts again when you come back to the area.";

        // 1 the item: a glow (outline / fill), or its own colours lit (bright); invisible
        itemSection = new Section(Tr("1   The item"), Tr("Outline / Fill: a coloured light on the item. Bright: its own colours light up, day and night (not through walls).\nInvisible: the item disappears - only what you add shows; still there to pick up. Worn outfits vanish from whoever wears them, you too. Not on enemies.") + timerNote);
        Wide(itemSection, itemSeg);
        itemSeg.Picked += i =>
        {
            if (loading) return;
            loading = true;
            glow.Checked = i < 2; fill.Checked = i == 1; brighten.Checked = i == 2;
            loading = false;
            Commit();
        };
        swatches.Small = true;
        glowRows.AddRange(Row(itemSection, Tr("Colour"), swatches));
        glowRows.AddRange(Row(itemSection, Tr("Strength"), Steps(opacity, strength, new[] { 15, 30, 60 }), opacity, Unit("%")));
        shineRows.AddRange(Row(itemSection, Tr("Shine"), Steps(brightStrength, strength, new[] { 50, 100, 200 }), brightStrength, Unit("%")));
        invisibleBox.CheckedChanged += (_, _) => { if (loading) return; style.Invisible = invisibleBox.Checked; UpdateLook(); Changed?.Invoke(this, EventArgs.Empty); };
        glowFx = Fx(itemSection, st => st.GlowFx);
        Wide(itemSection, invisibleBox); // last in the section (user, 2026-11-09)
        swatches.Picked += c =>
        {
            if (c is not Color colour)
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = Materials.Parse(style.GlowColor) };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                colour = dlg.Color;
            }
            style.GlowColor = Materials.Hex(colour);
            if (sameColor.Checked) style.BeamColor = style.GlowColor;
            UpdateLook();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        // 2 beam or arrow
        beamSection = new Section(Tr("2   Beam / arrow"), Tr("Something above the item, so you see where it is from far away.") + timerNote);
        Wide(beamSection, beamSeg);
        beamSeg.Picked += i =>
        {
            if (loading) return;
            loading = true;
            beam.Checked = i < 2;
            if (i < 2) marker.SelectedIndex = i;
            loading = false;
            Commit();
        };
        heightRow = Row(beamSection, Tr("Height"), Steps(length, new[] { Tr("Short"), Tr("Medium"), Tr("Tall") }, new[] { 10, 20, 30 }), length, Unit("m"));
        beamRows.AddRange(heightRow);
        beamRows.AddRange(Row(beamSection, Tr("Thickness"), Steps(width, new[] { Tr("Thin"), Tr("Normal"), Tr("Thick") }, new[] { 6, 10, 20 }), width, Unit("cm")));
        beamRows.AddRange(Row(beamSection, Tr("Strength"), Steps(beamStrength, strength, new[] { 15, 30, 60 }), beamStrength, Unit("%")));
        beamRows.AddRange(Row(beamSection, Tr("Colour"), markerSame, markerOwn));
        ownColourRow = Row(beamSection, "", markerSwatches);
        markerOwn.LinkClicked += (_, _) => sameColor.Checked = !sameColor.Checked;
        markerSwatches.Picked += c =>
        {
            if (c is not Color colour)
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = Materials.Parse(style.BeamColor) };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                colour = dlg.Color;
            }
            style.BeamColor = Materials.Hex(colour);
            UpdateLook();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        beamFx = Fx(beamSection, st => st.BeamFx);

        // 3 icon or text (one at a time)
        topSection = new Section(Tr("3   Icon / text"), Tr("A picture or 3D model, or a line of text (the item's name or your own), floating above the item and turning to face you.\nText is not shown on enemies. Spin and bob need a 3D icon or model on enemies; spin shows on 3D only.") + timerNote);
        Wide(topSection, topSeg);
        topSeg.Picked += PickTop;
        iconRows.AddRange(Row(topSection, Tr("Picture"), iconButton));
        iconRows.AddRange(Row(topSection, Tr("Size"), Steps(iconSize, new[] { "S", "M", "L" }, new[] { 40, 70, 120 }), iconSize, Unit("cm")));
        labelText.Width = 160;
        textRows.AddRange(Row(topSection, Tr("Text"), labelText));
        tips.SetToolTip(labelText, Tr("Leave empty to show each item's own name.\nText typed here shows on every item using this look (for one item only, give that item its own look)."));
        labelText.TextChanged += (_, _) =>
        {
            if (loading || style.LabelText == labelText.Text) return;
            style.LabelText = labelText.Text;
            Changed?.Invoke(this, EventArgs.Empty);
        };
        textRows.AddRange(Row(topSection, Tr("Height"), Steps(labelSize, new[] { "S", "M", "L" }, new[] { 100, 150, 250 }), labelSize, Unit("cm")));
        textRows.AddRange(Row(topSection, "", labelCat));
        labelCat.CheckedChanged += (_, _) => { if (loading) return; style.LabelCategory = labelCat.Checked; Edited(); };
        topFx = Fx(topSection, st => st.Icon.Length > 0 ? st.IconFx : st.LabelFx);

        // presets: switch the parts on / off; colours and sizes stay
        string[] names = { Tr("Glow only"), Tr("Glow + beam"), Tr("Glow + beam + name") };
        for (int i = 0; i < names.Length; i++)
        {
            var t = new Tile(i, names[i]) { Clickable = i < 3 };
            tips.SetToolTip(t, i < 3 ? Tr("Switches the parts on or off - your colours and sizes stay.") : Tr("Lit when the look is none of the presets."));
            t.Clicked += ApplyPreset;
            presetTiles.Add(t);
            board.Controls.Add(t);
        }
        sections.AddRange(new[] { itemSection, beamSection, topSection });
        foreach (var s in sections) board.Controls.Add(s);
        Controls.Add(board);
        board.BringToFront(); // docked last: fills what the header leaves
    }

    /// <summary>Icon / Text / Off - one at a time. Icon brings back the last icon, or opens the picker.</summary>
    void PickTop(int i)
    {
        if (loading) return;
        if (i == 0)
        {
            if (style.Icon.Length == 0)
            {
                bool label = style.Label;
                style.Label = false;
                if (lastIcon.Length > 0) style.Icon = lastIcon;
                else
                {
                    PickIcon(); // tells Changed itself when one is picked
                    if (style.Icon.Length == 0) style.Label = label; // cancelled
                    UpdateLook();
                    return;
                }
            }
        }
        else
        {
            if (style.Icon.Length > 0) lastIcon = style.Icon;
            style.Icon = "";
            style.Label = i == 1;
        }
        UpdateLook();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The preset the look matches: 0 glow only, 1 glow + beam, 2 glow + beam + name, 3 none (custom).</summary>
    int PresetOf() => !style.Glow || style.Icon.Length > 0 ? 3 : (style.Beam, style.Label) switch { (false, false) => 0, (true, false) => 1, (true, true) => 2, _ => 3 };

    void ApplyPreset(int p)
    {
        var st = style.Clone();
        if (!st.Glow) { st.Glow = true; st.Brighten = false; }
        st.Beam = p >= 1;
        if (st.Icon.Length > 0) lastIcon = st.Icon;
        st.Icon = "";
        st.Label = p == 2;
        Value = st;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Dev (UI test): picks a preset like a click.</summary>
    internal void TestPreset(int p) => ApplyPreset(p);

    string LabelSummary() => !style.Label ? Tr("Off") : (style.LabelCategory ? Tr("Category · ") : "") + (style.LabelText.Trim().Length > 0 ? $"\"{style.LabelText.Trim()}\"" : Tr("Item's name")) + Tf(" · {0} cm", labelSize.Value) + FxSummary(style.LabelFx);

    static string Time(int s) => s % 60 == 0 ? Tf("{0} min", s / 60) : $"{s} s";

    /// <summary>" · pulse · hides 30 s" - a section's extras for its card's summary line.</summary>
    static string FxSummary(PartFx x, bool flow = true)
    {
        var w = new List<string>();
        if (!x.Walls) w.Add("not through walls");
        if (x.Pulse) w.Add("pulse");
        if (x.Rainbow) w.Add("rainbow");
        if (x.Spin) w.Add("spin");
        if (x.Bob) w.Add("bob");
        if (x.Flow && flow) w.Add("flowing");
        if (x.HideAfter > 0) w.Add(x.BlinkOff > 0 ? Tf("blinks ({0} / {1})", Time(x.HideAfter), Time(x.BlinkOff)) : Tf("hides after {0}", Time(x.HideAfter)));
        return w.Count > 0 ? " · " + string.Join(" · ", w) : "";
    }

    /// <summary>The sections show the look: lit choices, the rows that fit, the matching preset.</summary>
    void RefreshCards()
    {
        int item = style.Glow ? (style.GlowFill ? 1 : 0) : style.Brighten ? 2 : 3;
        int mark = !style.Beam ? 2 : style.BeamArrow ? 1 : 0;
        int top = style.Icon.Length > 0 ? 0 : style.Label ? 1 : 2;
        itemSeg.Selected = item;
        beamSeg.Selected = mark;
        topSeg.Selected = top;
        foreach (var c in glowRows) c.Visible = item < 2;
        foreach (var c in shineRows) c.Visible = item == 2;
        foreach (var c in beamRows) c.Visible = mark < 2;
        foreach (var c in heightRow) c.Visible = mark == 0; // an arrow has its own size
        foreach (var c in iconRows) c.Visible = top == 0;
        foreach (var c in textRows) c.Visible = top == 1;
        bool own = !sameColor.Checked;
        markerSame.Text = own ? Tr("Its own colour ·") : Tr("Same as the glow ·");
        markerOwn.Text = own ? Tr("same as the glow") : Tr("use a different colour");
        foreach (var c in ownColourRow) c.Visible = own && mark < 2;
        markerSwatches.Current = Materials.Parse(style.BeamColor);
        markerSwatches.Invalidate();
        bool was = loading;
        loading = true; // setting the switches must not write back
        foreach (var x in fxRows)
        {
            var fx = x.Of(style);
            bool on = x == glowFx ? item < 2 : x == beamFx ? mark < 2 : top < 2;
            x.Walls.Checked = fx.Walls; x.Pulse.Checked = fx.Pulse; x.Rainbow.Checked = fx.Rainbow;
            x.Spin.Checked = fx.Spin; x.Bob.Checked = fx.Bob; x.Flow.Checked = fx.Flow;
            x.Hide.Value = Math.Clamp(fx.HideAfter, (int)x.Hide.Minimum, (int)x.Hide.Maximum);
            foreach (var c in x.Rows) c.Visible = on;
        }
        // the motions that fit: flow on a beam, bob on an arrow / icon / text, spin on an icon
        glowFx.Spin.Visible = glowFx.Bob.Visible = glowFx.Flow.Visible = false;
        beamFx.Spin.Visible = false;
        beamFx.Flow.Visible = mark == 0;
        beamFx.Bob.Visible = mark == 1;
        topFx.Spin.Visible = top == 0;
        topFx.Flow.Visible = false;
        foreach (var (seg, box, _, values) in steps) seg.Selected = Array.IndexOf(values, (int)box.Value);
        invisibleBox.Checked = style.Invisible;
        labelCat.Checked = style.LabelCategory;
        int p = PresetOf(); // the preset the look matches is lit (none when it matches none)
        foreach (var t in presetTiles) t.Selected = t.Index == p;
        loading = was;
        ShowMore();
    }

    /// <summary>The look as presets + three sections (The item, Beam / arrow, Icon / text) - the look pop-up.</summary>
    public bool Compact
    {
        get => compact;
        set
        {
            compact = value;
            if (value && sections.Count == 0) { BuildCards(); UpdateLook(); }
            ShowMore();
        }
    }

    /// <summary>Height of the quick header (dev: the tour's pictures).</summary>
    internal int HeaderHeight => Height;

    /// <summary>The sections' rows changed (a part switched on / off): the height changed.</summary>
    public event EventHandler? HeightChanged;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (compact && Width != arrangedWidth) ShowMore(); // wider / narrower: the sections are laid out again
    }

    /// <summary>Lays out the presets and the sections: three side by side when there is room, else one under the other.</summary>
    void ShowMore()
    {
        grid.Visible = !compact;
        board.Visible = compact;
        if (compact && sections.Count > 0)
        {
            int w = Math.Max(200, Width), gap = 8;
            arrangedWidth = Width;
            bool wide = w >= 720;
            int cols = wide ? sections.Count : 1, colW = (w - gap * (cols - 1)) / cols;
            int tileH = Font.Height * 2 + 46, tw = (w - gap * (presetTiles.Count - 1)) / presetTiles.Count;
            for (int i = 0; i < presetTiles.Count; i++) presetTiles[i].Bounds = new Rectangle(i * (tw + gap), 0, tw, tileH);
            int y = tileH + gap;
            foreach (var f in fxFlows) f.MaximumSize = new Size(Math.Max(120, colW - 110), 0);
            if (wide)
            {
                int h = sections.Max(s => s.Wanted(colW));
                for (int i = 0; i < sections.Count; i++) sections[i].Bounds = new Rectangle(i * (colW + gap), y, colW, h);
                y += h;
            }
            else
                foreach (var s in sections)
                {
                    int h = s.Wanted(colW);
                    s.Bounds = new Rectangle(0, y, colW, h);
                    y += h + gap;
                }
            if (Height != y + 4) Height = y + 4;
        }
        HeightChanged?.Invoke(this, EventArgs.Empty);
    }
    Style style = new();
    bool loading;

    public event EventHandler? Changed;

    readonly TableLayoutPanel grid;

    /// <summary>Grows to fit every row (larger Windows text scaling makes the rows taller than the default height).</summary>
    /// <returns>How much taller it got.</returns>
    public int FitHeight()
    {
        if (compact) { int before = Height; ShowMore(); return Math.Max(0, Height - before); }
        int need = grid.GetPreferredSize(new Size(grid.Width, 0)).Height + 6;
        int grow = Math.Max(0, need - Height);
        Height += grow;
        return grow;
    }

    bool icons3D;
    /// <summary>Creatures: the icon is built as a 3D shape (a flat camera-facing one is dropped by animated models),
    /// so the picker and preview show the 3D versions.</summary>
    public bool Icons3D
    {
        get => icons3D;
        set { icons3D = value; UpdateLook(); }
    }

    /// <summary>The small side sketch (item, wall, beam, icon); off where a real preview is shown next to it.</summary>
    public bool Sketch
    {
        get => preview.Visible;
        set => preview.Visible = value;
    }

    public StyleEditor() : this(false) { }

    /// <param name="wide">Three groups side by side (Glow | Beam | Icon and walls) - the strip above the item list on the
    /// main window; otherwise one column (the look windows).</param>
    public StyleEditor(bool wide)
    {
        grid = new TableLayoutPanel { ColumnCount = wide ? 4 : 2, Dock = DockStyle.Fill, Padding = new Padding(0) };
        for (int i = 0; i < grid.ColumnCount; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var current = grid;
        TableLayoutPanel Group(int col)
        {
            var g = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 12, 0) };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.Controls.Add(g, col, 1); // row 0: "through walls", which is for all three
            return g;
        }
        if (wide)
        {
            // applies to glow, beam and icon alike: its own row across the top, not inside one group
            walls.Text = Tr("Visible through walls (glow, beam and icon)");
            walls.Margin = new Padding(3, 0, 3, 4);
            grid.Controls.Add(walls, 0, 0);
            grid.SetColumnSpan(walls, 3);
            current = Group(0);
        }
        void Row(Control a, Control? b = null)
        {
            int r = current.RowCount++;
            current.Controls.Add(a, 0, r);
            if (b != null) current.Controls.Add(b, 1, r);
            else current.SetColumnSpan(a, 2);
        }
        Label L(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(16, 6, 0, 0) };
        Row(glow);
        Row(L(Tr("Colour")), glowColor);
        Row(L(Tr("Strength %")), opacity);
        fill.Padding = new Padding(16, 0, 0, 0);
        Row(fill);
        new ToolTip { AutoPopDelay = 20000 }.SetToolTip(fill,
            Tr("Off: an outline - the item looks normal, its edges glow (also through walls).\nOn: the colour covers the whole item."));
        if (wide) current = Group(1);
        Row(beam);
        marker.Margin = new Padding(19, 3, 3, 3);
        Row(marker);
        Row(L(Tr("Colour")), beamColor);
        sameColor.Padding = new Padding(16, 0, 0, 0);
        Row(sameColor);
        Row(L(Tr("Length (m)")), length);
        Row(L(Tr("Width (cm)")), width);
        Row(L(Tr("Strength %")), beamStrength);
        if (wide)
        {
            current = Group(2);
            Row(new Label { Text = Tr("Icon above the item"), AutoSize = true, Padding = new Padding(0, 4, 0, 3) }); // lines up with the Glow / Beam boxes
        }
        else Row(walls);
        iconButton.FlatAppearance.BorderColor = Color.FromArgb(160, 160, 160);
        Row(L(Tr("Icon")), iconButton);
        Row(L(Tr("Icon size (cm)")), iconSize);
        Row(brighten);
        Row(L(Tr("Strength %")), brightStrength);
        new ToolTip { AutoPopDelay = 20000 }.SetToolTip(brighten,
            Tr("The item's own colours shine, day and night. Nothing is added to the model and nothing shows through walls.\nIt changes the game's material, so everything using that material brightens too."));
        if (!wide) Row(new Label { AutoSize = true }); // the last row takes the spare height - keeps the rows above even
        preview.Paint += PaintPreview;
        if (wide)
        {
            // the sketch (item, wall, beam, icon) right after the settings, not at the far edge of a wide window
            preview.Size = new Size(110, 170);
            preview.Margin = new Padding(8, 0, 0, 0);
            grid.Controls.Add(preview, 3, 0);
            grid.SetRowSpan(preview, 2);
        }
        else
        {
            preview.Dock = DockStyle.Right;
            Controls.Add(preview);
        }
        Controls.Add(grid);
        grid.BringToFront();
        Height = wide ? 190 : 400;
        glowColor.Click += (_, _) => Pick(true);
        beamColor.Click += (_, _) => Pick(false);
        foreach (var cb in new[] { glow, beam, sameColor, fill, brighten }) cb.CheckedChanged += (_, _) => Commit();
        walls.CheckedChanged += (_, _) => { if (loading) return; style.AllWalls = walls.Checked; Commit(); }; // the one-column window: one switch for all
        opacity.ValueChanged += (_, _) => Commit();
        brightStrength.ValueChanged += (_, _) => Commit();
        length.ValueChanged += (_, _) => Commit();
        width.ValueChanged += (_, _) => Commit();
        beamStrength.ValueChanged += (_, _) => Commit();
        marker.SelectedIndexChanged += (_, _) => Commit();
        iconButton.Click += (_, _) => PickIcon();
        iconSize.ValueChanged += (_, _) => Commit();
        labelSize.ValueChanged += (_, _) => Commit();
    }

    public Style Value
    {
        get => style.Clone();
        set
        {
            style = value.Clone();
            loading = true;
            glow.Checked = style.Glow;
            fill.Checked = style.GlowFill;
            beam.Checked = style.Beam;
            marker.SelectedIndex = style.BeamArrow ? 1 : 0;
            walls.Checked = style.AllWalls;
            brighten.Checked = style.Brighten;
            brightStrength.Value = Math.Clamp(style.BrightenStrength, 25, 300);
            opacity.Value = Math.Clamp(style.GlowOpacity, 5, 100);
            length.Value = Math.Clamp((decimal)Math.Round(style.BeamLength / UnitsPerMetre), 1, 150);
            width.Value = Math.Clamp((decimal)Math.Round(style.BeamWidth * 2 / UnitsPerCm), width.Minimum, width.Maximum);
            beamStrength.Value = Math.Clamp(style.BeamStrength, 1, 300);
            iconSize.Value = Math.Clamp((decimal)Math.Round(style.IconSize * 2 / UnitsPerCm), iconSize.Minimum, iconSize.Maximum);
            if (labelText.Text != style.LabelText) labelText.Text = style.LabelText;
            labelSize.Value = Math.Clamp((decimal)Math.Round(style.LabelSize * 2 / UnitsPerCm), labelSize.Minimum, labelSize.Maximum);
            sameColor.Checked = style.GlowColor.Equals(style.BeamColor, StringComparison.OrdinalIgnoreCase);
            loading = false;
            UpdateLook();
        }
    }

    void PickIcon()
    {
        var c = Materials.Parse(style.Beam ? style.BeamColor : style.GlowColor);
        if (IconPicker.Pick(this, style.Icon, icons3D || style.Icon3D, c, icons3D) is var (id, threeD))
        {
            style.Icon = id;
            if (!icons3D) style.Icon3D = threeD && id.Length > 0;
            UpdateLook();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    void Pick(bool forGlow)
    {
        using var dlg = new ColorDialog { FullOpen = true, Color = Materials.Parse(forGlow ? style.GlowColor : style.BeamColor) };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var hex = Materials.Hex(dlg.Color);
        if (forGlow) style.GlowColor = hex; else style.BeamColor = hex;
        if (sameColor.Checked) { if (forGlow) style.BeamColor = hex; else style.GlowColor = hex; }
        UpdateLook();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void Commit()
    {
        if (loading) return;
        style.Glow = glow.Checked;
        style.GlowFill = fill.Checked;
        style.Beam = beam.Checked;
        style.BeamArrow = marker.SelectedIndex == 1;
        style.Brighten = brighten.Checked;
        style.BrightenStrength = (int)brightStrength.Value;
        style.GlowOpacity = (int)opacity.Value;
        style.BeamLength = (int)Math.Round((float)length.Value * UnitsPerMetre);
        style.BeamWidth = (float)width.Value * UnitsPerCm / 2; // diameter in cm -> radius in game units
        style.BeamStrength = (int)beamStrength.Value;
        style.IconSize = (float)iconSize.Value * UnitsPerCm / 2; // width in cm -> half size in game units
        style.LabelSize = (float)labelSize.Value * UnitsPerCm / 2; // text height in cm -> half height in game units
        if (sameColor.Checked) style.BeamColor = style.GlowColor;
        UpdateLook();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void UpdateLook()
    {
        glowColor.BackColor = Materials.Parse(style.GlowColor);
        beamColor.BackColor = Materials.Parse(style.BeamColor);
        glowColor.Enabled = opacity.Enabled = fill.Enabled = style.Glow;
        walls.Enabled = style.Glow || style.Beam || style.Icon.Length > 0; // nothing to see through walls otherwise
        iconSize.Enabled = style.Icon.Length > 0;
        brightStrength.Enabled = style.Brighten;
        bool show3D = icons3D || style.Icon3D;
        var ic = Icons.Get(style.Icon);
        var model = Models3D.Get(style.Icon);
        iconButton.Text = model != null ? Tr(model.Name) + Tr(" (model)") : ic == null ? Tr("No icon") : show3D ? Tr(ic.Name) + " (3D)" : Tr(ic.Name);
        iconButton.Image?.Dispose();
        var ccol = Materials.Parse(style.Beam ? style.BeamColor : style.GlowColor);
        int tile = Math.Max(18, Font.Height); // as tall as the text
        iconButton.Image = model != null ? Models3D.Tile(model, ccol, tile)
                         : ic == null ? null : show3D ? IconPicker.Tile3D(ic, ccol, tile) : IconPicker.Tile(ic, ccol, tile);
        swatches.Current = Materials.Parse(style.GlowColor);
        swatches.Invalidate();
        beamColor.Enabled = width.Enabled = beamStrength.Enabled = sameColor.Enabled = marker.Enabled = style.Beam;
        length.Enabled = style.Beam && !style.BeamArrow; // an arrow has its own size
        if (sameColor.Checked) beamColor.Enabled = false;
        if (sections.Count > 0) RefreshCards();
        preview.Invalidate();
    }

    /// <summary>A crate behind a wall, with its glow and beam as they'd roughly look.</summary>
    void PaintPreview(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = preview.Width, h = preview.Height, ground = h - 24;
        var item = new Rectangle(w / 2 - 14, ground - 26, 28, 26);
        var wall = new Rectangle(w / 2 - 4, ground - 70, 40, 70);
        using (var floor = new SolidBrush(Color.FromArgb(50, 56, 64))) g.FillRectangle(floor, 0, ground, w, h - ground);
        using (var crate = new SolidBrush(Color.FromArgb(150, 120, 80))) g.FillRectangle(crate, item);
        void Glow()
        {
            if (!style.Glow) return;
            // additive light: the crate stays visible, brighter with strength
            using var b = new SolidBrush(Color.FromArgb((int)Math.Min(200, 255 * style.GlowOpacity / 100f), Materials.Parse(style.GlowColor)));
            g.FillRectangle(b, item);
        }
        void Beam()
        {
            if (!style.Beam) return;
            var c = Materials.Parse(style.BeamColor);
            if (style.BeamArrow)
            {
                // a solid arrow just above the crate, pointing down at it
                float aw = Math.Clamp(style.BeamWidth * 1.6f, 10, 30), ah = aw * 1.7f, tipY = item.Top - 3;
                using var ab = new SolidBrush(Color.FromArgb((int)(120 + 135 * Math.Clamp(style.BeamStrength / 100f, 0, 1)), c));
                g.FillPolygon(ab, new[] { new PointF(w / 2f, tipY), new PointF(w / 2f - aw / 2, tipY - aw * 0.8f), new PointF(w / 2f + aw / 2, tipY - aw * 0.8f) });
                g.FillRectangle(ab, w / 2f - aw * 0.18f, tipY - ah, aw * 0.36f, ah - aw * 0.8f + 1);
                return;
            }
            float top = Math.Max(4, item.Top - (item.Top - 4) * Math.Min(1f, style.BeamLength / (30f * UnitsPerMetre)));
            float bw = Math.Clamp(style.BeamWidth / 2.5f, 2, 40);
            float bs = Math.Clamp(style.BeamStrength / 100f, 0.02f, 1f); // see-through like in game
            // soft round column (edges fade) with a brighter core, fading towards the top; starts at the item's top
            var r = new RectangleF(w / 2f - bw / 2, top, bw, item.Top - top);
            using (var path = new GraphicsPath())
            {
                path.AddRectangle(r);
                using var soft = new PathGradientBrush(new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) })
                { CenterColor = Color.FromArgb((int)(150 * bs), c), SurroundColors = new[] { Color.FromArgb(0, c) }, CenterPoint = new PointF(r.Left + r.Width / 2, r.Top + r.Height * 0.75f) };
                g.FillRectangle(soft, r);
            }
            var core = new RectangleF(w / 2f - Math.Max(1, bw * 0.35f) / 2, top, Math.Max(1, bw * 0.35f), item.Top - top);
            using var lg = new LinearGradientBrush(core, Color.FromArgb((int)(60 * bs), c), Color.FromArgb((int)(255 * bs), Color.FromArgb((c.R + 255) / 2, (c.G + 255) / 2, (c.B + 255) / 2)), LinearGradientMode.Vertical);
            g.FillRectangle(lg, core);
        }
        Glow();
        Beam();
        using (var wb = new SolidBrush(Color.FromArgb(105, 110, 118))) g.FillRectangle(wb, wall);
        if (style.GlowFx.Walls || style.BeamFx.Walls) { var clip = g.Clip; g.SetClip(wall); Glow(); Beam(); g.Clip = clip; }
        using var f = new Font(Font.FontFamily, 7.5f);
        using var tb = new SolidBrush(Color.FromArgb(160, 170, 180));
        g.DrawString("wall", f, tb, wall.Left + 6, wall.Bottom - 14);
        var ic = Icons.Get(style.Icon);
        var model = Models3D.Get(style.Icon);
        if (ic != null || model != null)
        {
            // at the top of the beam, or just above the crate
            float top = !style.Beam ? item.Top - 4
                : style.BeamArrow ? item.Top - 3 - Math.Clamp(style.BeamWidth * 1.6f, 10, 30) * 1.7f - 3
                : Math.Max(4, item.Top - (item.Top - 4) * Math.Min(1f, style.BeamLength / (30f * UnitsPerMetre)));
            int sz = (int)Math.Clamp(style.IconSize / 1.6f, 14, 44);
            float y = Math.Max(2, top - sz);
            var icol = Materials.Parse(style.Beam ? style.BeamColor : style.GlowColor);
            using var tile = model != null ? Models3D.Tile(model, icol, sz)
                           : icons3D || style.Icon3D ? IconPicker.Tile3D(ic!, icol, sz) : IconPicker.Tile(ic!, icol, sz);
            tile.MakeTransparent(Color.FromArgb(24, 28, 34));
            g.DrawImage(tile, w / 2f - sz / 2f, y, sz, sz);
        }
    }
}
