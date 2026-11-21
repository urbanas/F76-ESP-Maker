using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ItemFinder;

/// <summary>The sorting rules: which models make up each enemy, event and boss, and the lists the catalog filters with.
/// They live in rules.json (repo rules\rules.json, embedded in the exe), so item fixes can reach players without a new
/// version: the newest valid copy of the bundled one, the downloaded one (cache in the data folder) and one placed next to
/// the exe (Nexus optional file) wins. Category names, translations keys and all logic stay in code.</summary>
public sealed class Rules
{
    /// <summary>The newest file layout this app reads.</summary>
    public const int KnownSchema = 1;
    public const string OnlineUrl = "https://raw.githubusercontent.com/urbanas/F76-ESP-Maker/main/rules/rules.json";
    public const string NexusUrl = "https://www.nexusmods.com/fallout76/mods/4445";

    public int Schema { get; set; } = KnownSchema;
    /// <summary>Goes up on every published change; the app only takes a higher one.</summary>
    public int Version { get; set; }
    /// <summary>The oldest app version that can read these rules.</summary>
    public string MinApp { get; set; } = "2.5";
    /// <summary>The newest app version (a notice when the running app is older).</summary>
    public string? LatestApp { get; set; }
    public string Updated { get; set; } = "";
    public EnemyRules Enemies { get; set; } = new();
    public EventRules Events { get; set; } = new();
    public CatalogRules Catalog { get; set; } = new();
    public PartNameRules PartNames { get; set; } = new();
    /// <summary>Where the pick-up ball is offered and built (Pickup.Allowed) - can be widened or narrowed online.</summary>
    public PickupRules Pickup { get; set; } = new();
    /// <summary>Game materials (.bgsm paths "materials\...", any slashes / case) that Bright breaks in game (Razorgrain:
    /// shattered mirror shards, 2026-11-17): a model using one gets a fill glow in its own colour instead.</summary>
    public List<string> BrightGlow { get; set; } = new();
    public bool BreaksBright(string material) => BrightGlow.Any(m => Ba2Path.Normalize(m).Equals(material, StringComparison.OrdinalIgnoreCase));
    /// <summary>Extra presets (More - Add presets), and the keys of built-in ones to take away (an event that is over).</summary>
    public List<PresetRule> Presets { get; set; } = new();
    public List<string> RetirePresets { get; set; } = new();
    /// <summary>Fixes on single models: take one out of a category, add one, name a part.</summary>
    public List<ModelRule> Exclude { get; set; } = new();
    public List<ModelRule> Include { get; set; } = new();
    public List<ModelRule> Rename { get; set; } = new();
    /// <summary>Whole new categories (app 2.5.5+, user 2026-11-19: Stable Flux plants). Older apps don't know the field and
    /// skip it - so no schema change; never make anything else depend on it.</summary>
    public List<CategoryRule> Categories { get; set; } = new();
    /// <summary>Window text for names the rules bring before the next app version: language -> English -> text.</summary>
    public Dictionary<string, Dictionary<string, string>> Translations { get; set; } = new();
    /// <summary>What each version changed, shown before the restart. Version 0 = the next push (push-rules.ps1 fills it in).</summary>
    public List<ChangeRule> Changes { get; set; } = new();

    /// <summary>The changes after <paramref name="version"/>, newest first, in the window's language when the rules
    /// translate them ("" = none listed).</summary>
    public string ChangesSince(int version)
    {
        var t = Loc.UiLang != "en" && Translations.TryGetValue(Loc.UiLang, out var x) ? x : null;
        string Tr(string s) => t != null && t.TryGetValue(s, out var y) && y.Length > 0 ? y : s;
        var list = Changes.Where(c => c.Version > version && c.Text.Count > 0).OrderByDescending(c => c.Version).ToList();
        return string.Join("\n\n", list.Select(c =>
            (list.Count > 1 ? Loc.Tf("Version {0}", c.Version) + "\n" : "") + string.Join("\n", c.Text.Select(s => "• " + Tr(s)))));
    }

    /// <summary>True when only the header and change notes differ (a release announcement, no item fix) - user 2026-11-22:
    /// the "Item fixes" dialog for a version notice was confusing.</summary>
    public bool SameFixesAs(Rules other) => FixesJson(this) == FixesJson(other);

