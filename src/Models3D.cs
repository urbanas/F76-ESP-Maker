using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace ItemFinder;

/// <summary>A vertex of a 3D model icon: position, normal, texture position, colour (the colour multiplies the texture).</summary>
public readonly record struct MVert(Vector3 P, Vector3 N, float U, float V, Color C);

/// <summary>A small triangle mesh. Model space: z up, standing on z = 0, about 2 units tall, centred on x/y 0.</summary>
public sealed class Mesh
{
    public readonly List<MVert> V = new();
    public readonly List<(int, int, int)> T = new();
}

/// <summary>A 3D model icon: real geometry with its own texture, like the Spooky Scorched Beacons pumpkin.</summary>
/// <param name="Tinted">Drawn in the look's colour (star, heart...); otherwise in its own colours (pumpkin, skull...).</param>
public sealed record Model3D(string Id, string Name, bool Tinted, Func<Mesh> Build, Func<Bitmap> Texture, bool Shade = true);

/// <summary>The 3D model icons. Ids are "model:..." in Style.Icon (flat icons and 3D shapes use the icon ids).</summary>
public static class Models3D
{
    public const string Prefix = "model:";
    static readonly Vector3 Light = Vector3.Normalize(new(-0.45f, -0.6f, 0.75f));

    public static readonly Model3D[] All =
    {
        new("model:pumpkin", "Pumpkin", false, Pumpkin, () => Embedded("pumpkin_d.dds"), Shade: false),
        new("model:clown", "Clown mask", false, Clown, () => Embedded("slasher_d.dds"), Shade: false),
        new("model:skull", "Skull", false, Skull, SkullTexture),
        new("model:bobblehead", "Bobblehead", false, Bobblehead, BobbleTexture),
        new("model:cap", "Bottle cap", false, () => Coin(ph => 1 - 0.09f * MathF.Abs(MathF.Sin(10.5f * ph))), CapTexture),
        new("model:radiation", "Radiation", false, () => Coin(ph => 1), RadiationTexture),
        new("model:crown", "Crown", false, Crown, White),
        new("model:ghost", "Ghost", false, Ghost, GhostTexture),
        new("model:mushroom", "Mushroom", false, Mushroom, MushroomTexture),
        new("model:star", "Star", true, Star, White),
        new("model:heart", "Heart", true, Heart, White),
        new("model:gem", "Gem", true, Gem, White),
        new("model:exclamation", "Exclamation", true, Exclamation, White),
        new("model:question", "Question", true, Question, White),
        new("model:cross", "Cross", true, Cross, White),
        new("model:leaf", "Leaf", true, Leaf, White),
        // Fallout
        new("model:nuka", "Nuka bottle", false, NukaBottle, NukaTexture),
        new("model:mininuke", "Mini nuke", false, MiniNuke, White),
        new("model:stimpak", "Stimpak", false, Stimpak, White),
        new("model:fusioncore", "Fusion core", false, FusionCore, White),
        new("model:vaultdoor", "Vault door", false, () => Coin(ph => 0.92f + 0.08f * MathF.Tanh(5 * MathF.Sin(12 * ph))), VaultDoorTexture),
        new("model:atom", "Atom", true, Atom, White),
        new("model:lunchbox", "Lunchbox", false, Lunchbox, LunchboxTexture),
        new("model:mrhandy", "Mr. Handy", false, MrHandy, White),
        // anime-style
        new("model:neko", "Neko", false, Neko, NekoTexture),
        new("model:onigiri", "Onigiri", false, Onigiri, OnigiriTexture),
        new("model:sakura", "Sakura", false, Sakura, White),
        new("model:daruma", "Daruma", false, Daruma, DarumaTexture),
        new("model:kitsune", "Kitsune mask", false, Kitsune, KitsuneTexture),
        new("model:slime", "Slime", true, Slime, SlimeTexture),
        new("model:ramen", "Ramen", false, Ramen, RamenTexture),
    };

    public static Model3D? Get(string id) => id.StartsWith(Prefix) ? All.FirstOrDefault(m => m.Id == id) : null;

    static readonly Dictionary<string, Mesh> meshes = new();

    /// <summary>The model's mesh: shaded by its vertex colours (lit from above-front) unless it keeps flat colours.</summary>
    public static Mesh MeshOf(Model3D m)
    {
        lock (meshes)
        {
            if (meshes.TryGetValue(m.Id, out var cached)) return cached;
            var mesh = m.Build();
            // stand on z = 0
            float minZ = mesh.V.Min(v => v.P.Z);
            for (int i = 0; i < mesh.V.Count; i++)
            {
                var v = mesh.V[i];
                if (!(v.N.LengthSquared() > 1e-12f)) v = v with { N = Vector3.UnitZ }; // a point where faces cancel out
                var c = v.C;
                if (m.Shade)
                {
                    float k = 0.58f + 0.42f * Math.Max(0, Vector3.Dot(Vector3.Normalize(v.N), Light)) + 0.12f * Math.Max(0, v.N.Z);
                    c = Color.FromArgb(c.A, Math.Min(255, (int)(c.R * k)), Math.Min(255, (int)(c.G * k)), Math.Min(255, (int)(c.B * k)));
                }
                mesh.V[i] = v with { P = v.P with { Z = v.P.Z - minZ }, C = c };
            }
            return meshes[m.Id] = mesh;
        }
    }

    static readonly Dictionary<string, Rgba> textures = new();

    /// <summary>The model's texture as RGBA (for the preview) - the same picture the mod ships as a DDS.</summary>
    public static Rgba TextureOf(Model3D m)
    {
        lock (textures)
        {
            if (textures.TryGetValue(m.Id, out var t)) return t;
            using var bmp = m.Texture();
            return textures[m.Id] = Bitmaps.ToRgba(bmp);
        }
    }

    /// <summary>The DDS the mod ships (the original file for the ported Spooky Scorched textures).</summary>
    public static byte[] Dds(Model3D m)
    {
        if (m.Id is "model:pumpkin") return EmbeddedBytes("pumpkin_d.dds");
        if (m.Id is "model:clown") return EmbeddedBytes("slasher_d.dds");
        using var bmp = m.Texture();
        return Icons.Dds(bmp);
    }

    public static string TexturePath(Model3D m) => $@"textures\itemfinder\model_{m.Id[Prefix.Length..]}.dds";
    public static string MaterialTexture(Model3D m) => $"itemfinder/model_{m.Id[Prefix.Length..]}.dds";

