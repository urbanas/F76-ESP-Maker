using static ItemFinder.Loc;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;

namespace ItemFinder;

/// <summary>Hands mods to Vortex instead of copying them into Data. Vortex's Fallout 76 extension rewrites
/// sResourceArchive2List on every deploy: with its default setting ("Remove disabled mod archives from INI") it keeps
/// ONLY archives of enabled Vortex mods, so anything copied in by hand drops out of the game. A mod installed through
/// Vortex is recorded by that extension (mod attribute ba2archives) and stays listed. Vortex's own command line:
/// "--install-archive &lt;absolute path&gt;" (an already running Vortex takes it over).</summary>
static class Vortex
{
    /// <summary>Vortex.exe, or null when Vortex isn't installed.</summary>
    public static string? Exe => exe ??= Find();
    static string? exe;

    /// <summary>Vortex is installed and has managed Fallout 76 on this PC (its fallout76 folder exists).</summary>
    public static bool ManagesGame =>
        Exe != null && Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vortex", "fallout76"));

    /// <summary>A file Vortex deployed (it deploys with links).</summary>
    public static bool IsLink(string path) =>
        File.Exists(path) && new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);

    static string? Find()
    {
        // installed: the uninstall entry knows where
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            try
            {
                using var un = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (un == null) continue;
                foreach (var k in un.GetSubKeyNames())
                {
                    using var e = un.OpenSubKey(k);
                    if (e?.GetValue("DisplayName") is not string n || !n.StartsWith("Vortex", StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.GetValue("InstallLocation") is string dir && File.Exists(Path.Combine(dir.Trim('"'), "Vortex.exe")))
                        return Path.Combine(dir.Trim('"'), "Vortex.exe");
                }
            }
            catch { }
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Black Tree Gaming Ltd", "Vortex", "Vortex.exe");
        return File.Exists(pf) ? pf : null;
    }

    /// <summary>Packs the .ba2 files into "&lt;name&gt;.zip" (same name next time = Vortex offers to replace that mod).</summary>
    public static string Pack(IEnumerable<string> ba2Files, string name)
    {
        var dir = Directory.CreateDirectory(AppPaths.File("vortex")).FullName;
        var zip = Path.Combine(dir, string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".zip");
        if (File.Exists(zip)) File.Delete(zip);
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var f in ba2Files) z.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Fastest);
        return zip;
    }

    /// <summary>Before an install: Vortex installs into the game it has active, whatever "--game" says (user 2026-11-19:
    /// with Graveyard Keeper 2 active our mod went into its downloads). Closed Vortex: its active profile is switched to
    /// its Fallout 76 profile (settings.profiles.lastActiveProfile.fallout76). Open Vortex (state locked): the user is
    /// asked to switch it. False = cancelled.</summary>
    public static bool ReadyForInstall(IWin32Window? owner, Action<string> log)
    {
        string switchIt = Tr("Vortex installs mods into the game it is showing. Switch Vortex to Fallout 76 first, then press OK.");
        if (Running) return MessageBox.Show(owner, switchIt, "Vortex", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
        var fo76 = Fo76Profile();
        if (fo76 == null || ActiveProfile() == fo76) return true;
        Run("--set", $"settings.profiles.activeProfileId=\"{fo76}\"", "--set", $"settings.profiles.nextProfileId=\"{fo76}\"");
        if (ActiveProfile() == fo76) { log(Tr("Vortex switched to Fallout 76.")); return true; }
        return MessageBox.Show(owner, switchIt, "Vortex", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
    }

    static string? ActiveProfile() => Value(Run("--get", "settings.profiles.activeProfileId"), "settings.profiles.activeProfileId = ")?.Trim('"');

    /// <summary>Vortex's Fallout 76 profile (the one it switches to for the game), whatever game is active now.</summary>
    static string? Fo76Profile() =>
        Value(Run("--get", "settings.profiles.lastActiveProfile.fallout76"), "settings.profiles.lastActiveProfile.fallout76 = ")?.Trim('"') is { Length: > 0 } p ? p : null;

    /// <summary>Starts Vortex's install of the archive (.zip / .7z / .rar). The user confirms it in Vortex.</summary>
    public static void Install(string archive, Action<string> log)
    {
        var full = Path.GetFullPath(archive);
        Process.Start(Start("--game", "fallout76", "--install-archive", full));
        log(Tf("Handed {0} to Vortex - finish the install there (Vortex enables and deploys it; the game loads it after that).", Path.GetFileName(full)));
    }

    static ProcessStartInfo Start(params string[] args)
    {
        var psi = new ProcessStartInfo(Exe!) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment.Remove("ELECTRON_RUN_AS_NODE"); // set when started from an Electron app: Vortex then fails with "bad option"
        return psi;
    }

    /// <summary>Vortex is open: its saved state is locked (the --get / --set below only work while it is closed).</summary>
    public static bool Running => Process.GetProcessesByName("Vortex").Length > 0;

    /// <summary>A Vortex mod of Fallout 76: its id in Vortex, its .ba2 files and whether the active profile has it on.</summary>
    public sealed record Mod(string Id, List<string> Archives, bool Enabled);

    /// <summary>Reads Vortex's Fallout 76 mods (Vortex must be closed; takes a few seconds - Vortex starts headless).
    /// Null when it can't be read.</summary>
    public static (string Profile, List<Mod> Mods)? ReadMods()
    {
        if (Exe == null || Running) return null;
        // the Fallout 76 profile, not the active one: Vortex may be on another game (user 2026-11-19)
        var profile = Fo76Profile();
        if (profile == null) return null;
        // ids can hold dots, so they are cut between the known prefix and suffix
        var lines = Run("--get", "persistent.mods.fallout76", "--get", $"persistent.profiles.{Esc(profile)}.modState");
        const string modPre = "persistent.mods.fallout76.", ba2 = ".attributes.ba2archives = ";
        string statePre = $"persistent.profiles.{profile}.modState.", en = ".enabled = ";
        var on = new Dictionary<string, bool>(StringComparer.Ordinal);
        var archives = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            int i;
            if (l.StartsWith(statePre, StringComparison.Ordinal) && (i = l.LastIndexOf(en, StringComparison.Ordinal)) > statePre.Length)
                on[l[statePre.Length..i]] = l[(i + en.Length)..].Trim() == "true";
            else if (l.StartsWith(modPre, StringComparison.Ordinal) && (i = l.LastIndexOf(ba2, StringComparison.Ordinal)) > modPre.Length)
                try { archives[l[modPre.Length..i]] = System.Text.Json.JsonSerializer.Deserialize<List<string>>(l[(i + ba2.Length)..]) ?? new(); }
                catch { }
        }
        return (profile, archives.Select(a => new Mod(a.Key, a.Value, on.TryGetValue(a.Key, out var e) && e)).ToList());
    }

    /// <summary>Turns mods on / off in Vortex's active profile (Vortex must be closed). Vortex's next deploy then
    /// adds / removes their files and ini entries itself.</summary>
    public static bool SetEnabled(string profile, IEnumerable<(string Id, bool On)> mods, Action<string> log)
    {
        if (Exe == null || Running) return false;
        var args = new List<string>();
        foreach (var (id, isOn) in mods)
        {
            args.Add("--set"); args.Add($"persistent.profiles.{Esc(profile)}.modState.{Esc(id)}.enabled={(isOn ? "true" : "false")}");
        }
        if (args.Count == 0) return true;
        var output = Run(args.ToArray());
        bool ok = output.Any(l => l.Trim() == "changed");
        log(ok ? Tf("Changed {0} mod(s) in Vortex - Vortex applies it on its next deploy.", args.Count / 2)
               : Tr("Vortex didn't take the change: ") + string.Join(" ", output.Where(l => !l.StartsWith("BROKEN LOGGING"))));
        return ok;
    }

    /// <summary>Vortex splits state paths at dots; a dot inside a name is escaped with a backslash.</summary>
    static string Esc(string key) => key.Replace("\\", "\\\\").Replace(".", "\\.");

    static string? Value(List<string> lines, string prefix) =>
        lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) is string l ? l[prefix.Length..] : null;

    static List<string> Run(params string[] args)
    {
        var psi = Start(args);
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var lines = new List<string>();
        while (p.StandardOutput.ReadLine() is string l) lines.Add(l);
        if (!p.WaitForExit(60_000)) { try { p.Kill(); } catch { } }
        lines.AddRange(err.Result.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0));
        return lines;
    }

    /// <summary>The Vortex mod that owns this .ba2 (by name), or null.</summary>
    public static Mod? Owner(List<Mod> mods, string ba2) =>
        mods.FirstOrDefault(m => m.Archives.Contains(ba2, StringComparer.OrdinalIgnoreCase));
}
