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
        /// <summary>A Vortex mod that is off in Vortex (so not in Data): ticking turns it on in Vortex, whose next deploy
        /// puts it in the game. Also Missing (no file to read).</summary>
        public bool VortexOff;
        /// <summary>Missing from Data but a file with this name sits in a subfolder of Data (the game doesn't load it there).</summary>
        public string? FoundAt;
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
        MultiSelect = false, AllowDrop = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true,
    };
    readonly Label note = new() { Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button save = new() { Text = Tr("Save"), AutoSize = true, Height = 34, Enabled = false };
    Button moveToData = null!;
    bool loading, dirty, shown;
    /// <summary>Vortex's mods (read while it was closed): ticks / deletes of Vortex mods are done in Vortex too,
    /// or its next deploy would undo them.</summary>
    (string Profile, List<Vortex.Mod> Mods)? vortex;
    /// <summary>The list as the ini has it (what "nothing changed" means).</summary>
    List<string> original = new();
    /// <summary>The list was saved (the main window refreshes its install state).</summary>
    public bool Saved { get; private set; }

    readonly Settings settings;

    public ModManager(GameData game, Font font, Action<string> log, Settings settings)
    {
        this.game = game;
        this.settings = settings;
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
        Side(Tr("Install mod..."), InstallPicked);
        side.Controls.Add(new Label { Width = 130, Height = 10 });
        Side(Tr("▲  Up"), () => Move(-1));
        Side(Tr("▼  Down"), () => Move(1));
        Side(Tr("To the top"), () => Move(-mods.Count));
        Side(Tr("To the bottom"), () => Move(mods.Count));
        Side("Open Data folder", () => Process.Start("explorer.exe", game.DataFolder));
        Side("Open ini folder", () => Process.Start("explorer.exe", Path.GetDirectoryName(game.IniPath)!));
        moveToData = Side(Tr("Move to Data"), MoveToData);
        moveToData.Enabled = false;
        Button? toVortex = Vortex.ManagesGame ? Side(Tr("Hand to Vortex"), HandToVortex) : null;
        if (toVortex != null) toVortex.Enabled = false;
        list.SelectedIndexChanged += (_, _) =>
        {
            moveToData.Enabled = Selected()?.FoundAt != null;
            if (toVortex != null) toVortex.Enabled = Selected() is { Missing: false, Link: false };
        };
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
            // the list also raises this for every row while it is first created (each queued a whole refill: ~1 s at opening)
            if (loading || !shown || e.Item.Tag is not Mod m || m.On == e.Item.Checked) return;
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
        // ...or drop downloaded mods (.zip / .7z / .rar / .ba2) from Explorer: installs them
        list.DragOver += (_, e) => e.Effect =
            e.Data?.GetDataPresent(typeof(ListViewItem)) == true ? DragDropEffects.Move
            : e.Data?.GetData(DataFormats.FileDrop) is string[] fs && fs.Any(ModInstall.Supported) ? DragDropEffects.Copy
            : DragDropEffects.None;
        list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped)
            {
                var paths = dropped.Where(ModInstall.Supported).ToArray();
                BeginInvoke(() => InstallFiles(paths)); // not inside Explorer's drag (it would hang until the dialogs close)
                return;
            }
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
        Shown += async (_, _) =>
        {
            shown = true;
            var vortexRead = Vortex.ManagesGame && !Vortex.Running ? Task.Run(Vortex.ReadMods) : null;
            await Task.Run(ReadFiles);
            Fill(Selected());
            if (vortexRead == null || (vortex = await vortexRead) is not { } v || IsDisposed) return;
            // Vortex mods that are off: listed at the bottom so they can be ticked on from here
            foreach (var vm in v.Mods.Where(x => !x.Enabled))
                foreach (var a in vm.Archives)
                {
                    var row = mods.FirstOrDefault(o => o.Name.Equals(a, StringComparison.OrdinalIgnoreCase));
                    if (row == null) mods.Add(new Mod { Name = a, Missing = true, VortexOff = true });
                    else if (row.Missing) row.VortexOff = true;
                }
            Fill(Selected());
        };

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
        FindInSubfolders();
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
        // which mods share files, once, here off the window's thread (comparing every pair's lists took seconds)
        var read = mods.Where(m => m.Files != null && !m.Missing).ToList();
        var owners = new Dictionary<string, List<Mod>>();
        foreach (var m in read)
            foreach (var f in m.Files!)
                if (owners.TryGetValue(f, out var l)) l.Add(m); else owners[f] = new List<Mod> { m };
        var counts = new Dictionary<(Mod, Mod), int>();
        foreach (var l in owners.Values)
            if (l.Count > 1)
                foreach (var a in l)
                    foreach (var b in l)
                        if (a != b) counts[(a, b)] = counts.GetValueOrDefault((a, b)) + 1;
        shared = new(counts, read.ToHashSet()); // one swap: the window may be showing the list meanwhile
    }

    sealed record Overlaps(Dictionary<(Mod, Mod), int> Counts, HashSet<Mod> Indexed);
    Overlaps shared = new(new(), new());

    /// <summary>Files two mods both have (counted in ReadFiles; a mod read later is compared here, once).</summary>
    int Same(Mod a, Mod b)
    {
        var (counts, indexed) = shared;
        if (counts.TryGetValue((a, b), out var n)) return n;
        if (indexed.Contains(a) && indexed.Contains(b)) return 0;
        n = a.Files!.Count(b.Files!.Contains);
        counts[(a, b)] = n;
        return n;
    }

    /// <summary>Missing mods: look for a file with the same name anywhere under Data (e.g. a mod manager
    /// deployed it into Data\SomeMod\, where the game never loads it).</summary>
    void FindInSubfolders()
    {
        var missing = mods.Where(m => m.Missing).ToList();
        if (missing.Count == 0) return;
        try
        {
            var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(game.DataFolder, "*.ba2", opt))
                if (!string.Equals(Path.GetDirectoryName(f), Path.TrimEndingDirectorySeparator(game.DataFolder), StringComparison.OrdinalIgnoreCase))
                    found.TryAdd(Path.GetFileName(f), f);
            foreach (var m in missing)
                if (found.TryGetValue(m.Name, out var at)) m.FoundAt = at;
        }
        catch { }
    }

    /// <summary>Moves the picked mod's file from its subfolder straight into Data, where the game loads it.</summary>
    void MoveToData()
    {
        if (Selected() is not Mod m || m.FoundAt == null) return;
        if (GameData.GameRunning) { MessageBox.Show(this, Tr("Close Fallout 76 first - the game locks its mods while it runs."), Text); return; }
        try
        {
            var from = m.FoundAt;
            File.Move(from, Path.Combine(game.DataFolder, m.Name)); // a Vortex link moves as a link
            log(Tf("Moved {0} to Data (was in {1}).", m.Name, Path.GetRelativePath(game.DataFolder, Path.GetDirectoryName(from)!)));
            var fresh = Info(m.Name, m.On);
            try
            {
                var a = Ba2Archive.Open(Path.Combine(game.DataFolder, m.Name));
                fresh.Files = a.Entries.Select(e => e.Name).ToHashSet();
                fresh.Own = GameData.IsOwn(a);
            }
            catch { fresh.Files = new(); }
            mods[mods.IndexOf(m)] = fresh;
            Saved = true;
            Fill(fresh);
        }
        catch (Exception ex)
        {
            log(Tr("Move failed: ") + ex.Message);
            MessageBox.Show(this, Tr("Move failed: ") + ex.Message, Text);
        }
    }

    void InstallPicked()
    {
        using var dlg = new OpenFileDialog { Title = Tr("Install mod"), Filter = ModInstall.Filter, Multiselect = true };
        // the folder of the last pick; the first time Windows' Downloads
        var start = settings.ModDownloads is string last && Directory.Exists(last) ? last
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(start)) dlg.InitialDirectory = start;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        settings.ModDownloads = Path.GetDirectoryName(dlg.FileNames[0]);
        settings.Save();
        InstallFiles(dlg.FileNames);
    }

    /// <summary>Installs downloaded mods into Data and ticks them at the bottom of the list (they win over the others);
    /// the ini is written on Save as usual.</summary>
    void InstallFiles(string[] paths)
    {
        if (paths.Length == 0) return;
        if (GameData.GameRunning) { MessageBox.Show(this, Tr("Close Fallout 76 first - the game locks its mods while it runs."), Text); return; }
        if (Vortex.ManagesGame)
        {
            var r = MessageBox.Show(this, Tr("Vortex manages your mods, and when it deploys it removes mods copied in by hand from the game's mod list.\n\nInstall through Vortex?\n\nYes = Vortex installs it (recommended)\nNo = ESP Maker copies it into Data"),
                Tr("Install mod"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, settings.UseVortex ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2);
            if (r == DialogResult.Cancel) return;
            if ((r == DialogResult.Yes) != settings.UseVortex) { settings.UseVortex = r == DialogResult.Yes; settings.Save(); }
            if (r == DialogResult.Yes)
            {
                UseWaitCursor = true;
                bool ready = Vortex.ReadyForInstall(this, log); // Vortex on Fallout 76, not another game
                UseWaitCursor = false;
                if (!ready) return;
                foreach (var p in paths)
                    try { Vortex.Install(ModInstall.IsBa2(p) ? Vortex.Pack(new[] { p }, Path.GetFileNameWithoutExtension(p)) : p, log); }
                    catch (Exception ex) { log(Tf("Installing {0} failed: {1}", Path.GetFileName(p), ex.Message)); MessageBox.Show(this, Tf("Installing {0} failed: {1}", Path.GetFileName(p), ex.Message), Text); }
                MessageBox.Show(this, Tr("Finish the install in Vortex. Vortex puts the mod in the game and keeps it in the mod list."), Tr("Install mod"));
                return;
            }
        }
        Mod? last = null;
        foreach (var p in paths)
        {
            List<string> names;
            try
            {
                UseWaitCursor = true;
                names = ModInstall.Install(this, game, p, log);
            }
            catch (Exception ex)
            {
                log(Tf("Installing {0} failed: {1}", Path.GetFileName(p), ex.Message));
                MessageBox.Show(this, Tf("Installing {0} failed: {1}", Path.GetFileName(p), ex.Message), Text);
                continue;
            }
            finally { UseWaitCursor = false; }
            foreach (var n in names)
            {
                var old = mods.FindIndex(m => m.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                if (old >= 0) mods.RemoveAt(old); // a replaced mod moves to the bottom too
                var m = Info(n, true);
                try
                {
                    var a = Ba2Archive.Open(Path.Combine(game.DataFolder, n));
                    m.Files = a.Entries.Select(e => e.Name).ToHashSet();
                    m.Own = GameData.IsOwn(a);
                }
                catch { m.Files = new(); }
                mods.Add(m);
                last = m;
            }
        }
        if (last == null) return;
        Saved = true;
        Fill(last);
        Changed();
    }

    Mod? Selected() => list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as Mod : null;

    void Fill(Mod? select)
    {
        loading = true;
        list.BeginUpdate();
        // rows are updated in place when the count is the same (tick / move): clearing the list
        // jumped it back to the top, so the view scrolled under the mouse on every click
        bool reuse = list.Items.Count == mods.Count;
        int top = list.TopItem?.Index ?? 0;
        if (!reuse) list.Items.Clear();
        int n = 0, row = 0;
        foreach (var m in mods)
        {
            string what = m.Name + (m.Own ? Tr("   (ESP Maker)") : "") + (m.Link || m.VortexOff ? Tr("   (Vortex)") : "");
            string info = m.VortexOff
                ? m.On ? Tr("Turned on in Vortex on Save - Vortex adds it to the game when it deploys") : Tr("Off in Vortex - tick to turn it on")
                : m.Missing
                ? m.FoundAt != null
                    ? Tf("Found in Data\\{0} - press Move to Data", Path.GetRelativePath(game.DataFolder, Path.GetDirectoryName(m.FoundAt)!))
                    : Tr("File not found in Data folder - untick to drop it from the list")
                : Shares(m);
            var it = new ListViewItem(new[]
            {
                m.On && !m.Missing ? (++n).ToString() : "",
                what,
                m.Missing ? "" : Kb(m.Size),
                m.Missing ? "" : m.Changed.ToString("yyyy-MM-dd"),
                info,
            }) { Checked = m.On, Tag = m };
            // the whole row on hover: long names and notes don't fit their columns
            it.ToolTipText = m.FoundAt != null
                ? what + "\n" + info + "\n" + Tr("The game only loads archives that sit directly in the Data folder.")
                : what + (info is "" or "..." ? "" : "\n" + info);
            Color? fc = m.VortexOff ? Ui.ToTheme(m.On ? Color.SteelBlue : Color.Gray)
                : m.Missing ? Ui.ToTheme(Color.Firebrick)
                : !m.On ? Ui.ToTheme(Color.Gray) : null;
            if (fc != null) it.ForeColor = fc.Value;
            if (reuse)
            {
                var old = list.Items[row];
                for (int i = 0; i < it.SubItems.Count; i++) old.SubItems[i].Text = it.SubItems[i].Text;
                old.Checked = it.Checked;
                old.Tag = m;
                old.ToolTipText = it.ToolTipText;
                old.ForeColor = fc ?? list.ForeColor;
                if (m == select) { old.Selected = true; old.Focused = true; }
                else if (old.Selected) old.Selected = false;
            }
            else
            {
                list.Items.Add(it);
                if (m == select) { it.Selected = true; it.Focused = true; }
            }
            row++;
        }
        list.EndUpdate();
        if (!reuse && top > 0 && top < list.Items.Count) list.TopItem = list.Items[top];
        if (select != null && list.SelectedItems.Count > 0) list.SelectedItems[0].EnsureVisible();
        moveToData.Enabled = Selected()?.FoundAt != null;
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
            int same = Same(m, o);
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
        if (mods.Any(m => m.Link)) lines.Add(Tr("Vortex manages some of these mods - when it deploys, it keeps only its own mods in the list. Use Hand to Vortex for the others."));
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
        dirty = !Current().SequenceEqual(original, StringComparer.OrdinalIgnoreCase) || mods.Any(m => m.VortexOff && m.On);
        save.Enabled = dirty;
    }

    /// <summary>The ticked mods in order: what Save writes to the ini. Vortex mods being turned on aren't in Data yet
    /// (Vortex's deploy adds them to the list itself).</summary>
    List<string> Current() => mods.Where(m => m.On && !(m.VortexOff && !original.Contains(m.Name, StringComparer.OrdinalIgnoreCase))).Select(m => m.Name).ToList();

    /// <summary>Deletes the picked mod's file from Data for good and takes it out of the ini (after asking).</summary>
    void Delete()
    {
        if (Selected() is not Mod m) { MessageBox.Show(this, Tr("Pick a mod in the list first."), Text); return; }
        if (m.VortexOff) { MessageBox.Show(this, Tf("{0} is already off in Vortex and not in the game. To delete it for good, remove it in Vortex.", m.Name), Tr("Delete mod")); return; }
        if (!m.Missing && GameData.GameRunning) { MessageBox.Show(this, Tr("Close Fallout 76 first - the game locks its mods while it runs."), Text); return; }
        var ask = m.Missing
            ? Tf("{0} is not in Data. Take it out of {1}?", m.Name, game.IniName)
            : Tf("Delete {0} ({1}) from the Data folder for good and take it out of {2}?\n\nThis can't be undone.", m.Name, Kb(m.Size), game.IniName);
        Vortex.Mod? owner = null;
        if (m.Link)
        {
            if (Vortex.Running) { MessageBox.Show(this, Tr("This is a Vortex mod. Close Vortex first - ESP Maker then turns it off in Vortex too (otherwise Vortex puts it back when it deploys)."), Tr("Delete mod")); return; }
            owner = vortex is { } v ? Vortex.Owner(v.Mods, m.Name) : null;
            ask = owner != null
                ? Tf("{0} is a Vortex mod. Turn it off in Vortex and take it out of the game?\n\nIt stays in Vortex's mod list (off) - remove it there to delete it for good.", m.Name)
                : ask + Tr("\n\nThis is a Vortex mod: deleting removes only Vortex's link, and Vortex puts it back when it deploys. To get rid of it, remove it in Vortex instead.");
        }
        if (MessageBox.Show(this, ask, Tr("Delete mod"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            if (owner != null && !Vortex.SetEnabled(vortex!.Value.Profile, new[] { (owner.Id, false) }, log))
            {
                MessageBox.Show(this, Tr("Vortex didn't take the change - nothing was deleted. Turn the mod off in Vortex instead."), Tr("Delete mod"));
                return;
            }
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

    /// <summary>A mod copied in by hand becomes a Vortex mod: packed (with its " - Textures" archive), taken out of
    /// Data + ini, and installed by Vortex - so Vortex's deploy no longer drops it from the ini.</summary>
    void HandToVortex()
    {
        if (Selected() is not { Missing: false, Link: false } m) return;
        if (GameData.GameRunning) { MessageBox.Show(this, Tr("Close Fallout 76 first - the game locks its mods while it runs."), Text); return; }
        var names = new List<string> { m.Name };
        var tex = Path.GetFileNameWithoutExtension(m.Name) + " - Textures.ba2";
        if (mods.FirstOrDefault(o => o.Name.Equals(tex, StringComparison.OrdinalIgnoreCase)) is { Missing: false, Link: false } t) names.Add(t.Name);
        if (MessageBox.Show(this, Tf("Hand {0} to Vortex?\n\nESP Maker packs it, takes it out of Data and starts Vortex's install. Confirm it in Vortex - Vortex then puts it back in the game and keeps it in the mod list (also after deploys).", string.Join(" + ", names)),
                Tr("Hand to Vortex"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        UseWaitCursor = true;
        bool ready = Vortex.ReadyForInstall(this, log); // Vortex on Fallout 76, not another game
        UseWaitCursor = false;
        if (!ready) return;
        try
        {
            var zip = Vortex.Pack(names.Select(n => Path.Combine(game.DataFolder, n)), Path.GetFileNameWithoutExtension(m.Name));
            game.Uninstall(names, log);
            mods.RemoveAll(o => names.Contains(o.Name, StringComparer.OrdinalIgnoreCase));
            original.RemoveAll(n => names.Contains(n, StringComparer.OrdinalIgnoreCase));
            Saved = true;
            Fill(null);
            Changed();
            Vortex.Install(zip, log);
        }
        catch (Exception ex)
        {
            log(Tr("Hand to Vortex failed: ") + ex.Message);
            MessageBox.Show(this, Tr("Hand to Vortex failed: ") + ex.Message, Text);
        }
    }

    bool Save()
    {
        var names = Current();
        // Vortex mods ticked on / off: the same in Vortex, or its next deploy undoes it
        var flipped = mods.Where(m => (m.Link && m.On != original.Contains(m.Name, StringComparer.OrdinalIgnoreCase)) || (m.VortexOff && m.On)).ToList();
        if (flipped.Count > 0)
        {
            if (Vortex.Running)
            {
                var r = MessageBox.Show(this, Tr("Vortex is open. You turned Vortex mods on or off - Vortex undoes that when it deploys unless ESP Maker changes them in Vortex too.\n\nClose Vortex, then press Retry. Ignore saves anyway."),
                    Text, MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Warning);
                if (r == DialogResult.Abort) return false;
                if (r == DialogResult.Retry) return Save();
            }
            else
            {
                vortex ??= Vortex.ReadMods();
                if (vortex is { } v)
                {
                    var changes = flipped.Select(m => (Owner: Vortex.Owner(v.Mods, m.Name), m.On)).Where(c => c.Owner != null && c.Owner.Enabled != c.On)
                        .Select(c => (c.Owner!.Id, c.On)).Distinct().ToList();
                    if (changes.Count > 0) { UseWaitCursor = true; Vortex.SetEnabled(v.Profile, changes, log); UseWaitCursor = false; }
                }
            }
        }
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
