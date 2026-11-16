using static ItemFinder.Loc;
using System.Text.RegularExpressions;

namespace ItemFinder;

/// <summary>Enemy categories built from the game's races: each creature species is an entry whose models are
/// the body and outfit parts belonging to that race (armor addons). Robots are assembled from parts that the
/// data doesn't tie to a race, so they are grouped by part file names. Bosses with a model of their own get
/// individual entries, and those models are taken out of the species entries.</summary>
public static class Enemies
{
    public const string Humans = "Human enemies";
    public const string Bosses = "Bosses", Marked = "Legendary & bounty enemies";

    // the lists live in rules.json (Rules.cs: EnemyRules says what each one is for). Notes kept from the code:
    // NotBody - the Wendigo scout outfit is an unused skin (user, 2026-11-11); creature hats are left out (user, 2026-11-15:
    //   Brahmin listed 5 hats besides its 2 bodies). "Harmless animals" has no keyword: filled from Hostile animals
    //   (docile / livestock). Guardian (EN06) = every part of RD01_Enc01_GuardianBot plus its base; most parts are shared
    //   with other robots (SharedRobotParts, from their NPC records) and selected together (Entry.LinkParts).
    //   Cultists: the Cult Outfit = the robes most cultist enemies wear; pastor outfit, wood armour and the sack hood come
    //   from the cultist lists (no other NPC wears them); the leather / metal armour in those lists is left out.
    static EnemyRules R => Rules.Current.Enemies;
    static List<KindRule> Kinds => R.Kinds;
    static Regex SkipRace => Rules.Rx(R.SkipRace);
    static Regex NotBody => Rules.Rx(R.NotBody);
    static Regex SkinExtra => Rules.Rx(R.SkinExtra);
    static Regex NoBeam => Rules.Rx(R.NoBeam);
    static List<NamePattern> RobotParts => R.RobotParts;
    static Dictionary<string, string> GuardianParts => R.GuardianParts;
    static List<SharedParts> SharedRobotParts => R.SharedRobotParts;
    static List<NamedModels> BossModels => R.BossModels;
    static List<HumanGroup> HumanGroups => R.HumanGroups;
    static Dictionary<string, string> OutfitNames => R.OutfitNames;

    /// <summary>A human enemy's part as "Outfit - piece (female)": the outfit from the folder, the piece and body type from
    /// the file name (clothes\pastor\foutfit.nif = "Pastor outfit - outfit (female)").</summary>
    static string HumanPartName(string model)
    {
        var dirs = Path.GetDirectoryName(model)!.ToLowerInvariant().Split('\\')
                       .Where(d => d is not ("clothes" or "armor" or "atx" or "actors" or "f" or "m" or "characterassets")).ToList();
        string folder = dirs.LastOrDefault() ?? "";
        string outfit = OutfitNames.GetValueOrDefault(folder)
                        ?? System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(folder.Replace("atx_", "").Replace('_', ' '));
        string f = Path.GetFileNameWithoutExtension(model).ToLowerInvariant();
        string piece = f switch
        {
            _ when f.Contains("hood") || f.Contains("hate") || f.Contains("headgear") => "hood",
            _ when f.Contains("hatd") => "headpiece",
            _ when f.Contains("helmet") => "helmet",
            _ when f.Contains("hat") => "hat",
            _ when f.Contains("mask") => "mask",
            _ when f.Contains("glove") => "gloves",
            _ when f.Contains("leftarm") || f.Contains("larm") => "left arm",
            _ when f.Contains("rightarm") || f.Contains("rarm") => "right arm",
            _ when f.Contains("leftleg") || f.Contains("lleg") => "left leg",
            _ when f.Contains("rightleg") || f.Contains("rleg") => "right leg",
            _ when f.Contains("chest") || f.Contains("torso") => "chest",
            _ when f.Contains("eye") => "eyes",
            _ when f.Contains("mouth") => "mouth",
            _ when f.Contains("head") => "head",
            _ when f.Contains("outfit") || f.Contains("body") || f.Contains("robe") || f.StartsWith(folder) => "outfit",
            _ => f.Replace('_', ' '),
        };
        string who = System.Text.RegularExpressions.Regex.IsMatch(f, @"(^f_|_f$|_f_|female|^foutfit)") ? " (female)"
                   : System.Text.RegularExpressions.Regex.IsMatch(f, @"(^m_|_m$|_m_|male|^moutfit)") ? " (male)" : "";
        return $"{outfit} - {piece}{who}{(f.Contains("facebones") ? ", face fit" : "")}";
    }

