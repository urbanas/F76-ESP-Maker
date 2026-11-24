using static ItemFinder.Loc;
namespace ItemFinder;

/// <summary>The look panel on the right of the main window: what is picked (a category, items or one part), its tick,
/// whether it follows the look above it or has its own, the look settings and a 3D preview. Changes apply at once -
/// the main window listens to the events and stores them.</summary>
sealed class LookPanel : UserControl
{
    readonly Label title = new() { Dock = DockStyle.Top, Height = 30, Font = new Font(Loc.UiFont, 11.5f, FontStyle.Bold), AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label sub = new() { Dock = DockStyle.Top, Height = 38, ForeColor = Color.DimGray, UseMnemonic = false, AutoEllipsis = true };
    readonly CheckBox tick = new() { Dock = DockStyle.Top, Height = 28, Text = Tr("Lights up in the game"), AutoCheck = false };
    /// <summary>Whose look it uses: "Uses the Bosses look..." or "Own look · use the Bosses look again".</summary>
    readonly FlowLayoutPanel switchRow = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 2, 0, 6) };
    readonly Label followText = new() { AutoSize = true, MaximumSize = new Size(900, 0), UseMnemonic = false, ForeColor = Color.DimGray, Margin = new Padding(0, 5, 4, 0) };
    readonly LinkLabel backLink = new() { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 5, 0, 0) };
    /// <summary>Plain text at the top: dragging it moves the pop-up.</summary>
    public Control[] DragHandles => new Control[] { title, sub, titleRow };
    /// <summary>The title with "Reset" at its right (room left for the pop-up's close button).</summary>
    readonly Panel titleRow = new() { Dock = DockStyle.Top, Height = 32, Padding = new Padding(0, 0, 40, 0) };
    readonly Button reset = new() { Text = Tr("↺ Reset look"), AutoSize = true, Dock = DockStyle.Right };
    /// <summary>"⧉ Detach" right of Reset, shown while docked in the main window (MainForm wires it).</summary>
    public readonly Button Detach = new() { Text = Tr("⧉ Detach"), AutoSize = true, Dock = DockStyle.Right, Visible = false };
    readonly Panel detachGap = new() { Width = 6, Dock = DockStyle.Right, Visible = false };
    /// <summary>Docked: Detach shows and no room is kept for the pop-up's ✕.</summary>
    public bool Docked
    {
        set { Detach.Visible = detachGap.Visible = value; titleRow.Padding = new Padding(0, 0, value ? 0 : Ui.S(40), 0); }
    }
    /// <summary>"Reset" was clicked: back to the default look (an item: its category's; a category: the starting one).</summary>
    public event Action? ResetClicked;
    readonly Panel sameHint =new() { Dock = DockStyle.Top, Height = 0, Visible = false }; // (gone: the look is always editable)
    /// <summary>It follows the look above it (category / item): the first change makes it its own.</summary>
    bool follows;
    /// <summary>The look settings (the main window reads <see cref="StyleEditor.Value"/> on <see cref="LookEdited"/>).</summary>
    public readonly StyleEditor Editor = new() { Dock = DockStyle.Top, Sketch = false, Compact = true };
    readonly Panel pickRow = new() { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 4, 0, 0) };
    readonly ComboBox pick = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    /// <summary>The 3D preview (the main window shows the item in it).</summary>
    public readonly ModelView View = new() { Dock = DockStyle.Fill, ShowParts = false };
    readonly Panel body = new() { Dock = DockStyle.Fill };
    readonly Label empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray, Padding = new Padding(24), UseMnemonic = false };
    bool loading;

    /// <summary>The "Same as ... / Own look" row (dev: the tour's pictures).</summary>
    internal Control SwitchRow => switchRow;

    /// <summary>The look settings were changed.</summary>
    public event Action? LookEdited;
    /// <summary>"Own look" (true) or "Same as ..." (false) was picked.</summary>
    public event Action<bool>? OwnSwitched;
    /// <summary>The tick was clicked: true = light it up.</summary>
    public event Action<bool>? TickSwitched;
    /// <summary>Another item was picked to preview a category's look on.</summary>
    public event Action<Entry>? PreviewPicked;

    public LookPanel()
    {
        tick.Font = new Font(Loc.UiFont, 9.5f, FontStyle.Bold);
        switchRow.Controls.AddRange(new Control[] { followText, backLink });
        pickRow.Controls.Add(pick);
        pickRow.Controls.Add(new Label { Text = Tr("Preview on:"), Dock = DockStyle.Left, Width = 80, TextAlign = ContentAlignment.MiddleLeft });
        Arrange();
        View.Settings = scroll;
        // the preview is the full 3D view (no separate big window); "Hide settings" gives it the whole pop-up
        View.MakeFull();
        View.AddToolAction(() => settingsHidden ? Tr("Show settings") : Tr("Hide settings"), () =>
        {
            settingsHidden = !settingsHidden;
            scroll.Visible = !settingsHidden;
            FitPreview();
        }, Tr("Give the whole pop-up to the 3D view (and back)"));
        Controls.Add(body);
        Controls.Add(empty);
        Padding = new Padding(8, 4, 4, 4);
        Load += (_, _) => Editor.FitHeight();
        Editor.HeightChanged += (_, _) => { scroll.PerformLayout(); WantedHeightChanged?.Invoke(); FitPreview(); }; // a card opened / closed
        body.Resize += (_, _) => FitPreview();

        Editor.Changed += (_, _) =>
        {
            if (loading) return;
            if (follows)
            {
                // the first change: it gets its own look (a copy of the one it used), then the change goes into it
                var edited = Editor.Value;
                OwnSwitched?.Invoke(true);
                loading = true;
                Editor.Value = edited;
                loading = false;
            }
            LookEdited?.Invoke();
        };
        backLink.LinkClicked += (_, _) => OwnSwitched?.Invoke(false);
        reset.Click += (_, _) => ResetClicked?.Invoke();
        new ToolTip { AutoPopDelay = 20000 }.SetToolTip(reset, Tr("Back to the default look: an item goes back to its category's look, a category to ESP Maker's starting look. Ctrl+Z undoes it."));
        tick.Click += (_, _) => TickSwitched?.Invoke(tick.CheckState != CheckState.Checked);
        pick.SelectedIndexChanged += (_, _) => { if (!loading && pick.SelectedItem is Entry e) PreviewPicked?.Invoke(e); };
    }

    readonly StayPanel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };

    /// <summary>Scrolls only when told to: a clicked control (a look picture, a button) gets the focus, and a plain
    /// panel then jumps to show it.</summary>
    sealed class StayPanel : Panel
    {
        protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
    }

    bool hasTick, hasSwitch, hasPick, settingsHidden;

    /// <summary>The height that shows everything without scrolling (the pop-up grows to it when the screen has room).</summary>
    public int WantedHeight =>
        titleRow.Height + sub.Height + (hasTick ? tick.Height : 0)
        + (hasSwitch ? switchRow.GetPreferredSize(new Size(Math.Max(Ui.S(100), header.ClientSize.Width), 0)).Height : 0)
        + Math.Max(Editor.Height, PreviewMin) + Padding.Vertical;

    /// <summary>The least height of the preview (it sits right of the settings, as tall as they are or more).</summary>
    static int PreviewMin => Ui.S(300);

    /// <summary>The settings column: as wide as the editor wants (look stack, 2026-11-15), the preview takes the rest;
    /// a narrow pop-up gives the settings all of it and the preview goes under them.</summary>
    void FitPreview()
    {
        header.Height = titleRow.Height + sub.Height + (hasSwitch ? switchRow.GetPreferredSize(new Size(Math.Max(Ui.S(100), header.ClientSize.Width), 0)).Height : 0);
        int w = Math.Min(StyleEditor.WantedWidth(Editor.Font), Math.Max(Ui.S(300), body.Width - Ui.S(320)));
        if (scroll.Width != w) scroll.Width = w;
    }

    /// <summary>Title, "Own look · use ... again" (across the top, over both columns).</summary>
    readonly Panel header = new() { Dock = DockStyle.Top };
    /// <summary>The preview and its "Preview on:" row, right of the settings.</summary>
    readonly Panel right = new() { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };

    /// <summary><see cref="WantedHeight"/> may have changed (a card opened / closed, another look shown).</summary>
    public event Action? WantedHeightChanged;

    void Arrange()
    {
        // docked in this order: the last added sits on top (or outermost)
        title.Dock = DockStyle.Fill;
        titleRow.Controls.Add(title);
        titleRow.Controls.Add(reset);
        titleRow.Controls.Add(detachGap);
        titleRow.Controls.Add(Detach);
        foreach (var c in new Control[] { switchRow, tick, sub, titleRow }) header.Controls.Add(c);
        scroll.Controls.Add(Editor);
        scroll.Controls.Add(sameHint);
        // settings on the left (they scroll), the preview fills the right (as in the look-stack mock-up)
        scroll.Dock = DockStyle.Left;
        View.Dock = DockStyle.Fill;
        pickRow.Dock = DockStyle.Top;
        right.Controls.Add(View);
        right.Controls.Add(pickRow);
        body.Controls.Add(right);
        body.Controls.Add(scroll);
        body.Controls.Add(header);
    }

    /// <summary>Nothing to edit here (a section or the overview is picked): just a hint.</summary>
    public void ShowEmpty(string text)
    {
        empty.Text = text;
        body.Visible = false;
        empty.Visible = true;
    }

    /// <param name="ticked">The tick to show; null = no tick (a category).</param>
    /// <param name="sameAs">"Same as Bosses" - the look it can follow; null = no switch (a category has its own look).</param>
    /// <param name="previewOn">Items to choose the preview from (a category); null = no choice.</param>
    /// <param name="pickOk">The pick-up ball can be set (Entry.CanPick).</param>
    public void ShowLook(string heading, string text, CheckState? ticked, string? sameAs, bool isOwn, Style look, bool icons3D, List<Entry>? previewOn,
                         bool pickOk = true)
    {
        loading = true;
        title.Text = heading;
        sub.Text = text;
        tick.Visible = false; // gone (user, 2026-11-09): changing the look selects it - MainForm.LookEdited
        if (ticked is CheckState t) tick.CheckState = t;
        switchRow.Visible = sameAs != null;
        // sameAs comes as "Same as Bosses" / "Same as their categories"
        var parent = sameAs == null ? "" : sameAs.StartsWith("Same as ") ? sameAs[8..] : sameAs;
        var whose = parent == "their categories" ? Tr("their categories' looks") : Tf("the {0} look", parent);
        follows = sameAs != null && !isOwn;
        followText.Text = follows ? Tf("Uses {0} - change anything to give it its own.", whose) : Tr("Own look ·");
        backLink.Text = Tf("use {0} again", whose);
        backLink.Visible = !follows;
        Editor.Icons3D = icons3D;
        Editor.PickOk = pickOk;
        Editor.Value = look;
        Editor.Enabled = true;
        pickRow.Visible = previewOn is { Count: > 0 };
        pick.Items.Clear();
        if (previewOn != null) foreach (var e in previewOn) pick.Items.Add(e);
        loading = false;
        empty.Visible = false;
        body.Visible = true;
        if (pick.Items.Count > 0) pick.SelectedIndex = 0; // shows the first in the preview
        hasTick = false; hasSwitch = sameAs != null; hasPick = pick.Items.Count > 0;
        WantedHeightChanged?.Invoke();
        FitPreview();
    }
}