    /// <summary>The model as a picture (picker tiles, the look sketch): turned a little, seen slightly from above, in its
    /// material's colours (texture x vertex colour x colour) on the dark night background, or see-through when
    /// <paramref name="background"/> is transparent.</summary>
    public static Bitmap Tile(Model3D m, Color colour, int size, Color? background = null, float yaw = -0.5f)
    {
        var bg = background ?? Color.FromArgb(24, 28, 34);
        var mesh = MeshOf(m);
        var tex = TextureOf(m);
        var col = m.Tinted ? colour : Color.White;
        const int AA = 3;
        int n = size * AA;
        float pitch = 0.32f, cy = MathF.Cos(yaw), sy = MathF.Sin(yaw), cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        var eye = new Vector3(0, -cp, sp);           // towards the viewer
        var up = new Vector3(0, sp, cp);
        Vector3 Turn(Vector3 p) => new(p.X * cy - p.Y * sy, p.X * sy + p.Y * cy, p.Z);
        var pts = mesh.V.Select(v => Turn(v.P)).ToArray();
        float u0 = pts.Min(p => p.X), u1 = pts.Max(p => p.X), v0 = pts.Min(p => Vector3.Dot(p, up)), v1 = pts.Max(p => Vector3.Dot(p, up));
        float scale = n * 0.86f / Math.Max(u1 - u0, v1 - v0);
        float ox = n / 2f - (u0 + u1) / 2 * scale, oy = n / 2f + (v0 + v1) / 2 * scale;
        var sx = pts.Select(p => ox + p.X * scale).ToArray();
        var sy2 = pts.Select(p => oy - Vector3.Dot(p, up) * scale).ToArray();
        var dz = pts.Select(p => -Vector3.Dot(p, eye)).ToArray(); // smaller = nearer
        var depth = new float[n * n];
        Array.Fill(depth, float.MaxValue);
        var rgba = new float[n * n * 4];
        foreach (var (a, b, c) in mesh.T)
        {
            if (Vector3.Dot(Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]), eye) <= 0) continue; // back face
            float area = (sx[b] - sx[a]) * (sy2[c] - sy2[a]) - (sx[c] - sx[a]) * (sy2[b] - sy2[a]);
            if (MathF.Abs(area) < 1e-6f) continue;
            int x0 = Math.Max(0, (int)Math.Min(sx[a], Math.Min(sx[b], sx[c]))), x1 = Math.Min(n - 1, (int)Math.Max(sx[a], Math.Max(sx[b], sx[c])) + 1);
            int y0 = Math.Max(0, (int)Math.Min(sy2[a], Math.Min(sy2[b], sy2[c]))), y1 = Math.Min(n - 1, (int)Math.Max(sy2[a], Math.Max(sy2[b], sy2[c])) + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float wa = ((sx[b] - px) * (sy2[c] - py) - (sx[c] - px) * (sy2[b] - py)) / area;
                    float wb = ((sx[c] - px) * (sy2[a] - py) - (sx[a] - px) * (sy2[c] - py)) / area;
                    float wc = 1 - wa - wb;
                    if (wa < 0 || wb < 0 || wc < 0) continue;
                    float z = wa * dz[a] + wb * dz[b] + wc * dz[c];
                    int i = y * n + x;
                    if (z >= depth[i]) continue;
                    var (A, B, C) = (mesh.V[a], mesh.V[b], mesh.V[c]);
                    var (tr, tg, tb, ta) = Sample(tex, wa * A.U + wb * B.U + wc * C.U, wa * A.V + wb * B.V + wc * C.V);
                    if (ta < 0.05f) continue;
                    depth[i] = z;
                    float k = 1.15f / 255f;
                    rgba[i * 4] = Math.Min(1, tr * (wa * A.C.R + wb * B.C.R + wc * C.C.R) * col.R / 255f * k);
                    rgba[i * 4 + 1] = Math.Min(1, tg * (wa * A.C.G + wb * B.C.G + wc * C.C.G) * col.G / 255f * k);
                    rgba[i * 4 + 2] = Math.Min(1, tb * (wa * A.C.B + wb * B.C.B + wc * C.C.B) * col.B / 255f * k);
                    rgba[i * 4 + 3] = ta;
                }
        }
        // down to the tile size (averaging AA x AA samples) over the background
        var outPx = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = 0, g = 0, b = 0, al = 0;
                for (int j = 0; j < AA; j++)
                    for (int i = 0; i < AA; i++)
                    {
                        int q = ((y * AA + j) * n + x * AA + i) * 4;
                        float w = rgba[q + 3];
                        r += rgba[q] * w; g += rgba[q + 1] * w; b += rgba[q + 2] * w; al += w;
                    }
                float cov = al / (AA * AA);
                float bgA = bg.A / 255f, outA = cov + bgA * (1 - cov);
                int o = (y * size + x) * 4;
                float R = al > 0 ? r / al : 0, G = al > 0 ? g / al : 0, Bc = al > 0 ? b / al : 0;
                float Mix(float fg, int back) => outA > 0 ? (fg * cov + back / 255f * bgA * (1 - cov)) / outA : 0;
                outPx[o] = (byte)(Mix(Bc, bg.B) * 255); outPx[o + 1] = (byte)(Mix(G, bg.G) * 255); outPx[o + 2] = (byte)(Mix(R, bg.R) * 255);
                outPx[o + 3] = (byte)(outA * 255);
            }
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < size; y++) System.Runtime.InteropServices.Marshal.Copy(outPx, y * size * 4, bd.Scan0 + y * bd.Stride, size * 4);
        bmp.UnlockBits(bd);
        return bmp;
    }

    static (float, float, float, float) Sample(Rgba t, float u, float v)
    {
        int x = Math.Clamp((int)((u - MathF.Floor(u)) * t.Width), 0, t.Width - 1), y = Math.Clamp((int)((v - MathF.Floor(v)) * t.Height), 0, t.Height - 1);
        int i = (y * t.Width + x) * 4;
        return (t.Pixels[i] / 255f, t.Pixels[i + 1] / 255f, t.Pixels[i + 2] / 255f, t.Pixels[i + 3] / 255f);
    }

    // ---------------------------------------------------------------- building blocks

    static byte[] EmbeddedBytes(string file)
    {
        using var s = typeof(Models3D).Assembly.GetManifestResourceStream("ItemFinder.Models." + file)
                      ?? throw new FileNotFoundException("missing embedded " + file);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static Bitmap Embedded(string file) => Bitmaps.FromRgba(DdsFile.Read(EmbeddedBytes(file)), opaque: false);

    static Bitmap White()
    {
        var b = new Bitmap(16, 16);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.White);
        return b;
    }

    /// <summary>A grid surface: point(fi, fj) for fi, fj in 0..1 gives position and normal; outward winding.</summary>
    static void Grid(Mesh m, int rows, int cols, Func<float, float, (Vector3 P, Vector3 N)> point,
                     Func<Vector3, float, float, (float U, float V)> uv, Func<Vector3, Color> col, bool both = false)
    {
        int b0 = m.V.Count;
        for (int i = 0; i <= rows; i++)
            for (int j = 0; j <= cols; j++)
            {
                float fi = (float)i / rows, fj = (float)j / cols;
                var (p, n) = point(fi, fj);
                var (u, v) = uv(p, fi, fj);
                m.V.Add(new MVert(p, n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ, u, v, col(p)));
            }
        int w = cols + 1;
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                int a = b0 + i * w + j, c = a + 1, d = a + w, e = d + 1;
                m.T.Add((a, d, e)); m.T.Add((a, e, c));
                if (both) { m.T.Add((a, e, d)); m.T.Add((a, c, e)); }
            }
    }

    static Func<float, float, (Vector3, Vector3)> Ellipsoid(Vector3 c, float rx, float ry, float rz, float th0 = 0, float th1 = MathF.PI) =>
        (fi, fj) =>
        {
            float th = th0 + (th1 - th0) * fi, ph = 2 * MathF.PI * fj;
            float x = rx * MathF.Sin(th) * MathF.Cos(ph), y = ry * MathF.Sin(th) * MathF.Sin(ph), z = rz * MathF.Cos(th);
            return (c + new Vector3(x, y, z), new Vector3(x / (rx * rx), y / (ry * ry), z / (rz * rz)));
        };

    /// <summary>A surface of revolution around z: profile(f) = (radius, z, normal radial part, normal z part).</summary>
    static Func<float, float, (Vector3, Vector3)> Revolve(Func<float, (float R, float Z, float NR, float NZ)> profile) =>
        (fi, fj) =>
        {
            var (r, z, nr, nz) = profile(fi);
            float ph = 2 * MathF.PI * fj;
            return (new Vector3(r * MathF.Cos(ph), r * MathF.Sin(ph), z), new Vector3(nr * MathF.Cos(ph), nr * MathF.Sin(ph), nz));
        };

    static (float, float) Solid(Vector3 p, float fi, float fj) => (0.5f, 0.5f);
    static Func<Vector3, Color> Paint(Color c) => _ => c;

    /// <summary>A flat outline (in x/z) puffed up into a rounded cushion, front and back (star, heart, cross, leaf).</summary>
    static void Inflate(Mesh m, IList<Vector2> outline, float thick, Color col, int rings = 7, float y0 = 0,
                        Func<Vector2, bool, (float, float)>? uv = null)
    {
        outline = Ccw(outline);
        var c = outline.Aggregate(Vector2.Zero, (s, p) => s + p) / outline.Count;
        int n = outline.Count, first = m.V.Count;
        foreach (int side in new[] { -1, 1 })
        {
            int b0 = m.V.Count;
            for (int k = 0; k <= rings; k++)
            {
                float a = MathF.PI / 2 * k / rings, s = MathF.Cos(a), y = side * thick * MathF.Sin(a);
                for (int j = 0; j < n; j++)
                {
                    var p2 = c + (outline[j] - c) * s;
                    var (u, v) = uv?.Invoke(p2, side > 0) ?? (0.5f, 0.5f);
                    m.V.Add(new MVert(new Vector3(p2.X, y0 + y, p2.Y), Vector3.Zero, u, v, col));
                }
            }
            for (int k = 0; k < rings; k++)
                for (int j = 0; j < n; j++)
                {
                    int a = b0 + k * n + j, b = b0 + k * n + (j + 1) % n, d = a + n, e = b + n;
                    if (side < 0) { m.T.Add((a, b, e)); m.T.Add((a, e, d)); } else { m.T.Add((a, e, b)); m.T.Add((a, d, e)); }
                }
        }
        SmoothNormals(m, first);
    }

    /// <summary>Normals from the triangles, shared by vertices at the same place (a smooth rim between front and back).</summary>
    static void SmoothNormals(Mesh m, int from)
    {
        var acc = new Dictionary<(int, int, int), Vector3>();
        static (int, int, int) Key(Vector3 p) => ((int)MathF.Round(p.X * 1000), (int)MathF.Round(p.Y * 1000), (int)MathF.Round(p.Z * 1000));
        foreach (var (a, b, c) in m.T)
        {
            if (a < from) continue;
            var n = Vector3.Cross(m.V[b].P - m.V[a].P, m.V[c].P - m.V[a].P);
            foreach (var i in new[] { a, b, c }) acc[Key(m.V[i].P)] = acc.GetValueOrDefault(Key(m.V[i].P)) + n;
        }
        for (int i = from; i < m.V.Count; i++)
            if (acc.TryGetValue(Key(m.V[i].P), out var n) && n.LengthSquared() > 0) m.V[i] = m.V[i] with { N = Vector3.Normalize(n) };
    }

    /// <summary>Flat facets (a gem): each polygon gets its own vertices and normal; polygons listed outward.</summary>
    static void Facet(Mesh m, Color col, params Vector3[] poly)
    {
        var n = Vector3.Normalize(Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]));
        int b0 = m.V.Count;
        foreach (var p in poly) m.V.Add(new MVert(p, n, 0.5f, 0.5f, col));
        for (int k = 1; k + 1 < poly.Length; k++) m.T.Add((b0, b0 + k, b0 + k + 1));
    }

    /// <summary>A tube along a path (question mark), with round ends.</summary>
    static void Tube(Mesh m, IList<Vector3> path, float r, Color col)
    {
        int segs = 16, b0 = m.V.Count;
        for (int i = 0; i < path.Count; i++)
        {
            var t = Vector3.Normalize(path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)]);
            var side = Vector3.Normalize(Vector3.Cross(t, Vector3.UnitY));
            for (int j = 0; j <= segs; j++)
            {
                float a = 2 * MathF.PI * j / segs;
                var n = side * MathF.Cos(a) + Vector3.UnitY * MathF.Sin(a);
                m.V.Add(new MVert(path[i] + n * r, n, 0.5f, 0.5f, col));
            }
        }
        int w = segs + 1;
        for (int i = 0; i + 1 < path.Count; i++)
            for (int j = 0; j < segs; j++)
            {
                int a = b0 + i * w + j, c = a + 1, d = a + w, e = d + 1;
                m.T.Add((a, d, e)); m.T.Add((a, e, c));
            }
        Grid(m, 8, 16, Ellipsoid(path[0], r, r, r), Solid, Paint(col));
        Grid(m, 8, 16, Ellipsoid(path[^1], r, r, r), Solid, Paint(col));
    }

    static Bitmap Canvas(int size, Color fill, Action<Graphics, int> draw)
    {
        var b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(fill);
        draw(g, size);
        return b;
    }

    // ---------------------------------------------------------------- the models

    /// <summary>The Spooky Scorched Beacons pumpkin: ribbed squashed ball with the jack-o'-lantern face projected
    /// front and back, and a stem (pumpkin_d.dds: body centre u .5 v .55, half-size .42 x .33; stem v .12-.23).</summary>
    static Mesh Pumpkin()
    {
        var m = new Mesh();
        const float TU = 0.5f, TV = 0.55f, THW = 0.42f, THH = 0.33f, Ribs = 0.07f, Flat = 0.15f;
        float R = 2 * THW, RZ = 2 * THH, CZ = 1 - 2 * (TV - 0.5f);
        (float, float) Uv(Vector3 p, float fi, float fj) => (TU + THW * p.X / R, TV - THH * (p.Z - CZ) / RZ);
        Grid(m, 16, 48, (fi, fj) =>
        {
            float th = MathF.PI * fi, ph = 2 * MathF.PI * fj;
            float rib = 1 - Ribs + Ribs * MathF.Abs(MathF.Cos(4 * ph));
            float r = R * rib * MathF.Sin(th);
            var p = new Vector3(r * MathF.Cos(ph), r * MathF.Sin(ph), CZ + RZ * MathF.Cos(th) * (1 - Flat + Flat * MathF.Sin(th)));
            return (p, new Vector3(p.X / R, p.Y / R, (p.Z - CZ) / RZ));
        }, Uv, Paint(Color.White));
        float sr = R * 0.09f, z0 = CZ + RZ * 0.8f, z1 = CZ + RZ * ((TV - 0.12f) / THH);
        Grid(m, 1, 12, Revolve(f => (sr, z1 + (z0 - z1) * f, 1, 0)), Uv, Paint(Color.White));
        return m;
    }

    /// <summary>The Spooky Scorched Beacons clown mask (slasher_d.dds atlas: face u/v 0-.75, polka-dot hat strip v .8-1,
    /// colour swatches at u .9): ears, head with the face projected front and back, yellow brim, leaning cone, pompom.</summary>
    static Mesh Clown()
    {
        var m = new Mesh();
        const float Face = 0.75f, HatV0 = 0.8f;
        (float, float) Yellow(Vector3 p, float a, float b) => (0.9f, 0.12f);
        (float, float) Grey(Vector3 p, float a, float b) => (0.9f, 0.375f);
        (float, float) Red(Vector3 p, float a, float b) => (0.9f, 0.625f);
        float R = 0.75f, RZ = 0.64f, CZ = RZ;
        var white = Paint(Color.White);
        foreach (int sx in new[] { -1, 1 })
            Grid(m, 8, 16, Ellipsoid(new(sx * 0.98f * R, 0, CZ + 0.05f * RZ), 0.18f * R, 0.18f * R, 0.18f * R), Red, white);
        Grid(m, 20, 48, Ellipsoid(new(0, 0, CZ), R, R * 0.92f, RZ),
             (p, a, b) => (Face * (0.5f + 0.49f * p.X / R), Face * (0.5f - 0.49f * (p.Z - CZ) / RZ)), white);
        float br = 0.8f * R, bz0 = CZ + 0.66f * RZ, bz1 = CZ + 0.92f * RZ;
        Grid(m, 1, 32, Revolve(f => (br, bz1 + (bz0 - bz1) * f, 1, 0)), Yellow, white);
        Grid(m, 1, 32, Revolve(f => (br * f, bz1, 0, 1)), Yellow, white);
        float cr = 0.58f * R, ch = 1.15f * RZ, lean = 0.3f * R;
        Grid(m, 10, 32, (fi, fj) =>
        {
            float ph = 2 * MathF.PI * fj, h = 1 - fi, r = cr * (1 - h);
            return (new Vector3(r * MathF.Cos(ph) + lean * h * h, r * MathF.Sin(ph), bz1 + ch * h), new Vector3(MathF.Cos(ph), MathF.Sin(ph), cr / ch));
        }, (p, fi, fj) => (fj, HatV0 + 0.01f + (1 - HatV0 - 0.02f) * fi), white);
        Grid(m, 10, 20, Ellipsoid(new(lean, 0, bz1 + ch + 0.12f * R), 0.26f * R, 0.26f * R, 0.26f * R), Grey, white);
        return m;
    }

    static Mesh Skull()
    {
        var m = new Mesh();
        var white = Paint(Color.White);
        (float, float) Front(Vector3 p, float a, float b) => (0.5f + 0.47f * p.X / 0.82f, 0.5f - 0.47f * (p.Z - 1.0f) / 1.0f);
        Grid(m, 18, 40, Ellipsoid(new(0, 0, 1.18f), 0.8f, 0.74f, 0.74f), Front, white);   // cranium
        Grid(m, 12, 32, Ellipsoid(new(0, -0.06f, 0.5f), 0.52f, 0.5f, 0.34f), Front, white); // jaw
        return m;
    }

    static Bitmap SkullTexture() => Canvas(256, Color.FromArgb(236, 228, 206), (g, n) =>
    {
        float S(float f) => f * n;
        using (var shade = new PathGradientBrush(new[] { new PointF(0, 0), new PointF(n, 0), new PointF(n, n), new PointF(0, n) })
               { CenterColor = Color.FromArgb(248, 244, 232), SurroundColors = new[] { Color.FromArgb(200, 190, 166) } })
            g.FillRectangle(shade, 0, 0, n, n);
        using var dark = new SolidBrush(Color.FromArgb(30, 22, 20));
        g.FillEllipse(dark, S(0.22f), S(0.33f), S(0.22f), S(0.19f));                     // eyes
        g.FillEllipse(dark, S(0.56f), S(0.33f), S(0.22f), S(0.19f));
        g.FillPolygon(dark, new[] { new PointF(S(0.5f), S(0.55f)), new PointF(S(0.45f), S(0.66f)), new PointF(S(0.55f), S(0.66f)) }); // nose
        using var pen = new Pen(Color.FromArgb(60, 50, 40), S(0.012f));
        g.DrawLine(pen, S(0.33f), S(0.78f), S(0.67f), S(0.78f));                           // teeth
        for (int k = 0; k <= 6; k++) g.DrawLine(pen, S(0.33f + k * 0.0567f), S(0.73f), S(0.33f + k * 0.0567f), S(0.84f));
    });

    static Mesh Bobblehead()
    {
        var m = new Mesh();
        var white = Paint(Color.White);
        (float, float) Face(Vector3 p, float a, float b) => (0.375f + 0.36f * p.X / 0.64f, 0.375f - 0.36f * (p.Z - 1.35f) / 0.62f);
        (float, float) Swatch(Vector3 p, float a, float b) => (0.9f, 0.9f);
        Grid(m, 18, 40, Ellipsoid(new(0, 0, 1.35f), 0.64f, 0.6f, 0.62f), Face, white);                  // the big head
        Grid(m, 12, 24, Ellipsoid(new(0, 0, 0.62f), 0.3f, 0.24f, 0.38f), Swatch, Paint(Color.FromArgb(46, 104, 214))); // jumpsuit
        Grid(m, 8, 16, Ellipsoid(new(0, 0, 0.55f), 0.31f, 0.25f, 0.05f), Swatch, Paint(Color.FromArgb(240, 200, 60)));  // belt
        Grid(m, 1, 32, Revolve(f => (0.5f, 0.14f * (1 - f), 1, 0)), Swatch, Paint(Color.FromArgb(232, 184, 48)));      // stand
        Grid(m, 1, 32, Revolve(f => (0.5f * f, 0.14f, 0, 1)), Swatch, Paint(Color.FromArgb(232, 184, 48)));
        return m;
    }

    static Bitmap BobbleTexture() => Canvas(256, Color.White, (g, n) =>
    {
        float S(float f) => f * n * 0.75f; // the face square is u/v 0-.75; the rest stays white (vertex-coloured parts)
        using (var skin = new SolidBrush(Color.FromArgb(246, 206, 164))) g.FillRectangle(skin, 0, 0, S(1), S(1));
        using (var hair = new SolidBrush(Color.FromArgb(244, 200, 58)))
        {
            g.FillEllipse(hair, S(-0.1f), S(-0.35f), S(1.2f), S(0.62f));
            g.FillEllipse(hair, S(0.55f), S(0.12f), S(0.32f), S(0.2f)); // the curl
        }
        using var ink = new SolidBrush(Color.FromArgb(40, 34, 30));
        g.FillEllipse(ink, S(0.32f), S(0.42f), S(0.08f), S(0.11f));
        g.FillEllipse(ink, S(0.6f), S(0.42f), S(0.08f), S(0.11f));
        using var pen = new Pen(Color.FromArgb(40, 34, 30), S(0.025f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, S(0.3f), S(0.5f), S(0.42f), S(0.24f), 15, 150);   // big smile
        g.DrawLine(pen, S(0.3f), S(0.37f), S(0.42f), S(0.35f));
        g.DrawLine(pen, S(0.58f), S(0.35f), S(0.7f), S(0.37f));
    });

    /// <summary>A coin standing up (picture front and back): a bottle cap with crimped edge, or a smooth medallion.</summary>
    static Mesh Coin(Func<float, float> R)
    {
        var m = new Mesh();
        (float, float) Face(Vector3 p, float a, float b) => (0.5f + 0.43f * p.X * (p.Y > 0 ? -1 : 1), 0.5f - 0.43f * (p.Z - 1));
        (float, float) Rim(Vector3 p, float a, float b) => (0.97f, 0.97f);
        var white = Paint(Color.White);
        const float T = 0.12f;
        foreach (int side in new[] { -1, 1 })
            Grid(m, 6, 144, (fi, fj) =>
            {
                float ph = 2 * MathF.PI * fj, r = R(ph) * (side < 0 ? fi : 1 - fi);
                var p = new Vector3(r * MathF.Cos(ph), side * (T + 0.07f * (1 - r * r)), 1 + r * MathF.Sin(ph));
                return (p, new Vector3(0.25f * r * MathF.Cos(ph), side, 0.25f * r * MathF.Sin(ph)));
            }, Face, white);
        Grid(m, 1, 144, (fi, fj) =>
        {
            float ph = 2 * MathF.PI * fj, r = R(ph);
            return (new Vector3(r * MathF.Cos(ph), -T + 2 * T * fi, 1 + r * MathF.Sin(ph)), new Vector3(MathF.Cos(ph), 0, MathF.Sin(ph)));
        }, Rim, white);
        return m;
    }

    static Bitmap CapTexture() => Canvas(256, Color.FromArgb(206, 210, 214), (g, n) =>
    {
        using (var red = new LinearGradientBrush(new Rectangle(0, 0, n, n), Color.FromArgb(232, 38, 38), Color.FromArgb(150, 16, 20), 60f))
            g.FillEllipse(red, n * 0.06f, n * 0.06f, n * 0.88f, n * 0.88f);
        using var ring = new Pen(Color.White, n * 0.025f);
        g.DrawEllipse(ring, n * 0.16f, n * 0.16f, n * 0.68f, n * 0.68f);
        using var star = new SolidBrush(Color.White);
        g.FillPolygon(star, StarPoints(n * 0.5f, n * 0.5f, n * 0.22f, n * 0.09f));
    });

    static Bitmap RadiationTexture() => Canvas(256, Color.FromArgb(60, 60, 60), (g, n) =>
    {
        float c = n * 0.5f;
        using (var y = new SolidBrush(Color.FromArgb(250, 210, 30))) g.FillEllipse(y, n * 0.06f, n * 0.06f, n * 0.88f, n * 0.88f);
        using var k = new SolidBrush(Color.FromArgb(25, 22, 20));
        for (int i = 0; i < 3; i++)
            g.FillPie(k, c - n * 0.36f, c - n * 0.36f, n * 0.72f, n * 0.72f, -120 + i * 120 - 30, 60);
        using (var y2 = new SolidBrush(Color.FromArgb(250, 210, 30))) g.FillEllipse(y2, c - n * 0.11f, c - n * 0.11f, n * 0.22f, n * 0.22f);
        g.FillEllipse(k, c - n * 0.07f, c - n * 0.07f, n * 0.14f, n * 0.14f);
    });

    static Mesh Crown()
    {
        var m = new Mesh();
        Color gold = Color.FromArgb(255, 196, 58), red = Color.FromArgb(225, 30, 48);
        Grid(m, 1, 48, Revolve(f => (0.82f, 0.7f - 0.48f * f, 1, 0)), Solid, Paint(gold), both: true);     // band
        for (int k = 0; k < 5; k++)
        {
            float a = -MathF.PI / 2 + k * 2 * MathF.PI / 5;
            var at = new Vector3(0.82f * MathF.Cos(a), 0.82f * MathF.Sin(a), 0.7f);
            Grid(m, 8, 16, (fi, fj) =>
            {
                float ph = 2 * MathF.PI * fj, r = 0.26f * fi;
                var p = at + new Vector3(r * MathF.Cos(ph), r * MathF.Sin(ph), 0.72f * (1 - fi));
                return (p, new Vector3(MathF.Cos(ph), MathF.Sin(ph), 0.36f));
            }, Solid, Paint(gold));
            Grid(m, 8, 16, Ellipsoid(at + new Vector3(0, 0, 0.78f), 0.1f, 0.1f, 0.1f), Solid, Paint(gold));
        }
        foreach (float a in new[] { -90f, -45f, -135f, 45f, 135f, 90f })
        {
            float r = a * MathF.PI / 180;
            Grid(m, 8, 16, Ellipsoid(new(0.86f * MathF.Cos(r), 0.86f * MathF.Sin(r), 0.46f), 0.1f, 0.06f, 0.1f), Solid, Paint(red));
        }
        return m;
    }

    static Mesh Ghost()
    {
        var m = new Mesh();
        var white = Paint(Color.White);
        (float, float) Front(Vector3 p, float a, float b) => (0.5f + 0.45f * p.X / 0.8f, 0.5f - 0.45f * (p.Z - 1.1f) / 0.9f);
        Grid(m, 12, 40, Ellipsoid(new(0, 0, 1.3f), 0.72f, 0.62f, 0.72f, 0, MathF.PI / 2), Front, white);
        Grid(m, 10, 40, (fi, fj) =>
        {
            float ph = 2 * MathF.PI * fj, r = 0.72f + 0.1f * fi;
            float z = 1.3f - (1.3f - (0.18f + 0.12f * MathF.Sin(6 * ph))) * fi;
            return (new Vector3(r * MathF.Cos(ph), r * 0.86f * MathF.Sin(ph), z), new Vector3(MathF.Cos(ph), MathF.Sin(ph), -0.1f));
        }, Front, white, both: true);
        return m;
    }

    static Bitmap GhostTexture() => Canvas(256, Color.FromArgb(240, 244, 255), (g, n) =>
    {
        using var ink = new SolidBrush(Color.FromArgb(28, 28, 40));
        g.FillEllipse(ink, n * 0.33f, n * 0.24f, n * 0.11f, n * 0.17f);
        g.FillEllipse(ink, n * 0.56f, n * 0.24f, n * 0.11f, n * 0.17f);
        g.FillEllipse(ink, n * 0.44f, n * 0.47f, n * 0.12f, n * 0.15f);
    });

    static Mesh Mushroom()
    {
        var m = new Mesh();
        var white = Paint(Color.White);
        Color stem = Color.FromArgb(240, 228, 200), gills = Color.FromArgb(214, 196, 160);
        (float, float) Cap(Vector3 p, float fi, float fj) => (fj, 0.48f * fi);
        (float, float) Plain(Vector3 p, float a, float b) => (0.5f, 0.85f);
        Grid(m, 14, 48, Ellipsoid(new(0, 0, 0.92f), 1.0f, 1.0f, 0.78f, 0, MathF.PI / 2), Cap, white);
        Grid(m, 3, 48, Revolve(f => (1.0f - 0.72f * f, 0.92f, 0, -1)), Plain, Paint(gills));
        Grid(m, 1, 24, Revolve(f => (0.28f + 0.05f * f, 0.95f - 0.95f * f, 1, 0)), Plain, Paint(stem));
        return m;
    }

    static Bitmap MushroomTexture() => Canvas(256, Color.White, (g, n) =>
    {
        using (var red = new LinearGradientBrush(new Rectangle(0, 0, n, n / 2), Color.FromArgb(232, 40, 32), Color.FromArgb(196, 24, 20), 90f))
            g.FillRectangle(red, 0, 0, n, n / 2);
        using var dot = new SolidBrush(Color.FromArgb(250, 246, 236));
        var rnd = new Random(7);
        for (int k = 0; k < 14; k++)
        {
            float x = (k * 0.137f + 0.05f) % 1 * n, y = (0.08f + 0.32f * (k % 4) / 3f) * n, d = n * (0.07f + 0.03f * (k % 3));
            g.FillEllipse(dot, x - d / 2, y - d / 4, d, d / 2);
        }
    });

    static PointF[] StarPoints(float cx, float cy, float r1, float r2) =>
        Enumerable.Range(0, 10).Select(i =>
        {
            float a = -MathF.PI / 2 + i * MathF.PI / 5, r = i % 2 == 0 ? r1 : r2;
            return new PointF(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }).ToArray();

    static Mesh Star()
    {
        var m = new Mesh();
        var outline = Enumerable.Range(0, 10).Select(i =>
        {
            float a = MathF.PI / 2 + i * MathF.PI / 5, r = i % 2 == 0 ? 1f : 0.42f;
            return new Vector2(r * MathF.Cos(a), 1 + r * MathF.Sin(a));
        }).ToList();
        Inflate(m, outline, 0.36f, Color.White);
        return m;
    }

    static Mesh Heart()
    {
        var m = new Mesh();
        var outline = Enumerable.Range(0, 72).Select(i =>
        {
            float t = 2 * MathF.PI * i / 72;
            float x = 16 * MathF.Pow(MathF.Sin(t), 3), z = 13 * MathF.Cos(t) - 5 * MathF.Cos(2 * t) - 2 * MathF.Cos(3 * t) - MathF.Cos(4 * t);
            return new Vector2(-x / 17f, (z + 17) / 17f);
        }).ToList();
        Inflate(m, outline, 0.42f, Color.White, 8);
        return m;
    }

    static Mesh Gem()
    {
        var m = new Mesh();
        const int N = 8;
        Vector3 P(float r, float z, int k, float off = 0) { float a = 2 * MathF.PI * (k + off) / N - MathF.PI / 2; return new(r * MathF.Cos(a), r * MathF.Sin(a), z); }
        var c = Color.White;
        var tip = new Vector3(0, 0, 0);
        for (int k = 0; k < N; k++)
        {
            Facet(m, c, tip, P(0.85f, 1.2f, k + 1), P(0.85f, 1.2f, k));                       // pavilion
            Facet(m, c, P(0.85f, 1.2f, k), P(0.85f, 1.2f, k + 1), P(0.48f, 1.62f, k + 1), P(0.48f, 1.62f, k)); // crown
        }
        Facet(m, c, Enumerable.Range(0, N).Select(k => P(0.48f, 1.62f, k)).ToArray());          // table
        return m;
    }

    static Mesh Exclamation()
    {
        var m = new Mesh();
        Grid(m, 20, 32, (fi, fj) =>
        {
            // an egg standing on its point: wide on top, narrow at the bottom
            float th = MathF.PI * fi, ph = 2 * MathF.PI * fj;
            float r = MathF.Sin(th) * (0.2f + 0.1f * MathF.Cos(th) + 0.02f), z = 1.36f + 0.68f * MathF.Cos(th);
            return (new Vector3(r * MathF.Cos(ph), r * MathF.Sin(ph), z), new Vector3(MathF.Sin(th) * MathF.Cos(ph), MathF.Sin(th) * MathF.Sin(ph), MathF.Cos(th) * 0.45f));
        }, Solid, Paint(Color.White));
        Grid(m, 12, 24, Ellipsoid(new(0, 0, 0.24f), 0.23f, 0.23f, 0.23f), Solid, Paint(Color.White));
        return m;
    }

    static Mesh Question()
    {
        var m = new Mesh();
        var path = new List<Vector3>();
        for (int i = 0; i <= 28; i++)
        {
            float a = (160 - 250 * i / 28f) * MathF.PI / 180;
            path.Add(new Vector3(0.52f * MathF.Cos(a), 0, 1.45f + 0.52f * MathF.Sin(a)));
        }
        path.Add(new Vector3(0, 0, 0.82f));
        Tube(m, path, 0.17f, Color.White);
        Grid(m, 12, 24, Ellipsoid(new(0, 0, 0.24f), 0.22f, 0.22f, 0.22f), Solid, Paint(Color.White));
        return m;
    }

    static Mesh Cross()
    {
        var m = new Mesh();
        float a = 0.33f, b = 1f;
        var pts = new[] { (a, b), (a, a), (b, a), (b, -a), (a, -a), (a, -b), (-a, -b), (-a, -a), (-b, -a), (-b, a), (-a, a), (-a, b) }.Reverse().ToArray();
        // more points along the edges keep the cushion round
        var outline = new List<Vector2>();
        for (int i = 0; i < pts.Length; i++)
        {
            var p = new Vector2(pts[i].Item1, pts[i].Item2 + 1);
            var q = new Vector2(pts[(i + 1) % pts.Length].Item1, pts[(i + 1) % pts.Length].Item2 + 1);
            for (int k = 0; k < 4; k++) outline.Add(Vector2.Lerp(p, q, k / 4f));
        }
        Inflate(m, outline, 0.3f, Color.White);
        return m;
    }

    static Mesh Leaf()
    {
        var m = new Mesh();
        var outline = new List<Vector2>();
        for (int i = 0; i <= 24; i++) { float s = i / 24f; outline.Add(new(0.72f * MathF.Pow(Math.Max(0, MathF.Sin(MathF.PI * s)), 0.8f), 0.15f + 1.85f * s)); }
        for (int i = 23; i >= 1; i--) { float s = i / 24f; outline.Add(new(-0.72f * MathF.Pow(Math.Max(0, MathF.Sin(MathF.PI * s)), 0.8f), 0.15f + 1.85f * s)); }
        Inflate(m, outline, 0.16f, Color.White);
        Grid(m, 1, 12, Revolve(f => (0.06f, 0.18f * (1 - f), 1, 0)), Solid, Paint(Color.White)); // stem
        return m;
    }

    // ---------------------------------------------------------------- more building blocks

    /// <summary>Texture rows at v 0.94-1 are left plain (the model's vertex colours show there): parts that aren't
    /// pictures point at it.</summary>
    const float PlainV = 0.97f;

    static void PlainBand(Graphics g, int n, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillRectangle(b, 0, n * 0.94f, n, n * 0.06f + 1);
    }

    /// <summary>Counter-clockwise in x/z (seen from the front), so outward faces come out right whatever order it was drawn in.</summary>
    static List<Vector2> Ccw(IEnumerable<Vector2> outline)
    {
        var o = outline.ToList();
        float a = 0;
        for (int i = 0; i < o.Count; i++) { var p = o[i]; var q = o[(i + 1) % o.Count]; a += p.X * q.Y - q.X * p.Y; }
        if (a < 0) o.Reverse();
        return o;
    }

    /// <summary>Rounds the corners of a closed outline (Chaikin: each edge keeps its middle half).</summary>
    static List<Vector2> Rounded(IEnumerable<Vector2> outline, int times = 3)
    {
        var o = outline.ToList();
        for (int t = 0; t < times; t++)
        {
            var r = new List<Vector2>();
            for (int i = 0; i < o.Count; i++)
            {
                var p = o[i]; var q = o[(i + 1) % o.Count];
                r.Add(Vector2.Lerp(p, q, 0.25f)); r.Add(Vector2.Lerp(p, q, 0.75f));
            }
            o = r;
        }
        return o;
    }

    /// <summary>A turned shape (bottle, bowl...): profile points from the top down at (radius, height) with a colour; a
    /// point given twice makes a sharp edge (and a clean colour change). Texture position from <paramref name="uv"/>
    /// (position, colour, way around 0-1), else the plain band.</summary>
    static void Lathe(Mesh m, (float R, float Z, Color C)[] pts, int segs = 40, Func<Vector3, Color, float, (float, float)>? uv = null)
    {
        uv ??= (p, c, fj) => (fj, PlainV);
        int n = pts.Length, b0 = m.V.Count;
        bool Same(int a, int b) => a >= 0 && b >= 0 && a < n && b < n && MathF.Abs(pts[a].R - pts[b].R) < 1e-5f && MathF.Abs(pts[a].Z - pts[b].Z) < 1e-5f;
        int Prev(int i) { int k = i - 1; while (k >= 0 && Same(k, i)) k--; return Math.Max(k, 0); }
        int Next(int i) { int k = i + 1; while (k < n && Same(k, i)) k++; return Math.Min(k, n - 1); }
        for (int i = 0; i < n; i++)
        {
            // the profile's direction here (a doubled point takes only its own side: a crease)
            int a = Same(i - 1, i) ? i : Prev(i), b = Same(i + 1, i) ? i : Next(i);
            float dr = pts[b].R - pts[a].R, dz = pts[b].Z - pts[a].Z;
            var n2 = Vector2.Normalize(new Vector2(-dz, dr) + new Vector2(1e-6f, 0));
            for (int j = 0; j <= segs; j++)
            {
                float fj = (float)j / segs, ph = 2 * MathF.PI * fj;
                var p = new Vector3(pts[i].R * MathF.Cos(ph), pts[i].R * MathF.Sin(ph), pts[i].Z);
                var (u, v) = uv(p, pts[i].C, fj);
                m.V.Add(new MVert(p, new Vector3(n2.X * MathF.Cos(ph), n2.X * MathF.Sin(ph), n2.Y), u, v, pts[i].C));
            }
        }
        int w = segs + 1;
        for (int i = 0; i + 1 < n; i++)
            for (int j = 0; j < segs; j++)
            {
                int a = b0 + i * w + j, c = a + 1, d = a + w, e = d + 1;
                m.T.Add((a, d, e)); m.T.Add((a, e, c));
            }
    }

    /// <summary>A ring (torus) around the z axis turned by <paramref name="q"/>.</summary>
    static void Torus(Mesh m, Vector3 c, Quaternion q, float R, float r, Color col, int rows = 12, int cols = 48) =>
        Grid(m, rows, cols, (fi, fj) =>
        {
            float u = 2 * MathF.PI * (1 - fi), v = 2 * MathF.PI * fj;
            var n = new Vector3(MathF.Cos(u) * MathF.Cos(v), MathF.Cos(u) * MathF.Sin(v), MathF.Sin(u));
            var p = new Vector3((R + r * MathF.Cos(u)) * MathF.Cos(v), (R + r * MathF.Cos(u)) * MathF.Sin(v), r * MathF.Sin(u));
            return (c + Vector3.Transform(p, q), Vector3.Transform(n, q));
        }, (p, a, b) => (0.5f, PlainV), Paint(col));

    /// <summary>A flat outline (x/z) pushed out from y0 (front) to y1 (back): flat faces with a picture from
    /// <paramref name="uv"/> (outline point, back side?), plain walls.</summary>
    static void Extrude(Mesh m, IEnumerable<Vector2> outline, float y0, float y1, Color col, Func<Vector2, bool, (float, float)>? uv = null)
    {
        var o = Ccw(outline);
        uv ??= (p, back) => (0.5f, PlainV);
        var c = o.Aggregate(Vector2.Zero, (s, p) => s + p) / o.Count;
        int n = o.Count;
        foreach (var (y, back) in new[] { (y0, false), (y1, true) })
        {
            int b0 = m.V.Count;
            var nrm = new Vector3(0, back ? 1 : -1, 0);
            foreach (var p in o.Prepend(c))
            {
                var (u, v) = uv(p, back);
                m.V.Add(new MVert(new Vector3(p.X, y, p.Y), nrm, u, v, col));
            }
            for (int j = 0; j < n; j++)
            {
                int a = b0 + 1 + j, b = b0 + 1 + (j + 1) % n;
                if (back) m.T.Add((b0, b, a)); else m.T.Add((b0, a, b));
            }
        }
        for (int j = 0; j < n; j++)
        {
            var p = o[j]; var q = o[(j + 1) % n];
            var e = Vector2.Normalize(q - p);
            var nrm = new Vector3(e.Y, 0, -e.X);
            int b0 = m.V.Count;
            foreach (var (x, z, y) in new[] { (p.X, p.Y, y0), (q.X, q.Y, y0), (q.X, q.Y, y1), (p.X, p.Y, y1) })
                m.V.Add(new MVert(new Vector3(x, y, z), nrm, 0.5f, PlainV, col));
            m.T.Add((b0, b0 + 3, b0 + 2)); m.T.Add((b0, b0 + 2, b0 + 1));
        }
    }

    /// <summary>A picture projected onto the front (seen from -y) of a round shape. The back takes the plain band at the
    /// picture's edge (so seams run through plain colour), which must be the picture's background colour.</summary>
    static Func<Vector3, float, float, (float, float)> FrontPicture(float cx, float cz, float rx, float rz, float k = 0.45f) =>
        (p, a, b) =>
        {
            float u = 0.5f + k * (p.X - cx) / rx, v = 0.5f - k * (p.Z - cz) / rz;
            if (p.Y > 0.02f) return (p.X < cx ? 0.02f : 0.98f, PlainV);
            return (Math.Clamp(u, 0.01f, 0.99f), Math.Clamp(v, 0.01f, 0.93f));
        };

    static Font Bold(float px) => new("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel);

    /// <summary>Big round anime eyes: black with two white shines.</summary>
    static void AnimeEye(Graphics g, float cx, float cy, float w, float h)
    {
        using var ink = new SolidBrush(Color.FromArgb(30, 24, 34));
        using var shine = new SolidBrush(Color.White);
        g.FillEllipse(ink, cx - w / 2, cy - h / 2, w, h);
        g.FillEllipse(shine, cx - w * 0.28f, cy - h * 0.36f, w * 0.38f, h * 0.32f);
        g.FillEllipse(shine, cx + w * 0.08f, cy + h * 0.12f, w * 0.18f, h * 0.15f);
    }

    static void Blush(Graphics g, float cx, float cy, float w, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, cx - w / 2, cy - w * 0.28f, w, w * 0.56f);
    }

    // ---------------------------------------------------------------- Fallout

    static readonly Color Metal = Color.FromArgb(176, 184, 190), DarkMetal = Color.FromArgb(78, 84, 90);

    static Mesh NukaBottle()
    {
        var m = new Mesh();
        Color cap = Color.FromArgb(214, 36, 36), cola = Color.FromArgb(92, 40, 26), label = Color.White;
        Lathe(m, new (float, float, Color)[]
        {
            (0, 2.0f, cap), (0.15f, 2.0f, cap), (0.17f, 1.9f, cap), (0.17f, 1.9f, cola), (0.12f, 1.86f, cola), (0.11f, 1.6f, cola),
            (0.17f, 1.4f, cola), (0.33f, 1.16f, cola), (0.38f, 0.98f, cola), (0.38f, 0.98f, label), (0.4f, 0.56f, label),
            (0.4f, 0.56f, cola), (0.46f, 0.36f, cola), (0.45f, 0.14f, cola), (0.33f, 0.02f, cola), (0, 0, cola),
        }, 48, (p, c, fj) => c == label ? (fj, 0.9f * (0.98f - p.Z) / 0.42f) : (fj, PlainV));
        return m;
    }

    static Bitmap NukaTexture() => Canvas(256, Color.FromArgb(200, 22, 40), (g, n) =>
    {
        // the label: red with a white swoosh and a star, twice around
        using var pen = new Pen(Color.White, n * 0.07f);
        var pts = Enumerable.Range(0, 33).Select(i => new PointF(i * n / 32f, n * (0.45f + 0.16f * MathF.Sin(i / 32f * 4 * MathF.PI)))).ToArray();
        g.DrawCurve(pen, pts);
        using var star = new SolidBrush(Color.White);
        foreach (float x in new[] { 0.25f, 0.75f }) g.FillPolygon(star, StarPoints(n * x, n * 0.22f, n * 0.09f, n * 0.04f));
        PlainBand(g, n, Color.White);
    });

    static Mesh MiniNuke()
    {
        var m = new Mesh();
        Color body = Color.FromArgb(222, 184, 60), band = Color.FromArgb(40, 38, 36), nose = Color.FromArgb(205, 52, 40);
        Lathe(m, new (float, float, Color)[]
        {
            (0, 1.98f, Metal), (0.18f, 1.98f, Metal), (0.2f, 1.93f, Metal), (0.2f, 1.45f, Metal), (0.2f, 1.45f, body),
            (0.45f, 1.3f, body), (0.6f, 1.08f, body), (0.6f, 1.08f, band), (0.64f, 0.98f, band), (0.64f, 0.98f, body),
            (0.65f, 0.75f, body), (0.58f, 0.45f, body), (0.42f, 0.22f, body), (0.42f, 0.22f, nose), (0.2f, 0.06f, nose), (0, 0, nose),
        }, 48);
        // four tail fins, both sides
        for (int k = 0; k < 4; k++)
        {
            float a = MathF.PI / 4 + k * MathF.PI / 2;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            var side = new Vector3(-dir.Y, dir.X, 0) * 0.025f;
            Vector3 P(float r, float z) => dir * r + new Vector3(0, 0, z);
            var fin = new[] { P(0.19f, 1.95f), P(0.56f, 2.02f), P(0.56f, 1.72f), P(0.3f, 1.4f), P(0.19f, 1.45f) };
            Facet(m, Metal, fin.Select(p => p + side).ToArray());
            Facet(m, Metal, fin.Select(p => p - side).Reverse().ToArray());
        }
        return m;
    }

    static Mesh Stimpak()
    {
        var m = new Mesh();
        Color liquid = Color.FromArgb(226, 44, 52), needle = Color.FromArgb(214, 220, 224);
        Lathe(m, new (float, float, Color)[]
        {
            (0, 2.0f, Metal), (0.3f, 2.0f, Metal), (0.3f, 1.93f, Metal), (0.3f, 1.93f, Metal), (0.07f, 1.92f, Metal), (0.07f, 1.74f, Metal),
            (0.26f, 1.74f, Metal), (0.26f, 1.66f, Metal), (0.26f, 1.66f, Metal), (0.21f, 1.66f, liquid), (0.22f, 1.6f, liquid),
            (0.22f, 0.9f, liquid), (0.21f, 0.86f, liquid), (0.26f, 0.86f, Metal), (0.26f, 0.78f, Metal), (0.12f, 0.74f, Metal),
            (0.06f, 0.5f, Metal), (0.025f, 0.46f, needle), (0.02f, 0.03f, needle), (0, 0, needle),
        }, 36);
        // finger loops and the pressure gauge
        foreach (int sx in new[] { -1, 1 })
            Torus(m, new(sx * 0.4f, 0, 1.72f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), 0.12f, 0.035f, Metal, 8, 24);
        Grid(m, 10, 24, Ellipsoid(new(0, -0.25f, 1.32f), 0.15f, 0.07f, 0.15f), Solid, Paint(Color.FromArgb(240, 238, 228)));
        Grid(m, 1, 24, Revolve(f => (0.17f, 1.36f - 0.08f * f, 1, 0)), Solid, Paint(Metal));
        return m;
    }

    static Mesh FusionCore()
    {
        var m = new Mesh();
        Color glow = Color.FromArgb(255, 226, 80), dark = DarkMetal;
        var pts = new List<(float, float, Color)> { (0, 2.0f, dark), (0.28f, 2.0f, dark), (0.36f, 1.92f, dark), (0.36f, 1.74f, dark) };
        // glowing sections between dark rings
        foreach (var (top, bottom) in new[] { (1.74f, 1.42f), (1.36f, 1.04f), (0.98f, 0.66f) })
        {
            pts.Add((0.33f, top, glow)); pts.Add((0.33f, bottom, glow));
            pts.Add((0.37f, bottom, dark)); pts.Add((0.37f, bottom - 0.06f, dark));
        }
        pts.AddRange(new (float, float, Color)[] { (0.36f, 0.6f, dark), (0.36f, 0.1f, dark), (0.28f, 0, dark), (0, 0, dark) });
        Lathe(m, pts.ToArray(), 40);
        // three bars along the side
        for (int k = 0; k < 3; k++)
        {
            float a = k * 2 * MathF.PI / 3;
            var at = new Vector3(0.37f * MathF.Cos(a), 0.37f * MathF.Sin(a), 0);
            Grid(m, 1, 10, (fi, fj) =>
            {
                float ph = 2 * MathF.PI * fj;
                return (at + new Vector3(0.05f * MathF.Cos(ph), 0.05f * MathF.Sin(ph), 1.74f - 1.1f * fi), new Vector3(MathF.Cos(ph), MathF.Sin(ph), 0));
            }, Solid, Paint(Metal));
        }
        return m;
    }

    static Bitmap VaultDoorTexture() => Canvas(256, Color.FromArgb(120, 128, 138), (g, n) =>
    {
        using (var steel = new PathGradientBrush(new[] { new PointF(0, 0), new PointF(n, 0), new PointF(n, n), new PointF(0, n) })
               { CenterColor = Color.FromArgb(178, 186, 196), SurroundColors = new[] { Color.FromArgb(92, 100, 112) } })
            g.FillEllipse(steel, n * 0.04f, n * 0.04f, n * 0.92f, n * 0.92f);
        using var ring = new Pen(Color.FromArgb(70, 76, 86), n * 0.03f);
        g.DrawEllipse(ring, n * 0.17f, n * 0.17f, n * 0.66f, n * 0.66f);
        using var f = Bold(n * 0.36f);
        using var yellow = new SolidBrush(Color.FromArgb(246, 198, 30));
        using var shadow = new SolidBrush(Color.FromArgb(30, 60, 120));
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("76", f, shadow, new RectangleF(n * 0.015f, n * 0.02f, n, n), fmt);
        g.DrawString("76", f, yellow, new RectangleF(0, 0, n, n), fmt);
    });

    static Mesh Atom()
    {
        var m = new Mesh();
        var c = new Vector3(0, 0, 1);
        Grid(m, 14, 28, Ellipsoid(c, 0.3f, 0.3f, 0.3f), Solid, Paint(Color.White));
        for (int k = 0; k < 3; k++)
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, k * MathF.PI / 3) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.25f);
            Torus(m, c, q, 0.92f, 0.055f, Color.White, 10, 56);
            var e = Vector3.Transform(new Vector3(0.92f * MathF.Cos(k * 2.1f), 0.92f * MathF.Sin(k * 2.1f), 0), q);
            Grid(m, 8, 16, Ellipsoid(c + e, 0.11f, 0.11f, 0.11f), Solid, Paint(Color.White));
        }
        return m;
    }

    static Mesh Lunchbox()
    {
        var m = new Mesh();
        Color blue = Color.FromArgb(34, 84, 170);
        var box = Rounded(new[] { new Vector2(-0.95f, 0), new Vector2(0.95f, 0), new Vector2(0.95f, 1.35f), new Vector2(-0.95f, 1.35f) }, 2);
        Extrude(m, box, -0.36f, 0.36f, Color.White,
                (p, back) => (0.5f + 0.5f * p.X / 0.95f * (back ? -1 : 1), 0.92f * (1 - p.Y / 1.35f)));
        // the plain walls take the band colour (blue): Extrude gives them the plain v
        var handle = Enumerable.Range(0, 13).Select(i =>
        {
            float a = MathF.PI * i / 12;
            return new Vector3(0.38f * MathF.Cos(a), 0, 1.35f + 0.3f * MathF.Sin(a));
        }).ToList();
        Tube(m, handle, 0.06f, Metal);
        return m;
    }

    static Bitmap LunchboxTexture() => Canvas(256, Color.FromArgb(34, 84, 170), (g, n) =>
    {
        using var yellow = new SolidBrush(Color.FromArgb(246, 198, 30));
        g.FillRectangle(yellow, 0, n * 0.06f, n, n * 0.07f);
        g.FillRectangle(yellow, 0, n * 0.8f, n, n * 0.07f);
        g.FillEllipse(yellow, n * 0.29f, n * 0.2f, n * 0.42f, n * 0.52f);
        VaultFace(g, n * 0.5f, n * 0.47f, n * 0.34f);
        PlainBand(g, n, Color.FromArgb(34, 84, 170));
    });

    /// <summary>A cartoon vault-dweller face (round, blond curl, big grin).</summary>
    static void VaultFace(Graphics g, float cx, float cy, float d)
    {
        using (var skin = new SolidBrush(Color.FromArgb(246, 206, 164))) g.FillEllipse(skin, cx - d / 2, cy - d / 2, d, d);
        using (var hair = new SolidBrush(Color.FromArgb(250, 214, 70)))
        {
            g.FillPie(hair, cx - d * 0.52f, cy - d * 0.56f, d * 1.04f, d * 0.8f, 180, 180);
            g.FillEllipse(hair, cx + d * 0.08f, cy - d * 0.3f, d * 0.3f, d * 0.2f);
        }
        using var ink = new SolidBrush(Color.FromArgb(40, 34, 30));
        g.FillEllipse(ink, cx - d * 0.22f, cy - d * 0.05f, d * 0.1f, d * 0.14f);
        g.FillEllipse(ink, cx + d * 0.12f, cy - d * 0.05f, d * 0.1f, d * 0.14f);
        using var pen = new Pen(Color.FromArgb(40, 34, 30), d * 0.05f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, cx - d * 0.25f, cy - d * 0.02f, d * 0.5f, d * 0.34f, 15, 150);
    }

    static Mesh MrHandy()
    {
        var m = new Mesh();
        Color body = Color.FromArgb(170, 178, 184), eye = Color.FromArgb(244, 244, 236), pupil = Color.FromArgb(30, 30, 34);
        Color flame = Color.FromArgb(90, 170, 255);
        Grid(m, 18, 40, Ellipsoid(new(0, 0, 1.0f), 0.6f, 0.6f, 0.55f), Solid, Paint(body));
        Torus(m, new(0, 0, 1.0f), Quaternion.Identity, 0.6f, 0.04f, DarkMetal, 8, 48);
        // three eye stalks: one forward, two to the sides
        foreach (float deg in new[] { 270f, 150f, 30f })
        {
            float a = deg * MathF.PI / 180;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            var tip = dir * 0.62f + new Vector3(0, 0, 1.95f);
            Tube(m, new[] { dir * 0.2f + new Vector3(0, 0, 1.45f), dir * 0.42f + new Vector3(0, 0, 1.75f), tip }, 0.045f, DarkMetal);
            Grid(m, 10, 20, Ellipsoid(tip + dir * 0.06f, 0.14f, 0.14f, 0.14f), Solid, Paint(eye));
            Grid(m, 6, 12, Ellipsoid(tip + dir * 0.17f, 0.06f, 0.06f, 0.06f), Solid, Paint(pupil));
        }
        // arms
        foreach (int sx in new[] { -1, 1 })
        {
            Tube(m, new[] { new Vector3(sx * 0.55f, 0, 0.92f), new Vector3(sx * 0.92f, 0, 0.62f), new Vector3(sx * 0.9f, 0, 0.32f) }, 0.05f, DarkMetal);
            Grid(m, 8, 16, Ellipsoid(new(sx * 0.9f, 0, 0.28f), 0.09f, 0.09f, 0.09f), Solid, Paint(Metal));
        }
        // thruster and its flame
        Lathe(m, new (float, float, Color)[] { (0.26f, 0.5f, DarkMetal), (0.2f, 0.28f, DarkMetal), (0.2f, 0.28f, flame), (0.12f, 0.14f, flame), (0, 0, flame) }, 24);
        return m;
    }

    // ---------------------------------------------------------------- anime-style

    static Mesh Neko()
    {
        var m = new Mesh();
        Color fur = Color.White, pink = Color.FromArgb(255, 170, 190);
        Grid(m, 18, 40, Ellipsoid(new(0, 0, 0.95f), 0.9f, 0.76f, 0.78f), FrontPicture(0, 0.95f, 0.9f, 0.78f), Paint(fur));
        foreach (int sx in new[] { -1, 1 })
        {
            var ear = Rounded(new[] { new Vector2(sx * 0.82f, 1.3f), new Vector2(sx * 0.22f, 1.62f), new Vector2(sx * 0.7f, 2.05f) }, 2);
            Inflate(m, ear, 0.16f, Color.White, uv: (p, back) => (0.5f, PlainV));
            var inner = ear.Select(p => new Vector2(sx * 0.6f, 1.68f) + (p - new Vector2(sx * 0.6f, 1.68f)) * 0.55f).ToList();
            Inflate(m, inner, 0.08f, pink, y0: -0.1f, uv: (p, back) => (0.5f, PlainV));
        }
        return m;
    }

    static Bitmap NekoTexture() => Canvas(256, Color.FromArgb(252, 244, 232), (g, n) =>
    {
        AnimeEye(g, n * 0.33f, n * 0.47f, n * 0.15f, n * 0.19f);
        AnimeEye(g, n * 0.67f, n * 0.47f, n * 0.15f, n * 0.19f);
        Blush(g, n * 0.22f, n * 0.6f, n * 0.12f, Color.FromArgb(255, 176, 190));
        Blush(g, n * 0.78f, n * 0.6f, n * 0.12f, Color.FromArgb(255, 176, 190));
        using (var nose = new SolidBrush(Color.FromArgb(240, 120, 150)))
            g.FillPolygon(nose, new[] { new PointF(n * 0.47f, n * 0.56f), new PointF(n * 0.53f, n * 0.56f), new PointF(n * 0.5f, n * 0.6f) });
        using var pen = new Pen(Color.FromArgb(60, 46, 50), n * 0.012f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, n * 0.44f, n * 0.57f, n * 0.06f, n * 0.05f, 0, 180);   // the ":3" mouth
        g.DrawArc(pen, n * 0.5f, n * 0.57f, n * 0.06f, n * 0.05f, 0, 180);
        foreach (int s in new[] { -1, 1 })
            for (int k = 0; k < 3; k++)
                g.DrawLine(pen, n * (0.5f + s * 0.17f), n * (0.62f + k * 0.025f), n * (0.5f + s * 0.31f), n * (0.6f + k * 0.04f));
    });

    static Mesh Onigiri()
    {
        var m = new Mesh();
        var tri = Rounded(new[] { new Vector2(-1f, 0.1f), new Vector2(1f, 0.1f), new Vector2(0, 1.9f) }, 4);
        Inflate(m, tri, 0.42f, Color.White, 9, uv: (p, back) => back ? (0.12f, 0.12f) : (0.5f + 0.5f * p.X, (1.95f - p.Y) / 1.9f * 0.93f));
        return m;
    }

    static Bitmap OnigiriTexture() => Canvas(256, Color.FromArgb(252, 252, 246), (g, n) =>
    {
        using (var grain = new SolidBrush(Color.FromArgb(232, 232, 222)))
            for (int k = 0; k < 90; k++)
            {
                float x = (k * 0.618f % 1) * n, y = (k * 0.377f % 1) * n * 0.9f;
                g.FillEllipse(grain, x, y, n * 0.035f, n * 0.018f);
            }
        using (var nori = new SolidBrush(Color.FromArgb(30, 44, 36))) g.FillRectangle(nori, n * 0.3f, n * 0.66f, n * 0.4f, n * 0.3f);
        AnimeEye(g, n * 0.4f, n * 0.5f, n * 0.06f, n * 0.08f);
        AnimeEye(g, n * 0.6f, n * 0.5f, n * 0.06f, n * 0.08f);
        Blush(g, n * 0.33f, n * 0.57f, n * 0.07f, Color.FromArgb(255, 180, 190));
        Blush(g, n * 0.67f, n * 0.57f, n * 0.07f, Color.FromArgb(255, 180, 190));
        using var pen = new Pen(Color.FromArgb(60, 46, 50), n * 0.012f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, n * 0.47f, n * 0.53f, n * 0.06f, n * 0.05f, 0, 180);
    });

    static Mesh Sakura()
    {
        var m = new Mesh();
        Color petal = Color.FromArgb(255, 183, 201);
        var c = new Vector2(0, 1);
        var shape = new List<Vector2>();
        for (int i = 0; i <= 10; i++) { float t = i / 10f; shape.Add(new(-0.42f * MathF.Sin(MathF.PI * MathF.Pow(t, 0.75f)) - 0.04f, 0.1f + 0.85f * t)); }
        shape.Add(new(0, 0.86f));                      // the notch at the tip
        for (int i = 10; i >= 0; i--) { float t = i / 10f; shape.Add(new(0.42f * MathF.Sin(MathF.PI * MathF.Pow(t, 0.75f)) + 0.04f, 0.1f + 0.85f * t)); }
        for (int k = 0; k < 5; k++)
        {
            float a = k * 2 * MathF.PI / 5;
            var outline = shape.Select(p => c + new Vector2(p.X * MathF.Cos(a) - p.Y * MathF.Sin(a), p.X * MathF.Sin(a) + p.Y * MathF.Cos(a))).ToList();
            Inflate(m, outline, 0.1f, petal, 6, y0: 0.01f * k);
        }
        Grid(m, 10, 20, Ellipsoid(new(0, -0.08f, 1), 0.2f, 0.12f, 0.2f), Solid, Paint(Color.FromArgb(255, 214, 80)));
        return m;
    }

    static Mesh Daruma()
    {
        var m = new Mesh();
        Grid(m, 20, 40, (fi, fj) =>
        {
            // an egg, wider at the bottom
            float th = MathF.PI * fi, ph = 2 * MathF.PI * fj;
            float k = 1 + 0.12f * (1 - MathF.Cos(th)) / 2;
            float x = 0.78f * k * MathF.Sin(th) * MathF.Cos(ph), y = 0.7f * k * MathF.Sin(th) * MathF.Sin(ph), z = 1 + 0.98f * MathF.Cos(th);
            return (new Vector3(x, y, z), new Vector3(x / 0.6f, y / 0.5f, (z - 1) / 0.96f));
        }, FrontPicture(0, 1, 0.85f, 0.98f), Paint(Color.White));
        return m;
    }

    static Bitmap DarumaTexture() => Canvas(256, Color.FromArgb(214, 30, 38), (g, n) =>
    {
        using (var face = new SolidBrush(Color.FromArgb(252, 244, 230))) g.FillEllipse(face, n * 0.2f, n * 0.12f, n * 0.6f, n * 0.5f);
        using var ink = new SolidBrush(Color.FromArgb(28, 24, 24));
        using var white = new SolidBrush(Color.White);
        using var pen = new Pen(Color.FromArgb(28, 24, 24), n * 0.018f);
        foreach (float x in new[] { 0.38f, 0.62f })
        {
            g.FillEllipse(white, n * (x - 0.08f), n * 0.26f, n * 0.16f, n * 0.16f);
            g.DrawEllipse(pen, n * (x - 0.08f), n * 0.26f, n * 0.16f, n * 0.16f);
        }
        g.FillEllipse(ink, n * 0.34f, n * 0.3f, n * 0.08f, n * 0.08f); // one eye painted in: the wish is made
        using var brow = new Pen(Color.FromArgb(28, 24, 24), n * 0.045f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(brow, n * 0.27f, n * 0.17f, n * 0.2f, n * 0.12f, 200, 140);
        g.DrawArc(brow, n * 0.53f, n * 0.17f, n * 0.2f, n * 0.12f, 200, 140);
        g.DrawArc(brow, n * 0.37f, n * 0.45f, n * 0.14f, n * 0.1f, 200, 140);  // moustache
        g.DrawArc(brow, n * 0.49f, n * 0.45f, n * 0.14f, n * 0.1f, 200, 140);
        using var beard = new Pen(Color.FromArgb(28, 24, 24), n * 0.012f);
        for (int k = 0; k < 5; k++) g.DrawLine(beard, n * (0.4f + k * 0.05f), n * 0.53f, n * (0.38f + k * 0.06f), n * 0.59f);
        using var gold = new Pen(Color.FromArgb(240, 190, 70), n * 0.025f);
        g.DrawArc(gold, n * 0.26f, n * 0.66f, n * 0.48f, n * 0.2f, 20, 140);
        g.DrawArc(gold, n * 0.34f, n * 0.72f, n * 0.32f, n * 0.14f, 20, 140);
    });

    static Mesh Kitsune()
    {
        var m = new Mesh();
        var pic = FrontPicture(0, 1, 0.8f, 0.9f);
        // the face: the front half of an egg (open at the back, seen from both sides) and a pointed snout
        Grid(m, 18, 24, (fi, fj) =>
        {
            float th = MathF.PI * fi, ph = MathF.PI + MathF.PI * fj;
            float x = 0.8f * MathF.Sin(th) * MathF.Cos(ph), y = 0.45f * MathF.Sin(th) * MathF.Sin(ph), z = 1 + 0.9f * MathF.Cos(th);
            return (new Vector3(x, y, z), new Vector3(x / 0.64f, y / 0.2f, (z - 1) / 0.81f));
        }, pic, Paint(Color.White), both: true);
        Grid(m, 10, 20, Ellipsoid(new(0, -0.42f, 0.78f), 0.24f, 0.36f, 0.22f), pic, Paint(Color.White));
        foreach (int sx in new[] { -1, 1 })
        {
            var ear = Rounded(new[] { new Vector2(sx * 0.75f, 1.45f), new Vector2(sx * 0.2f, 1.75f), new Vector2(sx * 0.68f, 2.2f) }, 2);
            Inflate(m, ear, 0.1f, Color.White, y0: -0.15f, uv: (p, back) => (0.5f, PlainV));
            var inner = ear.Select(p => new Vector2(sx * 0.55f, 1.8f) + (p - new Vector2(sx * 0.55f, 1.8f)) * 0.55f).ToList();
            Inflate(m, inner, 0.05f, Color.FromArgb(214, 32, 42), y0: -0.22f, uv: (p, back) => (0.5f, PlainV));
        }
        return m;
    }

    static Bitmap KitsuneTexture() => Canvas(256, Color.FromArgb(250, 250, 246), (g, n) =>
    {
        using var red = new Pen(Color.FromArgb(214, 32, 42), n * 0.022f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var ink = new SolidBrush(Color.FromArgb(24, 22, 26));
        foreach (int s in new[] { -1, 1 })
        {
            // slanted eye slits with red flicks above
            var eye = new[] { new PointF(n * (0.5f + s * 0.08f), n * 0.43f), new PointF(n * (0.5f + s * 0.24f), n * 0.36f), new PointF(n * (0.5f + s * 0.2f), n * 0.42f) };
            g.FillPolygon(ink, eye);
            g.DrawBezier(red, n * (0.5f + s * 0.07f), n * 0.37f, n * (0.5f + s * 0.14f), n * 0.3f, n * (0.5f + s * 0.24f), n * 0.3f, n * (0.5f + s * 0.3f), n * 0.27f);
            g.DrawLine(red, n * (0.5f + s * 0.3f), n * 0.5f, n * (0.5f + s * 0.38f), n * 0.48f);
            g.DrawLine(red, n * (0.5f + s * 0.3f), n * 0.55f, n * (0.5f + s * 0.38f), n * 0.55f);
        }
        using (var dot = new SolidBrush(Color.FromArgb(214, 32, 42))) g.FillEllipse(dot, n * 0.47f, n * 0.18f, n * 0.06f, n * 0.1f); // forehead mark
        g.FillEllipse(ink, n * 0.47f, n * 0.58f, n * 0.06f, n * 0.04f);                       // nose tip
        g.DrawArc(red, n * 0.44f, n * 0.62f, n * 0.12f, n * 0.06f, 20, 140);                   // mouth
    });

    static Mesh Slime()
    {
        var m = new Mesh();
        Lathe(m, new (float, float, Color)[]
        {
            (0, 1.8f, Color.White), (0.1f, 1.62f, Color.White), (0.3f, 1.3f, Color.White), (0.62f, 0.9f, Color.White),
            (0.84f, 0.55f, Color.White), (0.86f, 0.3f, Color.White), (0.72f, 0.08f, Color.White), (0.4f, 0, Color.White), (0, 0, Color.White),
        }, 48, (p, c, fj) => FrontPicture(0, 0.7f, 0.86f, 0.9f)(p, 0, fj));
        return m;
    }

    static Bitmap SlimeTexture() => Canvas(256, Color.White, (g, n) =>
    {
        AnimeEye(g, n * 0.4f, n * 0.5f, n * 0.08f, n * 0.11f);
        AnimeEye(g, n * 0.6f, n * 0.5f, n * 0.08f, n * 0.11f);
        using var pen = new Pen(Color.FromArgb(40, 34, 40), n * 0.014f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, n * 0.46f, n * 0.55f, n * 0.08f, n * 0.06f, 10, 160);
        // a highlight on the jelly
        using var shine = new SolidBrush(Color.FromArgb(255, 255, 255));
        g.FillEllipse(shine, n * 0.3f, n * 0.3f, n * 0.06f, n * 0.09f);
    });

    static Mesh Ramen()
    {
        var m = new Mesh();
        Color bowl = Color.FromArgb(200, 36, 40), black = Color.FromArgb(34, 28, 28), broth = Color.White;
        Lathe(m, new (float, float, Color)[]
        {
            (0, 0.92f, broth), (0.88f, 0.92f, broth), (0.88f, 0.92f, black), (0.95f, 1.0f, black), (1.0f, 0.98f, black), (1.0f, 0.98f, bowl),
            (0.95f, 0.6f, bowl), (0.76f, 0.26f, bowl), (0.56f, 0.13f, bowl), (0.56f, 0.13f, black), (0.5f, 0.12f, black), (0.5f, 0, black), (0, 0, black),
        }, 56, (p, c, fj) => c == broth ? (0.5f + 0.5f * p.X / 0.88f, (0.5f + 0.5f * p.Y / 0.88f) * 0.92f) : (fj, PlainV));
        // half an egg, a sheet of nori, chopsticks
        Grid(m, 8, 24, Ellipsoid(new(0.38f, 0.22f, 0.92f), 0.26f, 0.2f, 0.12f, 0, MathF.PI / 2), Solid, Paint(Color.FromArgb(250, 248, 240)));
        Grid(m, 6, 16, Ellipsoid(new(0.38f, 0.22f, 0.99f), 0.12f, 0.1f, 0.07f, 0, MathF.PI / 2), Solid, Paint(Color.FromArgb(255, 170, 40)));
        Extrude(m, new[] { new Vector2(-0.5f, 0.8f), new Vector2(0.05f, 0.8f), new Vector2(0.05f, 1.55f), new Vector2(-0.5f, 1.55f) },
                0.5f, 0.54f, Color.FromArgb(34, 52, 40));
        Color wood = Color.FromArgb(206, 152, 92);
        Tube(m, new[] { new Vector3(-1.15f, -0.3f, 1.02f), new Vector3(1.15f, 0.45f, 1.3f) }, 0.035f, wood);
        Tube(m, new[] { new Vector3(-1.15f, -0.12f, 1.02f), new Vector3(1.15f, 0.62f, 1.3f) }, 0.035f, wood);
        return m;
    }

    static Bitmap RamenTexture() => Canvas(256, Color.FromArgb(222, 150, 70), (g, n) =>
    {
        using (var noodle = new Pen(Color.FromArgb(252, 226, 130), n * 0.022f))
            for (int k = 0; k < 9; k++)
            {
                var pts = Enumerable.Range(0, 17).Select(i => new PointF(n * (0.08f + i * 0.053f), n * (0.12f + k * 0.085f + 0.025f * MathF.Sin(i * 1.3f + k)))).ToArray();
                g.DrawCurve(noodle, pts);
            }
        // naruto (fishcake with the pink swirl) and scallions
        float cx = n * 0.3f, cy = n * 0.32f, r = n * 0.12f;
        using (var white = new SolidBrush(Color.FromArgb(252, 250, 246))) g.FillEllipse(white, cx - r, cy - r, 2 * r, 2 * r);
        using (var pink = new Pen(Color.FromArgb(240, 90, 140), n * 0.018f))
        {
            var sp = Enumerable.Range(0, 40).Select(i => { float t = i / 39f; float a = t * 3.2f * MathF.PI; return new PointF(cx + r * 0.85f * t * MathF.Cos(a), cy + r * 0.85f * t * MathF.Sin(a)); }).ToArray();
            g.DrawCurve(pink, sp);
        }
        using var green = new SolidBrush(Color.FromArgb(90, 180, 70));
        foreach (var (x, y) in new[] { (0.55f, 0.25f), (0.62f, 0.3f), (0.5f, 0.6f), (0.7f, 0.55f), (0.3f, 0.65f), (0.4f, 0.72f), (0.66f, 0.7f) })
            g.FillEllipse(green, n * x, n * y, n * 0.035f, n * 0.035f);
        PlainBand(g, n, Color.White);
    });
}
