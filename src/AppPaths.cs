namespace ItemFinder;

/// <summary>Where ESP Maker keeps its files: %AppData%\ESP Maker (settings, the autosave, the mods' setups, built
/// archives, the game index, error.log) - the exe can live anywhere, even in a read-only folder, and an update is just
/// a new exe. Portable mode (a file named "portable.txt" next to the exe - the dev build uses it) keeps everything next
/// to the exe instead.</summary>
public static class AppPaths
{
    static readonly string ExeDir = AppContext.BaseDirectory;

    public static bool Portable => System.IO.File.Exists(Path.Combine(ExeDir, "portable.txt"));

    /// <summary>The data folder (created on first use).</summary>
    public static string Data { get; } = Directory.CreateDirectory(Portable ? ExeDir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ESP Maker")).FullName;

    public static string File(string name) => Path.Combine(Data, name);

    /// <summary>Earlier versions kept their files next to the exe: copied over once (settings, autosave, mods' setups,
    /// saved setups - not the built archives, a new build makes them again, nor the game index, it's read again).</summary>
    public static void MoveOldFiles(Action<string> log)
    {
        // the app was called ESP Forge before 1.0: its data folder just gets the new name
        var forge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ESP Forge");
        if (!Portable && Directory.Exists(forge) && !System.IO.File.Exists(File("settings.json")))
        {
            try
            {
                foreach (var f in Directory.GetFiles(forge, "*", SearchOption.AllDirectories))
                {
                    var to = Path.Combine(Data, Path.GetRelativePath(forge, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    System.IO.File.Move(f, to, true);
                }
                Directory.Delete(forge, true);
                log($"Your files moved from {forge} to {Data} (the app's new name).");
            }
            catch (Exception ex) { log($"Couldn't move {forge} to {Data}: {ex.Message}"); }
            return;
        }
        if (Portable || System.IO.File.Exists(File("settings.json")) || !System.IO.File.Exists(Path.Combine(ExeDir, "settings.json"))) return;
        int copied = 0;
        void Copy(string from, string to)
        {
            if (!System.IO.File.Exists(from) || System.IO.File.Exists(to)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            System.IO.File.Copy(from, to);
            copied++;
        }
        try
        {
            foreach (var name in new[] { "settings.json", "autosave.itemfinder.json" }) Copy(Path.Combine(ExeDir, name), File(name));
            foreach (var dir in new[] { "mods", "output" })
            {
                var from = Path.Combine(ExeDir, dir);
                if (!Directory.Exists(from)) continue;
                foreach (var f in Directory.GetFiles(from, "*.json", SearchOption.AllDirectories))
                    Copy(f, Path.Combine(Data, dir, Path.GetRelativePath(from, f)));
            }
            log($"ESP Maker now keeps its files in {Data} - {copied} file(s) copied from {ExeDir} (the old ones can be deleted).");
        }
        catch (Exception ex) { log($"Couldn't copy the old files from {ExeDir}: {ex.Message}"); }
    }
}
