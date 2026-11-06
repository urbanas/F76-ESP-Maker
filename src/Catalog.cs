using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ItemFinder;

/// <summary>How a highlighted thing looks.</summary>
public sealed class Style
{
    public bool Glow { get; set; } = true;
    public string GlowColor { get; set; } = "#00E5FF";
    /// <summary>0..100 %.</summary>
    /// <summary>Glow brightness, 5..100 (100 = what was 40 before setup version 2 - that was too bright).</summary>
    public int GlowOpacity { get; set; } = 30;
    /// <summary>How the glow layer is drawn - for in-game tests, not in the editor. "" = the default, "B": untextured, normal
    /// blending set in the material, no blending block (tested: shows in the Pip-Boy, lights heads and Scorched arms);
    /// non-animated cut-out parts (leaves) use "A". "A" = added light with its own blending block, cut-out parts
    /// textured (shows as flat colour in the Pip-Boy, not on heads). "C" = added light in the material only (made
    /// mirelurks invisible).</summary>
    public string GlowMethod { get; set; } = "";
    /// <summary>Colour over the whole item (hides its texture). Off = an outline: clear where the surface faces you,
    /// coloured towards the edges - like the outline ESP mods, the item keeps its normal look.</summary>
    public bool GlowFill { get; set; }
    /// <summary>The marker above the item is a solid arrow pointing down at it instead of a beam into the sky
    /// (beam colour, strength and width; no length).</summary>
    public bool BeamArrow { get; set; }
    /// <summary>Off by default - beams are added where wanted (category or single item).</summary>
    public bool Beam { get; set; }
    public string BeamColor { get; set; } = "#00E5FF";
    /// <summary>Game units (1 m ≈ 70 units).</summary>
    public int BeamLength { get; set; } = 1000;
    /// <summary>Beam radius in game units (1 unit ≈ 1.43 cm).</summary>
    public float BeamWidth { get; set; } = 3.5f; // radius in game units: 10 cm wide
    /// <summary>Beam brightness, 100 % = the tested Spooky beam.</summary>
    public int BeamStrength { get; set; } = 30;
    public bool ThroughWalls { get; set; } = true;
    /// <summary>Icon above the item / at the top of the beam (<see cref="Icons"/> id), "" = none.</summary>
    public string Icon { get; set; } = "";
    /// <summary>Icon half size in game units.</summary>
    public float IconSize { get; set; } = 24.5f; // 70 cm wide
    /// <summary>A solid 3D shape instead of the flat camera-facing icon (creatures always get 3D).</summary>
    public bool Icon3D { get; set; }
    /// <summary>The item's name floats above it (above the icon), turned to the camera. Not on animated models.</summary>
    public bool Label { get; set; }
    /// <summary>Label text height in game units (half the card height).</summary>
    public float LabelSize { get; set; } = 10.5f; // 30 cm tall
    /// <summary>The label's own text; "" = the item's name.</summary>
    public string LabelText { get; set; } = "";
    /// <summary>Everything added (glow, marker, icon, label) shrinks away this many seconds after the model loads; 0 = never.
    /// Starts again whenever the game loads the model (coming back to the area).</summary>
    public int HideAfter { get; set; }
    /// <summary>The item's own colours shine: its game material gets its colour texture as a glow map. Works on the
    /// material, so everything using that material brightens; nothing is added to the model, nothing shows through walls.</summary>
    public bool Brighten { get; set; }
    /// <summary>Brighten strength, 100 % = the texture's own colours at full light, day or night.</summary>
    public int BrightenStrength { get; set; } = 100;

    public Style Clone() => (Style)MemberwiseClone();
}

/// <summary>One thing the user can tick. Everything sharing one of its models lights up with it.</summary>
public sealed class Entry
{
    public string Id = "";
    public string Category = "";
    public string Name = "";
    /// <summary>Extra info shown next to the name (what it's for, how many items share the model).</summary>
    public string Detail = "";
    public List<string> Models = new();
    /// <summary>Effect gradient textures this entry recolours in its glow colour (the shimmer of invisible enemies).</summary>
    public List<string>? Gradients;
    /// <summary>Every item name that uses one of the models (for search and the "also lights up" list).</summary>
    public List<string> ItemNames = new();
    /// <summary>Plants: the beam/glow is attached to the unharvested state only.</summary>
    public bool Harvestable;
    /// <summary>Weapons: put together from all its models (receiver, barrel, grip...) - the preview shows them as one.</summary>
    public bool Assembled;
    /// <summary>Models that get the beam (null = all). Creatures are several parts at the same spot - one beam each.</summary>
    public HashSet<string>? BeamModels;
    /// <summary>Creatures: the game drops the camera-facing node from animated (skinned) models, so no icon.</summary>
    public bool NoIcon;
    /// <summary>Not ticked together with other entries that share a model (a recipe shares its carrots with others).</summary>
    public bool NoLink;
    /// <summary>Linked part by part, not as a whole: entries with this flag share single parts (EN06 and the robots built
    /// from the same files), and a shared part selected in one is selected in all of them.</summary>
    public bool LinkParts;
    /// <summary>Junk listed again under one of its other scrap components (not its main one).</summary>
    public bool JunkCopy;
    /// <summary>Names for the parts list (model -> "Carrot (plant)"); file names otherwise.</summary>
    public Dictionary<string, string>? PartNames;
    /// <summary>A group of other entries (an XP recipe: its ingredients as they are listed in Plants, Aid &amp; food...).
    /// Ticking it ticks them; it has no models of its own and is never saved as ticked.</summary>
    public List<Entry>? Group;
    /// <summary>Spots in the world where something with these models is placed or can spawn; -1 = not counted
    /// (enemies). 0 = only ever in inventories - never visible in the world (a dropped item becomes a loot bag).</summary>
    public int Spots = -1;
    /// <summary>Editor ids of the records using the models (Everything else only: searchable there).</summary>
    public List<string>? EditorIds;

    public override string ToString() => Name;
}

public sealed class Category
{
    public string Name = "";
    public string Description = "";
    public Style DefaultStyle = new();
    public List<Entry> Entries = new();
    /// <summary>Heading the category is listed under.</summary>
    public string Section = "ITEMS";
    /// <summary>Sub-heading inside the section ("" = none), e.g. "Seasonal" in EVENTS; listed in <see cref="Catalog.GroupOrder"/>.</summary>
    public string Group = "";
    /// <summary>Its entries are groups of other entries (XP recipes). Its look goes to the entries a recipe ticks, as their
    /// own look (Project.RecipeLooks).</summary>
    public bool Groups;
}

/// <summary>The user's setup: what is ticked and how it should look. Saved next to the exe.</summary>
public sealed class Project
{
    public string ModName { get; set; } = "ESPMaker"; // new setups; older ones keep "ItemFinder"
    public Dictionary<string, Style> CategoryStyles { get; set; } = new();
    public HashSet<string> Selected { get; set; } = new();
    public Dictionary<string, Style> Overrides { get; set; } = new();
    /// <summary>Start from other mods' versions of a model (keeps e.g. another ESP mod's highlight); false = the game's original.</summary>
    public bool KeepOtherMods { get; set; } = true;
    /// <summary>Per entry: models (parts) left out of the mod - e.g. a robot's weapons or the EN06 base.</summary>
    public Dictionary<string, HashSet<string>> OffParts { get; set; } = new();
    /// <summary>Parts with their own look (entry id -> model -> look), e.g. the Pint-Sized Slasher mask inside Spooky Scorched.</summary>
    public Dictionary<string, Dictionary<string, Style>> PartLooks { get; set; } = new();