    static string FixesJson(Rules r)
    {
        var c = JsonSerializer.Deserialize<Rules>(JsonSerializer.Serialize(r, Options), Options)!;
        c.Version = 0; c.LatestApp = null; c.Updated = ""; c.MinApp = ""; c.Changes = new(); c.Translations = new();
        return JsonSerializer.Serialize(c, Options);
    }

    // ---- loading

    /// <summary>The rules in use (set at start; a newer download takes effect after a restart).</summary>
    public static Rules Current { get; private set; } = new();
    /// <summary>Where <see cref="Current"/> came from: "bundled", "downloaded" or "file".</summary>
    public static string Source { get; private set; } = "bundled";

    public static string CachePath => AppPaths.File("rules.json");
    static string BesideExe => Path.Combine(AppContext.BaseDirectory, "rules.json");

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Picks the newest valid rules (no network). A broken file is skipped; the bundled one always works.</summary>
    public static void Load(Action<string>? log = null)
    {
        var best = Bundled();
        string source = "bundled";
        foreach (var (path, from) in new[] { (CachePath, "downloaded"), (BesideExe, "file") }.DistinctBy(p => Path.GetFullPath(p.Item1).ToLowerInvariant()))
        {
            if (!File.Exists(path)) continue;
            var (r, error) = Parse(File.ReadAllText(path));
            if (r == null) { log?.Invoke(Loc.Tf("Item fixes in {0} were skipped: {1}", path, error)); continue; }
            if (r.Version > best.Version) { best = r; source = from; }
        }
        Use(best, source);
    }

    /// <summary>Rules from a file the user picked (Options - Load item fixes file): copied to the cache when valid and newer.</summary>
    public static string? Import(string path, out Rules? rules)
    {
        rules = null;
        string text;
        try { text = File.ReadAllText(path); } catch (Exception ex) { return ex.Message; }
        var (r, error) = Parse(text);
        if (r == null) return error;
        rules = r;
        if (r.Version > Current.Version) Save(text);
        return null;
    }

    public static void Use(Rules r, string source)
    {
        Current = r;
        Source = source;
        regexes.Clear();
        Loc.AddUi(r.Translations);
    }

    public static Rules Bundled()
    {
        using var s = typeof(Rules).Assembly.GetManifestResourceStream("ItemFinder.rules.json")
                      ?? throw new InvalidOperationException("rules.json is missing from the exe");
        using var reader = new StreamReader(s);
        var (r, error) = Parse(reader.ReadToEnd());
        return r ?? throw new InvalidDataException("the bundled rules.json is broken: " + error);
    }

    /// <summary>Reads and checks a rules file: null and the reason when it can't be used by this app.</summary>
    public static (Rules? Rules, string Error) Parse(string json)
    {
        Rules? r;
        try { r = JsonSerializer.Deserialize<Rules>(json, Options); }
        catch (Exception ex) { return (null, ex.Message); }
        if (r == null) return (null, "empty file");
        return r.Problem() is string p ? (null, p) : (r, "");
    }

    /// <summary>Why these rules can't be used (null = fine): unknown layout, made for a newer app, a broken pattern or a
    /// missing list.</summary>
    public string? Problem()
    {
        if (Schema > KnownSchema) return $"made for a newer ESP Maker (layout {Schema})";
        if (NewerThanApp(MinApp)) return $"needs ESP Maker {MinApp} or newer";
        if (Enemies.Kinds.Count == 0 || Events.All.Count == 0) return "lists missing";
        foreach (var p in Patterns())
            try { _ = new Regex(p, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException) { return $"broken pattern: {p}"; }
        foreach (var m in Exclude.Concat(Include).Concat(Rename))
            if (string.IsNullOrWhiteSpace(m.Model)) return "a model fix without a model";
        if (Include.Any(m => string.IsNullOrWhiteSpace(m.Category) || string.IsNullOrWhiteSpace(m.Entry))) return "an include without a category or entry";
        if (Rename.Any(m => string.IsNullOrWhiteSpace(m.Name))) return "a rename without a name";
        if (Categories.Any(c => string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Id))) return "a category without a name or id";
        if (Categories.GroupBy(c => c.Id.Trim().ToLowerInvariant()).Any(g => g.Count() > 1)) return "two categories with one id";
        return null;
    }

