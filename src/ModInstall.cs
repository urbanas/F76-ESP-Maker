using static ItemFinder.Loc;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ItemFinder;

/// <summary>Mod manager - Install from archive: a mod downloaded by hand (.zip / .7z / .rar, or a bare .ba2).
/// Fallout 76 mods are .ba2 archives: those are copied straight into Data (wherever they sit inside the download);
/// readmes, pictures, loose files and the like are left out. Returns the names copied (the caller lists them).</summary>
static class ModInstall
{
    public const string Filter = "Mod downloads (*.zip;*.7z;*.rar;*.ba2)|*.zip;*.7z;*.rar;*.ba2|All files (*.*)|*.*";

    public static bool IsBa2(string path) => Path.GetExtension(path).Equals(".ba2", StringComparison.OrdinalIgnoreCase);

    public static bool Supported(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".7z" or ".rar" or ".ba2";

    /// <summary>Installs one download; asks which .ba2 when there are several and before replacing a file in Data.</summary>
    public static List<string> Install(IWin32Window owner, GameData game, string path, Action<string> log)
    {
        var done = new List<string>();
        string file = Path.GetFileName(path);
        if (Path.GetExtension(path).Equals(".ba2", StringComparison.OrdinalIgnoreCase))
        {
            if (!MayReplace(owner, game, file)) return done;
            File.Copy(path, Path.Combine(game.DataFolder, file), true);
            log(Tf("Installed {0} to {1}", file, game.DataFolder));
            done.Add(file);
            return done;
        }

        using var archive = ArchiveFactory.Open(path);
        var all = archive.Entries.Where(e => !e.IsDirectory && e.Key != null).ToList();
        var ba2 = all.Where(e => e.Key!.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase)).Select(e => e.Key!).ToList();
        if (ba2.Count == 0)
        {
            var sample = string.Join("\n", all.Take(12).Select(e => "   " + e.Key));
            MessageBox.Show(owner, Tf("{0} has no .ba2 archive in it, so ESP Maker can't install it.\nIt may need a different install (see the mod's page). It holds:\n\n{1}", file, sample),
                Tr("Install mod"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return done;
        }
        var pick = ba2.Count == 1 ? ba2 : Pick(owner, file, ba2);
        if (pick.Count == 0) return done;
        // the same name twice (e.g. two option folders): only one can sit in Data
        var dupes = pick.GroupBy(k => Path.GetFileName(k), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
        {
            MessageBox.Show(owner, Tf("You picked more than one file named {0}. These are usually alternative versions - pick only one of them.", string.Join(", ", dupes)),
                Tr("Install mod"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return done;
        }
        var want = pick.Where(k => MayReplace(owner, game, Path.GetFileName(k))).ToHashSet(StringComparer.Ordinal);
        if (want.Count == 0) return done;

        // one pass through the archive: a solid .7z is slow to open entry by entry
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            var key = reader.Entry.Key;
            if (reader.Entry.IsDirectory || key == null || !want.Contains(key)) continue;
            var name = Path.GetFileName(key);
            var to = Path.Combine(game.DataFolder, name);
            var tmp = to + ".installing";
            using (var o = File.Create(tmp)) reader.WriteEntryTo(o);
            File.Move(tmp, to, true);
            log(Tf("Installed {0} from {1}", name, file));
            done.Add(name);
        }
        return done;
    }

    static bool MayReplace(IWin32Window owner, GameData game, string name)
    {
        var at = Path.Combine(game.DataFolder, name);
        if (!File.Exists(at)) return true;
        var link = new FileInfo(at).Attributes.HasFlag(FileAttributes.ReparsePoint)
            ? Tr("\n\nIt is a Vortex mod: Vortex may put its own version back when it deploys.") : "";
        return MessageBox.Show(owner, Tf("{0} is already in Data. Replace it with the one from the download?", name) + link,
            Tr("Install mod"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    /// <summary>Several .ba2 in one download: tick the ones to install (all ticked, but only the first of a repeated name).</summary>
    static List<string> Pick(IWin32Window owner, string file, List<string> keys)
    {
        using var f = new Form
        {
            Text = Tr("Install mod"), ShowIcon = false, MinimizeBox = false, MaximizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(620, 380), MinimumSize = new Size(420, 260),
            Font = (owner as Form)?.Font ?? Control.DefaultFont,
        };
        var head = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(12, 10, 12, 0),
            Text = Tf("{0} has {1} mod archives. Tick the ones to install - folders like \"Option A\" usually mean pick one.", file, keys.Count) };
        var box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) box.Items.Add(k, seen.Add(Path.GetFileName(k)));
        var ok = new Button { Text = Tr("Install"), AutoSize = true, Height = 34, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Tr("Cancel"), AutoSize = true, Height = 34, DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8) };
        bar.Controls.AddRange(new Control[] { cancel, ok });
        var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 0) };
        mid.Controls.Add(box);
        f.Controls.Add(mid);
        f.Controls.Add(bar);
        f.Controls.Add(head);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return Ui.Dialog(f).ShowDialog(owner) == DialogResult.OK ? box.CheckedItems.Cast<string>().ToList() : new();
    }
}