    /// <summary>Not what others see: first-person arms, dropped-item models.</summary>
    static Regex HumanSkip => Rules.Rx(R.HumanSkip);
    static Regex HumanNoBeam => Rules.Rx(R.HumanNoBeam);

    public static List<Category> Build(GameIndex idx, Func<string, bool> exists, IReadOnlyCollection<string> gameFiles)
    {
        var kw = idx.Records.Where(r => r.Type == "KYWD").ToDictionary(r => r.FormId, r => r.EditorId);
        var arma = idx.Records.Where(r => r.Type == "ARMA" && r.Models != null).ToList();
        string Norm(string m) => Catalog.NormalizeModel(m);
        // which races use each model - models also used by other races (humans, players) are left out
        var racesOf = new Dictionary<string, HashSet<uint>>();
        foreach (var a in arma)
            foreach (var m in a.Models!.Select(Norm))
                (racesOf.TryGetValue(m, out var s) ? s : racesOf[m] = new()).Add(a.Link); // main race only: Scorched outfits list
                                                                                           // humans as a second fit
        // characters by race (their default outfit is in Product)
        var npcsByRace = idx.Records.Where(r => r.Type == "NPC_" && r.Product != 0).GroupBy(r => r.Link).ToDictionary(g => g.Key, g => g.ToList());
        var meshFiles = gameFiles.Where(p => p.StartsWith(@"meshes\actors\") && p.EndsWith(".nif")).Select(p => p[7..]).ToList();

        // --- bosses first: their models are taken out of the species
        var bosses = new Category
        {
            Name = Bosses,
            Description = Tr("Bosses that have a model of their own, so each can be picked alone. Bosses that are just a bigger version of a normal creature share its model and are in that creature's entry."),
            DefaultStyle = new Style { GlowFill = true, GlowColor = "#FF2020", BeamColor = "#FF2020", BeamLength = 2100 },
        };
        var bossModels = new HashSet<string>();
        foreach (var (name, models) in BossModels)
        {
            var files = models.SelectMany(m => m.EndsWith('\\') ? meshFiles.Where(f => f.StartsWith(m) && !NotBody.IsMatch(f)) : new[] { m })
                              .Where(exists).Distinct().ToList();
            if (files.Count == 0) continue;
            var e = new Entry { Id = "boss:" + name.ToLowerInvariant(), Category = Bosses, Name = name, Models = files, ItemNames = new() { name } };
            e.BeamModels = files.Where(f => !NoBeam.IsMatch(Path.GetFileName(f))).Take(1).ToHashSet();
            if (e.BeamModels.Count == 0) e.BeamModels.Add(files[0]);
            e.Detail = files.Count == 1 ? Tr("own model") : Tf("{0} models", files.Count);
            if (files.All(GuardianParts.ContainsKey))
            {
                e.PartNames = new(GuardianParts);
                e.LinkParts = true;
                e.Detail = Tf("{0} parts - shared parts are selected together in every robot that uses them", files.Count);
            }
            bosses.Entries.Add(e);
            bossModels.UnionWith(files.Where(f => !f.Contains(@"\robot\parts\"))); // shared robot parts stay with the robots too
        }

        // --- species by race
        var groups = new Dictionary<(string Cat, string Name), (HashSet<uint> Races, List<string> Edids)>();
        foreach (var race in idx.Records.Where(r => r.Type == "RACE" && r.Name.Trim().Length > 0 && !SkipRace.IsMatch(r.EditorId)))
        {
            var words = (race.Keywords ?? new()).Select(k => kw.GetValueOrDefault(k) ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            if ((words.Contains("ActorTypeNPC") || words.Contains("ActorTypeHuman")) && !words.Contains("ActorTypeScorched")) continue;
            var kind = Kinds.FirstOrDefault(k => k.Keywords.Any(words.Contains));
            if (kind == null) continue;
            var category = kind.Category;
            if (category == "Hostile animals" && (words.Contains("ActorTypeDocile") || words.Contains("ActorTypeLivestock"))) category = "Harmless animals";
            var key = (category, race.Name.Trim());
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = (new(), new());
            g.Races.Add(race.FormId);
            g.Edids.Add(race.EditorId);
        }
        var cats = Kinds.Select(k => new Category { Name = k.Category, Description = k.Description, DefaultStyle = new Style { GlowFill = true, GlowColor = k.Color, BeamColor = k.Color, BeamLength = 1400 } })
                        .ToDictionary(c => c.Name);
        // a model several species use stays with the one named in its file name (Trog lists wendigo.nif)
        static string Token(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z]", "");
        var groupOfRace = groups.SelectMany(g => g.Value.Races.Select(r => (r, Name: g.Key.Name))).GroupBy(x => x.r).ToDictionary(x => x.Key, x => x.First().Name);
        bool Belongs(string model, string name)
        {
            var owners = racesOf[model].Select(r => groupOfRace.GetValueOrDefault(r)).OfType<string>().Distinct().ToList();
            if (owners.Count <= 1) return true;
            string file = Token(Path.GetFileNameWithoutExtension(model)), path = Token(model);
            var byFile = owners.Where(o => file.Contains(Token(o))).ToList();
            if (byFile.Count > 0) return byFile.Contains(name);
            var byPath = owners.Where(o => path.Contains(Token(o))).ToList();
            return byPath.Count == 0 || byPath.Contains(name);
        }
        foreach (var ((cat, name), (races, edids)) in groups)
        {
            var skins = races.Select(r => idx.ById[r].Link).Where(s => s != 0 && idx.ById.ContainsKey(s))
                             .SelectMany(s => idx.ById[s].Parts ?? new()).ToHashSet();
            var models = new List<string>();
            var body = new HashSet<string>();
            foreach (var a in arma.Where(a => races.Contains(a.Link)))
                foreach (var m in a.Models!.Select(Norm))
                {
                    if (NotBody.IsMatch(m) || bossModels.Contains(m) || !exists(m)) continue;
                    // not the creature: an Atomic Shop skin (the eyebot race lists the player's camera drone,
                    // atx_cambot) or a vehicle (vertibots list the parked vertibird) - scan 2026-11-15
                    if (m.StartsWith(@"atx\") || m.StartsWith(@"vehicles\")) continue;
                    // shared with people (clothes players wear too) - but a creature's own body file stays, even when a
                    // human-race record borrows it (the Scorched body is also the skin of Scorched statues)
                    bool ownBody = m.StartsWith(@"actors\") && !m.StartsWith(@"actors\character\") && !m.StartsWith(@"actors\powerarmor\");
                    if (!ownBody && racesOf[m].Any(r => !races.Contains(r) && IsHumanish(idx, kw, r))) continue;
                    if (!Belongs(m, name)) continue;
                    if (!models.Contains(m)) models.Add(m);
                    // outfits replace the body, so each may carry the beam; skin parts are chosen below
                    if (!skins.Contains(a.FormId) && !NoBeam.IsMatch(Path.GetFileName(m)) && !SkinExtra.IsMatch(Path.GetFileName(m))) body.Add(m);
                }
            if (models.Count == 0) continue;
            // a skin's parts are all visible at once (body + glass helmet, body + hair): one beam per skin, on the body
            foreach (var skin in races.Select(r => idx.ById[r].Link).Where(s => idx.ById.ContainsKey(s)).Distinct())
            {
                var parts = (idx.ById[skin].Parts ?? new()).Where(idx.ById.ContainsKey).SelectMany(p => idx.ById[p].Models ?? new())
                                                           .Select(Norm).Where(models.Contains).Distinct().ToList();
                var main = parts.FirstOrDefault(p => !NoBeam.IsMatch(Path.GetFileName(p)) && !SkinExtra.IsMatch(Path.GetFileName(p))) ?? parts.FirstOrDefault();
                if (main != null) body.Add(main);
            }
            // a boss with the same name (Bigfoot, Overgrown Tank): its extra parts join the boss entry
            if (bosses.Entries.FirstOrDefault(b => Token(b.Name) == Token(name)) is Entry boss)
            {
                boss.Models.AddRange(models.Where(m => !boss.Models.Contains(m) && !m.Contains(@"\supermutantbehemoth\characterassets\")));
                boss.Detail = boss.Models.Count == 1 ? Tr("own model") : Tf("{0} models", boss.Models.Count);
                continue;
            }
            var e = new Entry { Id = $"enemy:{cat.ToLowerInvariant()}:{name.ToLowerInvariant()}", Category = cat, Name = name, Models = models, ItemNames = new() { name } };
            e.BeamModels = body.Count > 0 ? body : new HashSet<string> { models[0] };
            e.Detail = models.Count == 1 ? Tr("1 model") : Tf("{0} models (body and outfits)", models.Count);
            cats[cat].Entries.Add(e);
            // everyday clothes made to fit them too (a Scorched in coveralls): human clothing, so players wearing it glow
            // too - a separate entry, off unless ticked
            // what they really wear: their characters' default outfits (through leveled lists) -> the armour pieces ->
            // the fit made for their race, else the human one
            var worn = new HashSet<uint>();
            void Expand(uint id, int depth)
            {
                if (depth > 6 || !idx.ById.TryGetValue(id, out var x)) return;
                if (x.Type == "ARMO") worn.Add(id);
                else if (x.Type is "LVLI" or "OTFT") foreach (var p in x.Parts ?? new()) Expand(p, depth + 1);
            }
            // not one-off location quest characters (LC097_R_Vargas: a suited executive filed under the Mirelurk Spawn race
            // gave mirelurk spawn a suit and a trilby - user, 2026-11-11)
            foreach (var npc in npcsByRace.Where(n => races.Contains(n.Key)).SelectMany(n => n.Value).Where(n => !Rules.Rx(R.LocationNpc).IsMatch(n.EditorId)))
                Expand(npc.Product, 0);
            var clothes = new List<string>();
            foreach (var armo in worn.Select(w => idx.ById[w]))
            {
                var fits = (armo.Parts ?? new()).Select(p => idx.ById.GetValueOrDefault(p)).OfType<EsmRecord>().Where(x => x.Type == "ARMA" && x.Models != null).ToList();
                var mine = fits.Where(x => races.Contains(x.Link) || (x.Parts ?? new()).Any(races.Contains)).ToList();
                foreach (var m in (mine.Count > 0 ? mine : fits).SelectMany(x => x.Models!.Select(Norm)).Distinct())
                    // never the human body (hands, skin): that would light up every player and NPC
                    // nor power armour (a Scorched outfit list holds a PA helmet: every player in PA would glow)
                    if (!m.StartsWith(@"actors\character\") && !m.Contains(@"powerarmor\") &&
                        !models.Contains(m) && !clothes.Contains(m) && !NotBody.IsMatch(m) && !bossModels.Contains(m) && exists(m) &&
                        racesOf[m].Any(r => !races.Contains(r) && IsHumanish(idx, kw, r)))
                        clothes.Add(m);
            }
            if (clothes.Count > 0)
                cats[cat].Entries.Add(new Entry
                {
                    Id = $"enemy:{cat.ToLowerInvariant()}:{name.ToLowerInvariant()} clothes", Category = cat, Name = $"{name}: everyday clothes",
                    Models = clothes, ItemNames = new() { name }, NoIcon = true,
                    Detail = Tf("{0} models - human clothes made to fit them: players wearing these glow too", clothes.Count),
                });
        }

        // --- the effect the game attaches to every Scorched (plague patches and particles) - whatever it wears. Its glow
        // marks every Scorched. The beam / icon go on the heads: every Scorched has exactly one,
        // whatever it wears (tested in game: a 3D model on the effect never showed - it doesn't carry extra shapes)
        const string ScorchedFx = @"actors\scorched\scorchedattachfx.nif";
        if (cats.GetValueOrDefault("Scorched")?.Entries.FirstOrDefault(e => e.Name == "Scorched") is Entry scorched && exists(ScorchedFx))
        {
            scorched.Models.Add(ScorchedFx);
            var heads = scorched.Models.Where(m => Regex.IsMatch(Path.GetFileName(m), @"^base(fe)?maleheadscorched\d*\.nif$", RegexOptions.IgnoreCase)).ToHashSet();
            scorched.BeamModels = heads.Count > 0 ? heads : new() { ScorchedFx };
            scorched.Detail = Tf("{0} models (body, heads, outfits and the Scorched effect - beam on the head)", scorched.Models.Count);
        }

        // --- robots built from parts (the data doesn't tie the parts to robot races)
        var used = cats.Values.SelectMany(c => c.Entries).SelectMany(e => e.Models).Concat(bossModels).ToHashSet();
        var partFiles = meshFiles.Where(f => (f.Contains(@"\robot\parts\") || f.Contains(@"\createabot\")) && !NotBody.IsMatch(f) && !used.Contains(f)).ToList();
        foreach (var (name, pattern) in RobotParts)
        {
            var rx = Rules.Rx(pattern);
            var files = partFiles.Where(f => rx.IsMatch(Path.GetFileName(f)) || rx.IsMatch(Path.GetDirectoryName(f) ?? "")).Where(exists).ToList();
            if (files.Count == 0) continue;
            partFiles.RemoveAll(files.Contains);
            var torso = files.Where(f => Path.GetFileName(f).StartsWith("torso") && !Path.GetFileName(f).Contains("armor")).ToHashSet();
            // a robot race with a few models of its own (e.g. Robobrain's clothes) gets the parts added
            static string Key(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z]", "");
            var e = cats["Robots"].Entries.FirstOrDefault(x => Key(x.Name) == Key(name));
            if (e == null)
            {
                e = new Entry { Id = "enemy:robots:" + name.ToLowerInvariant(), Category = "Robots", Name = name, ItemNames = new() { name }, BeamModels = new() };
                cats["Robots"].Entries.Add(e);
            }
            e.Models.AddRange(files.Where(f => !e.Models.Contains(f)));
            e.BeamModels = torso.Count > 0 ? torso : e.BeamModels is { Count: > 0 } ? e.BeamModels : new HashSet<string> { files[0] };
            e.Detail = Tf("{0} parts", e.Models.Count);
        }
        foreach (var (robot, parts) in SharedRobotParts)
        {
            if (cats["Robots"].Entries.FirstOrDefault(x => x.Name == robot) is not Entry r) continue;
            r.LinkParts = true;
            r.PartNames ??= new();
            foreach (var (m, label) in GuardianParts.Where(p => parts.Contains(Path.GetFileNameWithoutExtension(p.Key)) && exists(p.Key)))
            {
                if (!r.Models.Contains(m)) r.Models.Add(m);
                r.PartNames[m] = label;
            }
            r.Detail = Tf("{0} parts", r.Models.Count);
        }

        bosses.Section = "ENEMIES";
        foreach (var c in cats.Values) c.Section = "ENEMIES";
        foreach (var c in cats.Values) c.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        bosses.Entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        // --- marked enemies: the game hangs an effect on them; a beam added to that effect follows the enemy.
        // (ESP_Legendaries-style mods replace the same effect with a marker.)
        var marked = new Category
        {
            Name = Marked,
            Description = Tr("The game attaches an aura effect to legendary enemies and to Burning Springs bounty targets. The beam is added to that effect, so it follows the enemy - whatever kind it is. Glow has no effect here (the aura has no solid parts); the game's own aura stays."),
            DefaultStyle = new Style { Glow = false, Beam = true, GlowColor = "#FFD700", BeamColor = "#FFD700", BeamLength = 2100 },
        };
        foreach (var (name, models) in R.MarkedModels)
        {
            var files = models.Where(exists).ToList();
            if (files.Count > 0)
                marked.Entries.Add(new Entry { Id = "marked:" + name.ToLowerInvariant(), Category = Marked, Name = name, Models = files, ItemNames = new() { name }, Detail = Tr("beam on the aura effect") });
        }

        var humans = new Category
        {
            Name = Humans, Section = "ENEMIES",
            Description = Tr("Human enemies are people like players, so they are found by what only they wear - their outfits and (for the Lost) their faces. Where players can wear the same outfit, they glow too (noted per entry)."),
            DefaultStyle = new Style { GlowFill = true, GlowColor = Palette.Humans.Hex, BeamColor = Palette.Humans.Hex, BeamLength = 1400 },
        };
        var allMeshes = gameFiles.Where(p => p.StartsWith(@"meshes\") && p.EndsWith(".nif")).Select(p => p[7..]).ToList();
        foreach (var (name, folders, beam, note) in HumanGroups)
        {
            var files = allMeshes.Where(f => folders.Any(f.StartsWith) && !NotBody.IsMatch(f) && !HumanSkip.IsMatch(Path.GetFileName(f)) && exists(f))
                                 .Distinct().OrderBy(f => f).ToList();
            if (files.Count == 0) continue;
            var beamRx = beam.Length > 0 ? Rules.Rx(beam) : null;
            var body = files.Where(f => beamRx?.IsMatch(Path.GetFileName(f)) ?? !HumanNoBeam.IsMatch(Path.GetFileName(f))).ToHashSet();
            humans.Entries.Add(new Entry
            {
                Id = "human:" + name.ToLowerInvariant(), Category = Humans, Name = name, Models = files, ItemNames = new() { name },
                BeamModels = body.Count > 0 ? body : new HashSet<string> { files[0] },
                PartNames = files.ToDictionary(f => f, HumanPartName),
                Detail = Tf("{0} models - {1}", files.Count, note),
            });
        }

        // creature models are animated (skinned): the game drops the icon's camera-facing node there
        foreach (var e in cats.Values.SelectMany(c => c.Entries).Concat(bosses.Entries).Concat(humans.Entries)) e.NoIcon = true;

        var result = new List<Category>();
        marked.Section = "ENEMIES";
        if (marked.Entries.Count > 0) result.Add(marked);
        result.Add(bosses);
        if (humans.Entries.Count > 0) result.Add(humans);
        result.AddRange(cats.Values.Where(c => c.Entries.Count > 0));
        return result;
    }

    /// <summary>People: humans, ghouls, the player - their clothes must never glow.</summary>
    static bool IsHumanish(GameIndex idx, Dictionary<uint, string> kw, uint race)
    {
        if (!idx.ById.TryGetValue(race, out var r) || r.Type != "RACE") return true; // unknown race: be careful
        var words = (r.Keywords ?? new()).Select(k => kw.GetValueOrDefault(k) ?? "");
        return words.Any(w => w is "ActorTypeNPC" or "ActorTypeHuman");
    }
}
