namespace ItemFinder;

/// <summary>"Pick up from afar" (Nexus request, tested in game 2026-11-16, tools\pick-test): the item's collision becomes a
/// static body on layer 15 NONCOLLIDABLE - the activation ray hits it, players and other objects pass through - made of two
/// pieces: the item's own box (so the prompt still shows up close: a ray starting inside a shape doesn't hit it) and a big
/// sphere around it. Built from a vanilla two-box compound collision (the donor): its Havok data is reused with only numbers
/// changed (corners, planes, bounds, body settings), so no Havok writer is needed.</summary>
public static class Pickup
{
    /// <summary>Categories of things you pick up (or harvest / open where they lie): only there the ball is offered and built.
    /// Everything else - enemies, NPCs, CAMP items, containers, doors, furniture, workbenches, event objects - would lose
    /// its solid collision (walk-through walls) and isn't picked up anyway (user, 2026-11-16). An allow-list, so new
    /// categories stay without it until added. The list lives in rules.json (pickup) so it can be changed online
    /// (user, 2026-11-16); this copy is used when the rules in use have none.</summary>
    static readonly string[] BuiltIn =
    {
        "Bobbleheads", "Magazines", "XP recipes", "Plans", "Pre-war food", "Notes & holotapes", "Plants (all)", "Aid & food",
        "Junk & misc", "Junk: *", "Weapons", "Loot bags", "Clothes lying around", "All outfits (ground & display model)",
        "Other: Misc items", "Other: Notes & books", "Other: Plants", "Other: Weapons", "Other: Ammo", "Other: Keys",
        "Other: Apparel on the ground",
    };

