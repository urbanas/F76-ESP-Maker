using static ItemFinder.Loc;
using System.Drawing.Drawing2D;

namespace ItemFinder;

/// <summary>Edits one <see cref="Style"/> (the look pop-up): presets on top, then The item | the layers above it (a stack in
/// any order: beam, arrow, icon, item copy, text - user 2026-11-15) | the picked layer's settings. Stacked when narrow.</summary>
public sealed class StyleEditor : UserControl
{
    public const float UnitsPerMetre = 70f;
    /// <summary>Game units per centimetre (1 unit ≈ 1.43 cm).</summary>
    const float UnitsPerCm = 0.7f;

    // value holders: the step buttons write them, Commit reads them into the look
    readonly NumericUpDown opacity = new() { Minimum = 5, Maximum = 100, Increment = 5, Width = 55 };
    readonly NumericUpDown length = new() { Minimum = 1, Maximum = 150, Width = 55 };
    readonly NumericUpDown width = new() { Minimum = 2, Maximum = 300, Increment = 2, Width = 55 };
    readonly NumericUpDown beamStrength = new() { Minimum = 1, Maximum = 300, Increment = 5, Width = 55 };
    readonly NumericUpDown arrowWidth = new() { Minimum = 2, Maximum = 300, Increment = 2, Width = 55 };
    // grows with the text (larger Windows text scaling) instead of cutting the picture off
    readonly Button iconButton = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(130, 26), Padding = new Padding(2), Margin = new Padding(3, 3, 3, 3), FlatStyle = FlatStyle.Flat, TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft };
    readonly NumericUpDown iconStrength = new() { Minimum = 10, Maximum = 300, Increment = 10, Width = 55 };
    readonly NumericUpDown labelStrength = new() { Minimum = 10, Maximum = 300, Increment = 10, Width = 55 };
    readonly NumericUpDown copyStrength = new() { Minimum = 10, Maximum = 300, Increment = 10, Width = 55 };
    readonly NumericUpDown iconSize = new() { Minimum = 10, Maximum = 2000, Increment = 10, Width = 68 }; // % of the item's size
    readonly NumericUpDown copySize = new() { Minimum = 10, Maximum = 2000, Increment = 10, Width = 68 }; // % of the item's size
    readonly NumericUpDown labelSize = new() { Minimum = 10, Maximum = 2000, Increment = 10, Width = 68 }; // cm (like Mini item's 20 m)
    readonly TextBox labelText = new() { Width = 170, PlaceholderText = Tr("the item's name") };
    readonly CheckBox labelCat = new() { Text = Tr("Start with the category"), AutoSize = true };
    readonly NumericUpDown brightStrength = new() { Minimum = 25, Maximum = 300, Increment = 25, Width = 55 };
    readonly NumericUpDown xrayStrength = new() { Minimum = 25, Maximum = 300, Increment = 25, Width = 55 }; // Off + X-ray
    readonly List<Control> xrayRows = new();
    /// <summary>The beam has the glow's colour (state only, not shown).</summary>
    readonly CheckBox sameColor = new();
    readonly SwatchRow swatches = new() { Margin = new Padding(0, 0, 0, 2) };
    bool compact;

    readonly Panel board = new() { Dock = DockStyle.Fill };
    readonly List<Tile> presetTiles = new();
    readonly List<FlowLayoutPanel> fxFlows = new();
    /// <summary>Joined buttons for a number box's usual values: Soft 15 / Medium 30 / Strong 60 ...</summary>
    readonly List<(Seg Seg, NumericUpDown Box, string[] Names, int[] Values)> steps = new();
    readonly Seg itemSeg = new(Tr("Outline"), Tr("Fill"), Tr("Bright"), Tr("Off"));
    readonly SwatchRow markerSwatches = new() { Margin = new Padding(0, 0, 0, 2), Small = true };
    readonly Label markerSame = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 0, 0) };
    readonly LinkLabel markerOwn = new() { AutoSize = true, Margin = new Padding(0, 6, 3, 0) };
    Control[] ownColourRow = Array.Empty<Control>();
    readonly List<Control> glowRows = new(), shineRows = new();
    /// <summary>The icon before the icon layer was removed: adding it again brings it back.</summary>
    string lastIcon = "";
    int arrangedWidth;

    // the layers above the item: the list (top = highest) and the picked layer
    readonly FlowLayoutPanel layerList = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
    readonly DashedButton addButton = new(Tr("+ Add on top")) { Margin = new Padding(0, 2, 0, 6) };
    readonly ContextMenuStrip addMenu = new();
    readonly Label startLabel = new() { AutoSize = true, Text = Tr("Start from"), ForeColor = Color.DimGray };
    readonly Label skyLabel = new() { AutoSize = false, Text = Tr("▲ higher up"), ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 0, 2) };
    readonly Label stackHint = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 0),
                                       Text = Tr("The item is always the bottom row - it can't move, but it can be Off or Invisible.\nDrag a row (or Alt+Up / Alt+Down) to move it, Delete removes it.") };
    string? listed;
    /// <summary>The picked row: a layer kind, or "" = the item.</summary>
    string picked = "";
    readonly Dictionary<string, Section> layerSections = new();
    Section itemSection = null!, layersSection = null!;

    /// <summary>The settings' width in the pop-up: the layer list and the picked row's settings side by side.</summary>
    public static int WantedWidth(Font f) => f.Height * 25; // one column: list on top, the picked row's settings under it (user: more room for the 3D view)

    static string KindName(string kind) => kind switch
    {
        "beam" => Tr("Beam"), "arrow" => Tr("Arrow"), "icon" => Tr("Icon"), "copy" => Tr("Item copy"), "text" => Tr("Text"), _ => Tr("The item"),
    };

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

        public string Title { set => title.Text = value; }

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
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (Clickable) Clicked?.Invoke(Index); base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Ui.Panel);
            // a pill ("Start from" chips, as in the look-stack mock-up)
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = new GraphicsPath();
            int d = r.Height;
            path.AddArc(r.Left, r.Top, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 180);
            path.CloseFigure();
            using (var bg = new SolidBrush(selected ? Ui.AccentSoft : hover ? Ui.Hover : Ui.Window)) g.FillPath(bg, path);
            using (var pen = new Pen(selected ? Ui.Accent : Ui.Line, 1f)) g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Text, Font, r, selected ? Ui.Accent : Ui.Fg,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        public int WantedWidth => TextRenderer.MeasureText(Text, Font).Width + Font.Height * 2;

    }

    /// <summary>"+ Add on top": a dashed full-width button over the layer list.</summary>
    sealed class DashedButton : Control
    {
        bool hover;
        public DashedButton(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(e); base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Ui.Window);
            using var pen = new Pen(hover || Focused ? Ui.Accent : Ui.Muted) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, hover || Focused ? Ui.Accent : Ui.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>A row of the "+ Add on top" drop-down: colour dot (two halves for the item copy), the layer's name in bold
    /// and what it is underneath, lit under the mouse.</summary>
    sealed class AddRow : Control
    {
        readonly string what, kind;
        bool hover;
        public AddRow(string name, string what, string kind)
        {
            Text = name; this.what = what; this.kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(hover ? Ui.Hover : Ui.Window);
            if (hover) using (var p = new Pen(Ui.Accent)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            int d = Font.Height + 8, x = 10;
            KindIcon(g, kind, new Rectangle(x, (Height - d) / 2, d, d), hover ? Ui.Accent : Ui.Fg);
            x += d + 12;
            using var bold = new Font(Font, FontStyle.Bold);
            int top = (Height - 2 * Font.Height - 2) / 2;
            TextRenderer.DrawText(g, Text, bold, new Point(x, top), Ui.Fg, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, what, Font, new Rectangle(x, top + Font.Height + 2, Width - x - 6, Font.Height + 1), Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>A layer kind's line picture: beam = a tall ray with light around it, arrow = pointing down onto a line,
    /// icon = a framed picture, text = "Aa", copy = a small shape over a big one.</summary>
    static void KindIcon(Graphics g, string kind, Rectangle r, Color c)
    {
        using var p = new Pen(c, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        PointF P(float fx, float fy) => new(x + fx * w, y + fy * h);
        switch (kind)
        {
            case "beam":
                g.DrawLine(p, P(0.5f, 0.05f), P(0.5f, 0.95f));
                using (var thin = new Pen(Color.FromArgb(120, c), 1.2f))
                {
                    g.DrawLine(thin, P(0.32f, 0.2f), P(0.32f, 0.95f));
                    g.DrawLine(thin, P(0.68f, 0.2f), P(0.68f, 0.95f));
                }
                g.DrawLine(p, P(0.15f, 0.95f), P(0.85f, 0.95f));
                break;
            case "arrow":
                g.DrawLine(p, P(0.5f, 0.05f), P(0.5f, 0.7f));
                g.DrawLines(p, new[] { P(0.25f, 0.45f), P(0.5f, 0.72f), P(0.75f, 0.45f) });
                g.DrawLine(p, P(0.15f, 0.95f), P(0.85f, 0.95f));
                break;
            case "icon":
                g.DrawRectangle(p, x + w * 0.08f, y + h * 0.15f, w * 0.84f, h * 0.7f);
                g.DrawEllipse(p, x + w * 0.24f, y + h * 0.28f, w * 0.16f, h * 0.16f);
                g.DrawLines(p, new[] { P(0.08f, 0.8f), P(0.38f, 0.52f), P(0.58f, 0.7f), P(0.72f, 0.58f), P(0.92f, 0.76f) });
                break;
            case "text":
                using (var f = new Font(FontFamily.GenericSansSerif, h * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "Aa", f, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                g.DrawLine(p, P(0.15f, 0.88f), P(0.85f, 0.88f));
                break;
            default: // item copy
                g.DrawRectangle(p, x + w * 0.1f, y + h * 0.45f, w * 0.5f, h * 0.48f);
                using (var dash = new Pen(c, 1.4f) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(dash, x + w * 0.5f, y + h * 0.08f, w * 0.38f, h * 0.36f);
                break;
        }
    }

    /// <summary>The ground under the item's row (hatched strip).</summary>
    sealed class Ground : Control
    {
        public Ground() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Height = 6; Margin = new Padding(2, 0, 2, 4); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Ui.Window);
            using var b = new HatchBrush(HatchStyle.WideDownwardDiagonal, Ui.Muted, Ui.Line);
            e.Graphics.FillRectangle(b, ClientRectangle);
        }
    }

    /// <summary>One row of the layer list: drag handle, colour dot, name + summary, ▲ ▼ ✕. The item's row at the bottom
    /// (Kind "") only shows where the stack starts.</summary>
    sealed class LayerRow : Control
    {
        public readonly string Kind;
        public string Summary = "";
        public Color Dot;
        public bool Picked, CanUp, CanDown;
        public event Action<string>? Clicked, Removed, Up, Down;
        public event Action<string, string>? Dropped;
        Point? press;
        int hoverButton = -1;

        public LayerRow(string kind)
        {
            Kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Margin = new Padding(0, 0, 0, kind.Length > 0 ? 4 : 2);
            AllowDrop = kind.Length > 0;
            Cursor = Cursors.Hand;
        }

        protected override void OnFontChanged(EventArgs e) { Height = Font.Height * 2 + 14; base.OnFontChanged(e); }

        int B => Height - 14; // button size
        Rectangle Button(int i) => new(Width - 6 - (3 - i) * (B + 2), (Height - B) / 2, B, B);
        int ButtonAt(Point p) => Kind.Length == 0 ? -1 : Enumerable.Range(0, 3).FirstOrDefault(i => Button(i).Contains(p), -1);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Ui.Window);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool item = Kind.Length == 0;
            using (var bg = new SolidBrush(Picked ? Ui.AccentSoft : item ? Ui.Panel : Ui.Window)) g.FillRectangle(bg, r);
            using (var pen = new Pen(Picked ? Ui.Accent : Ui.Line, Picked ? 2f : 1f)) g.DrawRectangle(pen, r);
            int x = 6;
            TextRenderer.DrawText(g, item ? "⊥" : "⠿", Font, new Rectangle(x, 0, 14, Height), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            x += 18;
            int d = Font.Height - 2;
            var dot = new Rectangle(x, (Height - d) / 2, d, d);
            if (Kind == "copy")
            {
                using var a = new SolidBrush(Color.FromArgb(160, 120, 80)); using var b = new SolidBrush(Color.FromArgb(90, 122, 60));
                g.FillPie(a, dot, 90, 180); g.FillPie(b, dot, 270, 180);
            }
            else using (var b = new SolidBrush(Dot)) g.FillEllipse(b, dot);
            x += d + 8;
            int textW = (item ? Width - 6 : Button(0).Left - 4) - x;
            using var bold = new Font(Font, FontStyle.Bold);
            TextRenderer.DrawText(g, KindName(Kind), bold, new Rectangle(x, 6, textW, Font.Height + 1), Ui.Fg, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Summary, Font, new Rectangle(x, 6 + Font.Height + 1, textW, Font.Height + 1), Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (item) return;
            string[] marks = { "▲", "▼", "✕" };
            bool[] on = { CanUp, CanDown, true };
            for (int i = 0; i < 3; i++)
            {
                var br = Button(i);
                if (hoverButton == i && on[i]) using (var hb = new SolidBrush(Ui.Hover)) g.FillRectangle(hb, br);
                TextRenderer.DrawText(g, marks[i], Font, br, on[i] ? Ui.Fg : Ui.Line, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(3, 3, Width - 7, Height - 7));
            if (dragging == this) using (var veil = new SolidBrush(Color.FromArgb(150, Parent?.BackColor ?? Ui.Window))) g.FillRectangle(veil, r);
            if (dropEdge != 0)
            {
                // a thick bar with an arrow head at the left, on the edge where the layer will land
                int y = dropEdge < 0 ? 0 : Height - 4;
                using var bar = new SolidBrush(Ui.Accent);
                g.FillRectangle(bar, 0, y, Width, 4);
                int cy = y + 2;
                g.FillPolygon(bar, new[] { new Point(0, cy - 6), new Point(8, cy), new Point(0, cy + 6) });
            }
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && ButtonAt(e.Location) < 0) press = e.Location; base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { press = null; base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverButton = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hb = ButtonAt(e.Location);
            if (hb != hoverButton) { hoverButton = hb; Invalidate(); }
            if (press is Point p && Kind.Length > 0 && (Math.Abs(e.X - p.X) > 4 || Math.Abs(e.Y - p.Y) > 4))
            {
                press = null;
                dragging = this;
                Invalidate();
                try { DoDragDrop(Kind, DragDropEffects.Move); }
                finally { dragging = null; if (!IsDisposed) Invalidate(); }
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus();
            switch (ButtonAt(e.Location))
            {
                case 0: if (CanUp) Up?.Invoke(Kind); break;
                case 1: if (CanDown) Down?.Invoke(Kind); break;
                case 2: Removed?.Invoke(Kind); break;
                default: Clicked?.Invoke(Kind); break;
            }
            base.OnMouseClick(e);
        }
        protected override void OnDragOver(DragEventArgs e)
        {
            bool ok = e.Data?.GetData(typeof(string)) is string k && k != Kind;
            e.Effect = ok ? DragDropEffects.Move : DragDropEffects.None;
            // the dragged layer takes this row's place: above it when it comes from below, else below it (user, 2026-11-15:
            // no sign where it would land)
            int edge = ok && dragging != null ? (dragging.Top > Top ? -1 : 1) : 0;
            if (edge != dropEdge) { dropEdge = edge; Invalidate(); }
            base.OnDragOver(e);
        }
        protected override void OnDragLeave(EventArgs e) { if (dropEdge != 0) { dropEdge = 0; Invalidate(); } base.OnDragLeave(e); }
        protected override void OnDragDrop(DragEventArgs e) { dropEdge = 0; Invalidate(); if (e.Data?.GetData(typeof(string)) is string k && k != Kind) Dropped?.Invoke(k, Kind); base.OnDragDrop(e); }
        /// <summary>The row being dragged (dimmed), and where a drop on this row would put it: -1 above, 1 below.</summary>
        static LayerRow? dragging;
        int dropEdge;
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) => k is (Keys.Alt | Keys.Up) or (Keys.Alt | Keys.Down) or Keys.Delete || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) { Clicked?.Invoke(Kind); e.Handled = true; }
            else if (Kind.Length > 0)
            {
                if (e.Alt && e.KeyCode == Keys.Up && CanUp) { Up?.Invoke(Kind); e.Handled = true; }
                else if (e.Alt && e.KeyCode == Keys.Down && CanDown) { Down?.Invoke(Kind); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { Removed?.Invoke(Kind); e.Handled = true; }
            }
            base.OnKeyDown(e);
        }
    }

    /// <summary>A layer's colour: the item's (glow) colour, or its own ("" = the item's).</summary>
    sealed class OwnColour
    {
        public readonly Label Same = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 0, 0) };
        public readonly LinkLabel Own = new() { AutoSize = true, Margin = new Padding(0, 6, 3, 0) };
        public readonly SwatchRow Swatches = new() { Margin = new Padding(0, 0, 0, 2), Small = true };
        public Control[] SwatchRowCtl = Array.Empty<Control>();
        public readonly Func<Style, string?> Get;
        public readonly Action<Style, string> Set;
        public readonly Func<Style, string> Resolved;
        public OwnColour(Func<Style, string?> get, Action<Style, string> set, Func<Style, string> resolved) { Get = get; Set = set; Resolved = resolved; }
    }
    readonly List<OwnColour> ownColours = new();

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
        /// <summary>The Hide after row's buttons + seconds box (disabled on creatures).</summary>
        public FlowLayoutPanel? HideFlow;
        public Chip[] Boxes => new[] { Walls, Pulse, Rainbow, Spin, Bob, Flow };
        public FxRows(Func<Style, PartFx> of) => Of = of;
    }
    readonly List<FxRows> fxRows = new();
    FxRows glowFx = null!, beamFx = null!, arrowFx = null!, iconFx = null!, copyFx = null!, labelFx = null!;
    readonly CheckBox invisibleBox = new() { Text = Tr("Invisible - only what you add shows"), AutoSize = true };
    /// <summary>"Pick up from afar": the pick-up sphere's radius in metres (Style.PickRange), 0 = off.</summary>
    readonly NumericUpDown pickRange = new() { Minimum = 0, Maximum = 15, Increment = 1, Width = 48 };
    Control[] pickRow = Array.Empty<Control>();
    static readonly string PickWhy = Tr("Pick it up from further away: an invisible ball round the item (shown in the 3D view) that the game's aim hits - you walk straight through it.\nYou reach about 2 m past the ball's edge. The item stays where it is placed (it doesn't fall or get knocked about).\nInside the ball it may be picked instead of a door or box next to it - keep it small near those.");

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
        var tips = fxTips;
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
            x.HideFlow = (FlowLayoutPanel)hideRow[1];
            x.Rows.AddRange(hideRow);
            foreach (var b in x.Boxes)
                b.CheckedChanged += (_, _) =>
                {
                    if (loading) return;
                    // item look Off: the chip is its own X-ray (Style.OwnXray), GlowFx.Walls stays as it is
                    if (x == glowFx && !style.Glow && !style.Brighten) { style.OwnXray = x.Walls.Checked; Edited(); return; }
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
        // a layer's own colour: "Same as the item · use a different colour", the dots when it has its own
        void Colour(Section s, Func<Style, string?> get, Action<Style, string> set, Func<Style, string> resolved)
        {
            var oc = new OwnColour(get, set, resolved);
            Row(s, Tr("Colour"), oc.Same, oc.Own);
            oc.SwatchRowCtl = Row(s, "", oc.Swatches);
            oc.Own.LinkClicked += (_, _) =>
            {
                set(style, string.IsNullOrEmpty(get(style)) ? resolved(style) : "");
                UpdateLook();
                Changed?.Invoke(this, EventArgs.Empty);
            };
            oc.Swatches.Picked += c =>
            {
                if (c is not Color colour)
                {
                    using var dlg = new ColorDialog { FullOpen = true, Color = Materials.Parse(resolved(style)) };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    colour = dlg.Color;
                }
                set(style, Materials.Hex(colour));
                UpdateLook();
                Changed?.Invoke(this, EventArgs.Empty);
            };
            ownColours.Add(oc);
        }
        string[] strength = { Tr("Soft"), Tr("Medium"), Tr("Strong") };
        string[] thickness = { Tr("Thin"), Tr("Normal"), Tr("Thick") };
        const string timerNote = "\nA hide timer starts when the game loads the item, so it starts again when you come back to the area.";

        // 1 the item: a glow (outline / fill), or its own colours lit (bright); invisible
        itemSection = new Section(Tr("The item"), Tr("Outline / Fill: a coloured light on the item. Bright: its own colours light up, day and night (not through walls).\nInvisible: the item disappears - only what you add shows; still there to pick up. Worn outfits vanish from whoever wears them, you too. Not on enemies.") + timerNote);
        Wide(itemSection, itemSeg);
        itemSeg.Picked += i =>
        {
            if (loading) return;
            style.Glow = i < 2; style.GlowFill = i == 1; style.Brighten = i == 2;
            Commit();
        };
        swatches.Small = true;
        glowRows.AddRange(Row(itemSection, Tr("Colour"), swatches));
        glowRows.AddRange(Row(itemSection, Tr("Strength"), Steps(opacity, strength, new[] { 15, 30, 60 }), opacity, Unit("%")));
        shineRows.AddRange(Row(itemSection, Tr("Shine"), Steps(brightStrength, strength, new[] { 50, 100, 200 }), brightStrength, Unit("%")));
        invisibleBox.CheckedChanged += (_, _) => { if (loading) return; style.Invisible = invisibleBox.Checked; UpdateLook(); Changed?.Invoke(this, EventArgs.Empty); };
        glowFx = Fx(itemSection, st => st.GlowFx);
        xrayRows.AddRange(Row(itemSection, Tr("Strength"), Steps(xrayStrength, strength, new[] { 50, 100, 200 }), xrayStrength, Unit("%")));
        xrayStrength.ValueChanged += (_, _) => { if (loading) return; style.XrayStrength = (int)xrayStrength.Value; Edited(); };
        pickRow = Row(itemSection, Tr("Pickup range"), Steps(pickRange, new[] { Tr("Off"), "2 m", "4 m", "6 m" }, new[] { 0, 2, 4, 6 }), pickRange, Unit("m"));
        foreach (var c in pickRow) fxTips.SetToolTip(c, PickWhy);
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

        // the layers above the item, top of the list = highest; the item is the bottom row
        layersSection = new Section(Tr("Layers"), Tr("Add a beam, an arrow, an icon, a copy of the item or a line of text - any of them, in any order.\nThe top of the list is the highest above the item: each layer sits on the one under it.\nDrag a row (or ▲ ▼, Alt+Up / Alt+Down) to move it; ✕ or Delete removes it. Click a row for its settings."));
        addButton.Height = Font.Height + 14;
        skyLabel.Height = Font.Height + 2;
        Wide(layersSection, addButton);
        Wide(layersSection, skyLabel);
        Wide(layersSection, layerList);
        Wide(layersSection, stackHint);
        addButton.Click += (_, _) =>
        {
            // as wide as the button, roomy rows: the layer's colour dot, its name and what it is (user, 2026-11-15: the
            // plain menu looked very small)
            // a drop-down as wide as the button with rows like the layer list's: colour dot, name, what it is underneath
            var on = style.Stack;
            var kinds = Style.Kinds.Where(k => !on.Contains(k)).ToList();
            var panel = new Panel { BackColor = Ui.Window, Width = addButton.Width, Padding = new Padding(4) };
            int y = 4;
            var drop = new ToolStripDropDown { Padding = Padding.Empty, BackColor = Ui.Line };
            foreach (var k in kinds)
            {
                string kind = k;
                var row = new AddRow(KindName(kind), kind switch
                {
                    "beam" => Tr("A column of light above it"), "arrow" => Tr("Points down at it"), "icon" => Tr("A picture or 3D model"),
                    "text" => Tr("Its name or your own words"), "copy" => Tr("A copy of the item's own shape"), _ => "",
                }, kind) { Font = Font, Bounds = new Rectangle(4, y, panel.Width - 8, Font.Height * 2 + 14) };
                row.Click += (_, _) => { drop.Close(); AddLayer(kind); };
                panel.Controls.Add(row);
                y += row.Height + 2;
            }
            if (kinds.Count == 0)
            {
                panel.Controls.Add(new Label { Text = Tr("Everything is added"), ForeColor = Ui.Muted, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                                               Bounds = new Rectangle(4, y, panel.Width - 8, Font.Height + 12) });
                y += Font.Height + 14;
            }
            panel.Height = y + 2;
            drop.Items.Add(new ToolStripControlHost(panel) { Padding = Padding.Empty, Margin = new Padding(1), AutoSize = false, Size = panel.Size });
            drop.Closed += (_, _) => BeginInvoke(() => drop.Dispose());
            drop.Show(addButton, new Point(0, addButton.Height));
        };
        // the picked layer's settings
        var beamSection = layerSections["beam"] = new Section(Tr("Beam"), Tr("A light pillar into the sky, so you see where the item is from far away.") + timerNote);
        Row(beamSection, Tr("Height"), Steps(length, new[] { Tr("Short"), Tr("Medium"), Tr("Tall") }, new[] { 10, 20, 30 }), length, Unit("m"));
        Row(beamSection, Tr("Thickness"), Steps(width, thickness, new[] { 6, 10, 20 }), width, Unit("cm"));
        Row(beamSection, Tr("Strength"), Steps(beamStrength, strength, new[] { 15, 30, 60 }), beamStrength, Unit("%"));
        Row(beamSection, Tr("Colour"), markerSame, markerOwn);
        ownColourRow = Row(beamSection, "", markerSwatches);
        markerOwn.LinkClicked += (_, _) => { sameColor.Checked = !sameColor.Checked; Commit(); };
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

        var arrowSection = layerSections["arrow"] = new Section(Tr("Arrow"), Tr("A solid arrow pointing down at what is under it.") + timerNote);
        Row(arrowSection, Tr("Thickness"), Steps(arrowWidth, thickness, new[] { 6, 10, 20 }), arrowWidth, Unit("cm"));
        Colour(arrowSection, st => st.ArrowColor, (st, c) => st.ArrowColor = c, st => st.ArrowColour);
        arrowFx = Fx(arrowSection, st => st.ArrowFx);

        var iconSection = layerSections["icon"] = new Section(Tr("Icon"), Tr("A picture or 3D model floating above the item, turning to face you.\nSpin and bob need a 3D icon or model on enemies; spin shows on 3D only.") + timerNote);
        Row(iconSection, Tr("Picture"), iconButton);
        Row(iconSection, Tr("Size"), Steps(iconSize, new[] { "S", "M", "L" }, new[] { 50, 100, 200 }), iconSize, Unit(Tr("% of the item")));
        Row(iconSection, Tr("Strength"), Steps(iconStrength, strength, new[] { 50, 100, 200 }), iconStrength, Unit("%"));
        Colour(iconSection, st => st.IconColor, (st, c) => st.IconColor = c, st => st.IconColour);
        iconFx = Fx(iconSection, st => st.IconFx);

        var copySection = layerSections["copy"] = new Section(Tr("Item copy"), Tr("Item copy: a copy of the item itself, in its own colours.") + timerNote);
        Row(copySection, Tr("Size"), Steps(copySize, new[] { "S", "M", "L" }, new[] { 50, 100, 200 }), copySize, Unit(Tr("% of the item")));
        Row(copySection, Tr("Strength"), Steps(copyStrength, strength, new[] { 50, 100, 200 }), copyStrength, Unit("%"));
        copyFx = Fx(copySection, st => st.CopyFx);

        var textSection = layerSections["text"] = new Section(Tr("Text"), Tr("A line of text (the item's name or your own) floating above the item and turning to face you.\nOn creatures it can't turn: it faces the way the creature faces.") + timerNote);
        labelText.Width = 160;
        Row(textSection, Tr("Text"), labelText);
        tips.SetToolTip(labelText, Tr("Leave empty to show each item's own name.\nText typed here shows on every item using this look (for one item only, give that item its own look)."));
        labelText.TextChanged += (_, _) =>
        {
            if (loading || style.LabelText == labelText.Text) return;
            style.LabelText = labelText.Text;
            RefreshRows();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        Row(textSection, Tr("Height"), Steps(labelSize, new[] { "S", "M", "L" }, new[] { 100, 150, 250 }), labelSize, Unit("cm"));
        Row(textSection, "", labelCat);
        labelCat.CheckedChanged += (_, _) => { if (loading) return; style.LabelCategory = labelCat.Checked; Edited(); };
        Row(textSection, Tr("Strength"), Steps(labelStrength, strength, new[] { 50, 100, 200 }), labelStrength, Unit("%"));
        Colour(textSection, st => st.LabelColor, (st, c) => st.LabelColor = c, st => st.LabelColour);
        labelFx = Fx(textSection, st => st.LabelFx);

        // presets: switch the parts on / off; colours and sizes stay
        string[] names = { Tr("Glow only"), Tr("Glow + beam"), Tr("Glow + beam + name") };
        for (int i = 0; i < names.Length; i++)
        {
            var t = new Tile(i, names[i]);
            tips.SetToolTip(t, Tr("Switches the parts on or off - your colours and sizes stay."));
            t.Clicked += ApplyPreset;
            presetTiles.Add(t);
            board.Controls.Add(t);
        }
        board.Controls.Add(startLabel);
        foreach (var s in new[] { itemSection, layersSection }.Concat(layerSections.Values)) board.Controls.Add(s);
        Controls.Add(board);
        board.BringToFront(); // docked last: fills what the header leaves
    }

    void Edit(Action change)
    {
        change();
        UpdateLook();
        Changed?.Invoke(this, EventArgs.Empty);
        FocusPicked();
    }

    /// <summary>A layer on top of the stack; the icon brings back the last one, or opens the picker.</summary>
    void AddLayer(string kind)
    {
        if (kind == "icon" && style.Icon.Length == 0)
        {
            if (lastIcon.Length > 0) style.Icon = lastIcon;
            else if (!ChooseIcon() || style.Icon.Length == 0) return;
        }
        picked = kind;
        Edit(() => style.Set(kind, true));
    }

    void RemoveLayer(string kind)
    {
        var order = style.Stack.ToList();
        int i = order.IndexOf(kind);
        Edit(() =>
        {
            if (kind == "icon") { lastIcon = style.Icon; style.Icon = ""; }
            style.Set(kind, false);
            var left = style.Stack;
            picked = left.Length == 0 ? "" : left[Math.Clamp(i, 0, left.Length - 1)];
        });
    }

    /// <param name="by">+1 = one higher (up the list), -1 = one lower.</param>
    void MoveLayer(string kind, int by)
    {
        var order = style.Stack.ToList();
        int i = order.IndexOf(kind), j = i + by;
        if (i < 0 || j < 0 || j >= order.Count) return;
        (order[i], order[j]) = (order[j], order[i]);
        picked = kind;
        Edit(() => style.Layers = order);
    }

    /// <summary>Dragged onto another row: takes that row's place.</summary>
    void DropLayer(string kind, string onto)
    {
        var order = style.Stack.ToList();
        int to = order.IndexOf(onto);
        if (to < 0 || !order.Remove(kind)) return;
        order.Insert(Math.Clamp(to, 0, order.Count), kind);
        picked = kind;
        Edit(() => style.Layers = order);
    }

    void FocusPicked()
    {
        if (layerList.Controls.OfType<LayerRow>().FirstOrDefault(r => r.Kind == picked) is { } row) row.Focus();
    }

    string Summary(string kind) => kind switch
    {
        "beam" => Tf("{0} m", length.Value) + (sameColor.Checked ? "" : Tr(" · own colour")) + FxSummary(style.BeamFx),
        "arrow" => Tf("{0} cm", arrowWidth.Value) + FxSummary(style.ArrowFx),
        "icon" => iconButton.Text + FxSummary(style.IconFx),
        "copy" => Tf("{0} % of the item", copySize.Value) + FxSummary(style.CopyFx),
        "text" => LabelSummary(),
        _ => style.Glow ? (style.GlowFill ? Tr("Fill") : Tr("Outline")) : style.Brighten ? Tr("Bright") : Tr("Off"),
    };

    Color DotOf(string kind) => Materials.Parse(kind switch
    {
        "beam" => style.BeamColor, "arrow" => style.ArrowColour, "icon" => style.IconColour, "text" => style.LabelColour, _ => style.GlowColor,
    });

    /// <summary>The layer list as the look has it (rebuilt when the layers change, else just repainted).</summary>
    void RefreshRows()
    {
        var stack = style.Stack;
        string key = string.Join(",", stack);
        if (key != listed)
        {
            listed = key;
            layerList.SuspendLayout();
            foreach (Control c in layerList.Controls.Cast<Control>().ToList()) c.Dispose();
            layerList.Controls.Clear();
            foreach (var k in stack.Reverse().Append(""))
            {
                var row = new LayerRow(k) { Font = Font };
                row.Clicked += kind => { picked = kind; RefreshCards(); };
                row.Removed += RemoveLayer;
                row.Up += kind => MoveLayer(kind, +1);
                row.Down += kind => MoveLayer(kind, -1);
                row.Dropped += DropLayer;
                layerList.Controls.Add(row);
            }
            layerList.Controls.Add(new Ground());
            layerList.ResumeLayout();
        }
        foreach (var row in layerList.Controls.OfType<LayerRow>())
        {
            int i = Array.IndexOf(stack, row.Kind);
            row.Picked = row.Kind == picked;
            row.CanUp = i >= 0 && i < stack.Length - 1;
            row.CanDown = i > 0;
            row.Dot = row.Kind.Length == 0 && !style.Glow ? Ui.Line : DotOf(row.Kind);
            row.Summary = Summary(row.Kind);
            row.Invalidate();
        }
    }

    /// <summary>The preset the look matches: 0 glow only, 1 glow + beam, 2 glow + beam + name, 3 none (custom).</summary>
    int PresetOf() => !style.Glow ? 3 : string.Join(",", style.Stack) switch { "" => 0, "beam" => 1, "beam,text" => 2, _ => 3 };

    void ApplyPreset(int p)
    {
        var st = style.Clone();
        if (!st.Glow) { st.Glow = true; st.Brighten = false; }
        if (st.Icon.Length > 0) lastIcon = st.Icon;
        st.Icon = "";
        st.Arrow = st.Copy = false;
        st.Beam = p >= 1;
        st.Label = p == 2;
        st.Layers = p == 0 ? new() : p == 1 ? new() { "beam" } : new() { "beam", "text" };
        picked = "";
        Value = st;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Dev (UI test): picks a preset like a click.</summary>
    internal void TestPreset(int p) => ApplyPreset(p);

    string LabelSummary() => (style.LabelCategory ? Tr("Category · ") : "") + (style.LabelText.Trim().Length > 0 ? $"\"{style.LabelText.Trim()}\"" : Tr("Item's name")) + Tf(" · {0} cm", labelSize.Value) + FxSummary(style.LabelFx);

    static string Time(int s) => s % 60 == 0 ? Tf("{0} min", s / 60) : $"{s} s";

    /// <summary>" · pulse · hides 30 s" - a section's extras for its row's summary line.</summary>
    static string FxSummary(PartFx x, bool flow = true)
    {
        var w = new List<string>();
        if (x.Walls) w.Add(Tr("X-ray"));
        if (x.Pulse) w.Add(Tr("Pulse"));
        if (x.Rainbow) w.Add(Tr("Rainbow"));
        if (x.Spin) w.Add(Tr("Spin"));
        if (x.Bob) w.Add(Tr("Bob"));
        if (x.Flow && flow) w.Add(Tr("Light runs up"));
        if (x.HideAfter > 0) w.Add(Tf("hides after {0}", Time(x.HideAfter)));
        return w.Count > 0 ? " · " + string.Join(" · ", w) : "";
    }

    /// <summary>The section of the picked layer (the empty one without layers).</summary>
    Section PickedSection => layerSections.TryGetValue(picked, out var s) ? s : itemSection;

    /// <summary>The sections show the look: lit choices, the rows that fit, the layer list, the matching preset.</summary>
    void RefreshCards()
    {
        int item = style.Glow ? (style.GlowFill ? 1 : 0) : style.Brighten ? 2 : 3;
        itemSeg.Selected = item;
        var stack = style.Stack;
        if (picked.Length > 0 && !stack.Contains(picked)) picked = stack.LastOrDefault() ?? "";
        layersSection.Title = Tr("Layers") + "   " + Tf("{0} of {1}", stack.Length, Style.Kinds.Length);
        if (layerSections.TryGetValue(picked, out var ps)) ps.Title = KindName(picked) + "   ·   " + Tf("layer {0} from the item", Array.IndexOf(stack, picked) + 1);
        foreach (var c in glowRows) c.Visible = item < 2;
        foreach (var c in shineRows) c.Visible = item == 2;
        bool own = !sameColor.Checked;
        markerSame.Text = own ? Tr("Its own colour ·") : Tr("Same as the item ·");
        markerOwn.Text = own ? Tr("same as the item") : Tr("use a different colour");
        foreach (var c in ownColourRow) c.Visible = own;
        markerSwatches.Current = Materials.Parse(style.BeamColor);
        markerSwatches.Invalidate();
        foreach (var oc in ownColours)
        {
            bool mine = !string.IsNullOrEmpty(oc.Get(style));
            oc.Same.Text = mine ? Tr("Its own colour ·") : Tr("Same as the item ·");
            oc.Own.Text = mine ? Tr("same as the item") : Tr("use a different colour");
            foreach (var c in oc.SwatchRowCtl) c.Visible = mine;
            oc.Swatches.Current = Materials.Parse(oc.Resolved(style));
            oc.Swatches.Invalidate();
        }
        bool was = loading;
        loading = true; // setting the switches must not write back
        foreach (var x in fxRows)
        {
            var fx = x.Of(style);
            x.Walls.Checked = x == glowFx && item == 3 ? style.OwnXray : fx.Walls; x.Pulse.Checked = fx.Pulse; x.Rainbow.Checked = fx.Rainbow;
            x.Spin.Checked = fx.Spin; x.Bob.Checked = fx.Bob; x.Flow.Checked = fx.Flow;
            x.Hide.Value = Math.Clamp(fx.HideAfter, (int)x.Hide.Minimum, (int)x.Hide.Maximum);
            foreach (var c in x.Rows) c.Visible = true;
            // the motions that fit: flow on a beam, bob on an arrow / icon / copy / text, spin on an icon / copy
            x.Spin.Visible = x == iconFx || x == copyFx;
            x.Bob.Visible = x != glowFx && x != beamFx;
            // creatures / worn outfits (animated models): the game can't move or time added parts there (in game,
            // 2026-11-15) - greyed, the tooltip says why (user: not hidden)
            x.Spin.Locked = x.Bob.Locked = icons3D;
            fxTips.SetToolTip(x.Spin, icons3D ? AnimatedWhy : Tr("Turns round (3D icon or model only)."));
            fxTips.SetToolTip(x.Bob, icons3D ? AnimatedWhy : Tr("Floats up and down."));
            if (x.HideFlow != null)
            {
                foreach (Control k in x.HideFlow.Controls) k.Enabled = !icons3D;
                fxTips.SetToolTip(x.HideFlow, icons3D ? AnimatedWhy : "");
                if (x.Rows.FirstOrDefault(r => r is Label l && l.Text == Tr("Hide after")) is { } lab) fxTips.SetToolTip(lab, icons3D ? AnimatedWhy : "");
            }
            x.Flow.Visible = x == beamFx;
        }
        // Bright / Off: only X-ray (a copy in its own colours, Builder.XrayFor); pulse / rainbow / timer not for it yet
        glowFx.Pulse.Visible = glowFx.Rainbow.Visible = item < 2;
        if (item >= 2) foreach (var c in glowFx.Rows.Skip(2)) c.Visible = false;
        foreach (var c in xrayRows) c.Visible = item == 3 && style.OwnXray && !style.Invisible;
        xrayStrength.Value = Math.Clamp(style.XrayStrength, 25, 300);
        foreach (var (seg, box, _, values) in steps) seg.Selected = Array.IndexOf(values, (int)box.Value);
        invisibleBox.Checked = style.Invisible;
        // creatures / worn outfits: no pick-up ball (greyed with the reason, like the motions)
        foreach (var c in pickRow)
        {
            c.Enabled = pickOk;
            fxTips.SetToolTip(c, pickOk ? PickWhy : Tr("Only on things you pick up, harvest or open where they lie - not on enemies, people, CAMP items, containers, doors or furniture (they would lose their solid shape)."));
        }
        labelCat.Checked = style.LabelCategory;
        int p = PresetOf(); // the preset the look matches is lit (none when it matches none)
        foreach (var t in presetTiles) t.Selected = t.Index == p;
        loading = was;
        RefreshRows();
        ShowMore();
    }

    /// <summary>The look pop-up's layout (the only one since the look stack).</summary>
    public bool Compact
    {
        get => compact;
        set
        {
            compact = value;
            if (presetTiles.Count == 0) { BuildCards(); UpdateLook(); }
            ShowMore();
        }
    }

    /// <summary>Height of the quick header (dev: the tour's pictures).</summary>
    internal int HeaderHeight => Height;

    /// <summary>Dev (the tour's pictures): the "Start from" presets and the Layers box.</summary>
    internal IEnumerable<Control> PresetControls => presetTiles;
    internal Control LayersBox => layersSection;
    internal void DevPreset(int i) => ApplyPreset(i);

    /// <summary>The sections' rows changed (a part switched on / off): the height changed.</summary>
    public event EventHandler? HeightChanged;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width != arrangedWidth) ShowMore(); // wider / narrower: the sections are laid out again
    }

    /// <summary>Lays out "Start from" chips on top, then the layer list | the picked row's settings side by side when there
    /// is room (as in the look-stack mock-up), else one under the other.</summary>
    void ShowMore()
    {
        if (presetTiles.Count > 0)
        {
            int w = Math.Max(200, Width), gap = 8;
            arrangedWidth = Width;
            bool wide = w >= Font.Height * 30;
            var settings = PickedSection;
            foreach (var s in layerSections.Values.Append(itemSection)) s.Visible = s == settings;
            var shown = new[] { layersSection, settings };
            int listW = wide ? Math.Min(Font.Height * 20, (w - gap) * 45 / 100) : w, setW = wide ? w - listW - gap : w;
            // the chips: "Start from  (Glow only) (Glow + beam) (Glow + beam + name)", wrapping when narrow
            int chipH = Font.Height + 12, x = startLabel.Width + 10, y = 0;
            startLabel.Location = new Point(0, (chipH - startLabel.Height) / 2);
            foreach (var t in presetTiles)
            {
                int tw = t.WantedWidth;
                if (x + tw > w && x > startLabel.Width + 10) { x = startLabel.Width + 10; y += chipH + 4; }
                t.Bounds = new Rectangle(x, y, tw, chipH);
                x += tw + 6;
            }
            y += chipH + gap;
            foreach (var f in fxFlows) f.MaximumSize = new Size(Math.Max(120, setW - 110), 0);
            int rowW = Math.Max(160, listW - 18);
            addButton.Width = skyLabel.Width = rowW;
            stackHint.MaximumSize = new Size(rowW, 0);
            foreach (Control row in layerList.Controls)
                row.Size = row is Ground ? new Size(rowW - 4, 6) : new Size(rowW, Font.Height * 2 + 14);
            if (wide)
            {
                int h = Math.Max(layersSection.Wanted(listW), settings.Wanted(setW));
                layersSection.Bounds = new Rectangle(0, y, listW, h);
                settings.Bounds = new Rectangle(listW + gap, y, setW, h);
                y += h;
            }
            else
                foreach (var (s, sw) in new[] { (layersSection, listW), (settings, setW) })
                {
                    int h = s.Wanted(sw);
                    s.Bounds = new Rectangle(0, y, sw, h);
                    y += h + gap;
                }
            if (Height != y + 4) Height = y + 4;
        }
        HeightChanged?.Invoke(this, EventArgs.Empty);
    }
    Style style = new();
    bool loading;

    public event EventHandler? Changed;

    /// <summary>Grows to fit every row (larger Windows text scaling makes the rows taller than the default height).</summary>
    /// <returns>How much taller it got.</returns>
    public int FitHeight() { int before = Height; ShowMore(); return Math.Max(0, Height - before); }

    /// <summary>The cards' tooltips (the effects' change on creatures).</summary>
    readonly ToolTip fxTips = new() { AutoPopDelay = 20000 };
    static readonly string AnimatedWhy = Tr("Not on creatures or worn outfits: the game animates them itself and won't move or time anything added to them.");

    bool icons3D;
    /// <summary>Creatures: the icon is built as a 3D shape (a flat camera-facing one is dropped by animated models),
    /// so the picker and preview show the 3D versions.</summary>
    public bool Icons3D
    {
        get => icons3D;
        set { icons3D = value; UpdateLook(); }
    }

    /// <summary>The look is for things you pick up (Entry.CanPick): the pick-up ball row is usable; greyed otherwise.</summary>
    public bool PickOk
    {
        get => pickOk;
        set { pickOk = value; UpdateLook(); }
    }
    bool pickOk = true;

    /// <summary>Left from the old small sketch; the pop-up shows the real 3D preview.</summary>
    public bool Sketch { get => false; set { } }

    public StyleEditor()
    {
        Height = 400;
        foreach (var box in new[] { opacity, brightStrength, length, width, beamStrength, arrowWidth, iconStrength, labelStrength, copyStrength, iconSize, copySize, labelSize, pickRange })
            box.ValueChanged += (_, _) => Commit();
        iconButton.FlatAppearance.BorderColor = Color.FromArgb(160, 160, 160);
        iconButton.Click += (_, _) => PickIcon();
    }

    public Style Value
    {
        get => style.Clone();
        set
        {
            style = value.Clone();
            loading = true;
            brightStrength.Value = Math.Clamp(style.BrightenStrength, 25, 300);
            opacity.Value = Math.Clamp(style.GlowOpacity, 5, 100);
            length.Value = Math.Clamp((decimal)Math.Round(style.BeamLength / UnitsPerMetre), 1, 150);
            width.Value = Math.Clamp((decimal)Math.Round(style.BeamWidth * 2 / UnitsPerCm), width.Minimum, width.Maximum);
            arrowWidth.Value = Math.Clamp((decimal)Math.Round(style.ArrowWidth * 2 / UnitsPerCm), arrowWidth.Minimum, arrowWidth.Maximum);
            beamStrength.Value = Math.Clamp(style.BeamStrength, 1, 300);
            iconStrength.Value = Math.Clamp(style.IconStrength, 10, 300);
            labelStrength.Value = Math.Clamp(style.LabelStrength, 10, 300);
            copyStrength.Value = Math.Clamp(style.CopyStrength, 10, 300);
            iconSize.Value = Math.Clamp(style.IconScale, (int)iconSize.Minimum, (int)iconSize.Maximum);
            copySize.Value = Math.Clamp(style.CopyScale, (int)copySize.Minimum, (int)copySize.Maximum);
            pickRange.Value = Math.Clamp(style.PickRange, 0, (int)pickRange.Maximum);
            if (labelText.Text != style.LabelText) labelText.Text = style.LabelText;
            labelSize.Value = Math.Clamp((decimal)Math.Round(style.LabelSize * 2 / UnitsPerCm), labelSize.Minimum, labelSize.Maximum);
            sameColor.Checked = style.GlowColor.Equals(style.BeamColor, StringComparison.OrdinalIgnoreCase);
            loading = false;
            UpdateLook();
        }
    }

    /// <summary>The icon picker; false when cancelled.</summary>
    bool ChooseIcon()
    {
        var c = Materials.Parse(style.IconColour);
        if (IconPicker.Pick(this, style.Icon, icons3D || style.Icon3D, c, icons3D) is not var (id, threeD)) return false;
        style.Icon = id;
        if (!icons3D) style.Icon3D = threeD && id.Length > 0;
        return true;
    }

    void PickIcon()
    {
        if (!ChooseIcon()) return;
        style.Layers = style.Stack.ToList(); // "no icon" takes it off the stack
        UpdateLook();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void Commit()
    {
        if (loading) return;
        style.BrightenStrength = (int)brightStrength.Value;
        style.GlowOpacity = (int)opacity.Value;
        style.BeamLength = (int)Math.Round((float)length.Value * UnitsPerMetre);
        style.BeamWidth = (float)width.Value * UnitsPerCm / 2; // diameter in cm -> radius in game units
        style.ArrowWidth = (float)arrowWidth.Value * UnitsPerCm / 2;
        style.BeamStrength = (int)beamStrength.Value;
        style.IconStrength = (int)iconStrength.Value;
        style.LabelStrength = (int)labelStrength.Value;
        style.CopyStrength = (int)copyStrength.Value;
        style.IconScale = (int)iconSize.Value; // % of the item's size
        style.CopyScale = (int)copySize.Value;
        style.PickRange = (int)pickRange.Value;
        style.LabelSize = (float)labelSize.Value * UnitsPerCm / 2; // text height in cm -> half height in game units
        if (sameColor.Checked) style.BeamColor = style.GlowColor;
        UpdateLook();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void UpdateLook()
    {
        bool show3D = icons3D || style.Icon3D;
        var ic = Icons.Get(style.Icon);
        var model = Models3D.Get(style.Icon);
        iconButton.Text = model != null ? Tr(model.Name) + Tr(" (model)") : ic == null ? Tr("No icon") : show3D ? Tr(ic.Name) + " (3D)" : Tr(ic.Name);
        iconButton.Image?.Dispose();
        var ccol = Materials.Parse(style.IconColour);
        int tile = Math.Max(18, Font.Height); // as tall as the text
        iconButton.Image = model != null ? Models3D.Tile(model, ccol, tile)
                         : ic == null ? null : show3D ? IconPicker.Tile3D(ic, ccol, tile) : IconPicker.Tile(ic, ccol, tile);
        swatches.Current = Materials.Parse(style.GlowColor);
        swatches.Invalidate();
        if (presetTiles.Count > 0) RefreshCards();
    }
}