    /// <summary>The models of an entry that go into the mod.</summary>
    public List<string> ModelsOf(Entry e) =>
        OffParts.TryGetValue(e.Id, out var off) ? e.Models.Where(m => !off.Contains(m)).ToList() : e.Models;
    /// <summary>Setup format: 2 = glow strength rescaled (old 40 % = new 100 %); 3 = glow test methods dropped.
    /// Missing in older files (0).</summary>
    public int Version { get; set; }
    /// <summary>The newest setup format (MainForm.MigrateSetup brings older ones up to it).</summary>
    public const int CurrentVersion = 7; // 6: ESP Maker's own enemy colours; 7: junk split by component
    /// <summary>A new, empty setup - already in the newest format (a plain new Project() reads as a very old one).</summary>
    public static Project Fresh() => new() { Version = CurrentVersion };
    /// <summary>Entries whose own look came from ticking an XP recipe (and follows the XP recipes look); unticking them
    /// takes it away again. An Item look set by hand takes them off this list.</summary>
    public HashSet<string> RecipeLooks { get; set; } = new();
    /// <summary>Other mods' archives this project was imported from: never a starting point for builds (their highlight
    /// would stay under ours).</summary>
    public List<string> ImportedFrom { get; set; } = new();

    /// <summary>Brings an older setup up to date (glow strength scale).</summary>
    public void Upgrade()
    {
        if (Version < 2)
            foreach (var st in CategoryStyles.Values.Concat(Overrides.Values))
                st.GlowOpacity = Math.Clamp((int)Math.Round(st.GlowOpacity / 0.4 / 5) * 5, 5, 100);
        if (Version < 3) // the glow test is over: everything uses the method that won
            foreach (var st in CategoryStyles.Values.Concat(Overrides.Values)) st.GlowMethod = "";
        Version = Math.Max(Version, 3); // 4: recipe looks given (MainForm.MigrateSetup - it needs the entries)
    }

    /// <summary>An item's own look, else its category's look. While the category colour is still the default,
    /// things keep their enemy colour (each creature its own, see Palette); a colour the user picks for the
    /// category goes to everything in it.</summary>
    public Style StyleOf(Entry e, Category c)
    {
        if (Overrides.GetValueOrDefault(e.Id) is Style own) return own;
        var cs = CategoryStyles.GetValueOrDefault(c.Name) ?? c.DefaultStyle;
        if (Palette.EnemyColour(e) is not string colour) return cs;
        bool glowDefault = string.Equals(cs.GlowColor, c.DefaultStyle.GlowColor, StringComparison.OrdinalIgnoreCase);
        bool beamDefault = string.Equals(cs.BeamColor, c.DefaultStyle.BeamColor, StringComparison.OrdinalIgnoreCase);
        if (!glowDefault && !beamDefault) return cs;
        var st = cs.Clone();
        if (glowDefault) st.GlowColor = colour;
        if (beamDefault) st.BeamColor = colour;
        return st;
    }

    public static string AutosavePath => AppPaths.File("autosave.itemfinder.json");

