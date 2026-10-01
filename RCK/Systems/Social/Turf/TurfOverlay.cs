#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RCK.Social
{
    /// <summary>
    ///   Turf ownership on the big map (the quest sheet's map). Each turf's floor (the tiles of its owner ID and start
    ///   chunk, as in <see cref="Territorial.OnTurf"/>) is tinted in its holder's colour with a stronger outline; the
    ///   players' own factions get a white outline, a cleared turf is grey and a contested turf (one of its holders is
    ///   fighting) is striped. A legend in the map's top-left corner lists the factions with the turfs each holds. It's
    ///   a 160×160 texture, one pixel per tile like vanilla's map texture, laid over the map image. Host only: clients
    ///   have no turf list. The <c>Map.TurfOverlayKey</c> key (F8) hides and shows it.
    /// </summary>
    internal static class TurfOverlay
    {
        private const int Size = 160;
        private const float RefreshSeconds = 0.25f;
        private const float Pad = 8f, RowHeight = 20f, Swatch = 14f, Gap = 6f;
        private const int FontSize = 16;
        private const byte FillAlpha = 90, StripeAlpha = 190, OutlineAlpha = 230;

        private sealed class Row
        {
            public GameObject Go;
            public Image Swatch;
            public Text Text;
        }

        internal static readonly Color32 Cleared = new Color32(150, 150, 150, 255);
        private static readonly Color32 ClearedEdge = new Color32(175, 175, 175, 255);
        private static readonly Color32 PlayerEdge = new Color32(255, 255, 255, 255);
        internal static readonly Color32 FreeRacket = new Color32(200, 180, 120, 255);
        private static readonly Color32 FreeRacketEdge = new Color32(225, 210, 160, 255);

        private static readonly Dictionary<string, KeyValuePair<Color32, Color32>> named = new Dictionary<string, KeyValuePair<Color32, Color32>>(StringComparer.Ordinal)
        {
            { "Crepe", Pair(new Color32(45, 110, 255, 255), new Color32(120, 175, 255, 255)) },
            { "Blahd", Pair(new Color32(220, 40, 40, 255), new Color32(255, 115, 115, 255)) },
            { "Mafia", Pair(new Color32(40, 40, 45, 255), new Color32(230, 190, 60, 255)) },
            { "Cop", Pair(new Color32(25, 35, 120, 255), new Color32(110, 165, 255, 255)) },
            { "Thief", Pair(new Color32(140, 60, 200, 255), new Color32(195, 135, 240, 255)) },
            { "Hacker", Pair(new Color32(40, 190, 70, 255), new Color32(125, 240, 145, 255)) },
        };

        private static readonly Color32[] palette =
        {
            new Color32(255, 140, 0, 255), new Color32(0, 150, 140, 255), new Color32(255, 105, 180, 255),
            new Color32(128, 128, 0, 255), new Color32(0, 215, 230, 255), new Color32(215, 0, 215, 255),
            new Color32(140, 80, 30, 255), new Color32(170, 255, 0, 255), new Color32(250, 128, 114, 255),
            new Color32(110, 130, 150, 255), new Color32(128, 0, 0, 255), new Color32(255, 220, 120, 255),
        };

        private static readonly List<Row> rows = new List<Row>();
        private static readonly List<int> bits = new List<int>();

        private static Minimap map;
        private static GameObject overlay, legend;
        private static RawImage image;
        private static Image legendBack;
        private static Text legendTitle;
        private static Font font;
        private static Texture2D texture;
        private static Color32[] pixels;
        private static int shapesVersion = int.MinValue, legendRows;
        private static long signature;
        private static bool painted;
        private static float nextRefresh;
        private static bool on = true, configSeen, lastConfig;
        private static int toggleFrame = -1;
        private static bool loggedRects, broken;

        private static KeyValuePair<Color32, Color32> Pair(Color32 fill, Color32 edge) => new KeyValuePair<Color32, Color32>(fill, edge);

        /// <summary>Every <see cref="Minimap"/> update: polls the toggle key, then refreshes the overlay while the big map is open.</summary>
        internal static void Update(Minimap m)
        {
            if (broken || m == null) return;
            try
            {
                GameController gc = GameController.gameController;
                if (gc == null) return;
                FollowConfig();
                PollToggle(gc);
                if (!m.isMinimapBig || !gc.gameEventsStarted || m.canvas == null || !m.canvas.enabled) return;
                float now = Time.unscaledTime;
                if (!Interval.Due(ref nextRefresh, now, RefreshSeconds)) return;
                Refresh(gc, m);
            }
            catch (Exception e)
            {
                broken = true;
                Rck.Log.LogError($"Factions: the turf overlay failed, it stops until restart: {e}");
                try { Hide(); } catch { }
            }
        }

        private static void FollowConfig()
        {
            RckConfig config = Rck.Config;
            if (config == null) return;
            bool value = config.TurfOverlay.Value;
            if (configSeen && value == lastConfig) return;
            configSeen = true;
            lastConfig = value;
            on = value;
            nextRefresh = 0f;
        }

        private static void PollToggle(GameController gc)
        {
            if (toggleFrame == Time.frameCount) return;
            toggleFrame = Time.frameCount;
            RckConfig config = Rck.Config;
            if (config == null || !HotKeys.Pressed(config.TurfOverlayKey.Value)) return;
            if (TurfWar.Turfs.Count == 0) return;
            HotKeys.ClashWarned(config.TurfOverlayKey.Value, nameof(RckConfig.TurfOverlayKey));
            on = !on;
            nextRefresh = 0f;
            Rck.Log.LogInfo($"Factions: turf overlay {(on ? "on" : "off")}.");
            bool open = gc.minimapBig != null && gc.minimapBig.canvas != null && gc.minimapBig.canvas.enabled;
            if (!open) Squads.Announce(gc, "ItemPickupSlow", on ? "Turf map on" : "Turf map off");
        }

        private static void Refresh(GameController gc, Minimap m)
        {
            if (!on || !TurfShapes.Ensure(gc))
            {
                Hide();
                return;
            }
            if (!Ensure(gc, m)) return;
            if (shapesVersion != TurfShapes.Version)
            {
                shapesVersion = TurfShapes.Version;
                painted = false;
            }

            IReadOnlyList<TurfShapes.Shape> shapes = TurfShapes.All;
            ulong players = Factions.PlayerKeys(gc);
            ulong routed = FactionEvents.Routed;
            long sig = 17;
            foreach (TurfShapes.Shape s in shapes)
                sig = sig * 31 + (long)s.Turf.Current * 7 + (TurfShapes.Contested(s.Turf) ? 1 : 0) + (s.Turf.Ruined ? 2 : 0);
            sig = sig * 31 + (long)players;
            sig = sig * 31 + (long)routed;
            if (!painted || sig != signature)
            {
                Paint(players);
                Legend(players, routed);
                if (!painted) Rck.Log.LogInfo($"Factions: turf overlay drawing {shapes.Count} turf(s).");
                painted = true;
                signature = sig;
            }
            overlay.SetActive(true);
            if (legend != null) legend.SetActive(legendRows > 0);
        }

        private static void Hide()
        {
            if (overlay != null) overlay.SetActive(false);
            if (legend != null) legend.SetActive(false);
        }

        // ---- Objects ----

        private static bool Ensure(GameController gc, Minimap m)
        {
            if (map == m && overlay != null && image != null) return true;
            if (overlay != null) UnityEngine.Object.Destroy(overlay);
            if (legend != null) UnityEngine.Object.Destroy(legend);
            overlay = legend = null;
            legendBack = null;
            legendTitle = null;
            rows.Clear();
            map = m;
            painted = false;

            if (texture == null)
            {
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                {
                    name = "RCK_TurfOverlay",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                pixels = new Color32[Size * Size];
            }

            overlay = new GameObject("RCK_TurfOverlay", typeof(RectTransform), typeof(RawImage));
            overlay.layer = m.gameObject.layer;
            var rt = (RectTransform)overlay.transform;
            rt.SetParent(m.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetSiblingIndex(0);
            image = overlay.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;

            LogRects(m, rt);
            BuildLegend(gc, m);
            return true;
        }

        /// <summary>Once: where the map image is, so the overlay's alignment can be checked against the start marker.</summary>
        private static void LogRects(Minimap m, RectTransform rt)
        {
            if (loggedRects) return;
            loggedRects = true;
            var mapRt = m.transform as RectTransform;
            Image mapImage = m.image;
            string start = m.startingChunk == null ? "none"
                : $"anchors {m.startingChunk.rectTransform.anchorMin}-{m.startingChunk.rectTransform.anchorMax} pos {m.startingChunk.rectTransform.anchoredPosition}";
            Rck.Log.LogInfo($"Factions: turf overlay on '{m.name}': map rect {(mapRt == null ? "none" : mapRt.rect.ToString())}, "
                + $"pivot {(mapRt == null ? "none" : mapRt.pivot.ToString())}, scale {m.transform.lossyScale}, "
                + $"preserveAspect {(mapImage != null && mapImage.preserveAspect)}, start marker {start}, overlay rect {rt.rect}.");
        }

        private static void BuildLegend(GameController gc, Minimap m)
        {
            font = WarStyle.GameFont(gc);
            if (font == null) return;
            legend = new GameObject("RCK_TurfLegend", typeof(RectTransform), typeof(Image));
            legend.layer = m.gameObject.layer;
            var rt = (RectTransform)legend.transform;
            rt.SetParent(m.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(Pad, -Pad);
            rt.SetAsLastSibling();
            legendBack = legend.GetComponent<Image>();
            legendBack.color = new Color(0f, 0f, 0f, 0.65f);
            legendBack.raycastTarget = false;
            legendTitle = NewText(legend.transform, "Title");
            legendTitle.text = "Turf held";
            legendTitle.color = WarStyle.Yellow;
            Place(legendTitle.rectTransform, Pad, -Pad);
            legend.SetActive(false);
        }

        private static Text NewText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Text t = go.GetComponent<Text>();
            t.font = font;
            if (font.dynamic) t.fontSize = FontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Place(RectTransform rt, float x, float y, float width = 300f, float height = RowHeight)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static Row RowAt(int i)
        {
            while (rows.Count <= i)
            {
                var row = new Row { Go = new GameObject("Row" + rows.Count, typeof(RectTransform)) };
                row.Go.layer = legend.layer;
                row.Go.transform.SetParent(legend.transform, false);
                Place((RectTransform)row.Go.transform, Pad, -Pad - RowHeight * (rows.Count + 1), 300f, RowHeight);
                var sw = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
                sw.layer = legend.layer;
                sw.transform.SetParent(row.Go.transform, false);
                row.Swatch = sw.GetComponent<Image>();
                row.Swatch.raycastTarget = false;
                Place(row.Swatch.rectTransform, 0f, -(RowHeight - Swatch) / 2f, Swatch, Swatch);
                row.Text = NewText(row.Go.transform, "Text");
                Place(row.Text.rectTransform, Swatch + Gap, 0f, 280f, RowHeight);
                rows.Add(row);
            }
            return rows[i];
        }

        // ---- Drawing ----

        private static void Paint(ulong players)
        {
            Array.Clear(pixels, 0, pixels.Length);
            foreach (TurfShapes.Shape s in TurfShapes.All)
            {
                TurfWar.Turf t = s.Turf;
                if (t.Common && t.Ruined) continue;
                bool contested = TurfShapes.Contested(t);
                bool mine = (t.Current & players) != 0;
                bits.Clear();
                for (int k = 0; k < 64; k++)
                    if ((t.Current & (1UL << k)) != 0) bits.Add(k);
                for (int j = 0; j < s.Tiles.Count; j++)
                {
                    int idx = s.Tiles[j];
                    int x = idx % Size, y = idx / Size;
                    bool edge = s.Edges[j];
                    // A racket is dotted: sand while it's free, its racketeers' colour while it's run, with a dashed edge.
                    if (t.Common && (edge ? (x + y) % 2 != 0 : x % 2 != 0 || y % 2 != 0)) continue;
                    Color32 c;
                    if (bits.Count == 0) c = t.Common ? (edge ? FreeRacketEdge : FreeRacket) : edge ? ClearedEdge : Cleared;
                    else
                    {
                        // A turf shared by several factions is checkered in their colours.
                        int key = bits[((x >> 1) + (y >> 1)) % bits.Count];
                        KeyValuePair<Color32, Color32> col = ColorsOf(key);
                        c = edge ? (mine ? PlayerEdge : col.Value) : col.Key;
                    }
                    c.a = edge ? OutlineAlpha : t.Common ? StripeAlpha : contested && (x + y) % 3 == 0 ? StripeAlpha : FillAlpha;
                    pixels[idx] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        internal static KeyValuePair<Color32, Color32> ColorsOf(int key)
        {
            string name = key >= 0 && key < Factions.Keys.Count ? Factions.Keys[key] : null;
            if (name != null && named.TryGetValue(name, out KeyValuePair<Color32, Color32> col)) return col;
            Color32 fill = palette[(key < 0 ? 0 : key) % palette.Length];
            return Pair(fill, Color32.Lerp(fill, new Color32(255, 255, 255, 255), 0.45f));
        }

        private static void Legend(ulong players, ulong routed)
        {
            if (legend == null) return;
            ulong held = 0;
            foreach (TurfShapes.Shape s in TurfShapes.All) held |= s.Turf.Current;
            ulong keys = TurfWar.StartKeys | held;
            int n = 0;
            float widest = legendTitle.preferredWidth;
            for (int key = 0; key < 64 && key < Factions.Keys.Count; key++)
            {
                ulong bit = 1UL << key;
                if ((keys & bit) == 0) continue;
                int count = TurfWar.HeldCount(key);
                int rackets = TurfWar.RacketCount(key);
                bool faded = count + rackets == 0 || (routed & bit) != 0;
                Row row = RowAt(n++);
                row.Go.SetActive(true);
                Color32 fill = ColorsOf(key).Key;
                fill.a = (byte)(faded ? 90 : 255);
                row.Swatch.color = fill;
                string you = (players & bit) != 0 ? " (you)" : "";
                string state = (routed & bit) != 0 ? " routed" : "";
                string extra = rackets > 0 ? $" +{rackets} racket{(rackets == 1 ? "" : "s")}" : "";
                row.Text.text = $"{Squads.Short(key)}{you}: {count}{extra}{state}";
                row.Text.color = faded ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;
                widest = Mathf.Max(widest, Swatch + Gap + row.Text.preferredWidth);
            }
            int free = TurfWar.FreeRackets();
            if (free > 0)
            {
                Row row = RowAt(n++);
                row.Go.SetActive(true);
                row.Swatch.color = FreeRacket;
                row.Text.text = $"Rackets free: {free}";
                row.Text.color = Color.white;
                widest = Mathf.Max(widest, Swatch + Gap + row.Text.preferredWidth);
            }
            for (int i = n; i < rows.Count; i++) rows[i].Go.SetActive(false);
            ((RectTransform)legend.transform).sizeDelta = new Vector2(Pad * 2f + widest, Pad * 2f + RowHeight * (n + 1));
            legendRows = n;
            legend.SetActive(n > 0);
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
    internal static class Minimap_Update_TurfOverlay_Patch
    {
        private static void Postfix(Minimap __instance) => TurfOverlay.Update(__instance);
    }
}
