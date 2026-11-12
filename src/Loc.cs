using System.Text;

namespace ItemFinder;

/// <summary>The app's language: the 13 the game ships (strings\seventysix_&lt;code&gt;.strings in SeventySix - Localization.ba2).
/// English names stay the internal keys everywhere (entry ids, saved setups, curated lists, prefix matching);
/// <see cref="Name"/> turns one into the chosen language for display, search and in-game labels.</summary>
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("it", "Italiano"), ("es", "Español (España)"),
        ("esmx", "Español (Latinoamérica)"), ("pl", "Polski"), ("ptbr", "Português (Brasil)"), ("ru", "Русский"),
        ("ja", "日本語"), ("ko", "한국어"), ("zhhans", "简体中文"), ("zhhant", "繁體中文"),
    };

    /// <summary>The language in use ("en" until <see cref="LoadNames"/> picks another).</summary>
    public static string Lang { get; private set; } = "en";
    public static bool English => Lang == "en";
    /// <summary>Chinese, Japanese, Korean: need fonts with those characters (labels, the window).</summary>
    public static bool Cjk => Code is "ja" or "ko" or "zhhans" or "zhhant";
    /// <summary>The language for fonts: the names' language once read, else the window's.</summary>
    static string Code => Lang != "en" ? Lang : UiLang;

    static readonly Dictionary<string, bool> installed = new();

    /// <summary>The window's font: Segoe UI, or the language's own UI font for Chinese, Japanese and Korean.</summary>
    public static string UiFont => FontName;

    /// <summary>A font with the language's characters (Segoe UI has none for Chinese, Japanese, Korean); every one of
    /// these comes with Windows 10/11, Segoe UI if one is missing.</summary>
    public static string FontName
    {
        get
        {
            string want = Code switch
            {
                "ja" => "Yu Gothic UI", "ko" => "Malgun Gothic", "zhhans" => "Microsoft YaHei UI", "zhhant" => "Microsoft JhengHei UI",
                _ => "Segoe UI",
            };
            lock (installed)
                if (!installed.TryGetValue(want, out var ok))
                    installed[want] = ok = want == "Segoe UI" || System.Drawing.FontFamily.Families.Any(f => f.Name == want);
                else if (ok) return want;
            return installed[want] ? want : "Segoe UI";
        }
    }

    /// <summary>The window's own text: English -> the language picked at start (src\Lang\&lt;code&gt;.json, embedded).
    /// Read once at start (Program.Main) - a language change restarts the app.</summary>
    static Dictionary<string, string> ui = new();
    /// <summary>The language of the window's text (fixed for the session).</summary>
    public static string UiLang { get; private set; } = "en";

    /// <summary>Picks the window's language: <paramref name="lang"/>, or for "auto" the game's (from its ini files).</summary>
    public static void StartUi(string? lang, string? gameFolder)
    {
        if (string.IsNullOrEmpty(lang) || lang == "auto")
            lang = gameFolder != null && Directory.Exists(gameFolder) ? GameLanguage(gameFolder) : "en";
        ui = Table(lang) ?? new();
        UiLang = ui.Count > 0 ? lang : "en";
    }

    /// <summary>A language's table from the exe (null = none).</summary>
    public static Dictionary<string, string>? Table(string lang)
    {
        if (lang == "en") return null;
        using var s = typeof(Loc).Assembly.GetManifestResourceStream($"ItemFinder.Lang.{lang}.json");
        if (s == null) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s); } catch { return null; }
    }

    /// <summary>The window's text in its language (English when the table has no entry).</summary>
    public static string Tr(string english) => ui.TryGetValue(english, out var t) && t.Length > 0 ? t : english;

    /// <summary>Tr with string.Format arguments; a broken translation falls back to the English pattern.</summary>
    public static string Tf(string english, params object?[] args)
    {
        var f = Tr(english);
        try { return string.Format(f, args); }
        catch (FormatException) { return string.Format(english, args); }
    }

    /// <summary>English game text -> the same text in <see cref="Lang"/> (empty for English).</summary>
    static Dictionary<string, string> names = new();

    /// <summary>The language the game runs in: sLanguage from the custom ini, then the My Games and game-folder
    /// Fallout76.ini (Steam writes its language setting there); English if none names one of the 13.</summary>
    public static string GameLanguage(GameData game) => GameLanguage(game.GameFolder);

    public static string GameLanguage(string gameFolder)
    {
        var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Fallout 76");
        bool ms = !File.Exists(Path.Combine(gameFolder, "Fallout76.exe")) && File.Exists(Path.Combine(gameFolder, "Project76_GamePass.exe"));
        string baseIni = ms ? "Project76.ini" : "Fallout76.ini", custom = ms ? "Project76Custom.ini" : "Fallout76Custom.ini";
        foreach (var ini in new[] { Path.Combine(docs, custom), Path.Combine(docs, baseIni), Path.Combine(gameFolder, baseIni) })
            try
            {
                if (!File.Exists(ini)) continue;
                foreach (var line in File.ReadLines(ini))
                {
                    var t = line.Trim();
                    if (!t.StartsWith("sLanguage", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = t.IndexOf('=');
                    if (eq < 0 || t[..eq].Trim().Length != "sLanguage".Length) continue;
                    var code = t[(eq + 1)..].Trim().ToLowerInvariant();
                    if (Languages.Any(l => l.Code == code)) return code;
                }
            }
            catch { }
        return "en";
    }

    /// <summary>Reads the game's names in <paramref name="lang"/> ("auto" = the game's language) and pairs them with the
    /// English ones by string id. Unknown codes or a missing file leave English.</summary>
    public static void LoadNames(GameData game, GameIndex idx, string? lang)
    {
        if (string.IsNullOrEmpty(lang) || lang == "auto") lang = GameLanguage(game);
        names = new();
        Lang = "en";
        if (lang == "en" || !Languages.Any(l => l.Code == lang)) return;
        var en = ReadStrings(game, "en");
        var other = ReadStrings(game, lang);
        if (other.Count == 0) return;
        // one English text can have several translations (grammar, context): the most common one wins, counting
        // record names far above other text ("Lost" the enemy over "lost" in a sentence)
        var votes = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (id, e) in en)
        {
            if (!other.TryGetValue(id, out var t) || t.Length == 0) continue;
            if (!votes.TryGetValue(e, out var v)) votes[e] = v = new();
            v[t] = v.GetValueOrDefault(t) + (idx.NameIds.Contains(id) ? 1000 : 1);
        }
        foreach (var (e, v) in votes) names[e] = v.MaxBy(kv => kv.Value).Key;
        Lang = lang;
    }

    /// <summary>The display text for English game text: the whole text if the game has it, else "Prefix: rest" and
    /// "rest (extra)" translated piece by piece; English where the game has no translation.</summary>
    public static string Name(string english)
    {
        if (string.IsNullOrEmpty(english) || English && ui.Count == 0) return english;
        // the app's own wording first (category names, "Caps stashes", fixes like "Lost"), then the game's
        if (ui.TryGetValue(english, out var t) && t.Length > 0) return t;
        if (names.TryGetValue(english, out t)) return t;
        // "Notes (paper, small)": the bracket first, so its commas stay inside it
        if (english.EndsWith(')') && english.LastIndexOf(" (", StringComparison.Ordinal) is int q and > 0 && english.IndexOf(')') == english.Length - 1)
            return Name(english[..q]) + " (" + Name(english[(q + 2)..^1]) + ")";
        foreach (var sep in new[] { " · ", ": ", " - ", " / ", ", ", " & " })
        {
            int c = english.IndexOf(sep, StringComparison.Ordinal);
            if (c > 0 && c + sep.Length < english.Length)
            {
                string a = Name(english[..c]), b = Name(english[(c + sep.Length)..]);
                if (sep == ": " && Cjk) return a + "：" + b;
                return a + sep + b;
            }
        }
        if (english.EndsWith(')') && english.LastIndexOf(" (", StringComparison.Ordinal) is int p and > 0)
            return Name(english[..p]) + " (" + Name(english[(p + 2)..^1]) + ")";
        Missing?.Add(english);
        return english;
    }

    /// <summary>Dev (--uikeys): collects the pieces <see cref="Name"/> found no translation for.</summary>
    public static HashSet<string>? Missing;

    /// <summary>The game's own translation of this exact English text (null = none).</summary>
    public static string? GameName(string english) => names.TryGetValue(english, out var t) ? t : null;

    /// <summary>Whether the game has this exact English text translated.</summary>
    public static bool Has(string english) => English || names.ContainsKey(english);

    /// <summary>A strings file (id -> text). The files are UTF-8; one that isn't is read in its language's old code page.</summary>
    public static Dictionary<uint, string> ReadStrings(GameData game, string lang)
    {
        var map = new Dictionary<uint, string>();
        var e = game.Find($@"strings\seventysix_{lang}.strings");
        if (e == null) return map;
        var d = e.Archive.ReadFile(e);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var utf8 = new UTF8Encoding(false, true);
        Encoding legacy = Encoding.GetEncoding(lang switch
        {
            "pl" => 1250, "ru" => 1251, "ja" => 932, "ko" => 949, "zhhans" => 936, "zhhant" => 950, _ => 1252,
        });
        int count = BitConverter.ToInt32(d, 0), baseOff = 8 + 8 * count;
        for (int k = 0; k < count; k++)
        {
            uint id = BitConverter.ToUInt32(d, 8 + 8 * k);
            int off = baseOff + BitConverter.ToInt32(d, 12 + 8 * k);
            int end = Array.IndexOf(d, (byte)0, off);
            if (end <= off) continue;
            string s;
            try { s = utf8.GetString(d, off, end - off); }
            catch (DecoderFallbackException) { s = legacy.GetString(d, off, end - off); }
            map[id] = s;
        }
        return map;
    }
}
