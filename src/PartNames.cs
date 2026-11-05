using System.Text.RegularExpressions;

namespace ItemFinder;

/// <summary>Readable names for the parts list of every entry with several models: the game's own name for the apparel
/// or item using the model ("Pint-Sized Slasher Mask (male)"), else the file name split into words with the game's
/// vocabulary ("halloweencostume03_female_body" = "Halloween Costume 03 Body (female)"). Names set by a category
/// (human enemies, recipes) are kept.</summary>
public static class PartNames
{
    /// <summary>Words the game's names don't have but model files use.</summary>
    static readonly string[] ExtraWords =
    {
        "body", "head", "mask", "helmet", "hat", "hood", "outfit", "costume", "arm", "arms", "leg", "legs", "left", "right", "torso",
        "chest", "tail", "wing", "wings", "eye", "eyes", "glow", "glove", "gloves", "boot", "boots", "hair", "face", "jaw", "teeth",
        "claw", "claws", "lower", "upper", "base", "gun", "lid", "door", "fx", "skin", "shell", "fur", "spine", "neck", "hand", "hands",
        "foot", "feet", "back", "pack", "bag", "belt", "cape", "coat", "suit", "dress", "pants", "shirt", "jacket", "scarf", "mouth",
        "horn", "horns", "beard", "crown", "brain", "dome", "light", "lights", "damaged", "broken", "dead", "burnt", "dirty", "clean",
        "knee", "hip", "shoulder", "humanoid", "plate", "pieces", "liner", "backpack", "radio", "parasitic", "res", "low", "high", "mesh",
        "vertibird", "decal", "decals", "mascot", "animated", "anim", "railing", "rail", "rigged", "emblem", "logo", "nozzle", "barrel", "trim", "charge", "charger", "ascot", "vandalized", "pharm", "pharma", "bark", "cob", "ized",
    };
    /// <summary>File-name words that say nothing to a player.</summary>
    static readonly HashSet<string> Noise = new() { "atx", "go", "nif", "flora", "landscape", "plants", "dlc", "mesh" };
    static readonly Regex Tokens = new(@"[a-z]+|[0-9]+", RegexOptions.Compiled);

    public static void NameAll(GameIndex idx, IEnumerable<Category> cats)
    {
        var gameNames = GameNames(idx);
        var vocab = Vocabulary(idx);
        foreach (var e in cats.SelectMany(c => c.Entries).Where(e => e.Models.Count > 1))
        {
            // a helmet's "face fit" copy (facebones) has no name of its own: the helmet's
            string? GameName(string m) => gameNames.GetValueOrDefault(m)
                ?? (m.Replace("_facebones", "") is var plain && plain != m && gameNames.TryGetValue(plain, out var pn) ? pn + ", face fit" : null);
            var names = e.PartNames != null ? new Dictionary<string, string>(e.PartNames) : new();
            var added = new List<string>();
            // human enemies: the game's apparel names where there are some (their folder-based names are a fallback)
            if (e.Category == Enemies.Humans)
                foreach (var m in e.Models.Where(m => GameName(m) != null)) names.Remove(m);
            foreach (var m in e.Models.Where(m => !names.ContainsKey(m)))
            {
                names[m] = GameName(m) ?? FromFile(m, vocab);
                added.Add(m);
            }
            // two parts with the same name: the words of the file name that the name lacks tell them apart
            foreach (var group in added.GroupBy(m => names[m]).Where(g => g.Count() > 1).ToList())
                foreach (var dup in group)
                {
                    var have = names[dup].ToLowerInvariant().Split(' ', '(', ')', '-', '\'').ToHashSet();
                    var extra = FromFile(dup, vocab, withSex: false).Split(' ').Where(w => !have.Contains(w.ToLowerInvariant())).ToList();
                    if (extra.Count > 0) names[dup] += " · " + string.Join(" ", extra).ToLowerInvariant();
                }
            // still the same (a second version of a part): numbered
            foreach (var group in added.GroupBy(m => names[m]).Where(g => g.Count() > 1).ToList())
                foreach (var (dup, i) in group.Skip(1).Select((m, i) => (m, i)))
                    names[dup] += $" · {i + 2}";
            e.PartNames = names;
        }
    }