    /// <summary>Everything that changes the built mod, as one string (selection order doesn't matter).</summary>
    public string Fingerprint()
    {
        var copy = Json.Clone(this);
        var sel = Selected.OrderBy(s => s, StringComparer.Ordinal).ToList();
        copy.Selected = new();
        return System.Text.Json.JsonSerializer.Serialize(new { copy, sel }, Json.Options);
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T? Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
    public static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

/// <summary>Builds the categories from the game's own data, so new items appear after game updates.</summary>
public static class Catalog
{
    public const string Bobbleheads = "Bobbleheads", Magazines = "Magazines", XpPlants = "XP food ingredients (plants)", XpRecipes = "XP recipes", Plans = "Plans", LootSpots = "Loot spots & event objects";

    /// <summary>Food effects that boost XP or Intelligence (Intelligence also raises XP gain).</summary>
    /// <summary>Old "xpplant:..." ids -> the plant models they lit (to move old setups to Plants (all)).</summary>
    public static readonly Dictionary<string, HashSet<string>> OldXpPlantModels = new();

    static readonly HashSet<string> XpEffects = new(StringComparer.OrdinalIgnoreCase) { "FortifyXPBonusFood", "FortifyIntelligenceFood" };

    /// <summary>Editor IDs of cut / test / backup records - their names are hidden when the model has better ones.</summary>
    static readonly Regex Junk = new(@"^(zz|DEL_|CUT_|BACKUP|Test|DEBUG|DUPLICATE|xx)", RegexOptions.IgnoreCase);

    /// <summary>Item colours, from the hues no enemy colour uses (enemies: red/orange, yellow, teal,
    /// blue, purple, pink): nothing glows white; 120-165 deg green and 225-265 deg blue-violet are free.</summary>
    public const string Collectible = "#FFFFFF", PlantGreen = "#2EFF8A", LootViolet = "#6E5CFF", Everyday = "#C8E6FF";

    /// <summary>The item defaults before the colour scheme (to move setups still on them).</summary>
    public static readonly Dictionary<string, (string Old, string Now)> OldItemColours = new()
    {
        [Bobbleheads] = ("#FFD84A", Collectible), [Magazines] = ("#FF4FD8", Collectible), [Plans] = ("#38D8FF", Collectible),
        [XpRecipes] = ("#40FF60", PlantGreen), [LootSpots] = ("#FFD040", LootViolet), ["Plants (all)"] = ("#80FF40", PlantGreen),
        ["Aid & food"] = ("#FF5050", Everyday), ["Junk & misc"] = ("#FFA040", Everyday),
    };

    static Style Look(string color, int length = 1000) => new() { GlowColor = color, BeamColor = color, BeamLength = length };

    // a few records start with "/Meshes/" (the default paper loot bag)
    public static string NormalizeModel(string model) => Regex.Replace(Ba2Path.Normalize(model).Replace("\\\\", "\\"), @"^meshes\\", "");

    /// <param name="modelExists">Whether meshes\&lt;model&gt; is in the game files - records whose model is missing are left out.</param>
    public static List<Category> Build(GameIndex idx, Func<string, bool>? modelExists = null)
    {
        var exists = new Dictionary<string, bool>();
        bool Exists(string m) => modelExists == null || (exists.TryGetValue(m, out var x) ? x : exists[m] = modelExists(m));
        var world = idx.Records.Where(r => GameIndex.WorldTypes.Contains(r.Type) && Exists(NormalizeModel(r.Model))).ToList();
        var byModel = world.GroupBy(r => NormalizeModel(r.Model)).ToDictionary(g => g.Key, g => g.ToList());
        // weapons are put together from their default mods' models (receiver, barrel, grip, blade...); the record's own
        // model is often an empty dummy receiver. The part models count as the weapon's models (spots, names).
        var weaponModels = new Dictionary<EsmRecord, List<string>>();
        foreach (var r in world.Where(r => r.Type == "WEAP"))
        {
            var parts = (r.Parts ?? new()).Select(p => idx.ById.GetValueOrDefault(p)).Where(o => o?.Type == "OMOD" && o.Model.Length > 0)
                .Select(o => NormalizeModel(o!.Model)).Where(Exists).ToList();
            var own = NormalizeModel(r.Model);
            var models = (parts.Count > 0 && own.Contains("dummy", StringComparison.OrdinalIgnoreCase) ? parts : parts.Prepend(own))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            weaponModels[r] = models;
            foreach (var m in models.Where(m => m != own))
                if (byModel.TryGetValue(m, out var users)) { if (!users.Contains(r)) users.Add(r); }
                else byModel[m] = new() { r };
        }
        var cats = new List<Category>();

        // --- Bobbleheads: every type has its own model.
        var bobble = new Category
        {
            Name = Bobbleheads,
            Description = "Each bobblehead type has its own model, so you can pick them one by one. " +
                          "Bobbleheads shown on a Bobblehead Stand in a CAMP light up too.",
            DefaultStyle = Look(Collectible),
        };
        foreach (var g in world.Where(r => r.Type is "MISC" or "ALCH" && Regex.IsMatch(r.Name, @"^(Glowing )?Bobblehead: ", RegexOptions.IgnoreCase))
                               .GroupBy(r => NormalizeModel(r.Model)))
        {
            var name = g.Select(r => Regex.Replace(r.Name, "^Glowing ", "", RegexOptions.IgnoreCase)).GroupBy(n => n).OrderByDescending(n => n.Count()).First().Key;
            bobble.Entries.Add(MakeEntry(bobble, "bobble:" + g.Key, name, new[] { g.Key }, byModel, ""));
        }
        bobble.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        cats.Add(bobble);

        // --- Magazines: hundreds of titles share four models (only the cover differs), so it's all or nothing.
        var mags = new Category
        {
            Name = Magazines,
            Description = "Magazines have no model of their own: each sits on one of a few covers, and every title on a cover " +
                          "lights up together (hover an entry to see its titles). Pick the covers you want. " +
                          "Magazines lying around as decoration light up too.",
            DefaultStyle = Look(Collectible),
        };
        var magRecs = world.Where(r => r.Type is "BOOK" or "ALCH" && Regex.IsMatch(r.EditorId, @"^(Magazine_|PerkMag)", RegexOptions.IgnoreCase)).ToList();
        foreach (var g in magRecs.GroupBy(r => NormalizeModel(r.Model)).OrderByDescending(g => g.Select(r => r.Name).Distinct().Count()))
        {
            var titles = g.Select(r => r.Name.Trim()).Distinct().OrderBy(n => n).ToList();
            var series = titles.GroupBy(n => Regex.Replace(n, @"\s*\d+$", "")).OrderByDescending(x => x.Count()).ToList();
            var file = Path.GetFileNameWithoutExtension(g.Key);
            var month = Regex.Match(file, "^comic_grognak(jan|feb|mar|apr)$", RegexOptions.IgnoreCase);
            var months = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["jan"] = "January", ["feb"] = "February", ["mar"] = "March", ["apr"] = "April" };
            var robco = Regex.Match(file, "^robcofun(.+)$", RegexOptions.IgnoreCase);
            string name = month.Success ? $"{months[month.Groups[1].Value]} comic cover ({titles.Count} magazines)"
                : robco.Success ? $"RobCo Fun ({System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(robco.Groups[1].Value)} cover)"
                : titles.Count == 1 ? titles[0] : series.Count == 1 ? $"{series[0].Key} ({titles.Count} issues)" : $"{file} ({titles.Count} magazines)";
            var e = MakeEntry(mags, "magazines:" + g.Key, name, new[] { g.Key }, byModel, "");
            e.ItemNames = titles.Concat(e.ItemNames).Distinct().ToList(); // the titles first in the hover list
            e.Detail = series.Count == 1 ? $"{titles.Count} title(s)" : string.Join(", ", series.Take(4).Select(x => $"{x.Key} ×{x.Count()}")) + (series.Count > 4 ? ", ..." : "");
            mags.Entries.Add(e);
        }
        var magModels = mags.Entries.SelectMany(e => e.Models).ToHashSet();
        var bobbleModels = bobble.Entries.SelectMany(e => e.Models).ToHashSet();
        cats.Add(mags);

        // --- XP recipes: food that boosts XP or Intelligence, from the game's own recipes; each recipe lights up its
        // ingredients - the plants that give them and the ingredients lying in the world.
        var xp = new Category
        {
            Name = XpRecipes,
            Description = "Food that boosts XP or Intelligence (from the game's own recipes). Selecting a recipe selects its " +
                          "ingredients - the plants to harvest and the ingredients lying in the world, as listed in Plants, " +
                          "Aid & food... (they show in Your picks one by one, each with its own category's look). " +
                          "Click ► in front of a recipe to see them and deselect the ones you already have. The look set here " +
                          "goes to what a recipe selects (shown as their own look).",
            DefaultStyle = Look(PlantGreen, 600),
            Groups = true,
        };
        // each recipe's ingredients and their models (plant bush + the thing lying around): matched to entries at the end
        var recipeModels = new List<(Entry Recipe, Dictionary<string, List<string>> Ingredients)>();
        var xpFoods = idx.Records.Where(r => r.Type == "ALCH" && r.Effects != null &&
                                             r.Effects.Any(f => idx.ById.TryGetValue(f, out var m) && XpEffects.Contains(m.EditorId))).ToList();
        var crafted = idx.Records.Where(r => r.Type == "COBJ" && r.Product != 0).Select(r => r.Product).ToHashSet();
        var flora = world.Where(r => r.Type == "FLOR" && r.Harvest != 0).ToList();
        var worldSpots = idx.WorldSpots();
        foreach (var food in xpFoods.GroupBy(f => f.Name.Trim()).Select(g => g.First()))
        {
            var ingredients = idx.Records.Where(r => r.Type == "COBJ" && r.Product == food.FormId && r.Parts != null)
                                 .SelectMany(r => r.Parts!).Distinct().Select(p => idx.ById.GetValueOrDefault(p)).OfType<EsmRecord>().ToList();
            if (ingredients.Count == 0) continue;
            var partNames = new Dictionary<string, string>();
            var make = new List<string>();
            var elsewhere = new List<string>();
            foreach (var ing in ingredients.OrderBy(i => i.Name))
            {
                bool found = false;
                // the plants that give it (the plant's name must match - some harvest lists are shared by odd plants)
                foreach (var p in flora.Where(f => idx.Gives(f.Harvest, ing.FormId) && SameThing(f.Name, ing.Name)))
                {
                    partNames.TryAdd(NormalizeModel(p.Model), $"{ing.Name} (plant)");
                    // what the old "XP food ingredients (plants)" entry of this ingredient stood for (setup migration)
                    (OldXpPlantModels.TryGetValue("xpplant:" + ing.FormId.ToString("x8"), out var old) ? old
                        : OldXpPlantModels["xpplant:" + ing.FormId.ToString("x8")] = new()).Add(NormalizeModel(p.Model));
                    found = true;
                }
                // lying in the world - not water (its carton model is in every house)
                bool water = ing.Name.Contains("Water", StringComparison.OrdinalIgnoreCase);
                if (!water && worldSpots.GetValueOrDefault(ing.FormId) > 0 && ing.Model.Length > 0)
                {
                    partNames.TryAdd(NormalizeModel(ing.Model), ing.Name);
                    found = true;
                }
                if (found) continue;
                (crafted.Contains(ing.FormId) || water ? make : elsewhere).Add(ing.Name);
            }
            var models = partNames.Keys.Where(modelExists).ToList();
            if (models.Count == 0) continue;
            // several plant models of one ingredient: "Firecracker Berry (plant 1)", "(plant 2)"
            foreach (var same in models.GroupBy(m => partNames[m]).Where(g => g.Count() > 1).ToList())
            {
                int n = 0;
                foreach (var m in same) partNames[m] = partNames[m].EndsWith(" (plant)") ? partNames[m][..^1] + $" {++n})" : partNames[m] + $" {++n}";
            }
            var e = new Entry
            {
                Id = "recipe:" + food.FormId.ToString("x8"), Category = xp.Name, Name = food.Name.Trim(), Spots = -1, NoLink = true,
                ItemNames = ingredients.Select(i => i.Name).Distinct().ToList(),
                Detail = string.Join(" · ", new[]
                {
                    make.Count > 0 ? "also needs: " + string.Join(", ", make) : "",
                    elsewhere.Count > 0 ? "from creatures / fishing: " + string.Join(", ", elsewhere) : "",
                }.Where(x => x.Length > 0)),
            };
            var byIngredient = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in models)
            {
                var ing = Regex.Replace(partNames[m], @" \(plant( \d+)?\)$", "");
                (byIngredient.TryGetValue(ing, out var l) ? l : byIngredient[ing] = new()).Add(m);
            }
            recipeModels.Add((e, byIngredient));
            xp.Entries.Add(e);
        }
        xp.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        cats.Add(xp);

        // --- Plans: almost every plan uses one of two models, so per-plan choice is impossible. Only models
        // used mostly by plans - the few plans on e.g. the recipe book would light up all recipes too.
        var plans = new Category
        {
            Name = Plans,
            Description = "Almost all plans use one of two models - a rolled blueprint or Brotherhood tech documents - " +
                          "so the game can't light up a single plan. Select one or both kinds.",
            DefaultStyle = Look(Collectible),
        };
        var planLabels = new Dictionary<string, string>
        {
            [@"props\instr03blueprint.nif"] = "Plans (blueprint)",
            [@"props\bostechdocs.nif"] = "Plans (Brotherhood tech documents)",
        };
        foreach (var g in world.Where(r => r.Type == "BOOK" && r.Name.StartsWith("Plan: ", StringComparison.OrdinalIgnoreCase))
                               .GroupBy(r => NormalizeModel(r.Model)))
        {
            int onModel = byModel[g.Key].Count(r => !Junk.IsMatch(r.EditorId));
            if (g.Count() * 2 < onModel) continue; // the model mostly shows other things
            var label = planLabels.GetValueOrDefault(g.Key) ?? g.OrderBy(r => r.Name.Length).First().Name;
            var e = MakeEntry(plans, "plans:" + g.Key, label, new[] { g.Key }, byModel, "");
            int n = g.Select(r => r.Name).Distinct().Count();
            e.Detail = n == 1 ? "1 plan" : $"{n} plans";
            plans.Entries.Add(e);
        }
        plans.Entries.Sort((a, b) => b.ItemNames.Count.CompareTo(a.ItemNames.Count));
        cats.Add(plans);

        // --- Loot spots & event objects: containers / activators worth finding, by their in-game name.
        var spots = new Category
        {
            Name = LootSpots,
            Description = "Containers and spots worth finding - caps stashes, safes, treasure map mounds... (event objects are under EVENTS) " +
                          "They keep their beam after you've looted them (the game uses one model for full and empty).",
            DefaultStyle = Look(LootViolet),
        };
        foreach (var (label, names, extra) in new (string, string, string[])[]
                 {
                     ("Caps stashes", "^Caps Stash$", Array.Empty<string>()),
                     ("Safes", "^(Safe|Wall Safe|Loot Safe|Loot Wall Safe)$", Array.Empty<string>()),
                     ("Treasure map mounds", "^Mound$", Array.Empty<string>()),
                     ("Duffle bags", "^Duffle Bag$", Array.Empty<string>()),
                     ("Overseer's caches", "^Overseer's Cache$", Array.Empty<string>()),
                     ("First aid boxes", "^(First Aid|Chem Box|Animal Control Kit)$", Array.Empty<string>()),
                     ("Ammo boxes", "^Ammo Box$", new[] { @"setdressing\ammo\ammoboxnotskinned.nif" }),
                     ("Chem containers", "(?!)", new[] { @"setdressing\arktoschemcontainer01.nif" }),
                     ("Deathclaw nests", "^Deathclaw Nest$", Array.Empty<string>()),
                 })
        {
            var rx = new Regex(names, RegexOptions.IgnoreCase);
            var models = world.Where(r => r.Type is "CONT" or "ACTI" && rx.IsMatch(r.Name.Trim()))
                              .Select(r => NormalizeModel(r.Model)).Concat(extra.Where(Exists)).Distinct().OrderBy(m => m).ToList();
            if (models.Count == 0) continue;
            var e = MakeEntry(spots, "spot:" + label.ToLowerInvariant(), label, models, byModel, "");
            e.Detail = models.Count == 1 ? "1 model" : $"{models.Count} models";
            spots.Entries.Add(e);
        }
        cats.Add(spots);

        // --- Pre-war food: the game has no keyword for it - a list of names (Fallout wiki), one entry per food.
        var prewar = new Category
        {
            Name = PreWarFood,
            Description = "Packaged food from before the war (Fancy Lads, Sugar Bombs, Cram, InstaMash...). The same entries as in " +
                          "Aid & food - selecting one here selects it there too. Some share a model with cooked food (hover to see).",
            DefaultStyle = Look(Everyday),
        };
        var prewarNames = PreWarFoods.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // foods on the same model(s) are one entry: the six bubblegum flavours look the same
        foreach (var g in world.Where(r => r.Type == "ALCH" && prewarNames.Contains(r.Name.Trim()) && !Junk.IsMatch(r.EditorId))
                               .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                               .Select(n => (Name: n.Key, Models: n.Select(r => NormalizeModel(r.Model)).Distinct().OrderBy(m => m).ToList()))
                               .GroupBy(n => string.Join("|", n.Models)))
        {
            var foods = g.Select(n => n.Name).OrderBy(n => n.Length).ThenBy(n => n).ToList();
            var name = foods.Count == 1 ? foods[0] : foods.Count == 2 ? $"{foods[0]} / {foods[1]}" : $"{foods[0]} ({foods.Count} kinds)";
            var e = MakeEntry(prewar, "prewar:" + g.First().Models[0], name, g.First().Models, byModel, "");
            if (foods.Count > 2) e.Detail = string.Join(", ", foods);
            prewar.Entries.Add(e);
        }
        prewar.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        cats.Add(prewar);

        // --- Notes & holotapes: thousands of notes share a few paper models (like magazines), so one entry per model.
        // Only models used mostly by notes - a note on a book or a toy would light up every copy of it.
        var notes = new Category
        {
            Name = Notes,
            Description = "Notes, letters and holotapes lying around. They share a few models (paper, torn paper, holotape...), so " +
                          "every note on a model lights up together - hover an entry to see them. Notes you've already read light up too.",
            DefaultStyle = Look(Collectible),
        };
        bool IsNote(EsmRecord r) => r.Type is "BOOK" or "NOTE" && !Junk.IsMatch(r.EditorId)
                                    && !Regex.IsMatch(r.Name, @"^(Plan|Recipe|Schematic|Diagram): ", RegexOptions.IgnoreCase)
                                    && !Regex.IsMatch(r.EditorId, @"^(Magazine_|PerkMag)", RegexOptions.IgnoreCase);
        var noteLabels = new Dictionary<string, string>
        {
            [@"props\note_classified.nif"] = "Notes (paper)",
            [@"props\note_classfied.nif"] = "Notes (paper, folded)",
            [@"props\note_lowpoly.nif"] = "Notes (paper, small)",
            [@"props\noteripped_lowpoly.nif"] = "Notes (torn paper)",
            [@"props\holotape_prop.nif"] = "Holotapes",
            [@"interface\note\dotmatrixpage01.nif"] = "Printouts",
            [@"interface\note\postcard_lowpoly01.nif"] = "Postcards",
            [@"interface\note\lowpoly_notepad01.nif"] = "Notepads",
            [@"props\note_topsecret.nif"] = "Top secret notes",
        };
        foreach (var g in world.Where(IsNote).GroupBy(r => NormalizeModel(r.Model)))
        {
            if (magModels.Contains(g.Key) || bobbleModels.Contains(g.Key)) continue;
            int onModel = byModel[g.Key].Count(r => !Junk.IsMatch(r.EditorId));
            if (g.Count() * 2 < onModel) continue; // the model mostly shows other things
            var titles = g.Select(r => r.Name.Trim()).Distinct().OrderBy(n => n).ToList();
            var e = MakeEntry(notes, "notes:" + g.Key, noteLabels.GetValueOrDefault(g.Key) ?? (titles.Count == 1 ? titles[0] : Label(g.ToList())), new[] { g.Key }, byModel, "");
            e.ItemNames = titles.Concat(e.ItemNames).Distinct().ToList();
            e.Detail = titles.Count == 1 ? "1 note" : $"{titles.Count} notes";
            notes.Entries.Add(e);
        }
        notes.Entries.Sort((a, b) => b.ItemNames.Count.CompareTo(a.ItemNames.Count));
        cats.Add(notes);

        // --- Everything else, by kind of record, one entry per model.
        var generic = new (string Name, string[] Types, string Description, string Color)[]
        {
            ("Plants (all)", new[] { "FLOR" }, "Every harvestable plant. The beam disappears once the plant is harvested.", PlantGreen),
            ("Aid & food", new[] { "ALCH", "INGR" }, "Food, drinks, chems and ingredients lying in the world. Many of these show in your Pip-Boy item preview with the same model.", Everyday),
            ("Junk & misc", new[] { "MISC" }, "Misc items lying in the world that don't scrap into anything (scrap junk is under JUNK, by its main component).", Everyday),
            ("Weapons", new[] { "WEAP" }, "Weapons lying in the world (baseball bats, pipe guns...). The game uses the same model for a weapon " +
                                         "in someone's hands: your own weapon and weapons enemies or players carry glow too.", Everyday),
        };
        foreach (var (name, types, description, color) in generic)
        {
            var c = new Category { Name = name, Description = description, DefaultStyle = Look(color) };
            // junk: one category per main scrap component (the first one the game lists), under JUNK; entry ids stay
            // "name:Junk & misc:..." so older setups keep their ticks
            var junk = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            string? Component(string model)
            {
                if (!types.Contains("MISC")) return null;
                return (byModel.GetValueOrDefault(model) ?? new()).Where(r => r.Type == "MISC" && r.Parts is { Count: > 0 })
                    .Select(r => idx.ById.GetValueOrDefault(r.Parts![0])).Where(x => x?.Type == "CMPO" && x.Name.Trim().Length > 0)
                    .GroupBy(x => x!.Name.Trim()).OrderByDescending(x => x.Count()).FirstOrDefault()?.Key;
            }
            List<string> Scraps(IEnumerable<string> models) => models.SelectMany(m => byModel.GetValueOrDefault(m) ?? new())
                .Where(r => r.Type == "MISC" && r.Parts != null).SelectMany(r => r.Parts!).Select(p => idx.ById.GetValueOrDefault(p))
                .Where(x => x?.Type == "CMPO").Select(x => x!.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Category JunkCat(string comp) => junk.TryGetValue(comp, out var jc) ? jc : junk[comp] = new Category
            {
                Name = "Junk: " + comp, Section = "JUNK", Group = JunkKind(comp), DefaultStyle = Look(color),
                Description = $"Junk that scraps into {comp.ToLowerInvariant()} (hover an entry to see everything it scraps into). " +
                              "Junk with several components is listed under each of them. Give each component its own colour to tell look-alike junk apart.",
            };
            // one label per model, then models with the same label become one entry
            var labelled = new List<(string Label, string Model)>();
            // weapons: grouped by their first real model (the receiver when the record's own is a dummy)
            foreach (var g in world.Where(r => types.Contains(r.Type) && Real(r))
                         .GroupBy(r => weaponModels.TryGetValue(r, out var wm) && wm.Count > 0 ? wm[0] : NormalizeModel(r.Model)))
            {
                if (magModels.Contains(g.Key)) continue; // magazine covers: in Magazines (named for what they hold)
                if (bobbleModels.Contains(g.Key)) continue; // bobbleheads: in Bobbleheads (not three times over)
                // the loose mod box: ~2,600 mod items share it ("X-01 Headlamp"), so it scrapped into every component
                if (g.Key == @"props\modspartbox\modcrate.nif") continue;
                // creature attacks, and props used as thrown "weapons" (gas canisters, Tato, ore) - in their own categories
                if (types.Contains("WEAP") && (g.Key.StartsWith(@"actors\") || !g.Key.Contains(@"weapons\"))) continue;
                // the recipe book is one misc item among ~190 recipes (books) - name it for what lights up
                var users = byModel.GetValueOrDefault(g.Key) ?? new();
                int recipes = users.Count(r => r.Type == "BOOK" && r.Name.StartsWith("Recipe: ", StringComparison.OrdinalIgnoreCase));
                labelled.Add((recipes >= 8 && recipes * 2 > users.Count ? "Recipes (recipe book)" : BobbleName(users) ?? Label(g.ToList(), worldSpots, byPlacement: types.Contains("FLOR")), g.Key));
            }
            foreach (var g in labelled.GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
            {
                var models = g.Select(x => x.Model).Distinct().OrderBy(m => m).ToList();
                // weapons: the whole gun of one record - the base weapon (named like the entry, plainest editor id);
                // variants with other default parts would overlap in the preview
                if (types.Contains("WEAP"))
                    models = models.SelectMany(k => (byModel.GetValueOrDefault(k) ?? new())
                            .Where(r => weaponModels.TryGetValue(r, out var wm) && wm.Count > 0 && wm[0] == k))
                        .OrderBy(r => r.Name.Trim().Equals(g.Key, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(r => r.EditorId.Length)
                        .Take(1).SelectMany(r => weaponModels[r]).ToList();
                if (models.Count == 0) continue;
                var main = Component(models[0]);
                var into = main != null ? JunkCat(main) : c;
                var e = MakeEntry(into, $"name:{name}:{g.Key.ToLowerInvariant()}", g.Key, models, byModel, "");
                if (models.Count > 1) e.Detail = $"{models.Count} models" + (e.Detail.Length > 0 ? ", " + e.Detail : "");
                var scraps = main != null ? Scraps(models) : new();
                if (scraps.Count > 0) e.Detail = "scraps into: " + string.Join(", ", scraps) + (e.Detail.Length > 0 ? " · " + e.Detail : "");
                e.Harvestable = types.Contains("FLOR");
                e.Assembled = types.Contains("WEAP") && models.Count > 1;
                into.Entries.Add(e);
                // the same junk under each of its other components (own tick and look: Duct Tape for adhesive or for cloth)
                foreach (var comp in scraps.Where(s => !s.Equals(main, StringComparison.OrdinalIgnoreCase)))
                {
                    var other = JunkCat(comp);
                    var copy = MakeEntry(other, $"junk:{comp.ToLowerInvariant()}:{g.Key.ToLowerInvariant()}", g.Key, models, byModel, "");
                    copy.Detail = e.Detail;
                    copy.JunkCopy = true;
                    other.Entries.Add(copy);
                }
            }
            c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            cats.Add(c);
            foreach (var jc in junk.Values.OrderBy(j => j.Name))
            {
                jc.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                cats.Add(jc);
            }
        }
        CountSpots(cats.Where(c => !c.Groups).SelectMany(c => c.Entries), byModel, idx.WorldSpots());
        cats.Add(VendingMachines(idx, Exists, byModel));
        cats.Add(WorkbenchCategory(idx, Exists));
        cats.Add(LootBags(world, byModel));
        cats.Add(StorageBoxes(world, byModel));
        cats.Add(Apparel(idx, Exists, worldSpots));
        cats.Add(CampItems(idx, Exists, cats));
        // recipes: their ingredients are the entries listing those models (Cranberry in Plants, Sugar in Aid & food...)
        var others = cats.Where(c => !c.Groups).SelectMany(c => c.Entries).ToList();
        foreach (var (recipe, ingredients) in recipeModels)
        {
            // one row per ingredient: the bush to harvest and the thing lying around together (not CAMP planters - the
            // same plant model, but nothing to find in the world)
            recipe.Group = new();
            foreach (var (ing, models) in ingredients)
            {
                var members = others.Where(o => o.Models.Any(models.Contains) && !o.Name.EndsWith(" planter", StringComparison.OrdinalIgnoreCase))
                                    .Distinct().ToList();
                if (members.Count == 0) continue;
                recipe.Group.Add(new Entry
                {
                    Id = recipe.Id + ":" + ing.ToLowerInvariant(), Category = string.Join(" + ", members.Select(m => m.Category).Distinct()),
                    Name = ing, Group = members, Spots = -1, NoLink = true, ItemNames = new() { ing },
                    Detail = string.Join(" · ", members.Select(m => (m.Category.StartsWith("Plants") ? "harvest: " : "lying around: ") + m.Name)),
                });
            }
            recipe.Detail = string.Join(" · ", new[] { string.Join(", ", recipe.Group.Select(g => g.Name)), recipe.Detail }.Where(x => x.Length > 0));
        }
        xp.Entries.RemoveAll(r => r.Group is not { Count: > 0 });
        // spots set up only as scenery (no item record to count) are in the world but can't be counted
        foreach (var e in spots.Entries.Where(e => e.Spots == 0)) e.Spots = -1;
        return cats;
    }

    /// <summary>Records with a real in-game name (not an editor name like "CarFrame03Static_LN").</summary>
    static bool Real(EsmRecord r) => !(r.Name.Contains('_') && !r.Name.Contains(' '));

    static bool SameThing(string a, string b)
    {
        static IEnumerable<string> Stems(string s) => Regex.Split(s.ToLowerInvariant(), "[^a-z]+").Where(w => w.Length >= 4).Select(w => w[..4]);
        return Stems(a).Intersect(Stems(b)).Any();
    }

    /// <summary>Name for a model: its most common proper name, or "All plans" etc. for a big shared group.</summary>
    /// <summary>A bobblehead's model is named after the bobblehead, whatever else reuses it (the Sheepsquatch Horn
    /// consumable has the Sneak bobblehead as its model).</summary>
    static string? BobbleName(List<EsmRecord> users) =>
        users.Where(r => r.Type == "MISC" && r.Name.StartsWith("Bobblehead: ", StringComparison.OrdinalIgnoreCase)).Select(r => r.Name.Trim()).FirstOrDefault();

    /// <param name="spots">World spots per record: a name that is never placed loses to one that is (the Brain Fungus item,
    /// not the Brain Bombs food that reuses its model).</param>
    /// <param name="byPlacement">Plants: up to three plants on one bush are named together, most placed first.</param>
    static string Label(List<EsmRecord> recs, Dictionary<uint, int>? spots = null, bool byPlacement = false)
    {
        var good = recs.Where(r => !Junk.IsMatch(r.EditorId)).ToList();
        if (good.Count == 0) good = recs;
        var names = good.Select(r => r.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count >= 8)
        {
            var prefix = names.Where(n => n.Contains(": ")).GroupBy(n => n[..n.IndexOf(": ")]).OrderByDescending(x => x.Count()).FirstOrDefault();
            if (prefix != null && prefix.Count() >= names.Count * 0.6) return $"All {prefix.Key.ToLowerInvariant()}s";
        }
        if (byPlacement && spots != null)
        {
            // plants sharing a bush are named together ("Firecracker Berry / Bomb Berry"): which one you see depends on
            // the region, and the game's random-plant lists aren't counted - so neither name can be called the main one
            var plantNames = good.GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                                 .OrderByDescending(n => n.Sum(r => spots.GetValueOrDefault(r.FormId))).ThenByDescending(n => n.Count())
                                 .Select(n => n.Key).ToList();
            if (plantNames.Count is > 1 and <= 3) return string.Join(" / ", plantNames);
            return plantNames[0];
        }
        // a name that never lies in the world loses to one that does; otherwise the most common name
        return good.GroupBy(r => r.Name.Trim()).OrderByDescending(n => spots == null || n.Any(r => spots.GetValueOrDefault(r.FormId) > 0))
                   .ThenByDescending(n => n.Count()).ThenBy(n => n.Key.Length).First().Key;
    }

    /// <summary>Spots per entry: every placement / spawn spot of anything that uses one of its models.</summary>
    public static void CountSpots(IEnumerable<Entry> entries, Dictionary<string, List<EsmRecord>> byModel, Dictionary<uint, int> spots)
    {
        foreach (var e in entries)
            e.Spots = e.Models.SelectMany(m => byModel.GetValueOrDefault(m) ?? new()).Distinct().Sum(r => spots.GetValueOrDefault(r.FormId));
    }

    public const string Vending = "Vending machines";

    /// <summary>Player vending machines (every CAMP design has a model of its own, so only shops light up) and the
    /// Legendary Exchange machines. CAMP objects are never placed in the world itself: their spots are unknown (-1),
    /// so they aren't dropped with the things the game never places.</summary>
    static Category VendingMachines(GameIndex idx, Func<string, bool> exists, Dictionary<string, List<EsmRecord>> byModel)
    {
        var c = new Category
        {
            Name = Vending,
            Description = "Player vending machines in CAMPs - every design (Vendor Bot, Slocum's Joe, cash registers...) has a model of its " +
                          "own, so only shops light up. \"Select all\" finds any player's shop. The Legendary Exchange machines at " +
                          "train stations are here too.",
            DefaultStyle = Look("#FFB347"),
        };
        // designs that come in several colours / versions: one entry, named for all of them
        var names = new Dictionary<string, string>
        {
            [@"atx\workshop\atx_playervendingstation_cashregister\atx_cashregister.nif"] = "Cash Register Vending Machine (all colours)",
            [@"atx\workshop\atx_vendorbot\atx_vendorbot.nif"] = "Vendor Bot Vending Machine (all versions)",
            [@"atx\workshop\atx_slocum_joe_vending_protectron\atx_workshop_slocum_joe_vending_protectron.nif"] = "Slocum's Joe Vending Machine (all colours)",
            [@"atx\workshop\atx_old_west_bartender_vending_protectron\atx_workshop_old_west_bartender_vending_protectron.nif"] = "Tipsy Tom Vending Machine (all colours)",
            [@"setdressing\workshop\vendingmachines\workshopplayervendingmachine01.nif"] = "Vending Machine (the basic one)",
        };
        var designs = idx.Records.Where(r => r.Type == "ACTI" && r.Model.Length > 0 && r.Name.Trim().Length > 0
                                             && Regex.IsMatch(r.EditorId, "PlayerVend", RegexOptions.IgnoreCase)
                                             && !Regex.IsMatch(r.EditorId, "^(Test|zzz|DEPRECATED)", RegexOptions.IgnoreCase))
                                 .GroupBy(r => NormalizeModel(r.Model)).Where(g => exists(g.Key));
        foreach (var g in designs)
        {
            var name = names.GetValueOrDefault(g.Key) ?? g.Select(r => r.Name.Trim()).GroupBy(n => n).OrderByDescending(n => n.Count()).ThenBy(n => n.Key.Length).First().Key;
            // a "static" model is the kind the world's decoration uses: those machines may light up too
            var e = MakeEntry(c, "vending:" + g.Key, name, new[] { g.Key }, byModel,
                              g.Key.Contains("static") ? "CAMP shop · Nuka-Cola machines in the world may light up too" : "CAMP shop");
            e.Spots = -1;
            c.Entries.Add(e);
        }
        const string exchange = @"props\legendaryscrappingstation\legendaryscrappingstation.nif";
        if (exists(exchange))
        {
            var e = MakeEntry(c, "vending:" + exchange, "Legendary Exchange machines", new[] { exchange }, byModel, "train stations");
            CountSpots(new[] { e }, byModel, idx.WorldSpots());
            c.Entries.Add(e);
        }
        c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return c;
    }

    public const string Workbenches = "Workbenches";

    /// <summary>Crafting stations (furniture records): one entry per kind with every design of it - the world's and the
    /// CAMP ones. Seats called "bench" and power armor frames aren't stations.</summary>
    static Category WorkbenchCategory(GameIndex idx, Func<string, bool> exists)
    {
        var c = new Category
        {
            Name = Workbenches,
            Description = "Crafting stations: every design of each kind, in the world and in CAMPs (players' CAMP stations light up too).",
            DefaultStyle = Look("#7FE0FF"),
        };
        var kinds = new (string Name, string Match)[]
        {
            ("Tinker's Workbench", @"tinker"),
            ("Armor Workbench", @"armor workbench|workbencharmor"),
            ("Weapons Workbench", @"weapons? workbench|workbenchweapons|sam's workbench"),
            ("Power Armor Station", @"power armor station|workbenchpowerarmor"),
            ("Chemistry Station", @"chemistry station|workbenchchemistry"),
            ("Cooking Station", @"cooking|workbenchcooking|grill|radhog spit|donut fryer|meat spit"),
            ("Brewing Station", @"brewing|workbenchbrewing"),
        };
        var stations = idx.Records.Where(r => r.Type == "FURN" && r.Model.Length > 0 && r.Name.Trim().Length > 0
                                              && !Regex.IsMatch(r.EditorId, "^(zzz|Test|DELETE)|DoNotUse|Fabricator", RegexOptions.IgnoreCase)
                                              && !NormalizeModel(r.Model).StartsWith(@"markers\")).ToList();
        foreach (var (name, match) in kinds)
        {
            var rx = new Regex(match, RegexOptions.IgnoreCase);
            var mine = stations.Where(r => rx.IsMatch(r.Name) || rx.IsMatch(r.EditorId)).ToList();
            var models = mine.Select(r => NormalizeModel(r.Model)).Distinct().Where(exists).OrderBy(m => m).ToList();
            if (models.Count == 0) continue;
            int placed = mine.Sum(r => idx.Placed.GetValueOrDefault(r.FormId));
            c.Entries.Add(new Entry
            {
                Id = "workbench:" + name.ToLowerInvariant(), Category = Workbenches, Name = name, Models = models,
                ItemNames = mine.Select(r => r.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList(),
                Detail = $"{models.Count} design{(models.Count == 1 ? "" : "s")} · CAMP ones too",
                Spots = placed > 0 ? placed : -1, // CAMP-only kinds aren't placed in the world: unknown, not "never"
            });
        }
        return c;
    }

    public const string PreWarFood = "Pre-war food", Notes = "Notes & holotapes", LootBagsName = "Loot bags",
                        StorageBoxesName = "CAMP storage boxes", ApparelName = "Clothes lying around";

    /// <summary>Pre-war food (Fallout wiki list, 2026-11).</summary>
    static readonly string[] PreWarFoods =
    {
        "Blamco Brand Mac and Cheese", "Bubblegum", "Cajun Rice & Beans", "Canned Coffee", "Canned Dog Food", "Canned Meat Stew",
        "Cola Perk Bubblegum", "Cotton Candy Bites", "Cram", "Dandy Boy Apples", "Fancy Lads Snack Cakes", "Fasnacht Donut",
        "Funnel Cake", "Grape Perk Bubblegum", "Gum Drops", "Halloween Candy", "Imitation Seafood", "InstaMash", "Mystery Candy",
        "Noodle Cup", "Nuka Victory Candy", "Nuka-Cola Candy", "Nuka-Cola Quantum Candy", "Perfect Bubblegum", "Perfectly Preserved Pie",
        "Perk Bubblegum", "Popcorn", "Pork n' Beans", "Potato Crisps", "Preserved InstaMash", "Rudy's Canned Pozole", "Salisbury Steak",
        "Strawberry Perk Bubblegum", "Sugar Bombs", "Vegetarian Ham", "Watermelon Perk Bubblegum", "Yum Yum Deviled Eggs",
    };

    /// <summary>What's left when someone dies or drops things: the paper bag, creature meat piles, robot scrap piles and
    /// the loot bag skins players pick (Atom Shop / Scoreboard). Never placed in the world: spots unknown.</summary>
    static Category LootBags(List<EsmRecord> world, Dictionary<string, List<EsmRecord>> byModel)
    {
        var c = new Category
        {
            Name = LootBagsName,
            Description = "Loot bags: dropped items (paper bag), creature meat piles, robot scrap piles and every loot bag skin " +
                          "players can pick. Some skins reuse a world model (paper bags, gravestones...) - those light up too.",
            DefaultStyle = Look(LootViolet),
        };
        var names = new Dictionary<string, string>
        {
            [@"setdressing\brownbag01\brownbag01.nif"] = "Paper bag (dropped items)",
            [@"setdressing\corpse_meatbag01.nif"] = "Meat pile (creature loot)",
            [@"setdressing\robotdebris\robotpile.nif"] = "Metal pile (robot loot)",
        };
        // not skins: the cut backpack, a test record on the fire hydrant, a quest duffle bag (under Loot spots)
        foreach (var g in world.Where(r => r.Type == "CONT" && Regex.IsMatch(r.EditorId, "lootbag", RegexOptions.IgnoreCase)
                                           && !Regex.IsMatch(r.EditorId, "^(CUT_|BURN_)|_Test$", RegexOptions.IgnoreCase))
                               .GroupBy(r => NormalizeModel(r.Model)).Where(g => byModel.ContainsKey(g.Key)))
        {
            var name = names.GetValueOrDefault(g.Key) ?? g.OrderBy(r => Junk.IsMatch(r.EditorId) ? 1 : 0).ThenBy(r => r.Name.Length).First().Name.Trim();
            var e = MakeEntry(c, "lootbag:" + g.Key, name, new[] { g.Key }, byModel, names.ContainsKey(g.Key) ? "" : "loot bag skin");
            e.Spots = -1;
            c.Entries.Add(e);
        }
        // the everyday bags first, then the skins
        c.Entries = c.Entries.OrderBy(e => names.ContainsValue(e.Name) ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return c;
    }

    /// <summary>CAMP storage: the stash box and the Fallout 1st aid, ammo and scrap boxes - one entry per kind with every
    /// design of it. A shared model: every player's box lights up, not only yours.</summary>
    static Category StorageBoxes(List<EsmRecord> world, Dictionary<string, List<EsmRecord>> byModel)
    {
        var c = new Category
        {
            Name = StorageBoxesName,
            Description = "Stash boxes and the Fallout 1st aid, ammo and scrap boxes in CAMPs, every design. Every player's box lights up, not only yours.",
            DefaultStyle = Look("#B08CFF"),
        };
        foreach (var (name, match) in new[] { ("Aid boxes", "^My Aid Box$"), ("Ammo boxes", "^My Ammo Storage Box$"), ("Scrapboxes", "^My Scrapbox$"), ("Stash boxes", "^My Stash Box$") })
        {
            // not designs that reuse a world container (the Hunter's Blind tent's aid box is a first aid kit): they would
            // light up every one of those in the world, and tie this entry to that loot spot
            var models = world.Where(r => r.Type == "CONT" && Regex.IsMatch(r.Name.Trim(), match, RegexOptions.IgnoreCase))
                              .Select(r => NormalizeModel(r.Model)).Where(byModel.ContainsKey).Distinct()
                              .Where(m => byModel[m].All(u => u.Type != "CONT" || u.Name.Trim().StartsWith("My ", StringComparison.OrdinalIgnoreCase)))
                              .OrderBy(m => m).ToList();
            if (models.Count == 0) continue;
            var e = MakeEntry(c, "storage:" + name.ToLowerInvariant(), name, models, byModel, $"{models.Count} design{(models.Count == 1 ? "" : "s")}");
            e.Spots = -1;
            c.Entries.Add(e);
        }
        return c;
    }

    /// <summary>Apparel lying on the ground, by its ground model - only models nobody wears (a worn model would light
    /// up every player and NPC in that outfit).</summary>
    static Category Apparel(GameIndex idx, Func<string, bool> exists, Dictionary<uint, int> worldSpots)
    {
        var c = new Category
        {
            Name = ApparelName,
            Description = "Clothes and hats lying on the ground (pre-war dresses, suits, hats...). Only their ground models - " +
                          "clothes whose ground model is also the worn one are left out, so players wearing them don't glow.",
            DefaultStyle = Look(Everyday),
        };
        var worn = idx.Records.Where(r => r.Type == "ARMA" && r.Models != null).SelectMany(r => r.Models!).Select(NormalizeModel).ToHashSet();
        var armo = idx.Records.Where(r => r.Type == "ARMO" && r.Models != null && r.Name.Trim() is { Length: > 0 } n && n != "Default" && !Junk.IsMatch(r.EditorId)).ToList();
        var users = armo.SelectMany(r => r.Models!.Select(m => (Model: NormalizeModel(m), Rec: r))).GroupBy(x => x.Model)
                        .ToDictionary(g => g.Key, g => g.Select(x => x.Rec).Distinct().ToList());
        foreach (var (model, recs) in users)
        {
            // clothes and hats only: armour pieces are named after their mods ("Default", "BoS Epic")
            if (worn.Contains(model) || !model.Contains(@"clothes\") || !exists(model)) continue;
            if (model == @"clothes\hats\gomininghelmet.nif") continue; // no shapes that can glow
            int spots = recs.Sum(r => worldSpots.GetValueOrDefault(r.FormId));
            if (spots == 0) continue; // never lies in the world
            var names = recs.Select(r => r.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            c.Entries.Add(new Entry
            {
                Id = "apparel:" + model, Category = ApparelName, Name = Label(recs, worldSpots), Models = new() { model },
                ItemNames = names, Spots = spots, Detail = names.Count > 1 ? $"+{names.Count - 1} more share this model" : "",
            });
        }
        c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return c;
    }

    /// <summary>Section of the advanced "Everything else" categories (always shown; was an option until 2.0).</summary>
    public const string AllSection = "ALL";

    /// <summary>The sub-headings inside a section, in the order the tree shows them.</summary>
    public static readonly string[] GroupOrder =
        { "Seasonal", "Public events", "Raids & expeditions", "Metals", "Chemicals", "Parts & tech", "Materials", "Other" };

    /// <summary>A scrap component's kind (the junk section's sub-heading).</summary>
    static string JunkKind(string comp) => comp.ToLowerInvariant() switch
    {
        "aluminum" or "black titanium" or "copper" or "gold" or "lead" or "silver" or "steel" or "ultracite" => "Metals",
        "acid" or "adhesive" or "antiseptic" or "asbestos" or "coal" or "fertilizer" or "nuclear material" or "oil" or "lubricant" => "Chemicals",
        "circuitry" or "crystal" or "fiber optics" or "gear" or "screw" or "spring" or "ballistic fiber" => "Parts & tech",
        "bone" or "ceramic" or "cloth" or "concrete" or "cork" or "fiberglass" or "glass" or "leather" or "plastic" or "rubber" or "wood" => "Materials",
        _ => "Other",
    };
    /// <summary>Names of its categories ("Other: Doors"); older setups said "All: ".</summary>
    public const string OtherPrefix = "Other: ", OldOtherPrefix = "All: ";
    /// <summary>Loose things only count where they are placed; activators, containers and furniture stay (events spawn them).</summary>
    static bool PlacedOnly(string[] types) => !types.Any(t => t is "ACTI" or "CONT" or "FURN");
    static readonly Regex NothingDrawn = new(@"^markers\\|loadmarker|dummy|invisible|activatorvolume|^armor\\raider01\\static\.nif$|_startlights\.nif$|^clothes\\hats\\gomininghelmet\.nif$|^effects\\splinesparks", RegexOptions.IgnoreCase);

    /// <summary>Every named game object whose model no other category uses, one entry per model, sorted only by record
    /// type - nothing hand-picked. Items that never lie in the world are left out (they can't show their model); activators,
    /// containers and furniture stay, events and quests may spawn them.</summary>
    public static List<Category> Rest(GameIndex idx, Func<string, bool> exists, List<Category> cats)
    {
        var covered = cats.SelectMany(c => c.Entries).SelectMany(e => e.Models).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var spots = idx.WorldSpots();
        var result = new List<Category>();
        foreach (var (name, types) in new[]
        {
            ("Activators", new[] { "ACTI" }), ("Containers", new[] { "CONT" }), ("Furniture", new[] { "FURN" }),
            ("Misc items", new[] { "MISC" }), ("Food & aid", new[] { "ALCH", "INGR" }), ("Notes & books", new[] { "BOOK", "NOTE" }),
            ("Plants", new[] { "FLOR" }), ("Weapons", new[] { "WEAP" }), ("Ammo", new[] { "AMMO" }), ("Keys", new[] { "KEYM" }),
            ("Doors", new[] { "DOOR" }), ("Apparel on the ground", new[] { "ARMO" }), ("Moveable objects", new[] { "MSTT" }),
        })
        {
            var c = new Category
            {
                Name = OtherPrefix + name, Section = AllSection, DefaultStyle = Look("#7FE3FF"),
                Description = $"{name} the other categories don't have, named after the game's records - not sorted or checked by hand. " +
                              "Search also finds editor ids here." + (PlacedOnly(types) ? " Only ones placed in the world (or by a spawn list) are listed." : ""),
            };
            bool placedOnly = PlacedOnly(types);
            // apparel: its ground models (male / female); the rest: the record's model
            foreach (var g in idx.Records.Where(r => types.Contains(r.Type) && r.Name.Trim().Length > 0)
                                         .SelectMany(r => (r.Type == "ARMO" ? r.Models ?? new() : new List<string> { r.Model }).Where(m => m.Length > 0).Select(m => (r, m)))
                                         .GroupBy(x => NormalizeModel(x.m), x => x.r, StringComparer.OrdinalIgnoreCase))
            {
                // creature attacks; editor markers and dummies have nothing to draw
                if (covered.Contains(g.Key) || g.Key.StartsWith(@"actors\") || NothingDrawn.IsMatch(g.Key) || !exists(g.Key)) continue;
                covered.Add(g.Key); // used by several record types: listed under the first one only
                var recs = g.Distinct().ToList();
                int n = recs.Sum(r => spots.GetValueOrDefault(r.FormId));
                if (n == 0 && placedOnly) continue;
                var names = recs.Select(r => r.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                var ids = recs.Select(r => r.EditorId).Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToList();
                c.Entries.Add(new Entry
                {
                    Id = "all:" + g.Key, Category = c.Name, Name = Label(recs, spots), Models = new() { g.Key },
                    ItemNames = names, EditorIds = ids, Spots = n == 0 ? -1 : n, Harvestable = types.Contains("FLOR"),
                    Detail = string.Join(" · ", new[] { ids.FirstOrDefault() ?? "", names.Count > 1 ? $"+{names.Count - 1} more share this model" : "",
                                                        n == 0 ? "not placed directly - only where something spawns it" : "" }.Where(x => x.Length > 0)),
                });
            }
            c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (c.Entries.Count > 0) result.Add(c);
        }
        return result;
    }

    public const string CampItemsName = "CAMP items";
    static readonly Regex CampId = new(@"CAMP|Workshop|^ATX_|^SCORE_", RegexOptions.IgnoreCase);

    /// <summary>Things built in a CAMP (collectors, extractors, generators, purifiers, turrets, signs, displays...): the
    /// activators / containers / furniture whose editor id says CAMP, workshop, Atomic Shop or Scoreboard - one entry per
    /// model, none another category already has. Every player's copy lights up, and world spots using the same model.</summary>
    static Category CampItems(GameIndex idx, Func<string, bool> exists, List<Category> cats)
    {
        var c = new Category
        {
            Name = CampItemsName, DefaultStyle = Look("#FFB45C"),
            Description = "Things built in CAMPs - collectors (Pumpkin Pie Stand, Toxic Gooler Cooler...), extractors, generators, purifiers, " +
                          "turrets, signs and displays, named after the game's records. Every player's copy lights up, not only yours. " +
                          "Plain decorations (chairs, beds, statues) aren't in the game index, so they're not here.",
        };
        var covered = cats.SelectMany(x => x.Entries).SelectMany(e => e.Models).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var spots = idx.WorldSpots();
        bool Placeable(EsmRecord r) => r.Type is "ACTI" or "CONT" or "FURN" && r.Name.Trim().Length > 0 && r.Model.Length > 0;
        var camp = idx.Records.Where(r => Placeable(r) && CampId.IsMatch(r.EditorId)).Select(r => NormalizeModel(r.Model)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // names: every named record on that model (the same stand under two names)
        foreach (var g in idx.Records.Where(Placeable).GroupBy(r => NormalizeModel(r.Model), StringComparer.OrdinalIgnoreCase))
        {
            if (!camp.Contains(g.Key) || covered.Contains(g.Key) || g.Key.StartsWith(@"actors\") || NothingDrawn.IsMatch(g.Key) || !exists(g.Key)) continue;
            var all = g.ToList();
            var recs = all.Where(r => CampId.IsMatch(r.EditorId)).ToList();
            int n = all.Sum(r => spots.GetValueOrDefault(r.FormId));
            var names = all.Select(r => r.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var ids = all.Select(r => r.EditorId).Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToList();
            c.Entries.Add(new Entry
            {
                Id = "camp:" + g.Key, Category = c.Name, Name = Label(recs), Models = new() { g.Key },
                ItemNames = names, EditorIds = ids, Spots = n == 0 ? -1 : n,
                Detail = string.Join(" · ", new[] { recs[0].EditorId, names.Count > 1 ? $"+{names.Count - 1} more share this model" : "" }.Where(x => x.Length > 0)),
            });
        }
        c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return c;
    }

    static Entry MakeEntry(Category c, string id, string name, IEnumerable<string> models, Dictionary<string, List<EsmRecord>> byModel, string detail)
    {
        var e = new Entry { Id = id, Category = c.Name, Name = name, Models = models.ToList() };
        var names = e.Models.SelectMany(m => byModel.GetValueOrDefault(m) ?? new())
                            .Select(r => r.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        e.ItemNames = names;
        int others = names.Count(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase));
        e.Detail = detail.Length > 0 ? detail : others > 0 ? $"+{others} more share this model" : "";
        return e;
    }
}