    IEnumerable<string> Patterns()
    {
        var e = Enemies;
        foreach (var p in new[] { e.SkipRace, e.NotBody, e.SkinExtra, e.NoBeam, e.HumanSkip, e.HumanNoBeam, e.LocationNpc,
                                  Events.HeadPiece, Catalog.Junk, Catalog.NothingDrawn, Catalog.CampId })
            yield return p;
        foreach (var r in e.RobotParts) yield return r.Pattern;
        foreach (var h in e.HumanGroups.Where(h => h.Beam.Length > 0)) yield return h.Beam;
        foreach (var ev in Events.All) { yield return ev.Prefixes; yield return ev.OwnModels; }
        foreach (var c in Categories)
            foreach (var p in new[] { c.HarvestGives, c.Match, c.Skip }) if (p != null) yield return p;
    }

    /// <summary>True when <paramref name="version"/> is newer than the running app.</summary>
    public static bool NewerThanApp(string? version) =>
        System.Version.TryParse(version, out var v) && System.Version.TryParse(MainForm.AppVersion, out var app) && Normal(v) > Normal(app);

    static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static void Save(string json)
    {
        var tmp = CachePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, CachePath, true);
    }

    // ---- online

    /// <summary>One GET of rules.json from GitHub (5 s, nothing sent but the request). The newer valid rules are saved to the
    /// cache (they apply after a restart); returns them, or null with the reason (also when there's nothing newer).</summary>
    public static async Task<(Rules? Newer, Rules? Fetched, string Note)> CheckOnline()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ESP-Maker/" + MainForm.AppVersion);
            var json = await http.GetStringAsync(OnlineUrl);
            var (r, error) = Parse(json);
            if (r == null) return (null, null, error);
            if (r.Version <= Current.Version) return (null, r, "");
            Save(json);
            return (r, r, "");
        }
        catch (Exception ex) { return (null, null, ex is TaskCanceledException ? "no answer in 5 s" : ex.Message); }
    }

    // ---- fixes on single models (run on the built catalog: EventCatalog.BuildAll)

    static bool Same(string a, string? b) => b != null && string.Equals(a, b.Trim(), StringComparison.OrdinalIgnoreCase);
    /// <summary>The model is the rule's (paths with / or \, with or without meshes\; a path ending with a slash = a folder).</summary>
    static bool Hit(string model, string rule)
    {
        var r = ItemFinder.Catalog.NormalizeModel(rule);
        return r.EndsWith('\\') ? model.StartsWith(r, StringComparison.OrdinalIgnoreCase) : string.Equals(model, r, StringComparison.OrdinalIgnoreCase);
    }
    static IEnumerable<Category> In(List<Category> cats, string? category) => cats.Where(c => category == null || Same(c.Name, category));
    static bool IsEntry(Entry e, string? entry) => entry == null || Same(e.Id, entry) || Same(e.Name, entry);

    /// <summary>The count at the start of an entry's detail ("7 models...") follows a change.</summary>
    static void Recount(Entry e, int was)
    {
        if (e.Detail == Loc.Tr("1 model") || e.Detail == Loc.Tf("{0} models", was))
            e.Detail = e.Models.Count == 1 ? Loc.Tr("1 model") : Loc.Tf("{0} models", e.Models.Count);
        else if (e.Detail.StartsWith(was + " ")) e.Detail = e.Models.Count + e.Detail[was.ToString().Length..];
    }

    /// <summary>Exclude and Include: models out of / into entries. An entry left without models goes.</summary>
    public void FixModels(List<Category> cats, Func<string, bool> exists)
    {
        foreach (var x in Exclude)
            foreach (var c in In(cats, x.Category))
            {
                var emptied = new List<Entry>();
                foreach (var e in c.Entries.Where(e => IsEntry(e, x.Entry) && e.Models.Count > 0))
                {
                    int was = e.Models.Count;
                    if (e.Models.RemoveAll(m => Hit(m, x.Model)) == 0) continue;
                    e.BeamModels?.RemoveWhere(m => Hit(m, x.Model));
                    if (e.BeamModels is { Count: 0 } && e.Models.Count > 0) e.BeamModels.Add(e.Models[0]);
                    foreach (var k in e.PartNames?.Keys.Where(m => Hit(m, x.Model)).ToList() ?? new()) e.PartNames!.Remove(k);
                    Recount(e, was);
                    if (e.Models.Count == 0) emptied.Add(e);
                }
                c.Entries.RemoveAll(emptied.Contains);
                foreach (var g in c.Entries) g.Group?.RemoveAll(emptied.Contains);
            }
        foreach (var x in Include)
        {
            var m = ItemFinder.Catalog.NormalizeModel(x.Model.Trim());
            if (!exists(m) || In(cats, x.Category).FirstOrDefault() is not Category c) continue;
            var e = c.Entries.FirstOrDefault(e => IsEntry(e, x.Entry) && e.Group == null);
            if (e == null)
            {
                e = new Entry
                {
                    Id = $"rule:{c.Name.ToLowerInvariant()}:{x.Entry!.Trim().ToLowerInvariant()}", Category = c.Name, Name = x.Entry.Trim(),
                    ItemNames = new() { x.Entry.Trim() }, Detail = Loc.Tr("1 model"),
                    // worn and creature models are animated (skinned): only the 3D icon survives on them
                    NoIcon = (m.StartsWith(@"actors\") || m.Contains(@"clothes\") || m.Contains(@"powerarmor\")) && !m.EndsWith("_go.nif"),
                };
                c.Entries.Add(e);
                c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (!e.Models.Contains(m, StringComparer.OrdinalIgnoreCase))
            {
                int was = e.Models.Count;
                e.Models.Add(m);
                Recount(e, was);
            }
            if (!e.Models.Contains(m, StringComparer.OrdinalIgnoreCase)) e.Models.Add(m);
            if (!string.IsNullOrWhiteSpace(x.Name)) (e.PartNames ??= new())[m] = x.Name.Trim();
        }
    }

    /// <summary>Categories: new categories from the rules (before the model fixes, so include / exclude reach them too).
    /// Rows copied from <see cref="CategoryRule.From"/> are the same entries there - linked twins (same models).</summary>
    public void AddCategories(List<Category> cats, GameIndex idx, Func<string, bool> exists)
    {
        if (Categories.Count == 0) return;
        var world = idx.Records.Where(r => GameIndex.WorldTypes.Contains(r.Type)).ToList();
        var byModel = world.GroupBy(r => ItemFinder.Catalog.NormalizeModel(r.Model)).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var spots = idx.WorldSpots();
        foreach (var x in Categories)
        {
            if (cats.Any(c => Same(c.Name, x.Name))) continue; // a category the app already has
            var like = x.After == null ? null : cats.FirstOrDefault(c => Same(c.Name, x.After));
            var style = like?.DefaultStyle.Clone() ?? new Style();
            if (x.Color != null) { style.GlowColor = x.Color; style.BeamColor = x.Color; }
            if (x.BeamLength is int bl) style.BeamLength = bl;
            var c = new Category { Name = x.Name.Trim(), Description = Loc.Tr(x.Description), DefaultStyle = style, Section = x.Section ?? like?.Section ?? "ITEMS", Group = x.Group ?? like?.Group };
            string id = x.Id.Trim().ToLowerInvariant();
            // what the plant has to give when harvested (by the given item's name)
            HashSet<string>? gives = null;
            if (x.HarvestGives != null)
            {
                var items = idx.Records.Where(r => r.Name.Trim().Length > 0 && Rx(x.HarvestGives).IsMatch(r.Name.Trim())).Select(r => r.FormId).ToList();
                gives = world.Where(r => r.Type == "FLOR" && r.Harvest != 0 && items.Any(i => idx.Gives(r.Harvest, i)))
                             .Select(r => ItemFinder.Catalog.NormalizeModel(r.Model)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            // Match / Skip: tested on the row's name and each of its models
            bool Any(Entry e, string? p) => p != null && (Rx(p).IsMatch(e.Name) || e.Models.Any(m => Rx(p).IsMatch(m)));
            if (x.From != null && cats.FirstOrDefault(k => Same(k.Name, x.From)) is Category from)
                foreach (var e in from.Entries.Where(e => e.Group == null && e.Models.Count > 0))
                {
                    if (gives != null && !e.Models.Any(gives.Contains)) continue;
                    if (x.Match != null && !Any(e, x.Match) || Any(e, x.Skip)) continue;
                    int colon = e.Id.LastIndexOf(':');
                    c.Entries.Add(new Entry
                    {
                        Id = id + ":" + (colon >= 0 ? e.Id[(colon + 1)..] : e.Id), Category = c.Name, Name = e.Name, Models = e.Models.ToList(),
                        ItemNames = e.ItemNames.ToList(), Detail = e.Detail, Harvestable = e.Harvestable, NoIcon = e.NoIcon,
                        BeamModels = e.BeamModels?.ToHashSet(), Scenery = e.Scenery, Spots = e.Spots, PreviewModel = e.PreviewModel,
                    });
                }
            foreach (var p in x.Entries)
            {
                var models = p.Models.Select(m => ItemFinder.Catalog.NormalizeModel(m.Trim())).Where(exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (models.Count == 0 || p.Name.Trim().Length == 0) continue;
                var e = new Entry
                {
                    Id = id + ":" + p.Name.Trim().ToLowerInvariant(), Category = c.Name, Name = p.Name.Trim(), Models = models,
                    ItemNames = models.SelectMany(m => byModel.GetValueOrDefault(m) ?? new()).Select(r => r.Name.Trim()).Where(n => n.Length > 0)
                                      .Prepend(p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Detail = p.Detail.Length > 0 ? Loc.Tr(p.Detail) : models.Count == 1 ? Loc.Tr("1 model") : Loc.Tf("{0} models", models.Count),
                    Harvestable = models.Any(m => (byModel.GetValueOrDefault(m) ?? new()).Any(r => r.Type == "FLOR")),
                    NoIcon = models.All(m => (m.StartsWith(@"actors\") || m.Contains(@"clothes\") || m.Contains(@"powerarmor\")) && !m.EndsWith("_go.nif")),
                };
                ItemFinder.Catalog.CountSpots(new[] { e }, byModel, spots);
                c.Entries.Add(e);
            }
            if (c.Entries.Count == 0) continue;
            c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            // after the named category (and the rules' categories already put after it), else at the end of its section
            int at = like != null ? cats.IndexOf(like) + 1 : cats.FindLastIndex(k => k.Section == c.Section) + 1;
            while (like != null && at < cats.Count && Categories.Any(r => Same(cats[at].Name, r.Name) && Same(r.After ?? "", like.Name))) at++;
            cats.Insert(at == 0 ? cats.Count : at, c);
        }
    }

    /// <summary>Rename: part names (after the automatic names, so a rule wins).</summary>
    public void RenameParts(List<Category> cats)
    {
        foreach (var x in Rename)
            foreach (var e in In(cats, x.Category).SelectMany(c => c.Entries).Where(e => IsEntry(e, x.Entry)))
                foreach (var m in e.Models.Where(m => Hit(m, x.Model)))
                    (e.PartNames ??= new())[m] = x.Name!.Trim();
    }

    // ---- patterns

    static readonly ConcurrentDictionary<string, Regex> regexes = new();

    /// <summary>A rule's pattern, case-insensitive, with a match timeout (a slow pattern can't hang the app).</summary>
    public static Regex Rx(string pattern) =>
        regexes.GetOrAdd(pattern, p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

    public static string ToJson(Rules r) => JsonSerializer.Serialize(r, Options);
}

public sealed class EnemyRules
{
    /// <summary>Category by race keyword, first match wins.</summary>
    public List<KindRule> Kinds { get; set; } = new();
    /// <summary>Races that aren't enemies (pets, humans, test and cut content).</summary>
    public string SkipRace { get; set; } = "(?!)";
    /// <summary>Files that aren't the creature's body: effects, skeletons, projectiles, severed-limb pieces, creature hats...</summary>
    public string NotBody { get; set; } = "(?!)";
    /// <summary>Skin add-ons drawn on top of the body (alien glass helmet, bloodbug hair, mole rat sores...).</summary>
    public string SkinExtra { get; set; } = "(?!)";
    /// <summary>Small attached parts: they glow, but don't get a beam (one beam per creature).</summary>
    public string NoBeam { get; set; } = "(?!)";
    /// <summary>Robots: part file name pattern -> species. Torso parts carry the beam.</summary>
    public List<NamePattern> RobotParts { get; set; } = new();
    /// <summary>EN06's parts and their names (most are shared with other robots: SharedRobotParts).</summary>
    public Dictionary<string, string> GuardianParts { get; set; } = new();
    public List<SharedParts> SharedRobotParts { get; set; } = new();
    /// <summary>Bosses with models of their own (meshes\ paths; folders end with \). Missing files are skipped.</summary>
    public List<NamedModels> BossModels { get; set; } = new();
    /// <summary>The auras the game hangs on marked enemies (legendaries, bounty targets).</summary>
    public List<NamedModels> MarkedModels { get; set; } = new();
    /// <summary>Human enemies: model folders, the parts that carry the beam (pattern, empty = all but NoBeam ones), a note.</summary>
    public List<HumanGroup> HumanGroups { get; set; } = new();
    /// <summary>Readable outfit names for the parts list (folder names run words together).</summary>
    public Dictionary<string, string> OutfitNames { get; set; } = new();
    /// <summary>Not what others see: first-person arms, dropped-item models.</summary>
    public string HumanSkip { get; set; } = "(?!)";
    public string HumanNoBeam { get; set; } = "(?!)";
    /// <summary>One-off location quest characters whose outfits don't count for their race.</summary>
    public string LocationNpc { get; set; } = @"^LC\d+_";
}

public sealed class KindRule
{
    public string Category { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public string Color { get; set; } = "#FFFFFF";
    public string Description { get; set; } = "";
}

public sealed class NamePattern
{
    public string Name { get; set; } = ""; public string Pattern { get; set; } = "(?!)";
    public void Deconstruct(out string name, out string pattern) => (name, pattern) = (Name, Pattern);
}
public sealed class SharedParts
{
    public string Robot { get; set; } = ""; public List<string> Parts { get; set; } = new();
    public void Deconstruct(out string robot, out List<string> parts) => (robot, parts) = (Robot, Parts);
}
public sealed class NamedModels
{
    public string Name { get; set; } = ""; public List<string> Models { get; set; } = new();
    public void Deconstruct(out string name, out List<string> models) => (name, models) = (Name, Models);
}

public sealed class HumanGroup
{
    public string Name { get; set; } = "";
    public List<string> Folders { get; set; } = new();
    public string Beam { get; set; } = "";
    public string Note { get; set; } = "";
    public void Deconstruct(out string name, out List<string> folders, out string beam, out string note) => (name, folders, beam, note) = (Name, Folders, Beam, Note);
}

public sealed class EventRules
{
    /// <summary>The events section's sub-headings, in the order the tree shows them; an event's group not listed here
    /// goes last. Icon: a <see cref="CatIcons"/> name (unknown = a box, empty = flag).</summary>
    public List<GroupRule> Groups { get; set; } = new();
    /// <summary>One category per event, in this order.</summary>
    public List<EventRule> All { get; set; } = new();
    /// <summary>Costume parts worn on the head (the Spooky Scorched heads entry).</summary>
    public string HeadPiece { get; set; } = "(?!)";
    /// <summary>Event entries that are enemies (outfits the event's enemies wear) - also listed under ENEMIES.</summary>
    public List<string> EnemyPicks { get; set; } = new();
}

/// <summary>The pick-up ball's allow-list. Categories by English name ("Junk: *" = every name starting "Junk: "); Entries =
/// single entry ids allowed in any category (an event object); Block = entry ids never allowed. Empty Categories = the
/// app's built-in list (rules from before the ball).</summary>
public sealed class PickupRules
{
    public List<string> Categories { get; set; } = new();
    public List<string> Entries { get; set; } = new();
    public List<string> Block { get; set; } = new();
}

public sealed class ChangeRule
{
    public int Version { get; set; }
    public List<string> Text { get; set; } = new();
}

public sealed class GroupRule
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
}

public sealed class EventRule
{
    public string Name { get; set; } = "";
    /// <summary>The events section's sub-heading (Seasonal, Public events, Raids &amp; expeditions, Community...).</summary>
    public string Group { get; set; } = "Public events";
    /// <summary>Editor-ID prefixes of the event's records ("(?!)" = none: only the picks).</summary>
    public string Prefixes { get; set; } = "(?!)";
    /// <summary>Words a model path must contain to count as the event's own (an ordinary box the event borrows would
    /// light up the whole map).</summary>
    public string OwnModels { get; set; } = "(?!)";
    public string Color { get; set; } = "#FFFFFF";
    public string Description { get; set; } = "";
    public List<PickRule> Picks { get; set; } = new();
}

/// <summary>A whole new category (app 2.5.5+). Rows: copies of <see cref="From"/>'s entries (filtered), and/or its own
/// <see cref="Entries"/> (name + models). Name, description and details are English; their translations go in
/// <see cref="Rules.Translations"/>.</summary>
public sealed class CategoryRule
{
    public string Name { get; set; } = "";
    /// <summary>Entry ids start with it ("nuked" -> "nuked:blast berry") - keep it once players have ticks saved.</summary>
    public string Id { get; set; } = "";
    /// <summary>Placed right after this category, and starts with its look.</summary>
    public string? After { get; set; }
    /// <summary>ITEMS, EVENTS... (default: the After category's, else ITEMS).</summary>
    public string? Section { get; set; }
    public string? Group { get; set; }
    public string Description { get; set; } = "";
    public string? Color { get; set; }
    public int? BeamLength { get; set; }
    /// <summary>Copy the rows of this category (e.g. "Plants (all)"); they stay ticked together with it.</summary>
    public string? From { get; set; }
    /// <summary>From's rows: only plants whose harvest gives an item whose name matches (e.g. "^Raw .+ Flux$").</summary>
    public string? HarvestGives { get; set; }
    /// <summary>From's rows: only those whose name or one of its models matches / none matches.</summary>
    public string? Match { get; set; }
    public string? Skip { get; set; }
    public List<PickRule> Entries { get; set; } = new();
    public string? Note { get; set; }
}

public sealed class PickRule
{
    public string Name { get; set; } = "";
    public List<string> Models { get; set; } = new();
    public string Detail { get; set; } = "";
}

public sealed class CatalogRules
{
    /// <summary>Editor IDs of cut / test / backup records - their names are hidden when the model has better ones.</summary>
    public string Junk { get; set; } = "(?!)";
    /// <summary>Models that draw nothing (markers, dummies) - left out of the lists.</summary>
    public string NothingDrawn { get; set; } = "(?!)";
    /// <summary>Editor IDs of CAMP objects.</summary>
    public string CampId { get; set; } = "(?!)";
    public List<string> PreWarFoods { get; set; } = new();
    /// <summary>Food effects that boost XP or Intelligence.</summary>
    public List<string> XpEffects { get; set; } = new();
}

public sealed class PartNameRules
{
    /// <summary>Words the game's names don't have but model files use.</summary>
    public List<string> ExtraWords { get; set; } = new();
    /// <summary>File-name words that say nothing to a player.</summary>
    public List<string> Noise { get; set; } = new();
}

/// <summary>A preset from the rules: ticks the entries (by id) or whole categories (by name) and gives them glow, a beam
/// (metres, 0 = none) and an icon ("model:..." = a 3D one).</summary>
public sealed class PresetRule
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";
    public float IconSize { get; set; } = 25;
    public int Beam { get; set; } = 20;
    public List<string> Categories { get; set; } = new();
    public List<string> Entries { get; set; } = new();
}

/// <summary>A fix on one model (or every model in a folder: the path ends with \). Exclude: takes it out (of
/// <see cref="Category"/>, or of every category; <see cref="Entry"/> narrows it to one entry). Include: adds it to the entry
/// (by name or id; a new entry when there is none) in the category, <see cref="Name"/> = its part name. Rename: its part
/// name.</summary>
public sealed class ModelRule
{
    public string Model { get; set; } = "";
    public string? Category { get; set; }
    public string? Entry { get; set; }
    public string? Name { get; set; }
    /// <summary>Why (shown nowhere - for whoever edits the file).</summary>
    public string? Note { get; set; }
}
