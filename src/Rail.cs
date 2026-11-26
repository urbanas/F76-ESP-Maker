using static ItemFinder.Loc;
using System.Drawing.Drawing2D;

namespace ItemFinder;

/// <summary>The main window's colours (layout A): soft grey-green panels, amber for what is selected; light or dark.</summary>
static class Ui
{
    public static bool Dark;

    /// <summary>Windows display scale (1 = 100 %, 1.5 = 150 %). The app is system-DPI aware: the scale is read once at start
    /// (a second screen with another scale gets the window stretched by Windows). Sizes in the code are in 100 % pixels:
    /// S() turns one into screen pixels, ScaleTree scales a control tree that was built with 100 % sizes.</summary>
    public static readonly float F = ReadScale();
    static float ReadScale()
    {
        try { using var g = Graphics.FromHwnd(IntPtr.Zero); return Math.Max(1f, g.DpiX / 96f); } catch { return 1f; }
    }
    public static int S(int px) => F == 1f ? px : (int)Math.Round(px * F);
    public static float S(float px) => px * F;
    /// <summary>Screen pixels back to 100 % pixels (sizes saved in the settings).</summary>
    public static int U(int px) => F == 1f ? px : (int)Math.Round(px / F);
    public static Size S(Size s) => new(S(s.Width), S(s.Height));
    public static Padding S(Padding p) => new(S(p.Left), S(p.Top), S(p.Right), S(p.Bottom));
    /// <summary>Scales the sizes, places, paddings and margins of <paramref name="root"/>'s children (not root's own size:
    /// a form sets that itself), once, after they were built in 100 % pixels. Fonts are not touched: Windows scales them.</summary>
    public static void ScaleTree(Control root)
    {
        if (F == 1f) return;
        root.Padding = S(root.Padding);
        foreach (Control c in root.Controls) ScaleOne(c);
    }

