using System.Diagnostics;

namespace ItemFinder;

public sealed class MainForm : Form
{
    const int MaxShown = 3000;

    readonly Settings settings = Settings.Load();
    Project project = new();
    GameData? game;
    List<Category> cats = new();
    bool filling;
    /// <summary>Entries in other categories that share a model with an entry (e.g. Carrot in XP plants and in
    /// Plants) - they light up together in game, so they are ticked and unticked together.</summary>
    Dictionary<string, List<Entry>> twins = new();
    Dictionary<string, List<Entry>> entriesByModel = new();
    bool clearingSearch;
    /// <summary>Recipes opened in the list (their ingredients shown under them).</summary>
    readonly HashSet<string> expanded = new();
    /// <summary>Entries listed with their members under them in the item list (recipes with their ingredients).</summary>
    static bool HasRows(Entry e) => e.Group != null && e.Group.Any(g => g.Group != null); // a recipe (its rows are ingredients)

    /// <summary>The real entries under a group (a recipe's ingredients' plants and items).</summary>
    static IEnumerable<Entry> Leaves(Entry e) => e.Group == null ? new[] { e } : e.Group.SelectMany(Leaves);

    bool Full(Entry e) => e.Group != null ? e.Group.All(Full) : project.Selected.Contains(e.Id);

    /// <summary>Partly ticked: a recipe with only some ingredients ticked, an entry with some parts left out.</summary>
    bool IsPartial(Entry e) => e.Group != null
        ? IsTicked(e) && !Full(e)
        : project.Selected.Contains(e.Id) && project.OffParts.TryGetValue(e.Id, out var off) && off.Count > 0 && e.Models.Any(m => !off.Contains(m));

    /// <summary>Ticked: a group (recipe) counts as ticked while any of its members is.</summary>
    /// <summary>Rows unchecked while looking at Your picks or "Checked only": they stay listed (and open in the look
    /// panel) until another category or filter is picked - to compare with and without the look, or check again.</summary>
    readonly HashSet<string> keepShown = new();

    bool IsTicked(Entry e) => e.Group != null ? e.Group.Any(IsTicked) : project.Selected.Contains(e.Id);

