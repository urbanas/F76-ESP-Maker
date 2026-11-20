using static ItemFinder.Loc;
using System.Text.RegularExpressions;

namespace ItemFinder;

/// <summary>One category per seasonal event. The game tags event content with an editor-ID prefix (SDOW_ for The
/// Slasher, MN2_/E03A_Mischief for Mischief Night...). Objects are taken from those records, but only models that
/// belong mostly to the event - many event objects reuse ordinary models (a cardboard box, a cooler) and would
/// otherwise light up the whole map. The two Halloween events also get hand-picked entries (what the popular
/// ESP mods for them mark).</summary>
public static class EventCatalog
{
    // the events and their hand-picked entries live in rules.json (Rules.cs: EventRule). Notes kept from the code:
    // Spooky Scorched costumes = the heads SpookyBeacons marks plus everything in the game's Creature_Spooky_* outfits and
    // headwear (added in Build). Holiday Scorched: Scorched in Creature_Holiday_* outfits (OTFT CreatureOutfit_Festive_Scorched)
    // and the Festive Scorched Exterminator bosses drop Holiday Gifts; gifts, waterlogged and handmade gifts share giftbox.nif.
    // Raids, expeditions and public events reuse ordinary models: only hand-picked ones ("(?!)" = no automatic entries) -
    // the Gleaming Depths drill is the CAMP large generator, every CAMP would light up. The Pitt's own crates, chems and dead
    // drops come from the records (models under a pitt folder). The Overgrown Moonflower wears only the shared dummy effect
    // skin - not listed.

    /// <summary>Every category: items, events, enemies, and the event enemies copied under ENEMIES.</summary>
    public static List<Category> BuildAll(GameIndex idx, Func<string, bool> exists, IReadOnlyCollection<string> gameFiles)
    {
        var cats = Catalog.Build(idx, exists).Concat(Build(idx, exists)).Concat(Enemies.Build(idx, exists, gameFiles)).ToList();
        cats.Insert(cats.FindLastIndex(c => c.Section == "ITEMS") + 1, Catalog.Foliage(idx, gameFiles));
        // seasonal costumes (Spooky Scorched...) are their own event entries, not "everyday clothes": sharing models made
        // the two tick each other, so a ticked "Scorched: everyday clothes" re-ticked Spooky Scorched on every start
        var costumes = cats.Where(c => c.Section == "EVENTS").SelectMany(c => c.Entries).Where(e => EnemyPicks.Contains(e.Name))
                           .SelectMany(e => e.Models).ToHashSet();
        foreach (var c in cats.Where(c => c.Section == "ENEMIES"))
        {
            foreach (var e in c.Entries.Where(e => e.Id.StartsWith("enemy:") && e.Id.EndsWith(" clothes")))
            {
                e.Models.RemoveAll(costumes.Contains);
                e.Detail = Tf("{0} models - human clothes made to fit them: players wearing these glow too", e.Models.Count);
            }
            c.Entries.RemoveAll(e => e.Id.StartsWith("enemy:") && e.Id.EndsWith(" clothes") && e.Models.Count == 0);
        }
        AddShowmen(idx, exists, cats);
        AddEnemyCopies(cats);
        Rules.Current.AddCategories(cats, idx, exists);
        Rules.Current.FixModels(cats, exists);
        PartNames.NameAll(idx, cats);
        Rules.Current.RenameParts(cats);
        cats.AddRange(Catalog.Rest(idx, exists, cats));
        return cats;
    }

