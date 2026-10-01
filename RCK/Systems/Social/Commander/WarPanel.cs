#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The host's turf-war screens, drawn with Unity's immediate-mode GUI on a persistent object in the game's own
    ///   font and colours (see <see cref="WarStyle"/>). The war panel (<c>Map.WarPanelKey</c>, G) ranks the factions
    ///   by their share of the city's power (see <see cref="ControlPoints.Standings"/>); it opens by itself with the
    ///   <c>RCK_War_Panel</c> mutator or the commander console. The console (<c>Map.CommandConsoleKey</c>, T) is a
    ///   command map (see <see cref="CommandMap"/>: click a squad, then a tile) beside the recruiting, squad and war
    ///   controls of <see cref="Command"/>. Both are windows that can be dragged by the title, scale with the screen
    ///   height, hide with the pause menu, and stop mouse clicks over them from firing the player's weapon (see the
    ///   <c>MainGUI.CheckMouseOver</c> patch). Mouse only; they show only on the host, and only in levels with a turf war.
    /// </summary>
    internal sealed class WarPanel : MonoBehaviour
    {
        private const int PanelId = 0x52434B01, ConsoleId = 0x52434B02;
        private const float Reference = 1080f, PanelWidth = 470f;
        private const float Pad = 12f, Top = 42f, MapSize = 640f, Gap = 14f, SideWidth = 352f, LegendHeight = 24f;
        private const float ConsoleWidth = Pad + MapSize + Gap + SideWidth + Pad;
        private const float ConsoleHeight = Top + MapSize + 6f + LegendHeight + 10f;
        private const float RecruitHeight = 30f, SquadHeight = 46f, Spacing = 3f, ConfirmSeconds = 3f;
        private const int RecruitRows = 4, SquadRows = 3;

        private static WarPanel instance;
        private static bool broken;
        private static bool panelShown, consoleShown;
        // Closed with its key: stays closed in later levels until the key opens it again (this game session only).
        private static bool panelClosed, consoleClosed;
        private static int seenStamp = int.MinValue;
        private static Rect panelRect = new Rect(-1f, 150f, 0f, 0f), consoleRect = new Rect(-1f, 0f, ConsoleWidth, ConsoleHeight);
        private static Rect panelScreen, consoleScreen;
        private static bool drawnPanel, drawnConsole;
        private static float scale = 1f;
        private static Vector2 recruitScroll, squadScroll;
        private static float disbandAt = float.MinValue;
        private static Command.Team disbandFor;
        private static bool disbandAll;

        /// <summary>True while the mouse is over a shown window.</summary>
        internal static bool MouseOver { get; private set; }

        internal static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("RCK_WarUI");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WarPanel>();
        }

        private static bool Hidden(GameController gc)
        {
            if (gc == null || !gc.loadComplete || gc.levelTransitioning) return true;
            if (gc.menuGUI != null && gc.menuGUI.onMenu) return true;
            return gc.mainGUI != null && gc.mainGUI.menuGUI != null && gc.mainGUI.menuGUI.onMenu;
        }

        /// <summary>The level has a war to show: turfs were listed or the player commands a faction.</summary>
        private static bool HasWar() => TurfWar.Built || Command.Key >= 0;

        private void Update()
        {
            if (broken) return;
            try
            {
                MouseOver = false;
                GameController gc = FactionEvents.Server();
                if (gc == null || Hidden(gc) || !HasWar()) return;
                if (LevelScope.IsNew(ref seenStamp))
                {
                    bool command = Command.Key >= 0;
                    panelShown = (command || LevelMutators.Has(gc, WarConfig.PanelMutator)) && !panelClosed;
                    consoleShown = command && !consoleClosed;
                    disbandAt = float.MinValue;
                }
                RckConfig config = Rck.Config;
                if (config != null)
                {
                    if (HotKeys.Pressed(config.WarPanelKey.Value))
                    {
                        panelShown = !panelShown;
                        panelClosed = !panelShown;
                    }
                    if (HotKeys.Pressed(config.CommandConsoleKey.Value))
                    {
                        if (Command.Key >= 0)
                        {
                            consoleShown = !consoleShown;
                            consoleClosed = !consoleShown;
                        }
                        else Rck.Log.LogInfo("Factions: the commander console needs the RCK_Commander mutator or a [RCK]Command:: entry.");
                    }
                }
                Vector2 mouse = Input.mousePosition;
                mouse.y = Screen.height - mouse.y;
                MouseOver = (drawnPanel && panelShown && panelScreen.Contains(mouse))
                    || (drawnConsole && consoleShown && Command.Key >= 0 && consoleScreen.Contains(mouse));
            }
            catch (Exception e) { Break(e); }
        }

        private void OnGUI()
        {
            if (broken) return;
            drawnPanel = drawnConsole = false;
            try
            {
                GameController gc = FactionEvents.Server();
                if (gc == null || Hidden(gc) || !HasWar() || seenStamp != LevelScope.Stamp) return;
                if (!panelShown && !(consoleShown && Command.Key >= 0)) return;
                WarStyle.Ensure(gc);
                scale = Mathf.Max(0.5f, Screen.height / Reference);
                Matrix4x4 old = GUI.matrix;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                float width = Screen.width / scale, height = Screen.height / scale;
                if (panelShown)
                {
                    if (panelRect.x < 0f) panelRect.x = width - PanelWidth - 10f;
                    panelRect = Clamp(GUILayout.Window(PanelId, Fit(panelRect), DrawPanel, GUIContent.none, WarStyle.Window, GUILayout.Width(PanelWidth)), width, height);
                    panelScreen = ToScreen(panelRect);
                    drawnPanel = true;
                }
                if (consoleShown && Command.Key >= 0)
                {
                    if (consoleRect.x < 0f)
                    {
                        consoleRect.x = 10f;
                        consoleRect.y = Mathf.Max(0f, (height - ConsoleHeight) / 2f);
                    }
                    consoleRect.width = ConsoleWidth;
                    consoleRect.height = ConsoleHeight;
                    consoleRect = Clamp(GUI.Window(ConsoleId, consoleRect, DrawConsole, GUIContent.none, WarStyle.Window), width, height);
                    consoleScreen = ToScreen(consoleRect);
                    drawnConsole = true;
                }
                GUI.matrix = old;
            }
            catch (Exception e) { Break(e); }
        }

        private static void Break(Exception e)
        {
            broken = true;
            MouseOver = false;
            Rck.Log.LogError($"Factions: the war panel failed and is off until the game restarts: {e}");
        }

        // GUILayout.Window only grows a window, so each frame starts it at no size and lets the content decide.
        private static Rect Fit(Rect r) => new Rect(r.x, r.y, 0f, 0f);

        private static Rect Clamp(Rect r, float width, float height)
        {
            r.x = Mathf.Clamp(r.x, 0f, Mathf.Max(0f, width - Mathf.Min(r.width, width)));
            r.y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, height - 40f));
            return r;
        }

        private static Rect ToScreen(Rect r) => new Rect(r.x * scale, r.y * scale, r.width * scale, r.height * scale);

        private static string Count(int n, string one) => n == 1 ? $"1 {one}" : $"{n} {one}s";

        private static void Section(string title)
        {
            GUILayout.Space(8f);
            WarStyle.Text(title, WarStyle.Header);
            GUILayout.Space(2f);
        }

        // ---- The war panel ----

        private static void DrawPanel(int id)
        {
            try
            {
                GameController gc = GameController.gameController;
                WarStyle.WindowTitle(PanelWidth, "Turf war", "share of power");
                List<ControlPoints.Standing> standings = ControlPoints.Standings(gc);
                ulong players = Factions.PlayerKeys(gc), routed = FactionEvents.Routed;
                int commanded = Command.Key;
                bool points = ControlPoints.On;
                if (standings.Count == 0) WarStyle.Text("No factions in the war.", WarStyle.Small);
                foreach (ControlPoints.Standing s in standings)
                {
                    ulong bit = 1UL << s.Key;
                    bool out_ = (routed & bit) != 0 || (s.Turfs == 0 && s.Rackets == 0 && s.Living == 0);
                    Color32 fill = TurfOverlay.ColorsOf(s.Key).Key;
                    string name = Squads.Short(s.Key);
                    if (s.Key == commanded) name += " (yours)";
                    else if ((players & bit) != 0) name += " (you)";
                    if ((routed & bit) != 0) name += ", routed";
                    string stats = $"{Count(s.Turfs, "turf")}, {Count(s.Rackets, "racket")}, {s.Living} men";
                    if (points && s.Points >= 0) stats += $", {s.Points} CP";

                    Color was = GUI.color;
                    if (out_) GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    GUILayout.BeginHorizontal();
                    WarStyle.Swatch(fill, 1f);
                    WarStyle.Text(name, s.Key == commanded ? WarStyle.Bold : WarStyle.Label, GUILayout.Width(200f));
                    GUILayout.FlexibleSpace();
                    WarStyle.Bar(s.Power / 100f, fill, 140f);
                    WarStyle.Text(s.Power + "%", WarStyle.Small, GUILayout.Width(44f));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20f);
                    WarStyle.Text(stats, WarStyle.Small);
                    GUILayout.EndHorizontal();
                    GUI.color = was;
                    GUILayout.Space(4f);
                }
                GUILayout.Space(4f);
                int free = TurfWar.FreeRackets();
                RckConfig config = Rck.Config;
                string keys = HotKeys.Name(config?.WarPanelKey.Value ?? KeyCode.None) + " hides";
                if (commanded >= 0) keys += ", " + HotKeys.Name(config?.CommandConsoleKey.Value ?? KeyCode.None) + " commands";
                WarStyle.Text("Power counts turf, rackets, men and CP.", WarStyle.Small);
                WarStyle.Text((free > 0 ? $"Free rackets: {free}.  " : "") + keys + ".", WarStyle.Small);
                KeyWarnings(config, commanded >= 0);
            }
            catch (Exception e) { Break(e); }
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 34f));
        }

        // ---- The commander console ----

        private static void DrawConsole(int id)
        {
            try
            {
                GameController gc = GameController.gameController;
                int key = Command.Key;
                if (key < 0)
                {
                    WarStyle.WindowTitle(ConsoleWidth, "Commander");
                    WarStyle.Text(new Rect(Pad, Top, 400f, 24f), "Nothing to command.", WarStyle.Small);
                }
                else
                {
                    int cp = ControlPoints.Get(key);
                    string points = $"{cp} CP";
                    if (ControlPoints.On)
                        points += cp >= ControlPoints.Settings.Max ? " (full)" : $", +{ControlPoints.IncomeOf(key)} every {ControlPoints.Settings.Interval} s";
                    WarStyle.WindowTitle(ConsoleWidth, "Commander: " + Squads.Capital(Squads.Plural(key)), points);
                    var map = new Rect(Pad, Top, MapSize, MapSize);
                    CommandMap.Draw(map, gc, key);
                    CommandMap.DrawLegend(new Rect(Pad, map.yMax + 6f, MapSize, LegendHeight), key);
                    GUILayout.BeginArea(new Rect(map.xMax + Gap, Top, SideWidth, ConsoleHeight - Top - 10f));
                    try { DrawSide(gc, key, cp); }
                    finally { GUILayout.EndArea(); }
                }
            }
            catch (Exception e) { Break(e); }
            finally { GUI.enabled = true; }
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 34f));
        }

        private static void DrawSide(GameController gc, int key, int cp)
        {
            int alive = Squads.Living(Squads.Kind.Command), room = Squads.Room(Squads.Kind.Command);
            GUILayout.BeginHorizontal();
            WarStyle.Swatch(TurfOverlay.ColorsOf(key).Key, 1f);
            WarStyle.Text(Squads.Capital(Squads.Plural(key)), WarStyle.Bold);
            GUILayout.FlexibleSpace();
            WarStyle.Text($"men {alive}/{Command.Cap}", WarStyle.Small);
            GUILayout.EndHorizontal();

            Rect said = GUILayoutUtility.GetRect(10f, 44f, GUILayout.ExpandWidth(true), GUILayout.Height(44f));
            string status = Command.Status;
            if (status.Length > 0) WarStyle.Text(said, status, Command.StatusOk ? WarStyle.Status : WarStyle.Refused);
            else if (Command.Teams.Count == 0) WarStyle.Text(said, "Recruit a squad below, then click the map to send it.", WarStyle.Help);
            else if (CommandMap.Orders(out _)) WarStyle.Text(said, $"Orders go to {Who()}. Click the map.", WarStyle.Help);
            else WarStyle.Text(said, "Pick a squad, then click the map.", WarStyle.Help);

            DrawRecruits(cp, room);
            DrawSquads(gc, key);
            DrawWar(gc);

            GUILayout.FlexibleSpace();
            WarStyle.Text("Click a squad, then the map: enemy turf to attack, yours to defend, open ground to move. Right-click lets go. Turf, rackets, captures and kills earn CP.", WarStyle.Help);
            GUILayout.Space(4f);
            RckConfig config = Rck.Config;
            WarStyle.Text($"{HotKeys.Name(config?.CommandConsoleKey.Value ?? KeyCode.None)} hides, {HotKeys.Name(config?.WarPanelKey.Value ?? KeyCode.None)} standings. Drag the title to move.", WarStyle.Small);
            KeyWarnings(config, true);
        }

        // A line under the key hints for each shown toggle key that the player's SoR controls also use.
        private static void KeyWarnings(RckConfig config, bool console)
        {
            if (config == null) return;
            KeyWarning(config.WarPanelKey.Value, nameof(RckConfig.WarPanelKey));
            if (console) KeyWarning(config.CommandConsoleKey.Value, nameof(RckConfig.CommandConsoleKey));
        }

        private static void KeyWarning(KeyCode key, string setting)
        {
            string action = HotKeys.ClashWarned(key, setting);
            if (action != null)
                WarStyle.Text($"{HotKeys.Name(key)} is also \"{action}\" in your SoR controls. Change {setting} in RCK's config ([Map]).", WarStyle.Refused);
        }

        private static string Who()
        {
            if (!CommandMap.Orders(out Command.Team t)) return "nobody";
            return t != null ? t.Label.ToLowerInvariant() : "every squad";
        }

        private static void DrawRecruits(int cp, int room)
        {
            Section("Recruit");
            IReadOnlyList<Command.Offer> roster = Command.Roster;
            if (roster.Count == 0)
            {
                WarStyle.Text("No units to recruit.", WarStyle.Small);
                return;
            }
            CommandSettings settings = Command.Settings;
            bool scroll = roster.Count > RecruitRows;
            if (scroll)
                recruitScroll = GUILayout.BeginScrollView(recruitScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none,
                    GUILayout.Height(RecruitRows * (RecruitHeight + Spacing)));
            foreach (Command.Offer o in roster)
            {
                int n = Math.Min(settings.Size, room);
                if (o.Cost > 0) n = Math.Min(n, cp / o.Cost);
                string right;
                if (n > 0) right = o.Cost > 0 ? $"{n} for {o.Cost * n} CP" : $"{n}, free";
                else if (room <= 0) right = "at the cap";
                else right = $"need {o.Cost} CP";
                GUI.enabled = n > 0;
                Rect r = GUILayoutUtility.GetRect(10f, RecruitHeight, GUILayout.ExpandWidth(true), GUILayout.Height(RecruitHeight));
                // A new squad is picked when nothing else is, so the next map click sends it.
                if (WarStyle.PressRow(r, o.Name, right) && Command.Recruit(o) && !CommandMap.Picked) CommandMap.Selected = Command.Newest;
                GUI.enabled = true;
                GUILayout.Space(Spacing);
            }
            if (scroll) GUILayout.EndScrollView();
        }

        private static void DrawSquads(GameController gc, int key)
        {
            IReadOnlyList<Command.Team> teams = Command.Teams;
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            WarStyle.Text("Squads", WarStyle.Header);
            GUILayout.FlexibleSpace();
            GUI.enabled = teams.Count > 0;
            bool all = CommandMap.All;
            if (WarStyle.Press("All squads", WarStyle.Chip, all, GUILayout.Width(120f), GUILayout.Height(26f))) CommandMap.All = !all;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
            if (teams.Count == 0)
            {
                WarStyle.Text("None yet. Recruit one above.", WarStyle.Small);
                return;
            }

            Command.Team picked = CommandMap.Selected;
            Color fill = TurfOverlay.ColorsOf(key).Key;
            bool scroll = teams.Count > SquadRows;
            if (scroll)
                squadScroll = GUILayout.BeginScrollView(squadScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none,
                    GUILayout.Height(SquadRows * (SquadHeight + Spacing)));
            foreach (Command.Team t in teams)
            {
                Rect r = GUILayoutUtility.GetRect(10f, SquadHeight, GUILayout.ExpandWidth(true), GUILayout.Height(SquadHeight));
                bool on = all || picked == t || (picked == null && teams.Count == 1);
                if (GUI.Button(r, GUIContent.none, on ? WarStyle.ChipOn : WarStyle.Chip)) CommandMap.Selected = !all && picked == t ? null : t;
                var centre = new Vector2(r.x + 22f, r.y + r.height / 2f);
                WarStyle.Dot(centre, 26f, new Color(0f, 0f, 0f, 0.8f));
                WarStyle.Dot(centre, 22f, fill);
                WarStyle.Text(new Rect(centre.x - 13f, centre.y - 13f, 26f, 26f), t.Number.ToString(), WarStyle.Badge);
                GUI.BeginGroup(new Rect(r.x + 42f, r.y, r.width - 50f, r.height));
                string roll = Command.Roll(t);
                WarStyle.Text(new Rect(0f, 3f, 1000f, 20f), roll.Length > 0 ? $"{t.Label}: {roll}" : t.Label, on ? WarStyle.Bold : WarStyle.Label);
                WarStyle.Text(new Rect(0f, 23f, 1000f, 20f), Command.Doing(gc, t), WarStyle.Small);
                GUI.EndGroup();
                GUILayout.Space(Spacing);
            }
            if (scroll) GUILayout.EndScrollView();

            bool can = CommandMap.Orders(out Command.Team target);
            bool sure = can && Time.unscaledTime - disbandAt < ConfirmSeconds && disbandAll == all && disbandFor == target;
            GUILayout.BeginHorizontal();
            GUI.enabled = can;
            string who = all ? " all" : target != null ? " " + target.Number : "";
            if (WarStyle.Press("Recall" + who, WarStyle.Button, false, GUILayout.Height(30f))) Command.Recall(target);
            if (WarStyle.Press(sure ? "Sure? Disband" : "Disband" + who, WarStyle.Button, sure, GUILayout.Height(30f)))
            {
                if (sure)
                {
                    disbandAt = float.MinValue;
                    Command.Disband(target);
                }
                else
                {
                    disbandAt = Time.unscaledTime;
                    disbandFor = target;
                    disbandAll = all;
                    Command.Hint($"Click again to send {(target != null ? target.Label.ToLowerInvariant() : "every squad")} home for {Command.RefundOf(target)} CP back.");
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private static void DrawWar(GameController gc)
        {
            List<int> neutrals = Command.Neutrals(gc);
            if (neutrals.Count == 0) return;
            Section("Declare war");
            float w = (SideWidth - 6f) / 2f;
            for (int i = 0; i < neutrals.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Math.Min(i + 2, neutrals.Count); j++)
                {
                    int other = neutrals[j];
                    bool asking = Command.Confirming(other);
                    string text = asking ? $"Sure? {Squads.Short(other)}" : $"War on {Squads.Short(other)}";
                    if (WarStyle.Press(text, WarStyle.Button, asking, GUILayout.Width(w), GUILayout.Height(30f))) Command.DeclareWar(other);
                }
                GUILayout.EndHorizontal();
            }
        }
    }

    /// <summary>The mouse over a war window counts as over the game's interface, so clicks there don't attack.</summary>
    [HarmonyPatch(typeof(MainGUI), nameof(MainGUI.CheckMouseOver))]
    internal static class MainGUI_CheckMouseOver_WarPanel_Patch
    {
        private static void Postfix(MainGUI __instance)
        {
            if (!WarPanel.MouseOver) return;
            GameController gc = GameController.gameController;
            if (gc != null && __instance.agent != null && __instance.agent == gc.playerAgent) __instance.overInterface = true;
        }
    }
}
