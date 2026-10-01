#nullable disable
using System;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The look of the war windows: the game's own pixel font (whatever the player's language and font setting give
    ///   the quest sheet, else the game's Munro font), dark boxes with thin pale borders like the game's menus, yellow
    ///   highlights and text with a black drop shadow. Immediate-mode GUI styles, built inside <c>OnGUI</c> and rebuilt
    ///   if the font changes.
    /// </summary>
    internal static class WarStyle
    {
        internal static readonly Color Yellow = new Color(1f, 0.85f, 0.15f);
        internal static readonly Color Pale = new Color(1f, 0.95f, 0.6f);
        internal static readonly Color Grey = new Color(0.74f, 0.74f, 0.78f);
        internal static readonly Color Dim = new Color(0.5f, 0.5f, 0.54f);
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.9f);

        internal static Texture2D White, Circle, Ring;
        internal static GUIStyle Window, Title, Label, Small, Bold, Header, Status, Refused, Help, Button, Chip, ChipOn, Badge, Tip;
        private static GUIStyle buttonText, buttonTextHot, chipText, chipTextOn, smallRight, rowLeft, rowLeftHot, rowRight, rowRightHot;
        private static Texture2D panel, buttonBack, hoverBack, activeBack, tipBack;

        private static Font font, styled;
        private static bool fallback, loggedFont, loggedFallback;
        private static float nextTry = float.MinValue;

        /// <summary>
        ///   The font the game's interface uses now. Until the quest sheet exists it's a built-in fallback, and it looks
        ///   again about once a second.
        /// </summary>
        internal static Font GameFont(GameController gc)
        {
            if (font != null && !fallback) return font;
            float now = Time.unscaledTime;
            if (!Interval.Due(ref nextTry, now, 1f, force: font == null)) return font;
            Font f = FromGame(gc);
            if (f != null)
            {
                font = f;
                fallback = false;
                if (!loggedFont)
                {
                    loggedFont = true;
                    Rck.Log.LogInfo($"Factions: war windows use the game font '{f.name}' (dynamic {f.dynamic}, size {f.fontSize}).");
                }
                return font;
            }
            if (font == null)
            {
                foreach (string builtin in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
                {
                    try { font = Resources.GetBuiltinResource<Font>(builtin); }
                    catch { }
                    if (font != null) break;
                }
                fallback = true;
                if (!loggedFallback)
                {
                    loggedFallback = true;
                    Rck.Log.LogInfo("Factions: the game font isn't loaded yet; war windows use a built-in font for now.");
                }
            }
            return font;
        }

        private static Font FromGame(GameController gc)
        {
            if (gc == null) return null;
            try
            {
                QuestSheet sheet = gc.mainGUI != null ? gc.mainGUI.questSheetScript : null;
                if (sheet != null && sheet.levelNumText != null && sheet.levelNumText.font != null) return sheet.levelNumText.font;
                if (sheet != null && sheet.questTitle != null && sheet.questTitle.font != null) return sheet.questTitle.font;
                if (gc.munroExpandedFont != null) return gc.munroExpandedFont;
                return gc.munroFont;
            }
            catch (Exception e)
            {
                if (!loggedFallback) Rck.Log.LogWarning($"Factions: couldn't read the game's font: {e.Message}");
                loggedFallback = true;
                return null;
            }
        }

        /// <summary>Builds the textures and styles (call from <c>OnGUI</c>). Rebuilds the styles when the font changes.</summary>
        internal static void Ensure(GameController gc)
        {
            Font f = GameFont(gc);
            if (White == null || Circle == null || Ring == null || panel == null || buttonBack == null || hoverBack == null || activeBack == null || tipBack == null)
            {
                White = Solid(Color.white);
                Circle = Disc(32, 0f);
                Ring = Disc(32, 0.72f);
                panel = Box(new Color(0.04f, 0.04f, 0.05f, 0.9f), new Color(0.95f, 0.95f, 0.95f, 0.38f));
                buttonBack = Box(new Color(0.16f, 0.16f, 0.18f, 0.95f), new Color(0.6f, 0.6f, 0.62f, 1f));
                hoverBack = Box(new Color(0.24f, 0.22f, 0.12f, 0.97f), Yellow);
                activeBack = Box(new Color(0.35f, 0.3f, 0.08f, 0.97f), Yellow);
                tipBack = Box(new Color(0f, 0f, 0f, 0.92f), new Color(0.8f, 0.8f, 0.8f, 0.6f));
                styled = null;
            }
            if (Window != null && styled == f) return;
            styled = f;
            bool sized = f == null || f.dynamic;

            GUIStyle Make(int size, Color colour, TextAnchor align = TextAnchor.MiddleLeft, bool wrap = false)
            {
                var s = new GUIStyle
                {
                    font = f,
                    alignment = align,
                    wordWrap = wrap,
                    richText = false,
                    clipping = TextClipping.Overflow,
                    stretchWidth = wrap,
                    padding = new RectOffset(0, 0, 1, 1),
                    normal = { textColor = colour },
                };
                if (sized) s.fontSize = size;
                return s;
            }

            Window = new GUIStyle
            {
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(12, 12, 38, 10),
                normal = { background = panel },
                onNormal = { background = panel },
            };
            Title = Make(22, Yellow);
            Label = Make(18, Color.white);
            Small = Make(15, Grey);
            Bold = Make(18, Pale);
            Header = Make(16, Yellow);
            Status = Make(16, Pale, wrap: true);
            Refused = Make(16, new Color(1f, 0.5f, 0.42f), wrap: true);
            Help = Make(15, Grey, wrap: true);
            Badge = Make(13, Color.white, TextAnchor.MiddleCenter);
            Tip = Make(15, Color.white, wrap: true);
            smallRight = Make(15, Grey, TextAnchor.MiddleRight);
            Tip.normal.background = tipBack;
            Tip.border = new RectOffset(2, 2, 2, 2);
            Tip.padding = new RectOffset(8, 8, 5, 5);

            Button = new GUIStyle
            {
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(2, 2, 2, 2),
                font = f,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = buttonBack, textColor = Color.white },
                hover = { background = hoverBack, textColor = Yellow },
                active = { background = activeBack, textColor = Yellow },
                onNormal = { background = activeBack, textColor = Yellow },
                onHover = { background = activeBack, textColor = Yellow },
                onActive = { background = activeBack, textColor = Yellow },
            };
            if (sized) Button.fontSize = 17;
            Chip = new GUIStyle(Button);
            if (sized) Chip.fontSize = 16;
            ChipOn = new GUIStyle(Chip)
            {
                normal = { background = activeBack, textColor = Yellow },
                hover = { background = activeBack, textColor = Yellow },
            };
            buttonText = Make(17, Color.white, TextAnchor.MiddleCenter);
            buttonTextHot = Make(17, Yellow, TextAnchor.MiddleCenter);
            chipText = Make(16, Color.white, TextAnchor.MiddleCenter);
            chipTextOn = Make(16, Yellow, TextAnchor.MiddleCenter);
            rowLeft = Make(17, Color.white);
            rowLeftHot = Make(17, Yellow);
            rowRight = Make(15, Pale, TextAnchor.MiddleRight);
            rowRightHot = Make(15, Yellow, TextAnchor.MiddleRight);
        }

        // ---- Drawing ----

        /// <summary><paramref name="text"/> in <paramref name="style"/> with a black drop shadow (styles with no background only).</summary>
        internal static void Text(Rect r, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            Color was = GUI.contentColor;
            GUI.contentColor = new Color(Shadow.r, Shadow.g, Shadow.b, Shadow.a * was.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            GUI.contentColor = was;
            GUI.Label(r, text, style);
        }

        /// <summary>A shadowed label in a layout.</summary>
        internal static void Text(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            Rect r = GUILayoutUtility.GetRect(new GUIContent(text), style, options);
            Text(r, text, style);
        }

        /// <summary>A button (or a chip when <paramref name="on"/> is given) with shadowed text. True when clicked.</summary>
        internal static bool Press(Rect r, string text, GUIStyle style, bool on = false)
        {
            bool chip = style == Chip || style == ChipOn;
            bool hit = GUI.Button(r, GUIContent.none, on && chip ? ChipOn : style);
            bool hot = on || (GUI.enabled && r.Contains(Event.current.mousePosition));
            GUIStyle t = chip ? (hot ? chipTextOn : chipText) : hot ? buttonTextHot : buttonText;
            Text(r, text, t);
            return hit;
        }

        /// <summary>A button in a layout.</summary>
        internal static bool Press(string text, GUIStyle style, bool on, params GUILayoutOption[] options)
        {
            Rect r = GUILayoutUtility.GetRect(new GUIContent(text), style, options);
            return Press(r, text, style, on);
        }

        /// <summary>A wide button with <paramref name="left"/> on its left and <paramref name="right"/> on its right.</summary>
        internal static bool PressRow(Rect r, string left, string right, bool on = false)
        {
            bool hit = GUI.Button(r, GUIContent.none, on ? ChipOn : Button);
            bool hot = on || (GUI.enabled && r.Contains(Event.current.mousePosition));
            var inner = new Rect(r.x + 10f, r.y, r.width - 20f, r.height);
            Text(inner, left, hot ? rowLeftHot : rowLeft);
            Text(inner, right, hot ? rowRightHot : rowRight);
            return hit;
        }

        /// <summary>A window's title, drawn by hand in the game's font, with a rule under it.</summary>
        internal static void WindowTitle(float width, string title, string right = null)
        {
            Text(new Rect(12f, 6f, width - 24f, 26f), title, Title);
            if (!string.IsNullOrEmpty(right)) Text(new Rect(12f, 7f, width - 24f, 26f), right, smallRight);
            Fill(new Rect(10f, 32f, width - 20f, 2f), new Color(Yellow.r, Yellow.g, Yellow.b, 0.45f));
        }

        internal static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = new Color(c.r * was.r, c.g * was.g, c.b * was.b, c.a * was.a);
            GUI.DrawTexture(r, White);
            GUI.color = was;
        }

        internal static void Frame(Rect r, Color c, float width = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, width), c);
            Fill(new Rect(r.x, r.yMax - width, r.width, width), c);
            Fill(new Rect(r.x, r.y, width, r.height), c);
            Fill(new Rect(r.xMax - width, r.y, width, r.height), c);
        }

        internal static void Dot(Vector2 centre, float size, Color c, Texture2D tex = null)
        {
            Color was = GUI.color;
            GUI.color = new Color(c.r * was.r, c.g * was.g, c.b * was.b, c.a * was.a);
            GUI.DrawTexture(new Rect(centre.x - size / 2f, centre.y - size / 2f, size, size), tex != null ? tex : Circle);
            GUI.color = was;
        }

        /// <summary>A colour swatch in a layout row.</summary>
        internal static void Swatch(Color32 colour, float alpha, float size = 14f)
        {
            Rect r = GUILayoutUtility.GetRect(size, size + 8f, GUILayout.Width(size), GUILayout.Height(size + 8f));
            r = new Rect(r.x, r.y + 4f, size, size);
            Color c = colour;
            c.a = alpha;
            Fill(r, c);
            Frame(r, new Color(0f, 0f, 0f, 0.8f * alpha));
            GUILayout.Space(6f);
        }

        /// <summary>A horizontal share bar in a layout row.</summary>
        internal static void Bar(float share, Color32 colour, float width)
        {
            Rect r = GUILayoutUtility.GetRect(width, 22f, GUILayout.Width(width), GUILayout.Height(22f));
            r = new Rect(r.x, r.y + 6f, width, 10f);
            Fill(r, new Color(1f, 1f, 1f, 0.12f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(share), r.height), colour);
            Frame(r, new Color(0f, 0f, 0f, 0.7f));
            GUILayout.Space(4f);
        }

        // ---- Textures ----

        private static Texture2D New(int size, FilterMode filter)
            => new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
            };

        private static Texture2D Solid(Color c)
        {
            Texture2D t = New(1, FilterMode.Point);
            t.SetPixel(0, 0, c);
            t.Apply(false);
            return t;
        }

        /// <summary>An 8×8 box with a 2-pixel border, drawn nine-sliced (<c>border = 2</c>) so the border keeps its width.</summary>
        private static Texture2D Box(Color back, Color edge)
        {
            Texture2D t = New(8, FilterMode.Point);
            var px = new Color[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    px[y * 8 + x] = x < 2 || y < 2 || x > 5 || y > 5 ? edge : back;
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        /// <summary>A white anti-aliased disc, or a ring when <paramref name="inner"/> (a share of the radius) is above 0.</summary>
        private static Texture2D Disc(int size, float inner)
        {
            Texture2D t = New(size, FilterMode.Bilinear);
            var px = new Color[size * size];
            float r = size / 2f, ri = r * inner;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d);
                    if (inner > 0f) a = Mathf.Min(a, Mathf.Clamp01(d - ri));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }
    }
}