    public static List<Category> Build(GameIndex idx, Func<string, bool> exists)
    {
        var world = idx.Records.Where(r => GameIndex.WorldTypes.Contains(r.Type)).ToList();
        var byModel = world.GroupBy(r => Catalog.NormalizeModel(r.Model)).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<Category>();
        var spots = idx.WorldSpots();
        foreach (var ev in Rules.Current.Events.All)
        {
            var (name, prefixes, ownModels, color, description, picks) = (ev.Name, ev.Prefixes, ev.OwnModels, ev.Color, ev.Description, ev.Picks);
            var c = new Category
            {
                Name = name, Description = description, Section = "EVENTS", Group = ev.Group,
                DefaultStyle = new Style { GlowColor = color, BeamColor = color, BeamLength = 1400 },
            };
            var taken = new HashSet<string>();
            foreach (var p in picks)
            {
                var models = p.Models.Where(exists).ToList();
                void Add(string pick, List<string> ms, string detail)
                {
                    if (ms.Count == 0) return;
                    taken.UnionWith(ms);
                    c.Entries.Add(new Entry
                    {
                        Id = $"event:{name.ToLowerInvariant()}:{pick.ToLowerInvariant()}", Category = name, Name = pick, Models = ms,
                        ItemNames = ms.SelectMany(m => byModel.GetValueOrDefault(m) ?? new()).Select(r => r.Name.Trim()).Where(n => n.Length > 0).Distinct().Append(pick).ToList(),
                        Detail = detail.Length > 0 ? detail : ms.Count == 1 ? Tr("1 model") : Tf("{0} models", ms.Count),
                        // worn outfits are animated (skinned): only the 3D icon survives on them, like on creatures
                        NoIcon = ms.All(m => (m.Contains(@"clothes\") || m.Contains(@"powerarmor\")) && !m.EndsWith("_go.nif")),
                    });
                }
                if (p.Name == SpookyCostumes)
                {
                    // the costumes in two entries: heads (headwear of the game's outfits, hats, masks, helmets, hoods)
                    // and bodies. Every part carries the beam: not every costume has a head piece (a head and an outfit
                    // on the same enemy make two beams in one column - just a little brighter)
                    var (all, heads) = SpookyOutfits(idx);
                    var everything = models.Concat(all).Where(exists).Distinct().ToList();
                    var isHead = everything.Where(m => heads.Contains(m) || HeadPiece.IsMatch(Path.GetFileName(m))).ToHashSet();
                    Add(SpookyHeads, everything.Where(isHead.Contains).ToList(), "masks, hats, helmets, hoods and Silver Shroud gloves · players wearing them glow too");
                    Add(SpookyBodies, everything.Where(m => !isHead.Contains(m)).ToList(), "outfits · players wearing them glow too");
                    continue;
                }
                Add(p.Name, models, p.Detail);
            }
            // the event's own records: plans and notes share a few paper models with everything else - left out
            var rx = Rules.Rx(prefixes);
            var own = Rules.Rx(ownModels);
            var mine = world.Where(r => r.Type is "ACTI" or "CONT" or "MISC" or "FLOR" or "ALCH" or "INGR" or "KEYM" && rx.IsMatch(r.EditorId) && r.Name.Trim().Length > 0);
            foreach (var g in mine.GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var models = g.Select(r => Catalog.NormalizeModel(r.Model)).Distinct()
                              .Where(m => !taken.Contains(m) && exists(m) && own.IsMatch(m) && MostlyEvent(byModel[m], rx)).ToList();
                if (models.Count == 0) continue;
                taken.UnionWith(models);
                c.Entries.Add(new Entry
                {
                    Id = $"event:{name.ToLowerInvariant()}:{g.Key.ToLowerInvariant()}", Category = name, Name = g.Key, Models = models,
                    ItemNames = new() { g.Key }, Harvestable = g.Any(r => r.Type == "FLOR"),
                    Detail = models.Count == 1 ? Tr("1 model") : Tf("{0} models", models.Count),
                });
            }
            if (c.Entries.Count == 0) continue;
            Catalog.CountSpots(c.Entries, byModel, spots);
            c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            result.Add(c);
        }
        return result; // (the category tree sorts them A-Z by the shown name: MainForm.AddSectionNode)
    }

    /// <summary>The pick for the costumes Spooky Scorched wear; built as two entries, heads and bodies.</summary>
    public const string SpookyCostumes = "Spooky Scorched (costumes)", SpookyHeads = "Spooky Scorched (costume heads)",
                        SpookyBodies = "Spooky Scorched (costume bodies)", FestiveCostumes = "Festive Scorched (costumes)";
    // the Silver Shroud costume has no head piece: its gloves carry the beam and icon, as in Spooky Beacons (HeadPiece)
    static Regex HeadPiece => Rules.Rx(Rules.Current.Events.HeadPiece);

    /// <summary>Event entries that are enemies (outfits the event's enemies wear) - also listed under ENEMIES.</summary>
    static List<string> EnemyPicks => Rules.Current.Events.EnemyPicks;

    public const string EventEnemies = "Event enemies";

    /// <summary>Copies of the event enemies in an ENEMIES category (after Human enemies), so they are found there too.
    /// Same models, so ticking one ticks the other (linked ticking).</summary>
    public static void AddEnemyCopies(List<Category> cats)
    {
        var copies = new Category
        {
            Name = EventEnemies, Section = "ENEMIES",
            Description = Tr("Enemies of seasonal events and raids (the Pint-Sized Slasher, Spooky Scorched, costumed Lost, the Gleaming Depths Stalker...). The same entries as under EVENTS - selecting one here selects it there too."),
            DefaultStyle = new Style { GlowFill = true, GlowColor = "#FF7A00", BeamColor = "#FF7A00", BeamLength = 1400 },
        };
        foreach (var c in cats.Where(c => c.Section == "EVENTS"))
            foreach (var e in c.Entries.Where(e => EnemyPicks.Contains(e.Name)))
                copies.Entries.Add(new Entry
                {
                    Id = "eventenemy:" + e.Id["event:".Length..], Category = EventEnemies, Name = e.Name, Models = e.Models.ToList(),
                    ItemNames = e.ItemNames.ToList(), Detail = Tf("{0} · event: {1}", e.Detail, Loc.Name(c.Name)), Harvestable = e.Harvestable,
                    BeamModels = e.BeamModels?.ToHashSet(), NoIcon = e.NoIcon, Spots = e.Spots,
                });
        if (copies.Entries.Count == 0) return;
        copies.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        int at = cats.FindIndex(c => c.Name == "Human enemies");
        cats.Insert(at >= 0 ? at + 1 : cats.Count, copies);
    }

    /// <summary>Models of the game's Spooky Scorched costumes (ARMO Creature_Spooky_*: outfit and headwear), without the
    /// body parts they pull in (hands, skin) - and which of them are worn on the head.</summary>
    static (List<string> All, List<string> Heads) SpookyOutfits(GameIndex idx)
    {
        var all = new List<string>();
        var heads = new List<string>();
        foreach (var armo in idx.Records.Where(r => r.Type == "ARMO" && r.EditorId.StartsWith("Creature_Spooky_", StringComparison.OrdinalIgnoreCase)))
            foreach (var arma in (armo.Parts ?? new()).Select(p => idx.ById.GetValueOrDefault(p)).Where(a => a?.Type == "ARMA"))
                foreach (var m in (arma!.Models ?? new()).Select(Catalog.NormalizeModel).Distinct())
                {
                    if (m.StartsWith(@"actors\")) continue; // hands, skin, effects
                    if (!all.Contains(m)) all.Add(m);
                    if (armo.EditorId.Contains("Headwear", StringComparison.OrdinalIgnoreCase) && !heads.Contains(m)) heads.Add(m);
                }
        return (all, heads);
    }

    public const string Showmen = "Showmen";

    /// <summary>The Showmen of the Atlantic City expeditions: an ENEMIES category with one entry per outfit, read from the
    /// game's outfit list (LLI_Outfit_Showmen_XPD_AC: each outfit = sunglasses + one suit). The sunglasses are left out
    /// (common shades), so the suit carries the beam. Atlantic City expeditions gets a "Showmen" group entry that ticks
    /// them all - a group, not one entry with every model, so ticking a single outfit doesn't light up all of them.</summary>
    static void AddShowmen(GameIndex idx, Func<string, bool> exists, List<Category> cats)
    {
        var list = idx.Records.FirstOrDefault(r => r.Type == "LVLI" && r.EditorId == "LLI_Outfit_Showmen_XPD_AC_NONPLAYABLE");
        if (list?.Parts == null) return;
        var cat = new Category
        {
            Name = Showmen, Section = "ENEMIES",
            Description = Tr("The Showmen of the Atlantic City expeditions, found by the outfits they wear. Anyone wearing the same outfits glows too (players included)."),
            DefaultStyle = new Style { GlowFill = true, GlowColor = Palette.Humans.Hex, BeamColor = Palette.Humans.Hex, BeamLength = 1400 },
        };
        foreach (var outfit in list.Parts.Select(p => idx.ById.GetValueOrDefault(p)).Where(o => o?.Type == "LVLI" && o.Parts != null))
        {
            foreach (var armo in outfit!.Parts!.Select(p => idx.ById.GetValueOrDefault(p)).Where(a => a?.Type == "ARMO"))
            {
                var models = (armo!.Parts ?? new()).Select(p => idx.ById.GetValueOrDefault(p)).Where(a => a?.Type == "ARMA")
                    .SelectMany(a => a!.Models ?? new()).Select(Catalog.NormalizeModel)
                    .Where(m => !m.StartsWith(@"actors\") && exists(m)).Distinct().OrderBy(m => m).ToList();
                // the Cowhide outfit's gloves are Preston Garvey's, worn with many other outfits: the suit is enough
                if (models.Any(m => !m.Contains("glove"))) models.RemoveAll(m => m.Contains("glove"));
                var name = armo.Name.Trim().Length > 0 ? armo.Name.Trim() : armo.EditorId;
                if (models.Count == 0 || cat.Entries.Any(e => e.Name == name)) continue;
                // two outfits on the same models (White Tuxedo, Black and White Tuxedo): one entry naming both
                if (cat.Entries.FirstOrDefault(e => e.Models.SequenceEqual(models)) is Entry same)
                {
                    same.Name += " / " + name;
                    same.ItemNames.Insert(0, name);
                    continue;
                }
                cat.Entries.Add(new Entry
                {
                    Id = "showmen:" + name.ToLowerInvariant(), Category = Showmen, Name = name, Models = models,
                    ItemNames = new() { name, "Showman" }, NoIcon = true,
                    Detail = Tf("{0} models - anyone wearing this outfit glows too", models.Count),
                });
            }
        }
        if (cat.Entries.Count == 0) return;
        cat.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        int at = cats.FindIndex(c => c.Name == Enemies.Humans);
        cats.Insert(at >= 0 ? at + 1 : cats.Count, cat);
        if (cats.FirstOrDefault(c => c.Name == "Atlantic City expeditions") is Category ac)
        {
            ac.Entries.Add(new Entry
            {
                Id = "event:atlantic city expeditions:showmen", Category = ac.Name, Name = Showmen, Group = cat.Entries.ToList(),
                Spots = -1, NoLink = true, ItemNames = new() { Showmen, "Showman" },
                Detail = Tf("{0} outfits - the same entries as under ENEMIES > Showmen", cat.Entries.Count),
            });
            ac.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>More than half of the named things using the model are event records.</summary>
    static bool MostlyEvent(List<EsmRecord> users, Regex rx) => users.Count(r => rx.IsMatch(r.EditorId)) * 2 > users.Count;
}