    readonly TreeView catTree = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f), HideSelection = false, ShowLines = false, FullRowSelect = true, ItemHeight = 22 };
    static readonly Font SectionFont = new("Segoe UI", 10f, FontStyle.Bold);
    readonly Label catTitle = new() { UseMnemonic = false, Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
    readonly Label catDesc = new() { UseMnemonic = false, Dock = DockStyle.Top, Height = 66, ForeColor = Color.DimGray };
    // the look of what is picked (category, items or a part), on the right; changes apply at once
    readonly LookPanel inspector = new() { Dock = DockStyle.Fill };
    readonly Panel logPanel = new() { Dock = DockStyle.Bottom, Height = 130, Visible = false, Padding = new Padding(8, 0, 8, 4) };
    Button? buildButton;
    readonly ToolStripMenuItem keepItem = new("Keep other mods' changes");
    /// <summary>Which game install is used (Steam or Microsoft Store / Game Pass) - click to pick another.</summary>
    readonly ToolStripMenuItem gameItem = new("Game folder...");
    readonly TextBox search = new() { PlaceholderText = "Search all items...", Width = 190, Margin = new Padding(3, 6, 3, 3) };
    readonly CheckBox selectedOnly = new() { Text = "Selected only", AutoSize = true, Padding = new Padding(8, 4, 0, 0) };
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, ShowItemToolTips = true, HideSelection = false };
    readonly Label listInfo = new() { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.DimGray };
    readonly TextBox logBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 3, 0, 0), UseMnemonic = false };
    readonly Label installState = new() { Dock = DockStyle.Top, Height = 24, Padding = new Padding(4, 2, 0, 0), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), UseMnemonic = false };
    readonly List<Button> actionButtons = new();

    /// <summary>The picked category; null when a section (ITEMS / EVENTS / ENEMIES) is picked.</summary>
    Category? Current => catTree.SelectedNode?.Tag as Category;
    string? NodeTag => catTree.SelectedNode?.Tag as string;
    /// <summary>The picked section (ITEMS / EVENTS / ENEMIES), when a section node is picked.</summary>
    string? CurrentSection => NodeTag is string t && !t.StartsWith(Picks) ? t : null;
    /// <summary>The overview or one of its parts is picked.</summary>
    bool IsPicks => NodeTag?.StartsWith(Picks) == true;
    /// <summary>The overview part picked ("Enemies" under Your picks), null for the whole overview.</summary>
    string? PicksSection => NodeTag is string t && t.StartsWith(Picks + ":") ? t[(Picks.Length + 1)..] : null;

    readonly Panel infoBox = new() { Dock = DockStyle.Bottom, Height = 120 };
    /// <summary>Tag of the overview node at the top of the tree: everything ticked, from all categories.</summary>
    const string Picks = "\u0001picks";

    /// <summary>"1.0", "1.0.1"... (the project's Version).</summary>
    public static string AppVersion
    {
        get
        {
            var v = typeof(MainForm).Assembly.GetName().Version ?? new Version(1, 0);
            return v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
        }
    }

    /// <summary>The list row under a point, or null. While the list is being refilled ListView.HitTest can point at a
    /// row that isn't there yet and throws - nothing is under the mouse then.</summary>
    ListViewItem? RowAt(Point p)
    {
        if (filling) return null;
        try { return list.HitTest(p).Item; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>Messages from before the window was there (e.g. files moved to the data folder), shown in the log.</summary>
    public static readonly List<string> StartNotes = new();


    /// <summary>File name for the mod: no characters Windows forbids, no commas (the ini's archive list is
    /// comma-separated) and not starting like the game's own archives.</summary>
    public static string ArchiveName(string mod)
    {
        var n = string.Concat(mod.Trim().Split(Path.GetInvalidFileNameChars().Append(',').ToArray())).Trim().TrimEnd('.');
        if (n.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        if (n.Length == 0 || n.StartsWith("SeventySix", StringComparison.OrdinalIgnoreCase)) n = "ESPMaker" + n;
        return n + ".ba2";
    }

    public MainForm()
    {
        Text = "ESP Maker " + AppVersion;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { } // the exe's own icon
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 930);
        Width = Math.Min(1560, area.Width);
        Height = Math.Min(930, area.Height); // tall enough to list every category without scrolling
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        // left: categories, and what the picked one is
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var catsLabel = new Label { Text = "Categories", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        infoBox.Controls.Add(catDesc);
        infoBox.Controls.Add(catTitle);
        left.Controls.Add(catTree);
        left.Controls.Add(catsLabel);
        left.Controls.Add(infoBox);
        left.Resize += (_, _) => FitInfo();

        // middle: search + item list (click a row: its look on the right)
        var middle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false };
        var tickAll = Btn("Select all", (_, _) => TickShown(true));
        var untickAll = Btn("Deselect all", (_, _) => TickShown(false));
        // a ListView with checkboxes also flips the box on double-click; only a click on the box itself should
        bool doubleClick = false;
        list.DoubleClick += (_, _) =>
        {
            doubleClick = false;
            if (list.FocusedItem?.Tag is Entry en && Expandable(en)) ToggleRows(en); // open / close its parts or ingredients
        };
        list.MouseDown += (_, e) => doubleClick = e.Clicks > 1;
        list.ItemCheck += (_, e) => { if (doubleClick && !filling) e.NewValue = e.CurrentValue; };
        list.MouseUp += (_, _) => doubleClick = false;
        // partly ticked rows get a mixed box (Windows list boxes only know ticked / unticked)
        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        list.DrawItem += (_, e) => { };
        list.DrawSubItem += (_, e) =>
        {
            if (e.Item?.Tag is not Entry en) { e.DrawDefault = true; return; }
            bool selected = e.Item.Selected, lit = selected && list.Focused;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            if (e.ColumnIndex == 2)
            {
                // the look (an ingredient's: each of its looks in its own colour)
                using (var bg = new SolidBrush(selected ? (list.Focused ? SystemColors.Highlight : SystemColors.ControlLight) : list.BackColor)) e.Graphics.FillRectangle(bg, e.Bounds);
                int x = e.Bounds.Left + 4, right = e.Bounds.Right - 4;
                var looks = e.SubItem?.Tag as List<(string Text, Color Colour)> ?? new() { (e.SubItem?.Text ?? "", e.SubItem?.ForeColor ?? list.ForeColor) };
                for (int i = 0; i < looks.Count && x < right; i++)
                {
                    // an ingredient's looks (bush and item): each part in its own colour
                    var part = (i > 0 ? " / " : "") + looks[i].Text;
                    TextRenderer.DrawText(e.Graphics, part, list.Font, new Rectangle(x, e.Bounds.Top, right - x, e.Bounds.Height),
                                          lit ? SystemColors.HighlightText : looks[i].Colour, flags | TextFormatFlags.EndEllipsis);
                    x += TextRenderer.MeasureText(e.Graphics, part, list.Font, Size.Empty, flags).Width;
                }
                return;
            }
            var badge = PartsBadge(en);
            if (e.ColumnIndex != 0 || !IsPartial(en) && badge == null) { e.DrawDefault = true; return; }
            var row = e.Item.Bounds;
            var cell = new Rectangle(row.Left, row.Top, list.Columns[0].Width, row.Height);
            using (var bg = new SolidBrush(selected ? (list.Focused ? SystemColors.Highlight : SystemColors.ControlLight) : list.BackColor)) e.Graphics.FillRectangle(bg, cell);
            // the box drawn by hand: partly ticked rows get a mixed one (Windows list boxes only know ticked / unticked)
            var state = IsPartial(en) ? System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal
                      : e.Item.Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
            var box = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
            var label = e.Item.GetBounds(ItemBoundsPortion.Label);
            int bx = cell.Left + Math.Max(2, (label.Left - cell.Left - box.Width) / 2);
            CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(bx, row.Top + (row.Height - box.Height) / 2), state);
            var badgeRect = badge != null ? LinkRect(cell, badge) : new Rectangle(cell.Right, row.Top, 0, row.Height);
            var text = new Rectangle(label.Left, row.Top, badgeRect.Left - 4 - label.Left, row.Height);
            TextRenderer.DrawText(e.Graphics, e.Item.Text, e.Item.Font ?? list.Font, text, lit ? SystemColors.HighlightText : e.Item.ForeColor,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (badge != null) DrawLink(e.Graphics, badge, badgeRect, hoverLink == (e.ItemIndex, 0), lit);
        };
        // the links: a hand over them, underlined
        list.MouseMove += (_, e) =>
        {
            var hitItem = RowAt(e.Location);
            (int, int)? now = null;
            if (hitItem != null)
                if (LinkAt(hitItem, 0) is Rectangle r && r.Contains(e.Location)) now = (hitItem.Index, 0);
            if (now == hoverLink) return;
            var old = hoverLink;
            hoverLink = now;
            list.Cursor = now != null ? Cursors.Hand : Cursors.Default;
            foreach (var h in new[] { old, now })
                if (h is (int i, _) && i < list.Items.Count) list.Invalidate(list.Items[i].Bounds);
        };
        list.MouseLeave += (_, _) =>
        {
            if (hoverLink is (int i, _) && i < list.Items.Count) list.Invalidate(list.Items[i].Bounds);
            hoverLink = null;
            list.Cursor = Cursors.Default;
        };
        list.MouseClick += (_, e) =>
        {
            var hitItem = RowAt(e.Location);
            if (e.Button != MouseButtons.Left || hitItem?.Tag is not Entry en) return;
            // "8 parts ›": open / close the parts under the row
            if (LinkAt(hitItem, 0) is Rectangle badge && badge.Contains(e.Location)) { ToggleRows(en); return; }
            if (!Expandable(en)) return;
            var label = hitItem.GetBounds(ItemBoundsPortion.Label);
            if (e.X >= label.Left && e.X < label.Left + 20) ToggleRows(en); // the ► / ▼ in front of the name (the box is left of it)
        };
        list.KeyDown += (_, e) =>
        {
            if (list.FocusedItem?.Tag is not Entry en || !Expandable(en)) return;
            bool open = expanded.Contains(en.Id);
            if (e.KeyCode == Keys.Right && !open || e.KeyCode == Keys.Left && open || e.KeyCode == Keys.Enter) { ToggleRows(en); e.Handled = true; }
        };
        // right-click: copy / paste looks, tick / untick, back to the category's look
        var rowMenu = new ContextMenuStrip();
        var copyItem = new ToolStripMenuItem("Copy look", null, (_, _) => CopyLook()) { ShortcutKeyDisplayString = "Ctrl+C" };
        var pasteItem = new ToolStripMenuItem("Paste look", null, (_, _) => PasteLook()) { ShortcutKeyDisplayString = "Ctrl+V" };
        var tickItem = new ToolStripMenuItem("Select", null, (_, _) => TickSelected(true));
        var untickItem = new ToolStripMenuItem("Deselect", null, (_, _) => TickSelected(false));
        var sameItem = new ToolStripMenuItem("Use the category's look", null, (_, _) => { ResetColours(); ShowInspector(); });
        rowMenu.Items.AddRange(new ToolStripItem[] { copyItem, pasteItem, sameItem, new ToolStripSeparator(), tickItem, untickItem });
        list.ContextMenuStrip = rowMenu;
        rowMenu.Opening += (_, e) =>
        {
            var sel = SelectedEntries();
            if (sel.Count == 0) { e.Cancel = true; return; }
            string n = sel.Count > 1 ? $" {sel.Count} items" : "";
            copyItem.Enabled = sel.Count == 1 && sel[0].Group == null;
            copyItem.Text = copyItem.Enabled ? $"Copy look of {sel[0].Name}" : "Copy look (click one item)";
            pasteItem.Enabled = copiedLook != null;
            pasteItem.Text = copiedLook != null ? $"Paste look of {copiedFrom}{(sel.Count > 1 ? " onto" + n : "")}" : "Paste look (copy one first)";
            sameItem.Enabled = sel.SelectMany(Leaves).Any(x => project.Overrides.ContainsKey(x.Id));
            tickItem.Text = "Select" + n; untickItem.Text = "Deselect" + n;
            tickItem.Enabled = sel.Any(x => !IsTicked(x) || IsPartial(x));
            untickItem.Enabled = sel.Any(IsTicked);
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C) { CopyLook(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.V) { PasteLook(); e.Handled = true; }
        };
        // the picked rows' look in the panel (after the selection settles: a click sends several events)
        var pickTimer = new System.Windows.Forms.Timer { Interval = 80 };
        pickTimer.Tick += (_, _) => { pickTimer.Stop(); ShowInspector(); };
        list.SelectedIndexChanged += (_, _) => { if (!filling) { pickTimer.Stop(); pickTimer.Start(); } };
        top.Controls.AddRange(new Control[] { search, selectedOnly, tickAll, untickAll });
        list.Columns.Add("Item", 300);
        list.Columns.Add("Category", 150);
        list.Columns.Add("Look", 230);
        list.Columns.Add("Details", 360);
        middle.Controls.Add(list);
        middle.Controls.Add(listInfo);
        middle.Controls.Add(top);

        // right: the look of whatever is picked
        inspector.LookEdited += LookEdited;
        inspector.OwnSwitched += OwnSwitched;
        inspector.TickSwitched += TickSwitched;
        inspector.PreviewPicked += e => { if (game != null) inspector.View.Show(game, e, inspector.Editor.Value, project.KeepOtherMods); };

        var inner = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        inner.Panel1.Controls.Add(middle);
        inner.Panel2.Controls.Add(inspector);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(inner);

        // bottom: whether the game shows the current choices, and the one button that makes it so
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 6, 10, 6) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        var install = buildButton = new Button { Text = "Build && save", AutoSize = true, Height = 32, Padding = new Padding(14, 0, 14, 0), Font = new Font(Font, FontStyle.Bold) };
        install.Click += (_, _) => BuildSave();
        var import = new Button { Text = "Import .ba2...", AutoSize = true, Height = 32 };
        import.Click += (_, _) => ImportBa2();
        var more = new Button { Text = "More ▾", AutoSize = true, Height = 32 };
        var moreMenu = new ContextMenuStrip();
        gameItem.Click += (_, _) => PickGameFolder();
        keepItem.CheckOnClick = true;
        keepItem.CheckedChanged += (_, _) => { if (!filling) { project.KeepOtherMods = keepItem.Checked; Save(); } };
        keepItem.ToolTipText = "On: if another mod (e.g. another ESP mod) already changed an item's model, keep its changes and add the glow/beam on top.\n" +
                               "Off: start from the game's original model - for those items this mod's look replaces the other mod's.";
        moreMenu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Add presets...", null, (_, _) => RunPresets()),
            new ToolStripMenuItem("Show the tour...", null, (_, _) => { using var t = new Tutorial(Font); t.ShowDialog(this); }),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Uninstall from the game...", null, (_, _) => Uninstall()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Start empty...", null, (_, _) => StartEmpty()),
            new ToolStripMenuItem("Save project...", null, (_, _) => SaveSetup()),
            new ToolStripMenuItem("Open project...", null, (_, _) => LoadSetup()),
            new ToolStripSeparator(),
            keepItem,
            gameItem,
            new ToolStripMenuItem("Open the output folder", null, (_, _) => { Directory.CreateDirectory(OutputDir); Process.Start("explorer.exe", OutputDir); }),
        });
        more.Click += (_, _) => moreMenu.Show(more, new Point(0, -moreMenu.PreferredSize.Height));
        var logLink = new LinkLabel { Text = "Show log", AutoSize = true, Padding = new Padding(0, 8, 12, 0) };
        logLink.LinkClicked += (_, _) => { logPanel.Visible = !logPanel.Visible; logLink.Text = logPanel.Visible ? "Hide log" : "Show log"; };
        logPanel.VisibleChanged += (_, _) => logLink.Text = logPanel.Visible ? "Hide log" : "Show log";
        actions.Controls.AddRange(new Control[] { install, import, more, logLink });
        installState.Dock = DockStyle.Fill;
        installState.TextAlign = ContentAlignment.MiddleLeft;
        bottom.Controls.Add(installState);
        bottom.Controls.Add(actions);
        logPanel.Controls.Add(logBox);
        actionButtons.AddRange(new[] { install, import, more, tickAll, untickAll });

        Controls.Add(split);
        Controls.Add(logPanel);
        Controls.Add(bottom);
        Controls.Add(status);
        Load += (_, _) => { RestoreWindow(split, inner); _ = LoadGameAsync(); };
        FormClosing += (_, _) => RememberWindow(split, inner);

        // remember which sections are folded
        catTree.AfterCollapse += (_, e) => { if (e.Node?.Tag is string s && !settings.Collapsed.Contains(s)) { settings.Collapsed.Add(s); settings.Save(); } };
        catTree.AfterExpand += (_, e) => { if (e.Node?.Tag is string s && settings.Collapsed.Remove(s)) settings.Save(); };
        var searchTimer = new System.Windows.Forms.Timer { Interval = 250 };
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); FillList(); ShowInspector(); };
        search.TextChanged += (_, _) => { searchTimer.Stop(); if (!clearingSearch) searchTimer.Start(); };
        // picking a category ends a search: back to that category's list (also when clicking the one already picked)
        void ClearSearch() { clearingSearch = true; search.Text = ""; clearingSearch = false; searchTimer.Stop(); }
        catTree.AfterSelect += (_, _) => { keepShown.Clear(); ClearSearch(); ShowCategory(); FillList(); ShowInspector(); };
        catTree.NodeMouseClick += (_, e) =>
        {
            if (e.Node != catTree.SelectedNode) return; // another node: AfterSelect does it
            // the category already picked: back to its own look (and its list, if a search was on)
            if (search.Text.Length > 0) { ClearSearch(); FillList(); }
            list.SelectedItems.Clear();
            ShowInspector();
        };
        selectedOnly.CheckedChanged += (_, _) => { keepShown.Clear(); FillList(); };
        list.ItemChecked += (_, e) =>
        {
            if (filling) return;
            if (e.Item.Tag is PartRow pr)
            {
                // a part's box: that part in or out (the item follows: ticked while any part is)
                SetPartOn(pr, e.Item.Checked);
                Save();
                RefreshCategoryCounts();
                BeginInvoke(() => { FillList(keepScroll: true); ShowInspector(); });
                return;
            }
            if (e.Item.Tag is not Entry en) return;
            TickWithTwins(en, e.Item.Checked);
            // ticking a partly ticked thing (after an untick) ticks all of it: all ingredients, all parts
            if (e.Item.Checked && project.OffParts.Remove(en.Id)) BeginInvoke(() => FillList(keepScroll: true));
            else if (!e.Item.Checked && project.OffParts.ContainsKey(en.Id)) BeginInvoke(() => FillList(keepScroll: true)); // its "k/N parts" goes
            SyncParts(en);
            // recipes on screen show whether any ingredient is ticked, parts rows follow their item: refresh them
            if (list.Items.Cast<ListViewItem>().Any(li => li.Tag is PartRow || li.Tag is Entry x && x.Group != null)) BeginInvoke(() => FillList(keepScroll: true));
            Save();
            RefreshCategoryCounts();
            // other rows on screen (search results) may show a linked twin: update their boxes after this event
            BeginInvoke(SyncChecks);
            if (inspItems.Contains(en)) BeginInvoke(ShowInspector);
            // the overview drops what was unticked and updates its breakdown
            if (IsPicks) BeginInvoke(() => { ShowCategory(); FillList(keepScroll: true); });
        };
        SetBusy(true);
    }

    Button Btn(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(4, 2, 4, 2) };
        b.Click += click;
        return b;
    }

    static string OutputDir => AppPaths.File("output");

    void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(s)); return; }
        logBox.AppendText(s + Environment.NewLine);
        if (!logPanel.Visible) status.Text = s.Length > 200 ? s[..200] + "..." : s;
    }

    void Status(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Status(s)); return; }
        status.Text = s;
    }

    void SetBusy(bool busy)
    {
        foreach (var b in actionButtons) b.Enabled = !busy;
        UseWaitCursor = busy;
    }

    void Save()
    {
        try { Json.Write(Project.AutosavePath, project); } catch { }
    }

    /// <summary>The ESP Maker mods in the game's load order (main .ba2 names), found by what's in them.</summary>
    List<string> OwnMods() => (game?.OwnInstalled ?? new()).Where(n => !n.EndsWith(" - Textures.ba2", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n).ToList();

    /// <summary>A mod's files in Data: the .ba2 and its texture archive.</summary>
    static List<string> ModFiles(string modName) => new() { ArchiveName(modName), Path.GetFileName(Builder.TextureArchive(ArchiveName(modName))) };

    void ShowInstallState()
    {
        var mods = OwnMods();
        installState.Text = mods.Count == 0 ? "Select what you want to find, then click \"Build & save\"." : "In the game: " + string.Join(", ", mods);
        installState.ForeColor = mods.Count == 0 ? Color.DimGray : Color.SeaGreen;
    }

    /// <summary>Another game install (e.g. the Microsoft Store version next to Steam's): remembered, then the app
    /// restarts to read it.</summary>
    void PickGameFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick the Fallout 76 folder", UseDescriptionForTitle = true,
            InitialDirectory = game?.GameFolder ?? "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (GameData.GameFolderIn(dlg.SelectedPath) is not string picked)
        {
            MessageBox.Show(this, "That folder has no Fallout76.exe or Project76_GamePass.exe.", Text);
            return;
        }
        if (game != null && string.Equals(picked, game.GameFolder, StringComparison.OrdinalIgnoreCase)) return;
        Save();
        settings.GameFolder = picked;
        settings.Save();
        MessageBox.Show(this, "ESP Maker restarts now to read that game.", Text);
        Application.Restart();
        Environment.Exit(0);
    }

    /// <summary>Setups from when the app kept a list of mods (before 1.2): they go with the saved projects.</summary>
    static void MoveOldMods()
    {
        var old = AppPaths.File("mods");
        if (!Directory.Exists(old)) return;
        try
        {
            var to = Path.Combine(Directory.CreateDirectory(OutputDir).FullName, "my mods");
            Directory.Move(old, to);
            StartNotes.Add($"Your mods' setups moved to {to}: open them with More > Open project, or import the .ba2.");
        }
        catch (Exception ex) { StartNotes.Add($"Couldn't move {old}: {ex.Message}"); }
    }
    /// <summary>A setup becomes the current one: checked against the game data, the window refreshed.</summary>
    void UseProject(Project p)
    {
        project = p;
        keepShown.Clear();
        var all = cats.SelectMany(c => c.Entries).ToList();
        MigrateSetup(all);
        TickTwins(all);
        MatchLinkedParts();
        UpdateMoreMenu();
        Save();
        RefreshCategoryCounts();
        ShowCategory();
        FillList();
        ShowInspector();
    }


    /// <summary>Older setups: show twins of ticked entries as ticked too (they light up anyway - same model). Not for
    /// entries with parts left out: an imported mod ticks those part by part, and a shared model must not pull in a whole
    /// outfit list.</summary>
    void TickTwins(List<Entry> all)
    {
        foreach (var e in all.Where(e => project.Selected.Contains(e.Id) && !project.OffParts.ContainsKey(e.Id)).ToList()) SetTicked(e, true);
    }

    async Task LoadGameAsync()
    {
        try
        {
            if (File.Exists(Project.AutosavePath)) project = Json.Read<Project>(Project.AutosavePath) ?? new();
        }
        catch { project = new(); }
        MoveOldMods();
        UpdateMoreMenu();
        var folder = GameData.FindGameFolder(settings.GameFolder);
        if (folder == null)
        {
            using var dlg = new FolderBrowserDialog { Description = "Where is Fallout 76 installed?", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK && GameData.GameFolderIn(dlg.SelectedPath) is string picked)
            {
                settings.GameFolder = folder = picked;
                settings.Save();
            }
            else { Status("Fallout 76 not found."); return; }
        }
        try
        {
            var exclude = project.ImportedFrom.ToList();
            (game, cats) = await Task.Run(() =>
            {
                var g = GameData.Load(folder, exclude, Status);
                var idx = GameIndex.Load(g, Status);
                Status("Sorting items into categories...");
                bool Exists(string m) => g.Find(@"meshes\" + m) != null;
                return (g, EventCatalog.BuildAll(idx, Exists, g.AllPaths));
            });
            // things the game never places in the world (only crafted or looted, like Fish Bits) never show their
            // model there - a dropped item becomes a loot bag - so they can't be highlighted: leave them out.
            // Not event things: the event scripts spawn those (Slasher radio, gravestones) so they show 0 placed spots
            int dropped = 0;
            foreach (var c in cats.Where(c => c.Section is "ITEMS" or "JUNK")) dropped += c.Entries.RemoveAll(e => e.Spots == 0);
            cats.RemoveAll(c => c.Entries.Count == 0);
            if (dropped > 0) Log($"{dropped:N0} things the game never places in the world (only crafted or looted) are left out - they can't be highlighted.");
            var kept = cats.SelectMany(c => c.Entries).ToHashSet();
            foreach (var c in cats.Where(c => c.Groups))
            {
                foreach (var r in c.Entries)
                {
                    foreach (var ing in r.Group ?? new()) ing.Group?.RemoveAll(g => !kept.Contains(g));
                    r.Group?.RemoveAll(ing => ing.Group is { Count: 0 });
                }
                c.Entries.RemoveAll(r => r.Group is not { Count: > 0 });
            }
            var all = cats.SelectMany(c => c.Entries).ToList();
            var byModel = all.SelectMany(e => e.Models.Select(m => (m, e))).GroupBy(x => x.m).ToDictionary(g => g.Key, g => g.Select(x => x.e).ToList());
            // recipes share ingredients (carrots) with each other and with Plants: they don't tick each other
            // junk under several components (Duct Tape: adhesive, cloth): each component category ticks on its own
            static bool JunkCat(Entry x) => x.Category.StartsWith("Junk: ");
            twins = all.ToDictionary(e => e.Id, e => e.NoLink ? new List<Entry>()
                : e.Models.SelectMany(m => byModel[m]).Where(o => o != e && !o.NoLink && !(JunkCat(e) && JunkCat(o)) && !(e.LinkParts && o.LinkParts)).Distinct().ToList());
            entriesByModel = byModel;
            MigrateSetup(all);
            TickTwins(all);
            MatchLinkedParts();
            Save();
            catTree.BeginUpdate();
            var picksNode = new TreeNode { Tag = Picks, NodeFont = SectionFont };
            foreach (var sec in cats.Select(c => c.Section).Distinct()) picksNode.Nodes.Add(new TreeNode { Tag = Picks + ":" + sec });
            catTree.Nodes.Add(picksNode);
            if (!settings.Collapsed.Contains(Picks)) picksNode.Expand();
            foreach (var g in cats.GroupBy(c => c.Section))
            {
                var sec = new TreeNode { Tag = g.Key, NodeFont = SectionFont };
                foreach (var c in g) sec.Nodes.Add(new TreeNode { Tag = c });
                catTree.Nodes.Add(sec);
                if (!settings.Collapsed.Contains(g.Key)) sec.Expand();
            }
            RefreshCategoryCounts();
            catTree.EndUpdate();
            // start on Your picks; with nothing ticked yet (a new user) on the first category instead
            catTree.SelectedNode = project.Selected.Count > 0 ? picksNode
                : catTree.Nodes[1].IsExpanded ? catTree.Nodes[1].Nodes[0] : catTree.Nodes[1];
            int ticked = cats.Sum(TickedIn);
            Status($"Ready - {cats.Sum(c => c.Entries.Count):N0} things to choose from ({game.Edition}: {game.GameFolder}).");
            Log($"Fallout 76 ({game.Edition}) in {game.GameFolder}; mods are listed in {game.IniPath}.");
            if (ticked > 0) Log($"Your last setup was loaded: {ticked} item(s) selected.");
            Log($"ESP Maker {AppVersion}");
            foreach (var note in StartNotes) Log(note);
            gameItem.Text = $"Game: {game.Edition} - change...";
            ShowInstallState();
            if (game.OwnInstalled.Count > 0) Log($"Installed now: {string.Join(", ", game.OwnInstalled)} (not used as a starting point - rebuilds start from the original models).");
            SetBusy(false);
            // a new user: the tour, once
            if (!settings.TourSeen && project.Selected.Count == 0)
            {
                settings.TourSeen = true;
                settings.Save();
                BeginInvoke(() => { using (var t = new Tutorial(Font)) t.ShowDialog(this); RunPresets(); }); // then the presets to start from
            }
        }
        catch (Exception ex) { Status("Error: " + ex.Message); Log(ex.ToString()); }
    }

    int TickedIn(Category c) => c.Entries.Count(IsTicked);

    /// <summary>Node texts with tick counts. Changing a node's text doesn't change the selection, so this is
    /// safe while a tick event is running.</summary>
    void RefreshCategoryCounts()
    {
        foreach (TreeNode sec in catTree.Nodes)
        {
            if (sec.Tag is Picks)
            {
                var ticked = Ticked();
                var picksText = $"★ Your picks  ({ticked.Count})      ";
                if (sec.Text != picksText) sec.Text = picksText;
                foreach (TreeNode part in sec.Nodes)
                {
                    var s = ((string)part.Tag)[(Picks.Length + 1)..];
                    var partText = $"{Title(s)}  ({ticked.Count(e => CatOf(e).Section == s)})";
                    if (part.Text != partText) part.Text = partText;
                }
                continue;
            }
            int total = 0, all = 0;
            foreach (TreeNode n in sec.Nodes)
            {
                if (n.Tag is not Category c) continue;
                int t = TickedIn(c);
                total += t;
                all += c.Entries.Count;
                // categories with something selected stand out (bold green, a dot) in the long list
                var text = t > 0 ? $"● {c.Name}  ({t} / {c.Entries.Count})      " : $"{c.Name}  ({t} / {c.Entries.Count})"; // ticked / everything in it
                if (n.Text != text) n.Text = text;
                MarkSelected(n, t > 0, null);
            }
            var name = sec.Tag as string ?? "";
            // trailing spaces: a bold node font is wider than the tree font and would be clipped
            var secText = $"{Title(name)}  ({total} of {all:N0} selected)      ";
            if (sec.Text != secText) sec.Text = secText;
            MarkSelected(sec, total > 0, SectionFont);
        }
    }

    static readonly Color SelectedCatColor = Color.FromArgb(20, 125, 60);

    static void MarkSelected(TreeNode n, bool on, Font? baseFont)
    {
        var colour = on ? SelectedCatColor : Color.Empty;
        var font = on ? SectionFont : baseFont;
        if (n.ForeColor != colour) n.ForeColor = colour;
        if (n.NodeFont != font) n.NodeFont = font;
    }

    /// <summary>Everything ticked, each thing once: linked twins in later categories (the same bobblehead under
    /// Aid &amp; food and Junk &amp; misc) are left out - the shown entry says "also in".</summary>
    /// <summary>Your picks, plus the rows unchecked there since it was opened (in list order).</summary>
    List<Entry> PicksWithKept()
    {
        var ids = Ticked().Select(e => e.Id).ToHashSet();
        return cats.SelectMany(c => c.Entries).Where(e => ids.Contains(e.Id) || keepShown.Contains(e.Id) && !IsTicked(e)).ToList();
    }

    List<Entry> Ticked()
    {
        var shown = new List<Entry>();
        var covered = new HashSet<string>();
        foreach (var e in cats.SelectMany(c => c.Entries).Where(e => project.Selected.Contains(e.Id)))
        {
            if (covered.Contains(e.Id)) continue;
            shown.Add(e);
            foreach (var t in twins.GetValueOrDefault(e.Id, new())) covered.Add(t.Id);
        }
        return shown;
    }

    static string Title(string section) => section.Length == 0 ? "" : section[..1] + section[1..].ToLowerInvariant();

    void ShowCategory()
    {
        catDesc.Height = 66;
        if (IsPicks)
        {
            var part = PicksSection;
            var ticked = Ticked().Where(e => part == null || CatOf(e).Section == part).ToList();
            catTitle.Text = part == null ? $"Your picks ({ticked.Count})" : $"Your picks: {Title(part)} ({ticked.Count})";
            catDesc.Text = ticked.Count == 0
                ? "Nothing selected yet. Pick a category above and select what you want to highlight."
                : "Everything you selected is listed on the right - deselect there to remove it.";
            FitInfo();
            return;
        }
        if (CurrentSection is string s)
        {
            catTitle.Text = Title(s);
            catDesc.Text = $"Everything in {Title(s)}. Pick one of its categories to see and set that category's look.";
            FitInfo();
            return;
        }
        if (Current is not Category c) return;
        catTitle.Text = c.Name;
        catDesc.Text = c.Description;
        FitInfo();
    }

    Style LookOf(Category c) => project.CategoryStyles.GetValueOrDefault(c.Name) ?? c.DefaultStyle;
    /// <summary>The info box under the tree only takes the room its text needs.</summary>
    void FitInfo()
    {
        int w = Math.Max(100, infoBox.ClientSize.Width);
        catDesc.Height = Math.Max(catDesc.Height, TextRenderer.MeasureText(catDesc.Text, catDesc.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + 6);
        infoBox.Height = catTitle.Height + catDesc.Height + 8;
    }

    /// <summary>Back where the window was last time (if that place is still on a screen), same size and column width.</summary>
    void RestoreWindow(SplitContainer split, SplitContainer inner)
    {
        if (settings.Layout < 2)
        {
            // the window was sized for the old two-pane layout: once, open wider for the look panel
            var area = Screen.FromControl(this).WorkingArea;
            if (settings.Window is { Length: 4 } old) settings.Window = new[] { old[0], old[1], Math.Max(old[2], Math.Min(1560, area.Width)), old[3] };
            if (settings.Window is { Length: 4 } wv && wv[0] + wv[2] > area.Right) wv[0] = Math.Max(area.Left, area.Right - wv[2]);
            settings.LeftWidth = Math.Min(settings.LeftWidth, 300);
            settings.Layout = 2;
        }
        if (settings.Window is { Length: 4 } w)
        {
            var r = new Rectangle(w[0], w[1], Math.Max(900, w[2]), Math.Max(600, w[3]));
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r))) { StartPosition = FormStartPosition.Manual; Bounds = r; }
        }
        if (settings.Maximized) WindowState = FormWindowState.Maximized;
        split.SplitterDistance = Math.Clamp(settings.LeftWidth, 220, Math.Max(220, split.Width - 300));
        inner.SplitterDistance = Math.Clamp(inner.Width - settings.LookWidth, 300, Math.Max(300, inner.Width - 320));
    }

    void RememberWindow(SplitContainer split, SplitContainer inner)
    {
        settings.LookWidth = inner.Width - inner.SplitterDistance;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.Window = new[] { b.X, b.Y, b.Width, b.Height };
        settings.Maximized = WindowState == FormWindowState.Maximized;
        settings.LeftWidth = split.SplitterDistance;
        settings.Save();
    }

    Category CatOf(Entry e) => cats.First(c => c.Name == e.Category);

    /// <param name="keepScroll">Stay at the same place in the list (refresh after a tick change).</param>
    void FillList(bool keepScroll = false)
    {
        if (cats.Count == 0) return;
        int top = keepScroll ? list.TopItem?.Index ?? 0 : 0;
        var q = search.Text.Trim();
        IEnumerable<Entry> src = q.Length > 0 || selectedOnly.Checked ? cats.SelectMany(c => c.Entries)
            : IsPicks ? PicksWithKept().Where(e => PicksSection == null || CatOf(e).Section == PicksSection)
            : CurrentSection is string sec ? cats.Where(c => c.Section == sec).SelectMany(c => c.Entries)
            : Current?.Entries ?? new();
        if (selectedOnly.Checked) src = src.Where(e => project.Selected.Contains(e.Id) || keepShown.Contains(e.Id));
        if (q.Length > 0)
        {
            var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            src = src.Where(e => words.All(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                                                 e.ItemNames.Any(n => n.Contains(w, StringComparison.OrdinalIgnoreCase))));
        }
        var all = src.ToList();
        var picked = list.SelectedItems.Cast<ListViewItem>().Select(li => li.Tag).ToList();
        filling = true;
        list.BeginUpdate();
        list.Items.Clear();
        ListViewItem AddRow(Entry e, string indent)
        {
            var li = new ListViewItem(indent + e.Name) { Tag = e, Checked = IsTicked(e), UseItemStyleForSubItems = false };
            li.SubItems.Add(e.Category);
            li.SubItems.Add("");
            var tw = twins.GetValueOrDefault(e.Id, new());
            var also = string.Join(", ", tw.Select(t => t.Category).Distinct());
            var where = e.Spots switch { <= 0 => "", 1 => "1 spot in the world", _ => $"{e.Spots:N0} spots in the world" };
            var offParts = IsTicked(e) ? project.OffParts.GetValueOrDefault(e.Id)?.Count ?? 0 : 0; // unticked: nothing lights up
            var ownParts = project.PartLooks.GetValueOrDefault(e.Id)?.Count ?? 0;
            var partsNote = string.Join(" · ", new[] { offParts > 0 ? $"{e.Models.Count - offParts} of {e.Models.Count} parts light up" : "",
                                                       ownParts > 0 ? $"{ownParts} part{(ownParts > 1 ? "s" : "")} with own look" : "" }.Where(x => x.Length > 0));
            // found through a thing sharing its model (Live & Love 8 is on the same cover as 40 other magazines): say which
            var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var via = words.Length == 0 || words.All(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase)) ? null
                : e.ItemNames.FirstOrDefault(n => words.All(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)));
            var matchNote = via != null && e.Group == null ? $"matches: {via} (same model)" : "";
            var detail = string.Join(" · ", new[] { matchNote, where, e.Detail, partsNote, tw.Count > 0 ? "also in: " + also : "" }.Where(x => x.Length > 0));
            li.SubItems.Add(detail);
            li.ToolTipText = e.Group != null ? e.Name + " - ingredients:\n" + string.Join("\n", e.Group.Select(g => $"{g.Name} ({g.Category})"))
                : (e.ItemNames.Count > 1
                    ? "Lights up together (same model):\n" + string.Join("\n", e.ItemNames.Take(25)) + (e.ItemNames.Count > 25 ? $"\n... and {e.ItemNames.Count - 25} more" : "")
                    : e.Name)
                  + (tw.Count > 0 ? "\n\nChecked together with (same model):\n" + string.Join("\n", tw.Select(t => $"{t.Name} ({t.Category})")) : "");
            SetColourCell(li);
            list.Items.Add(li);
            return li;
        }
        foreach (var e in all.Take(MaxShown))
        {
            var li = AddRow(e, "");
            if (!Expandable(e)) continue;
            // a recipe (its ingredients, the same entries as in their own categories) or a thing made of parts: ► opens it
            bool open = expanded.Contains(e.Id);
            li.Text = (open ? "▼ " : "► ") + e.Name;
            if (!open) continue;
            if (HasRows(e)) { foreach (var g in e.Group!) AddRow(g, "          "); continue; }
            // parts with their own look first, then the beam-carrying main part, then by name
            var main = e.BeamModels?.FirstOrDefault(e.Models.Contains);
            var looks = project.PartLooks.GetValueOrDefault(e.Id);
            foreach (var m in e.Models.OrderByDescending(m => looks?.ContainsKey(m) == true).ThenByDescending(m => m == main)
                                      .ThenBy(m => e.PartNames?.GetValueOrDefault(m) ?? m, StringComparer.OrdinalIgnoreCase))
            {
                var pr = new PartRow(e, m);
                var pli = new ListViewItem("          " + (looks?.ContainsKey(m) == true ? "◆ " : "") + PartLabel(pr)) { Tag = pr, Checked = PartOn(pr), UseItemStyleForSubItems = false };
                pli.SubItems.Add("");
                pli.SubItems.Add("");
                var others = SharedWith(e)(m);
                pli.SubItems.Add(m + (others.Count > 0 ? " · also part of: " + string.Join(", ", others) : ""));
                pli.ToolTipText = PartLabel(pr) + "\n" + m;
                SetColourCell(pli);
                list.Items.Add(pli);
            }
        }
        foreach (ListViewItem li in list.Items) if (picked.Contains(li.Tag)) li.Selected = true;
        // the category column only says something when the list mixes categories (search, Your picks, a whole section)
        list.Columns[1].Width = q.Length > 0 || selectedOnly.Checked || IsPicks || CurrentSection != null ? 150 : 0;
        list.EndUpdate();
        if (keepScroll && list.Items.Count > 0) list.TopItem = list.Items[Math.Min(top, list.Items.Count - 1)];
        filling = false;
        listInfo.Text = (all.Count > MaxShown ? $"Showing the first {MaxShown:N0} of {all.Count:N0} - type in the search box to narrow it down."
                      : $"{all.Count:N0} shown. Hover an item to see what else shares its model.");
    }

    void SetColourCell(ListViewItem li)
    {
        if (li.Tag is PartRow pr)
        {
            // a part: its own look, or the item's (greyed: it just follows)
            var own = project.PartLooks.GetValueOrDefault(pr.Entry.Id)?.GetValueOrDefault(pr.Model);
            var pc = li.SubItems[2];
            if (own != null) (pc.Text, pc.ForeColor) = LookText(own, pr.Entry, true);
            else { pc.Text = "as the item"; pc.ForeColor = SystemColors.GrayText; }
            li.ForeColor = PartOn(pr) ? list.ForeColor : SystemColors.GrayText;
            return;
        }
        if (li.Tag is not Entry e) return;
        var cell = li.SubItems[2];
        if (e.Group != null && !HasRows(e))
        {
            // an ingredient (bush + item): the looks of its entries - one if they agree, else both
            var looks = e.Group.Select(LookCell).Distinct().ToList();
            cell.Text = looks.Count == 1 ? looks[0].Text : string.Join(" / ", looks.Select(l => l.Text));
            cell.ForeColor = looks.Count > 0 ? looks[0].Colour : SystemColors.GrayText;
            cell.Tag = looks.Count > 1 ? looks : null; // drawn in parts, each in its own colour
            return;
        }
        if (e.Group != null) { cell.Text = ""; return; } // a recipe: its ingredients show theirs
        (cell.Text, cell.ForeColor) = LookCell(e);
    }

    /// <summary>An entry's look in the Look column: "■ glow + beam 14 m" in its colour ("own: " for its own look).</summary>
    (string Text, Color Colour) LookCell(Entry e) => LookText(project.StyleOf(e, CatOf(e)), e, project.Overrides.ContainsKey(e.Id));

    (string Text, Color Colour) LookText(Style st, Entry e, bool own)
    {
        var parts = new List<string>();
        if (st.Glow) parts.Add("glow");
        if (st.Beam) parts.Add(st.BeamArrow ? "arrow" : $"beam {Math.Round(st.BeamLength / StyleEditor.UnitsPerMetre)} m");
        if (Icons.Get(st.Icon) is Icons.Icon ic) parts.Add((e.NoIcon || st.Icon3D ? "3D " : "") + ic.Name.ToLowerInvariant() + " icon");
        else if (Models3D.Get(st.Icon) is Model3D m3) parts.Add(m3.Name.ToLowerInvariant() + " model");
        if (st.Brighten) parts.Add("bright colours");
        return ((own ? "own: " : "") + (parts.Count > 0 ? "■ " + string.Join(" + ", parts) : "nothing"), Readable(Materials.Parse(st.Glow ? st.GlowColor : st.BeamColor)));
    }

    /// <summary>The look colour as text on the white list: very light colours (pale pink, light cyan...) are
    /// darkened in the same hue until they stand out.</summary>
    static Color Readable(Color c)
    {
        static double Lum(Color x) => (0.299 * x.R + 0.587 * x.G + 0.114 * x.B) / 255;
        for (int i = 0; i < 6 && Lum(c) > 0.62; i++)
            c = Color.FromArgb((int)(c.R * 0.85), (int)(c.G * 0.85), (int)(c.B * 0.85));
        return c;
    }

    void RefreshColours()
    {
        list.BeginUpdate();
        foreach (ListViewItem li in list.Items) SetColourCell(li);
        list.EndUpdate();
    }

    /// <summary>Setups saved by older versions: "Notes, plans &amp; books" became "Plans" (its "All plans" = both plan
    /// kinds, its look moves over); ticks of things no longer offered (removed categories) are dropped.</summary>
    /// <summary>The 3D shapes (extruded icons) that have a 3D model counterpart; the others keep working as they were.</summary>
    static readonly Dictionary<string, string> ShapeModels = new()
    {
        ["star"] = "model:star", ["skull"] = "model:skull", ["heart"] = "model:heart", ["crown"] = "model:crown", ["ghost"] = "model:ghost",
        ["pumpkin"] = "model:pumpkin", ["mushroom"] = "model:mushroom", ["cross"] = "model:cross", ["question"] = "model:question",
        ["bobblehead"] = "model:bobblehead", ["diamond"] = "model:gem", ["exclaim"] = "model:exclamation", ["warning"] = "model:exclamation",
        ["herb"] = "model:leaf", ["leaf"] = "model:leaf", ["seedling"] = "model:leaf", ["flower"] = "model:sakura", ["dollar"] = "model:cap",
        ["moneybag"] = "model:cap", ["radiation"] = "model:radiation", ["gear"] = "model:vaultdoor",
    };

    void MigrateSetup(List<Entry> all)
    {
        project.Upgrade();
        // the 3D shapes tab is gone: a 3D shape (chosen as one, or any icon on creatures / worn outfits, which only showed
        // 3D shapes) becomes the matching 3D model
        void ToModel(Style st, bool creature)
        {
            if ((st.Icon3D || creature) && ShapeModels.TryGetValue(st.Icon, out var model)) { st.Icon = model; st.Icon3D = false; }
        }
        var byId = all.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        const string oldPlans = "Notes, plans & books";
        if (project.Selected.Remove($"name:{oldPlans}:all plans"))
            foreach (var e in all.Where(e => e.Category == Catalog.Plans && e.Models.Count == 1 &&
                                             e.Models[0] is @"props\instr03blueprint.nif" or @"props\bostechdocs.nif"))
                project.Selected.Add(e.Id);
        if (project.CategoryStyles.Remove(oldPlans, out var look)) project.CategoryStyles.TryAdd(Catalog.Plans, look);
        // graves and pyres moved from Loot spots to their events
        if (project.Selected.Remove("spot:disturbed graves (halloween)")) project.Selected.Add("event:the slasher (halloween):disturbed graves");
        if (project.Selected.Remove("spot:mischief night pyres")) project.Selected.Add("event:mischief night:pyres & bonfires");
        // Spooky Scorched (costumes) split into heads and bodies: its tick, look, parts and part looks go to both
        foreach (var prefix in new[] { "event:", "eventenemy:" })
        {
            string old = prefix + "the slasher (halloween):" + EventCatalog.SpookyCostumes.ToLowerInvariant();
            var split = all.Where(e => e.Id == prefix + "the slasher (halloween):" + EventCatalog.SpookyHeads.ToLowerInvariant() ||
                                       e.Id == prefix + "the slasher (halloween):" + EventCatalog.SpookyBodies.ToLowerInvariant()).ToList();
            if (split.Count == 0) continue;
            project.OffParts.Remove(old, out var oldOff);
            if (project.Selected.Remove(old))
                foreach (var e in split.Where(e => oldOff == null || !e.Models.All(oldOff.Contains))) project.Selected.Add(e.Id); // a half left all out stays unticked
            if (project.Overrides.Remove(old, out var oldLook)) foreach (var e in split) project.Overrides.TryAdd(e.Id, oldLook.Clone());
            if (oldOff != null)
                foreach (var e in split.Where(e => !e.Models.All(oldOff.Contains)))
                    if (oldOff.Where(e.Models.Contains).ToHashSet() is { Count: > 0 } mine) project.OffParts[e.Id] = mine;
            if (project.PartLooks.Remove(old, out var oldParts))
                foreach (var e in split)
                    if (oldParts.Where(kv => e.Models.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value) is { Count: > 0 } mine) project.PartLooks[e.Id] = mine;
        }
        // (after the split: its heads and bodies are NoIcon entries that keep the old look)
        foreach (var (cat, st) in project.CategoryStyles)
            ToModel(st, cats.FirstOrDefault(c => c.Name == cat)?.Entries is { Count: > 0 } es && es.All(e => e.NoIcon));
        foreach (var (id, st) in project.Overrides) ToModel(st, byId.GetValueOrDefault(id)?.NoIcon == true);
        foreach (var (id, looks) in project.PartLooks)
            foreach (var st in looks.Values) ToModel(st, byId.GetValueOrDefault(id)?.NoIcon == true);
        // the Sneak bobblehead model's consumable entry was named after the Sheepsquatch Horn (which reuses that model)
        const string oldHorn = "name:Aid & food:sheepsquatch horn", sneak = "name:Aid & food:bobblehead: sneak";
        if (project.Selected.Remove(oldHorn)) project.Selected.Add(sneak);
        if (project.Overrides.Remove(oldHorn, out var hornLook)) project.Overrides.TryAdd(sneak, hornLook);
        // Magazines: one entry per cover instead of "all magazines"
        if (project.Selected.Remove("magazines:all"))
            foreach (var e in all.Where(e => e.Category == Catalog.Magazines)) project.Selected.Add(e.Id);
        if (project.Overrides.Remove("magazines:all", out var magLook))
            foreach (var e in all.Where(e => e.Category == Catalog.Magazines)) project.Overrides.TryAdd(e.Id, magLook.Clone());
        // XP food ingredients (plants) became XP recipes: a ticked plant stays ticked as the same plant under Plants (all)
        foreach (var id in project.Selected.Where(i => i.StartsWith("xpplant:")).ToList())
        {
            project.Selected.Remove(id);
            if (!Catalog.OldXpPlantModels.TryGetValue(id, out var ms)) continue;
            foreach (var plant in all.Where(e => e.Id.StartsWith("name:Plants (all):") && ms.Overlaps(e.Models))) project.Selected.Add(plant.Id);
        }
        if (project.CategoryStyles.Remove(Catalog.XpPlants, out var xpLook)) project.CategoryStyles.TryAdd(Catalog.XpRecipes, xpLook);
        // recipes are groups now: an old recipe tick ticks its ingredients; recipes keep no tick or parts of their own
        foreach (var r in all.Where(e => e.Group != null))
        {
            if (project.Selected.Remove(r.Id)) foreach (var g in Leaves(r)) project.Selected.Add(g.Id);
            project.OffParts.Remove(r.Id);
            project.Overrides.Remove(r.Id);
        }
        // the recipe book got its own name
        const string oldRecipes = "name:Junk & misc:scanning module recipe", recipes = "name:Junk & misc:recipes (recipe book)";
        if (project.Selected.Remove(oldRecipes)) project.Selected.Add(recipes);
        if (project.Overrides.Remove(oldRecipes, out var recipeLook)) project.Overrides.TryAdd(recipes, recipeLook);
        var known = all.Select(e => e.Id).ToHashSet();
        // the default colours changed to the creature families: saved looks still on an old default follow
        var oldDefaults = new Dictionary<string, string>
        {
            [Catalog.Bobbleheads] = "#FFC800", [Catalog.Magazines] = "#00E5FF", [Catalog.Plans] = "#C080FF", ["Robots"] = "#40C0FF",
            ["Scorched"] = "#FF7020", ["Super Mutants"] = "#80FF40", ["Feral Ghouls"] = "#FFE040", ["Cryptids"] = "#C060FF",
            ["Insects & bugs"] = "#FF4040", ["Mirelurks & crabs"] = "#40FFC0", ["Hostile animals"] = "#FF8040", ["Harmless animals"] = "#FFA0C0",
        };
        foreach (var (cat, old) in oldDefaults)
            if (project.CategoryStyles.GetValueOrDefault(cat) is Style st && cats.FirstOrDefault(c => c.Name == cat) is Category c)
            {
                if (string.Equals(st.GlowColor, old, StringComparison.OrdinalIgnoreCase)) st.GlowColor = c.DefaultStyle.GlowColor;
                if (string.Equals(st.BeamColor, old, StringComparison.OrdinalIgnoreCase)) st.BeamColor = c.DefaultStyle.BeamColor;
            }
        // Animals was split into hostile and harmless
        foreach (var id in project.Selected.Where(i => i.StartsWith("enemy:animals:")).ToList())
        {
            project.Selected.Remove(id);
            var rest = id["enemy:animals:".Length..];
            project.Selected.Add(known.Contains("enemy:harmless animals:" + rest) ? "enemy:harmless animals:" + rest : "enemy:hostile animals:" + rest);
        }
        if (project.CategoryStyles.Remove("Animals", out var animals))
        {
            project.CategoryStyles.TryAdd("Hostile animals", animals);
            project.CategoryStyles.TryAdd("Harmless animals", animals.Clone());
        }
        // entries named after a model's things can get a new name (now: what lies in the world wins over what is never
        // placed): an old "name:<category>:<name>" moves to the entry of that category that still lists the name
        string? Renamed(string id)
        {
            if (known.Contains(id) || !id.StartsWith("name:")) return null;
            int at = id.IndexOf(':', 5);
            if (at < 0) return null;
            string cat = id[5..at], name = id[(at + 1)..];
            // (junk moved to JUNK: its ids still start with "name:Junk & misc:")
            return all.FirstOrDefault(e => (e.Category.Equals(cat, StringComparison.OrdinalIgnoreCase) ||
                                            e.Id.StartsWith($"name:{cat}:", StringComparison.OrdinalIgnoreCase)) &&
                                           e.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))?.Id;
        }
        foreach (var id in project.Selected.ToList())
            if (Renamed(id) is string now) { project.Selected.Remove(id); project.Selected.Add(now); }
        foreach (var id in project.Overrides.Keys.ToList())
            if (Renamed(id) is string now && project.Overrides.Remove(id, out var ownLook)) project.Overrides.TryAdd(now, ownLook);
        foreach (var id in project.OffParts.Keys.ToList())
            if (Renamed(id) is string now && project.OffParts.Remove(id, out var off)) project.OffParts.TryAdd(now, off);
        project.Selected.RemoveWhere(id => !known.Contains(id));
        if (project.Version < 5)
        {
            // the colour scheme: enemies take the fill, items on an old default colour move to the new one
            foreach (var c in cats.Where(c => c.Section == "ENEMIES"))
                if (project.CategoryStyles.GetValueOrDefault(c.Name) is Style es) es.GlowFill = true;
            foreach (var (cat, (old, now)) in Catalog.OldItemColours)
                if (project.CategoryStyles.GetValueOrDefault(cat) is Style ist)
                {
                    if (string.Equals(ist.GlowColor, old, StringComparison.OrdinalIgnoreCase)) ist.GlowColor = now;
                    if (string.Equals(ist.BeamColor, old, StringComparison.OrdinalIgnoreCase)) ist.BeamColor = now;
                }
            // looks given by recipes follow the XP recipes look
            if (cats.FirstOrDefault(c => c.Groups) is Category xpc)
                foreach (var id in project.RecipeLooks) project.Overrides[id] = LookOf(xpc).Clone();
        }
        if (project.Version < 4)
        {
            // recipes ticked before their look existed: fully ticked ones give their ingredients the XP recipes look
            foreach (var r in all.Where(e => HasRows(e) && Full(e)))
                foreach (var leaf in Leaves(r)) SetRecipeLook(leaf, true);
        }
        if (project.Version < 6)
        {
            // ESP Maker's own enemy colours: looks still on an old category default move to the new one
            string Moved(Category c, string colour) =>
                Palette.OldCategoryColours.TryGetValue(c.Name, out var oc) && string.Equals(colour, oc.Old, StringComparison.OrdinalIgnoreCase) ? oc.Now : colour;
            foreach (var c in cats.Where(c => Palette.OldCategoryColours.ContainsKey(c.Name)))
            {
                var looks = c.Entries.Select(e => project.Overrides.GetValueOrDefault(e.Id)).Append(project.CategoryStyles.GetValueOrDefault(c.Name));
                foreach (var st in looks.OfType<Style>()) { st.GlowColor = Moved(c, st.GlowColor); st.BeamColor = Moved(c, st.BeamColor); }
            }
        }
        if (project.Version < 7 && project.CategoryStyles.GetValueOrDefault("Junk & misc") is Style junkLook)
        {
            // junk split by component (JUNK): each part starts with the Junk & misc look
            foreach (var c in cats.Where(c => c.Section == "JUNK")) project.CategoryStyles.TryAdd(c.Name, junkLook.Clone());
        }
        project.Version =Math.Max(project.Version, Project.CurrentVersion);
        project.RecipeLooks.RemoveWhere(id => !project.Overrides.ContainsKey(id) || !known.Contains(id));
        // parts lists: only entries and models that still exist
        var modelsOf = all.ToDictionary(e => e.Id, e => e.Models.ToHashSet());
        foreach (var id in project.OffParts.Keys.ToList())
        {
            if (modelsOf.TryGetValue(id, out var ms)) project.OffParts[id].RemoveWhere(m => !ms.Contains(m));
            if (ms == null || project.OffParts[id].Count == 0) project.OffParts.Remove(id);
        }
        foreach (var id in project.Overrides.Keys.Where(id => !known.Contains(id)).ToList()) project.Overrides.Remove(id);
    }

    /// <summary>A project file holds ticks, looks and options - to keep them or share them.</summary>
    void SaveSetup()
    {
        using var dlg = new SaveFileDialog { Filter = "ESP Maker project|*.itemfinder.json", FileName = ArchiveName(project.ModName)[..^4] + ".itemfinder.json", InitialDirectory = Directory.CreateDirectory(OutputDir).FullName };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Json.Write(dlg.FileName, project);
        Log($"Project saved: {dlg.FileName}");
    }

    void LoadSetup()
    {
        using var dlg = new OpenFileDialog { Filter = "ESP Maker project|*.itemfinder.json;*.json", InitialDirectory = Directory.CreateDirectory(OutputDir).FullName };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Project? p;
        try { p = Json.Read<Project>(dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(this, "Can't read that file: " + ex.Message, Text); return; }
        if (p == null) return;
        UseProject(p);
        Log($"Project opened: {Path.GetFileName(dlg.FileName)} - {project.Selected.Count} item(s) selected. Click \"Build & save\" to use it in the game.");
    }

    void StartEmpty()
    {
        if (project.Selected.Count > 0 && MessageBox.Show(this, "Clear everything selected now? Mods already in the game stay there.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        UseProject(Project.Fresh());
        Log("Started empty.");
    }

    /// <summary>Opens a .ba2 as a project: an ESP Maker mod comes back as it was built; for other mods the items they
    /// change get selected and their colours are read as far as possible.</summary>
    async void ImportBa2()
    {
        if (game == null) return;
        using var dlg = new OpenFileDialog { Title = "Import a mod", Filter = "BA2 archive|*.ba2", InitialDirectory = game.DataFolder };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        ImportResult r;
        SetBusy(true);
        Status($"Reading {Path.GetFileName(dlg.FileName)}...");
        try
        {
            var c = cats;
            r = await Task.Run(() => Importer.Read(dlg.FileName, c));
        }
        catch (Exception ex) { Status(""); MessageBox.Show(this, $"Can't import {Path.GetFileName(dlg.FileName)}: {ex.Message}", Text); return; }
        finally { SetBusy(false); }
        if (r.Project.Selected.Count == 0)
        {
            Status("");
            foreach (var m in r.Unknown) Log("  not in the lists: " + m);
            MessageBox.Show(this, $"Nothing to import: none of the {r.Models} model(s) {Path.GetFileName(r.Source)} changes are in ESP Maker's lists. Your selection stays as it is.", Text);
            return;
        }
        bool add = false;
        if (project.Selected.Count > 0)
        {
            var answer = MessageBox.Show(this, $"{Path.GetFileName(r.Source)} selects {r.Project.Selected.Count} item(s). You have {project.Selected.Count} selected now.\n\n" +
                                               "Yes = replace them\nNo = add it to them", Text, MessageBoxButtons.YesNoCancel);
            if (answer == DialogResult.Cancel) return;
            add = answer == DialogResult.No;
        }
        await UseImport(r, add);
        catTree.SelectedNode = catTree.Nodes[0];
        MessageBox.Show(this, ImportReport(r), Text);
    }

    string ImportReport(ImportResult r)
    {
        var name = Path.GetFileName(r.Source);
        if (r.Kind == Importer.Exact) return $"{name} is back as it was built: {r.Project.Selected.Count} item(s) selected.";
        var text = $"{name} changes {r.Models} model(s): {r.Models - r.Unknown.Count} found, {r.Project.Selected.Count} item(s) selected.";
        text += r.Kind == Importer.OlderBuild
            ? "\n\nThis build is from an older ESP Maker. Beam length and width start at the category's."
            : "\n\nColours are read from the mod where possible, so check the looks.";
        if (r.Unknown.Count > 0)
        {
            text += $"\n\n{r.Unknown.Count} model(s) aren't in ESP Maker's lists and are not included - the log lists them.";
            logPanel.Visible = true;
        }
        if (r.Kind == Importer.OtherMod) text += $"\n\nThe original mod stays installed until you turn it off. Name yours when you click \"Build & save\".";
        return text;
    }

    /// <summary>The import becomes the project (or joins it); a mod it came from stops being a starting point.</summary>
    internal async Task UseImport(ImportResult r, bool add)
    {
        var before = project.ImportedFrom.ToList();
        if (add) AddProject(r.Project); else UseProject(r.Project);
        Log($"Imported {Path.GetFileName(r.Source)} ({r.Kind}): {r.Models} model(s), {r.Project.Selected.Count} item(s) selected.");
        foreach (var m in r.Unknown) Log("  not in the lists: " + m);
        if (project.ImportedFrom.SequenceEqual(before) || game == null) return;
        var folder = game.GameFolder;
        var exclude = project.ImportedFrom.ToList();
        game = await Task.Run(() => GameData.Load(folder, exclude, _ => { }));
    }

    /// <summary>Adds a project to the current one: its items keep the look they had there, parts stay on if either has them on.</summary>
    void AddProject(Project p)
    {
        foreach (var c in cats)
            foreach (var e in c.Entries.Where(e => p.Selected.Contains(e.Id)))
            {
                project.Overrides[e.Id] = p.StyleOf(e, c).Clone();
                project.RecipeLooks.Remove(e.Id);
                var off = p.OffParts.GetValueOrDefault(e.Id);
                if (project.Selected.Add(e.Id))
                {
                    if (off != null) project.OffParts[e.Id] = new(off);
                }
                else if (project.OffParts.TryGetValue(e.Id, out var mine))
                {
                    if (off != null) mine.IntersectWith(off);
                    if (off == null || mine.Count == 0) project.OffParts.Remove(e.Id);
                }
                if (p.PartLooks.GetValueOrDefault(e.Id) is { } parts) project.PartLooks[e.Id] = parts;
            }
        project.ImportedFrom = project.ImportedFrom.Union(p.ImportedFrom, StringComparer.OrdinalIgnoreCase).ToList();
        UseProject(project);
    }

    /// <summary>The section's look goes to every category in it (and, if wanted, replaces single items' own colours).</summary>
    /// <summary>Dev (window pictures): pick a category in the tree and open an entry's rows.</summary>
    internal void DevShow(string category, string? open)
    {
        foreach (TreeNode sec in catTree.Nodes)
            foreach (TreeNode n in sec.Nodes)
                if (n.Tag is Category c && c.Name == category) catTree.SelectedNode = n;
        // "entry id|model": that part picked (its item opened)
        string? part = open?.Contains('|') == true ? open[(open.IndexOf('|') + 1)..] : null;
        if (part != null) open = open![..open.IndexOf('|')];
        if (open != null && cats.SelectMany(c => c.Entries).FirstOrDefault(e => e.Id == open) is Entry pe && part != null)
        {
            if (!expanded.Contains(pe.Id)) ToggleRows(pe);
            list.SelectedItems.Clear();
            foreach (ListViewItem li in list.Items) if (li.Tag is PartRow pr && pr.Entry == pe && pr.Model == part) { li.Selected = li.Focused = true; li.EnsureVisible(); }
            ShowInspector();
            return;
        }
        if (open != null && cats.SelectMany(c => c.Entries).FirstOrDefault(e => e.Id == open) is Entry en)
        {
            ToggleRows(en); // picked (and opened, if it has parts): its look shows in the panel
            ShowInspector();
        }
    }

    /// <summary>Opens / closes a recipe in the list.</summary>
    void ToggleRows(Entry e)
    {
        if (!expanded.Add(e.Id)) expanded.Remove(e.Id);
        FillList(keepScroll: true);
        foreach (ListViewItem li in list.Items)
            if (li.Tag == e) { li.Selected = true; li.Focused = true; break; }
    }

    /// <summary>Ticks / unticks an entry and the entries sharing its models, and says so in the log.</summary>
    void TickWithTwins(Entry en, bool on)
    {
        if (!on) keepShown.Add(en.Id); // Your picks / Checked only: the row stays until the view changes
        if (en.Group != null)
        {
            // a recipe (or one ingredient of it): its ingredients - with the XP recipes look as their own while ticked
            foreach (var g in en.Group) TickWithTwins(g, on);
            foreach (var leaf in Leaves(en)) SetRecipeLook(leaf, on);
            return;
        }
        if (!on) SetRecipeLook(en, false); // unticked anywhere: the recipe look goes with it
        SetTicked(en, on);
        if (twins.GetValueOrDefault(en.Id) is { Count: > 0 } tw)
            Log($"{en.Name}: also {(on ? "selected" : "unselected")} {string.Join(", ", tw.Select(t => $"{t.Name} ({t.Category})"))} - " +
                "the game uses the same 3D model for them, so they always light up together.");
    }

    /// <summary>Gives an entry the XP recipes look as its own (on), or takes back a look it got that way (off) - a look set
    /// by hand stays.</summary>
    void SetRecipeLook(Entry e, bool on)
    {
        if (on)
        {
            if (project.Overrides.ContainsKey(e.Id) && !project.RecipeLooks.Contains(e.Id)) return; // its own look, set by hand
            if (cats.FirstOrDefault(c => c.Groups) is not Category recipes) return;
            project.Overrides[e.Id] = LookOf(recipes).Clone();
            project.RecipeLooks.Add(e.Id);
        }
        else if (project.RecipeLooks.Remove(e.Id)) project.Overrides.Remove(e.Id);
    }

    void SetTicked(Entry e, bool on)
    {
        if (e.Group != null) { foreach (var g in e.Group) SetTicked(g, on); return; } // never saved itself
        foreach (var x in twins.GetValueOrDefault(e.Id, new()).Prepend(e))
            if (on) project.Selected.Add(x.Id); else project.Selected.Remove(x.Id);
    }

    void SyncChecks()
    {
        filling = true;
        foreach (ListViewItem li in list.Items)
            if (li.Tag is Entry e && li.Checked != IsTicked(e) || li.Tag is PartRow p && li.Checked != PartOn(p)) li.Checked = !li.Checked;
        filling = false;
    }

    void TickShown(bool on)
    {
        filling = true;
        foreach (ListViewItem li in list.Items)
            if (li.Tag is Entry e) { SetTicked(e, on); SyncParts(e); }
        foreach (ListViewItem li in list.Items) if (li.Tag is Entry e) li.Checked = IsTicked(e);
        filling = false;
        if (expanded.Count > 0) BeginInvoke(() => FillList(keepScroll: true)); // ingredient rows follow their recipes
        Save();
        RefreshCategoryCounts();
    }

    List<Entry> Picked()
    {
        var picked = list.SelectedItems.Cast<ListViewItem>().Select(li => li.Tag).OfType<Entry>().SelectMany(Leaves).Distinct().ToList();
        if (picked.Count == 0) MessageBox.Show(this, "Click one or more items in the list first (Ctrl/Shift for several).", Text);
        return picked;
    }

    void OwnColours()
    {
        var picked = Picked();
        if (picked.Count == 0) return;
        var first = picked[0];
        int tickedNow = picked.Count(e => project.Selected.Contains(e.Id));
        var tickState = tickedNow == picked.Count ? CheckState.Checked : tickedNow == 0 ? CheckState.Unchecked : CheckState.Indeterminate;
        if (picked.Count == 1 && tickState == CheckState.Checked && IsPartial(first)) tickState = CheckState.Indeterminate; // some parts left out
        using var dlg = LookDialog(game, picked, project.StyleOf(first, CatOf(first)), project.KeepOtherMods, Font, out var ed, out var view,
                                   project.OffParts.GetValueOrDefault(first.Id), m => (entriesByModel.GetValueOrDefault(m) ?? new())
                                       .Where(o => o != first).Select(o => $"{o.Name} ({o.Category})").Distinct().ToList(), tickState, out var tick,
                                   partLooks: picked.Count == 1 ? project.PartLooks.GetValueOrDefault(first.Id) : null);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        bool nowOn = tick.CheckState != CheckState.Unchecked;
        if (picked.Count == 1 ? nowOn != (tickedNow == 1) : tick.CheckState != tickState && tick.CheckState != CheckState.Indeterminate)
        {
            foreach (var e in picked) TickWithTwins(e, nowOn);
            RefreshCategoryCounts();
            SyncChecks();
            if (IsPicks) ShowCategory();
            FillList(keepScroll: true);
        }
        foreach (var e in picked) { project.Overrides[e.Id] = ed.Value; project.RecipeLooks.Remove(e.Id); } // now their own
        if (picked.Count == 1 && first.Models.Count > 1)
        {
            // parts with their own look (kept while unticked, like the look itself)
            var pl = view.PartLooks.Where(kv => first.Models.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (pl.Count > 0) project.PartLooks[first.Id] = pl; else project.PartLooks.Remove(first.Id);
        }
        if (first.Models.Count > 1 && nowOn) // unticked: the part choice stays for next time
        {
            var off = view.OffParts;
            if (off.Count > 0) project.OffParts[first.Id] = off; else project.OffParts.Remove(first.Id);
            if (off.Count > 0) Log($"{first.Name}: {first.Models.Count - off.Count} of {first.Models.Count} parts light up.");
        }
        foreach (var e in picked) SyncParts(e);
        Save();
        RefreshColours();
        if (first.Models.Count > 1) FillList(keepScroll: true); // the parts note in Details
    }

    /// <summary>Category / section look: the look settings, and a preview of one of its items (picked from a list).</summary>
    internal static Form CategoryLookDialog(GameData? game, string title, List<Entry> entries, Style look, bool icons3D, bool keepOtherMods, Font font,
                                            out StyleEditor ed)
    {
        var dlg = new Form { Text = title, FormBorderStyle = FormBorderStyle.Sizable, MaximizeBox = true, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = font, AutoScaleMode = AutoScaleMode.Font, ShowIcon = false };
        ed = new StyleEditor { Dock = DockStyle.Top, Value = look, Icons3D = icons3D, Sketch = false };
        var side = new Panel { Dock = DockStyle.Left, Width = 340 };
        side.Controls.Add(ed);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 0, 0) };
        var view = new ModelView { Dock = DockStyle.Fill, ShowParts = false };
        var pick = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var e in entries) pick.Items.Add(e);
        right.Controls.Add(view);
        right.Controls.Add(pick);
        right.Controls.Add(new Label { Text = "Preview:", Dock = DockStyle.Top, Height = 20 });
        dlg.Controls.Add(right);
        dlg.Controls.Add(side);
        dlg.Controls.Add(buttons);
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        dlg.ClientSize = new Size(side.Width + 560, Math.Max(ed.Height, 520) + buttons.Height + 8);
        dlg.MinimumSize = dlg.Size;
        var editor = ed;
        dlg.Load += (_, _) => { if (editor.FitHeight() > 0) dlg.Height += Math.Max(0, editor.Bottom + 8 - side.ClientSize.Height); };
        pick.SelectedIndexChanged += (_, _) => { if (game != null && pick.SelectedItem is Entry e) view.Show(game, e, editor.Value, keepOtherMods); };
        editor.Changed += (_, _) => view.Look = editor.Value;
        if (pick.Items.Count > 0) pick.SelectedIndex = 0;
        return dlg;
    }

    /// <summary>Item look: the look settings, and the first item as the mod will show it.</summary>
    internal static Form LookDialog(GameData? game, List<Entry> picked, Style look, bool keepOtherMods, Font font, out StyleEditor ed, out ModelView view,
                                    IReadOnlySet<string>? offParts, Func<string, List<string>>? shared, CheckState ticked, out CheckBox tick,
                                    string? focusPart = null, IReadOnlyDictionary<string, Style>? partLooks = null)
    {
        var dlg = new Form { Text = picked.Count == 1 ? "Look for: " + picked[0].Name : $"Look for {picked.Count} items", FormBorderStyle = FormBorderStyle.Sizable, MaximizeBox = true, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = font, AutoScaleMode = AutoScaleMode.Font, ShowIcon = false };
        ed = new StyleEditor { Dock = DockStyle.Top, Value = look, Icons3D = picked.All(e => e.NoIcon), Sketch = false };
        var side = new Panel { Dock = DockStyle.Left, Width = 340 }; // the settings keep their own height
        // the same tick as in the list: the parts and the look below only matter while it is ticked
        tick = new CheckBox
        {
            Text = picked.Count == 1 ? "Selected - lights up in the game" : $"Selected - all {picked.Count} light up in the game",
            Dock = DockStyle.Top, Height = 30, Font = new Font(font, FontStyle.Bold), CheckState = ticked,
            Padding = new Padding(4, 0, 0, 0),
        };
        side.Controls.Add(ed);
        side.Controls.Add(tick);
        tick.SendToBack(); // docked first: on top
        var fit = ed;
        dlg.Load += (_, _) => { int g = fit.FitHeight(); if (g > 0) dlg.Height += Math.Max(0, fit.Bottom + 8 - side.ClientSize.Height); };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        // the item as the mod will show it, redrawn as the look changes
        view = new ModelView { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 0, 0) };
        var v2 = view;
        if (picked.Count > 1) buttons.Controls.Add(new Label { Text = $"Preview: {picked[0].Name}", AutoSize = true, UseMnemonic = false, Padding = new Padding(0, 9, 12, 0), ForeColor = SystemColors.GrayText });
        dlg.Controls.Add(view);
        dlg.Controls.Add(side);
        dlg.Controls.Add(buttons);
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        dlg.ClientSize = new Size(side.Width + 560 + (picked[0].Models.Count > 1 ? 336 : 0), Math.Max(ed.Height, 520) + buttons.Height + 8);
        dlg.MinimumSize = dlg.Size;
        if (game != null) view.Show(game, picked[0], ed.Value, keepOtherMods, offParts, shared, ticked != CheckState.Unchecked, focusPart, partLooks);
        var t = tick;
        t.CheckedChanged += (_, _) => v2.Ticked = t.CheckState != CheckState.Unchecked;
        v2.ParentTick += state => t.CheckState = state;
        var editor = ed;
        var v = view;
        editor.Changed += (_, _) => v.Look = editor.Value;
        return dlg;
    }

    // ---------------------------------------------------------------- the look panel

    /// <summary>A part of a multi-model entry, listed under it when it is opened (► / "8 parts ›").</summary>
    sealed record PartRow(Entry Entry, string Model);

    /// <summary>What the look panel shows: a category (nothing picked in the list), items, or one part.</summary>
    Category? inspCat;
    List<Entry> inspItems = new();
    PartRow? inspPart;

    /// <summary>Can be opened in the list: a recipe (its ingredients) or a thing made of several models (its parts).</summary>
    static bool Expandable(Entry e) => HasRows(e) || e.Group == null && e.Models.Count > 1;

    string PartLabel(PartRow p) => p.Entry.PartNames?.GetValueOrDefault(p.Model) ?? Path.GetFileNameWithoutExtension(p.Model);

    bool PartOn(PartRow p) => IsTicked(p.Entry) && project.OffParts.GetValueOrDefault(p.Entry.Id)?.Contains(p.Model) != true;

    Func<string, List<string>> SharedWith(Entry first) => m => (entriesByModel.GetValueOrDefault(m) ?? new())
        .Where(o => o != first).Select(o => $"{o.Name} ({o.Category})").Distinct().ToList();

    /// <summary>Shows the picked rows (or the picked category when no row is) in the look panel.</summary>
    void ShowInspector()
    {
        if (game == null) return;
        inspCat = null; inspItems = new(); inspPart = null;
        var tags = list.SelectedItems.Cast<ListViewItem>().Select(li => li.Tag).ToList();
        if (tags.Count == 1 && tags[0] is PartRow pr)
        {
            inspPart = pr;
            var e = pr.Entry;
            var looks = project.PartLooks.GetValueOrDefault(e.Id);
            bool own = looks?.ContainsKey(pr.Model) == true;
            inspector.ShowLook(PartLabel(pr), $"Part of {e.Name} ({e.Category})", PartOn(pr) ? CheckState.Checked : CheckState.Unchecked,
                               $"Same as {e.Name}", own, own ? looks![pr.Model] : project.StyleOf(e, CatOf(e)), e.NoIcon, null);
            inspector.View.Show(game, e, project.StyleOf(e, CatOf(e)), project.KeepOtherMods, project.OffParts.GetValueOrDefault(e.Id), SharedWith(e),
                                IsTicked(e), pr.Model, looks);
            return;
        }
        var items = tags.OfType<Entry>().SelectMany(Leaves).Distinct().ToList();
        if (items.Count > 0)
        {
            inspItems = items;
            var first = items[0];
            int tickedNow = items.Count(e => project.Selected.Contains(e.Id));
            var tickState = tickedNow == items.Count ? CheckState.Checked : tickedNow == 0 ? CheckState.Unchecked : CheckState.Indeterminate;
            if (items.Count == 1 && tickState == CheckState.Checked && IsPartial(first)) tickState = CheckState.Indeterminate; // some parts left out
            var catNames = items.Select(e => e.Category).Distinct().ToList();
            string heading = items.Count == 1 ? first.Name : $"{items.Count} items";
            string text = items.Count == 1
                ? $"In {first.Category}" + (first.Models.Count > 1 ? $" · {first.Models.Count} parts (► in the list opens them)" : "")
                : string.Join(", ", items.Take(4).Select(e => e.Name)) + (items.Count > 4 ? "..." : "") + " - changes go to all of them";
            inspector.ShowLook(heading, text, tickState, catNames.Count == 1 ? $"Same as {catNames[0]}" : "Same as their categories",
                               project.Overrides.ContainsKey(first.Id), project.StyleOf(first, CatOf(first)), items.All(e => e.NoIcon), null);
            inspector.View.Show(game, first, project.StyleOf(first, CatOf(first)), project.KeepOtherMods, project.OffParts.GetValueOrDefault(first.Id),
                                SharedWith(first), IsTicked(first), null, project.PartLooks.GetValueOrDefault(first.Id));
            return;
        }
        if (Current is Category c)
        {
            inspCat = c;
            // preview on: ticked things first, then the rest (recipes: their ingredients' entries)
            var entries = c.Entries.SelectMany(Leaves).Distinct().OrderByDescending(e => project.Selected.Contains(e.Id)).Take(500).ToList();
            inspector.ShowLook(c.Name, "The look of everything in this category. Click an item in the list to give it its own.",
                               null, null, true, LookOf(c), c.Entries.Count > 0 && c.Entries.All(e => e.NoIcon), entries);
            return;
        }
        inspector.ShowEmpty(IsPicks ? "Everything you selected, from all categories.\n\nClick an item to see and change its look."
                                    : "Pick a category on the left, or click an item, to see and change its look.");
    }

    /// <summary>The look settings in the panel changed: store them for what is shown.</summary>
    void LookEdited()
    {
        var look = inspector.Editor.Value;
        if (inspPart is PartRow pr)
        {
            if (!project.PartLooks.TryGetValue(pr.Entry.Id, out var looks)) return; // "same as the item": not editable
            looks[pr.Model] = look;
            Save();
            RefreshColours();
            inspector.View.Show(game!, pr.Entry, project.StyleOf(pr.Entry, CatOf(pr.Entry)), project.KeepOtherMods, project.OffParts.GetValueOrDefault(pr.Entry.Id),
                                SharedWith(pr.Entry), IsTicked(pr.Entry), pr.Model, looks);
            return;
        }
        if (inspItems.Count > 0)
        {
            foreach (var e in inspItems) { project.Overrides[e.Id] = look.Clone(); project.RecipeLooks.Remove(e.Id); } // their own now
        }
        else if (inspCat is Category c)
        {
            project.CategoryStyles[c.Name] = look;
            if (c.Groups) foreach (var id in project.RecipeLooks) project.Overrides[id] = look.Clone(); // recipe ingredients follow
        }
        else return;
        Save();
        RefreshColours();
        inspector.View.Look = look;
    }

    /// <summary>"Own look" / "Same as ...": an own look starts as a copy of the one it followed.</summary>
    void OwnSwitched(bool own)
    {
        if (inspPart is PartRow pr)
        {
            var e = pr.Entry;
            if (own)
            {
                if (!project.PartLooks.TryGetValue(e.Id, out var looks)) project.PartLooks[e.Id] = looks = new();
                looks[pr.Model] = project.StyleOf(e, CatOf(e)).Clone();
            }
            else if (project.PartLooks.TryGetValue(e.Id, out var looks) && looks.Remove(pr.Model) && looks.Count == 0) project.PartLooks.Remove(e.Id);
            Save();
            FillList(keepScroll: true); // the part's row and the item's "◆" badge
        }
        else if (inspItems.Count > 0)
        {
            foreach (var e in inspItems)
                if (own) project.Overrides.TryAdd(e.Id, project.StyleOf(e, CatOf(e)).Clone());
                else { project.Overrides.Remove(e.Id); }
            foreach (var e in inspItems) project.RecipeLooks.Remove(e.Id);
            Save();
            RefreshColours();
        }
        ShowInspector();
    }

    /// <summary>The panel's tick: items or a part in or out.</summary>
    void TickSwitched(bool on)
    {
        if (inspPart is PartRow pr) SetPartOn(pr, on);
        else if (inspItems.Count > 0)
            foreach (var e in inspItems)
            {
                TickWithTwins(e, on);
                if (on) project.OffParts.Remove(e.Id); // ticking a partly ticked thing ticks all of it
                SyncParts(e);
            }
        else return;
        Save();
        RefreshCategoryCounts();
        FillList(keepScroll: true);
        if (IsPicks) ShowCategory();
        ShowInspector();
    }

    /// <summary>A part in or out. The item follows its parts: ticking a part of an unticked item ticks the item with just
    /// that part; leaving out its last part unticks it (and ticking it again then brings all parts back).</summary>
    void SetPartOn(PartRow p, bool on)
    {
        SetPart(p.Entry, p.Model, on, TickWithTwins);
        SyncParts(p.Entry);
    }

    void SetPart(Entry e, string model, bool on, Action<Entry, bool> tick)
    {
        var off = project.OffParts.GetValueOrDefault(e.Id) ?? new HashSet<string>();
        if (on)
        {
            if (!IsTicked(e)) { off = e.Models.Where(m => m != model).ToHashSet(); tick(e, true); }
            else off.Remove(model);
        }
        else
        {
            if (!IsTicked(e)) return;
            off.Add(model);
            if (e.Models.All(off.Contains)) { tick(e, false); off.Clear(); }
        }
        if (off.Count > 0) project.OffParts[e.Id] = off; else project.OffParts.Remove(e.Id);
    }

    bool PartLit(Entry e, string model) => project.Selected.Contains(e.Id) && project.OffParts.GetValueOrDefault(e.Id)?.Contains(model) != true;

    IEnumerable<Entry> LinkedWith(string model) => (entriesByModel.GetValueOrDefault(model) ?? new()).Where(o => o.LinkParts);

    /// <summary>After a change to e: its shared parts get the same state in the other entries that list them.</summary>
    void SyncParts(Entry e)
    {
        if (!e.LinkParts) return;
        foreach (var m in e.Models)
        {
            bool lit = PartLit(e, m);
            foreach (var o in LinkedWith(m).Where(o => o != e)) SetPart(o, m, lit, SetTicked);
        }
    }

    /// <summary>A setup just opened: a shared part that is on anywhere lights up in the game, so it is shown on everywhere.</summary>
    void MatchLinkedParts()
    {
        foreach (var m in entriesByModel.Keys.Where(m => LinkedWith(m).Skip(1).Any()).ToList())
            if (LinkedWith(m).Any(e => PartLit(e, m)))
                foreach (var e in LinkedWith(m).ToList()) SetPart(e, m, true, SetTicked);
    }

    // ---------------------------------------------------------------- recommended presets

    const string SlasherHeads = "event:the slasher (halloween):spooky scorched (costume heads)",
                 SlasherBodies = "event:the slasher (halloween):spooky scorched (costume bodies)";
    /// <summary>Spooky Scorched Beacons' purple, and its green for the Pint-Sized Slasher mask.</summary>
    const string SlasherPurple = "#941FFF", SlasherGreen = "#33FF40";

    /// <summary>The presets offered: tiles with a picture.</summary>
    List<PresetPicker.Tile> PresetTiles()
    {
        Image M(string model, string colour) => Models3D.Get(model) is Model3D m ? Models3D.Tile(m, Materials.Parse(colour), 64) : new Bitmap(64, 64);
        Image I(string icon, string colour) => Icons.Get(icon) is Icons.Icon ic ? IconPicker.Tile(ic, Materials.Parse(colour), 64) : new Bitmap(64, 64);
        var tiles = new List<PresetPicker.Tile>
        {
            new("enemies", "Enemies", "each kind its own colour, glow, through walls", M("model:skull", "#FF642F")),
            new("legendary", "Legendary enemies", "glow, beam and a 3D crown", M("model:crown", "#FFD700")),
            new("bobbleheads", "Bobbleheads", "glow, beam and a 3D bobblehead", M("model:bobblehead", "#FFFFFF")),
            new("magazines", "Magazines", "glow, beam and a magazine icon", I("magazine", "#FFFFFF")),
            new("plans", "Plans", "glow, beam and a plan icon", I("scroll", "#FFFFFF")),
            new("slasher", "Slasher event", "Spooky Scorched purple with a pumpkin; the Slasher mask green with a clown", M("model:pumpkin", SlasherPurple)),
            new("xp", "XP buffs", "every XP recipe's ingredients: glow and a 3D ramen", M("model:ramen", Catalog.PlantGreen)),
            new("vending", "Player vendors", "every CAMP vending machine: glow, a tall beam and a $ icon", I("dollar", "#FFB347")),
            new("plants", "Bright plants", "harvestable plants in their own colours, brighter - nothing added, not through walls", I("herb", Catalog.PlantGreen)),
        };
        return tiles.Where(t => PresetEntries(t.Key).Count > 0).ToList();
    }

    /// <summary>What a preset ticks.</summary>
    List<Entry> PresetEntries(string key)
    {
        IEnumerable<Entry> In(Func<Category, bool> which) => cats.Where(which).SelectMany(c => c.Entries);
        return (key switch
        {
            // not the opt-in "everyday clothes" (players would glow) and not the auras (their own preset)
            "enemies" => In(c => c.Section == "ENEMIES" && c.Name != Enemies.Marked).Where(e => !e.Name.Contains("everyday clothes")),
            "legendary" => In(c => c.Name == Enemies.Marked).Where(e => e.Id == "marked:legendary enemies"),
            "bobbleheads" => In(c => c.Name == Catalog.Bobbleheads),
            "magazines" => In(c => c.Name == Catalog.Magazines),
            "plans" => In(c => c.Name == Catalog.Plans),
            "vending" => In(c => c.Name == Catalog.Vending).Where(e => e.Id.Contains("legendaryscrappingstation") == false),
            "plants" => In(c => c.Name == "Plants (all)"),
            "slasher" => In(c => true).Where(e => e.Id is SlasherHeads or SlasherBodies),
            "xp" => In(c => c.Groups),
            _ => Enumerable.Empty<Entry>(),
        }).ToList();
    }

    void RunPresets()
    {
        if (game == null) return;
        using var dlg = new PresetPicker(PresetTiles(), Font);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        ApplyPresets(dlg.Picked);
        if (dlg.Install) BuildSave();
    }

    /// <summary>Ticks each preset's things and gives them its look (the category's, or - the Slasher - the entries' own).</summary>
    void ApplyPresets(List<string> picked)
    {
        Style Look(Category c, Action<Style> set) { var st = LookOf(c).Clone(); set(st); project.CategoryStyles[c.Name] = st; return st; }
        static void Glowing(Style st) { st.Glow = true; st.GlowFill = false; st.GlowOpacity = 30; st.ThroughWalls = true; }
        static void Beam(Style st, string colour, int metres = 20)
        { st.Beam = true; st.BeamArrow = false; st.BeamColor = colour; st.BeamLength = (int)(metres * StyleEditor.UnitsPerMetre); st.BeamWidth = 3.5f; st.BeamStrength = 30; }
        static void Icon(Style st, string icon, float size) { st.Icon = icon; st.IconSize = size; st.Icon3D = false; }
        foreach (var key in picked)
        {
            var entries = PresetEntries(key);
            foreach (var e in entries) { TickWithTwins(e, true); SyncParts(e); }
            foreach (var c in entries.Select(CatOf).Distinct())
                switch (key)
                {
                    case "enemies":
                        // the category's own colour, outlined, through walls, nothing above them
                        var look = c.DefaultStyle.Clone();
                        Glowing(look);
                        look.Beam = false; look.Icon = "";
                        project.CategoryStyles[c.Name] = look;
                        break;
                    case "legendary": Look(c, st => { Glowing(st); Beam(st, st.BeamColor, 30); Icon(st, "model:crown", 30); }); break;
                    case "bobbleheads": Look(c, st => { Glowing(st); Beam(st, st.GlowColor); Icon(st, "model:bobblehead", 20); }); break;
                    case "magazines": Look(c, st => { Glowing(st); Beam(st, st.GlowColor); Icon(st, "magazine", 25); }); break;
                    case "plans": Look(c, st => { Glowing(st); Beam(st, st.GlowColor); Icon(st, "scroll", 25); }); break;
                    case "vending": Look(c, st => { Glowing(st); Beam(st, st.GlowColor, 40); Icon(st, "dollar", 40); }); break;
                    case "plants":
                        var bright = Look(c, st => { st.Glow = st.Beam = false; st.Icon = ""; st.Brighten = true; st.BrightenStrength = 100; });
                        // the same models ticked in other lists (Aid & food...) mustn't glow in those lists' look
                        foreach (var t in entries.SelectMany(e => twins.GetValueOrDefault(e.Id) ?? new()))
                            project.Overrides.TryAdd(t.Id, bright.Clone());
                        break;
                    case "xp":
                        var xp = Look(c, st => { Glowing(st); st.Beam = false; Icon(st, "model:ramen", 15); });
                        foreach (var id in project.RecipeLooks) project.Overrides[id] = xp.Clone(); // the ingredients follow
                        break;
                }
            if (key == "slasher")
            {
                // as Spooky Scorched Beacons: heads with a purple beam and the pumpkin, bodies outlined purple, the
                // Pint-Sized Slasher mask green with its own beam and the clown
                var heads = new Style { GlowColor = SlasherPurple }; Glowing(heads); Beam(heads, SlasherPurple, 14); Icon(heads, "model:pumpkin", 30);
                var bodies = new Style { GlowColor = SlasherPurple, BeamColor = SlasherPurple }; Glowing(bodies); bodies.Beam = false;
                project.Overrides[SlasherHeads] = heads;
                project.Overrides[SlasherBodies] = bodies;
                if (entries.FirstOrDefault(e => e.Id == SlasherHeads) is Entry he)
                {
                    var looks = project.PartLooks.TryGetValue(he.Id, out var pl) ? pl : project.PartLooks[he.Id] = new();
                    foreach (var m in he.Models.Where(m => m.Contains("pintsizedslasher")))
                    {
                        var mask = new Style { GlowColor = SlasherGreen }; Glowing(mask); Beam(mask, SlasherGreen, 14); Icon(mask, "model:clown", 30);
                        looks[m] = mask;
                    }
                }
            }
        }
        Save();
        RefreshCategoryCounts();
        ShowCategory();
        FillList();
        ShowInspector();
        Log($"Presets added: {string.Join(", ", picked)} - {Ticked().Count} things selected now.");
    }


    /// <summary>Tells when another installed ESP Maker mod changes the same models: only one of them shows (the one
    /// listed later in the ini).</summary>
    void WarnOverlaps(string builtFile)
    {
        if (game == null) return;
        try
        {
            var ours = Ba2Archive.Open(builtFile).Entries.Select(e => e.Name).Where(n => n.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var other in OwnMods().Where(n => !n.Equals(Path.GetFileName(builtFile), StringComparison.OrdinalIgnoreCase)))
            {
                var path = Path.Combine(game.DataFolder, other);
                if (!File.Exists(path)) continue;
                int shared = Ba2Archive.Open(path).Entries.Count(e => ours.Contains(e.Name));
                if (shared > 0)
                    Log($"Note: {other} changes {shared} of the same models - for those only one mod shows (the one listed later in {game.IniName}, now {ArchiveName(project.ModName)}).");
            }
        }
        catch (Exception ex) { Log("Couldn't compare with the other mods: " + ex.Message); }
    }

    /// <summary>The "More" menu items that show a setting.</summary>
    void UpdateMoreMenu()
    {
        filling = true;
        keepItem.Checked = project.KeepOtherMods;
        filling = false;
    }

    (int Row, int Col)? hoverLink;
    Style? copiedLook;
    string copiedFrom = "";

    List<Entry> SelectedEntries() => list.SelectedItems.Cast<ListViewItem>().Select(li => li.Tag).OfType<Entry>().ToList();

    /// <summary>"8 parts ›" (or "5/8 parts ›" with some left out) for things made of several models: a hint that the
    /// Item look window lets you pick them.</summary>
    string? PartsBadge(Entry e)
    {
        if (e.Group != null || e.Models.Count < 2) return null;
        // unticked: nothing lights up, so just how many there are (the part choice is kept for when it's ticked again)
        int off = IsTicked(e) ? project.OffParts.GetValueOrDefault(e.Id)?.Count ?? 0 : 0;
        int own = project.PartLooks.GetValueOrDefault(e.Id)?.Count ?? 0; // parts with their own look
        return (off > 0 ? $"{e.Models.Count - off}/{e.Models.Count} parts" : $"{e.Models.Count} parts") + (own > 0 ? $" ◆{own}" : "") + " ›";
    }

    Rectangle LinkRect(Rectangle cell, string text)
    {
        int w = TextRenderer.MeasureText(text, list.Font, Size.Empty, TextFormatFlags.NoPadding).Width + 10;
        return new Rectangle(cell.Right - w, cell.Top, w, cell.Height);
    }

    Rectangle? LinkAt(ListViewItem li, int col)
    {
        if (li.Tag is not Entry e) return null;
        var row = li.Bounds;
        return col == 0 && PartsBadge(e) is string b ? LinkRect(new Rectangle(row.Left, row.Top, list.Columns[0].Width, row.Height), b) : null;
    }

    void DrawLink(Graphics g, string text, Rectangle r, bool hover, bool onHighlight)
    {
        var colour = onHighlight ? SystemColors.HighlightText : hover ? Color.FromArgb(0, 92, 197) : Color.FromArgb(96, 128, 170);
        using var f = hover ? new Font(list.Font, FontStyle.Underline) : null;
        TextRenderer.DrawText(g, text, f ?? list.Font, r, colour, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }

    void CopyLook()
    {
        var sel = SelectedEntries();
        if (sel.Count != 1 || sel[0].Group != null) return;
        copiedLook = project.StyleOf(sel[0], CatOf(sel[0])).Clone();
        copiedFrom = sel[0].Name;
        Log($"Copied the look of {copiedFrom} - right-click other items and Paste look (or Ctrl+V).");
    }

    void PasteLook()
    {
        if (copiedLook == null) return;
        var picked = Picked();
        if (picked.Count == 0) return;
        foreach (var e in picked) { project.Overrides[e.Id] = copiedLook.Clone(); project.RecipeLooks.Remove(e.Id); } // now their own
        Save();
        RefreshColours();
        Log($"Pasted the look of {copiedFrom} onto {(picked.Count == 1 ? picked[0].Name : $"{picked.Count} items")}.");
    }

    /// <summary>Ticks / unticks the selected rows (as clicking their boxes would).</summary>
    void TickSelected(bool on)
    {
        foreach (var li in list.SelectedItems.Cast<ListViewItem>().ToList())
        {
            if (li.Tag is not Entry e) continue;
            if (on && li.Checked && IsPartial(e)) { li.Checked = false; li.Checked = true; } // partly ticked: all of it
            else li.Checked = on;
        }
    }

    void ResetColours()
    {
        var picked = Picked();
        foreach (var e in picked) { project.Overrides.Remove(e.Id); project.RecipeLooks.Remove(e.Id); }
        Save();
        RefreshColours();
    }

    async Task<BuildResult?> Build(string outFile)
    {
        if (game == null) return null;
        if (project.Selected.Count == 0) { MessageBox.Show(this, "Select at least one item first.", Text); return null; }
        var (models, bytes) = Builder.Estimate(project, cats, game);
        if (models > 500 && MessageBox.Show(this,
                $"That's {models:N0} models (roughly {bytes / 1_000_000:N0} MB). A very big mod makes the game load longer and fills the world with beams.\n\nBuild it anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return null;
        SetBusy(true);
        logBox.Clear();
        Log($"Building {Path.GetFileName(outFile)}...");
        try
        {
            var p = project;
            // mods may have been added/removed since the app started: read the load order again
            var folder = game.GameFolder;
            var exclude = project.ImportedFrom.ToList();
            game = await Task.Run(() => GameData.Load(folder, exclude, _ => { }));
            var g = game;
            var r = await Task.Run(() => Builder.Build(p, cats, g, outFile, Log));
            foreach (var c in r.Conflicts) Log("Note: " + c);
            Log($"Done: {r.Models} model(s) changed{(r.Brightened > 0 ? $", {r.Brightened} material(s) brightened" : "")}{(r.Failed > 0 ? $", {r.Failed} could not be changed" : "")}. File: {outFile}");
            if (r.Failed > 0)
                MessageBox.Show(this, $"{r.Failed} model(s) could not be changed and are not in the mod - see the log for which and why.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return r;
        }
        catch (Exception ex) { Log("Failed: " + ex.Message); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return null; }
        finally { SetBusy(false); }
    }

    /// <summary>A small dialog: <paramref name="fill"/> adds the controls and returns what checks OK (null closes it).</summary>
    bool AskDialog(string title, string okText, Func<FlowLayoutPanel, Func<string?>> fill)
    {
        using var dlg = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowIcon = false,
                                   StartPosition = FormStartPosition.CenterParent, Font = Font, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12) };
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        var check = fill(flow);
        var error = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(360, 0), UseMnemonic = false };
        flow.Controls.Add(error);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = 360 };
        var ok = new Button { Text = okText, Width = 100 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        flow.Controls.Add(buttons);
        dlg.Controls.Add(flow);
        ok.Click += (_, _) =>
        {
            error.Text = check() ?? "";
            if (error.Text == "") dlg.DialogResult = DialogResult.OK;
        };
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK;
    }

    /// <summary>The mod's name (a mod of this app with that name is replaced, another mod's file never) and whether to install it.</summary>
    (string Name, bool Install)? AskSave()
    {
        var box = new ComboBox { Text = project.ModName, Width = 360, DropDownStyle = ComboBoxStyle.DropDown };
        var install = new CheckBox { Text = "Install in the game", AutoSize = true, Checked = true };
        bool done = AskDialog("Build & save", "Build", flow =>
        {
            flow.Controls.Add(new Label { Text = "Mod name (its .ba2 file) - pick one of your mods to replace it:", AutoSize = true });
            box.Items.AddRange(OwnMods().Select(n => (object)Path.GetFileNameWithoutExtension(n)).ToArray());
            flow.Controls.Add(box);
            flow.Controls.Add(install);
            return () =>
            {
                var n = box.Text.Trim();
                if (n.Length == 0) return "Type a name.";
                if (!install.Checked) return null;
                var inData = Path.Combine(game!.DataFolder, ArchiveName(n));
                if (!File.Exists(inData)) return null;
                if (!IsOwnFile(inData)) return $"The game already has a {ArchiveName(n)} from another mod - pick another name.";
                if (string.Equals(ArchiveName(n), ArchiveName(project.ModName), StringComparison.OrdinalIgnoreCase)) return null;
                return MessageBox.Show(this, $"Replace your mod {ArchiveName(n)}?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes ? null : "Pick another name.";
            };
        });
        return done ? (box.Text.Trim(), install.Checked) : null;
    }

    static bool IsOwnFile(string path)
    {
        try { return GameData.IsOwn(Ba2Archive.Open(path)); }
        catch { return false; }
    }

    async void BuildSave()
    {
        if (game == null) return;
        if (project.Selected.Count == 0) { MessageBox.Show(this, "Select at least one item first.", Text); return; }
        if (AskSave() is not var (name, install)) return;
        project.ModName = name;
        Save();
        if (!install) { BuildFile(); return; }
        if (GameData.GameRunning) { MessageBox.Show(this, "Close Fallout 76 first - the game locks its archives while it runs.", Text); return; }
        var outFile = Path.Combine(OutputDir, ArchiveName(name));
        if (await Build(outFile) is not BuildResult built) return;
        try
        {
            // the mod, and its texture archive when it replaces game textures
            var files = new List<string> { outFile };
            if (built.TextureArchive != null) files.Add(built.TextureArchive);
            var names = files.Select(Path.GetFileName).OfType<string>().ToList();
            // only this mod's files are replaced (e.g. a texture archive it no longer needs); other mods stay
            var stale = ModFiles(name).Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            game.Install(files, stale, Log);
            game.OwnInstalled.RemoveAll(n => stale.Contains(n, StringComparer.OrdinalIgnoreCase) || names.Contains(n, StringComparer.OrdinalIgnoreCase));
            game.OwnInstalled.AddRange(names);
            ShowInstallState();
            WarnOverlaps(outFile);
            Log("Installed. Start the game to see it (changes need a game restart).");
            var originals = project.ImportedFrom.Where(n => game.ReadArchiveList().Contains(n, StringComparer.OrdinalIgnoreCase) && File.Exists(Path.Combine(game.DataFolder, n))).ToList();
            if (originals.Count > 0)
                MessageBox.Show(this, $"{ArchiveName(name)} is installed and wins over the mod you imported.\n\n{string.Join(", ", originals)} is still installed - turn it off in your mod manager if you only want yours.", Text);
        }
        catch (UnauthorizedAccessException ex) when (game.Microsoft)
        {
            Log("Install failed: " + ex.Message);
            MessageBox.Show(this, "Windows didn't allow writing to the game's Data folder.\n\nIn the Xbox app open Fallout 76, click \"...\" next to Play " +
                                  "and turn on \"Enable mods\" (if it's not there, the game may need to be reinstalled to a normal folder such as C:\\XboxGames), then try again.", Text);
        }
        catch (Exception ex) { Log("Install failed: " + ex.Message); MessageBox.Show(this, "Install failed: " + ex.Message, Text); }
    }

    /// <summary>"Install in the game" off: only the file, wherever the user wants it.</summary>
    async void BuildFile()
    {
        using var dlg = new SaveFileDialog { Filter = "BA2 archive|*.ba2", FileName = ArchiveName(project.ModName), InitialDirectory = Directory.CreateDirectory(OutputDir).FullName };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (await Build(dlg.FileName) is BuildResult r)
            Log(r.TextureArchive != null
                ? $"Two files: {Path.GetFileName(dlg.FileName)} and {Path.GetFileName(r.TextureArchive)}. To use them: put both in the game's Data folder and add both names to the END of sResourceArchive2List in {game?.IniName ?? "Fallout76Custom.ini"} (or install them with your mod manager)."
                : $"To use it: put the file in the game's Data folder and add its name to the END of sResourceArchive2List in {game?.IniName ?? "Fallout76Custom.ini"} (or install it with your mod manager).");
    }

    void Uninstall()
    {
        if (game == null) return;
        if (GameData.GameRunning) { MessageBox.Show(this, "Close Fallout 76 first.", Text); return; }
        var mods = OwnMods();
        if (mods.Count == 0) { MessageBox.Show(this, "There are no ESP Maker mods in the game.", Text); return; }
        var boxes = new CheckedListBox { Width = 360, Height = 24 + 20 * Math.Min(mods.Count, 8), CheckOnClick = true, IntegralHeight = false };
        foreach (var m in mods) boxes.Items.Add(m, m.Equals(ArchiveName(project.ModName), StringComparison.OrdinalIgnoreCase) || mods.Count == 1);
        bool done = AskDialog("Uninstall", "Remove", flow =>
        {
            flow.Controls.Add(new Label { Text = "Remove from the game:", AutoSize = true });
            flow.Controls.Add(boxes);
            return () => boxes.CheckedItems.Count == 0 ? "Select a mod." : null;
        });
        if (!done) return;
        var names = boxes.CheckedItems.Cast<string>().SelectMany(n => ModFiles(Path.GetFileNameWithoutExtension(n))).ToList();
        try
        {
            game.Uninstall(names, Log);
            game.OwnInstalled.RemoveAll(n => names.Contains(n, StringComparer.OrdinalIgnoreCase));
            ShowInstallState();
            Log("Uninstalled.");
        }
        catch (Exception ex) { Log("Uninstall failed: " + ex.Message); MessageBox.Show(this, "Uninstall failed: " + ex.Message, Text); }
    }

    // ---------------------------------------------------------------- dev: import test

    /// <summary>Dev (--importtest): each .ba2 through Import and a real build of the result (to a temp file): what the
    /// build changes must be the models found, no more and no fewer. Then a build through Import again.</summary>
    internal async Task RunImportTest(string logPath, string[] files)
    {
        var log = new List<string>();
        int failed = 0;
        void Check(bool ok, string what) { log.Add((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failed++; }
        var temp = Path.Combine(Path.GetTempPath(), "espmaker-importtest.ba2");
        foreach (var file in files)
            try
            {
                log.Add(Path.GetFileName(file));
                var r = Importer.Read(file, cats);
                project = Project.Fresh();
                await UseImport(r, false);
                var lit = Ticked0().SelectMany(e => project.ModelsOf(e)).Select(Ba2Path.Normalize).ToHashSet();
                log.Add($"     {r.Kind}: {r.Models} models, {r.Unknown.Count} unknown, {project.Selected.Count} selected, {project.OffParts.Count} with parts off, {project.CategoryStyles.Count} category looks, {project.Overrides.Count} own looks");
                foreach (var g in project.CategoryStyles.Take(6)) log.Add($"     {g.Key}: glow {g.Value.Glow} {g.Value.GlowColor} fill {g.Value.GlowFill} walls {g.Value.ThroughWalls} beam {g.Value.Beam} arrow {g.Value.BeamArrow}");
                foreach (var m in r.Unknown.Take(6)) log.Add("     unknown: " + m);
                var models = Ba2Archive.Open(r.Source).Entries.Where(e => e.Name.StartsWith(@"meshes\") && e.Name.EndsWith(".nif")).Select(e => e.Name[7..]).ToHashSet();
                Check(lit.SetEquals(models.Except(r.Unknown)), $"selection lights the mod's models exactly ({lit.Count})");
                if (project.Selected.Count == 0) continue; // nothing listed: Import says so and stops
                var built = Builder.Build(project, cats, game!, temp, _ => { });
                var again = Importer.Read(temp, cats);
                Check(built.Failed == 0, $"builds: {built.Models} models, {built.Failed} failed");
                Check(again.Kind == Importer.Exact && again.Project.Selected.SetEquals(project.Selected) && again.Project.OffParts.Count == project.OffParts.Count, "the build imports back exactly");
                Check(!File.Exists(file) || !project.ImportedFrom.Any() || r.Kind == Importer.OtherMod, "only other mods are remembered as originals");
            }
            catch (Exception ex) { log.Add("ERROR " + ex); failed++; }
        try { File.Delete(temp); } catch { }
        log.Add(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
        File.WriteAllLines(logPath, log);
    }

    void TickSwitchedFor(Entry e) => SetTicked(e, true);

    /// <summary>Selected entries (dev: tests).</summary>
    List<Entry> Ticked0() => cats.SelectMany(c => c.Entries).Where(e => e.Group == null && project.Selected.Contains(e.Id)).DistinctBy(e => e.Id).ToList();

    // ---------------------------------------------------------------- dev: UI test

    /// <summary>Dev (--uitest): drives the window like a user would - category, items, parts, own looks, ticks, copy /
    /// paste, search - on a fresh setup and checks what gets stored. Writes the results to <paramref name="logPath"/>.</summary>
    internal void RunUiTest(string logPath)
    {
        var log = new List<string>();
        int failed = 0;
        void Check(bool ok, string what) { log.Add((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failed++; }
        Entry E(string id) => cats.SelectMany(c => c.Entries).First(e => e.Id == id);
        void Pick(params object[] tags)
        {
            list.SelectedItems.Clear();
            foreach (ListViewItem li in list.Items) if (tags.Contains(li.Tag)) li.Selected = true;
            ShowInspector();
        }
        Style Edited(string colour) { var st = inspector.Editor.Value; st.GlowColor = colour; inspector.Editor.Value = st; return st; }
        try
        {
            project = Project.Fresh();
            Save();
            var bosses = cats.First(c => c.Name == "Bosses");
            var guardian = E("boss:guardian (en06)");
            string baseModel = guardian.Models.First(m => m.EndsWith("guardianbotbase.nif"));
            foreach (TreeNode sec in catTree.Nodes) foreach (TreeNode n in sec.Nodes) if (n.Tag == bosses) catTree.SelectedNode = n;

            log.Add("category");
            Check(inspCat == bosses, "nothing picked: the panel shows the category");
            Edited("#123456"); LookEdited();
            Check(project.CategoryStyles.GetValueOrDefault("Bosses")?.GlowColor == "#123456", "category look stored");

            log.Add("one item");
            Pick(guardian);
            Check(inspItems.SequenceEqual(new[] { guardian }) && inspCat == null, "item picked: the panel shows it");
            TickSwitched(true);
            Check(project.Selected.Contains(guardian.Id) && !project.OffParts.ContainsKey(guardian.Id), "panel box checks it (all parts)");
            OwnSwitched(true);
            Check(project.Overrides.GetValueOrDefault(guardian.Id)?.GlowColor == "#123456", "own look starts as the category's");
            Edited("#654321"); LookEdited();
            Check(project.Overrides[guardian.Id].GlowColor == "#654321" && project.CategoryStyles["Bosses"].GlowColor == "#123456", "own look edited, category untouched");
            OwnSwitched(false);
            Check(!project.Overrides.ContainsKey(guardian.Id), "same as the category: own look removed");

            log.Add("parts");
            ToggleRows(guardian);
            var partRows = list.Items.Cast<ListViewItem>().Where(li => li.Tag is PartRow pr && pr.Entry == guardian).ToList();
            Check(partRows.Count == guardian.Models.Count, $"opened: {partRows.Count} part rows");
            var basePart = new PartRow(guardian, baseModel);
            Pick(basePart);
            Check(inspPart == basePart, "part picked: the panel shows it");
            TickSwitched(false);
            Check(project.OffParts.GetValueOrDefault(guardian.Id)?.Contains(baseModel) == true && IsTicked(guardian) && IsPartial(guardian), "part left out: item partly checked");
            OwnSwitched(true);
            Edited("#00FF00"); LookEdited();
            Check(project.PartLooks.GetValueOrDefault(guardian.Id)?.GetValueOrDefault(baseModel)?.GlowColor == "#00FF00", "part's own look stored");
            Check(PartsBadge(guardian)?.Contains("◆1") == true, $"badge shows the own look: {PartsBadge(guardian)}");
            foreach (var m in guardian.Models) SetPartOn(new PartRow(guardian, m), false);
            Check(!IsTicked(guardian) && !project.OffParts.ContainsKey(guardian.Id), "every part out: item unchecked, part choice cleared");
            SetPartOn(basePart, true);
            Check(IsTicked(guardian) && project.OffParts.GetValueOrDefault(guardian.Id)?.Count == guardian.Models.Count - 1, "one part in on an unchecked item: item checked with just that part");
            FillList(keepScroll: true);
            Check(list.SelectedItems.Count == 1 && Equals(list.SelectedItems[0].Tag, basePart), "the picked row stays picked through a refresh");
            Check(list.Items.Cast<ListViewItem>().Any(li => Equals(li.Tag, basePart) && li.Checked) &&
                  list.Items.Cast<ListViewItem>().Count(li => li.Tag is PartRow p && p.Entry == guardian && li.Checked) == 1, "part boxes match");
            OwnSwitched(false);
            Check(!project.PartLooks.ContainsKey(guardian.Id), "part back to the item's look");

            log.Add("shared parts");
            var sentry = E("enemy:robots:sentry bot");
            var robobrain = E("enemy:robots:robobrain");
            string arm = guardian.Models.First(m => m.EndsWith("armleftsentrybot.nif"));
            Check(!twins[guardian.Id].Contains(sentry), "EN06 and Sentry Bot don't tick each other as a whole");
            SetPartOn(new PartRow(guardian, arm), true);
            Check(PartLit(sentry, arm) && PartLit(robobrain, arm) && IsPartial(sentry) && IsPartial(robobrain), "shared part selected under EN06: selected in the other robots, only that part");
            SetPartOn(new PartRow(sentry, arm), false);
            Check(!PartLit(guardian, arm) && !IsTicked(sentry) && !IsTicked(robobrain) && IsTicked(guardian), "shared part unselected under Sentry Bot: gone everywhere, EN06 keeps its other part");
            TickWithTwins(sentry, true); SyncParts(sentry);
            Check(PartLit(guardian, arm) && guardian.Models.Where(m => m.Contains(@"\robot\parts\")).All(m => PartLit(guardian, m)), "whole Sentry Bot selected: EN06's shared parts follow");
            TickWithTwins(sentry, false); SyncParts(sentry);
            Check(!PartLit(guardian, arm) && IsTicked(guardian) && !IsTicked(robobrain), "whole Sentry Bot unselected: they go again");

            log.Add("quick looks");
            Pick(guardian);
            OwnSwitched(true);
            inspector.Editor.TestPreset(1); LookEdited(); // Outline + beam
            var ql = project.Overrides[guardian.Id];
            Check(ql.Glow && !ql.GlowFill && ql.Beam && !ql.BeamArrow && ql.BeamLength == (int)(20 * StyleEditor.UnitsPerMetre), "Outline + beam stored");
            Check(inspector.Editor.TestShown == "Outline + beam", $"box shows it: {inspector.Editor.TestShown}");
            var tweaked = inspector.Editor.Value; tweaked.BeamLength = 999; inspector.Editor.Value = tweaked; LookEdited();
            Check(inspector.Editor.TestShown.StartsWith("Custom"), $"a changed detail shows Custom: {inspector.Editor.TestShown}");
            inspector.Editor.TestPreset(6); LookEdited(); // Tall beacon
            Check(!project.Overrides[guardian.Id].Glow && project.Overrides[guardian.Id].BeamLength == (int)(60 * StyleEditor.UnitsPerMetre), "Tall beacon stored");
            Check(project.Overrides[guardian.Id].GlowColor == ql.GlowColor, "the colour stays when a quick look is picked");
            int h0 = inspector.Editor.Height; bool open0 = inspector.Editor.TestMoreOpen;
            inspector.Editor.TestMore();
            Check(inspector.Editor.TestMoreOpen != open0 && inspector.Editor.Height != h0, $"More settings folds / unfolds ({h0} -> {inspector.Editor.Height})");
            inspector.Editor.TestMore();
            OwnSwitched(false);

            log.Add("several items, copy / paste");
            var behemoth = E(bosses.Entries.First(e => e.Name.StartsWith("Behemoth")).Id);
            var bigfoot = bosses.Entries.First(e => e.Name == "Bigfoot");
            var jersey = bosses.Entries.First(e => e.Name == "Jersey Devil");
            Pick(behemoth, bigfoot);
            Check(inspItems.Count == 2, "two items picked");
            OwnSwitched(true);
            Edited("#ABCDEF"); LookEdited();
            Check(project.Overrides.GetValueOrDefault(behemoth.Id)?.GlowColor == "#ABCDEF" && project.Overrides.GetValueOrDefault(bigfoot.Id)?.GlowColor == "#ABCDEF", "edit goes to both");
            Pick(behemoth); CopyLook();
            Pick(jersey); PasteLook();
            Check(project.Overrides.GetValueOrDefault(jersey.Id)?.GlowColor == "#ABCDEF", "paste gives the copied look");
            TickSwitched(true);
            Check(project.Selected.Contains(jersey.Id), "panel box on a pasted item");

            log.Add("tour");
            using (var tour = new Tutorial(Font))
            {
                tour.Show(this);
                using var bmp = new Bitmap(tour.Width, tour.Height);
                tour.DrawToBitmap(bmp, new Rectangle(Point.Empty, tour.Size));
                bmp.Save(Path.Combine(Path.GetDirectoryName(logPath)!, "tour1.png"));
                tour.Close();
            }
            Check(Tutorial.Steps.All(st => typeof(Tutorial).Assembly.GetManifestResourceStream("ItemFinder.Tutorial." + st.Picture) != null), "every tour picture is built in");

            log.Add("presets");
            using (var pp = new PresetPicker(PresetTiles(), Font))
            {
                pp.Show(this);
                using var bmp = new Bitmap(pp.Width, pp.Height);
                pp.DrawToBitmap(bmp, new Rectangle(Point.Empty, pp.Size));
                bmp.Save(Path.Combine(Path.GetDirectoryName(logPath)!, "presets.png"));
                pp.Close();
            }
            Check(PresetTiles().Count == 9, $"9 presets offered ({PresetTiles().Count})");
            project = Project.Fresh();
            ApplyPresets(new() { "bobbleheads", "enemies", "slasher", "xp", "legendary" });
            var bobbles = cats.First(c => c.Name == Catalog.Bobbleheads);
            Check(bobbles.Entries.All(IsTicked), "bobbleheads all checked");
            Check(project.CategoryStyles.GetValueOrDefault(Catalog.Bobbleheads) is { Glow: true, Beam: true, Icon: "model:bobblehead" }, "bobbleheads: glow, beam, 3D bobblehead");
            Check(project.CategoryStyles.GetValueOrDefault("Robots") is { Beam: false, Glow: true, GlowFill: false, ThroughWalls: true, Icon: "" }, "enemies: outline, no beams");
            var everyday = cats.SelectMany(c => c.Entries).Where(e => e.Name.Contains("everyday clothes") && IsTicked(e)).ToList();
            Check(everyday.All(e => twins.GetValueOrDefault(e.Id, new()).Any(t => IsTicked(t) && !t.Name.Contains("everyday clothes"))),
                  $"everyday clothes only checked as twins ({everyday.Count})");
            Check(project.CategoryStyles.GetValueOrDefault(Enemies.Marked) is { Beam: true, GlowFill: false, Icon: "model:crown" }, "legendary: outline, beam + crown");
            Check(project.Overrides.GetValueOrDefault(SlasherHeads) is { Beam: true, Icon: "model:pumpkin" } && project.PartLooks.GetValueOrDefault(SlasherHeads)?.Count == 2,
                  "slasher: heads purple + pumpkin, the mask green + clown");
            Check(project.CategoryStyles.GetValueOrDefault(Catalog.XpRecipes) is { Icon: "model:ramen" } && project.RecipeLooks.Count > 0 &&
                  project.RecipeLooks.All(id => project.Overrides[id].Icon == "model:ramen"), $"XP buffs: ingredients checked with the ramen ({project.RecipeLooks.Count})");

            log.Add("build, import");
            Check(ModFiles("a,b").SequenceEqual(new[] { "ab.ba2", "ab - Textures.ba2" }), "name cleaned for the ini (no commas)");
            var kept = Json.Clone(project);
            var temp = Path.Combine(Path.GetTempPath(), "espmaker-uitest.ba2");
            project = Project.Fresh();
            TickSwitchedFor(guardian);
            project.OffParts[guardian.Id] = new() { baseModel };
            project.Overrides[guardian.Id] = new Style { GlowColor = "#123456", GlowOpacity = 55, GlowFill = true, ThroughWalls = false, Beam = true, BeamColor = "#654321", BeamStrength = 40 };
            var built = Builder.Build(project, cats, game!, temp, _ => { });
            var back = Importer.Read(temp, cats);
            Check(back.Kind == Importer.Exact && back.Project.Selected.SetEquals(project.Selected) && back.Project.OffParts[guardian.Id].SetEquals(new[] { baseModel }), "a build imports back: selection and parts");
            Check(back.Project.Overrides[guardian.Id].GlowOpacity == 55 && back.Project.ModName == "espmaker-uitest", "imports back: look, name from the file");
            // the same mod without its stored setup: an older build
            var arch = Ba2Archive.Open(temp);
            var plain = Path.Combine(Path.GetTempPath(), "espmaker-uitest-old.ba2");
            Ba2Writer.WriteGeneral(plain, arch.Entries.Where(e => e.Name != Importer.SetupPath).Select(e => (e.Name, arch.ReadFile(e))).ToList());
            var older = Importer.Read(plain, cats);
            var look = older.Project.Overrides.GetValueOrDefault(guardian.Id) ?? older.Project.CategoryStyles.GetValueOrDefault(bosses.Name);
            Check(older.Kind == Importer.OlderBuild && older.Project.Selected.Contains(guardian.Id) && older.Project.OffParts.GetValueOrDefault(guardian.Id)?.Contains(baseModel) == true, "older build: item selected, left-out part off");
            Check(look != null && look.GlowColor == "#123456" && look.GlowOpacity == 55 && look.GlowFill && !look.ThroughWalls && look.Beam && look.BeamColor == "#654321" && look.BeamStrength == 40, "older build: look read from its materials");
            Check(GameData.IsOwn(arch), "our archive is recognised");
            File.Delete(temp); File.Delete(plain);

            log.Add("build & save dialog");
            // presses the dialog's main button; if that leaves it open (refused), its Cancel next
            (string Name, bool Install)? Answer(string name)
            {
                project.ModName = name;
                var press = new System.Windows.Forms.Timer { Interval = 150 };
                bool pressed = false;
                press.Tick += (_, _) =>
                {
                    if (Application.OpenForms.Cast<Form>().LastOrDefault() is not Form f || f == this) return;
                    if (pressed) { press.Stop(); ((Button)f.CancelButton!).PerformClick(); return; }
                    pressed = true;
                    ((Button)f.AcceptButton!).PerformClick();
                };
                press.Start();
                var answer = AskSave();
                press.Stop();
                return answer;
            }
            var free = Answer("Espmaker Ui Test Name");
            Check(free is { Name: "Espmaker Ui Test Name", Install: true }, "a new name: accepted, install on");
            var taken = Directory.GetFiles(game!.DataFolder, "*.ba2").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .FirstOrDefault(n => !n.StartsWith("SeventySix") && !n.EndsWith(" - Textures") && !IsOwnFile(Path.Combine(game.DataFolder, n + ".ba2")));
            if (taken != null) Check(Answer(taken) == null, $"another mod's file ({taken}): refused");
            project = kept;

            log.Add("your picks: unchecking keeps the row");
            var viewBefore = catTree.SelectedNode;
            catTree.SelectedNode = catTree.Nodes[0];
            var firstPick = list.Items.Cast<ListViewItem>().Select(li => li.Tag).OfType<Entry>().First();
            Pick(firstPick);
            TickSwitched(false);
            Check(!IsTicked(firstPick) && list.Items.Cast<ListViewItem>().Any(li => li.Tag == firstPick && li.Selected && !li.Checked), "unchecked in Your picks: still listed, picked, unchecked");
            TickSwitched(true);
            Check(IsTicked(firstPick) && list.Items.Cast<ListViewItem>().Count(li => li.Tag == firstPick) == 1, "checked again: one row");
            TickSwitched(false);
            catTree.SelectedNode = catTree.Nodes[1].Nodes[0];
            catTree.SelectedNode = catTree.Nodes[0];
            Check(list.Items.Cast<ListViewItem>().All(li => li.Tag != firstPick), "after another category: gone from Your picks");
            TickWithTwins(firstPick, true);
            catTree.SelectedNode = viewBefore;

            log.Add("search and views");
            search.Text = "deathclaw"; FillList();
            Check(list.Items.Count > 0 && list.Columns[1].Width > 0, $"search: {list.Items.Count} rows, category column shown");
            search.Text = ""; FillList();
            Check(list.Columns[1].Width == 0, "in a category: category column hidden");
            list.SelectedItems.Clear(); ShowInspector();
            Check(inspCat == bosses, "nothing picked again: back to the category");
            var (models, _) = Builder.Estimate(project, cats, game!);
            Check(models > 0, $"build estimate: {models} models");
        }
        catch (Exception ex) { log.Add("ERROR " + ex); failed++; }
        log.Add(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
        File.WriteAllLines(logPath, log);
    }

    /// <summary>Dev (--setuptest): each saved setup in a folder goes through what opening it does: the checks must survive,
    /// nothing may stay on an unknown id, migrating again must change nothing, and it must still estimate a build.</summary>
    internal void RunSetupTest(string folder, string logPath)
    {
        var log = new List<string>();
        int failed = 0;
        var all = cats.SelectMany(c => c.Entries).ToList();
        var known = all.Select(e => e.Id).ToHashSet();
        foreach (var file in Directory.GetFiles(folder, "*.itemfinder.json", SearchOption.AllDirectories))
        {
            string name = Path.GetRelativePath(folder, file);
            try
            {
                var p = Json.Read<Project>(file) ?? throw new InvalidDataException("empty file");
                int version = p.Version, ticks = p.Selected.Count, looks = p.Overrides.Count;
                project = p;
                MigrateSetup(all);
                string once = project.Fingerprint();
                var unknown = project.Selected.Where(id => !known.Contains(id)).ToList();
                var lostLooks = project.Overrides.Keys.Where(id => !known.Contains(id)).ToList();
                var dropped = p.Selected.ToHashSet();
                var original = Json.Read<Project>(file)!;
                log.Add($"     dropped: {string.Join(" | ", original.Selected.Where(id => !dropped.Contains(id)).Take(60))}");
                project = Json.Clone(project);
                MigrateSetup(all);
                bool stable = project.Fingerprint() == once;
                var (models, _) = Builder.Estimate(project, cats, game!);
                bool ok = stable && unknown.Count == 0 && lostLooks.Count == 0 && project.Version == Project.CurrentVersion && (ticks == 0 || models > 0);
                if (!ok) failed++;
                log.Add($"{(ok ? "  ok   " : "  FAIL ")}{name}: v{version}->v{project.Version}, ticks {ticks}->{project.Selected.Count}, looks {looks}->{project.Overrides.Count}, {models} models" +
                        (stable ? "" : ", NOT STABLE on a second migrate") +
                        (unknown.Count > 0 ? $", unknown ticks: {string.Join(" | ", unknown.Take(5))}" : "") +
                        (lostLooks.Count > 0 ? $", looks on unknown ids: {string.Join(" | ", lostLooks.Take(5))}" : ""));
            }
            catch (Exception ex) { failed++; log.Add($"  FAIL {name}: {ex.GetType().Name} {ex.Message}"); }
        }
        log.Add(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
        File.WriteAllLines(logPath, log);
    }

    /// <summary>Dev (--tutorial-shots): the tour's pictures from this window - each step set up, captured, and the part
    /// that matters framed. Uses (and resets) the setup next to the exe.</summary>
    internal async Task MakeTourShots(string dir)
    {
        Directory.CreateDirectory(dir);
        Rectangle On(Control c, int height = -1)
        {
            var p = c.PointToScreen(Point.Empty);
            return new Rectangle(p.X - Location.X, p.Y - Location.Y, c.Width, height < 0 ? c.Height : height);
        }
        Rectangle Rows(Func<ListViewItem, bool> which)
        {
            var rows = list.Items.Cast<ListViewItem>().Where(which).ToList();
            var l = On(list);
            int top = rows.Min(r => r.Bounds.Top), bottom = rows.Max(r => r.Bounds.Bottom);
            return new Rectangle(l.Left + 2, l.Top + top, Math.Min(list.ClientSize.Width - 4, list.Columns[0].Width + list.Columns[2].Width), bottom - top);
        }
        async Task Shot(string name, params Rectangle[] frames)
        {
            await Task.Delay(400);
            Refresh();
            using var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(Point.Empty, Size));
            using (var g = Graphics.FromImage(bmp))
                for (int i = 0; i < frames.Length; i++) Tutorial.Frame(g, frames[i], frames.Length > 1 ? i + 1 : int.Parse(name[4..5]));
            bmp.Save(Path.Combine(dir, name));
        }
        project = Project.Fresh(); // the default mod name in the pictures, not the dev setup's
        Save();
        RefreshCategoryCounts();
        status.Text = "";
        logPanel.Visible = false;
        var bobbles = cats.First(c => c.Name == Catalog.Bobbleheads);
        DevShow(Catalog.Bobbleheads, null);
        list.SelectedItems.Clear(); ShowInspector();
        await Shot("step1.png", On(catTree));
        foreach (var e in bobbles.Entries.Take(5)) TickWithTwins(e, true);
        Save(); RefreshCategoryCounts(); FillList();
        status.Text = "";
        await Shot("step2.png", Rows(li => li.Index < 7));
        list.SelectedItems.Clear(); ShowInspector();
        await Shot("step3.png", On(inspector.Editor, inspector.Editor.HeaderHeight));
        var guardian = cats.SelectMany(c => c.Entries).First(e => e.Id == "boss:guardian (en06)");
        DevShow(Enemies.Bosses, guardian.Id);
        TickSwitched(true);
        status.Text = "";
        await Task.Delay(300);
        await Shot("step4.png", On(inspector.SwitchRow), Rows(li => li.Tag is PartRow p && p.Entry == guardian));
        await Task.Delay(2500); // the 3D preview loads
        await Shot("step5.png", On(inspector.View));
        var stateText = On(installState);
        stateText.Width = TextRenderer.MeasureText(installState.Text, installState.Font).Width + installState.Padding.Horizontal + 8;
        await Shot("step6.png", On(buildButton!), stateText);
    }

    /// <summary>The game data is loaded (dev: the UI test waits for it).</summary>
    internal bool Ready => game != null && cats.Count > 0 && catTree.Nodes.Count > 0;
}
