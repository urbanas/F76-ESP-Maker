using System.Text;

namespace ItemFinder;

/// <summary>Headless developer modes; results go to selftest\ next to the exe.</summary>
public static class SelfTest
{
    static string Dir => Directory.CreateDirectory(AppPaths.File("selftest")).FullName;

    static (GameData Game, GameIndex Index, List<Category> Cats) Load(Action<string> log)
    {
        var folder = GameData.FindGameFolder(Settings.Load().GameFolder) ?? throw new DirectoryNotFoundException("game not found");
        var game = GameData.Load(folder, new List<string>(), _ => { });
        var idx = GameIndex.Load(game, log);
        bool Exists(string m) => game.Find(@"meshes\" + m) != null;
        return (game, idx, EventCatalog.BuildAll(idx, Exists, game.AllPaths));
    }

    /// <summary>--preview: pictures of entries (ids separated by |) as the Item look preview draws them, with the
    /// look from a setup file (or the category's), from three sides.</summary>
    static Dictionary<string, PreviewScene>? propModels;

    public static int Preview(string ids, string outDir, string? projectFile)
    {
        var sb = new StringBuilder();
        try
        {
            var (game, _, cats) = Load(s => { });
            var project = projectFile != null ? Json.Read<Project>(projectFile) ?? new Project() : new Project();
            project.Upgrade();
            Directory.CreateDirectory(outDir);
            foreach (var id in ids.Split('|'))
            {
                var c = cats.First(c => c.Entries.Any(e => e.Id == id));
                var e = c.Entries.First(e => e.Id == id);
                var st = project.StyleOf(e, c);
                var model = e.BeamModels?.FirstOrDefault() ?? e.Models[0];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var scene = e.Assembled ? ModelPreview.LoadAll(game, project.ModelsOf(e), st, e.NoIcon, e.Harvestable, true, project.KeepOtherMods)
                    : ModelPreview.Load(game, model, st, e.NoIcon, e.Harvestable, true, project.KeepOtherMods, e.Name);
                sb.AppendLine($"{id}: {model} parts={scene.Parts.Count} ({string.Join(", ", scene.Parts.GroupBy(p => p.Kind).Select(g => $"{g.Key} {g.Count()}"))}) " +
                              $"textured={scene.Parts.Count(p => p.Tex != null)} box={scene.Min}..{scene.Max} load={sw.ElapsedMilliseconds}ms {scene.Note}");
                foreach (var p in scene.Parts.Where(p => p.Kind != PartKind.Glow))
                    sb.AppendLine($"   {p.Kind} {p.Name}: x {p.P.Where((_, i) => i % 3 == 0).DefaultIfEmpty().Min():0}..{p.P.Where((_, i) => i % 3 == 0).DefaultIfEmpty().Max():0}" +
                                  $" y {p.P.Where((_, i) => i % 3 == 1).DefaultIfEmpty().Min():0}..{p.P.Where((_, i) => i % 3 == 1).DefaultIfEmpty().Max():0}" +
                                  $" z {p.P.Where((_, i) => i % 3 == 2).DefaultIfEmpty().Min():0}..{p.P.Where((_, i) => i % 3 == 2).DefaultIfEmpty().Max():0}");
                var safe = string.Concat(id.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
                int k = 0;
                foreach (var (yaw, whole) in new[] { (-0.9f, false), (1.6f, false), (-0.9f, true) })
                {
                    var (min, max) = whole ? (scene.AllMin, scene.AllMax) : (scene.Min, scene.Max);
                    float r = Math.Max(1f, 0.5f * MathF.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y) + (max.Z - min.Z) * (max.Z - min.Z)));
                    sw.Restart();
                    using var bmp = PreviewRenderer.Render(scene, st, 480, 480, yaw, 0.35f, ((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2), r / MathF.Sin(18 * MathF.PI / 180));
                    sb.AppendLine($"   view {k}: {sw.ElapsedMilliseconds}ms");
                    bmp.Save(Path.Combine(outDir, $"{safe}_{k++}.png"));
                }
                { // the size guide: grid + 1.8 m person, framed like the preview's
                    var (min, max) = (scene.Min, scene.Max);
                    float cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2, size = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
                    float d = 0.5f * MathF.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y)) + 35;
                    // the Add menu's game models, placed as the big view places them
                    var pm = propModels ??= ModelView.LoadPropModels(game);
                    var person = pm.GetValueOrDefault(ModelView.PropPerson) is { } pp ? ModelView.Stand(pp, cx + 0.78f * d, cy + 0.62f * d, min.Z, MathF.PI).ToArray()
                               : new[] { SizeGuide.PersonAt(cx + 0.78f * d, cy + 0.62f * d, min.Z) };
                    var g = new SizeGuide(min.Z, cx, cy, size < 42 ? 7 : 70, Math.Max(d + 60, size * 1.5f), true, person);
                    float z1 = Math.Max(max.Z, min.Z + 126), r = 0.5f * MathF.Sqrt(4 * (d + 25) * (d + 25) + (z1 - min.Z) * (z1 - min.Z));
                    using var bmp = PreviewRenderer.Render(scene, st, 480, 480, -0.9f, 0.35f, (cx + 0.39f * d, cy + 0.31f * d, (min.Z + z1) / 2), r / MathF.Sin(18 * MathF.PI / 180), g);
                    bmp.Save(Path.Combine(outDir, $"{safe}_guide.png"));
                    // a house beside it and walls around it (the person outside), seen from 30 m at eye height and from above
                    var walls = ModelView.WallsAround(pm, min, max);
                    float wd = walls == null ? d : Math.Max(d, (Math.Max(walls.Value.HalfX, walls.Value.HalfY) + 45) / 0.78f);
                    var house = pm.GetValueOrDefault(ModelView.PropHouse);
                    float hd = wd + (house == null ? 322 : MathF.Sqrt(MathF.Pow(Math.Max(-house.Min.X, house.Max.X), 2) + MathF.Pow(Math.Max(-house.Min.Y, house.Max.Y), 2))) + 10;
                    var all = new List<PreviewPart>();
                    if (house != null) all.AddRange(ModelView.Stand(house, cx - 0.78f * hd, cy - 0.62f * hd, min.Z)); else all.Add(SizeGuide.HouseAt(cx - 0.78f * hd, cy - 0.62f * hd, min.Z));
                    if (walls != null) all.AddRange(walls.Value.Parts); else all.Add(SizeGuide.ShedAround(min, max));
                    if (pm.GetValueOrDefault(ModelView.PropPerson) is { } pp2) all.AddRange(ModelView.Stand(pp2, cx + 0.78f * wd, cy + 0.62f * wd, min.Z, MathF.PI));
                    var props = new SizeGuide(min.Z, cx, cy, 70, 2800, true, all.ToArray());
                    var day = new RenderExtras { Sky = Sky.Day };
                    var eye = (cx + 2100 * MathF.Cos(-0.9f), cy + 2100 * MathF.Sin(-0.9f), min.Z + 112);
                    using var far = PreviewRenderer.Render(scene, st, 640, 400, eye, (cx - eye.Item1, cy - eye.Item2, 0), 50, 2, props, day);
                    far.Save(Path.Combine(outDir, $"{safe}_far.png"));
                    var mid = (cx - 0.39f * hd, cy - 0.31f * hd, min.Z + 150);
                    using var near = PreviewRenderer.Render(scene, st, 640, 400, -1.2f, 0.45f, mid, hd * 1.6f + 400, props, day);
                    near.Save(Path.Combine(outDir, $"{safe}_props.png"));
                    using var close = PreviewRenderer.Render(scene, st, 640, 400, -0.5f, 0.3f, (cx + 0.39f * wd, cy + 0.31f * wd, min.Z + 120), wd * 2.2f + 300, props, day);
                    close.Save(Path.Combine(outDir, $"{safe}_walls.png"));
                }
            }
        }
        catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
        File.WriteAllText(Path.Combine(outDir, "preview.log"), sb.ToString());
        return 0;
    }

    /// <summary>--lookshot: opens the Item look window for an entry and saves a picture of it once the preview is drawn.</summary>
    /// <summary>Dev: game textures to PNG (each pixel x 4 if small) with their size and alpha range in tex.log.</summary>
    public static int Tex(string paths, string outDir)
    {
        var sb = new StringBuilder();
        var (game, _, _) = Load(s => { });
        Directory.CreateDirectory(outDir);
        foreach (var path in paths.Split('|'))
        {
            var t = ModelPreview.LoadTexture(game, path);
            if (t == null) { sb.AppendLine($"{path}: not found"); continue; }
            var a = Enumerable.Range(0, t.Width * t.Height).Select(i => t.Pixels[i * 4 + 3]).ToList();
            sb.AppendLine($"{path}: {t.Width}x{t.Height} alpha {a.Min()}..{a.Max()} first {string.Join(" ", Enumerable.Range(0, Math.Min(8, t.Width)).Select(x => $"{t.Pixels[x * 4]:X2}{t.Pixels[x * 4 + 1]:X2}{t.Pixels[x * 4 + 2]:X2}/{t.Pixels[x * 4 + 3]:X2}"))}");
            using var bmp = Bitmaps.FromRgba(t, opaque: true);
            int k = t.Width < 128 || t.Height < 32 ? 4 : 1;
            using var big = new Bitmap(t.Width * k, Math.Max(t.Height * k, 16));
            using (var g = Graphics.FromImage(big)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; g.DrawImage(bmp, 0, 0, big.Width, big.Height); }
            big.Save(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".png"));
        }
        File.WriteAllText(Path.Combine(outDir, "tex.log"), sb.ToString());
        return 0;
    }

    /// <summary>Dev: every multi-part entry's part names (parts list labels).</summary>
    public static int PartNamesDump(string file)
    {
        var (_, _, cats) = Load(s => { });
        var sb = new StringBuilder();
        foreach (var e in cats.SelectMany(c => c.Entries).Where(e => e.Models.Count > 1))
        {
            sb.AppendLine($"== {e.Category} / {e.Name}");
            foreach (var m in e.Models) sb.AppendLine($"   {e.PartNames?.GetValueOrDefault(m) ?? "(none)",-60} {m}");
        }
        File.WriteAllText(file, sb.ToString());
        return 0;
    }

    /// <summary>Dev: every part of one model as the preview builds it (kind, name, vertex box), glow on.</summary>
    public static int Scene(string model, string file)
    {
        var (game, _, _) = Load(s => { });
        var st = new Style { Glow = true, GlowColor = "#FF0000" };
        var scene = ModelPreview.Load(game, model, st, false, false, true, false);
        var sb = new StringBuilder();
        foreach (var p in scene.Parts)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i + 2 < p.P.Length; i += 3) { x0 = Math.Min(x0, p.P[i]); x1 = Math.Max(x1, p.P[i]); z0 = Math.Min(z0, p.P[i + 2]); z1 = Math.Max(z1, p.P[i + 2]); }
            sb.AppendLine($"{p.Kind,-7} {p.Name,-28} verts {p.P.Length / 3,5}  x {x0,8:F1}..{x1,8:F1}  z {z0,8:F1}..{z1,8:F1}");
        }
        File.WriteAllText(file, sb.ToString());
        // and a plain picture of it, from the front-left and from behind (file.png, file-back.png)
        var plain = new Style { Glow = false, Beam = false, Brighten = false, Icon = "" };
        var bare = ModelPreview.Load(game, model, plain, true, false, false, false);
        var (mn, mx) = (bare.Min, bare.Max);
        float r = Math.Max(1f, 0.5f * MathF.Sqrt((mx.X - mn.X) * (mx.X - mn.X) + (mx.Y - mn.Y) * (mx.Y - mn.Y) + (mx.Z - mn.Z) * (mx.Z - mn.Z)));
        foreach (var (yaw, name) in new[] { (-2.0f, ".png"), (1.1f, "-back.png") })
        {
            using var bmp = PreviewRenderer.Render(bare, plain, 640, 480, yaw, 0.3f, ((mn.X + mx.X) / 2, (mn.Y + mx.Y) / 2, (mn.Z + mx.Z) / 2), r / MathF.Sin(20 * MathF.PI / 180),
                                                   x: new RenderExtras { Sky = Sky.Day });
            bmp.Save(Path.ChangeExtension(file, null) + name);
        }
        return 0;
    }

    public static int LookShot(string id, string png, string? projectFile)
    {
        var (game, _, cats) = Load(_ => { });
        var project = projectFile != null ? Json.Read<Project>(projectFile) ?? new Project() : new Project();
        ApplicationConfiguration.Initialize();
        if (id.StartsWith("cat:")) // a category's look window
        {
            var cat = cats.First(x => x.Name == id[4..]);
            using var cd = MainForm.CategoryLookDialog(game, "Look for: " + cat.Name, cat.Entries, project.CategoryStyles.GetValueOrDefault(cat.Name) ?? cat.DefaultStyle,
                                                      false, project.KeepOtherMods, new Font("Segoe UI", 9f), out _);
            Shoot(cd, png);
            return 0;
        }
        var c = cats.First(c => c.Entries.Any(e => e.Id == id));
        var e = c.Entries.First(e => e.Id == id);
        using var dlg = MainForm.LookDialog(game, new List<Entry> { e }, project.StyleOf(e, c), project.KeepOtherMods, new Font("Segoe UI", 9f), out _, out _,
                                            project.OffParts.GetValueOrDefault(e.Id), null,
                                            project.Selected.Contains(e.Id) ? CheckState.Checked : CheckState.Unchecked, out _,
                                            partLooks: project.PartLooks.GetValueOrDefault(e.Id),
                                            focusPart: project.PartLooks.GetValueOrDefault(e.Id)?.Keys.FirstOrDefault());
        Shoot(dlg, png);
        return 0;
    }

    static void Shoot(Form dlg, string png)
    {
        dlg.StartPosition = FormStartPosition.CenterScreen;
        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            using var bmp = new Bitmap(dlg.Width, dlg.Height);
            dlg.DrawToBitmap(bmp, new Rectangle(Point.Empty, dlg.Size));
            bmp.Save(png);
            dlg.Close();
        };
        dlg.Shown += (_, _) => timer.Start();
        dlg.ShowDialog();
    }

    /// <summary>--list: every category and entry with its models.</summary>
    public static int List()
    {
        var sb = new StringBuilder();
        var (_, _, cats) = Load(s => sb.AppendLine(s));
        foreach (var c in cats)
        {
            sb.AppendLine($"== {c.Name} ({c.Entries.Count})");

            foreach (var e in c.Entries)
                sb.AppendLine($"   {e.Name}  <{e.Id}>  [{e.Detail}] spots={e.Spots}  models: {string.Join(", ", e.Models)}  names: {string.Join(" · ", e.ItemNames)}");
        }
        File.WriteAllText(Path.Combine(Dir, "list.txt"), sb.ToString());
        return 0;
    }

    /// <summary>--selftest &lt;tested beacons folder&gt; &lt;entry ids, comma separated&gt;: compares materials and the beam
    /// with the in-game-tested Spooky Scorched Beacons files, then builds the given entries.</summary>
    public static int Run(string testedFolder, string ids)
    {
        var sb = new StringBuilder();
        void Log(string s) { sb.AppendLine(s); }
        int fails = 0;
        try
        {
            var (game, _, cats) = Load(Log);
            var tpl = game.Find(Materials.Template)!;
            var template = tpl.Archive.ReadFile(tpl);

            // 1. materials: same bytes as the tested through-walls beam / body glow, apart from the colour floats
            var purple = System.Drawing.Color.FromArgb(148, 32, 255);
            fails += Compare(Log, "beam.bgem", Materials.Beam(template, purple, true), Path.Combine(testedFolder, @"materials\spookybeacons\beam.bgem"));
            fails += Compare(Log, "bodyglow.bgem", Materials.Glow(template, purple, 40, true, additive: false), Path.Combine(testedFolder, @"materials\spookybeacons\bodyglow.bgem"));

            // 2. beam geometry: same as the tested head beam (z 150..1000, half width 12)
            var head = Directory.GetFiles(Path.Combine(testedFolder, "meshes"), "*.nif", SearchOption.AllDirectories)
                                .Select(p => (p, NifFile.Parse(File.ReadAllBytes(p))))
                                .FirstOrDefault(x => x.Item2.Blocks.Any(b => b.Type == "BSTriShape" && x.Item2.Strings[BitConverter.ToInt32(b.Data, 0)] == "SpookyBeam:0"));
            if (head.Item2 != null)
            {
                var tested = head.Item2.Blocks.First(b => b.Type == "BSTriShape" && head.Item2.Strings[BitConverter.ToInt32(b.Data, 0)] == "SpookyBeam:0").Data;
                var mine = NifPatch.BeamShape(BitConverter.ToInt32(tested, 0), BitConverter.ToInt32(tested, 116), BitConverter.ToInt32(tested, 120), 1000, 12, z0: 150);
                bool same = tested.AsSpan().SequenceEqual(mine);
                Log($"beam shape vs {Path.GetFileName(head.p)}: {(same ? "IDENTICAL" : "DIFFERENT")} ({mine.Length} / {tested.Length} bytes)");
                if (!same)
                {
                    fails++;
                    for (int i = 0; i < Math.Min(mine.Length, tested.Length); i++)
                        if (mine[i] != tested[i]) { Log($"   first difference at byte {i}"); break; }
                }
            }
            else { Log("no tested beam nif found"); fails++; }

            // 3. build the requested entries and check every output model reads back
            var project = new Project { ModName = "ItemFinderTest" };
            foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries)) project.Selected.Add(id.Trim());
            var outFile = Path.Combine(Dir, "ItemFinderTest.ba2");
            var res = Builder.Build(project, cats, game, outFile, Log);
            Log($"built {res.Models} models, {res.Failed} failed");
            foreach (var c in res.Conflicts) Log("  conflict: " + c);
            var arch = Ba2Archive.Open(outFile);
            foreach (var e in arch.Entries.Where(e => e.Name.EndsWith(".nif")))
            {
                var nif = NifFile.Parse(arch.ReadFile(e));
                Log($"  ok {e.Name}: {nif.Blocks.Count} blocks, children of root: {nif.Children(0).Count}");
                var outPath = Path.Combine(Dir, "out", e.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, arch.ReadFile(e));
            }
            Log($"materials: {string.Join(", ", arch.Entries.Where(e => e.Name.EndsWith(".bgem")).Select(e => e.Name))}");
        }
        catch (Exception ex) { Log("ERROR " + ex); fails++; }
        Log(fails == 0 ? "ALL OK" : $"{fails} problem(s)");
        File.WriteAllText(Path.Combine(Dir, "selftest.log"), sb.ToString());
        return fails == 0 ? 0 : 1;
    }

    /// <summary>--build &lt;project.json&gt; &lt;out.ba2&gt;: builds a saved setup exactly like the app, log in build.log.</summary>
    public static int Build(string projectFile, string outFile)
    {
        var sb = new StringBuilder();
        try
        {
            var (game, _, cats) = Load(s => sb.AppendLine(s));
            var project = Json.Read<Project>(projectFile) ?? throw new InvalidDataException("can't read " + projectFile);
            project.Upgrade();
            var (em, eb) = Builder.Estimate(project, cats, game);
            sb.AppendLine($"estimate: {em} models, {eb / 1_000_000} MB");
            var r = Builder.Build(project, cats, game, outFile, s => sb.AppendLine(s));
            sb.AppendLine($"built {r.Models} models, {r.Failed} failed");
            foreach (var c in r.Conflicts) sb.AppendLine("conflict: " + c);
        }
        catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
        File.WriteAllText(Path.Combine(Dir, "build.log"), sb.ToString());
        return 0;
    }

    /// <summary>--stress: ticks every entry of every category, builds, and checks every output model:
    /// re-parses it and verifies all node children / shape refs point inside the file. Log: stress.log.</summary>
    /// <param name="projectFile">Looks to use (e.g. every feature on); every entry gets ticked either way.</param>
    public static int Stress(string? projectFile = null)
    {
        var sb = new StringBuilder();
        int bad = 0;
        try
        {
            var (game, _, cats) = Load(s => sb.AppendLine(s));
            var project = projectFile != null ? Json.Read<Project>(projectFile) ?? new Project() : new Project();
            project.Upgrade();
            foreach (var e in cats.SelectMany(c => c.Entries)) project.Selected.Add(e.Id);
            var outFile = Path.Combine(Dir, "stress.ba2");
            var log = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = Builder.Build(project, cats, game, outFile, log.Add);
            File.WriteAllLines(Path.Combine(Dir, "stress-build.log"), log);
            sb.AppendLine($"built {r.Models} models, {r.Failed} failed in {sw.Elapsed.TotalSeconds:F1} s, archive {new FileInfo(outFile).Length / 1048576.0:F1} MB");
            foreach (var g in log.Where(l => l.Contains("could not be changed") || l.Contains("not found")).GroupBy(l => System.Text.RegularExpressions.Regex.Replace(l, @"^.*?(could not be changed|not found)", "$1")).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {g.Count(),5} x {g.Key}   e.g. {g.First().Trim()}");
            foreach (var g in log.Where(l => l.Contains(" - ") && !l.Contains("could not")).Select(l => l[(l.LastIndexOf(" - ") + 3)..])
                                 .SelectMany(l => l.Split(", ")).Where(p => !p.StartsWith("glow on") && p != "beam")
                                 .GroupBy(p => System.Text.RegularExpressions.Regex.Replace(p, @"\d+", "N")).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  note {g.Count(),5} x {g.Key}");
            var arch = Ba2Archive.Open(outFile);
            int checkedModels = 0;
            foreach (var e in arch.Entries.Where(e => e.Name.EndsWith(".nif")))
            {
                string? problem = null;
                try
                {
                    var nif = NifFile.Parse(arch.ReadFile(e));
                    int n = nif.Blocks.Count;
                    for (int i = 0; i < n && problem == null; i++)
                    {
                        if (nif.IsNode(i))
                            foreach (var k in nif.Children(i))
                            {
                                if (k < -1 || k >= n) problem = $"node {i} child {k} out of range";
                                else if (k >= 0 && nif.IsShape(k) && NifPatch.IsEditorOnly(nif.Strings.ElementAtOrDefault(BitConverter.ToInt32(nif.Blocks[k].Data, 0)) ?? ""))
                                    problem = $"node {i} still holds editor marker {k}";
                            }
                        if (nif.IsShape(i))
                        {
                            var b = nif.Blocks[i].Data;
                            int rr = NifFile.ShapeRefs(b);
                            for (int j = 0; j < 3; j++) { int v = BitConverter.ToInt32(b, rr + 4 * j); if (v < -1 || v >= n) problem = $"shape {i} ref {j} = {v} out of range"; }
                        }
                    }
                    int beams = nif.Strings.Count(s => s == "ItemFinderBeam");
                    if (beams > 1) problem ??= $"{beams} beams"; // creature parts other than the body get none
                }
                catch (Exception ex) { problem = ex.Message; }
                checkedModels++;
                if (problem != null) { bad++; sb.AppendLine($"  BAD {e.Name}: {problem}"); }
            }
            sb.AppendLine($"checked {checkedModels} output models, {bad} bad");
        }
        catch (Exception ex) { sb.AppendLine("ERROR " + ex); bad++; }
        File.WriteAllText(Path.Combine(Dir, "stress.log"), sb.ToString());
        return bad == 0 ? 0 : 1;
    }

    /// <summary>--previewall: every model of every entry through the Item look preview (load, patch, draw one small view).
    /// Counts crashes, the "can't change" note, empty pictures and the slowest. Log: previewall.log.</summary>
    public static int PreviewAll(string? projectFile = null)
    {
        var sb = new StringBuilder();
        var errors = new Dictionary<string, List<string>>();
        var notes = new Dictionary<string, List<string>>();
        var mods = new Dictionary<string, List<string>>(); // caused by other installed mods (e.g. a mod that empties doors): not counted
        var blank = new List<string>();
        var slow = new List<(long Ms, string Name)>();
        int done = 0;
        void Add(Dictionary<string, List<string>> d, string key, string what) { if (!d.TryGetValue(key, out var l)) d[key] = l = new(); l.Add(what); }
        try
        {
            var (game, _, cats) = Load(s => { });
            var project = projectFile != null ? Json.Read<Project>(projectFile) ?? new Project() : new Project();
            project.Upgrade();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cats)
                foreach (var e in c.Entries.Where(e => e.Group == null))
                {
                    var st = project.StyleOf(e, c);
                    foreach (var model in e.Models.Where(seen.Add))
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            var scene = ModelPreview.Load(game, model, st, e.NoIcon, e.Harvestable, true, project.KeepOtherMods, e.Name);
                            var (min, max) = (scene.Min, scene.Max);
                            float r = Math.Max(1f, 0.5f * MathF.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y) + (max.Z - min.Z) * (max.Z - min.Z)));
                            using var bmp = PreviewRenderer.Render(scene, st, 96, 96, -0.9f, 0.35f, ((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2), r / MathF.Sin(18 * MathF.PI / 180));
                            var corner = bmp.GetPixel(0, 0);
                            bool empty = true;
                            for (int y = 0; y < 96 && empty; y++) for (int x = 0; x < 96 && empty; x++) empty = bmp.GetPixel(x, y) == corner;
                            if (empty) blank.Add($"{e.Name}: {model}");
                            if (scene.Note.Contains(".ba2")) Add(mods, "NOTE " + scene.Note, $"{e.Name}: {model}");
                            else if (scene.Note != "") Add(notes, System.Text.RegularExpressions.Regex.Replace(scene.Note, @"[^ ]+\.ba2", "<mod>.ba2"), $"{e.Name}: {model}");
                        }
                        catch (Exception ex) when (ex.Message.Contains(".ba2")) { Add(mods, "ERROR " + ex.Message, $"{e.Name}: {model}"); }
                        catch (Exception ex) { Add(errors, ex.GetType().Name + ": " + ex.Message, $"{e.Name}: {model}"); }
                        slow.Add((sw.ElapsedMilliseconds, model));
                        done++;
                    }
                }
        }
        catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
        sb.AppendLine($"{done} models, {errors.Values.Sum(l => l.Count)} failed to show, {notes.Values.Sum(l => l.Count)} with a note, {blank.Count} empty pictures, {mods.Values.Sum(l => l.Count)} from other mods (not counted)");
        foreach (var (k, l) in errors.OrderByDescending(kv => kv.Value.Count)) { sb.AppendLine($"  ERROR {l.Count} x {k}"); foreach (var w in l.Take(40)) sb.AppendLine("     " + w); }
        foreach (var (k, l) in notes.OrderByDescending(kv => kv.Value.Count)) { sb.AppendLine($"  NOTE {l.Count} x {k}"); foreach (var w in l.Take(40)) sb.AppendLine("     " + w); }
        foreach (var w in blank.Take(40)) sb.AppendLine("  EMPTY " + w);
        foreach (var (k, l) in mods) { sb.AppendLine($"  MOD {l.Count} x {k}"); foreach (var w in l.Take(40)) sb.AppendLine("     " + w); }
        foreach (var (ms, m) in slow.OrderByDescending(x => x.Ms).Take(5)) sb.AppendLine($"  slow {ms} ms {m}");
        File.WriteAllText(Path.Combine(Dir, "previewall.log"), sb.ToString());
        return 0;
    }

