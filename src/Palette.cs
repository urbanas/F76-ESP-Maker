using System.Text.RegularExpressions;

namespace ItemFinder;

/// <summary>Enemy colours: each creature family has its own base colour (ESP Maker's scheme), and each creature in it
/// a shade of it - a small hue / brightness step worked out from its name, so the same creature always gets the same
/// shade and neighbours in a family still look a little different. Enemies stay out of the hues items use (greens
/// 120-165 deg, blue-violets 225-265 deg - see Catalog).</summary>
public static class Palette
{
    static string Key(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z]", "");

    /// <summary>A family: base hue (degrees), saturation, brightness.</summary>
    public sealed record Family(double Hue, double Sat, double Val)
    {
        public string Hex => Hsv(Hue, Sat, Val);
    }

    public static readonly Family Robots = new(214, 0.45, 1.00);       // cool steel blue
    public static readonly Family Scorched = new(26, 0.92, 1.00);      // amber orange
    public static readonly Family Scorchbeasts = new(338, 0.78, 1.00); // raspberry
    public static readonly Family Ghouls = new(44, 0.95, 1.00);        // gold
    public static readonly Family Mutants = new(92, 0.85, 0.95);       // lime (yellower than the item greens)
    public static readonly Family Cryptids = new(296, 0.70, 1.00);     // magenta-violet
    public static readonly Family Bugs = new(60, 0.80, 1.00);          // lemon
    public static readonly Family Water = new(170, 0.90, 0.88);        // jade
    public static readonly Family Hunters = new(200, 0.85, 0.95);      // hostile animals: azure
    public static readonly Family Docile = new(186, 0.30, 1.00);       // harmless animals: pale ice
    public static readonly Family Humans = new(356, 0.90, 0.95);       // raiders, Lost, mercs: deep red
    public static readonly Family Cult = new(318, 0.55, 1.00);         // cultists: orchid
    public static readonly Family Overgrown = new(72, 0.90, 0.85);     // mole miners, Overgrown: chartreuse
    public static readonly Family Others = new(280, 0.45, 1.00);       // every other creature: lilac

    /// <summary>Which family each creature (entry name, letters only) belongs to.</summary>
    static readonly Dictionary<string, Family> Members = new();

    static Palette()
    {
        void Add(Family f, params string[] names) { foreach (var n in names) Members[n] = f; }
        Add(Robots, "assaultron", "protectron", "sentrybot", "mrhandy", "robobrain", "eyebot", "liberator", "asam", "cargobot",
            "vertibot", "militaryturret", "bubbleturret", "enclaveturret", "zetandrone", "guardianen");
        Add(Mutants, "supermutant", "behemoth", "behemothboss", "rustking");
        Add(Scorched, "scorched", "ultraciteterror");
        Add(Scorchbeasts, "scorchbeast", "scorchbeastqueen", "stormgoliaths");
        Add(Ghouls, "feralghoul");
        Add(Cryptids, "mothman", "wisemothman", "snallygaster", "graftonmonster", "flatwoodsmonster", "sheepsquatch",
            "impostersheepsquatch", "jerseydevil", "bluedevil", "lesserdevil", "ogua", "bigfoot", "alien", "deathclaw");
        Add(Bugs, "bloatfly", "bloodbug", "stingwing", "radroach", "radant", "cavecricket", "honeybeast", "firefly");
        Add(Water, "mirelurk", "mirelurkhunter", "mirelurkking", "mirelurkqueen", "mirelurkspawn", "hermitcrab", "radscorpion",
            "gulper", "fogcrawler", "angler", "megasloth", "floater");
        Add(Hunters, "molerat", "wolf", "radrat", "radhog", "attackdog", "fevhound");
        Add(Docile, "radstag", "radtoad", "radturkey", "pheasant", "brahmin", "chicken", "rabbit", "squirrel", "fox", "frog",
            "opossum", "owl", "beaver");
        Add(Humans, "lost", "rustkingdomraiders", "raiders", "gasmaskraiders", "bloodeaglemercs", "communists", "fanatics");
        Add(Cult, "cultists");
        Add(Overgrown, "moleminer", "overgrownpollinator", "overgrownthorn", "overgrowntank");
    }

    /// <summary>The colour for an enemy, or null when it's in no family. (Items don't use these: they would collide with
    /// enemies; items get the free hues, see Catalog.)</summary>
    public static string? EnemyColour(Entry e)
    {
        var key = Key(e.Name);
        if (!Members.TryGetValue(key, out var f)) return null;
        // the shade: up to +-8 degrees of hue and a little darker, from a stable hash of the name
        uint h = 2166136261;
        foreach (var ch in key) h = (h ^ ch) * 16777619;
        double hue = f.Hue + ((int)(h % 17) - 8);
        double val = f.Val * (1.0 - (h / 17 % 5) * 0.03);
        return Hsv(hue, f.Sat, val);
    }

    /// <summary>"#RRGGBB" from hue (degrees), saturation and brightness (0-1).</summary>
    public static string Hsv(double hue, double sat, double val)
    {
        hue = ((hue % 360) + 360) % 360;
        double c = val * sat, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = val - c;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        int B(double v) => (int)Math.Round((v + m) * 255);
        return $"#{B(r):X2}{B(g):X2}{B(b):X2}";
    }

    /// <summary>The enemy category colours before ESP Maker 1.0 (setups still on one of them move to the new one).</summary>
    public static readonly Dictionary<string, (string Old, string Now)> OldCategoryColours = new()
    {
        ["Robots"] = ("#2BB8FF", Robots.Hex), ["Scorched"] = ("#FF642F", Scorched.Hex), ["Super Mutants"] = ("#55E84A", Mutants.Hex),
        ["Feral Ghouls"] = ("#FF9A3D", Ghouls.Hex), ["Cryptids"] = ("#B54CFF", Cryptids.Hex), ["Insects & bugs"] = ("#FFE24A", Bugs.Hex),
        ["Mirelurks & crabs"] = ("#35DDE0", Water.Hex), ["Hostile animals"] = ("#5BC9E8", Hunters.Hex),
        ["Harmless animals"] = ("#66DFC7", Docile.Hex), ["Other creatures"] = ("#A0A0FF", Others.Hex), ["Human enemies"] = ("#FF4B32", Humans.Hex),
    };
}
