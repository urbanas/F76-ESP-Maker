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
    sealed record Pick(string Name, string[] Models, string Detail = "");

    /// <summary>Name, editor-ID prefixes, words a model path must contain to count as the event's own (an ordinary
    /// box or candle the event borrows would light up the whole map), colour, description, hand-picked entries.</summary>
    static readonly (string Name, string Prefixes, string OwnModels, string Color, string Description, Pick[] Picks)[] All =
    {
        ("The Slasher (Halloween)", "^SDOW_", "slasher", "#FF7A00",
         "The Pint-Sized Slasher event. Graves to dig, the Phantoms and the Slasher himself. " +
         "Outfit entries also light up players wearing that outfit.",
         new[]
         {
             new Pick("Disturbed graves", new[] { @"landscape\gravesactivators\graveactivator01\graveactivator01.nif", @"landscape\gravesactivators\graveactivator01\graveactivator_noskeleton.nif" }),
             new Pick("Pint-Sized Phantoms (outfit)", new[] { @"atx\clothes\pint_sized_slasher\slashervictim\pintsizedslasher_slashervictim_m.nif", @"atx\clothes\pint_sized_slasher\slashervictim\pintsizedslasher_slashervictim_f.nif" }, "players in this outfit glow too"),
             new Pick("Pint-Sized Slasher (costume)", new[] { @"atx\clothes\pintsizedslasher\pintsizedslasher_m.nif", @"atx\clothes\pintsizedslasher\pintsizedslasher_f.nif" }, "players in this costume glow too"),
             new Pick("Uninvited Guest (Pale Rider armour)", new[] { @"atx\actors\powerarmor\characterassets\mods\pa_palerider_body.nif", @"atx\actors\powerarmor\characterassets\mods\pa_palerider_larm.nif", @"atx\actors\powerarmor\characterassets\mods\pa_palerider_rarm.nif", @"atx\actors\powerarmor\characterassets\mods\pa_palerider_lleg.nif", @"atx\actors\powerarmor\characterassets\mods\pa_palerider_rleg.nif" }, "players in this power armour glow too"),
             new Pick("Slasher hat", new[] { @"clothes\slasherhat\slasherhat_go.nif" }),
             // the costumes Spooky Scorched wear in the Halloween season: these heads (the set SpookyBeacons marks) plus
             // everything in the game's Creature_Spooky_* outfits and headwear (added in Build)
             new Pick(SpookyCostumes, new[]
             {
                 @"atx\clothes\pintsizedslasher\pintsizedslasher_mask_m.nif",
                 @"atx\clothes\pintsizedslasher\pintsizedslashermask_f.nif",
                 @"atx\clothes\mothman\atx_mothmanmask_2_m.nif",
                 @"atx\clothes\mothman\atx_mothmanmask_2_f.nif",
                 @"atx\clothes\atx_cardboardrobot\atx_cardboardrobot_helmet_m.nif",
                 @"atx\clothes\atx_cardboardrobot\atx_cardboardrobot_helmet_f.nif",
                 @"atx\clothes\atx_executioner\atx_executioneroutfithood_m.nif",
                 @"atx\clothes\atx_executioner\atx_executioneroutfithood_f.nif",
                 @"clothes\halloweencostumes\halloweencostume01_male_hat.nif",
                 @"clothes\halloweencostumes\halloweencostume01_female_hat.nif",
                 @"clothes\halloweencostumes\halloweencostume01_male_hat_facebones.nif",
                 @"clothes\halloweencostumes\halloweencostume01_female_hat_facebones.nif",
                 @"atx\clothes\psycho\atx_psycho_mask_m.nif",
                 @"atx\clothes\psycho\atx_psycho_mask_f.nif",
                 @"atx\clothes\rubbermonster\atx_outfit_rubbermonster_m_helmet.nif",
                 @"atx\clothes\rubbermonster\atx_outfit_rubbermonster_f_helmet.nif",
                 @"atx\clothes\halloweenpirate\atx_halloweenpirate_hat_m.nif",
                 @"atx\clothes\halloweenpirate\atx_halloweenpirate_hat_f.nif",
                 @"atx\clothes\halloweenvikingbustedhorns\atx_halloweenvikingbustedhorns_helmet_m.nif",
                 @"atx\clothes\halloweenvikingbustedhorns\atx_halloweenvikingbustedhorns_helmet_f.nif",
                 @"clothes\sheepsquatchmascot\sheepsquatchmascothatm.nif",
                 @"clothes\sheepsquatchmascot\sheepsquatchmascothatf.nif",
                 @"clothes\silvershroud\silvershroudgloves.nif",
                 @"clothes\silvershroud\silvershroudglovesf.nif",
             }, "players wearing these costumes glow too"),
         }),
        ("Mischief Night", "^(MN2_|E03A_Mischief)", @"(mischief|mn2_|halloween|batplushie|radscorpionplushie|pumkin|pumpkin)", "#B040FF",
         "Mischief Night objectives: pyres to light, candy bowls, boxes of forks, paint for graffiti, fireworks crates.",
         new[]
         {
             new Pick("Pyres & bonfires", new[] { @"setdressing\mischief_bonfire\mischief_bonfire.nif" }),
             new Pick("Candy bowls", new[] { @"atx\workshop\atx_workshop_halloweencandybowl\atx_workshop_halloweencandybowl.nif", @"atx\workshop\atx_workshop_halloweencandybowl\crania\atx_workshop_craniacandybowl.nif", @"atx\workshop\atx_workshop_halloweencandybowl\morgue\atx_workshop_candybowl_morgue.nif" }, "CAMP candy bowls light up too"),
             new Pick("Boxes of forks", new[] { @"scol\seventysix.esm\cm00573cc8.nif", @"scol\seventysix.esm\cm00573cc9.nif" }),
             new Pick("Paint buckets (graffiti)", new[] { @"props\paint\paintbucket_generic.nif", @"props\paint\paintbucket_blue.nif", @"props\paint\paintbucket_red.nif", @"props\paint\paintbucket_yellow.nif", @"props\paint\paintbucketpink_tol.nif" }, "paint buckets everywhere light up too"),
             new Pick("Fireworks crates", new[] { @"setdressing\fireworkscrate\mn2_fireworkscrate.nif" }),
             new Pick("Costumed Lost (mummy & costume)", new[] { @"atx\clothes\mummy\atx_mummy_m.nif", @"atx\clothes\mummy\atx_mummy_f.nif", @"clothes\halloweencostumes\halloweencostume02_male_body.nif", @"clothes\halloweencostumes\halloweencostume02_female_body.nif" }, "players wearing these costumes glow too"),
         }),
        // Holiday Scorched (December): Scorched in the game's Creature_Holiday_* outfits (OTFT CreatureOutfit_Festive_Scorched)
        // and the Festive Scorched Exterminator bosses (MrClausHat / SantaBeard NONPLAYABLE) drop Holiday Gifts. The gifts,
        // waterlogged gifts (fishing) and handmade gifts all share giftbox.nif; gift wrap = giftwrappingpaper_01.nif
        ("Holiday Scorched", "^(c_)?Festive_", "(?!)", "#30E070",
         "Holiday Scorched: Scorched in Santa, Mrs. Claus, elf and snowflake pant-suit outfits - they drop Holiday Gifts. " +
         "Also the gifts and gift wrap when they lie on the ground.",
         new[]
         {
             new Pick(FestiveCostumes, new[]
             {
                 @"atx\clothes\mrclaus\mrclausm.nif", @"atx\clothes\mrclaus\mrclausf.nif",
                 @"atx\clothes\mrclaus\mrclaushatm.nif", @"atx\clothes\mrclaus\mrclaushatf.nif",
                 @"atx\clothes\mrsclaus\mrsclausm.nif", @"atx\clothes\mrsclaus\mrsclausf.nif",
                 @"atx\clothes\christmaself\christmaselfm.nif", @"atx\clothes\christmaself\christmaselff.nif",
                 @"atx\clothes\christmaself\christmaselfhatm.nif", @"atx\clothes\christmaself\christmaselfhatf.nif",
                 @"atx\clothes\pantsuit\atx_pantsuit_m.nif", @"atx\clothes\pantsuit\atx_pantsuit_f.nif",
                 @"atx\clothes\santabeard\santabeardm.nif", @"atx\clothes\santabeard\santabeardfemale.nif",
             }, "Santa suits & hats, Mrs. Claus dresses, elf outfits, snowflake pant suits, Santa beards · players wearing them glow too"),
             new Pick("Holiday gifts", new[] { @"atx\setdressing\giftboxes\giftbox.nif" }, "gifts on the ground (also waterlogged and handmade ones) · the Atlantic City gift-box clue lights up too"),
             new Pick("Gift wrap", new[] { @"setdressing\giftbox\giftwrappingpaper_01.nif" }),
         }),
        ("Fasnacht", "^E01F_", @"(fasnacht|helvetia|helv_|cm0059e955)", "#40D0FF", "Fasnacht Day: bonfire, beehives, frog egg clusters and the other supply spots.", Array.Empty<Pick>()),
        ("Meat Week", "^E02A_", @"(meat_bbq|meatweek|grahm|cm0054a3a6)", "#FF4040", "Meat Week (Grahm's) cookout objects.", Array.Empty<Pick>()),
        ("Mothman Equinox", "^E07A_", @"(mothman|moth|enlightened|cultist)", "#E0E060", "Mothman Equinox ritual objects and Enlightened Mothman Eggs.", Array.Empty<Pick>()),
        ("Invaders from Beyond", "^E07B_", @"(zetan|brainwave)", "#60FF90", "Invaders from Beyond: the Zetan brainwave siphons.", Array.Empty<Pick>()),
        ("Once in a Blue Moon", "^MOON_", @"(bluemoon|moon_)", "#4080FF", "Once in a Blue Moon objectives.", Array.Empty<Pick>()),
        // the maps are bought from the machine (inventory only) - nothing else to light up
        ("Lucky Strike", "^MTRZ05", "(?!)", "#FFC040",
         "Lucky Strike: the dig sites your map leads to and the U-Mine-It! vending machines that sell the maps.",
         new[]
         {
             new Pick("Dig sites", new[] { @"setdressing\ashlootmounds\ashlootmound02.nif" }),
             new Pick("U-Mine-It! vending machines", new[] { @"setdressing\autodispenser\autodispenserumine.nif" }),
         }),
        // the raid's objectives reuse ordinary models: only these hand-picked ones (the drill is the CAMP large generator -
        // every CAMP would light up). "(?!)" = no automatic entries
        ("Gleaming Depths (raid)", "^RD01_", "(?!)", "#5CFFB8",
         "The Gleaming Depths raid: fuel canisters to carry, where they go, the kinetic charging nodes and the Ultragenetic Mole Miner Stalker.",
         new[]
         {
             new Pick("Fuel Canister", new[] { @"dlc03\setdressing\quest\fogcondensor\fogcondensorcanister01.nif" }, "Plastic Gas Canisters elsewhere light up too"),
             new Pick("Fuel Deposit", new[] { @"setdressing\coalchute\coalchute.nif" }, "CAMP fertilizer storages and ore extractors light up too"),
             new Pick("Kinetic Charging Node", new[] { @"interiors\vault\vault111\setdressing\v111generator02.nif" }),
             new Pick("Ultragenetic Mole Miner Stalker", new[] { @"actors\moleminer\characterassets\body_mesh_cultist.nif" }, "its body - cultist mole miners light up too"),
         }),
        // public events: only hand-picked objects with their own models (the rest of each event is enemies or borrowed props)
        ("Tunnel of Love", "^E09C_", "(?!)", "#FF60B0",
         "Tunnel of Love: robot parts and heart lamps to collect, the confetti cannons and Miss Lovely.",
         new[]
         {
             new Pick("Robot parts", new[] { @"setdressing\fakemisshandy\fakemisshandypartbucket.nif" }),
             new Pick("Heart lamps", new[] { @"setdressing\tunnelofloveprops\tol_loveheartcollectible.nif" }),
             new Pick("Confetti cannons", new[] { @"setdressing\tunnelofloveprops\tol_confetticannon.nif" }),
             new Pick("Miss Lovely", new[] { @"setdressing\fakemisshandy\fakemisshandy01.nif" }),
         }),
        ("Radiation Rumble", "^E05_Radiation", "(?!)", "#B0FF40",
         "Radiation Rumble: the irradiated ore veins to mine and the ore storage container to fill.",
         new[]
         {
             new Pick("Irradiated ore veins", new[] { @"landscape\plants\florairradiatedore01.nif" }),
             new Pick("Ore storage container", new[] { @"scol\seventysix.esm\cm0023e109.nif" }),
         }),
        ("Most Wanted", "^E09D_", "(?!)", "#E0A050",
         "Most Wanted (Gunther's Wild West Show): loot safes, wall safes, loot dispensers and the getaway wagon.",
         new[]
         {
             new Pick("Loot safes", new[] { @"setdressing\guntherswildwestshowprops\guntherssafe.nif" }),
             new Pick("Loot wall safes", new[] { @"setdressing\guntherswildwestshowprops\guntherswallsafe.nif" }),
             new Pick("Loot dispensers", new[] { @"setdressing\guntherswildwestshowprops\gwws_ticketmachineandlight.nif" }),
             new Pick("Getaway wagon", new[] { @"setdressing\guntherswagon\guntherswagon.nif" }, "CAMP getaway wagons light up too"),
         }),
        ("Encryptid", "^E01B_Encryptid", "(?!)", "#C080FF",
         "Encryptid: the pylons to power up.",
         new[] { new Pick("Pylons", new[] { @"props\encryptedpylon\encryptedpylon_computer.nif" }) }),
        ("Uranium Fever", "^MTNS06", "(?!)", "#80FF60",
         "Uranium Fever: the uranium extractor to defend.",
         new[] { new Pick("Uranium extractor", new[] { @"setdressing\indmachines\custom\indmachines_extractor01\indmachines_extractor02.nif" }) }),
        ("Dangerous Pastimes", "^Storm_E01", "(?!)", "#60C0FF",
         "Skyline Valley: the harvester parts to collect.",
         new[] { new Pick("Harvester parts", new[] { @"dlc01\setdressing\machinegreebles\mechanical_metal09.nif" }) }),
        ("Eviction Notice", "^E08B_", "(?!)", "#C06030",
         "Eviction Notice: the meat bags to destroy.",
         new[] { new Pick("Meat bags", new[] { @"props\supermutant\meatbag_01.nif", @"props\supermutant\meatbag_floor01.nif" }, "meat bags in super mutant camps light up too") }),
        ("Campfire Tales", "^E01C_", "(?!)", "#FF9040",
         "Campfire Tales: dry kindling to collect (wet kindling doesn't count), the campfire and the items the tales ask for.",
         new[]
         {
             new Pick("Dry kindling", new[] { @"setdressing\campfiretalestwigs\campfiretalestwig_06.nif" }),
             new Pick("Wet kindling", new[] { @"setdressing\campfiretalestwigs\campfiretalestwig_05.nif" }, "the one to leave"),
             new Pick("Campfire", new[] { @"props\campfiretalescampfire\campfiretales_campfire.nif" }),
             new Pick("Tale items (pet rock, lucky charm, telescope)", new[] { @"scol\seventysix.esm\cm004215f6.nif", @"setdressing\sheepsquatchhorn\sheepsquatchhorn_small.nif", @"dlc04\setdressing\telescope\dlc04_telescope.nif" }),
         }),
        ("Distinguished Guests", "^MTNM04", "(?!)", "#FFE080",
         "Distinguished Guests (Bolton Greens): place settings and centerpieces to put on the tables.",
         new[]
         {
             new Pick("Place settings", new[] { @"props\platesetting\mtnm04_platesetting_messy.nif" }),
             new Pick("Centerpieces", new[] { @"setdressing\pumpkins\pumpkincenterpiece01.nif" }),
         }),
        // expedition objectives share most of their models with the casino floor or the rest of the game: hand-picked, and
        // the detail says what else lights up. The Overgrown Moonflower wears only the shared dummy effect skin - not here
        ("Atlantic City expeditions", "^XPD_AC", "(?!)", "#FFD24A",
         "Atlantic City expeditions (Tax Evasion, The Most Sensational Game, The Human Condition): listening bugs, slot machines, " +
         "tickets, supplies, secret teddy bears and the other objectives.",
         new[]
         {
             new Pick("Listening bugs", new[] { @"setdressing\powerbox\powerbox_activator.nif" }, "Tax Evasion · a few power boxes elsewhere light up too"),
             new Pick("Slot machines", new[] { @"setdressing\ac_slotmachines\ac_slotmachine01.nif", @"setdressing\ac_slotmachines\ac_slotmachine02.nif" }, "Tax Evasion · every casino slot machine lights up, the game has no separate objective model"),
             new Pick("Cap stash drop-off", new[] { @"atx\setdressing\atx_stashboxcontainers\atx_stashboxcontainer_ornate.nif" }, "Tax Evasion"),
             new Pick("Terminals", new[] { @"furniture\terminals\terminalon.nif", @"furniture\terminals\terminalconsoleontall.nif" }, "every terminal in the game lights up too, not only the objective ones"),
             new Pick("Tickets", new[] { @"dlc04\setdressing\games\dlc04_nukacade_ticketstack01.nif" }, "The Most Sensational Game"),
             new Pick("Tato baskets", new[] { @"props\consumables\pitt\pitt_tatobasket_01.nif" }, "The Most Sensational Game"),
             new Pick("Confetti cannons", new[] { @"setdressing\tunnelofloveprops\tol_confetticannon.nif" }, "The Most Sensational Game · Tunnel of Love's cannons too"),
             new Pick("Secret teddy bears", new[] { @"setdressing\teddybear\toyteddybeardirty01_babyteddy.nif", @"setdressing\teddybear\toyteddybeardirty01_red.nif", @"setdressing\teddybear\toyteddybeardirty01_demon.nif", @"setdressing\teddybear\toyteddybeardirty01_polar.nif", @"setdressing\teddybear\toyteddybeardirty01_blue.nif" }, "The Most Sensational Game · these teddy bears elsewhere light up too"),
             new Pick("Muni Supplies", new[] { @"scol\seventysix.esm\cm007598ce.nif" }, "The Human Condition"),
             new Pick("Muni security cameras", new[] { @"scol\seventysix.esm\cm003609a6.nif" }, "The Human Condition"),
             new Pick("Medical Supplies", new[] { @"setdressing\firstaidkit\firstaidkitvandalized.nif" }, "The Human Condition · first aid kits elsewhere light up too"),
             new Pick("Lost Dossiers", new[] { @"setdressing\office\folderpapers02.nif" }, "The Human Condition · these folders elsewhere light up too"),
             new Pick("City Hall metal detectors", new[] { @"setdressing\xpd_ac\xpd_ac_cityhall\xpd_ac_cityhall_metaldetector.nif" }),
         }),
        // the Pitt's own crates, chems and dead drops come from the records (models under a pitt folder); the rest is
        // hand-picked. Left out: drop boxes (every trash can), backpacks, toolboxes, lunchboxes, PA systems (markers)
        ("The Pitt expeditions", "^XPD_?(Pitt|RTTP)", "pitt|uniondues", "#FF9A3C",
         "The Pitt expeditions: Union supply crates, chemicals to gather, coolant canisters, dead drops, deposit points, " +
         "prison doors, trench masks and meds.",
         new[]
         {
             new Pick("Coolant canisters / Troglocide", new[] { @"props\gascanister02.nif" }, "gas canisters elsewhere light up too"),
             new Pick("Coolant canister stockpiles", new[] { @"atx\setdressing\atx_freezer\atx_freezer_nukacola.nif" }, "CAMP Nuka-Cola freezers light up too"),
             new Pick("Chemical weapons stockpiles", new[] { @"setdressing\machinekit\custom\console\industrialmachineconsole01activator.nif" }, "these consoles elsewhere light up too"),
             new Pick("Distribution stations", new[] { @"setdressing\graftonsteelmachines\graftonregulator01\graftonregulatoractivator01.nif" }, "these machines elsewhere light up too"),
             new Pick("Testing vats", new[] { @"interiors\industrial\smpipe\indsmpipe1way01activator.nif" }, "these pipe valves elsewhere light up too"),
             new Pick("Prison doors", new[] { @"interiors\jail\jaildoorswing01.nif", @"setdressing\doors\switchdoors\switchdoorexsmlatch01\switchdoorexsmlatch01.nif", @"setdressing\doors\switchdoors\switchdoorexsmlatch01\switchdoorexsmlatch02.nif" }, "these doors elsewhere light up too"),
             new Pick("Trench masks", new[] { @"armor\gasmask\gasmaskgo.nif" }, "gas masks lying elsewhere light up too"),
             new Pick("Trench meds", new[] { @"props\radx.nif" }, "every Rad-X lights up too"),
             new Pick("Fallen prisoner's collars", new[] { @"dlc04\clothes\slavecollar\slavecollar_go.nif" }),
             new Pick("Steel ingots", new[] { @"props\steelingotstack\steel_ingot_stack.nif" }),
             new Pick("Hazards (trench mounds)", new[] { @"props\escapefrompurgatory_trenchmound\escapefrompurgatory_trenchmound_sign.nif" }),
         }),
    };

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
        AddEnemyCopies(cats);
        PartNames.NameAll(idx, cats);
        cats.AddRange(Catalog.Rest(idx, exists, cats));
        return cats;
    }

    /// <summary>The events section's sub-heading for an event.</summary>
    static string EventGroup(string name) =>
        name.Contains("(raid)") || name.Contains("expeditions") ? "Raids & expeditions"
        : name is "The Slasher (Halloween)" or "Mischief Night" or "Fasnacht" or "Meat Week" or "Mothman Equinox"
            or "Invaders from Beyond" or "Lucky Strike" or "Holiday Scorched" ? "Seasonal"
        : "Public events";

    public static List<Category> Build(GameIndex idx, Func<string, bool> exists)
    {
        var world = idx.Records.Where(r => GameIndex.WorldTypes.Contains(r.Type)).ToList();
        var byModel = world.GroupBy(r => Catalog.NormalizeModel(r.Model)).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<Category>();
        var spots = idx.WorldSpots();
        foreach (var (name, prefixes, ownModels, color, description, picks) in All)
        {
            var c = new Category
            {
                Name = name, Description = description, Section = "EVENTS", Group = EventGroup(name),
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
            var rx = new Regex(prefixes, RegexOptions.IgnoreCase);
            var own = new Regex(ownModels, RegexOptions.IgnoreCase);
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
        return result;
    }

    /// <summary>The pick for the costumes Spooky Scorched wear; built as two entries, heads and bodies.</summary>
    public const string SpookyCostumes = "Spooky Scorched (costumes)", SpookyHeads = "Spooky Scorched (costume heads)",
                        SpookyBodies = "Spooky Scorched (costume bodies)", FestiveCostumes = "Festive Scorched (costumes)";
    // the Silver Shroud costume has no head piece: its gloves carry the beam and icon, as in Spooky Beacons
    static readonly Regex HeadPiece = new(@"(hat|helmet|mask|hood|headgear|headwear|facebones|silvershroudgloves)", RegexOptions.IgnoreCase);

    /// <summary>Event entries that are enemies (outfits the event's enemies wear) - also listed under ENEMIES.</summary>
    static readonly HashSet<string> EnemyPicks = new()
    {
        "Pint-Sized Slasher (costume)", "Pint-Sized Phantoms (outfit)", "Uninvited Guest (Pale Rider armour)", SpookyHeads, SpookyBodies,
        "Costumed Lost (mummy & costume)", "Ultragenetic Mole Miner Stalker", FestiveCostumes,
    };

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

    /// <summary>More than half of the named things using the model are event records.</summary>
    static bool MostlyEvent(List<EsmRecord> users, Regex rx) => users.Count(r => rx.IsMatch(r.EditorId)) * 2 > users.Count;
}
