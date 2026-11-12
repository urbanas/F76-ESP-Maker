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

    /// <summary>Category by race keyword, first match wins.</summary>
    static readonly (string Category, string[] Keywords, string Color, string Description)[] Kinds =
    {
        ("Robots", new[] { "ActorTypeRobot", "ActorTypeTurret" }, Palette.Robots.Hex, "Robots and turrets. Robots are built from parts; every part glows, the beam rises from the torso."),
        ("Scorched", new[] { "ActorTypeScorched" }, Palette.Scorched.Hex, "Scorched and Scorchbeasts. Their outfits are Scorched-only; clothing they share with players is left out so you don't glow yourself."),
        ("Super Mutants", new[] { "ActorTypeSuperMutant", "ActorTypeSuperMutantBehemoth" }, Palette.Mutants.Hex, "Super Mutants and Behemoths, with their armor pieces."),
        ("Feral Ghouls", new[] { "ActorTypeFeralGhoul" }, Palette.Ghouls.Hex, "Feral ghouls, glowing ones and their outfits."),
        ("Cryptids", new[] { "ActorTypeCryptid" }, Palette.Cryptids.Hex, "Mothman, Snallygaster, Grafton Monster, Flatwoods Monster and other cryptids."),
        ("Insects & bugs", new[] { "ActorTypeBug" }, Palette.Bugs.Hex, "Bloatflies, bloodbugs, stingwings, radroaches, ants, crickets..."),
        ("Mirelurks & crabs", new[] { "ActorTypeMirelurk", "ActorTypeMirelurkKing", "ActorTypeArthropods" }, Palette.Water.Hex, "Mirelurks, hunters, kings, hermit crabs, radscorpions..."),
        ("Hostile animals", new[] { "ActorTypeAnimal" }, Palette.Hunters.Hex, "Animals that attack - wolves, yao guai, mole rats, mongrels, attack dogs..."),
        // filled from the line above: animals the game tags docile or livestock (no keyword matches this one)
        ("Harmless animals", new[] { "" }, Palette.Docile.Hex, "Animals that don't attack - brahmin, radstags, chickens, rabbits, foxes, squirrels..."),
        ("Other creatures", new[] { "ActorTypeCreature" }, Palette.Others.Hex, "Every other creature."),
    };

    /// <summary>Races that aren't enemies (pets, humans, test and cut content).</summary>
    static readonly Regex SkipRace = new(@"^(CAMPPets_|CUT_|DEL_|zz|Dev|DLC05ArmorRack|PowerArmor|Human|Main_Human|Ghoul|GHL_|Lost(Healer)?Race|Fisherman|P62_|ATX_COMP_|Dummy|RedRocketRobot|DogCollectron|MechTest|SQ_|DogmeatRace|CatRace|CatPetRace|GhoulChild|SCORE_|DLC01WorkshopWallLight|TurretWorkshop)", RegexOptions.IgnoreCase);

    /// <summary>Files that aren't the creature's body: effects, skeletons, projectiles, severed-limb pieces... and the
    /// Wendigo scout outfit (an unused skin: wendigos wear no clothes in game - user, 2026-11-11).</summary>
    static readonly Regex NotBody = new(@"(wendigoscoutoutfit|dummyfxskin|skeleton|\\_rig\\|fx|explosion|projectile|hazard|muzzle|morphs\\|replace|dest\.nif|dest\d|lite\.nif|light|overheat|particles|attack|despawn|intro|landingeffects|standin|dummyweapon|chargeao|chargestealth|dest(chest|[lr]arm|head)|legdest|headdest|mps|encryptedfire)", RegexOptions.IgnoreCase);

    /// <summary>Small attached parts: they glow, but don't get a beam (one beam per creature).</summary>
    /// <summary>Skin add-ons drawn on top of the body (alien glass helmet, bloodbug hair, mole rat sores...).</summary>
    static readonly Regex SkinExtra = new(@"(glass|hair|sores|nosering|mohawk|fur[._]|flower|vine|spikes|corpse|cordycep|collar)", RegexOptions.IgnoreCase);

    static readonly Regex NoBeam = new(@"(hand|head|hat|glasses|backpack|glove|helmet|mask|eye|crown|mouth|horns|tie|bowtie|ascot|smock|chains|headband|arm|leg|decal|armor|armour|plate|eggs)", RegexOptions.IgnoreCase);

    /// <summary>Robots: part file name word -> species. Torso parts carry the beam.</summary>
    static readonly (string Name, string Pattern)[] RobotParts =
    {
        ("Protectron", "protectron"),
        ("Assaultron", "assaultron"),
        ("Sentry Bot", "sentry"),
        ("Mr. Handy", @"(handy|gutsy|rose_|sawhand|flamethrowerhand|pincherhand|armstypea|mobsterhat)"),
        ("Robobrain", "robobrain"),
    };

    /// <summary>EN06: every part of its NPC record (RD01_Enc01_GuardianBot) plus the base it rises from. Most are files other
    /// robots use too, so they are listed there as well (SharedRobotParts) and selected together (Entry.LinkParts).</summary>
    static readonly Dictionary<string, string> GuardianParts = new()
    {
        [@"actors\createabot\guardianbot\torsoguardianbot.nif"] = "Torso",
        [@"actors\createabot\guardianbottop.nif"] = "Top",
        [@"actors\createabot\guardianbot\guardianbotbase.nif"] = "Base",
        [@"actors\createabot\guardianbot\guardianbotdoors01.nif"] = "Base doors",
        [@"actors\robot\parts\headsentrytype1.nif"] = "Head (shared)",
        [@"actors\robot\parts\headsentrytype1armor.nif"] = "Head armor (shared)",
        [@"actors\robot\parts\sentryfrontarmor.nif"] = "Front armor (shared)",
        [@"actors\robot\parts\sentrytoparmor.nif"] = "Top armor (shared)",
        [@"actors\robot\parts\armleftsentrybot.nif"] = "Left arm (shared)",
        [@"actors\robot\parts\armrightsentrybot.nif"] = "Right arm (shared)",
        [@"actors\robot\parts\armleftsentryarmor.nif"] = "Left arm armor (shared)",
        [@"actors\robot\parts\armrightsentryarmor.nif"] = "Right arm armor (shared)",
        [@"actors\robot\parts\sentryleftshoulderclustergrenadelauncher.nif"] = "Left shoulder launcher (shared)",
        [@"actors\robot\parts\sentryrightshoulderclustergrenadelauncher.nif"] = "Right shoulder launcher (shared)",
        [@"actors\dlc01\robot\parts\robots_handleftautomaticlaser.nif"] = "Left laser hand (shared)",
        [@"actors\dlc01\robot\parts\robots_handrightautomaticlaser.nif"] = "Right laser hand (shared)",
    };

    /// <summary>The robots using EN06's shared parts, from the part lists of their NPC records.</summary>
    static readonly (string Robot, string[] Parts)[] SharedRobotParts =
    {
        ("Sentry Bot", new[] { "armleftsentrybot", "armrightsentrybot", "armleftsentryarmor", "armrightsentryarmor", "headsentrytype1", "headsentrytype1armor", "sentryfrontarmor", "sentrytoparmor",
                               "sentryleftshoulderclustergrenadelauncher", "sentryrightshoulderclustergrenadelauncher", "robots_handleftautomaticlaser", "robots_handrightautomaticlaser" }),
        ("Robobrain", new[] { "armleftsentrybot", "armrightsentrybot", "armleftsentryarmor", "armrightsentryarmor", "headsentrytype1", "sentryfrontarmor", "sentrytoparmor" }),
        ("Protectron", new[] { "armleftsentryarmor", "armrightsentryarmor", "headsentrytype1armor", "sentryfrontarmor", "sentrytoparmor", "robots_handrightautomaticlaser" }),
        ("Mr. Handy", new[] { "armrightsentryarmor" }),
    };

    /// <summary>Bosses with models of their own (meshes\ paths; folders end with \). Missing files are skipped.</summary>
    static readonly (string Name, string[] Models)[] BossModels =
    {
        ("Scorchbeast Queen", new[] { @"actors\scorchbeast\characterassets\scorchbeastqueen.nif" }),
        ("Earle Williams (Wendigo Colossus)", new[] { @"actors\wendigocolossus\characterassets\wendigocolossus.nif", @"actors\wendigocolossus\characterassets\wendigocolossus_boss.nif" }),
        ("Ultracite Terror", new[] { @"actors\scorchtongue\characterassets\scorchtongue.nif", @"actors\scorchtonguebody\characterassets\scorchtonguebody.nif", @"actors\scorchtonguetail\characterassets\scorchtonguetail.nif", @"actors\scorchtonguetail\scorchtonguetail.nif" }),
        ("Ultracite Abomination", new[] { @"actors\ultraciteabomination\characterassets\ultraciteabomination.nif" }),
        ("Storm Goliaths", new[] { @"actors\stormboss\characterassets\stormboss.nif", @"actors\stormboss\parts\" }),
        ("Mirelurk Queen", new[] { @"actors\mirelurkqueen\characterassets\mirelurkqueen.nif", @"actors\mirelurkqueen\characterassets\mirelurkqueen_parasitic.nif" }),
        ("Wise Mothman", new[] { @"actors\mothman\characterassets\mothmanmeshexportroot_wise.nif", @"actors\mothman\characterassets\mothmanmeshexportroot_wise_equinox.nif" }),
        ("Rust King", new[] { @"actors\supermutant\rustking\characterassets\rustkingbody01.nif", @"actors\supermutant\rustking\characterassets\rustkingarmour01.nif", @"actors\supermutant\rustking\characterassets\rustkingcrown01.nif", @"actors\supermutant\rustking\characterassets\rustkinghead01.nif", @"actors\supermutant\rustking\characterassets\rustkingeye01.nif", @"actors\supermutant\rustking\characterassets\rustkingmouth01.nif", @"actors\supermutant\rustking\characterassets\rustkingheadmeatcap.nif" }),
        ("Behemoth (boss)", new[] { @"actors\supermutantbehemoth\boss\supermutantbehemothboss.nif" }),
        ("Bigfoot", new[] { @"actors\supermutantbehemoth\bigfoot\characterassets\supermutantbehemoth_bigfoot.nif" }),
        ("Overgrown Tank", new[] { @"actors\supermutant\overgrown\overgrowntank.nif", @"actors\supermutant\overgrown\overgrowntankflowres.nif" }),
        ("Guardian (EN06)", GuardianParts.Keys.ToArray()),
        ("Imposter Sheepsquatch", new[] { @"actors\createabot\characterassets\sheepsquatchfanaticoutfit.nif" }),
        ("Jersey Devil", new[] { @"actors\jerseydevil\characterassets\jerseydevil.nif" }),
        ("Blue Devil", new[] { @"actors\deathclaw\bluedevil\bluedevil.nif", @"actors\deathclaw\bluedevil\bluedevil_main.nif" }),
    };

    /// <summary>Human enemies share the human body with players, so they are found by what only
    /// they wear: model folders, the parts that carry the beam, and a note when players can wear it too.</summary>
    static readonly (string Name, string[] Folders, string Beam, string Note)[] HumanGroups =
    {
        ("Lost", new[] { @"actors\storm_lost\", @"clothes\vaultarmourlost\", @"clothes\vaultsuitburntlost\" }, @"eyes\.nif$", "faces, eyes and Lost-only armour"),
        ("Rust Kingdom raiders", new[] { @"clothes\rustkingdomraiders\" }, @"(chest|rustraider_[abc]_[mf]\.nif)", "their own armour"),
        ("Raiders", new[] { @"clothes\raider\", @"clothes\raider04_fashion\", @"clothes\raider06_ski\" }, "", "players wearing these outfits glow too"),
        ("Gas-mask raiders", new[] { @"atx\clothes\gasmaskraider\" }, "", "players wearing this outfit glow too"),
        ("Blood Eagle mercs", new[] { @"atx\clothes\bloodeaglemerc_charmer\" }, "", "players wearing this outfit glow too"),
        // Cult Outfit = the robes most cultist enemies wear (Outfit_Cultist levelled lists); the rest is random armour
        // pastor outfit, wood armour and the sack hood come from the cultist lists and (apart from a few corpses) no other
        // NPC wears them; the leather / metal armour and under-armour also in those lists are left out - hundreds of
        // raiders, settlers and players wear them
        ("Cultists", new[] { @"clothes\cultist", @"clothes\cultoutfit\", @"clothes\pastor\", @"armor\wood\", @"armor\raidermodarmor\headgear1" }, "",
         "players wearing cultist robes, the pastor outfit, wood armour or the sack hood glow too"),
        ("Communists", new[] { @"atx\clothes\communist" }, "", "players wearing these outfits glow too"),
        ("Fanatics", new[] { @"atx\clothes\atx_fanatic", @"actors\powerarmor\fanaticspa\" }, "", "players wearing these outfits glow too"),
    };

    /// <summary>Readable outfit names for the parts list (folder names run words together).</summary>
    static readonly Dictionary<string, string> OutfitNames = new()
    {
        ["cultistadept"] = "Cultist Adept robes", ["cultistelder"] = "Cultist Elder robes", ["cultistincarnatehelmet"] = "Cultist Incarnate",
        ["cultistneophyte"] = "Cultist Neophyte robes", ["cultoutfit"] = "Cult outfit", ["pastor"] = "Pastor outfit", ["wood"] = "Wood armour",
        ["raidermodarmor"] = "Sack hood", ["vaultarmourlost"] = "Lost vault armour", ["vaultsuitburntlost"] = "Lost burnt vault suit",
        ["rustkingdomraiders"] = "Rust Kingdom armour", ["gasmaskraider"] = "Gas-mask raider", ["bloodeaglemerc_charmer"] = "Blood Eagle merc",
        ["fanaticspa"] = "Fanatic power armour", ["storm_lost"] = "Lost face",
    };

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
    static readonly Regex HumanSkip = new(@"(go\.nif$|_go_|^go_|1st|_fp\.nif$|_1p\.nif$)", RegexOptions.IgnoreCase);
    static readonly Regex HumanNoBeam = new(@"(helmet|hat|glove|hood|mask|eye|mouth|facebones|arm|leg|scarf|bag)", RegexOptions.IgnoreCase);

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
            if (kind.Category == null) continue;
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
            foreach (var npc in npcsByRace.Where(n => races.Contains(n.Key)).SelectMany(n => n.Value).Where(n => !Regex.IsMatch(n.EditorId, @"^LC\d+_")))
                Expand(npc.Product, 0);
            var clothes = new List<string>();
            foreach (var armo in worn.Select(w => idx.ById[w]))
            {
                var fits = (armo.Parts ?? new()).Select(p => idx.ById.GetValueOrDefault(p)).OfType<EsmRecord>().Where(x => x.Type == "ARMA" && x.Models != null).ToList();
                var mine = fits.Where(x => races.Contains(x.Link) || (x.Parts ?? new()).Any(races.Contains)).ToList();
                foreach (var m in (mine.Count > 0 ? mine : fits).SelectMany(x => x.Models!.Select(Norm)).Distinct())
                    // never the human body (hands, skin): that would light up every player and NPC
                    if (!m.StartsWith(@"actors\character\") &&
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
            var rx = new Regex(pattern, RegexOptions.IgnoreCase);
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
        foreach (var (name, models) in new (string, string[])[]
                 {
                     ("Legendary enemies", new[] { @"effects\shaderparticles\legendaryradiationfxsp.nif", @"effects\shaderparticles\legendaryradiation_b_fxsp.nif" }),
                     ("Bounty targets (Burning Springs)", new[] { @"effects\shaderparticles\burn_bountyhunt_casterparticles.nif" }),
                 })
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
            var beamRx = beam.Length > 0 ? new Regex(beam, RegexOptions.IgnoreCase) : null;
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
