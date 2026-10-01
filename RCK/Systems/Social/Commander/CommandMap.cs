#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The commander console's map (see <see cref="WarPanel"/>): the level's walls and floors, every turf tinted in its
    ///   holders' colours (dotted for rackets, striped while under attack, a white edge for the commanded faction's), a
    ///   numbered badge per turf, the player's squads as numbered discs with lines to where they're headed, and other
    ///   faction members as dots. Click a squad (or the console's squad chips), then a turf to attack it, or one of
    ///   your own to defend it, or open ground to move there; right-click clears the pick. Host only.
    /// </summary>
    internal static class CommandMap
    {
        private const int Size = TurfShapes.Size, Px = 4;
        private const float Tile = TurfShapes.Tile;
        private const float TurfSeconds = 0.5f, DotsSeconds = 0.5f, BaseSeconds = 20f;
        private const float SquadRadius = 11f, PickRadius = 14f, PingSeconds = 0.7f;
        private const byte FillAlpha = 110, StripeAlpha = 215, EdgeAlpha = 240;

        /// <summary>Order lines: red to attack, green to hold, yellow to move; grey for a turf they can't be sent to.</summary>
        private static readonly Color AttackColour = new Color(1f, 0.35f, 0.3f), HoldColour = new Color(0.55f, 0.95f, 0.55f);
        private static readonly Color RefusedColour = new Color(0.62f, 0.62f, 0.62f);

        private static Vector2 pingWorld;
        private static Color pingColour;
        private static float pingAt = float.MinValue;

        private static Texture2D baseTex, turfTex;
        private static Color32[] basePixels, turfPixels;
        private static readonly bool[] walkable = new bool[Size * Size];
        private static int minX, minY, span = Size;
        private static int baseStamp = int.MinValue;
        private static float baseBuilt = float.MinValue;
        private static bool loggedBase;

        private static int turfVersion = int.MinValue;
        private static long turfSignature;
        private static float nextTurf;
        private static readonly List<Vector2> badges = new List<Vector2>();
        private static readonly List<int> bits = new List<int>();

        private struct Dot
        {
            public Vector2 At;
            public Color32 Colour;
        }

        private static readonly List<Dot> dots = new List<Dot>();
        private static float nextDots;

        private static Command.Team selected;
        private static bool all;

        static CommandMap() => LevelScope.ResetAtBoth(Reset);

        private static void Reset()
        {
            selected = null;
            all = false;
        }

        /// <summary>The squad orders go to (null with <see cref="All"/> or with nothing picked).</summary>
        internal static Command.Team Selected
        {
            get
            {
                if (selected != null && !Contains(Command.Teams, selected)) selected = null;
                return selected;
            }
            set
            {
                selected = value;
                if (value != null) all = false;
            }
        }

        /// <summary>True while orders go to every squad.</summary>
        internal static bool All
        {
            get => all;
            set
            {
                all = value;
                if (value) selected = null;
            }
        }

        /// <summary>True when a squad (or every squad) is picked.</summary>
        internal static bool Picked => All || Selected != null;

        /// <summary>
        ///   Who a map click orders: every squad (<paramref name="to"/> null), the picked one, or the only squad when
        ///   there's just one. False when nobody would get the order.
        /// </summary>
        internal static bool Orders(out Command.Team to)
        {
            to = null;
            if (All) return true;
            to = Selected;
            if (to != null) return true;
            IReadOnlyList<Command.Team> teams = Command.Teams;
            if (teams.Count != 1) return false;
            to = teams[0];
            return true;
        }

        private static bool Contains(IReadOnlyList<Command.Team> teams, Command.Team t)
        {
            foreach (Command.Team x in teams)
                if (x == t) return true;
            return false;
        }

        // ---- Drawing ----

        /// <summary>Draws the map in <paramref name="r"/> (window coordinates) and handles its clicks.</summary>
        internal static void Draw(Rect r, GameController gc, int key)
        {
            WarStyle.Fill(r, new Color(0.05f, 0.05f, 0.06f, 1f));
            if (!TurfShapes.LevelReady(gc))
            {
                WarStyle.Text(r, "  No map in this level.", WarStyle.Small);
                return;
            }
            EnsureBase(gc);
            bool shapes = TurfShapes.Ensure(gc);
            if (shapes) EnsureTurf(gc, key);

            Event e = Event.current;
            Vector2 mouse = e.mousePosition;
            bool hover = r.Contains(mouse);
            IReadOnlyList<Command.Team> teams = Command.Teams;

            if (e.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, baseTex);
                if (shapes && turfTex != null) GUI.DrawTexture(r, turfTex);
                if (shapes) DrawBadges(gc, r);
                DrawDots(gc, r, key);
                DrawSquads(gc, r, key, teams);
                DrawPlayer(gc, r);
                DrawPing(r);
                WarStyle.Frame(r, new Color(0.95f, 0.95f, 0.95f, 0.38f), 2f);
                if (hover) DrawTip(gc, r, key, teams, mouse, shapes);
                if (!Picked)
                {
                    string hint = teams.Count == 0 ? "Recruit a squad (on the right) to give orders."
                        : teams.Count == 1 ? $"Click the map to send {teams[0].Label}."
                        : "Click a squad to give it orders.";
                    WarStyle.Text(new Rect(r.x + 8f, r.yMax - 26f, r.width - 16f, 22f), hint, WarStyle.Small);
                }
            }
            else if (e.type == EventType.MouseDown && hover)
            {
                Click(gc, r, key, teams, mouse, e.button, shapes);
                e.Use();
            }
        }

        private static Vector2 ToMap(Rect r, Vector2 world)
        {
            float fx = world.x / Tile - minX + 0.5f, fy = world.y / Tile - minY + 0.5f;
            return new Vector2(r.x + fx * r.width / span, r.yMax - fy * r.height / span);
        }

        private static Vector2 TileCentre(Rect r, float tx, float ty)
            => new Vector2(r.x + (tx - minX + 0.5f) * r.width / span, r.yMax - (ty - minY + 0.5f) * r.height / span);

        private static Vector2 ToWorld(Rect r, Vector2 p)
            => new Vector2(((p.x - r.x) * span / r.width + minX - 0.5f) * Tile, ((r.yMax - p.y) * span / r.height + minY - 0.5f) * Tile);

        private static void ToTile(Rect r, Vector2 p, out int tx, out int ty)
        {
            tx = Mathf.FloorToInt((p.x - r.x) * span / r.width) + minX;
            ty = Mathf.FloorToInt((r.yMax - p.y) * span / r.height) + minY;
        }

        private static void DrawBadges(GameController gc, Rect r)
        {
            bool rackets = WarConfig.RacketsOn(gc);
            IReadOnlyList<TurfShapes.Shape> all = TurfShapes.All;
            for (int i = 0; i < all.Count && i < badges.Count; i++)
            {
                TurfWar.Turf t = all[i].Turf;
                if (t.Common && (t.Ruined || !rackets)) continue;
                Vector2 c = TileCentre(r, badges[i].x, badges[i].y);
                string text = (t.Common ? "$" : "") + (i + 1);
                float w = Mathf.Max(18f, 8f + 8f * text.Length);
                var box = new Rect(c.x - w / 2f, c.y - 8f, w, 16f);
                Color edge = t.Current != 0 ? (Color)TurfOverlay.ColorsOf(Factions.LowestBit(t.Current)).Value : t.Common ? (Color)TurfOverlay.FreeRacket : (Color)TurfOverlay.Cleared;
                WarStyle.Fill(box, new Color(0f, 0f, 0f, 0.78f));
                WarStyle.Frame(box, edge);
                WarStyle.Text(box, text, WarStyle.Badge);
            }
        }

        private static void DrawDots(GameController gc, Rect r, int key)
        {
            float now = Time.unscaledTime;
            if (now >= nextDots || now < nextDots - DotsSeconds)
            {
                nextDots = now + DotsSeconds;
                CollectDots(gc);
            }
            foreach (Dot d in dots)
            {
                Vector2 p = ToMap(r, d.At);
                if (!r.Contains(p)) continue;
                WarStyle.Dot(p, 7f, new Color(0f, 0f, 0f, 0.85f));
                WarStyle.Dot(p, 5f, d.Colour);
            }
        }

        private static void CollectDots(GameController gc)
        {
            dots.Clear();
            if (gc.agentList == null) return;
            var mine = new HashSet<Agent>();
            foreach (Command.Team t in Command.Teams)
                foreach (Agent m in Command.Living(t)) mine.Add(m);
            ulong common = Factions.CommonFolkBit;
            foreach (Agent a in gc.agentList)
            {
                if (dots.Count >= 600) break;
                if (a == null || a.dead || a.ghost || a.disappeared || a.isPlayer > 0 || mine.Contains(a)) continue;
                ulong keys = Factions.KeysOf(a);
                if (keys == 0) continue;
                ulong own = keys & ~common;
                Color32 c = own != 0 ? TurfOverlay.ColorsOf(Factions.LowestBit(own)).Key : TurfOverlay.FreeRacket;
                dots.Add(new Dot { At = a.tr.position, Colour = c });
            }
        }

        private static void DrawSquads(GameController gc, Rect r, int key, IReadOnlyList<Command.Team> teams)
        {
            Color fill = TurfOverlay.ColorsOf(key).Key, edge = TurfOverlay.ColorsOf(key).Value;
            Command.Team pick = Selected;
            bool everyone = All;
            foreach (Command.Team t in teams)
            {
                bool on = everyone || t == pick || (pick == null && teams.Count == 1);
                Vector2 at = SquadAt(r, t, out Agent lead);
                if (lead == null) continue;
                SquadOrders.Order o = SquadOrders.Of(lead);
                if (o != null && o.HasGoal)
                {
                    Vector2 goal = ToMap(r, o.Goal);
                    Color line = o.Target != null ? AttackColour : o.Holding != null ? HoldColour : WarStyle.Yellow;
                    line.a = on ? 1f : 0.55f;
                    if (Vector2.Distance(at, goal) > SquadRadius + 4f)
                    {
                        Dotted(at, goal, line);
                        WarStyle.Dot(goal, 9f, new Color(0f, 0f, 0f, line.a), WarStyle.Ring);
                        WarStyle.Dot(goal, 8f, line, WarStyle.Ring);
                    }
                }
                foreach (Agent m in Command.Living(t))
                {
                    Vector2 p = ToMap(r, m.tr.position);
                    if (!r.Contains(p)) continue;
                    WarStyle.Dot(p, 8f, new Color(0f, 0f, 0f, 0.9f));
                    WarStyle.Dot(p, 6f, edge);
                }
            }
            foreach (Command.Team t in teams)
            {
                Vector2 at = SquadAt(r, t, out Agent lead);
                if (lead == null) continue;
                if (everyone || t == pick || (pick == null && teams.Count == 1))
                {
                    float pulse = 34f + 5f * Mathf.PingPong(Time.unscaledTime * 3f, 1f);
                    WarStyle.Dot(at, pulse, WarStyle.Yellow, WarStyle.Ring);
                }
                WarStyle.Dot(at, SquadRadius * 2f + 3f, Color.black);
                WarStyle.Dot(at, SquadRadius * 2f, fill);
                WarStyle.Dot(at, SquadRadius * 2f, edge, WarStyle.Ring);
                WarStyle.Text(new Rect(at.x - 12f, at.y - 9f, 24f, 18f), t.Number.ToString(), WarStyle.Badge);
            }
        }

        /// <summary>Where <paramref name="t"/> is drawn: its member nearest the squad's middle.</summary>
        private static Vector2 SquadAt(Rect r, Command.Team t, out Agent lead)
        {
            lead = null;
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (Agent m in Command.Living(t))
            {
                sum += (Vector2)m.tr.position;
                n++;
            }
            if (n == 0) return Vector2.zero;
            Vector2 mid = sum / n;
            float best = float.MaxValue;
            foreach (Agent m in Command.Living(t))
            {
                float d = Vector2.Distance(mid, m.tr.position);
                if (d < best)
                {
                    best = d;
                    lead = m;
                }
            }
            return ToMap(r, lead.tr.position);
        }

        private static void DrawPlayer(GameController gc, Rect r)
        {
            Agent p = gc.playerAgent;
            if (p == null || p.dead) return;
            Vector2 at = ToMap(r, p.tr.position);
            WarStyle.Dot(at, 12f, Color.black);
            WarStyle.Dot(at, 9f, Color.white);
            WarStyle.Text(new Rect(at.x + 7f, at.y - 18f, 60f, 18f), "You", WarStyle.Small);
        }

        private static void Dotted(Vector2 a, Vector2 b, Color c)
        {
            float length = Vector2.Distance(a, b);
            if (length < 1f || length > 4000f) return;
            Vector2 dir = (b - a) / length;
            for (float s = SquadRadius + 2f; s < length - 4f; s += 7f)
            {
                Vector2 p = a + dir * s;
                WarStyle.Fill(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), new Color(0f, 0f, 0f, c.a * 0.8f));
                WarStyle.Fill(new Rect(p.x - 1.5f, p.y - 1.5f, 3f, 3f), c);
            }
        }

        private static Command.Team SquadUnder(Rect r, IReadOnlyList<Command.Team> teams, Vector2 mouse)
        {
            Command.Team best = null;
            float bestDistance = PickRadius;
            foreach (Command.Team t in teams)
            {
                Vector2 at = SquadAt(r, t, out Agent lead);
                if (lead == null) continue;
                float d = Vector2.Distance(at, mouse);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = t;
                }
            }
            return best;
        }

        private static void DrawTip(GameController gc, Rect r, int key, IReadOnlyList<Command.Team> teams, Vector2 mouse, bool shapes)
        {
            string tip = null;
            bool orders = Orders(out Command.Team to);
            Command.Team team = SquadUnder(r, teams, mouse);
            if (team != null)
            {
                tip = $"{team.Label}: {Command.Roll(team)}\n{Command.Doing(gc, team)}";
                tip += team == Selected && !All ? "\nClick: let go" : "\nClick: pick it";
            }
            else
            {
                ToTile(r, mouse, out int tx, out int ty);
                TurfShapes.Shape s = shapes ? TurfShapes.At(tx, ty) : null;
                if (s != null && s.Turf.Common && (s.Turf.Ruined || !WarConfig.RacketsOn(gc))) s = null;
                if (s != null)
                {
                    TurfWar.Turf t = s.Turf;
                    int holders = 0;
                    foreach (Agent h in t.Holders)
                        if (h != null && !TurfWar.IsResolved(h)) holders++;
                    tip = $"{Command.Place(gc, t)}\n{holders} holding{(TurfShapes.Contested(t) ? ", under attack" : "")}";
                    if (orders)
                    {
                        Command.Aim aim = Command.Intent(gc, t, out string intent);
                        if (intent.Length > 0) tip += "\n" + intent;
                        Color c = ColourOf(aim);
                        Outline(r, s, c);
                        if (aim != Command.Aim.None) Preview(r, to, mouse, c);
                    }
                }
                else if (orders && Walkable(tx, ty))
                {
                    tip = "Click: move here";
                    Preview(r, to, mouse, WarStyle.Yellow);
                }
            }
            if (tip == null) return;
            var content = new GUIContent(tip);
            Vector2 size = WarStyle.Tip.CalcSize(content);
            size.x = Mathf.Min(size.x, 320f);
            size.y = WarStyle.Tip.CalcHeight(content, size.x);
            float x = mouse.x + 16f, y = mouse.y + 16f;
            if (x + size.x > r.xMax) x = mouse.x - 8f - size.x;
            if (y + size.y > r.yMax) y = mouse.y - 8f - size.y;
            GUI.Label(new Rect(x, y, size.x, size.y), content, WarStyle.Tip);
        }

        private static Color ColourOf(Command.Aim aim)
            => aim == Command.Aim.Defend ? HoldColour : aim == Command.Aim.None ? RefusedColour : AttackColour;

        /// <summary>Rings the edge of <paramref name="s"/>, pulsing, to show which turf a click would send them to.</summary>
        private static void Outline(Rect r, TurfShapes.Shape s, Color c)
        {
            float size = Mathf.Max(2f, r.width / span);
            c.a = 0.55f + 0.4f * Mathf.PingPong(Time.unscaledTime * 2.5f, 1f);
            for (int i = 0; i < s.Tiles.Count && i < s.Edges.Count; i++)
            {
                if (!s.Edges[i]) continue;
                int at = s.Tiles[i];
                Vector2 p = TileCentre(r, at % Size, at / Size);
                WarStyle.Fill(new Rect(p.x - size / 2f, p.y - size / 2f, size, size), c);
            }
        }

        /// <summary>Faint dotted lines from the squads a click would order (<paramref name="to"/> null: all) to the mouse.</summary>
        private static void Preview(Rect r, Command.Team to, Vector2 mouse, Color c)
        {
            c.a = 0.6f;
            foreach (Command.Team t in Command.Teams)
            {
                if (to != null && t != to) continue;
                Vector2 at = SquadAt(r, t, out Agent lead);
                if (lead != null && Vector2.Distance(at, mouse) > SquadRadius + 4f) Dotted(at, mouse, c);
            }
        }

        private static void Ping(Vector2 world, Color c)
        {
            pingWorld = world;
            pingColour = c;
            pingAt = Time.unscaledTime;
        }

        /// <summary>A ring that grows and fades where the last order was given.</summary>
        private static void DrawPing(Rect r)
        {
            float age = Time.unscaledTime - pingAt;
            if (age < 0f || age > PingSeconds) return;
            float k = age / PingSeconds;
            Vector2 at = ToMap(r, pingWorld);
            Color c = pingColour;
            c.a = 1f - k;
            WarStyle.Dot(at, 12f + 30f * k, new Color(0f, 0f, 0f, c.a * 0.7f), WarStyle.Ring);
            WarStyle.Dot(at, 10f + 30f * k, c, WarStyle.Ring);
        }

        // ---- Clicks ----

        private static void Click(GameController gc, Rect r, int key, IReadOnlyList<Command.Team> teams, Vector2 mouse, int button, bool shapes)
        {
            if (button == 1)
            {
                Selected = null;
                All = false;
                return;
            }
            if (button != 0) return;
            Command.Team hit = SquadUnder(r, teams, mouse);
            if (hit != null)
            {
                Selected = hit == Selected && !All ? null : hit;
                return;
            }
            if (!Orders(out Command.Team to))
            {
                Command.Note(teams.Count == 0 ? "Recruit a squad first." : "Pick a squad first: click one on the map or in the list.");
                return;
            }
            ToTile(r, mouse, out int tx, out int ty);
            Vector2 world = ToWorld(r, mouse);
            TurfShapes.Shape s = shapes ? TurfShapes.At(tx, ty) : null;
            if (s != null && !(s.Turf.Common && (s.Turf.Ruined || !WarConfig.RacketsOn(gc))))
            {
                Command.Aim aim = Command.Intent(gc, s.Turf, out _);
                if (Command.Capture(to, s.Turf)) Ping(world, ColourOf(aim));
                return;
            }
            if (!Walkable(tx, ty)) Command.Note("They can't go there.");
            else if (Command.Move(to, world)) Ping(world, WarStyle.Yellow);
        }

        private static bool Walkable(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && walkable[y * Size + x];

        // ---- The level's terrain ----

        private static readonly Color32 Void = new Color32(12, 12, 14, 255);
        private static readonly Color32 Wall = new Color32(48, 44, 52, 255), WallEdge = new Color32(92, 86, 98, 255);
        private static readonly Color32 WallWood = new Color32(80, 58, 40, 255), WallSteel = new Color32(70, 78, 88, 255);
        private static readonly Color32 Fence = new Color32(120, 120, 135, 255), Glass = new Color32(140, 195, 230, 255);
        private static readonly Color32 DoorColour = new Color32(170, 150, 110, 255);
        private static readonly Color32 WaterColour = new Color32(46, 96, 170, 255), IceColour = new Color32(175, 215, 235, 255);
        private static readonly Color32 HoleColour = new Color32(6, 6, 8, 255), Ooze = new Color32(90, 170, 60, 255);
        private static readonly Color32 Grass = new Color32(62, 112, 56, 255), Dirt = new Color32(112, 90, 64, 255);
        private static readonly Color32 Wood = new Color32(128, 96, 64, 255), Fire = new Color32(200, 90, 40, 255);
        private static readonly Color32 Indoor = new Color32(150, 140, 125, 255), Street = new Color32(88, 88, 92, 255);
        private static readonly Color32 Belt = new Color32(105, 100, 80, 255);

        private static void EnsureBase(GameController gc)
        {
            float now = Time.unscaledTime;
            if (!LevelScope.IsNew(ref baseStamp) && baseTex != null && now - baseBuilt < BaseSeconds && now >= baseBuilt) return;
            baseBuilt = now;
            BuildBase(gc);
        }

        private static void BuildBase(GameController gc)
        {
            TileData[,] tiles = gc.tileInfo.tileArray;
            tk2dTileMap walls = gc.tileInfo.tilemapWalls, floors = gc.tileInfo.tilemapFloors;
            int w = Math.Min(tiles.GetLength(0), Size), h = Math.Min(tiles.GetLength(1), Size);

            var wall = new bool[Size * Size];
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (walls.GetTile(x, y, 0) == -1) continue;
                    wall[y * Size + x] = true;
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
            if (x1 < 0)
            {
                x0 = y0 = 0;
                x1 = w - 1;
                y1 = h - 1;
            }
            x0 = Math.Max(0, x0 - 1);
            y0 = Math.Max(0, y0 - 1);
            x1 = Math.Min(Size - 1, x1 + 1);
            y1 = Math.Min(Size - 1, y1 + 1);
            int s = Math.Max(x1 - x0 + 1, y1 - y0 + 1);
            minX = Mathf.Clamp(x0 - (s - (x1 - x0 + 1)) / 2, 0, Size - s);
            minY = Mathf.Clamp(y0 - (s - (y1 - y0 + 1)) / 2, 0, Size - s);
            if (s != span || baseTex == null)
            {
                span = s;
                Recreate(ref baseTex, "RCK_CommandMap", span * Px);
                Recreate(ref turfTex, "RCK_CommandTurf", span * Px);
                basePixels = new Color32[span * Px * span * Px];
                turfPixels = new Color32[span * Px * span * Px];
            }
            turfVersion = int.MinValue;

            // Tiles with no floor are void; if most tiles have none, the floor map isn't telling us that, so ignore it.
            int empty = 0, counted = 0;
            if (floors != null)
                for (int x = minX; x < minX + span; x++)
                    for (int y = minY; y < minY + span; y++)
                    {
                        if (x >= w || y >= h) continue;
                        counted++;
                        if (!wall[y * Size + x] && floors.GetTile(x, y, 0) == -1) empty++;
                    }
            bool useFloors = floors != null && counted > 0 && empty * 10 < counted * 7;

            Array.Clear(walkable, 0, walkable.Length);
            int texSize = span * Px, voids = 0;
            for (int ty = 0; ty < span; ty++)
            {
                for (int tx = 0; tx < span; tx++)
                {
                    int x = minX + tx, y = minY + ty;
                    TileData td = x < w && y < h ? tiles[x, y] : null;
                    bool isWall = x < w && y < h && wall[y * Size + x];
                    Color32 c = Colour(td, isWall, useFloors && !isWall && floors.GetTile(x, y, 0) == -1, out bool walk);
                    if (isWall && Exposed(wall, x, y, w, h) && (td == null || td.wallMaterial == wallMaterialType.Normal || td.wallMaterial == wallMaterialType.Border || td.wallMaterial == wallMaterialType.None))
                        c = WallEdge;
                    if (c.Equals(Void)) voids++;
                    walkable[y * Size + x] = walk;
                    int shade = ((x + y) & 1) == 0 ? 3 : -3;
                    if (!isWall && (x % 16 == 0 || y % 16 == 0)) shade -= 6;
                    c = Shade(c, shade);
                    for (int py = 0; py < Px; py++)
                    {
                        int row = (ty * Px + py) * texSize + tx * Px;
                        for (int px = 0; px < Px; px++) basePixels[row + px] = c;
                    }
                }
            }
            baseTex.SetPixels32(basePixels);
            baseTex.Apply(false);
            if (!loggedBase)
            {
                loggedBase = true;
                Rck.Log.LogInfo($"Factions: commander map covers tiles {minX},{minY} to {minX + span - 1},{minY + span - 1} ({span}×{span}); {voids} void tile(s), floor map {(useFloors ? "used" : "ignored")}.");
            }
        }

        private static bool Exposed(bool[] wall, int x, int y, int w, int h)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (!wall[ny * Size + nx]) return true;
                }
            return false;
        }

        private static Color32 Colour(TileData td, bool isWall, bool noFloor, out bool walk)
        {
            walk = false;
            if (td == null) return Void;
            switch (td.wallMaterial)
            {
                case wallMaterialType.Bars:
                case wallMaterialType.BarbedWire:
                case wallMaterialType.LockdownWall:
                    return Fence;
                case wallMaterialType.Window:
                    return Glass;
                case wallMaterialType.Door:
                case wallMaterialType.DoorOpen:
                    walk = true;
                    return DoorColour;
            }
            if (td.lockdownWall) return Fence;
            if (isWall)
            {
                switch (td.wallMaterial)
                {
                    case wallMaterialType.Wood: return WallWood;
                    case wallMaterialType.Steel: return WallSteel;
                    case wallMaterialType.Glass: return Glass;
                    default: return Wall;
                }
            }
            if (td.hole) return HoleColour;
            if (td.water || td.lake) return WaterColour;
            walk = true;
            if (td.ice) return IceColour;
            if (td.spillOoze) return Ooze;
            if (td.groundFire) return Fire;
            if (td.conveyorBelt) return Belt;
            switch (td.floorMaterial)
            {
                case floorMaterialType.Water:
                case floorMaterialType.Canal:
                case floorMaterialType.Pool:
                    walk = false;
                    return WaterColour;
                case floorMaterialType.Ice:
                case floorMaterialType.IceRink:
                    return IceColour;
                case floorMaterialType.Hole:
                    walk = false;
                    return HoleColour;
                case floorMaterialType.DirtFloor:
                case floorMaterialType.CaveFloor:
                    return Dirt;
                case floorMaterialType.CityParkFloor:
                    return Grass;
                case floorMaterialType.WoodClean:
                case floorMaterialType.WoodSlats:
                case floorMaterialType.Bridge:
                    return Wood;
                case floorMaterialType.FlamePit:
                    return Fire;
                case floorMaterialType.ConveyorBelt:
                    return Belt;
                case floorMaterialType.None:
                case floorMaterialType.ClearFloor:
                    if (noFloor)
                    {
                        walk = false;
                        return Void;
                    }
                    return td.bush ? Grass : Street;
                default:
                    return Indoor;
            }
        }

        private static Color32 Shade(Color32 c, int by)
            => new Color32((byte)Mathf.Clamp(c.r + by, 0, 255), (byte)Mathf.Clamp(c.g + by, 0, 255), (byte)Mathf.Clamp(c.b + by, 0, 255), 255);

        private static void Recreate(ref Texture2D tex, string name, int size)
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        // ---- The turf layer ----

        private static void EnsureTurf(GameController gc, int key)
        {
            if (turfTex == null) return;
            bool fresh = turfVersion != TurfShapes.Version;
            float now = Time.unscaledTime;
            if (!Interval.Due(ref nextTurf, now, TurfSeconds, force: fresh)) return;
            bool rackets = WarConfig.RacketsOn(gc);
            long sig = 17;
            foreach (TurfShapes.Shape s in TurfShapes.All)
                sig = sig * 31 + (long)s.Turf.Current * 7 + (TurfShapes.Contested(s.Turf) ? 1 : 0) + (s.Turf.Ruined ? 2 : 0);
            sig = sig * 31 + key;
            sig = sig * 31 + (rackets ? 1 : 0);
            if (!fresh && sig == turfSignature) return;
            if (fresh)
            {
                turfVersion = TurfShapes.Version;
                Centres();
            }
            turfSignature = sig;
            PaintTurf(key, rackets);
        }

        /// <summary>Each turf's badge sits on the average of its tiles.</summary>
        private static void Centres()
        {
            badges.Clear();
            foreach (TurfShapes.Shape s in TurfShapes.All)
            {
                if (s.Tiles.Count == 0)
                {
                    badges.Add(new Vector2(s.Centre.x / Tile, s.Centre.y / Tile));
                    continue;
                }
                float sx = 0f, sy = 0f;
                foreach (int idx in s.Tiles)
                {
                    sx += idx % Size;
                    sy += idx / Size;
                }
                badges.Add(new Vector2(sx / s.Tiles.Count, sy / s.Tiles.Count));
            }
        }

        private static void PaintTurf(int key, bool rackets)
        {
            Array.Clear(turfPixels, 0, turfPixels.Length);
            int texSize = span * Px;
            ulong ours = key >= 0 ? 1UL << key : 0;
            foreach (TurfShapes.Shape s in TurfShapes.All)
            {
                TurfWar.Turf t = s.Turf;
                if (t.Common && (t.Ruined || !rackets)) continue;
                bool contested = TurfShapes.Contested(t);
                bool mine = (t.Current & ours) != 0;
                bits.Clear();
                for (int k = 0; k < 64; k++)
                    if ((t.Current & (1UL << k)) != 0) bits.Add(k);
                Color32 edgeColour = mine ? new Color32(255, 255, 255, 255)
                    : bits.Count > 0 ? TurfOverlay.ColorsOf(bits[0]).Value
                    : t.Common ? TurfOverlay.FreeRacket : TurfOverlay.Cleared;
                edgeColour.a = EdgeAlpha;
                int edgeWidth = mine ? 2 : 1;
                int me = s.Index;
                foreach (int idx in s.Tiles)
                {
                    int x = idx % Size, y = idx / Size;
                    int tx = x - minX, ty = y - minY;
                    if (tx < 0 || ty < 0 || tx >= span || ty >= span) continue;
                    Color32 fill = bits.Count == 0 ? (t.Common ? TurfOverlay.FreeRacket : TurfOverlay.Cleared)
                        : TurfOverlay.ColorsOf(bits[((x >> 1) + (y >> 1)) % bits.Count]).Key;
                    bool left = Outside(x - 1, y, me), right = Outside(x + 1, y, me), down = Outside(x, y - 1, me), up = Outside(x, y + 1, me);
                    for (int py = 0; py < Px; py++)
                    {
                        int gy = ty * Px + py;
                        int row = gy * texSize;
                        for (int px = 0; px < Px; px++)
                        {
                            int gx = tx * Px + px;
                            bool edge = (left && px < edgeWidth) || (right && px >= Px - edgeWidth) || (down && py < edgeWidth) || (up && py >= Px - edgeWidth);
                            Color32 c;
                            if (edge) c = edgeColour;
                            else if (t.Common)
                            {
                                // A racket is dotted.
                                if (((gx + gy) & 1) != 0 || (gy & 1) != 0) continue;
                                c = fill;
                                c.a = StripeAlpha;
                            }
                            else
                            {
                                c = fill;
                                // Striped while its holders fight.
                                c.a = contested && (gx + gy) % 8 < 3 ? StripeAlpha : FillAlpha;
                            }
                            turfPixels[row + gx] = c;
                        }
                    }
                }
            }
            turfTex.SetPixels32(turfPixels);
            turfTex.Apply(false);
        }

        private static bool Outside(int x, int y, int me)
        {
            TurfShapes.Shape s = TurfShapes.At(x, y);
            return s == null || s.Index != me;
        }

        // ---- The legend under the map ----

        /// <summary>One line under the map: each faction's colour and how many turfs it holds.</summary>
        internal static void DrawLegend(Rect r, int key)
        {
            ulong held = 0;
            foreach (TurfWar.Turf t in TurfWar.Turfs) held |= t.Current;
            ulong keys = (TurfWar.StartKeys | held) & ~Factions.CommonFolkBit;
            ulong routed = FactionEvents.Routed;
            float x = r.x;
            for (int k = 0; k < 64 && k < Factions.Keys.Count; k++)
            {
                ulong bit = 1UL << k;
                if ((keys & bit) == 0) continue;
                int count = TurfWar.HeldCount(k), rackets = TurfWar.RacketCount(k);
                bool faded = count + rackets == 0 || (routed & bit) != 0;
                string text = $"{Squads.Short(k)}{(k == key ? " (you)" : "")} {count}{(rackets > 0 ? " +" + rackets + "$" : "")}";
                float w = WarStyle.Small.CalcSize(new GUIContent(text)).x;
                if (x + 18f + w > r.xMax) break;
                Color c = TurfOverlay.ColorsOf(k).Key;
                c.a = faded ? 0.35f : 1f;
                var sw = new Rect(x, r.y + (r.height - 12f) / 2f, 12f, 12f);
                WarStyle.Fill(sw, c);
                WarStyle.Frame(sw, k == key ? Color.white : new Color(0f, 0f, 0f, 0.8f));
                Color was = GUI.color;
                if (faded) GUI.color = new Color(was.r, was.g, was.b, was.a * 0.5f);
                WarStyle.Text(new Rect(x + 17f, r.y, w + 4f, r.height), text, WarStyle.Small);
                GUI.color = was;
                x += 17f + w + 16f;
            }
        }
    }
}