    /// <summary>A window built in 100 % pixels: its controls, and its own size unless it sizes itself (AutoSize).</summary>
    public static void ScaleForm(Form f)
    {
        if (F == 1f) return;
        if (f.MinimumSize != Size.Empty) f.MinimumSize = S(f.MinimumSize);
        if (!f.AutoSize)
        {
            // kept on the screen (a big window at 200 % on a small screen)
            var area = Screen.FromControl(f.Owner ?? f).WorkingArea;
            var c = S(f.ClientSize);
            var frame = f.Size - f.ClientSize;
            f.ClientSize = new Size(Math.Min(c.Width, area.Width - frame.Width), Math.Min(c.Height, area.Height - frame.Height));
            if (f.MinimumSize.Width > area.Width || f.MinimumSize.Height > area.Height)
                f.MinimumSize = new Size(Math.Min(f.MinimumSize.Width, area.Width), Math.Min(f.MinimumSize.Height, area.Height));
        }
        ScaleTree(f);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object?> scaledOnce = new();

    /// <summary>A control that sizes itself from its text and Ui.S() sizes: ScaleTree leaves its size alone.</summary>
    public interface ISelfSized { }

    /// <summary>A control (and what is in it) made after its window was scaled, with 100 % sizes: to the display scale.</summary>
    public static T Scaled<T>(T c) where T : Control { if (F != 1f) ScaleOne(c); return c; }

    /// <summary>One control and what is in it. An auto-sized control keeps sizing itself (its font already grew with
    /// the scale; Control.Scale made such buttons twice as wide); only a height set larger than it needs is scaled.</summary>
    static void ScaleOne(Control c)
    {
        // each control once (a scaled one can be moved into a window that is scaled later, e.g. the look editor)
        if (scaledOnce.TryGetValue(c, out _)) return;
        scaledOnce.Add(c, null);
        c.Margin = S(c.Margin);
        c.Padding = S(c.Padding);
        if (c.MinimumSize != Size.Empty) c.MinimumSize = S(c.MinimumSize);
        if (c.MaximumSize != Size.Empty) c.MaximumSize = S(c.MaximumSize);
        if (c is Splitter sp) { sp.MinSize = S(sp.MinSize); sp.MinExtra = S(sp.MinExtra); }
        if (c is SplitContainer sc)
        {
            sc.Panel1MinSize = S(sc.Panel1MinSize);
            sc.Panel2MinSize = S(sc.Panel2MinSize);
            sc.SplitterWidth = S(sc.SplitterWidth);
        }
        if (c is ISelfSized) c.Location = new Point(S(c.Left), S(c.Top));
        else if (c.AutoSize) { if (c is ButtonBase && c.Dock == DockStyle.None) c.MinimumSize = new Size(c.MinimumSize.Width, Math.Max(c.MinimumSize.Height, S(c.Height))); }
        else if (c is not (SplitterPanel or TabPage)) c.Bounds = new Rectangle(S(c.Left), S(c.Top), S(c.Width), S(c.Height));
        foreach (Control k in c.Controls) ScaleOne(k);
    }
    static Color P(int r, int g, int b, int dr, int dg, int db) => Dark ? Color.FromArgb(dr, dg, db) : Color.FromArgb(r, g, b);
    public static Color Panel => P(243, 244, 242, 34, 39, 36);
    public static Color Window => P(255, 255, 255, 28, 32, 30);
    public static Color Line => P(192, 197, 190, 58, 64, 60);
    public static Color Sel => P(222, 231, 225, 52, 64, 57);
    public static Color Hover => P(234, 237, 233, 44, 50, 46);
    public static Color Accent => P(196, 106, 0, 224, 160, 74);
    public static Color AccentSoft => P(252, 222, 176, 58, 46, 29);
    /// <summary>A picked segment / chip: solid orange with white text in light mode (a soft fill looked pale), soft in dark.</summary>
    public static Color LitBack => Dark ? AccentSoft : Accent;
    public static Color LitText => Dark ? Accent : Color.White;
    public static Color Fg => P(31, 36, 33, 231, 235, 232);
    public static Color Muted => P(84, 92, 87, 154, 163, 158);
    /// <summary>Dark mode's background for boxes to type in.</summary>
    static readonly Color Field = Color.FromArgb(46, 52, 48);

    /// <summary>Light colour -> dark colour, for the colours the window's controls were made with.</summary>
    static readonly (Color Light, Color Dark)[] Pairs =
    {
        (SystemColors.Control, Color.FromArgb(34, 39, 36)),
        (Color.FromArgb(243, 244, 242), Color.FromArgb(34, 39, 36)),
        (SystemColors.Window, Color.FromArgb(28, 32, 30)),
        (Color.White, Color.FromArgb(28, 32, 30)),
        (SystemColors.ControlText, Color.FromArgb(231, 235, 232)),
        (SystemColors.WindowText, Color.FromArgb(231, 235, 232)),
        (Color.FromArgb(31, 36, 33), Color.FromArgb(231, 235, 232)),
        (Color.DimGray, Color.FromArgb(154, 163, 158)),
        (Color.FromArgb(84, 92, 87), Color.FromArgb(154, 163, 158)),
        (SystemColors.GrayText, Color.FromArgb(130, 138, 133)),
        (Color.Gray, Color.FromArgb(130, 138, 133)),
        (Color.FromArgb(192, 197, 190), Color.FromArgb(58, 64, 60)),
        (SystemColors.ControlLight, Color.FromArgb(52, 64, 57)),
        (Color.FromArgb(196, 106, 0), Color.FromArgb(224, 160, 74)),
        (Color.FromArgb(20, 125, 60), Color.FromArgb(92, 191, 131)),
        (Color.SeaGreen, Color.FromArgb(92, 191, 131)),
        (Color.Firebrick, Color.FromArgb(240, 110, 100)),
        (Color.Blue, Color.FromArgb(130, 175, 255)),
        (Color.FromArgb(0, 0, 255), Color.FromArgb(130, 175, 255)),
    };

    /// <summary>The colour in the current theme: a known light or dark colour becomes its pair for now; others stay.</summary>
    public static Color ToTheme(Color c)
    {
        int argb = c.ToArgb();
        foreach (var (l, d) in Pairs)
            if (l.ToArgb() == argb || d.ToArgb() == argb) return Dark ? d : l;
        return c;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    /// <summary>Dark or light title bar (Windows 10 2004+ / 11; older ones ignore it).</summary>
    public static void TitleBar(Form f)
    {
        if (!f.IsHandleCreated) return;
        int on = Dark ? 1 : 0;
        try { DwmSetWindowAttribute(f.Handle, 20, ref on, 4); } catch { }
    }

    /// <summary>Dark or light scroll bars on a list, tree, text box or scrolling panel.</summary>
    public static void ScrollBars(Control c)
    {
        if (!c.IsHandleCreated) return;
        try { SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
    }

    static readonly Dictionary<Control, (FlatStyle Style, bool VisualBack, Color Back, Color Border)> buttonStyle = new();
    /// <summary>What dark mode gives a framed button.</summary>
    static readonly Color DarkButton = Color.FromArgb(44, 50, 46);
    static readonly Dictionary<ComboBox, FlatStyle> comboStyle = new();

    /// <summary>Gives a control and everything in it the current theme's colours.</summary>
    public static void Apply(Control root)
    {
        foreach (var c in All(root))
        {
            // a button's colours as made (light), remembered before the first theming changes them
            // (a button first seen in dark mode takes its dark parent's colour: kept as its light pair, and a plain
            // control-grey button keeps the visual style - else switching back to light left it dark)
            if (c is ButtonBase bb and (Button or CheckBox { Appearance: Appearance.Button }) && !buttonStyle.ContainsKey(bb))
            {
                var light = Pairs.FirstOrDefault(p => p.Dark.ToArgb() == bb.BackColor.ToArgb()).Light;
                var back = light.IsEmpty ? bb.BackColor : light;
                buttonStyle[bb] = (bb.FlatStyle, bb.UseVisualStyleBackColor || back.ToArgb() == SystemColors.Control.ToArgb(), back, bb.FlatAppearance.BorderColor);
            }
            c.BackColor = ToTheme(c.BackColor);
            c.ForeColor = ToTheme(c.ForeColor);
            switch (c)
            {
                case ButtonBase b and (Button or CheckBox { Appearance: Appearance.Button }):
                    var (style, visual, back, border) = buttonStyle[b];
                    if (Dark)
                    {
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = Line;
                        b.FlatAppearance.MouseOverBackColor = Hover;
                        b.BackColor = b.FlatAppearance.BorderSize == 0 ? b.BackColor : DarkButton;
                    }
                    else
                    {
                        b.FlatStyle = style;
                        if (style != FlatStyle.Flat) { b.BackColor = back; b.UseVisualStyleBackColor = visual; }
                        else if (b.BackColor.ToArgb() == DarkButton.ToArgb()) b.BackColor = back; // a framed flat button (the Icon box)
                        b.FlatAppearance.BorderColor = border;
                        b.FlatAppearance.MouseOverBackColor = Color.Empty;
                    }
                    break;
                case ComboBox cb:
                    if (!comboStyle.ContainsKey(cb)) comboStyle[cb] = cb.FlatStyle;
                    cb.FlatStyle = Dark ? FlatStyle.Flat : comboStyle[cb];
                    if (cb.BackColor.ToArgb() == Window.ToArgb() || cb.BackColor.ToArgb() == Field.ToArgb()) cb.BackColor = Dark ? Field : SystemColors.Window;
                    break;
                case LinkLabel ll:
                    ll.LinkColor = ToTheme(ll.LinkColor);
                    ll.ActiveLinkColor = ToTheme(ll.ActiveLinkColor);
                    break;
                case TreeView or ListView:
                    ScrollBars(c);
                    break;
                case TextBoxBase or UpDownBase:
                    // a box to type in: a raised grey in dark (window black looks like a hole)
                    if (c.BackColor.ToArgb() == Window.ToArgb() || c.BackColor.ToArgb() == Field.ToArgb()) c.BackColor = Dark ? Field : SystemColors.Window;
                    ScrollBars(c);
                    break;
                case ScrollableControl { AutoScroll: true }:
                    ScrollBars(c);
                    break;
            }
        }
    }

    /// <summary>A dialog in the current theme: its controls when it loads (after they are all added), its title bar.</summary>
    public static T Dialog<T>(T f) where T : Form
    {
        // dark: cloaked (hidden by Windows, still painting) until the first full paint, else its controls flash white
        bool cloaked = false;
        // built in 100 % pixels: to the display scale first (before the dialog's own Load handlers fit it)
        f.Load += (_, _) => { ScaleForm(f); Apply(f); };
        f.HandleCreated += (_, _) =>
        {
            TitleBar(f);
            if (!Dark) return;
            int on = 1;
            try { cloaked = DwmSetWindowAttribute(f.Handle, 13, ref on, 4) == 0; } catch { } // DWMWA_CLOAK
        };
        f.Shown += (_, _) =>
        {
            if (!cloaked) return;
            cloaked = false;
            f.Refresh();
            int off = 0;
            try { DwmSetWindowAttribute(f.Handle, 13, ref off, 4); } catch { }
        };
        return f;
    }

    static IEnumerable<Control> All(Control root)
    {
        yield return root;
        foreach (Control c in root.Controls)
            foreach (var x in All(c)) yield return x;
    }

    /// <summary>Menus in the current theme.</summary>
    public static void Menus() => ToolStripManager.Renderer = Dark ? new DarkMenus() : new ToolStripProfessionalRenderer();

    sealed class DarkMenus : ToolStripProfessionalRenderer
    {
        public DarkMenus() : base(new DarkTable()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Fg : Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Fg; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
            using var bg = new SolidBrush(Sel);
            e.Graphics.FillRectangle(bg, r);
            TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, r, Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    sealed class DarkTable : ProfessionalColorTable
    {
        static Color Bg => Color.FromArgb(34, 39, 36);
        static Color Hi => Color.FromArgb(52, 64, 57);
        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuBorder => Color.FromArgb(58, 64, 60);
        public override Color MenuItemBorder => Hi;
        public override Color MenuItemSelected => Hi;
        public override Color MenuItemSelectedGradientBegin => Hi;
        public override Color MenuItemSelectedGradientEnd => Hi;
        public override Color SeparatorDark => Color.FromArgb(58, 64, 60);
        public override Color SeparatorLight => Bg;
        public override Color CheckBackground => Hi;
        public override Color CheckSelectedBackground => Hi;
        // the menu bar at the top of the window
        public override Color MenuStripGradientBegin => Panel;
        public override Color MenuStripGradientEnd => Panel;
        public override Color MenuItemPressedGradientBegin => Bg;
        public override Color MenuItemPressedGradientMiddle => Bg;
        public override Color MenuItemPressedGradientEnd => Bg;
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>One section button on the rail: a drawn icon, the name under it and an amber badge with the count.</summary>
sealed class RailButton : Control
{
    public string Icon = "";
    public int Count;
    public bool On;
    bool hover;

    public RailButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public void Set(string text, int count, bool on)
    {
        if (Text == text && Count == count && On == on) return;
        Text = text; Count = count; On = on;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(e); base.OnKeyDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Ui.Panel);
        var box = new RectangleF(Ui.S(2), 1, Width - Ui.S(4), Height - 2);
        if (On || hover)
        {
            using var bg = new SolidBrush(On ? Ui.Sel : Ui.Hover);
            using var path = Ui.Round(box, Ui.S(7f));
            g.FillPath(bg, path);
        }
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Ui.Accent, 1.5f);
            using var path = Ui.Round(box, Ui.S(7f));
            g.DrawPath(pen, path);
        }
        var colour = On ? Ui.Fg : Ui.Muted;
        DrawIcon(g, Icon, new RectangleF(Width / 2f - Ui.S(11f), Ui.S(9f), Ui.S(22f), Ui.S(22f)), colour);
        using var font = new Font(Loc.UiFont, 8.5f, On ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(g, Text, font, new Rectangle(0, Ui.S(34), Width, Height - Ui.S(36)), colour,
                              TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (Count > 0)
        {
            var s = Count.ToString("N0");
            using var bf = new Font(Loc.UiFont, 7.5f, FontStyle.Bold);
            var sz = TextRenderer.MeasureText(g, s, bf, Size.Empty, TextFormatFlags.NoPadding);
            var badge = new RectangleF(Width / 2f + Ui.S(4f), Ui.S(4f), sz.Width + Ui.S(9f), sz.Height + Ui.S(2f));
            using var bb = new SolidBrush(Ui.Accent);
            using var bp = Ui.Round(badge, badge.Height / 2);
            g.FillPath(bb, bp);
            TextRenderer.DrawText(g, s, bf, Rectangle.Round(badge), Color.White,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Simple line icons (24-unit grid, like the mockup's).</summary>
    static void DrawIcon(Graphics g, string icon, RectangleF r, Color c)
    {
        var state = g.Save();
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 24f, r.Height / 24f);
        using var pen = new Pen(c, 1.7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (icon)
        {
            case "mods": // stacked layers = the load order
                g.DrawPolygon(pen, new PointF[] { new(12, 4), new(20, 8), new(12, 12), new(4, 8) });
                g.DrawLines(pen, new PointF[] { new(4, 12), new(12, 16), new(20, 12) });
                g.DrawLines(pen, new PointF[] { new(4, 16), new(12, 20), new(20, 16) });
                break;
            case "picks":
                g.DrawPolygon(pen, new PointF[] { new(12, 3), new(14.6f, 8.6f), new(20.6f, 9.3f), new(16.1f, 13.4f), new(17.3f, 19.4f), new(12, 16.4f), new(6.7f, 19.4f), new(7.9f, 13.4f), new(3.4f, 9.3f), new(9.4f, 8.6f) });
                break;
            case "ITEMS":
                g.DrawPolygon(pen, new PointF[] { new(4, 8), new(12, 4), new(20, 8), new(20, 16), new(12, 20), new(4, 16) });
                g.DrawLines(pen, new PointF[] { new(4, 8), new(12, 12), new(20, 8) });
                g.DrawLine(pen, 12, 12, 12, 20);
                break;
            case "JUNK": // a gear
                g.DrawEllipse(pen, 8.5f, 8.5f, 7, 7);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, 12 + (float)Math.Cos(a) * 5.5f, 12 + (float)Math.Sin(a) * 5.5f, 12 + (float)Math.Cos(a) * 8.5f, 12 + (float)Math.Sin(a) * 8.5f);
                }
                break;
            case "EVENTS":
                using (var p = Ui.Round(new RectangleF(4, 5, 16, 15), 2)) g.DrawPath(pen, p);
                g.DrawLine(pen, 4, 10, 20, 10);
                g.DrawLine(pen, 9, 3, 9, 7);
                g.DrawLine(pen, 15, 3, 15, 7);
                break;
            case "ENEMIES":
                g.DrawEllipse(pen, 5, 5, 14, 14);
                g.DrawLine(pen, 12, 2, 12, 7); g.DrawLine(pen, 12, 17, 12, 22);
                g.DrawLine(pen, 2, 12, 7, 12); g.DrawLine(pen, 17, 12, 22, 12);
                break;
            default: // everything else (all game objects): a grid
                foreach (var (x, y) in new[] { (4, 4), (13, 4), (4, 13), (13, 13) })
                    using (var p = Ui.Round(new RectangleF(x, y, 7, 7), 1.5f)) g.DrawPath(pen, p);
                break;
        }
        g.Restore(state);
    }
}

/// <summary>The item list: Windows draws its own column lines in the empty part under the rows (a hidden, zero-width
/// column gets a second one next to the first). After each paint that part is painted over with the same thin lines
/// the rows have, so every column ends in one line all the way down.</summary>
sealed class FlatList : ListView
{
    // owner-drawn rows repaint on every mouse move (hot tracking): without this they flicker
    public FlatList() => DoubleBuffered = true;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F || !OwnerDraw || View != View.Details) return; // WM_PAINT
        int top = Items.Count > 0 ? Items[Items.Count - 1].Bounds.Bottom : HeaderBottom();
        if (top >= ClientSize.Height) return;
        using var g = CreateGraphics();
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, 0, top, ClientSize.Width, ClientSize.Height - top);
        using var pen = new Pen(Ui.Line);
        int x = Items.Count > 0 ? Items[Items.Count - 1].Bounds.Left : 0;
        foreach (ColumnHeader c in Columns.Cast<ColumnHeader>().OrderBy(c => c.DisplayIndex))
        {
            x += c.Width;
            if (c.Width > 0 && x - 1 < ClientSize.Width) g.DrawLine(pen, x - 1, top, x - 1, ClientSize.Height);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out Rect r);
    struct Rect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    /// <summary>Repaints the column header (its tick box follows the rows' ticks).</summary>
    public void RefreshHeader()
    {
        if (!IsHandleCreated) return;
        var header = SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero); // LVM_GETHEADER
        if (header != IntPtr.Zero) InvalidateRect(header, IntPtr.Zero, true);
    }

    int HeaderBottom()
    {
        var header = SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero); // LVM_GETHEADER
        return header != IntPtr.Zero && GetWindowRect(header, out var r) ? r.Bottom - r.Top : 0;
    }
}

/// <summary>The ready-made looks as small pictures (4 per row) in the look's colour; click one to use it.
/// The picture is a little figure on a stand: outline / fill / beam / arrow drawn the way the look does it in the game.</summary>
sealed class PresetTiles : Control
{
    public string[] Names = Array.Empty<string>();
    /// <summary>The look in use, -1 = none (a custom look).</summary>
    public int Selected = -1;
    public Color Colour = Color.Cyan;
    int hover = -1;
    const int Cols = 4;
    static int TileH => Ui.S(82);
    static int Gap => Ui.S(6);

    /// <summary>A tile was clicked.</summary>
    public event Action<int>? Picked;
    /// <summary>The mouse moved onto a tile (-1: off them all).</summary>
    public event Action<int>? Hovered;

    public PresetTiles()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 2, 0, 2);
    }

    public override Size GetPreferredSize(Size proposed) => new(Math.Max(Ui.S(240), proposed.Width), Rows * (TileH + Gap));
    int Rows => (Names.Length + Cols - 1) / Cols;

    Rectangle Tile(int i)
    {
        int w = (Width - (Cols - 1) * Gap) / Cols;
        return new Rectangle(i % Cols * (w + Gap), i / Cols * (TileH + Gap), w, TileH);
    }

    int At(Point p) { for (int i = 0; i < Names.Length; i++) if (Tile(i).Contains(p)) return i; return -1; }

    protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.Location); if (h != hover) { hover = h; Invalidate(); Hovered?.Invoke(h); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); Hovered?.Invoke(-1); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { int i = At(e.Location); if (i >= 0 && e.Button == MouseButtons.Left) { Focus(); Picked?.Invoke(i); } base.OnMouseClick(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int s = Math.Max(0, Selected);
        int to = e.KeyCode switch { Keys.Left => s - 1, Keys.Right => s + 1, Keys.Up => s - Cols, Keys.Down => s + Cols, _ => -1 };
        if (to >= 0 && to < Names.Length) { Picked?.Invoke(to); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Ui.Panel);
        using var font = new Font(Loc.UiFont, 8f);
        using var bold = new Font(Loc.UiFont, 8f, FontStyle.Bold);
        for (int i = 0; i < Names.Length; i++)
        {
            var r = Tile(i);
            bool on = i == Selected;
            using (var bg = new SolidBrush(on ? Ui.AccentSoft : i == hover ? Ui.Hover : Ui.Window))
            using (var path = Ui.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Ui.S(7f)))
            {
                g.FillPath(bg, path);
                using var pen = new Pen(on ? Ui.Accent : Ui.Line, Ui.S(on ? 2f : 1f));
                g.DrawPath(pen, path);
            }
            var pic = new Rectangle(r.X + (r.Width - Ui.S(52)) / 2, r.Y + Ui.S(5), Ui.S(52), Ui.S(52));
            DrawLook(g, Names[i], pic, Colour);
            TextRenderer.DrawText(g, Tr(Names[i]), on ? bold : font, new Rectangle(r.X + Ui.S(2), pic.Bottom, r.Width - Ui.S(4), r.Bottom - pic.Bottom),
                                  Ui.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        if (Focused && ShowFocusCues && Selected >= 0)
        {
            using var pen = new Pen(Ui.Fg) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, Rectangle.Inflate(Tile(Selected), -3, -3));
        }
    }

    /// <summary>The little scene: dark ground, a grey figure on a stand, and the look's effects in its colour.</summary>
    public static void DrawLook(Graphics g, string name, Rectangle r, Color c) =>
        DrawScene(g, r, c, fill: name.Contains("fill", StringComparison.OrdinalIgnoreCase) || name == "Fill + beam",
                  outline: name.StartsWith("Outline"), beam: name.Contains("beam") || name == "Tall beacon",
                  arrow: name.Contains("arrow"), own: name == "Bright colours", tall: name == "Tall beacon", strong: name == "Bright fill");

    /// <summary>The same little scene for any look (the item list's Look column).</summary>
    public static void DrawStyle(Graphics g, Style st, Rectangle r) =>
        DrawScene(g, r, Materials.Parse(st.Glow ? st.GlowColor : st.BeamColor), fill: st.Glow && st.GlowFill, outline: st.Glow && !st.GlowFill,
                  beam: st.Beam, arrow: st.Arrow, own: st.Brighten,
                  tall: st.Beam && st.BeamLength >= 2000, strong: st.GlowFill && st.GlowOpacity >= 70);

    static void DrawScene(Graphics g, Rectangle r, Color c, bool fill, bool outline, bool beam, bool arrow, bool own, bool tall, bool strong)
    {
        var state = g.Save();
        using (var clip = Ui.Round(r, Math.Min(Ui.S(5), r.Width / 6))) { g.SetClip(clip); using var scene = new SolidBrush(Color.FromArgb(42, 47, 44)); g.FillPath(scene, clip); }
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 100f, r.Height / 100f);
        if (beam)
        {
            var b = tall ? new RectangleF(43, 0, 14, 90) : new RectangleF(46, 8, 8, 52);
            using var lg = new LinearGradientBrush(b, Color.FromArgb(0, c), Color.FromArgb(230, c), LinearGradientMode.Vertical);
            g.FillRectangle(lg, b);
        }
        using (var stand = new SolidBrush(Color.FromArgb(110, 110, 110))) g.FillEllipse(stand, 28, 85, 44, 10);
        using (var body = new SolidBrush(own ? Color.FromArgb(150, 200, 240) : Color.FromArgb(138, 143, 140))) g.FillRectangle(body, 45, 58, 10, 30);
        using (var head = new SolidBrush(own ? Color.FromArgb(255, 226, 120) : Color.FromArgb(169, 170, 160))) g.FillEllipse(head, 34, 32, 32, 32);
        if (own)
        {
            // its own colours, brighter: a soft white shine around it
            using var shine = new Pen(Color.FromArgb(150, 255, 255, 230), 3f);
            g.DrawEllipse(shine, 31, 29, 38, 38);
        }
        if (fill)
        {
            using var f = new SolidBrush(Color.FromArgb(strong ? 245 : 160, c));
            g.FillRectangle(f, 45, 58, 10, 30);
            g.FillEllipse(f, 34, 32, 32, 32);
        }
        if (outline)
        {
            using var p = new Pen(c, Math.Max(4f, 170f / r.Width)); // at least ~1.7 px in the item list's tiny pictures
            g.DrawRectangle(p, 45, 58, 10, 30);
            g.DrawEllipse(p, 34, 32, 32, 32);
        }
        if (arrow) using (var a = new SolidBrush(c)) g.FillPolygon(a, new PointF[] { new(38, 6), new(62, 6), new(50, 24) });
        g.Restore(state);
    }
}

/// <summary>A row of colour dots to pick from, and a last "+" dot that opens the full colour picker.</summary>
sealed class SwatchRow : Control, Ui.ISelfSized
{
    public static readonly string[] Colours = { "#FFD23F", "#FF8A00", "#FF3B30", "#FF4FB3", "#A66BFF", "#3D8BFF", "#00E5FF", "#3CE07A", "#C8F03C", "#FFFFFF" };
    public Color Current = Color.Cyan;
    int D = Ui.S(22), Gap = Ui.S(6);
    int hover = -1;