    /// <summary>Model -> the name of the apparel using it (ARMO -> ARMA: male model first, female second), or of the world
    /// item with that model. Playable apparel names win (the enemy-only copies are often called "Default").</summary>
    static Dictionary<string, string> GameNames(GameIndex idx)
    {
        var armaUsers = new Dictionary<uint, List<EsmRecord>>();
        foreach (var o in idx.Records.Where(r => r.Type == "ARMO" && r.Name.Length > 0 && r.Parts != null))
            foreach (var p in o.Parts!) (armaUsers.TryGetValue(p, out var l) ? l : armaUsers[p] = new()).Add(o);
        var votes = new Dictionary<string, Dictionary<string, int>>();
        void Vote(string model, string name, int weight)
        {
            if (name.Length == 0 || name.Equals("Default", StringComparison.OrdinalIgnoreCase)) return;
            var d = votes.TryGetValue(model, out var v) ? v : votes[model] = new();
            d[name] = d.GetValueOrDefault(name) + weight;
        }
        var gender = new Dictionary<string, string>();
        foreach (var a in idx.Records.Where(r => r.Type == "ARMA" && r.Models != null))
        {
            var models = a.Models!.Select(Catalog.NormalizeModel).ToList();
            foreach (var (m, i) in models.Select((m, i) => (m, i)))
            {
                if (models.Count >= 2 && i < 2 && models[0] != models[1]) gender[m] = i == 0 ? " (male)" : " (female)";
                foreach (var o in armaUsers.GetValueOrDefault(a.FormId) ?? new())
                    Vote(m, o.Name, o.EditorId.Contains("playable", StringComparison.OrdinalIgnoreCase) ? 1 : 10);
            }
        }
        foreach (var r in idx.Records.Where(r => GameIndex.WorldTypes.Contains(r.Type) && r.Model.Length > 0))
            Vote(Catalog.NormalizeModel(r.Model), r.Name, 5);
        return votes.ToDictionary(kv => kv.Key, kv => kv.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key.Length).First().Key + gender.GetValueOrDefault(kv.Key, ""));
    }

    static HashSet<string> Vocabulary(GameIndex idx)
    {
        var count = new Dictionary<string, int>();
        foreach (var r in idx.Records)
            foreach (Match w in Regex.Matches(r.Name.ToLowerInvariant(), "[a-z]{3,}")) count[w.Value] = count.GetValueOrDefault(w.Value) + 1;
        var words = new HashSet<string>(ExtraWords.Concat(count.Keys));
        // rare run-together names ("MirelurkQueen" in one record) would stop "mirelurkqueen" from splitting; common ones
        // that are real words ("deathclaw") stay
        foreach (var w in count.Where(kv => kv.Value < 3 && kv.Key.Length >= 7).Select(kv => kv.Key).ToList())
            for (int i = 3; i <= w.Length - 3; i++)
                if (words.Contains(w[..i]) && words.Contains(w[i..]) && w[..i] != w && w[i..] != w) { words.Remove(w); break; }
        return words;
    }

    /// <summary>The file name in words: "atx_bearmask_m" = "Bear Mask (male)".</summary>
    static string FromFile(string model, HashSet<string> vocab, bool withSex = true)
    {
        var words = new List<string>();
        string sex = "";
        bool afterDlc = false;
        foreach (Match t in Tokens.Matches(Path.GetFileNameWithoutExtension(model).ToLowerInvariant()))
        {
            var tok = t.Value;
            if (char.IsDigit(tok[0]))
            {
                if (!afterDlc) words.Add(tok.TrimStart('0') is { Length: > 0 } n ? n : "0"); // "dlc01" is a game version, not a part
                afterDlc = false;
                continue;
            }
            afterDlc = tok == "dlc";
            if (Noise.Contains(tok)) continue;
            foreach (var w in Split(tok, vocab))
                if (!Noise.Contains(w)) words.Add(w is "l" ? "left" : w is "r" ? "right" : w);
        }
        // a lone f / m (often stuck on the end: "sheepsquatchmascotf") or female / male = the body type
        for (int i = words.Count - 1; i >= 0; i--)
            if (words[i] is "f" or "female" or "m" or "male")
            {
                sex = words[i].StartsWith('f') ? " (female)" : " (male)";
                words.RemoveAt(i);
                break;
            }
        if (words.Count == 0) return Path.GetFileNameWithoutExtension(model);
        return string.Join(" ", words.Select(w => char.IsDigit(w[0]) ? w : char.ToUpperInvariant(w[0]) + w[1..])) + (withSex ? sex : "");
    }

    /// <summary>Splits a run of letters into known words (fewest pieces; unknown letters cost more and stay together).</summary>
    static List<string> Split(string s, HashSet<string> vocab)
    {
        int n = s.Length;
        var cost = new int[n + 1];
        var from = new int[n + 1];
        var known = new bool[n + 1];
        Array.Fill(cost, int.MaxValue);
        cost[0] = 0;
        for (int i = 0; i < n; i++)
        {
            if (cost[i] == int.MaxValue) continue;
            for (int j = i + 2; j <= Math.Min(n, i + 20); j++)
                if (vocab.Contains(s[i..j]) && cost[i] + 1 < cost[j]) { cost[j] = cost[i] + 1; from[j] = i; known[j] = true; }
            if (cost[i] + 3 < cost[i + 1]) { cost[i + 1] = cost[i] + 3; from[i + 1] = i; known[i + 1] = false; }
        }
        var parts = new List<(string W, bool Known)>();
        for (int j = n; j > 0; j = from[j]) parts.Insert(0, (s[from[j]..j], known[j]));
        // unknown letters next to each other form one word
        var result = new List<string>();
        foreach (var (w, k) in parts)
            if (!k && result.Count > 0 && parts.Count > 0 && result[^1].Length > 0 && !vocab.Contains(result[^1]) && result[^1] is not ("f" or "m"))
                result[^1] += w;
            else result.Add(w);
        return result;
    }
}
