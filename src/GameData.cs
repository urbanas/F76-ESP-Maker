using static ItemFinder.Loc;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ItemFinder;

/// <summary>Finds the game, reads the mod list from its custom ini and indexes every archive
/// so a file can be looked up the way the game would load it (mods first, last listed wins).
/// Two PC versions: Steam (Fallout76.exe, Fallout76Custom.ini) and Microsoft - the Microsoft Store / PC Game Pass
/// build run by the Xbox app (Project76_GamePass.exe, Project76Custom.ini; mods need the Xbox app's "Enable mods").</summary>
public sealed class GameData
{
    public string GameFolder { get; }
    public string DataFolder => Path.Combine(GameFolder, "Data");

    const string SteamExe = "Fallout76.exe", MicrosoftExe = "Project76_GamePass.exe";
    /// <summary>The Microsoft Store / Game Pass version (else Steam).</summary>
    public bool Microsoft => !File.Exists(Path.Combine(GameFolder, SteamExe)) && File.Exists(Path.Combine(GameFolder, MicrosoftExe));
    public string Edition => Microsoft ? Tr("Microsoft Store / Game Pass") : Tr("Steam");
    /// <summary>A folder with either version's exe.</summary>
    public static bool IsGameFolder(string folder) => File.Exists(Path.Combine(folder, SteamExe)) || File.Exists(Path.Combine(folder, MicrosoftExe));
    /// <summary>The custom ini the game reads (Fallout76Custom.ini / Project76Custom.ini).</summary>
    public string IniName => Microsoft ? "Project76Custom.ini" : "Fallout76Custom.ini";
#if DEVTOOLS
    /// <summary>Dev (--installtest): an ini outside My Documents, so the test never touches the real one.</summary>
    public static string? TestIni;
    public string IniPath => TestIni ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Fallout 76", IniName);
#else
    public string IniPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Fallout 76", IniName);
#endif

    readonly Dictionary<string, Ba2Entry> index = new();
    readonly Dictionary<string, Ba2Entry> vanilla = new();
    /// <summary>Archives in the mod list that were built by this app (skipped as starting files).</summary>
    public List<string> OwnInstalled { get; } = new();
    public IReadOnlyCollection<string> AllPaths => index.Keys;

    GameData(string gameFolder) => GameFolder = gameFolder;

    public static string? FindGameFolder(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return GameFolderIn(overridePath);
        return Candidates().FirstOrDefault(IsGameFolder) is string found ? Path.GetFullPath(found) : null;
    }

    /// <summary>The game folder at or just under a folder the user picked (the Xbox app puts it in "Content").</summary>
    public static string? GameFolderIn(string picked)
    {
        foreach (var f in new[] { picked, Path.Combine(picked, "Content"), Path.Combine(picked, "Fallout 76", "Content") })
            if (IsGameFolder(f)) return Path.GetFullPath(f);
        return null;
    }