    /// <summary>Smaller dots (a narrow column).</summary>
    public bool Small
    {
        set { D = Ui.S(value ? 17 : 22); Gap = Ui.S(value ? 4 : 6); Size = new Size(Colours.Length * (D + Gap) + D + Ui.S(9), D + Ui.S(8)); Invalidate(); }
    }

    /// <summary>A colour dot was clicked (its colour), or the "+" one (null).</summary>
    public event Action<Color?>? Picked;

    public SwatchRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Size = new Size(Colours.Length * (D + Gap) + D + Ui.S(9), D + Ui.S(8)); // room for the last dot's picked ring (it was cut)
        Margin = new Padding(3, 3, 3, 3);
    }

    Rectangle Dot(int i) => new(Ui.S(4) + i * (D + Gap), Ui.S(4), D, D);
    int At(Point p) { for (int i = 0; i <= Colours.Length; i++) if (Dot(i).Contains(p)) return i; return -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = At(e.Location);
        if (i >= 0) Picked?.Invoke(i < Colours.Length ? ColorTranslator.FromHtml(Colours[i]) : null);
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Ui.Panel);
        bool matched = false;
        for (int i = 0; i <= Colours.Length; i++)
        {
            var r = Dot(i);
            if (i < Colours.Length)
            {
                var c = ColorTranslator.FromHtml(Colours[i]);
                bool on = c.ToArgb() == Current.ToArgb();
                matched |= on;
                using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
                using var pen = new Pen(on ? Ui.Fg : i == hover ? Ui.Muted : Ui.Line, Ui.S(on ? 2.5f : 1f));
                g.DrawEllipse(pen, on ? Rectangle.Inflate(r, Ui.S(2), Ui.S(2)) : r);
            }
            else
            {
                // "+": any colour; shows the current one when it isn't one of the dots
                if (!matched) using (var b = new SolidBrush(Current)) g.FillEllipse(b, r);
                using var pen = new Pen(!matched ? Ui.Fg : i == hover ? Ui.Muted : Ui.Line, Ui.S(!matched ? 2.5f : 1f));
                g.DrawEllipse(pen, !matched ? Rectangle.Inflate(r, Ui.S(2), Ui.S(2)) : r);
                if (matched) TextRenderer.DrawText(g, "+", Font, r, Ui.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}

/// <summary>Joined buttons in a row ("Outline | Whole item"); the lit one is the choice, none lit = a value of its own.</summary>
sealed class Seg : Control, Ui.ISelfSized
{
    readonly string[] options;
    int selected = -1, hover = -1;

    /// <summary>A button was clicked (its index).</summary>
    public event Action<int>? Picked;

    public Seg(params string[] options)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        this.options = options;
        Cursor = Cursors.Hand;
        Margin = new Padding(3, 3, 6, 3);
        Size = GetPreferredSize(Size.Empty);
    }

    /// <summary>The lit button, -1 = none.</summary>
    public int Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }

    /// <summary>Changes a button's text (e.g. a count in it); the control resizes.</summary>
    public void SetText(int i, string text)
    {
        if (options[i] == text) return;
        options[i] = text;
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    int W(int i) => TextRenderer.MeasureText(options[i], Font).Width + Ui.S(14);
    /// <summary>At least this high (e.g. the height of the buttons beside it); kept when the font changes.</summary>
    public int MinHeight { get => minHeight; set { minHeight = value; Size = GetPreferredSize(Size.Empty); } }
    int minHeight;
    public override Size GetPreferredSize(Size proposed) => new(Enumerable.Range(0, options.Length).Sum(W) + 1, Math.Max(minHeight, Font.Height + Ui.S(10)));
    protected override void OnFontChanged(EventArgs e) { Size = GetPreferredSize(Size.Empty); base.OnFontChanged(e); }
    Rectangle Part(int i) { int x = 0; for (int j = 0; j < i; j++) x += W(j); return new(x, 0, W(i), Height - 1); }
    int At(Point p) { for (int i = 0; i < options.Length; i++) if (Part(i).Contains(p)) return i; return -1; }
    /// <summary>A button that can't be picked now (greyed; a lit one stays lit), -1 = none.</summary>
    public int Off { get => off; set { if (off != value) { off = value; Invalidate(); } } }
    int off = -1;
    void Pick(int i) { if (i < 0 || i >= options.Length || i == off) return; Selected = i; Picked?.Invoke(i); }
    protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { Focus(); Pick(At(e.Location)); base.OnMouseClick(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) Pick(Math.Max(0, selected - 1));
        if (e.KeyCode == Keys.Right) Pick(Math.Min(options.Length - 1, selected + 1));
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Ui.Window);
        using var line = new Pen(Focused ? Ui.Accent : Ui.Line);
        for (int i = 0; i < options.Length; i++)
        {
            var r = Part(i);
            bool on = i == selected && Enabled;
            using (var bg = new SolidBrush(on ? Ui.LitBack : i == hover && Enabled && i != off ? Ui.Hover : Ui.Window)) g.FillRectangle(bg, r);
            TextRenderer.DrawText(g, options[i], Font, r, !Enabled || i == off && !on ? Ui.Muted : on ? Ui.LitText : Ui.Fg,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (i > 0) g.DrawLine(line, r.Left, 0, r.Left, r.Bottom);
        }
        g.DrawRectangle(line, 0, 0, Part(options.Length - 1).Right, Height - 1);
    }
}

/// <summary>A small funnel button that opens the list filters (All / Selected / ...) as a menu. Showing everything it is
/// only the funnel; another filter lights it up with the filter's name, so a filtered list is never a surprise.
/// Same use as <see cref="Seg"/> (Selected, SetText, Picked). (User, 2026-11-24: the always-shown filter strip took a
/// whole row for something used a few times ever.)</summary>
sealed class FilterButton : Control, Ui.ISelfSized
{
    readonly string[] options;
    int selected;
    bool hover;
    public event Action<int>? Picked;

    public FilterButton(params string[] options)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        this.options = options;
        Cursor = Cursors.Hand;
        AccessibleName = Loc.Tr("Show");
        Fit();
    }

    /// <summary>The filter in use (0 = all).</summary>
    public int Selected { get => selected; set { if (selected == value) return; selected = value; Fit(); Invalidate(); } }

    /// <summary>A filter's text with its count ("Selected 5").</summary>
    public void SetText(int i, string text)
    {
        if (options[i] == text) return;
        options[i] = text;
        if (i == selected) { Fit(); Invalidate(); }
    }

    int Icon => Font.Height + Ui.S(4);
    /// <summary>The funnel only while showing all; the funnel and the filter's name otherwise.</summary>
    void Fit() => Width = selected <= 0 ? Icon + Ui.S(8) : Icon + TextRenderer.MeasureText(options[selected], Font).Width + Ui.S(20);
    protected override void OnFontChanged(EventArgs e) { Fit(); base.OnFontChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseClick(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Open(); base.OnMouseClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space or Keys.Down) { Open(); e.Handled = true; } base.OnKeyDown(e); }

    /// <summary>The filters as a menu under the button, the one in use ticked.</summary>
    void Open()
    {
        var menu = new ContextMenuStrip { Font = Font };
        for (int i = 0; i < options.Length; i++)
        {
            int k = i;
            menu.Items.Add(new ToolStripMenuItem(options[i], null, (_, _) => { Selected = k; Picked?.Invoke(k); }) { Checked = i == selected });
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, new Point(Width - menu.PreferredSize.Width, Height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Ui.Panel);
        bool on = selected > 0;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Ui.Round(r, Ui.S(4f)))
        {
            using (var bg = new SolidBrush(on ? Ui.LitBack : hover ? Ui.Hover : Ui.Window)) g.FillPath(bg, path);
            using var pen = new Pen(Focused ? Ui.Accent : on ? Ui.Accent : Ui.Line);
            g.DrawPath(pen, path);
        }
        var fg = on ? Ui.LitText : Ui.Fg;
        // the funnel
        float s = Icon * 0.55f, x = Ui.S(4) + (Icon - s) / 2f, y = (Height - s) / 2f;
        using (var pen = new Pen(fg, Ui.S(1.4f)) { LineJoin = LineJoin.Round })
            g.DrawPolygon(pen, new PointF[] { new(x, y), new(x + s, y), new(x + s * 0.6f, y + s * 0.5f), new(x + s * 0.6f, y + s),
                                              new(x + s * 0.4f, y + s * 0.85f), new(x + s * 0.4f, y + s * 0.5f) });
        if (on)
            TextRenderer.DrawText(g, options[selected], Font, new Rectangle(Ui.S(4) + Icon, 0, Width - Icon - Ui.S(8), Height), fg,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>An on / off switch (a pill with a knob).</summary>
sealed class Toggle : Control
{
    bool on;

    /// <summary>Clicked: <see cref="Checked"/> is already the new state.</summary>
    public event Action? Toggled;

    public Toggle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Size = new Size(34, 18);
        Margin = new Padding(3, 0, 6, 0);
    }

    public bool Checked { get => on; set { if (on != value) { on = value; Invalidate(); } } }

    void Flip() { Checked = !on; Toggled?.Invoke(); }
    protected override void OnMouseClick(MouseEventArgs e) { Focus(); Flip(); base.OnMouseClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Flip(); base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Ui.Panel);
        int h = Height - 2, w = Width - 2;
        using var pill = new GraphicsPath();
        pill.AddArc(1, 1, h, h, 90, 180);
        pill.AddArc(w - h + 1, 1, h, h, 270, 180);
        pill.CloseFigure();
        using (var b = new SolidBrush(on ? Ui.Accent : Ui.Line)) g.FillPath(b, pill);
        if (Focused) using (var p = new Pen(Ui.Fg)) g.DrawPath(p, pill);
        int k = h - Ui.S(4);
        using var knob = new SolidBrush(Ui.Dark ? Ui.Fg : Ui.Window);
        g.FillEllipse(knob, on ? w - k - Ui.S(1) : Ui.S(3), Ui.S(3), k, k);
    }
}

/// <summary>A borderless window that can still be resized by dragging its edges (the look pop-up).</summary>
sealed class SizablePopup : Form
{
    static int Grip => Ui.S(7);
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84 || WindowState != FormWindowState.Normal) return; // WM_NCHITTEST
        var p = PointToClient(Cursor.Position);
        bool l = p.X < Grip, r = p.X >= ClientSize.Width - Grip, t = p.Y < Grip, b = p.Y >= ClientSize.Height - Grip;
        int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
        if (hit != 0) m.Result = hit;
    }
}

