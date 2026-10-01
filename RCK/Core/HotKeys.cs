#nullable disable
using System;
using System.Collections.Generic;
using Rewired;
using UnityEngine;

namespace RCK
{
    /// <summary>
    ///   RCK's own toggle keys (the <c>[Map]</c> keys of <see cref="RckConfig"/>). <see cref="Pressed"/> ignores keys
    ///   while the player types in chat. <see cref="Clash"/> looks the key up in the player's own SoR keyboard controls
    ///   (Rewired), so a key the player also bound to a game action can be called out on screen and in the log.
    /// </summary>
    public static class HotKeys
    {
        private const float RecheckSeconds = 2f;
        private static readonly Dictionary<KeyCode, string> bound = new Dictionary<KeyCode, string>();
        private static readonly HashSet<KeyCode> warned = new HashSet<KeyCode>();
        private static float nextCheck;
        private static bool broken;

        /// <summary>True on the frame <paramref name="key"/> goes down, unless it's None or the player is typing in chat.</summary>
        public static bool Pressed(KeyCode key)
        {
            if (key == KeyCode.None || !Input.GetKeyDown(key)) return false;
            GameController gc = Rck.gc;
            return gc == null || gc.chatLog == null || !gc.chatLog.chatting;
        }

        /// <summary>The key's name for on-screen hints: <c>T</c>, <c>5</c> for Alpha5, or <c>(no key)</c>.</summary>
        public static string Name(KeyCode key)
        {
            if (key == KeyCode.None) return "(no key)";
            string name = key.ToString();
            return name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6 ? name.Substring(5) : name;
        }

        /// <summary>
        ///   The game action the player's enabled keyboard controls bind to <paramref name="key"/> (its descriptive name,
        ///   e.g. <c>Reload</c>), or null when the key is free. Re-read every couple of seconds, so a rebind in the
        ///   controls menu shows up.
        /// </summary>
        public static string Clash(KeyCode key)
        {
            if (key == KeyCode.None || broken) return null;
            if (Interval.Due(ref nextCheck, Time.unscaledTime, RecheckSeconds)) Recheck();
            return bound.TryGetValue(key, out string action) ? action : null;
        }

        /// <summary>
        ///   <see cref="Clash"/>, and the first time a key clashes in this game session, a warning in the log that says
        ///   which <c>[Map]</c> <paramref name="setting"/> moves it.
        /// </summary>
        public static string ClashWarned(KeyCode key, string setting)
        {
            string action = Clash(key);
            if (action != null && warned.Add(key))
                Rck.Log.LogWarning($"RCK: {setting} = {key} is also \"{action}\" in your SoR controls, so both happen on a press. "
                    + $"Pick another key for {setting} in the [Map] section of {Rck.Config?.FilePath ?? "RCK's BepInEx config"}.");
            return action;
        }

        private static void Recheck()
        {
            bound.Clear();
            try
            {
                if (!ReInput.isReady) return;
                Player player = ReInput.players.GetPlayer(0);
                if (player == null) return;
                foreach (ControllerMap map in player.controllers.maps.GetAllMaps(ControllerType.Keyboard))
                {
                    // The game enables the "Keyboard" category while the keyboard plays and disables the rest.
                    if (map == null || !map.enabled) continue;
                    foreach (ActionElementMap e in map.AllMaps)
                    {
                        if (e == null || !e.enabled || e.keyCode == KeyCode.None || bound.ContainsKey(e.keyCode)) continue;
                        string action = e.actionDescriptiveName;
                        if (string.IsNullOrEmpty(action)) action = ReInput.mapping.GetAction(e.actionId)?.name;
                        bound[e.keyCode] = string.IsNullOrEmpty(action) ? "a game action" : action;
                    }
                }
            }
            catch (Exception e)
            {
                broken = true;
                bound.Clear();
                Rck.Log.LogWarning($"RCK: couldn't read the SoR controls to check RCK's keys for clashes: {e.Message}");
            }
        }
    }
}
