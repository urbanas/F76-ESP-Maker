using System.Drawing.Drawing2D;

namespace ItemFinder;

/// <summary>A small line icon for each category (tree and search results), so the lists don't read as one block of text.
/// Drawn on a 24-unit grid like the rail's icons; picked by the category's name, else by its section / sub-heading.</summary>
static class CatIcons
{
    public static string KindOf(Category c)
    {
        var n = c.Name.ToLowerInvariant();
        bool Has(params string[] w) => w.Any(n.Contains);
        switch (c.Section)
        {
            case "JUNK":
                return c.Group switch { "Metals" => "ingot", "Chemicals" => "flask", "Parts & tech" => "gear", "Materials" => "bricks", _ => "cube" };
            case "EVENTS":
                return c.Group switch { "Seasonal" => "sparkle", "Raids & expeditions" => "shield", _ => "flag" };
            case "ENEMIES":
                return Has("robot") ? "robot"
                     : Has("boss") ? "crown"
                     : Has("legendary", "bounty") ? "star"
                     : Has("event") ? "flag"
                     : Has("insect", "bug") ? "bug"
                     : Has("ghoul", "scorched", "mutant", "lost", "goliath") ? "skull"
                     : Has("animal", "creature", "mirelurk", "cryptid") ? "paw"
                     : Has("human", "raider", "merc", "cultist", "fanatic", "communist") ? "person"
                     : "target";
        }
        if (c.Section == Catalog.AllSection) return "cube";
        return Has("bobble") ? "bobble"
             : Has("magazine") ? "magazine"
             : Has("recipe") ? "pot"
             : Has("xp food") ? "sprout"
             : Has("plan") && !Has("plant") ? "plan"
             : Has("loot spot") ? "pin"
             : Has("pre-war") ? "can"
             : Has("note", "holotape") ? "tape"
             : Has("plant") ? "leaf"
             : Has("aid") ? "aid"
             : Has("junk") ? "wrench"
             : Has("weapon") ? "gun"
             : Has("vending") ? "vending"
             : Has("workbench") ? "hammer"
             : Has("camp items") ? "tent"
             : Has("bag") ? "sack"
             : Has("storage", "box") ? "crate"
             : Has("clothes", "apparel") ? "shirt"
             : "cube";
    }

    public static void Draw(Graphics g, Category c, RectangleF r, Color colour) => Draw(g, KindOf(c), r, colour);