/// <summary>A see-through accent area (never focused, clicks go through) showing where the dragged look pop-up will dock.</summary>
sealed class DockHint : Form
{
    public DockHint()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Opacity = 0.45;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x20 | 0x80 | 0x08000000; // WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            return cp;
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    /// <summary>Just under <paramref name="top"/> (the dragged pop-up stays on top of it).</summary>
    public void Below(Form top) => SetWindowPos(Handle, top.Handle, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE | NOMOVE | NOACTIVATE
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Accent);
        using var edge = new Pen(Ui.Fg, 3);
        g.DrawRectangle(edge, 1, 1, Width - 3, Height - 3);
        TextRenderer.DrawText(g, Text, big ??= new Font(Font.FontFamily, 14f, FontStyle.Bold), new Rectangle(10, 0, Width - 20, Height), Color.White,
                              TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
    Font? big;
}

/// <summary>The thin strip on the main window's right edge that opens / closes the docked look panel (text turned 90°).</summary>
sealed class SideTab : Control
{
    bool hover;
    public SideTab()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(hover ? Ui.Line : Ui.Panel);
        using (var edge = new Pen(Ui.Line)) g.DrawLine(edge, 0, 0, 0, Height);
        g.TranslateTransform(Width, 0);
        g.RotateTransform(90);
        // (TextRenderer ignores the turn - GDI+ text does not)
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var fg = new SolidBrush(Ui.Fg);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Text, Font, fg, new RectangleF(0, 0, Height, Width), sf);
    }
}

