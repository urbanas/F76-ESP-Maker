using System.Globalization;

namespace ItemFinder;

/// <param name="TextureArchive">The second archive with the game textures this mod replaces (null: none) - the game
/// only takes replacements of its own textures from a texture archive.</param>
/// <param name="Brightened">Game materials this mod replaces with brighter ones.</param>
public sealed record BuildResult(string Archive, int Models, int Failed, List<string> Conflicts, string? TextureArchive = null, int Brightened = 0);

/// <summary>Turns the ticked entries into one general BA2: patched models + the colour materials they use.</summary>
public static class Builder
{
    public static BuildResult Build(Project project, List<Category> cats, GameData game, string outFile, Action<string> log)
    {
        project = Json.Clone(project); // the UI stays usable while building; never read a set that is being changed
        var tplEntry = game.Find(Materials.Template) ?? throw new FileNotFoundException("The game's AmbDefaultFX.bgem was not found");
        var template = tplEntry.Archive.ReadFile(tplEntry);

        var files = new List<(string Path, byte[] Data)>();
        var replaced = new List<TextureOut>(); // the game's own textures, replaced
        var materials = new Dictionary<string, byte[]>();
        var done = new Dictionary<string, string>(); // model -> entry name that claimed it
        var conflicts = new List<string>();
        var cutouts = new Dictionary<string, Cut>(); // game material -> how its glow must be made
        var masks = new Dictionary<string, bool[,]>(); // 3D icon shapes
        var brightened = new Dictionary<string, byte[]>(); // game material -> its brighter copy
        var labels = new Dictionary<string, float>(); // label texture -> its width / height
        int failed = 0;

        // when the same model is ticked twice, an entry with its own colours wins, then junk under its main component and
        // shared robot parts under their robot, not the boss; otherwise category order
        var work = cats.SelectMany(c => c.Entries.Where(e => project.Selected.Contains(e.Id)).Select(e => (Cat: c, Entry: e)))
                       .OrderBy(x => project.Overrides.ContainsKey(x.Entry.Id) || project.PartLooks.ContainsKey(x.Entry.Id) ? 0 : 1)
                       // dedicated categories before the catch-all lists that happen to share a model (Clean Surgical Tools is
                       // the paper bag: its white junk look won and the loot bags never showed their own - user, 2026-11-10)
                       .ThenBy(x => CatchAll(x.Cat) ? 1 : 0)
                       .ThenBy(x => x.Entry.JunkCopy || x.Entry.LinkParts && x.Cat.Name == Enemies.Bosses ? 1 : 0).ToList();
        foreach (var (cat, e) in work)
            {
                var st = project.StyleOf(e, cat);
                var partLooks = project.PartLooks.GetValueOrDefault(e.Id);
                if (!Shows(st) && partLooks is not { Count: > 0 })
                {
                    // "show nothing" still claims the models, so a linked twin in another category (e.g. the same
                    // bobblehead under Aid & food) doesn't light them up in its own colours
                    foreach (var m in e.Models) done.TryAdd(@"meshes\" + m, $"{e.Name} in {cat.Name} (set to show nothing)");
                    log($"  {e.Name}: glow and beam are both off - left as in the game");
                    continue;
                }
                // what a look puts on a model (the entry's look, or a part's own)
                PatchOptions Options(Style s, bool main)
                {
                    // the arrow is a solid shape: the 3D-icon material (shaded by its vertex colours), in the beam colour
                    string? beamMat = !s.Beam ? null : s.BeamArrow ? Icon3DMaterial(s, template, materials) : BeamMaterial(s, template, materials, files);
                    float beamLength = s.BeamArrow ? NifPatch.ArrowSize(s.BeamWidth) is var a ? a.Gap + a.Height : 0 : s.BeamLength; // icons go on top
                    // animated objects (first aid kits, wall safes, ammo boxes: skinned, not creatures) showed no outline in
                    // game, only a fill (2026-11-05) - they get the fill whatever the look says
                    Style? filled = s.GlowFill || e.NoIcon ? null : s.Clone();
                    if (filled != null) filled.GlowFill = true;
                    Func<GlowPart, GlowMat?>? glowFor = s.Glow ? part => GlowFor(part.Skinned && filled != null ? filled : s, part, game, template, materials, cutouts, files) : null;
                    var chosen = Icons.Get(s.Icon);
                    bool use3D = e.NoIcon || s.Icon3D; // creatures can only show the 3D shape
                    string? iconMat = main && !use3D && chosen != null ? IconMaterial(s, chosen, template, materials, files) : null;
                    // the solid version always comes along: a flat icon on an animated model (fish, safes with lids,
                    // flairs) would be dropped by the game, so NifPatch uses the solid one there
                    bool[,]? mask = main && chosen != null ? (masks.TryGetValue(chosen.Id, out var mk) ? mk : masks[chosen.Id] = Icons.Mask(chosen)) : null;
                    string? icon3dMat = mask != null ? Icon3DMaterial(s, template, materials) : null;
                    var model3d = main ? Models3D.Get(s.Icon) : null;
                    string? modelMat = model3d != null ? ModelMaterial(s, model3d, template, materials, files) : null;
                    string? labelMat = null;
                    float labelRatio = 0;
                    if (main && s.Label && !e.NoIcon)
                    {
                        string text = s.LabelText.Trim().Length > 0 ? s.LabelText.Trim() : Icons.LabelText(e.Name);
                        if (s.LabelCategory) text = Icons.LabelText(cat.Name) + " · " + text;
                        (labelMat, labelRatio) = LabelMaterial(s, text, template, materials, files, labels);
                    }
                    static PartMotion? M(PartFx x, bool flow = false) => x.Moves || x.HideAfter > 0
                        ? new PartMotion(x.Pulse, x.Rainbow, x.Spin, x.Bob, flow && x.Flow, x.HideAfter, x.HideAfter > 0 ? x.BlinkOff : 0) : null;
                    var motion = s.Moves || s.Timed ? new Motion(M(s.GlowFx), M(s.BeamFx, !s.BeamArrow), M(s.IconFx), M(s.LabelFx)) : null;
                    return new PatchOptions(glowFor, main ? beamMat : null, beamLength, s.BeamWidth, e.Harvestable, iconMat, s.IconSize, mask, icon3dMat,
                                            s.BeamArrow, model3d != null ? Models3D.MeshOf(model3d) : null, modelMat,
                                            labelMat, s.LabelSize * labelRatio, s.LabelSize, s.Invisible && e.CanHide,
                                            motion, p => materials.TryGetValue(p, out var mb) ? Materials.BaseScale(mb) : 1f);
                }
                // invisible enemies: their shimmer's gradient in the glow colour (a game texture: into the texture archive)
                if (e.Gradients != null && st.Glow)
                    foreach (var tex in e.Gradients.Where(t => !replaced.Any(r => r.Path == t)))
                    {
                        var img = Gradient(game, tex, st);
                        byte fmt = game.Find(tex) is { } orig && Dxgi.IsSrgb(orig.Format) ? Dxgi.B8G8R8A8Srgb : Dxgi.B8G8R8A8;
                        replaced.Add(new TextureOut(tex, img.Width, img.Height, fmt, Dxgi.MipsBgra(img)));
                        log($"  {e.Name}: {tex} recoloured");
                    }
                var models = project.ModelsOf(e);
                // the beam / icon go on the main model - or the first part left when that one was unticked
                var beamModels = e.BeamModels?.Where(models.Contains).ToHashSet();
                if (beamModels is { Count: 0 } && models.Count > 0) beamModels.Add(models[0]);
                foreach (var model in models)
                {
                    // a part with its own look carries its own beam / icon
                    var own = partLooks?.GetValueOrDefault(model);
                    var look = own ?? st;
                    var path = @"meshes\" + model;
                    if (look.Brighten) Brighten(e.Name, path, look, game, brightened, log);
                    if (!ChangesModel(look))
                    {
                        // brighten only: a linked twin may still put its own glow on the model
                        if (!look.Brighten) done.TryAdd(path, $"{e.Name} in {cat.Name} (set to show nothing)");
                        continue;
                    }
                    bool mainModel = own != null || beamModels == null || beamModels.Contains(model);
                    var opts = Options(look, mainModel);
                    if (done.TryGetValue(path, out var by)) { log($"  {e.Name}: {model} is also selected as {by} - that look is used"); continue; }
                    // a hidden worn outfit: the bare body / hands go in its place (the outfit model is the body under it too)
                    var srcPath = path;
                    if (e.HideAs(model, look) is string bare)
                    {
                        opts = opts with { Invisible = false };
                        if (bare.Length > 0) { srcPath = @"meshes\" + bare; log($"  {e.Name}: {model} - shown as {bare}"); }
                        else log($"  {e.Name}: {model} - kept visible (covers hair / head, which can't be shown instead)");
                    }
                    var winner = game.Find(srcPath);
                    var src = project.KeepOtherMods ? winner : game.FindOriginal(srcPath) ?? winner;
                    if (src == null) { log($"  {e.Name}: {model} not found in the game files - skipped"); failed++; continue; }
                    try
                    {
                        PatchResult r;
                        bool fellBack = false;
                        try { r = NifPatch.Apply(src.Archive.ReadFile(src), opts); }
                        catch (Exception ex) when (game.FindOriginal(srcPath) is { } orig && orig != src)
                        {
                            // the other mod's file is gone (removed while the app was open) or in a form this app can't change
                            log($"  {e.Name}: couldn't use {model} from {src.Archive.FileName} ({ex.Message}) - using the original game model");
                            r = NifPatch.Apply(orig.Archive.ReadFile(orig), opts);
                            fellBack = true;
                        }
                        if (winner != null && !winner.Archive.FileName.StartsWith("SeventySix - ", StringComparison.OrdinalIgnoreCase))
                            conflicts.Add(project.KeepOtherMods && !fellBack
                                ? $"{e.Name}: {model} is also changed by {winner.Archive.FileName} - kept its changes and added the highlight on top"
                                : fellBack
                                ? $"{e.Name}: {model} is also changed by {winner.Archive.FileName} - its version couldn't be read, so this starts from the original game model and replaces it"
                                : $"{e.Name}: {model} is also changed by {winner.Archive.FileName} - started from the original game model instead, so this replaces that mod's version");
                        files.Add((path, r.Data));
                        done[path] = $"{e.Name} in {cat.Name}";
                        var parts = new List<string>();
                        if (opts.GlowFor != null) parts.Add(r.GlowShapes > 0 ? $"glow on {r.GlowShapes} part(s)" : "no parts to glow");
                        if (r.Beam) parts.Add("beam");
                        if (r.Note.Length > 0) parts.Add(r.Note);
                        log($"  {e.Name}: {model} - {string.Join(", ", parts)}{(own != null ? " (own look)" : "")}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        log($"  {e.Name}: {model} could not be changed ({ex.Message}) - skipped");
                    }
                }
            }
        if (files.Count == 0 && replaced.Count == 0 && brightened.Count == 0) throw new InvalidOperationException("Nothing to build - select at least one item that has glow or beam on.");
        files.AddRange(materials.Select(kv => (kv.Key, kv.Value)));
        files.AddRange(brightened.Select(kv => (kv.Key, kv.Value)));
        files.Add((Importer.SetupPath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(SavedSetup(project, cats), Json.Options))); // for Import
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        Ba2Writer.WriteGeneral(outFile, files);
        var texFile = TextureArchive(outFile);
        if (replaced.Count > 0) Ba2Writer.WriteTextures(texFile, replaced);
        else if (File.Exists(texFile)) File.Delete(texFile); // an earlier build's: no longer part of this one
        return new BuildResult(outFile, files.Count(f => f.Path.EndsWith(".nif")), failed, conflicts, replaced.Count > 0 ? texFile : null, brightened.Count);
    }

    /// <summary>The setup stored in the archive: only what the ticked items use - looks, parts and category looks of
    /// unticked items stay out (bug report 2026-11-07: they showed up in setup.json).</summary>
    /// <summary>A list of everything of a kind (junk, aid, CAMP items, plants, outfits, Everything else): on a shared
    /// model it gives way to a category made for that thing (plans, loot bags, loot spots, events, enemies...).</summary>
    public static bool CatchAll(Category c) =>
        c.Section is "JUNK" or Catalog.AllSection || c.Name is "Junk & misc" or "Aid & food" or "Plants (all)" or Catalog.CampItemsName
        || c.Name.StartsWith("All outfits", StringComparison.Ordinal);

    static Project SavedSetup(Project project, List<Category> cats)
    {
        // only what this mod is built from: looks of unselected items, parts left out and categories none of the
        // selected items take their look from stay out (user report, 2026-11-09)
        var p = Json.Clone(project);
        var known = cats.SelectMany(c => c.Entries).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        p.Selected = p.Selected.Where(known.ContainsKey).ToHashSet();
        bool On(string id) => p.Selected.Contains(id);
        p.Overrides = p.Overrides.Where(kv => On(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        p.OffParts = p.OffParts.Where(kv => On(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        p.PartLooks = p.PartLooks.Where(kv => On(kv.Key))
                       .Select(kv => (kv.Key, Parts: kv.Value.Where(m => p.ModelsOf(known[kv.Key]).Contains(m.Key)).ToDictionary(m => m.Key, m => m.Value)))
                       .Where(x => x.Parts.Count > 0).ToDictionary(x => x.Key, x => x.Parts);
        p.RecipeLooks = p.RecipeLooks.Where(On).ToHashSet();
        var usedCats = cats.Where(c => c.Entries.Any(e => On(e.Id) && !p.Overrides.ContainsKey(e.Id))).Select(c => c.Name).ToHashSet();
        p.CategoryStyles = p.CategoryStyles.Where(kv => usedCats.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        return p;
    }

    /// <summary>Name label: its text picture (once per text) and a material per text and colour, like the icon's.</summary>
    static (string Material, float Ratio) LabelMaterial(Style st, string text, byte[] template, Dictionary<string, byte[]> mats,
                                                         List<(string Path, byte[] Data)> files, Dictionary<string, float> ratios)
    {
        string slug = new string(text.ToLowerInvariant().Select(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '_').ToArray());
        if (slug.Length > 24) slug = slug[..24];
        string key = $"{slug}_{Ba2Hash.Crc(text):x8}";
        var tex = Icons.LabelTexture(key);
        if (!ratios.TryGetValue(tex, out float ratio))
            using (var bmp = Icons.DrawLabel(text))
            {
                files.Add((tex, Icons.Dds(bmp)));
                ratios[tex] = ratio = bmp.Width / (float)bmp.Height;
            }
        var c = Materials.Parse(st.Beam ? st.BeamColor : st.GlowColor);
        var path = $@"materials\itemfinder\label_{key}_{Key(Materials.Hex(c))}{(st.LabelFx.Walls ? "_x" : "")}.bgem";
        if (!mats.ContainsKey(path)) mats[path] = Materials.Icon(template, c, st.LabelFx.Walls, Icons.LabelMaterialTexture(key));
        return (path, ratio);
    }

    static bool ChangesModel(Style s) => s.Glow || s.Beam || s.Label || s.Invisible || Icons.Get(s.Icon) != null || Models3D.Get(s.Icon) != null;
    static bool Shows(Style s) => ChangesModel(s) || s.Brighten;

    /// <summary>Brighter copies of the game materials a model uses, under the game's own paths. Always from the game's
    /// original: another mod's copy can be an older format (Oka's Plant Glow is v20) and already changed.</summary>
    static void Brighten(string item, string path, Style st, GameData game, Dictionary<string, byte[]> brightened, Action<string> log)
    {
        var src = game.FindOriginal(path) ?? game.Find(path);
        if (src == null) return;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { NifPatch.Apply(src.Archive.ReadFile(src), new PatchOptions(p => { used.Add(p.Material); return null; }, null, 0, 0, false)); }
        catch (Exception ex) { log($"  {item}: couldn't read {path} ({ex.Message}) - not brightened"); return; }
        foreach (var material in used)
        {
            int at = material.IndexOf(@"materials\", StringComparison.OrdinalIgnoreCase);
            var key = Ba2Path.Normalize(at >= 0 ? material[at..] : @"materials\" + material);
            if (!key.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase) || brightened.ContainsKey(key)) continue;
            var entry = game.FindOriginal(key);
            if ((entry != null ? Materials.Brighten(entry.Archive.ReadFile(entry), st.BrightenStrength / 100f) : null) is byte[] bright)
            {
                brightened[key] = bright;
                log($"  {item}: {key} brightened");
            }
            else log($"  {item}: {key} can't be brightened");
        }
    }

    /// <summary>Models a build would change and roughly how big the .ba2 gets: each model's compressed size in the
    /// game files, x1.85 with glow (the glow copies the lit parts - measured on a 1,300-model build), x1.05 without.</summary>
    public static (int Models, long Bytes) Estimate(Project project, List<Category> cats, GameData game)
    {
        var seen = new HashSet<string>();
        long bytes = 0;
        foreach (var c in cats)
            foreach (var e in c.Entries.Where(e => project.Selected.Contains(e.Id)).OrderBy(e => project.Overrides.ContainsKey(e.Id) ? 0 : 1))
            {
                var st = project.StyleOf(e, c);
                if (!Shows(st) && !project.PartLooks.ContainsKey(e.Id)) continue;
                foreach (var m in project.ModelsOf(e))
                {
                    var path = @"meshes\" + m;
                    if (!seen.Add(path)) continue;
                    var src = project.KeepOtherMods ? game.Find(path) : game.FindOriginal(path) ?? game.Find(path);
                    if (src != null && ChangesModel(st)) bytes += (long)((src.Packed != 0 ? src.Packed : src.Size) * (st.Glow ? 1.85 : 1.05));
                }
            }
        return (seen.Count, bytes);
    }

    static string Key(string hex) => hex.Trim().TrimStart('#').ToLowerInvariant();

    /// <summary>Solid: plain glow. Cutout: glow with the texture's cut-out. Unknown: a cut-out part whose
    /// material can't be read - no glow rather than a glowing rectangle.</summary>
    sealed record Cut(bool Solid, string Texture = "", byte Ref = 0);

    static GlowMat? GlowFor(Style st, GlowPart part, GameData game, byte[] template,
                           Dictionary<string, byte[]> mats, Dictionary<string, Cut> cache, List<(string Path, byte[] Data)> files)
    {
        var method = st.GlowMethod.Length > 0 ? st.GlowMethod : part.Cutout && !part.Skinned ? "A" : "B";
        if (method is "B" or "C")
        {
            var vc = Materials.Parse(st.GlowColor);
            var vpath = $@"materials\itemfinder\glow{method.ToLowerInvariant()}_{Key(Materials.Hex(vc))}_{st.GlowOpacity}{(st.GlowFx.Walls ? "_x" : "")}{(st.GlowFill ? "" : "_o")}.bgem";
            if (!mats.ContainsKey(vpath)) mats[vpath] = Materials.GlowVariant(template, vc, st.GlowOpacity, st.GlowFx.Walls, method == "B", !st.GlowFill);
            // no blending block at all: keeping a cut-out part's own block hid the glow (its alpha test fails on the
            // see-through colour) - so animated cut-out parts glow whole
            return new GlowMat(vpath, -1, 1);
        }
        var material = part.Material;
        int at = material.IndexOf(@"materials\", StringComparison.OrdinalIgnoreCase);
        string key = at >= 0 ? material[at..] : material.Length > 0 ? @"materials\" + material : "";
        string cacheKey = $"{key}|{part.Cutout}|{part.InlineTexture}|{part.Threshold}";
        if (!cache.TryGetValue(cacheKey, out var cut))
        {
            // a cut-out part without a usable material file: the game draws it with the texture written in the model
            var inline = part.Cutout && part.InlineTexture.Length > 0
                ? new Cut(false, StripTextures(part.InlineTexture), part.Threshold)
                : part.Cutout ? new Cut(false) : new Cut(true);
            try
            {
                var entry = key.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase) ? game.Find(key) : null;
                var info = entry != null ? Materials.Cutout(entry.Archive.ReadFile(entry)) : null;
                cut = info is { } i ? new Cut(false, i.Texture, i.Ref) : entry == null ? inline : new Cut(true);
            }
            catch { cut = inline; }
            cache[cacheKey] = cut;
        }
        if (cut.Solid) return new GlowMat(GlowMaterial(st, template, mats));
        if (cut.Texture.Length == 0) return null;
        var c = Materials.Parse(st.GlowColor);
        var path = $@"materials\itemfinder\glowa_{Key(Materials.Hex(c))}_{st.GlowOpacity}{(st.GlowFx.Walls ? "_x" : "")}{(st.GlowFill ? "" : "_o")}_cut{Ba2Hash.Crc(cut.Texture.ToLowerInvariant()):x8}.bgem";
        if (!mats.ContainsKey(path))
        {
            // the part's own texture would tint the glow (dark red berries glowed almost black, green leaves bright):
            // a white copy keeping only its see-through parts gives the same cut-out in one even colour
            var mask = CutMask(game, cut.Texture, files);
            mats[path] = Materials.CutoutGlow(template, c, st.GlowOpacity, st.GlowFx.Walls, mask ?? cut.Texture, cut.Ref, !st.GlowFill, white: mask != null);
        }
        return new GlowMat(path, mats[path][0x29]);
    }

    /// <summary>A cut-out texture as white with its own transparency (at most 256 px, like the game's lower mips), written
    /// once as textures\itemfinder\cut\[hash].dds; returns the path for the material, or null if the texture can't be read.</summary>
    static string? CutMask(GameData game, string texture, List<(string Path, byte[] Data)> files)
    {
        string name = $"{Ba2Hash.Crc(texture.ToLowerInvariant()):x8}";
        string file = $@"textures\itemfinder\cut\{name}.dds", forMaterial = $"itemfinder/cut/{name}.dds";
        if (files.Any(f => f.Path == file)) return forMaterial;
        var src = ModelPreview.LoadTexture(game, texture);
        if (src == null) return null;
        int k = 1;
        while (src.Width / k > 256 || src.Height / k > 256) k *= 2;
        int w = Math.Max(1, src.Width / k), h = Math.Max(1, src.Height / k);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // the most opaque of the k x k pixels, so thin cut-out edges (stems, grass) don't vanish
                int a = 0;
                for (int dy = 0; dy < k; dy++)
                    for (int dx = 0; dx < k; dx++)
                        a = Math.Max(a, src.Pixels[((y * k + dy) * src.Width + x * k + dx) * 4 + 3]);
                int i = (y * w + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = 255;
                px[i + 3] = (byte)a;
            }
        files.Add((file, DdsFile.WriteBgra(new Rgba(w, h, px))));
        return forMaterial;
    }

    /// <summary>Texture paths in models may start with "textures\"; material files use paths inside textures\.</summary>
    static string StripTextures(string p)
    {
        p = p.Replace('\\', '/');
        int at = p.IndexOf("textures/", StringComparison.OrdinalIgnoreCase);
        return at >= 0 ? p[(at + 9)..] : p;
    }

    static string GlowMaterial(Style st, byte[] template, Dictionary<string, byte[]> mats)
    {
        var c = Materials.Parse(st.GlowColor);
        var path = $@"materials\itemfinder\glowa_{Key(Materials.Hex(c))}_{st.GlowOpacity.ToString(CultureInfo.InvariantCulture)}{(st.GlowFx.Walls ? "_x" : "")}.bgem";
        if (!mats.ContainsKey(path)) mats[path] = Materials.Glow(template, c, st.GlowOpacity, st.GlowFx.Walls);
        return path;
    }

    /// <summary>Icon texture (once per icon) and a material per icon and colour (beam colour, or glow colour
    /// when there is no beam).</summary>
    static string IconMaterial(Style st, Icons.Icon icon, byte[] template, Dictionary<string, byte[]> mats, List<(string Path, byte[] Data)> files)
    {
        var tex = Icons.TexturePath(icon.Id);
        if (!files.Any(f => f.Path == tex))
            using (var bmp = Icons.Draw(icon)) files.Add((tex, Icons.Dds(bmp)));
        var c = Materials.Parse(st.Beam ? st.BeamColor : st.GlowColor);
        var path = $@"materials\itemfinder\icon_{icon.Id}_{Key(Materials.Hex(c))}{(st.IconFx.Walls ? "_x" : "")}.bgem";
        if (!mats.ContainsKey(path)) mats[path] = Materials.Icon(template, c, st.IconFx.Walls, Icons.MaterialTexture(icon.Id));
        return path;
    }

    /// <summary>A 3D model's texture (once per model) and a material per model and colour; models in their own colours
    /// get white (one material per model).</summary>
    static string ModelMaterial(Style st, Model3D model, byte[] template, Dictionary<string, byte[]> mats, List<(string Path, byte[] Data)> files)
    {
        var tex = Models3D.TexturePath(model);
        if (!files.Any(f => f.Path == tex)) files.Add((tex, Models3D.Dds(model)));
        var c = model.Tinted ? Materials.Parse(st.Beam ? st.BeamColor : st.GlowColor) : Color.White;
        string id = model.Id[Models3D.Prefix.Length..];
        var path = $@"materials\itemfinder\model_{id}{(model.Tinted ? "_" + Key(Materials.Hex(c)) : "")}{(st.IconFx.Walls ? "_x" : "")}.bgem";
        if (!mats.ContainsKey(path)) mats[path] = Materials.Model(template, c, st.IconFx.Walls, Models3D.MaterialTexture(model));
        return path;
    }

    /// <summary>The texture archive next to a mod archive: "ItemFinder.ba2" -> "ItemFinder - Textures.ba2".</summary>
    public static string TextureArchive(string modArchive) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modArchive))!, Path.GetFileNameWithoutExtension(modArchive) + " - Textures.ba2");

    /// <summary>An effect gradient (32 x 32: see-through at one end) in the glow colour: the original's brightness steps
    /// kept a little, its transparency exactly the original's (raising it made the shimmer vanish in game - the effect
    /// reads it as more than opacity).</summary>
    internal static Rgba Gradient(GameData game, string path, Style st)
    {
        var src = ModelPreview.LoadTexture(game, path);
        var c = Materials.Parse(st.GlowColor);
        int w = src?.Width ?? 32, h = src?.Height ?? 32;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                float a0 = src != null ? src.Pixels[i + 3] : 255f * x / Math.Max(1, w - 1);
                float lum = src != null ? (0.3f * src.Pixels[i] + 0.59f * src.Pixels[i + 1] + 0.11f * src.Pixels[i + 2]) / 255f : 1f;
                float k = 0.75f + 0.25f * lum;
                px[i] = (byte)(c.R * k); px[i + 1] = (byte)(c.G * k); px[i + 2] = (byte)(c.B * k);
                px[i + 3] = (byte)a0;
            }
        return new Rgba(w, h, px);
    }

    static string Icon3DMaterial(Style st, byte[] template, Dictionary<string, byte[]> mats)
    {
        var c = Materials.Parse(st.Beam ? st.BeamColor : st.GlowColor);
        var path = $@"materials\itemfinder\icon3d_{Key(Materials.Hex(c))}{(st.IconFx.Walls ? "_x" : "")}.bgem";
        if (!mats.ContainsKey(path)) mats[path] = Materials.Icon3D(template, c, st.IconFx.Walls);
        return path;
    }

    const string FlowTexture = @"textures\itemfinder\beamflow.dds";

    static string BeamMaterial(Style st, byte[] template, Dictionary<string, byte[]> mats, List<(string Path, byte[] Data)> files)
    {
        var c = Materials.Parse(st.BeamColor);
        // flowing beam: one band per 2 m of beam
        int tiles = st.BeamFx.Flow && !st.BeamArrow ? Math.Max(1, (int)Math.Round(st.BeamLength / 140f)) : 0;
        var path = $@"materials\itemfinder\beam_{Key(Materials.Hex(c))}_{st.BeamStrength}{(st.BeamFx.Walls ? "_x" : "")}{(tiles > 0 ? $"_flow{tiles}" : "")}.bgem";
        if (tiles > 0 && !files.Any(f => f.Path == FlowTexture))
            using (var bmp = Materials.FlowTexture()) files.Add((FlowTexture, Icons.Dds(bmp)));
        if (!mats.ContainsKey(path)) mats[path] = Materials.Beam(template, c, st.BeamFx.Walls, st.BeamStrength, tiles > 0 ? "itemfinder/beamflow.dds" : null, tiles);
        return path;
    }
}
