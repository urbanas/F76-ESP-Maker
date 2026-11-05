namespace ItemFinder;

public sealed class Settings
{
    /// <summary>Game folder override (when Steam can't find it).</summary>
    public string? GameFolder { get; set; }
    /// <summary>Category tree sections the user folded.</summary>
    public List<string> Collapsed { get; set; } = new();
    /// <summary>Main window place and size (normal state), whether it was maximised, and the category column width.</summary>
    public int[]? Window { get; set; }
    public bool Maximized { get; set; }
    public int LeftWidth { get; set; } = 300;
    /// <summary>Width of the look panel on the right.</summary>
    public int LookWidth { get; set; } = 430;
    /// <summary>2 = the three-pane window (list + look panel): older window sizes were for the narrower layout.</summary>
    public int Layout { get; set; }
    /// <summary>The tour was shown once (a new user's first start).</summary>
    public bool TourSeen { get; set; }

    static string FilePath => AppPaths.File("settings.json");

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return Json.Read<Settings>(FilePath) ?? new(); } catch { }
        return new();
    }

    public void Save() { try { Json.Write(FilePath, this); } catch { } }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
#if DEVTOOLS // the dev and test commands (not in the release build: publish with -p:DevTools=false)
        // the test commands rewrite the setup they run on: never on a user's real data (only a portable test copy)
        if (args.Length > 0 && args[0].StartsWith("--") && !AppPaths.Portable)
        {
            Console.Error.WriteLine("Dev commands only run in a portable copy (portable.txt next to the exe) - they would change the real setup.");
            Environment.Exit(2);
            return;
        }
        if (args.Length == 3 && args[0] == "--picker")
        {
            ApplicationConfiguration.Initialize();
            IconPicker.CaptureTo = args[1];
            IconPicker.Pick(null!, args[2] == "models" ? "model:pumpkin" : "star", args[2] == "3d", Color.FromArgb(255, 216, 74), args[2] == "only3d");
            return;
        }
        if (args.Length == 2 && args[0] == "--models")
        {
            // dev: every 3D model icon, big, from two sides
            const int T = 200;
            using var sheet = new Bitmap(T * 8, T * 2 * ((Models3D.All.Length + 7) / 8));
            using (var g = Graphics.FromImage(sheet))
                for (int i = 0; i < Models3D.All.Length; i++)
                    foreach (var (side, yaw) in new[] { (0, -0.5f), (1, 2.3f) })
                    {
                        using var tile = Models3D.Tile(Models3D.All[i], Color.FromArgb(255, 216, 74), T, yaw: yaw);
                        g.DrawImage(tile, (i % 8) * T, (i / 8 * 2 + side) * T);
                    }
            sheet.Save(args[1]);
            return;
        }
        if (args.Length is 3 or 4 && args[0] == "--preview") { Environment.Exit(SelfTest.Preview(args[1], args[2], args.Length == 4 ? args[3] : null)); return; }
        if (args.Length == 3 && args[0] == "--scene") { Environment.Exit(SelfTest.Scene(args[1], args[2])); return; }
        if (args.Length == 2 && args[0] == "--partnames") { Environment.Exit(SelfTest.PartNamesDump(args[1])); return; }
        if (args.Length == 3 && args[0] == "--tex") { Environment.Exit(SelfTest.Tex(args[1], args[2])); return; }
        if (args.Length is 3 or 4 && args[0] == "--lookshot") { Environment.Exit(SelfTest.LookShot(args[1], args[2], args.Length == 4 ? args[3] : null)); return; }
        if (args.Length == 2 && args[0] == "--tour-pages")
        {
            // dev: the tour window, one picture per step (for the mod page)
            ApplicationConfiguration.Initialize();
            using var tour = new Tutorial(new Font("Segoe UI", 9f)) { StartPosition = FormStartPosition.CenterScreen };
            tour.Shown += (_, _) => { tour.SavePages(args[1]); Environment.Exit(0); };
            Application.Run(tour);
            return;
        }
        if (args.Length == 2 && args[0] == "--tutorial-shots")
        {
            // dev: the tour's pictures from the real window (into src\Tutorial, then rebuild)
            ApplicationConfiguration.Initialize();
            var form = new MainForm { StartPosition = FormStartPosition.CenterScreen, Size = new Size(1500, 930) };
            var wait = new System.Windows.Forms.Timer { Interval = 300 };
            wait.Tick += async (_, _) => { if (!form.Ready) return; wait.Stop(); await form.MakeTourShots(args[1]); Environment.Exit(0); };
            form.Shown += (_, _) => wait.Start();
            Application.Run(form);
            return;
        }
        if (args.Length == 2 && args[0] == "--uitest")
        {
            // dev: drive the window and check what gets stored (uses - and resets - the setup next to the exe)
            ApplicationConfiguration.Initialize();
            var form = new MainForm { StartPosition = FormStartPosition.CenterScreen, Size = new Size(1500, 930) };
            var wait = new System.Windows.Forms.Timer { Interval = 300 };
            wait.Tick += (_, _) => { if (!form.Ready) return; wait.Stop(); form.RunUiTest(args[1]); Environment.Exit(0); };
            form.Shown += (_, _) => wait.Start();
            Application.Run(form);
            return;
        }
        if (args.Length is 2 or 3 or 4 && args[0] == "--mainshot")
        {
            // dev: picture of the main window once the game data is loaded; exits without saving the window place
            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => File.AppendAllText(AppPaths.File("error.log"), $"{e.Exception}{Environment.NewLine}");
            var form = new MainForm { StartPosition = FormStartPosition.CenterScreen, Size = new Size(1280, 960) };
            var t = new System.Windows.Forms.Timer { Interval = 12000 };
            t.Tick += (_, _) => { using var bmp = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size)); bmp.Save(args[1]); Environment.Exit(0); };
            if (args.Length >= 3) // a category to show (and an entry to open), shortly before the picture
            {
                var pick = new System.Windows.Forms.Timer { Interval = 9000 };
                pick.Tick += (_, _) => { pick.Stop(); form.DevShow(args[2], args.Length == 4 ? args[3] : null); };
                form.Shown += (_, _) => pick.Start();
            }
            form.Shown += (_, _) => t.Start();
            Application.Run(form);
            return;
        }
        if (args.Length == 1 && args[0] == "--list") { Environment.Exit(SelfTest.List()); return; }
        if (args.Length == 2 && args[0] == "--icons") { Environment.Exit(SelfTest.IconSheet(args[1])); return; }
        if (args.Length == 1 && args[0] == "--links") { Environment.Exit(SelfTest.Links()); return; }
        if (args.Length == 2 && args[0] == "--brightentest") { Environment.Exit(SelfTest.BrightenTest(args[1])); return; }
        if (args.Length is 1 or 2 && args[0] == "--stress") { Environment.Exit(SelfTest.Stress(args.Length == 2 ? args[1] : null)); return; }
        if (args.Length is 1 or 2 && args[0] == "--previewall") { Environment.Exit(SelfTest.PreviewAll(args.Length == 2 ? args[1] : null)); return; }
        if (args.Length == 2 && args[0] == "--installtest") { Environment.Exit(SelfTest.InstallTest(args[1])); return; }
        if (args.Length == 3 && args[0] == "--setuptest")
        {
            // dev: every saved setup in a folder through what opening it does (uses the setup next to the exe)
            ApplicationConfiguration.Initialize();
            var form = new MainForm { StartPosition = FormStartPosition.CenterScreen, Size = new Size(1500, 930) };
            var wait = new System.Windows.Forms.Timer { Interval = 300 };
            wait.Tick += (_, _) => { if (!form.Ready) return; wait.Stop(); form.RunSetupTest(args[1], args[2]); Environment.Exit(0); };
            form.Shown += (_, _) => wait.Start();
            Application.Run(form);
            return;
        }
        if (args.Length >= 3 && args[0] == "--importtest")
        {
            // dev: every .ba2 through Import and a build of the result (uses the setup next to the exe)
            ApplicationConfiguration.Initialize();
            var form = new MainForm { StartPosition = FormStartPosition.CenterScreen, Size = new Size(1500, 930) };
            var wait = new System.Windows.Forms.Timer { Interval = 300 };
            wait.Tick += async (_, _) => { if (!form.Ready) return; wait.Stop(); await form.RunImportTest(args[1], args[2..]); Environment.Exit(0); };
            form.Shown += (_, _) => wait.Start();
            Application.Run(form);
            return;
        }
        if (args.Length == 3 && args[0] == "--build") { Environment.Exit(SelfTest.Build(args[1], args[2])); return; }
        if (args.Length == 3 && args[0] == "--selftest") { Environment.Exit(SelfTest.Run(args[1], args[2])); return; }
#endif
        ApplicationConfiguration.Initialize();
        AppPaths.MoveOldFiles(note => MainForm.StartNotes.Add(note));
        Application.ThreadException += (_, e) =>
        {
            // the full error in the data folder, for reporting
            var log = AppPaths.File("error.log");
            try { File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e.Exception}{Environment.NewLine}{Environment.NewLine}"); } catch { }
            MessageBox.Show($"Something went wrong: {e.Exception.Message}\n\nThe details are in {log} - please attach that file when you report it.",
                            "ESP Maker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        // errors outside the window's thread (background work) end the app: at least log them
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { File.AppendAllText(AppPaths.File("error.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}"); } catch { }
        };
        Application.Run(new MainForm());
    }
}