/// <summary>A panel that lets the mouse through near its edges (to the window under it: <see cref="SizablePopup"/>'s grips).</summary>
sealed class EdgePanel : Panel
{
    static int Grip => Ui.S(6);
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84)
        {
            var p = PointToClient(Cursor.Position);
            if (p.X < Grip || p.Y < Grip || p.X >= Width - Grip || p.Y >= Height - Grip) { m.Result = -1; return; } // HTTRANSPARENT
        }
        base.WndProc(ref m);
    }
}

/// <summary>An on / off button with a word on it (an effect: Pulse, Rainbow...): a soft-accent pill while on.</summary>
sealed class Chip : Control, Ui.ISelfSized
{
    bool on, hover;

    /// <summary>Checked changed (by a click or in code), like a check box.</summary>
    public event EventHandler? CheckedChanged;

    public Chip(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        Text = text;
        Cursor = Cursors.Hand;
        Fit();
    }

    public bool Checked
    {
        get => on;
        set { if (on == value) return; on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    bool locked;
    /// <summary>Greyed and not clickable, but still shows its tooltip (a disabled control shows none) - for "can't be
    /// used here, and why".</summary>
    public bool Locked
    {
        get => locked;
        set { if (locked == value) return; locked = value; Cursor = value ? Cursors.Default : Cursors.Hand; TabStop = !value; Invalidate(); }
    }

    void Fit() => Size = new Size(TextRenderer.MeasureText(Text, Font).Width + Ui.S(22), Font.Height + Ui.S(10));
    protected override void OnFontChanged(EventArgs e) { Fit(); base.OnFontChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Fit(); base.OnTextChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { if (!locked) { Focus(); Checked = !on; } base.OnMouseClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && !locked) Checked = !on; base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Ui.Window);
        float h = Height - 2, w = Width - 2;
        using var pill = new GraphicsPath();
        pill.AddArc(0.5f, 0.5f, h, h, 90, 180);
        pill.AddArc(w - h + 0.5f, 0.5f, h, h, 270, 180);
        pill.CloseFigure();
        if (locked)
        {
            using (var b = new SolidBrush(Ui.Window)) g.FillPath(b, pill);
            using (var p = new Pen(Ui.Line) { DashStyle = DashStyle.Dot }) g.DrawPath(p, pill);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }
        using (var b = new SolidBrush(on ? Ui.LitBack : hover ? Ui.Hover : Ui.Window)) g.FillPath(b, pill);
        using (var p = new Pen(on || Focused ? Ui.Accent : Ui.Line, on ? 1.5f : 1f)) g.DrawPath(p, pill);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, on ? Ui.LitText : Ui.Fg,
                              TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