#if DEVTOOLS
    /// <summary>--installtest &lt;empty sandbox folder&gt;: Install / Uninstall on a made-up game folder and ini (never the real
    /// ones). Log: installtest.log.</summary>
    public static int InstallTest(string sandbox)
    {
        var log = new List<string>();
        int failed = 0;
        void Check(bool ok, string what) { log.Add((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failed++; }
        var quiet = new Action<string>(_ => { });
        try
        {
            var folder = Path.Combine(sandbox, "game");
            var data = Directory.CreateDirectory(Path.Combine(folder, "Data")).FullName;
            File.WriteAllBytes(Path.Combine(folder, "Fallout76.exe"), Array.Empty<byte>());
            var built = Directory.CreateDirectory(Path.Combine(sandbox, "built")).FullName;
            string a = Path.Combine(built, "Mine.ba2"), t = Path.Combine(built, "Mine - Textures.ba2");
            File.WriteAllText(a, "new"); File.WriteAllText(t, "tex");
            var ini = GameData.TestIni = Path.Combine(sandbox, "ini", "Fallout76Custom.ini");
            var g = GameData.Load(folder, new List<string>(), quiet);
            string List() => File.ReadAllLines(ini).First(l => l.StartsWith("sResourceArchive2List", StringComparison.OrdinalIgnoreCase));
            void Reset(string? text = null)
            {
                foreach (var f in Directory.GetFiles(data)) File.Delete(f);
                Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(ini)!)) File.Delete(f);
                if (text != null) File.WriteAllText(ini, text);
            }

            log.Add("no ini yet");
            g.Install(new[] { a, t }, Array.Empty<string>(), quiet);
            Check(File.Exists(Path.Combine(data, "Mine.ba2")) && File.Exists(Path.Combine(data, "Mine - Textures.ba2")), "both archives copied to Data");
            Check(List() == "sResourceArchive2List=Mine.ba2,Mine - Textures.ba2", "ini made, both listed: " + List());
            Check(File.ReadAllLines(ini).Contains("[Archive]") && File.ReadAllLines(ini).Contains("bInvalidateOlderFiles=1"), "ini has the usual mod setup");

            log.Add("ini with other mods");
            const string text = "[General]\r\nsStartingConsoleCommand=x\r\n\r\n[Archive]\r\n; my mods\r\nsResourceArchive2List= Other.ba2 , Another.ba2\r\nbInvalidateOlderFiles=1\r\n\r\n[Display]\r\nfoo=1\r\n";
            Reset(text);
            File.WriteAllText(Path.Combine(data, "Other.ba2"), "other");
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            Check(List() == "sResourceArchive2List=Other.ba2,Another.ba2,Mine.ba2", "ours last, others kept: " + List());
            Check(File.ReadAllText(ini + ".before-espmaker") == text, "backup is the file as it was");
            Check(File.ReadAllLines(ini).Length == text.Split("\r\n").Length - 1 && File.ReadAllLines(ini).Contains("; my mods") && File.ReadAllLines(ini).Contains("foo=1"), "every other line kept");
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            Check(List() == "sResourceArchive2List=Other.ba2,Another.ba2,Mine.ba2", "installed again: not listed twice");
            Check(File.ReadAllText(ini + ".before-espmaker") == text, "second install keeps the first backup");
            g.Install(new[] { a, t }, Array.Empty<string>(), quiet);
            g.Install(new[] { a }, new[] { "Mine - Textures.ba2" }, quiet);
            Check(!File.Exists(Path.Combine(data, "Mine - Textures.ba2")) && !List().Contains("Textures"), "texture archive no longer needed: gone from Data and ini");
            g.Uninstall(new[] { "mine.BA2" }, quiet);
            Check(!File.Exists(Path.Combine(data, "Mine.ba2")) && List() == "sResourceArchive2List=Other.ba2,Another.ba2", "uninstall (any case): ours gone, others kept: " + List());
            Check(File.Exists(Path.Combine(data, "Other.ba2")), "other mod's file untouched");
            Check(File.ReadAllLines(ini).Contains("; my mods") && File.ReadAllLines(ini).Contains("foo=1"), "other lines still there");

            log.Add("odd inis");
            Reset("[Archive]\r\nbInvalidateOlderFiles=1\r\n");
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            Check(File.ReadAllLines(ini)[1] == "sResourceArchive2List=Mine.ba2", "no list line: added under [Archive]");
            Reset("[General]\r\nfoo=1\r\n");
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            var noArch = File.ReadAllLines(ini);
            Check(Array.IndexOf(noArch, "[Archive]") >= 0 && Array.IndexOf(noArch, "sResourceArchive2List=Mine.ba2") == Array.IndexOf(noArch, "[Archive]") + 1, "no [Archive] section: added");
            Reset("[archive]\r\nSRESOURCEARCHIVE2LIST=Other.ba2\r\n");
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            Check(List() == "sResourceArchive2List=Other.ba2,Mine.ba2", "other letter case: same line used");
            Reset("[Archive]\r\nsResourceArchive2List=\r\n");
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            Check(List() == "sResourceArchive2List=Mine.ba2", "empty list: no stray comma");
            g.Uninstall(new[] { "Mine.ba2" }, quiet);
            Check(List() == "sResourceArchive2List=", "last mod out: empty list");
            Reset("﻿[Archive]\r\nsResourceArchive2List=Other.ba2\r\n");
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            Check(List() == "sResourceArchive2List=Other.ba2,Mine.ba2", "ini with a byte order mark");
            Reset();
            File.WriteAllBytes(ini, Encoding.Latin1.GetBytes("[Archive]\r\n; café\r\nsResourceArchive2List=Other.ba2\r\n"));
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            Check(File.ReadAllBytes(ini).AsSpan().IndexOf(Encoding.Latin1.GetBytes("café")) >= 0, "non-UTF-8 comment survives");
            g.Uninstall(new[] { "Mine.ba2" }, quiet);
            Check(File.ReadAllBytes(ini).AsSpan().IndexOf(Encoding.Latin1.GetBytes("café")) >= 0, "...and an uninstall");
            Reset("[Archive]\r\nsResourceArchive2List=Other.ba2\r\nsResourceArchive2List=Second.ba2\r\n");
            g.Install(new[] { a }, Array.Empty<string>(), quiet);
            Check(File.ReadAllLines(ini).Where(l => l.StartsWith("sResourceArchive2List")).All(l => l.EndsWith(",Mine.ba2")), "two list lines: ours on both");
            g.Uninstall(new[] { "Mine.ba2" }, quiet);
            Check(!File.ReadAllText(ini).Contains("Mine.ba2") && File.ReadAllText(ini).Contains("Other.ba2") && File.ReadAllText(ini).Contains("Second.ba2"), "two list lines: uninstall clears both");
            Reset();
            g.Uninstall(new[] { "Mine.ba2" }, quiet);
            Check(!File.Exists(ini), "uninstall with no ini: nothing made, no error");

            log.Add("failures");
            Reset("[Archive]\r\nsResourceArchive2List=Other.ba2\r\n");
            File.WriteAllText(Path.Combine(data, "Mine.ba2"), "old");
            string before = File.ReadAllText(ini);
            using (new FileStream(Path.Combine(data, "Mine.ba2"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool threw = false;
                try { g.Install(new[] { a }, Array.Empty<string>(), quiet); } catch (IOException) { threw = true; }
                Check(threw && File.ReadAllText(ini) == before, "archive locked: reports it, ini unchanged");
            }
            Reset("[Archive]\r\nsResourceArchive2List=Other.ba2\r\n");
            File.SetAttributes(ini, FileAttributes.ReadOnly);
            bool failedIni = false;
            try { g.Install(new[] { a }, Array.Empty<string>(), quiet); } catch (UnauthorizedAccessException) { failedIni = true; }
            File.SetAttributes(ini, FileAttributes.Normal);
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(ini)!)) File.SetAttributes(f, FileAttributes.Normal);
            Check(failedIni, "read-only ini: reports it (not silently left uninstalled)");

            log.Add("Microsoft version");
            var ms = Directory.CreateDirectory(Path.Combine(sandbox, "ms")).FullName;
            Directory.CreateDirectory(Path.Combine(ms, "Data"));
            File.WriteAllBytes(Path.Combine(ms, "Project76_GamePass.exe"), Array.Empty<byte>());
            var mg = GameData.Load(ms, new List<string>(), quiet);
            Check(mg.Microsoft && mg.IniName == "Project76Custom.ini", "its own ini name");
        }
        catch (Exception ex) { log.Add("ERROR " + ex); failed++; }
        log.Add(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
        File.WriteAllLines(Path.Combine(Dir, "installtest.log"), log);
        return failed == 0 ? 0 : 1;
    }
#endif

    /// <summary>--links: entries in different categories that share a model (what linked ticking would join).</summary>
    /// <summary>--brightentest: every game material through Materials.Brighten: the result must read back with its
    /// emissive on, a glow map, the other textures and everything behind the emissive block untouched.</summary>
    public static int BrightenTest(string logFile)
    {
        var sb = new StringBuilder();
        int bad = 0, done = 0, unread = 0, noColour = 0;
        void Check(bool ok, string what) { if (ok) sb.AppendLine("  ok " + what); else { sb.AppendLine("  FAIL " + what); bad++; } }
        var (game, _, _) = Load(_ => { });
        // the layout against known game files: Rad Mothman eggs glow by a map, carrots don't. The patched carrot's bytes
        // are counted from the file's end, so a wrong offset in Materials can't agree with itself
        byte[] Original(string path) => game.FindOriginal(path) is Ba2Entry f ? f.Archive.ReadFile(f) : Array.Empty<byte>();
        var eggs = Original(@"materials\actors\mothman\floraradeggs.bgsm");
        var carrot = Original(@"materials\landscape\plants\carrot01.bgsm");
        Check(Materials.ReadEmissive(eggs) is { } ge && eggs[ge.Flag] == 1 && eggs[ge.GlowMap] == 1 && ge.GlowMap == eggs.Length - 24, "glow map flag where the game's eggs have it");
        Check(Materials.ReadEmissive(carrot) is { } gc && carrot[gc.Flag] == 0 && carrot[gc.GlowMap] == 0 && gc.GlowMap == carrot.Length - 24, "carrots have no glow");
        var bright = Materials.Brighten(carrot, 1.5f) ?? Array.Empty<byte>();
        Check(bright.Length == carrot.Length + 31 + 12 && bright[^24] == 1 && Encoding.ASCII.GetString(bright).Split("Carrot01_d.dds").Length == 3, "brightened carrot: colour texture as glow map, flag set");
        foreach (var path in game.AllPaths.Where(p => p.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase)))
        {
            if (game.FindOriginal(path) is not Ba2Entry entry) continue;
            var src = entry.Archive.ReadFile(entry);
            var from = Materials.ReadEmissive(src);
            if (from == null) { unread++; continue; }
            var m = Materials.Brighten(src, 1.5f);
            if (m == null) { noColour++; continue; }
            done++;
            var to = Materials.ReadEmissive(m);
            bool good = to != null && m[to.Flag] == 1 && m[to.GlowMap] == 1 && to.GlowLength > 1 && BitConverter.ToSingle(m, to.Strength) > 0
                        && m[..from.GlowAt].SequenceEqual(src[..from.GlowAt])
                        && m[(to.Strength + 4)..to.GlowMap].SequenceEqual(src[(from.Strength + 4)..from.GlowMap])
                        && m[(to.GlowMap + 1)..].SequenceEqual(src[(from.GlowMap + 1)..]);
            if (!good) { if (bad < 5) Check(false, path); else bad++; }
        }
        Check(done > 20000, $"{done} materials brightened");
        Check(unread < 50, $"{unread} unreadable");
        sb.AppendLine($"  ok {noColour} without a colour texture");
        sb.AppendLine(bad == 0 ? "ALL PASSED" : $"{bad} FAILED");
        File.WriteAllText(logFile, sb.ToString());
        return bad == 0 ? 0 : 1;
    }

    public static int Links()
    {
        var sb = new StringBuilder();
        var (_, _, cats) = Load(_ => { });
        var all = cats.SelectMany(c => c.Entries).ToList();
        var byModel = all.SelectMany(e => e.Models.Select(m => (m, e))).GroupBy(x => x.m).ToDictionary(g => g.Key, g => g.Select(x => x.e).ToList());
        foreach (var e in all)
        {
            var twins = e.Models.SelectMany(m => byModel[m]).Where(o => o != e).Distinct().ToList();
            if (twins.Count > 0) sb.AppendLine($"{e.Category} / {e.Name}  ->  {string.Join("; ", twins.Select(t => t.Category + " / " + t.Name))}");
        }
        File.WriteAllText(Path.Combine(Dir, "links.txt"), sb.ToString());
        return 0;
    }

    /// <summary>--icons &lt;out.png&gt;: every icon as it looks in game (coloured, dark background), for checking.</summary>
    public static int IconSheet(string outFile)
    {
        const int cell = 96, cols = 8;
        int rows = (Icons.All.Length + cols - 1) / cols;
        using var sheet = new System.Drawing.Bitmap(cols * cell, rows * (cell + 18));
        using var g = System.Drawing.Graphics.FromImage(sheet);
        g.Clear(System.Drawing.Color.FromArgb(24, 28, 34));
        using var font = new System.Drawing.Font("Segoe UI", 8f);
        for (int i = 0; i < Icons.All.Length; i++)
        {
            int x = i % cols * cell, y = i / cols * (cell + 18);
            using var tile = IconPicker.Tile(Icons.All[i], System.Drawing.Color.FromArgb(255, 216, 74), cell);
            g.DrawImage(tile, x, y);
            g.DrawString(Icons.All[i].Name, font, System.Drawing.Brushes.Gray, x + 4, y + cell);
        }
        sheet.Save(outFile);
        return 0;
    }

    static int Compare(Action<string> log, string name, byte[] mine, string testedPath)
    {
        var tested = File.ReadAllBytes(testedPath);
        if (mine.Length != tested.Length) { log($"{name}: length {mine.Length} vs tested {tested.Length}"); return 1; }
        var diffs = Enumerable.Range(0, mine.Length).Where(i => mine[i] != tested[i]).ToList();
        // the colour floats sit at 0x94 in the stock file, shifted by the texture string length change
        int texLen = BitConverter.ToInt32(mine, 0x3C), at = 0x94 + texLen - 27;
        var unexpected = diffs.Where(i => i < at || i >= at + 12).ToList();
        log($"{name}: {(unexpected.Count == 0 ? "same as tested" : "DIFFERENT at " + string.Join(",", unexpected.Select(i => "0x" + i.ToString("x"))))} (colour bytes differ: {diffs.Count - unexpected.Count})");
        return unexpected.Count == 0 ? 0 : 1;
    }
}
