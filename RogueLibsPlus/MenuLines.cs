using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RogueLibsPlus
{
    /// <summary>
    ///   <para>Extra lines above the game's version text, at the bottom left of the main menu. RogueLibs 4.0 no longer
    ///   draws <c>RogueLibs.CreateVersionText</c> lines (VersionText is obsolete and does nothing), so mods that want a
    ///   version or a warning on the menu use this instead.</para>
    /// </summary>
    public static class MenuLines
    {
        private const string ObjectName = "RogueLibsPlus.MenuLines";
        private const string ProblemColor = "#FF5A4F";

        private static readonly List<KeyValuePair<string, string>> lines = new List<KeyValuePair<string, string>>();
        private static bool failed;

        /// <summary>
        ///   <para>Sets the line with the specified <paramref name="id"/>, or removes it if <paramref name="text"/> is
        ///   <see langword="null"/> or empty. Lines keep the order they were first added in, top to bottom.</para>
        /// </summary>
        public static void Set(string id, string? text)
        {
            if (id is null) throw new ArgumentNullException(nameof(id));
            int index = lines.FindIndex(l => l.Key == id);
            if (string.IsNullOrEmpty(text))
            {
                if (index >= 0) lines.RemoveAt(index);
            }
            else if (index >= 0) lines[index] = new KeyValuePair<string, string>(id, text!);
            else lines.Add(new KeyValuePair<string, string>(id, text!));
            Refresh();
        }

        /// <summary>
        ///   <para>Sets a red line for an install or load problem. Say what to do, e.g. "delete BepInEx\plugins\CCU".</para>
        /// </summary>
        public static void SetProblem(string id, string text) => Set(id, $"<color={ProblemColor}>{text}</color>");

        /// <summary>
        ///   <para>Gets the text of the line with the specified <paramref name="id"/>, or <see langword="null"/>.</para>
        /// </summary>
        public static string? Get(string id)
        {
            foreach (KeyValuePair<string, string> l in lines)
                if (l.Key == id) return l.Value;
            return null;
        }

        internal static void Patch(Harmony harmony)
        {
            harmony.Patch(Fixes.Need(AccessTools.Method(typeof(GameController), nameof(GameController.SetVersionText)),
                "GameController.SetVersionText"), postfix: new HarmonyMethod(typeof(MenuLines), nameof(Refresh)));
        }

        /// <summary>
        ///   <para>Redraws the lines. The game calls this after every <c>GameController.SetVersionText</c>.</para>
        /// </summary>
        public static void Refresh()
        {
            if (failed) return;
            try
            {
                GameController? gc = GameController.gameController;
                Text? version = gc != null ? gc.versionText2 : null;
                if (version == null) return;

                Transform? existing = version.transform.Find(ObjectName);
                if (lines.Count == 0)
                {
                    if (existing != null) existing.gameObject.SetActive(false);
                    return;
                }
                Text text = existing != null ? existing.GetComponent<Text>() : Create(version);
                text.gameObject.SetActive(true);
                var sb = new StringBuilder();
                foreach (KeyValuePair<string, string> l in lines)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(l.Value);
                }
                text.text = sb.ToString();
            }
            catch (Exception e)
            {
                // Never break the menu over a version line: log once and stop drawing.
                failed = true;
                RogueLibsPlusPlugin.Log.LogWarning($"Menu lines disabled: {e.GetType().Name}: {e.Message}");
            }
        }

        // VersionText2 is anchored at its top left and truncates overflow, so extra lines inside it would fall off the
        // screen. A copy of it (same font, outline and shadow) sits on top of it instead and grows upwards.
        private static Text Create(Text version)
        {
            GameObject copy = UnityEngine.Object.Instantiate(version.gameObject, version.transform, false);
            copy.name = ObjectName;
            for (int i = copy.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(copy.transform.GetChild(i).gameObject);

            var rect = (RectTransform)copy.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            Text text = copy.GetComponent<Text>();
            text.alignment = TextAnchor.LowerLeft;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }
    }
}