    public static void Draw(Graphics g, string kind, RectangleF r, Color colour)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 24f, r.Height / 24f);
        using var p = new Pen(colour, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        static PointF[] P(params float[] xy) { var a = new PointF[xy.Length / 2]; for (int i = 0; i < a.Length; i++) a[i] = new(xy[i * 2], xy[i * 2 + 1]); return a; }
        void Rr(float x, float y, float w, float h, float rad) { using var path = Ui.Round(new RectangleF(x, y, w, h), rad); g.DrawPath(p, path); }
        switch (kind)
        {
            case "bobble": // big head on a small body, on a stand
                g.DrawEllipse(p, 6, 2, 12, 11);
                g.DrawLine(p, 12, 13, 12, 15);
                g.DrawPolygon(p, P(9, 15, 15, 15, 16, 19, 8, 19));
                g.DrawLine(p, 5, 21.5f, 19, 21.5f);
                break;
            case "magazine":
                Rr(5, 3, 14, 18, 1.5f);
                g.DrawLine(p, 8, 7, 16, 7);
                g.DrawLine(p, 8, 11, 16, 11);
                g.DrawLine(p, 8, 14.5f, 13, 14.5f);
                break;
            case "pot": // cooking pot with steam
                g.DrawLine(p, 3, 11, 21, 11);
                g.DrawArc(p, 5, 3, 14, 16, 0, 180);
                g.DrawBezier(p, 9, 8, 8, 6.5f, 10, 5.5f, 9, 3);
                g.DrawBezier(p, 15, 8, 14, 6.5f, 16, 5.5f, 15, 3);
                break;
            case "sprout": // a stem with two leaves and an "XP" spark
                g.DrawLine(p, 12, 21, 12, 11);
                g.DrawBezier(p, 12, 14, 7, 14, 4, 11, 4, 7);
                g.DrawBezier(p, 4, 7, 9, 7, 12, 10, 12, 14);
                g.DrawBezier(p, 12, 11, 12, 7, 15, 4, 20, 4);
                g.DrawBezier(p, 20, 4, 20, 9, 17, 11, 12, 11);
                break;
            case "leaf":
                g.DrawBezier(p, 4, 20, 4, 9, 11, 4, 20, 4);
                g.DrawBezier(p, 20, 4, 20, 13, 15, 20, 4, 20);
                g.DrawLine(p, 4, 20, 14, 10);
                break;
            case "plan": // a sheet with a folded corner and a little house drawn on it
                g.DrawPolygon(p, P(5, 3, 15, 3, 19, 7, 19, 21, 5, 21));
                g.DrawLines(p, P(15, 3, 15, 7, 19, 7));
                g.DrawPolygon(p, P(8.5f, 18, 8.5f, 13.5f, 12, 10.5f, 15.5f, 13.5f, 15.5f, 18));
                break;
            case "pin": // map pin
                using (var path = new GraphicsPath())
                {
                    path.AddArc(5, 2, 14, 14, 140, 260);
                    path.AddLine(17.4f, 13.5f, 12, 21.5f);
                    path.CloseFigure();
                    g.DrawPath(p, path);
                }
                g.DrawEllipse(p, 9.5f, 6.5f, 5, 5);
                break;
            case "can": // a tin can
                g.DrawEllipse(p, 6, 3, 12, 4);
                g.DrawLine(p, 6, 5, 6, 19);
                g.DrawLine(p, 18, 5, 18, 19);
                g.DrawArc(p, 6, 17, 12, 4, 0, 180);
                g.DrawLine(p, 6, 10, 18, 10);
                g.DrawLine(p, 6, 14, 18, 14);
                break;
            case "tape": // a holotape / cassette
                Rr(2.5f, 5, 19, 14, 2);
                g.DrawEllipse(p, 6, 8.5f, 4, 4);
                g.DrawEllipse(p, 14, 8.5f, 4, 4);
                g.DrawPolygon(p, P(7, 19, 8.5f, 15.5f, 15.5f, 15.5f, 17, 19));
                break;
            case "aid": // first-aid cross in a box
                Rr(3.5f, 3.5f, 17, 17, 3);
                g.DrawLine(p, 12, 8, 12, 16);
                g.DrawLine(p, 8, 12, 16, 12);
                break;
            case "wrench":
                g.DrawLine(p, 4.5f, 19.5f, 12.5f, 11.5f);
                g.DrawArc(p, 11, 3, 10, 10, 160, 290);
                break;
            case "gun": // a pistol
                g.DrawPolygon(p, P(3, 7, 21, 7, 21, 11.5f, 11, 11.5f, 9.5f, 19, 5, 19, 6.5f, 11.5f, 3, 11.5f));
                g.DrawArc(p, 9.5f, 9.5f, 4, 5, 0, 180);
                break;
            case "vending": // a vending machine
                Rr(5, 2, 14, 20, 1.5f);
                Rr(7.5f, 4.5f, 6, 11, 0.8f);
                g.DrawLine(p, 16, 6, 16, 10);
                g.DrawLine(p, 8, 18.5f, 16, 18.5f);
                break;
            case "hammer":
                g.DrawPolygon(p, P(4, 5, 16, 5, 18, 7, 18, 10, 4, 10));
                g.DrawLine(p, 11, 10, 11, 21);
                break;
            case "sack": // a tied bag
                g.DrawLines(p, P(9.5f, 8, 8, 4, 16, 4, 14.5f, 8));
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(9.5f, 8, 3, 11, 3, 21, 12, 21);
                    path.AddBezier(12, 21, 21, 21, 21, 11, 14.5f, 8);
                    g.DrawPath(p, path);
                }
                g.DrawLine(p, 9, 8, 15, 8);
                break;
            case "tent": // a CAMP tent with its door flap
                g.DrawPolygon(p, P(2.5f, 20, 12, 4, 21.5f, 20));
                g.DrawLines(p, P(12, 4, 9, 20));
                g.DrawLines(p, P(12, 4, 15, 20));
                g.DrawLine(p, 12, 4, 13.5f, 2);
                break;
            case "crate": // a box with a lid and a latch
                Rr(3, 9, 18, 11, 1.5f);
                Rr(2, 5, 20, 4, 1);
                g.DrawRectangle(p, 10, 11, 4, 3);
                break;
            case "shirt":
                g.DrawPolygon(p, P(8.5f, 3, 3, 6, 1.5f, 11, 5.5f, 12, 5.5f, 21, 18.5f, 21, 18.5f, 12, 22.5f, 11, 21, 6, 15.5f, 3));
                g.DrawArc(p, 8.5f, 0.5f, 7, 5, 0, 180);
                break;
            case "ingot": // two stacked metal bars
                g.DrawPolygon(p, P(2.5f, 19, 5, 13, 19, 13, 21.5f, 19));
                g.DrawPolygon(p, P(6.5f, 13, 9, 7, 15, 7, 17.5f, 13));
                break;
            case "flask":
                g.DrawPolygon(p, P(10, 3, 14, 3, 14, 9, 19.5f, 19, 18.5f, 21, 5.5f, 21, 4.5f, 19, 10, 9));
                g.DrawLine(p, 7.5f, 15, 16.5f, 15);
                break;
            case "gear":
                g.DrawEllipse(p, 8.5f, 8.5f, 7, 7);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(p, 12 + (float)Math.Cos(a) * 5.5f, 12 + (float)Math.Sin(a) * 5.5f, 12 + (float)Math.Cos(a) * 8.5f, 12 + (float)Math.Sin(a) * 8.5f);
                }
                break;
            case "bricks":
                Rr(3, 5, 18, 14, 1);
                g.DrawLine(p, 3, 12, 21, 12);
                g.DrawLine(p, 9, 5, 9, 12);
                g.DrawLine(p, 15, 5, 15, 12);
                g.DrawLine(p, 12, 12, 12, 19);
                break;
            case "sparkle":
                g.DrawPolygon(p, P(12, 2.5f, 14, 10, 21.5f, 12, 14, 14, 12, 21.5f, 10, 14, 2.5f, 12, 10, 10));
                break;
            case "flag":
                g.DrawLine(p, 6, 21, 6, 3);
                g.DrawPolygon(p, P(6, 4, 19.5f, 4, 16, 8.5f, 19.5f, 13, 6, 13));
                break;
            case "shield":
                g.DrawPolygon(p, P(12, 2.5f, 20, 5.5f, 19, 14, 12, 21.5f, 5, 14, 4, 5.5f));
                g.DrawLine(p, 12, 7, 12, 17);
                break;
            case "robot":
                Rr(4.5f, 7, 15, 13, 2.5f);
                g.DrawLine(p, 12, 7, 12, 4);
                g.DrawEllipse(p, 10.75f, 1.5f, 2.5f, 2.5f);
                g.DrawEllipse(p, 7.5f, 10.5f, 3, 3);
                g.DrawEllipse(p, 13.5f, 10.5f, 3, 3);
                g.DrawLine(p, 9, 16.5f, 15, 16.5f);
                break;
            case "crown":
                g.DrawPolygon(p, P(3, 18, 3, 7, 8, 12, 12, 5, 16, 12, 21, 7, 21, 18));
                break;
            case "star":
                g.DrawPolygon(p, P(12, 3, 14.6f, 8.6f, 20.6f, 9.3f, 16.1f, 13.4f, 17.3f, 19.4f, 12, 16.4f, 6.7f, 19.4f, 7.9f, 13.4f, 3.4f, 9.3f, 9.4f, 8.6f));
                break;
            case "bug":
                g.DrawEllipse(p, 8, 8, 8, 13);
                g.DrawEllipse(p, 9.5f, 3, 5, 5);
                foreach (var (y1, y2, dx) in new[] { (11f, 8f, 4f), (14f, 14f, 5f), (17f, 20f, 4f) })
                {
                    g.DrawLine(p, 8, y1, 8 - dx, y2);
                    g.DrawLine(p, 16, y1, 16 + dx, y2);
                }
                break;
            case "skull":
                g.DrawArc(p, 4.5f, 2.5f, 15, 14, 160, 220);
                g.DrawLines(p, P(5, 12.5f, 8, 15.5f, 8, 20.5f, 16, 20.5f, 16, 15.5f, 19, 12.5f));
                g.DrawEllipse(p, 7.5f, 8.5f, 3, 3);
                g.DrawEllipse(p, 13.5f, 8.5f, 3, 3);
                break;
            case "paw":
                g.DrawEllipse(p, 7, 12, 10, 8.5f);
                g.DrawEllipse(p, 3, 8, 3.5f, 4.5f);
                g.DrawEllipse(p, 7.5f, 3.5f, 3.5f, 4.5f);
                g.DrawEllipse(p, 13, 3.5f, 3.5f, 4.5f);
                g.DrawEllipse(p, 17.5f, 8, 3.5f, 4.5f);
                break;
            case "person":
                g.DrawEllipse(p, 8, 2.5f, 8, 8);
                g.DrawArc(p, 4, 13, 16, 16, 180, 180);
                g.DrawLine(p, 4, 21, 20, 21);
                break;
            case "target":
                g.DrawEllipse(p, 5, 5, 14, 14);
                g.DrawLine(p, 12, 2, 12, 7); g.DrawLine(p, 12, 17, 12, 22);
                g.DrawLine(p, 2, 12, 7, 12); g.DrawLine(p, 17, 12, 22, 12);
                break;
            default: // a box
                g.DrawPolygon(p, P(4, 8, 12, 4, 20, 8, 20, 16, 12, 20, 4, 16));
                g.DrawLines(p, P(4, 8, 12, 12, 20, 8));
                g.DrawLine(p, 12, 12, 12, 20);
                break;
        }
        g.Restore(state);
    }
}