    /// <summary>The ball is offered for this entry: its category (English name; "A + B" for entries in several) is on the
    /// list, or the entry itself is, and it isn't blocked.</summary>
    public static bool Allowed(string category, string id)
    {
        var r = Rules.Current.Pickup;
        if (r.Block.Contains(id, StringComparer.OrdinalIgnoreCase)) return false;
        if (r.Entries.Contains(id, StringComparer.OrdinalIgnoreCase)) return true;
        var cats = r.Categories.Count > 0 ? r.Categories : (IList<string>)BuiltIn;
        bool On(string c) => cats.Any(p => p.EndsWith('*') ? c.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)
                                                          : c.Equals(p, StringComparison.OrdinalIgnoreCase));
        return category.Split(" + ").Any(On);
    }

    /// <summary>A vanilla model whose collision is a compound of 16 8-corner boxes (the item's box + up to 15 balls).</summary>
    public const string DonorModel = @"meshes\furniture\primitive\primitive_chair01.nif";
    // Nested balls: inside a ball its own surface can't be aimed at (a ray starting inside a shape doesn't hit it - the
    // gap the user found with one 15 m ball, 2026-11-16), so the next smaller ball must be well within reach (~2.1 m).
    const int NonCollidable = 15;
    /// <summary>Game units per Havok unit (FO4 / FO76 Havok scale 0.0142875).</summary>
    public const float UnitsPerHavok = 69.99125f;

    /// <summary>The donor's Havok data (the bhkPhysicsSystem block without its length), or null when it isn't one.</summary>
    /// <summary>How many pieces a usable donor blob has (dev scan --pickdonors).</summary>
    public static int Pieces(byte[] blob) { var (d, items) = Read(blob); return items.Single(x => x.Type == "hkFreeListArrayElement").Count; }

    public static byte[]? DonorBlob(byte[] nif)
    {
        var f = NifFile.Parse(nif);
        var b = f.Blocks.FirstOrDefault(x => x.Type == "bhkPhysicsSystem").Data;
        if (b == null || b.Length < 8) return null;
        var blob = b.AsSpan(4, Math.Min(BitConverter.ToInt32(b, 0), b.Length - 4)).ToArray();
        Make(blob, (0, 0, 0), (0.1f, 0.1f, 0.1f), 1); // throws (with the reason) on an unexpected layout
        return blob;
    }

    /// <summary>The donor's data with piece 1 = the box min..max and piece 2 = a sphere of <paramref name="radius"/> round its
    /// centre (all in Havok units, model space). Throws when the donor isn't laid out as expected.</summary>
    public static byte[] Make(byte[] donor, (float X, float Y, float Z) min, (float X, float Y, float Z) max, float radius)
    {
        var bl = (byte[])donor.Clone();
        var (data, items) = Read(bl);
        int D = data;
        (int Off, int Count) One(string type) { var i = items.Single(x => x.Type == type); return (D + i.Off, i.Count); }
        var inst = One("hkFreeListArrayElement");
        if (inst.Count < 2) throw new InvalidDataException("donor: fewer than two pieces");
        float[] c = { (min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2 };
        float[] half = { Math.Max(0.01f, (max.X - min.X) / 2), Math.Max(0.01f, (max.Y - min.Y) / 2), Math.Max(0.01f, (max.Z - min.Z) / 2) };
        // piece 0 = the item's box; then nested balls from the outside in, evenly spaced - ALL spare pieces used (at 1.5 m
        // apart the pick-up dropped out after crossing each shell, user 2026-11-16: the next ball was at the edge of reach)
        int balls = Math.Max(1, inst.Count - 1);
        float Ball(int n) => n >= 1 && n <= balls ? radius * (balls - n + 1) / balls : 0;
        for (int n = 0; n < inst.Count; n++)
        {
            int io = inst.Off + 128 * n;
            // the piece's own placement: none (no turn, no offset, scale 1), corners are in model space; w values kept (Havok
            // packs flags there)
            W3(bl, io, 1, 0, 0); W3(bl, io + 16, 0, 1, 0); W3(bl, io + 32, 0, 0, 1);
            W3(bl, io + 48, 0, 0, 0);
            W3(bl, io + 64, 1, 1, 1);
            int shape = BitConverter.ToInt32(bl, io + 80);
            if (shape <= 0 || shape >= items.Count || items[shape].Type != "hknpConvexPolytopeShape") throw new InvalidDataException("donor: piece is not a box");
            int so = D + items[shape].Off;
            var verts = items.FirstOrDefault(i => i.Type == "hkVector4f" && D + i.Off == so + 96);
            var planes = items.Where(i => i.Type == "hkVector4" && i.Off > verts.Off).OrderBy(i => i.Off).FirstOrDefault();
            if (verts.Count != 8 || planes.Type == null) throw new InvalidDataException("donor: piece is not an 8-corner box");
            // which corner is which: the donor's own corners against their middle
            var old = Enumerable.Range(0, 8).Select(k => R3(bl, D + verts.Off + 16 * k)).ToArray();
            float[] mid = { old.Average(v => v[0]), old.Average(v => v[1]), old.Average(v => v[2]) };
            // piece 2: a tiny box with a convex radius = a sphere (Havok rounds every shape by its convex radius)
            float[] h = Ball(n) == 0 ? half : new[] { 0.01f, 0.01f, 0.01f };
            var now = old.Select(v => Enumerable.Range(0, 3).Select(a => c[a] + (v[a] > mid[a] ? h[a] : -h[a])).ToArray()).ToArray();
            for (int k = 0; k < 8; k++) W3(bl, D + verts.Off + 16 * k, now[k][0], now[k][1], now[k][2]);
            for (int k = 0; k < planes.Count; k++)
            {
                var p = R3(bl, D + planes.Off + 16 * k);
                float far = now.Max(v => p[0] * v[0] + p[1] * v[1] + p[2] * v[2]);
                BitConverter.TryWriteBytes(bl.AsSpan(D + planes.Off + 16 * k + 12), -far);
            }
            BitConverter.TryWriteBytes(bl.AsSpan(so + 24), Ball(n) > 0 ? Ball(n) : 0.01f); // hknpShape.convexRadius (the donor's own on a box)
        }
        // bounds of the whole compound and of every node of its tree: round both pieces (loose is fine)
        float[] lo = new float[3], hi = new float[3];
        for (int a = 0; a < 3; a++) { lo[a] = Math.Min(c[a] - half[a], c[a] - radius) - 0.05f; hi[a] = Math.Max(c[a] + half[a], c[a] + radius) + 0.05f; }
        var comp = One("hknpCompoundShape");
        void Aabb(int o) { W3(bl, o, lo[0], lo[1], lo[2]); W3(bl, o + 16, hi[0], hi[1], hi[2]); } // w kept (tree links)
        Aabb(comp.Off + 144);
        foreach (var t in items.Where(i => i.Type == "hkcdDynamicTree::Codec32"))
            for (int k = 0; k < t.Count; k++)
            {
                int o = D + t.Off + 32 * k;
                if (R3(bl, o).Any(v => v != 0) || R3(bl, o + 16).Any(v => v != 0)) Aabb(o);
            }
        // the body: static (like static vanilla food), on NONCOLLIDABLE
        var body = One("hknpPhysicsSystemData::bodyCinfoWithAttachment");
        int bo = body.Off;
        uint filter = BitConverter.ToUInt32(bl, bo + 16);
        BitConverter.TryWriteBytes(bl.AsSpan(bo + 16), (filter & ~0x7Fu) | NonCollidable);
        BitConverter.TryWriteBytes(bl.AsSpan(bo + 8), 0);          // flags
        bl[bo + 40] = 0;                                            // motion type: static
        W3(bl, bo + 48, c[0], c[1], c[2]);                          // position
        BitConverter.TryWriteBytes(bl.AsSpan(bo + 112), -1f);       // mass
        BitConverter.TryWriteBytes(bl.AsSpan(bo + 120), 0L);        // mass distribution: none
        BitConverter.TryWriteBytes(bl.AsSpan(bo + 128), (ushort)0xFFFF); // motion properties: none
        return bl;
    }

    static float[] R3(byte[] b, int o) => new[] { BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8) };
    static void W3(byte[] b, int o, float x, float y, float z)
    {
        BitConverter.TryWriteBytes(b.AsSpan(o), x); BitConverter.TryWriteBytes(b.AsSpan(o + 4), y); BitConverter.TryWriteBytes(b.AsSpan(o + 8), z);
    }

    /// <summary>A Havok tag file's DATA start and its items (type name, offset in DATA, count). Item 0 is the null item.</summary>
    static (int Data, List<(string Type, int Off, int Count)> Items) Read(byte[] b)
    {
        var sec = new Dictionary<string, (int Start, int End)>();
        void Walk(int o, int end)
        {
            while (o + 8 <= end)
            {
                int size = (int)(((uint)b[o] << 24 | (uint)b[o + 1] << 16 | (uint)b[o + 2] << 8 | b[o + 3]) & 0x3FFFFFFF);
                if (size < 8) throw new InvalidDataException("bad Havok section");
                string tag = System.Text.Encoding.ASCII.GetString(b, o + 4, 4);
                sec.TryAdd(tag, (o + 8, o + size));
                if (tag is "TAG0" or "TYPE" or "INDX") Walk(o + 8, o + size);
                o += size;
            }
        }
        Walk(0, b.Length);
        var strs = new List<string>();
        var (ts, te) = sec["TSTR"];
        for (int o = ts; o < te;)
        {
            int z = Array.IndexOf(b, (byte)0, o, te - o);
            if (z < 0) break;
            strs.Add(System.Text.Encoding.ASCII.GetString(b, o, z - o));
            o = z + 1;
        }
        int p = sec["TNAM"].Start;
        int V()
        {
            int x = b[p];
            if ((x & 0x80) == 0) { p += 1; return x; }
            if ((x & 0xC0) == 0x80) { p += 2; return ((x << 8) | b[p - 1]) & 0x3FFF; }
            if ((x & 0xE0) == 0xC0) { p += 3; return ((x << 16) | (b[p - 2] << 8) | b[p - 1]) & 0x1FFFFF; }
            if ((x & 0xF8) == 0xE0) { p += 4; return (int)((((uint)x << 24) | ((uint)b[p - 3] << 16) | ((uint)b[p - 2] << 8) | b[p - 1]) & 0x7FFFFFF); }
            if ((x & 0xF8) == 0xE8) { p += 5; return (int)((((long)b[p - 4] << 24) | ((long)b[p - 3] << 16) | ((long)b[p - 2] << 8) | b[p - 1]) & 0x7FFFFFFF); } // (template values)
            throw new InvalidDataException("bad Havok number");
        }
        int nTypes = V();
        var names = new string[nTypes];
        names[0] = "";
        for (int i = 1; i < nTypes; i++)
        {
            names[i] = strs[V()];
            int tc = V();
            for (int k = 0; k < tc; k++) { V(); V(); }
        }
        var items = new List<(string, int, int)>();
        var (ist, ien) = sec["ITEM"];
        for (int o = ist; o + 12 <= ien; o += 12)
        {
            int t = (int)(BitConverter.ToUInt32(b, o) & 0xFFFFFF);
            items.Add((t > 0 && t < nTypes ? names[t] : "", BitConverter.ToInt32(b, o + 4), BitConverter.ToInt32(b, o + 8)));
        }
        return (sec["DATA"].Start, items);
    }
}