    /// <summary>Where the game usually is: Steam libraries first, then the Xbox app's game folders.</summary>
    static IEnumerable<string> Candidates()
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam)
        {
            var libraries = new List<string> { steam };
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (var lib in libraries) yield return Path.Combine(lib, "steamapps", "common", "Fallout76");
        }
        // the Xbox app: each drive's hidden .GamingRoot names its games folder (XboxGames by default)
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var roots = new List<string> { Path.Combine(drive.Name, "XboxGames") };
            roots.AddRange(GamingRoots(drive.Name));
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(root, "Fallout 76", "Content");
                yield return Path.Combine(root, "Fallout76", "Content");
            }
            yield return Path.Combine(drive.Name, "Program Files", "ModifiableWindowsApps", "Fallout76");
            yield return Path.Combine(drive.Name, "Program Files", "ModifiableWindowsApps", "Fallout 76");
        }
    }

    /// <summary>Games folders named in a drive's .GamingRoot (a small header, then UTF-16 paths relative to the drive).</summary>
    static List<string> GamingRoots(string drive)
    {
        var found = new List<string>();
        try
        {
            var file = Path.Combine(drive, ".GamingRoot");
            if (!File.Exists(file)) return found;
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length <= 8) return found;
            foreach (var part in System.Text.Encoding.Unicode.GetString(bytes, 8, (bytes.Length - 8) / 2 * 2).Split('\0'))
            {
                var rel = part.Trim().TrimStart('\\');
                if (rel.Length > 0 && rel.IndexOfAny(Path.GetInvalidPathChars()) < 0) found.Add(Path.Combine(drive, rel));
            }
        }
        catch { }
        return found;
    }

    /// <param name="exclude">Archive file names to ignore (this tool's own output).</param>
    public static GameData Load(string gameFolder, ICollection<string> exclude, Action<string> status)
    {
        var g = new GameData(gameFolder);
        var order = new List<string>();
        var mods = g.ReadArchiveList();
        for (int i = mods.Count - 1; i >= 0; i--)
            if (!exclude.Contains(mods[i], StringComparer.OrdinalIgnoreCase)) order.Add(mods[i]);
        var vanilla = Directory.GetFiles(g.DataFolder, "SeventySix - *.ba2").Select(Path.GetFileName).OfType<string>()
            .OrderBy(f => f.Contains("00Update", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(f => f);
        order.AddRange(vanilla);
        foreach (var name in order)
        {
            var path = Path.Combine(g.DataFolder, name);
            if (!File.Exists(path)) continue;
            status(Tf("Reading {0}...", name));
            try
            {
                // an ESP Maker build under any name: never start from its models (beams would stack up)
                const string textures = " - Textures.ba2";
                var a = Ba2Archive.Open(path);
                bool ownTextures = name.EndsWith(textures, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(g.DataFolder, name[..^textures.Length] + ".ba2"))
                                   && IsOwn(Ba2Archive.Open(Path.Combine(g.DataFolder, name[..^textures.Length] + ".ba2")));
                if (ownTextures || IsOwn(a)) { g.OwnInstalled.Add(name); continue; }
                bool isVanilla = name.StartsWith("SeventySix - ", StringComparison.OrdinalIgnoreCase);
                foreach (var e in a.Entries)
                {
                    g.index.TryAdd(e.Name, e);
                    if (isVanilla) g.vanilla.TryAdd(e.Name, e);
                }
            }
            catch (Exception ex) { status(Tf("Skipped {0}: {1}", name, ex.Message)); }
        }
        status(Tf("Ready - {0:N0} game files indexed.", g.index.Count));
        return g;
    }

    /// <summary>Built by this app: it has our materials or textures, whatever the file is called.</summary>
    public static bool IsOwn(Ba2Archive a) => a.Entries.Any(e => e.Name.StartsWith(@"materials\itemfinder\") || e.Name.StartsWith(@"textures\itemfinder\"));

    readonly Dictionary<string, Ba2Archive?> extra = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks in a specific archive in Data first (loaded or not), then the normal load order.</summary>
    public Ba2Entry? Find(string path, string? preferArchive = null)
    {
        var key = Ba2Path.Normalize(path);
        if (preferArchive != null)
        {
            if (!extra.TryGetValue(preferArchive, out var a))
            {
                var file = Path.Combine(DataFolder, preferArchive);
                a = File.Exists(file) ? Ba2Archive.Open(file) : null;
                extra[preferArchive] = a;
            }
            if (a?.Entries.FirstOrDefault(e => e.Name == key) is Ba2Entry hit) return hit;
        }
        var e = index.GetValueOrDefault(key);
        // a mod turned off / removed while the app is open (Vortex takes its file away): the game's own file instead
        return e != null && !Alive(e.Archive) ? vanilla.GetValueOrDefault(key) : e;
    }

    readonly Dictionary<Ba2Archive, (bool Alive, long At)> alive = new();

    /// <summary>The archive's file is still there (checked at most every 2 seconds per archive).</summary>
    bool Alive(Ba2Archive a)
    {
        long now = Environment.TickCount64;
        lock (alive)
        {
            if (alive.TryGetValue(a, out var c) && now - c.At < 2000) return c.Alive;
            bool ok = File.Exists(a.FilePath);
            alive[a] = (ok, now);
            return ok;
        }
    }

    /// <summary>The game's own version of a file, ignoring every mod.</summary>
    public Ba2Entry? FindOriginal(string path) => vanilla.GetValueOrDefault(Ba2Path.Normalize(path));

    static readonly Regex ListLine = new(@"^\s*sResourceArchive2List\s*=(.*)$", RegexOptions.IgnoreCase);
    static List<string> ArchivesOn(string line) => ListLine.Match(line).Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    /// <summary>Every list line: an ini can have two (hand edits), and which one wins is the game's business, so all are kept up to date.</summary>
    static List<int> ListLines(List<string> lines) => Enumerable.Range(0, lines.Count).Where(i => ListLine.IsMatch(lines[i])).ToList();

    /// <summary>The ini's own encoding: UTF-8, or an old code page, whose bytes Latin-1 reads and writes back unchanged.</summary>
    Encoding IniEncoding()
    {
        try { new UTF8Encoding(false, true).GetCharCount(File.ReadAllBytes(IniPath)); return new UTF8Encoding(false); }
        catch (DecoderFallbackException) { return Encoding.Latin1; }
    }

    public List<string> ReadArchiveList()
    {
        if (!File.Exists(IniPath)) return new();
        return File.ReadAllLines(IniPath, IniEncoding()).FirstOrDefault(ListLine.IsMatch) is string line ? ArchivesOn(line) : new();
    }

    /// <summary>Every mod archive in Data (all .ba2 but the game's own "SeventySix - " files), by name.</summary>
    public List<string> ModArchivesInData() =>
        Directory.Exists(DataFolder)
            ? Directory.GetFiles(DataFolder, "*.ba2").Select(Path.GetFileName).OfType<string>()
                .Where(n => !n.StartsWith("SeventySix - ", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
            : new();

    /// <summary>Mod manager: the ini's list becomes exactly this order (every list line; one is added under [Archive]
    /// if there is none). The ini as it was is kept as "....modmanager-backup" (one step back).</summary>
    public void WriteArchiveList(IReadOnlyList<string> names, Action<string> log)
    {
        if (!File.Exists(IniPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IniPath)!);
            File.WriteAllLines(IniPath, new[] { "[Archive]", "sResourceDataDirsFinal=", "bInvalidateOlderFiles=1" });
            log(Tf("Created {0}.", IniPath));
        }
        var encoding = IniEncoding();
        var lines = File.ReadAllLines(IniPath, encoding).ToList();
        var text = "sResourceArchive2List=" + string.Join(",", names);
        var listLines = ListLines(lines);
        foreach (int i in listLines) lines[i] = text;
        if (listLines.Count == 0)
        {
            int arch = lines.FindIndex(l => l.Trim().Equals("[Archive]", StringComparison.OrdinalIgnoreCase));
            if (arch < 0) { lines.Add("[Archive]"); arch = lines.Count - 1; }
            lines.Insert(arch + 1, text);
        }
        File.Copy(IniPath, IniPath + ".modmanager-backup", true);
        File.WriteAllLines(IniPath, lines, encoding);
        log(Tf("Saved the mod list in {0} ({1} on).", IniName, names.Count));
    }

    /// <summary>Removes the archives from Data and from the ini list.</summary>
    public void Uninstall(IReadOnlyList<string> names, Action<string> log)
    {
        foreach (var n in names)
        {
            var p = Path.Combine(DataFolder, n);
            if (File.Exists(p)) { File.Delete(p); log($"Removed {n} from {DataFolder}"); }
        }
        if (!File.Exists(IniPath)) return;
        var encoding = IniEncoding();
        var lines = File.ReadAllLines(IniPath, encoding).ToList();
        bool changed = false;
        foreach (int i in ListLines(lines))
        {
            var list = ArchivesOn(lines[i]);
            if (list.RemoveAll(n => names.Contains(n, StringComparer.OrdinalIgnoreCase)) == 0) continue;
            lines[i] = "sResourceArchive2List=" + string.Join(",", list);
            changed = true;
        }
        if (!changed) return;
        File.WriteAllLines(IniPath, lines, encoding);
        log(Tf("Removed it from {0}.", IniName));
    }

    /// <summary>Either version of the game is running (it locks its archives).</summary>
    public static bool GameRunning => Process.GetProcessesByName("Fallout76").Length > 0 || Process.GetProcessesByName("Project76_GamePass").Length > 0;

    /// <summary>Copies the archives into Data, lists them at the end of sResourceArchive2List (so they win
    /// over other mods) and removes stale files from an earlier build that are no longer produced.</summary>
    public void Install(IReadOnlyList<string> builtFiles, IReadOnlyList<string> staleNames, Action<string> log)
    {
        foreach (var f in builtFiles)
        {
            File.Copy(f, Path.Combine(DataFolder, Path.GetFileName(f)), true);
            log(Tf("Copied {0} to {1}", Path.GetFileName(f), DataFolder));
        }
        foreach (var s in staleNames)
        {
            var p = Path.Combine(DataFolder, s);
            if (File.Exists(p)) { File.Delete(p); log($"Removed old {s}"); }
        }
        // the game never makes its custom ini itself (players add it for mods): create it if it isn't there
        if (!File.Exists(IniPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IniPath)!);
            File.WriteAllLines(IniPath, new[] { "[Archive]", "sResourceDataDirsFinal=", "bInvalidateOlderFiles=1" }); // the usual mod setup
            log(Tf("Created {0}.", IniPath));
        }
        var encoding = IniEncoding();
        var lines = File.ReadAllLines(IniPath, encoding).ToList();
        var names = builtFiles.Select(Path.GetFileName).OfType<string>().ToList();
        var listLines = ListLines(lines);
        foreach (int i in listLines)
        {
            var list = ArchivesOn(lines[i]);
            list.RemoveAll(n => staleNames.Contains(n, StringComparer.OrdinalIgnoreCase) || names.Contains(n, StringComparer.OrdinalIgnoreCase));
            list.AddRange(names);
            lines[i] = "sResourceArchive2List=" + string.Join(",", list);
        }
        if (listLines.Count == 0)
        {
            int arch = lines.FindIndex(l => l.Trim().Equals("[Archive]", StringComparison.OrdinalIgnoreCase));
            if (arch < 0) { lines.Add("[Archive]"); arch = lines.Count - 1; }
            lines.Insert(arch + 1, "sResourceArchive2List=" + string.Join(",", names));
        }
        var backup = IniPath + ".before-espmaker";
        if (!File.Exists(backup)) File.Copy(IniPath, backup);
        File.WriteAllLines(IniPath, lines, encoding);
        log(Tf("Updated {0} (archives listed last so they win).", IniName));
    }
}
