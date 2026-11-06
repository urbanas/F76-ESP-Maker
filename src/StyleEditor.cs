using System.Drawing.Drawing2D;

namespace ItemFinder;

/// <summary>Edits one <see cref="Style"/>: glow and beam on/off, colours, opacity, beam length and
/// thickness, see-through walls; with a small picture of the result.</summary>
public sealed class StyleEditor : UserControl
{
    public const float UnitsPerMetre = 70f;
    /// <summary>Game units per centimetre (1 unit ≈ 1.43 cm).</summary>
    const float UnitsPerCm = 0.7f;

    readonly CheckBox glow = new() { Text = "Glow (item shines)", AutoSize = true };
    readonly Button glowColor = new() { Width = 70, Height = 26, FlatStyle = FlatStyle.Flat };
    readonly NumericUpDown opacity = new() { Minimum = 5, Maximum = 100, Increment = 5, Width = 55 };
    readonly CheckBox beam = new() { Text = "Marker above the item", AutoSize = true };
    readonly ComboBox marker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Items = { "Beam of light into the sky", "Arrow pointing down" } };
    readonly Button beamColor = new() { Width = 70, Height = 26, FlatStyle = FlatStyle.Flat };
    readonly NumericUpDown length = new() { Minimum = 1, Maximum = 150, Width = 55 };
    readonly NumericUpDown width = new() { Minimum = 2, Maximum = 300, Increment = 2, Width = 55 };
    readonly NumericUpDown beamStrength = new() { Minimum = 1, Maximum = 300, Increment = 5, Width = 55 };
    // grows with the text (larger Windows text scaling) instead of cutting the picture off
    readonly Button iconButton = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(130, 26), Padding = new Padding(2), Margin = new Padding(3, 3, 3, 3), FlatStyle = FlatStyle.Flat, TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft };
    readonly NumericUpDown iconSize = new() { Minimum = 20, Maximum = 600, Increment = 10, Width = 55 };
    readonly NumericUpDown labelSize = new() { Minimum = 10, Maximum = 200, Increment = 5, Width = 55 };
    readonly TextBox labelText = new() { Width = 170, PlaceholderText = "the item's name" };
    readonly NumericUpDown hideAfter =new() { Minimum = 5, Maximum = 600, Increment = 5, Width = 55 };
    readonly CheckBox walls = new() { Text = "Visible through walls", AutoSize = true };
    readonly CheckBox brighten = new() { Text = "Brighten its own colours", AutoSize = true };
    readonly NumericUpDown brightStrength = new() { Minimum = 25, Maximum = 300, Increment = 25, Width = 55 };
    readonly CheckBox fill = new() { Text = "Fill the whole item", AutoSize = true };
    readonly CheckBox sameColor = new() { Text = "Same colour as the glow", AutoSize = true };
    readonly Panel preview = new() { Width = 110, BackColor = Color.FromArgb(24, 28, 34) };
    // the quick header (Compact): a ready-made look, one colour, the icon, through walls - the rest under "More settings"
    readonly TableLayoutPanel header = new() { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false, Padding = new Padding(0, 0, 0, 4) };
    readonly ComboBox preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly Button quickColour = new() { Width = 70, Height = 26, FlatStyle = FlatStyle.Flat };
    readonly Button quickIcon = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(130, 26), Padding = new Padding(2), FlatStyle = FlatStyle.Flat, TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft };
    readonly CheckBox quickWalls = new() { Text = "Visible through walls", AutoSize = true };
    readonly PresetTiles tiles = new() { Dock = DockStyle.Fill };
    readonly SwatchRow swatches = new();
    readonly Label tipLabel = new() { Dock = DockStyle.Fill, AutoSize = false, Height = 36, ForeColor = Color.DimGray, UseMnemonic = false, Padding = new Padding(0, 2, 0, 0) };

    /// <summary>Under the pictures: what the pointed-at look does (else the one in use; a custom look says so).</summary>
    void ShowTip(int hovered)
    {
        int i = hovered >= 0 ? hovered : tiles.Selected;
        tipLabel.Text = i >= 0 ? Presets[i].Tip : "Your own mix - fine-tune it below. Click a picture to start from a ready-made look.";
    }
    bool compact;

    // Compact: the details as one card per effect (Glow / Marker / Icon / Shine): a switch and a one-line summary, a click
    // on the line opens it (one at a time). The cards drive the grid's controls (the grid itself stays hidden).
    readonly Panel cards = new() { Dock = DockStyle.Fill, Visible = false, BackColor = Color.FromArgb(217, 220, 215), Padding = new Padding(1, 1, 1, 0) };
    readonly Label fineLabel = new() { Text = "FINE-TUNE", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 8, 0, 2) };
    readonly List<Card> cardList = new();
    /// <summary>The open card, -1 = all closed (remembered while the app runs).</summary>
    static int openCard;
    /// <summary>Joined buttons for a number box's usual values: Soft 15 / Medium 30 / Strong 60 ...</summary>
    readonly List<(Seg Seg, NumericUpDown Box, string[] Names, int[] Values)> steps = new();
    readonly Seg shapeSeg = new("Outline", "Whole item"), kindSeg = new("Beam", "Arrow");
    readonly SwatchRow markerSwatches = new() { Margin = new Padding(0, 0, 0, 2) };
    readonly Label markerSame = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 0, 0) };
    readonly LinkLabel markerOwn = new() { AutoSize = true, Margin = new Padding(0, 6, 3, 0) };
    Control[] heightRow = Array.Empty<Control>();

    /// <summary>One effect: a head line (switch, name, summary, ▸) and its settings under it while open.</summary>
    sealed class Card : Panel
    {
        public readonly Toggle Switch = new() { Anchor = AnchorStyles.Left };
        public readonly Label Summary = new() { Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
        public readonly TableLayoutPanel Body = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Color.White, Padding = new Padding(10, 4, 8, 8), Visible = false };
        readonly TableLayoutPanel head = new() { Dock = DockStyle.Top, Height = 34, ColumnCount = 4, BackColor = Color.FromArgb(243, 244, 242), Padding = new Padding(6, 0, 8, 0), Cursor = Cursors.Hand };
        readonly Label title = new() { AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = false };
        readonly Label chevron = new() { AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = Color.DimGray, Text = "▸" };

        /// <summary>The head line was clicked (not the switch).</summary>
        public event Action? HeadClicked;

        public Card(string name, string what)
        {
            title.Text = name;
            Dock = DockStyle.Top;
            Padding = new Padding(0, 0, 0, 1); // the line under it (the card's own colour)
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            head.Controls.Add(Switch, 0, 0);
            head.Controls.Add(title, 1, 0);
            head.Controls.Add(Summary, 2, 0);
            head.Controls.Add(chevron, 3, 0);
            foreach (var c in new Control[] { head, title, Summary, chevron }) c.Click += (_, _) => HeadClicked?.Invoke();
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var desc = new Label { Text = what, AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = Color.DimGray, UseMnemonic = false, Margin = new Padding(0, 2, 0, 6) };
            Body.Controls.Add(desc, 0, Body.RowCount++);
            Body.SetColumnSpan(desc, 2);
            Controls.Add(Body);
            Controls.Add(head); // docked first: on top
            BackColor = Color.FromArgb(217, 220, 215);
        }

        protected override void OnFontChanged(EventArgs e) { title.Font = new Font(Font, FontStyle.Bold); base.OnFontChanged(e); }

        public bool Open
        {
            get => Body.Visible;
            set { Body.Visible = value; chevron.Text = value ? "▾" : "▸"; }
        }

        /// <summary>Sets the height for its content; returns it.</summary>
        public int Fit(int width) => Height = head.Height + (Open ? Body.GetPreferredSize(new Size(width, 0)).Height : 0) + 1;
    }

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
            return s;
        }
        Control[] Row(Card c, string label, params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            f.Controls.AddRange(cs);
            var l = L(label);
            int r = c.Body.RowCount++;
            c.Body.Controls.Add(l, 0, r);
            c.Body.Controls.Add(f, 1, r);
            return new Control[] { l, f };
        }
        string[] strength = { "Soft", "Medium", "Strong" };

        var glowCard = new Card("Glow", "A coloured light on the item itself.");
        Row(glowCard, "Shape", shapeSeg);
        Row(glowCard, "Strength", Steps(opacity, strength, new[] { 15, 30, 60 }), opacity, Unit("%"));
        shapeSeg.Picked += i => fill.Checked = i == 1;
        glowCard.Switch.Toggled += () => glow.Checked = glowCard.Switch.Checked;

        var markerCard = new Card("Marker", "Something above the item, so you see where it is from far away.");
        Row(markerCard, "Kind", kindSeg);
        kindSeg.Picked += i => marker.SelectedIndex = i;
        heightRow = Row(markerCard, "Height", Steps(length, new[] { "Short", "Medium", "Tall" }, new[] { 10, 20, 30 }), length, Unit("m"));
        Row(markerCard, "Thickness", Steps(width, new[] { "Thin", "Normal", "Thick" }, new[] { 6, 10, 20 }), width, Unit("cm"));
        Row(markerCard, "Strength", Steps(beamStrength, strength, new[] { 15, 30, 60 }), beamStrength, Unit("%"));
        Row(markerCard, "Colour", markerSame, markerOwn);
        markerCard.Body.Controls.Add(markerSwatches, 0, markerCard.Body.RowCount++);
        markerCard.Body.SetColumnSpan(markerSwatches, 2);
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
        markerCard.Switch.Toggled += () => beam.Checked = markerCard.Switch.Checked;

        var iconCard = new Card("Icon", "A small picture floating above the item.");
        Row(iconCard, "Picture", iconButton);
        Row(iconCard, "Size", Steps(iconSize, new[] { "S", "M", "L" }, new[] { 40, 70, 120 }), iconSize, Unit("cm"));
        iconCard.Switch.Toggled += () =>
        {
            if (iconCard.Switch.Checked) PickIcon();
            else { style.Icon = ""; Changed?.Invoke(this, EventArgs.Empty); }
            UpdateLook(); // the picker cancelled: the switch goes back off
        };

        var labelCard = new Card("Name", "The item's name floats above it (above the icon) and turns to face you. Not on enemies.");
        Row(labelCard, "Text", labelText);
        new ToolTip { AutoPopDelay = 20000 }.SetToolTip(labelText,
            "Leave empty to show each item's own name.\nText typed here shows on every item using this look (for one item only, give that item its own look).");
        labelText.TextChanged += (_, _) =>
        {
            if (loading || style.LabelText == labelText.Text) return;
            style.LabelText = labelText.Text;
            if (cardList.Count > 4) cardList[4].Summary.Text = LabelSummary();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        Row(labelCard, "Text height", Steps(labelSize, new[] { "S", "M", "L" }, new[] { 20, 30, 50 }), labelSize, Unit("cm"));
        labelCard.Switch.Toggled += () => { if (loading) return; style.Label = labelCard.Switch.Checked; UpdateLook(); Changed?.Invoke(this, EventArgs.Empty); };

        var timerCard = new Card("Hide after", "Everything added (glow, marker, icon, name) shrinks away after this time. The clock starts when the game loads the item, so it starts again when you come back to the area. Not on enemies.");
        Row(timerCard, "After", Steps(hideAfter, new[] { "30 s", "1 min", "2 min" }, new[] { 30, 60, 120 }), hideAfter, Unit("s"));
        timerCard.Switch.Toggled += () => { if (loading) return; style.HideAfter = timerCard.Switch.Checked ? (int)hideAfter.Value : 0; UpdateLook(); Changed?.Invoke(this, EventArgs.Empty); };

        var shineCard = new Card("Shine", "The item's own colours light up, day and night. Nothing is added around it, and it doesn't show through walls.");
        Row(shineCard, "Strength", Steps(brightStrength, strength, new[] { 50, 100, 200 }), brightStrength, Unit("%"));
        shineCard.Switch.Toggled += () => brighten.Checked = shineCard.Switch.Checked;

        cardList.AddRange(new[] { glowCard, markerCard, iconCard, shineCard, labelCard, timerCard });
        for (int i = 0; i < cardList.Count; i++)
        {
            int k = i;
            cardList[i].HeadClicked += () => { openCard = openCard == k ? -1 : k; ShowMore(); };
            cardList[i].Switch.Toggled += () => { if (cardList[k].Switch.Checked && openCard != k) { openCard = k; ShowMore(); } }; // switched on: show its settings
        }
        for (int i = cardList.Count - 1; i >= 0; i--) cards.Controls.Add(cardList[i]); // docked: the last added sits on top
        Controls.Add(cards);
        cards.BringToFront(); // docked last: fills what the header leaves
    }

    string LabelSummary() => !style.Label ? "Off" : (style.LabelText.Trim().Length > 0 ? $"\"{style.LabelText.Trim()}\"" : "Item's name") + $" · {labelSize.Value} cm";

    /// <summary>The cards show the look: switches, lit buttons, summaries.</summary>
    void RefreshCards()
    {
        string Step(NumericUpDown box, string unit)
        {
            var (_, _, names, values) = steps.First(s => s.Box == box);
            int i = Array.IndexOf(values, (int)box.Value);
            return i >= 0 ? names[i].ToLowerInvariant() : $"{box.Value} {unit}";
        }
        cardList[0].Switch.Checked = style.Glow;
        cardList[1].Switch.Checked = style.Beam;
        cardList[2].Switch.Checked = style.Icon.Length > 0;
        cardList[3].Switch.Checked = style.Brighten;
        shapeSeg.Selected = style.GlowFill ? 1 : 0;
        kindSeg.Selected = style.BeamArrow ? 1 : 0;
        foreach (var (seg, box, _, values) in steps) seg.Selected = Array.IndexOf(values, (int)box.Value);
        foreach (var c in heightRow) c.Visible = !style.BeamArrow; // an arrow has its own size
        bool own = !sameColor.Checked;
        markerSame.Text = own ? "Its own colour ·" : "Same as the glow ·";
        markerOwn.Text = own ? "same as the glow" : "use a different colour";
        markerSwatches.Visible = own;
        markerSwatches.Current = Materials.Parse(style.BeamColor);
        markerSwatches.Invalidate();
        cardList[0].Summary.Text = style.Glow ? $"{(style.GlowFill ? "Whole item" : "Outline")} · {Step(opacity, "%")}" : "Off";
        cardList[1].Summary.Text = style.Beam ? (style.BeamArrow ? "Arrow" : $"Beam · {length.Value} m") + $" · {Step(width, "cm")}" + (own ? " · own colour" : "") : "Off";
        cardList[2].Summary.Text = style.Icon.Length > 0 ? $"{iconButton.Text} · {iconSize.Value} cm" : "None";
        cardList[3].Summary.Text = style.Brighten ? $"On · {Step(brightStrength, "%")}" : "Off";
        bool was = loading;
        loading = true; // setting the switches must not write back
        cardList[4].Switch.Checked = style.Label;
        cardList[5].Switch.Checked = style.HideAfter > 0;
        loading = was;
        cardList[4].Summary.Text = LabelSummary();
        cardList[5].Summary.Text = style.HideAfter > 0 ? (style.HideAfter % 60 == 0 ? $"{style.HideAfter / 60} min" : $"{style.HideAfter} s") : "Never";

        ShowMore();
    }

    /// <summary>A ready-made look: what it sets (colour, icon and through walls stay as they are).</summary>
    sealed record Preset(string Name, string Tip, Action<Style> Apply)
    {
        public override string ToString() => Name;
    }

    static void GlowAs(Style s, bool fill, int strength) { s.Glow = true; s.GlowFill = fill; s.GlowOpacity = strength; }
    static void BeamAs(Style s, bool on, bool arrow = false, int metres = 20, int strength = 30, float width = 3.5f)
    { s.Beam = on; s.BeamArrow = arrow; s.BeamLength = (int)(metres * UnitsPerMetre); s.BeamStrength = strength; s.BeamWidth = width; s.Brighten = false; }

    static readonly Preset[] Presets =
    {
        new("Outline", "The item looks normal, its edges glow - also through walls.", s => { GlowAs(s, false, 30); BeamAs(s, false); }),
        new("Outline + beam", "Edges glow, and a 20 m beam of light rises from it.", s => { GlowAs(s, false, 30); BeamAs(s, true); }),
        new("Outline + arrow", "Edges glow, and an arrow above points down at it.", s => { GlowAs(s, false, 30); BeamAs(s, true, arrow: true); }),
        new("Fill", "The whole item in a see-through colour (the classic ESP look).", s => { GlowAs(s, true, 30); BeamAs(s, false); }),
        new("Fill + beam", "The whole item coloured, and a 20 m beam.", s => { GlowAs(s, true, 30); BeamAs(s, true); }),
        new("Bright fill", "The whole item strongly coloured - easy to spot, hides its details.", s => { GlowAs(s, true, 60); BeamAs(s, false); }),
        new("Tall beacon", "No glow, a strong 30 m beam - seen from far away.", s => { s.Glow = false; BeamAs(s, true, metres: 30, strength: 60, width: 7f); }),
        new("Bright colours", "The item's own colours shine, day and night. Nothing is added to it, nothing shows through walls.", s => { s.Glow = false; BeamAs(s, false); s.Brighten = true; s.BrightenStrength = 100; }),
    };
    const string CustomName = "Custom";

    /// <summary>Gives a look one of the ready-made looks by name (colour, icon and through walls stay).</summary>
    public static void ApplyPreset(Style st, string name) => Presets.FirstOrDefault(p => p.Name == name)?.Apply(st);

    /// <summary>The preset this look matches, or null (a look of its own).</summary>
    static Preset? Matching(Style st) => Presets.FirstOrDefault(p =>
    {
        var t = st.Clone();
        p.Apply(t);
        bool beamSame = !st.Beam || t.BeamArrow == st.BeamArrow && t.BeamStrength == st.BeamStrength && Math.Abs(t.BeamWidth - st.BeamWidth) < 0.01f &&
                                    (st.BeamArrow || t.BeamLength == st.BeamLength);
        return t.Glow == st.Glow && (!st.Glow || t.GlowFill == st.GlowFill && t.GlowOpacity == st.GlowOpacity) && t.Beam == st.Beam && beamSame &&
               t.Brighten == st.Brighten && (!st.Brighten || t.BrightenStrength == st.BrightenStrength);
    });

    /// <summary>The quick header on top (ready-made looks, one colour, icon, through walls) with the detailed settings
    /// folded under "More settings" - the main window's look panel.</summary>
    public bool Compact
    {
        get => compact;
        set
        {
            compact = value;
            header.Visible = value;
            if (value && cardList.Count == 0) { BuildCards(); UpdateLook(); }
            ShowMore();
        }
    }

    /// <summary>Dev (UI test): pick a ready-made look by index (-1 = none), open / close a card, what the box shows.</summary>
    internal void TestPreset(int i) => preset.SelectedIndex = i;
    internal void TestMore() { openCard = openCard >= 0 ? -1 : 0; ShowMore(); }
    internal string TestShown => preset.SelectedItem?.ToString() ?? "";
    internal bool TestMoreOpen => compact ? openCard >= 0 : grid.Visible;

    /// <summary>Height of the quick header (dev: the tour's pictures).</summary>
    internal int HeaderHeight => header.Visible ? header.Height : Height;

    /// <summary>A card was opened / closed: the height changed.</summary>
    public event EventHandler? HeightChanged;

    void ShowMore()
    {
        grid.Visible = !compact;
        cards.Visible = compact;
        if (compact)
        {
            int w = Math.Max(100, Width);
            int h = header.GetPreferredSize(new Size(w, 0)).Height + 1;
            for (int i = 0; i < cardList.Count; i++) { cardList[i].Open = i == openCard; h += cardList[i].Fit(w - 2); }
            if (Height != h + 4) Height = h + 4; // a little air under the last card
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
            walls.Text = "Visible through walls (glow, beam and icon)";
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
        Row(L("Colour"), glowColor);
        Row(L("Strength %"), opacity);
        fill.Padding = new Padding(16, 0, 0, 0);
        Row(fill);
        new ToolTip { AutoPopDelay = 20000 }.SetToolTip(fill,
            "Off: an outline - the item looks normal, its edges glow (also through walls).\nOn: the colour covers the whole item.");
        if (wide) current = Group(1);
        Row(beam);
        marker.Margin = new Padding(19, 3, 3, 3);
        Row(marker);
        Row(L("Colour"), beamColor);
        sameColor.Padding = new Padding(16, 0, 0, 0);
        Row(sameColor);
        Row(L("Length (m)"), length);
        Row(L("Width (cm)"), width);
        Row(L("Strength %"), beamStrength);
        if (wide)
        {
            current = Group(2);
            Row(new Label { Text = "Icon above the item", AutoSize = true, Padding = new Padding(0, 4, 0, 3) }); // lines up with the Glow / Beam boxes
        }
        else Row(walls);
        iconButton.FlatAppearance.BorderColor = Color.FromArgb(160, 160, 160);
        Row(L("Icon"), iconButton);
        Row(L("Icon size (cm)"), iconSize);
        Row(brighten);
        Row(L("Strength %"), brightStrength);
        new ToolTip { AutoPopDelay = 20000 }.SetToolTip(brighten,
            "The item's own colours shine, day and night. Nothing is added to the model and nothing shows through walls.\n" +
            "It changes the game's material, so everything using that material brightens too.");
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
        // the quick header (shown in Compact mode)
        void HRow(string label, Control c)
        {
            int r = header.RowCount++;
            header.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) }, 0, r);
            header.Controls.Add(c, 1, r);
        }
        foreach (var pr in Presets) preset.Items.Add(pr);
        preset.Items.Add(CustomName);
        // the ready-made looks as pictures (the dropdown stays behind them, unseen: it holds which one is in use)
        tiles.Names = Presets.Select(p => p.Name).ToArray();
        tiles.MinimumSize = new Size(340, tiles.GetPreferredSize(Size.Empty).Height);
        tiles.Picked += i => preset.SelectedIndex = i;
        tiles.Hovered += i => ShowTip(i);
        preset.SelectedIndexChanged += (_, _) => { tiles.Selected = preset.SelectedIndex < Presets.Length ? preset.SelectedIndex : -1; tiles.Invalidate(); ShowTip(-1); };
        int tr = header.RowCount++;
        header.Controls.Add(tiles, 0, tr);
        header.SetColumnSpan(tiles, 2);
        int dr = header.RowCount++;
        header.Controls.Add(tipLabel, 0, dr);
        header.SetColumnSpan(tipLabel, 2);
        swatches.Picked += c =>
        {
            if (c is not Color colour)
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = Materials.Parse(style.GlowColor) };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                colour = dlg.Color;
            }
            style.GlowColor = style.BeamColor = Materials.Hex(colour);
            Value = style;
            Changed?.Invoke(this, EventArgs.Empty);
        };
        HRow("Colour", swatches);
        HRow("Icon", quickIcon);
        int wr = header.RowCount++;
        header.Controls.Add(quickWalls, 0, wr);
        header.SetColumnSpan(quickWalls, 2);
        int mr = header.RowCount++;
        header.Controls.Add(fineLabel, 0, mr);
        header.SetColumnSpan(fineLabel, 2);
        quickIcon.FlatAppearance.BorderColor = Color.FromArgb(160, 160, 160);
        Controls.Add(header);
        header.SendToBack(); // docked first: on top of the grid
        var tips = new ToolTip { AutoPopDelay = 20000 };
        tips.SetToolTip(preset, string.Join("\n", Presets.Select(pr => $"{pr.Name}: {pr.Tip}")) + "\n\nColour, icon and through walls stay as they are.");
        tips.SetToolTip(quickColour, "One colour for the glow and the marker (a different marker colour: the Marker card).");
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (loading || preset.SelectedItem is not Preset pr) return;
            pr.Apply(style);
            Value = style; // the detailed settings follow
            Changed?.Invoke(this, EventArgs.Empty);
        };
        quickColour.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, Color = Materials.Parse(style.GlowColor) };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            style.GlowColor = style.BeamColor = Materials.Hex(dlg.Color);
            Value = style;
            Changed?.Invoke(this, EventArgs.Empty);
        };
        quickWalls.CheckedChanged += (_, _) => { if (!loading) walls.Checked = quickWalls.Checked; };
        quickIcon.Click += (_, _) => PickIcon();

        glowColor.Click += (_, _) => Pick(true);
        beamColor.Click += (_, _) => Pick(false);
        foreach (var cb in new[] { glow, beam, walls, sameColor, fill, brighten }) cb.CheckedChanged += (_, _) => Commit();
        opacity.ValueChanged += (_, _) => Commit();
        brightStrength.ValueChanged += (_, _) => Commit();
        length.ValueChanged += (_, _) => Commit();
        width.ValueChanged += (_, _) => Commit();
        beamStrength.ValueChanged += (_, _) => Commit();
        marker.SelectedIndexChanged += (_, _) => Commit();
        iconButton.Click += (_, _) => PickIcon();
        iconSize.ValueChanged += (_, _) => Commit();
        labelSize.ValueChanged += (_, _) => Commit();
        hideAfter.ValueChanged += (_, _) => Commit();
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
            walls.Checked = style.ThroughWalls;
            brighten.Checked = style.Brighten;
            brightStrength.Value = Math.Clamp(style.BrightenStrength, 25, 300);
            opacity.Value = Math.Clamp(style.GlowOpacity, 5, 100);
            length.Value = Math.Clamp((decimal)Math.Round(style.BeamLength / UnitsPerMetre), 1, 150);
            width.Value = Math.Clamp((decimal)Math.Round(style.BeamWidth * 2 / UnitsPerCm), width.Minimum, width.Maximum);
            beamStrength.Value = Math.Clamp(style.BeamStrength, 1, 300);
            iconSize.Value = Math.Clamp((decimal)Math.Round(style.IconSize * 2 / UnitsPerCm), iconSize.Minimum, iconSize.Maximum);
            if (labelText.Text != style.LabelText) labelText.Text = style.LabelText;
            labelSize.Value = Math.Clamp((decimal)Math.Round(style.LabelSize * 2 / UnitsPerCm), labelSize.Minimum, labelSize.Maximum);
            if (style.HideAfter > 0) hideAfter.Value = Math.Clamp(style.HideAfter, (int)hideAfter.Minimum, (int)hideAfter.Maximum);
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
        style.ThroughWalls = walls.Checked;
        style.Brighten = brighten.Checked;
        style.BrightenStrength = (int)brightStrength.Value;
        style.GlowOpacity = (int)opacity.Value;
        style.BeamLength = (int)Math.Round((float)length.Value * UnitsPerMetre);
        style.BeamWidth = (float)width.Value * UnitsPerCm / 2; // diameter in cm -> radius in game units
        style.BeamStrength = (int)beamStrength.Value;
        style.IconSize = (float)iconSize.Value * UnitsPerCm / 2; // width in cm -> half size in game units
        style.LabelSize = (float)labelSize.Value * UnitsPerCm / 2; // text height in cm -> half height in game units
        if (style.HideAfter > 0) style.HideAfter = (int)hideAfter.Value;
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
        iconButton.Text = model != null ? model.Name + " (model)" : ic == null ? "No icon" : show3D ? ic.Name + " (3D)" : ic.Name;
        iconButton.Image?.Dispose();
        var ccol = Materials.Parse(style.Beam ? style.BeamColor : style.GlowColor);
        int tile = Math.Max(18, Font.Height); // as tall as the text
        iconButton.Image = model != null ? Models3D.Tile(model, ccol, tile)
                         : ic == null ? null : show3D ? IconPicker.Tile3D(ic, ccol, tile) : IconPicker.Tile(ic, ccol, tile);
        quickIcon.Text = iconButton.Text;
        quickIcon.Image?.Dispose();
        quickIcon.Image = iconButton.Image == null ? null : new Bitmap(iconButton.Image);
        quickColour.BackColor = Materials.Parse(style.GlowColor);
        tiles.Colour = swatches.Current = Materials.Parse(style.GlowColor);
        tiles.Invalidate(); swatches.Invalidate();
        bool was = loading;
        loading = true;
        quickWalls.Checked = style.ThroughWalls;
        preset.SelectedItem = (object?)Matching(style) ?? CustomName;
        loading = was;
        beamColor.Enabled = width.Enabled = beamStrength.Enabled = sameColor.Enabled = marker.Enabled = style.Beam;
        length.Enabled = style.Beam && !style.BeamArrow; // an arrow has its own size
        if (sameColor.Checked) beamColor.Enabled = false;
        if (cardList.Count > 0) RefreshCards();
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
        if (style.ThroughWalls) { var clip = g.Clip; g.SetClip(wall); Glow(); Beam(); g.Clip = clip; }
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
