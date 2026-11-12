using static ItemFinder.Loc;
using System.Diagnostics;

namespace ItemFinder;

/// <summary>Options - Mod manager: every mod archive in the game's Data folder and the ini's load order
/// (sResourceArchive2List). Tick = listed (the game loads it), untick = stays in Data but isn't loaded.
/// Order: top loads first, lower wins when two mods have the same file. Nothing is written until Save.</summary>
sealed class ModManager : Form
{
    sealed class Mod
    {
        public string Name = "";
        public bool On, Missing, Link, Own;
        public long Size;
        public DateTime Changed;
        public HashSet<string>? Files;
    }

    readonly GameData game;
    readonly Action<string> log;
    readonly List<Mod> mods = new();
    readonly ListView list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false,
        MultiSelect = false, AllowDrop = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    readonly Label note = new() { Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button save = new() { Text = Tr("Save"), AutoSize = true, Height = 34, Enabled = false };
    bool loading, dirty;
    /// <summary>The list as the ini has it (what "nothing changed" means).</summary>
    List<string> original = new();
    /// <summary>The list was saved (the main window refreshes its install state).</summary>
    public bool Saved { get; private set; }

    public ModManager(GameData game, Font font, Action<string> log)
    {
        this.game = game;
        this.log = log;
        Text = Tr("ESP Maker - mod manager");
        Font = font;
        ShowIcon = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 600);
        MinimumSize = new Size(640, 400);

        var head = new Label
        {
            Dock = DockStyle.Top, Height = 48, ForeColor = Color.DimGray,
            Text = Tf("Mods in {0}, as listed in {1}. Ticked = the game loads it; unticked stays in Data but is off.\n", game.DataFolder, game.IniName) +
                   Tr("Top loads first - when two mods change the same file, the LOWER one wins. Drag rows, or Ctrl+Up / Ctrl+Down."),
        };
        list.Columns.Add("#", 44);
        list.Columns.Add("Mod", 290);
        list.Columns.Add("Size", 80, HorizontalAlignment.Right);
        list.Columns.Add("Changed", 100);
        list.Columns.Add("Shares files with", 380);

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 150, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10, 0, 0, 0) };
        Button Side(string text, Action act)
        {
            var b = new Button { Text = text, Width = 130, Height = 34, Margin = new Padding(0, 0, 0, 6) };
            b.Click += (_, _) => act();
            side.Controls.Add(b);
            return b;
        }
        Side(Tr("▲  Up"), () => Move(-1));
        Side(Tr("▼  Down"), () => Move(1));
        Side(Tr("To the top"), () => Move(-mods.Count));
        Side(Tr("To the bottom"), () => Move(mods.Count));
        Side("Open Data folder", () => Process.Start("explorer.exe", game.DataFolder));
        Side("Open ini folder", () => Process.Start("explorer.exe", Path.GetDirectoryName(game.IniPath)!));
        side.Controls.Add(new Label { Width = 130, Height = 18 });
        Side(Tr("Delete mod..."), Delete).ForeColor = Color.Firebrick;

        var cancel = new Button { Text = Tr("Cancel"), AutoSize = true, Height = 34, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.AddRange(new Control[] { cancel, save });
        cancel.Margin = new Padding(8, 0, 0, 0);
        save.Margin = Padding.Empty;
        // one bar under the list: the notes left, Save / Cancel right
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(16, 10, 16, 14) };
        bottom.Controls.Add(note);
        bottom.Controls.Add(buttons);
        save.Click += (_, _) => Save();
        CancelButton = cancel;

        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 0) };
        page.Controls.Add(list);
        page.Controls.Add(side);
        page.Controls.Add(head);
        Controls.Add(page);
        Controls.Add(bottom);

        list.ItemChecked += (_, e) =>
        {
            if (loading || e.Item.Tag is not Mod m || m.On == e.Item.Checked) return; // the list also raises this when it first shows its rows
            m.On = e.Item.Checked;
            Changed();
            BeginInvoke(() => Fill(m)); // not while the list is in its own event
        };
        list.KeyDown += (_, e) =>
        {
            if (!e.Control || e.KeyCode is not (Keys.Up or Keys.Down)) return;
            Move(e.KeyCode == Keys.Up ? -1 : 1);
            e.Handled = true;
        };
        // drag a row onto another: it takes that row's place
        list.ItemDrag += (_, e) => { if (e.Item is ListViewItem it) list.DoDragDrop(it, DragDropEffects.Move); };
        list.DragOver += (_, e) => e.Effect = e.Data?.GetDataPresent(typeof(ListViewItem)) == true ? DragDropEffects.Move : DragDropEffects.None;
        list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(typeof(ListViewItem)) is not ListViewItem it || it.Tag is not Mod m) return;
            var at = list.PointToClient(new Point(e.X, e.Y));
            int to = list.GetItemAt(at.X, at.Y) is ListViewItem target ? target.Index : mods.Count - 1;
            int from = mods.IndexOf(m);
            if (to == from) return;
            mods.RemoveAt(from);
            mods.Insert(to, m);
            Fill(m);
            Changed();
        };
        FormClosing += (_, e) =>
        {
            if (!dirty || DialogResult == DialogResult.OK) return;
            var r = MessageBox.Show(this, Tr("Save the changes to the mod list?"), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) e.Cancel = true;
            else if (r == DialogResult.Yes && !Save()) e.Cancel = true;
        };
        Shown += async (_, _) => { await Task.Run(ReadFiles); Fill(Selected()); };

        LoadMods();
    }

    /// <summary>Listed mods in the ini's order, then the unlisted ones in Data by name.</summary>
    void LoadMods()
    {
        var listed = game.ReadArchiveList().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var n in listed) mods.Add(Info(n, true));
        foreach (var n in game.ModArchivesInData())
            if (!listed.Contains(n, StringComparer.OrdinalIgnoreCase)) mods.Add(Info(n, false));
        original = Current();
        Fill(null);
    }

    Mod Info(string name, bool on)
    {
        var m = new Mod { Name = name, On = on };
        var fi = new FileInfo(Path.Combine(game.DataFolder, name));
        if (!fi.Exists) { m.Missing = true; return m; }
        m.Link = fi.Attributes.HasFlag(FileAttributes.ReparsePoint); // Vortex deploys with links
        // a link: the size and date of the real file it points at
        var real = m.Link ? fi.ResolveLinkTarget(true) as FileInfo ?? fi : fi;
        if (!real.Exists) real = fi;
        m.Size = real.Length;
        m.Changed = real.LastWriteTime;
        return m;
    }

    /// <summary>Background: each archive's file list (for "Shares files with") and whether ESP Maker built it.</summary>
    void ReadFiles()
    {
        foreach (var m in mods.ToList())
        {
            if (m.Missing) continue;
            try
            {
                var a = Ba2Archive.Open(Path.Combine(game.DataFolder, m.Name));
                m.Files = a.Entries.Select(e => e.Name).ToHashSet();
                m.Own = GameData.IsOwn(a);
            }
            catch { m.Files = new(); }
        }
    }

    Mod? Selected() => list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as Mod : null;

    void Fill(Mod? select)
    {
        loading = true;
        list.BeginUpdate();
        list.Items.Clear();
        int n = 0;
        foreach (var m in mods)
        {
            string what = m.Name + (m.Own ? Tr("   (ESP Maker)") : "") + (m.Link ? Tr("   (Vortex)") : "");
            var it = new ListViewItem(new[]
            {
                m.On && !m.Missing ? (++n).ToString() : "",
                what,
                m.Missing ? "" : Kb(m.Size),
                m.Missing ? "" : m.Changed.ToString("yyyy-MM-dd"),
                m.Missing ? Tr("Not in Data - untick to drop it from the list") : Shares(m),
            }) { Checked = m.On, Tag = m };
            if (m.Missing) it.ForeColor = Ui.ToTheme(Color.Firebrick);
            else if (!m.On) it.ForeColor = Ui.ToTheme(Color.Gray);
            list.Items.Add(it);
            if (m == select) { it.Selected = true; it.Focused = true; }
        }
        list.EndUpdate();
        if (select != null && list.SelectedItems.Count > 0) list.SelectedItems[0].EnsureVisible();
        loading = false;
        ShowNote();
    }

    static string Kb(long b) => b >= 1 << 20 ? Tf("{0:0.0} MB", b / 1048576.0) : Tf("{0} KB", Math.Max(1, b / 1024));

    /// <summary>Other mods with some of the same files: for a ticked mod, which ones it wins over (it is lower) and loses to.</summary>
    string Shares(Mod m)
    {
        if (m.Files == null) return "...";
        var wins = new List<string>();
        var loses = new List<string>();
        var off = new List<string>();
        int me = mods.IndexOf(m);
        foreach (var o in mods)
        {
            if (o == m || o.Files == null || o.Missing) continue;
            int same = m.Files.Count(o.Files.Contains);
            if (same == 0) continue;
            var t = $"{o.Name} ({same})";
            if (!m.On || !o.On) off.Add(t);
            else (mods.IndexOf(o) < me ? wins : loses).Add(t);
        }
        var parts = new List<string>();
        if (loses.Count > 0) parts.Add("loses to " + string.Join(", ", loses));
        if (wins.Count > 0) parts.Add("wins over " + string.Join(", ", wins));
        if (off.Count > 0) parts.Add((parts.Count > 0 ? Tr("also ") : "") + string.Join(", ", off) + (m.On ? Tr(" (off)") : ""));
        return string.Join("; ", parts);
    }

    void ShowNote()
    {
        var lines = new List<string>();
        if (GameData.GameRunning) lines.Add("Fallout 76 is running: a saved list is used the next time the game starts.");
        if (mods.Any(m => m.Link)) lines.Add("Vortex manages some of these mods - Vortex may write its own order back when it deploys.");
        note.Text = string.Join("\n", lines);
        note.ForeColor = Ui.ToTheme(GameData.GameRunning ? Color.Firebrick : Color.DimGray);
    }

    void Move(int by)
    {
        if (Selected() is not Mod m) return;
        int from = mods.IndexOf(m), to = Math.Clamp(from + by, 0, mods.Count - 1);
        if (to == from) return;
        mods.RemoveAt(from);
        mods.Insert(to, m);
        Fill(m);
        Changed();
        list.Focus();
    }

    void Changed()
    {
        dirty = !Current().SequenceEqual(original, StringComparer.OrdinalIgnoreCase);
        save.Enabled = dirty;
    }

    /// <summary>The ticked mods in order: what Save writes to the ini.</summary>
    List<string> Current() => mods.Where(m => m.On).Select(m => m.Name).ToList();

    /// <summary>Deletes the picked mod's file from Data for good and takes it out of the ini (after asking).</summary>
    void Delete()
    {
        if (Selected() is not Mod m) { MessageBox.Show(this, Tr("Pick a mod in the list first."), Text); return; }
        if (!m.Missing && GameData.GameRunning) { MessageBox.Show(this, Tr("Close Fallout 76 first - the game locks its mods while it runs."), Text); return; }
        var ask = m.Missing
            ? Tf("{0} is not in Data. Take it out of {1}?", m.Name, game.IniName)
            : Tf("Delete {0} ({1}) from the Data folder for good and take it out of {2}?\n\nThis can't be undone.", m.Name, Kb(m.Size), game.IniName);
        if (m.Link) ask += Tr("\n\nThis is a Vortex mod: deleting removes only Vortex's link, and Vortex puts it back when it deploys. To get rid of it, remove it in Vortex instead.");
        if (MessageBox.Show(this, ask, Tr("Delete mod"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            game.Uninstall(new[] { m.Name }, log);
            int at = mods.IndexOf(m);
            mods.Remove(m);
            original.RemoveAll(n => n.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
            Saved = true;
            Fill(mods.Count == 0 ? null : mods[Math.Min(at, mods.Count - 1)]);
            Changed();
        }
        catch (Exception ex)
        {
            log(Tr("Delete failed: ") + ex.Message);
            MessageBox.Show(this, Tr("Delete failed: ") + ex.Message, Text);
        }
    }

    bool Save()
    {
        var names = Current();
        try
        {
            game.WriteArchiveList(names, log);
            dirty = false;
            Saved = true;
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }
        catch (Exception ex)
        {
            log(Tr("Saving the mod list failed: ") + ex.Message);
            MessageBox.Show(this, Tr("Saving failed: ") + ex.Message, Text);
            return false;
        }
    }
}
