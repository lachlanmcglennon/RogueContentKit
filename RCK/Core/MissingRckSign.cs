using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RCK
{
    /// <summary>
    ///   Campaigns that need RCK place a vanilla Sign whose text starts with <see cref="Rck.MissingRckSignTag"/>, telling
    ///   players without RCK what to install. With RCK loaded, each one is hidden as soon as it starts and removed when the
    ///   level has loaded, the way the game removes its own start-of-level signs. The host removes it for everyone; a
    ///   client only keeps its copy hidden.
    /// </summary>
    internal sealed class MissingRckSign : MonoBehaviour
    {
        private static int removed;
        private static int hidden;
        private static bool logQueued;

        private readonly List<Renderer> renderers = new List<Renderer>();
        private Sign sign = null!;

        internal static bool IsTagged(Sign sign)
        {
            string? text = !string.IsNullOrEmpty(sign.extraVarString) ? sign.extraVarString : sign.signTextOnline;
            return text != null && text.StartsWith(Rck.MissingRckSignTag, StringComparison.Ordinal);
        }

        internal static void Handle(Sign sign)
        {
            GameController gc = sign.gc;
            if (gc == null || gc.streamingWorld || sign.GetComponent<MissingRckSign>() != null) return;
            if (IsTagged(sign)) sign.gameObject.AddComponent<MissingRckSign>().Begin(sign);
            // A client can get the synced text a moment after Start: keep checking until the level has loaded.
            else if (!gc.serverPlayer && string.IsNullOrEmpty(sign.signTextOnline)) sign.StartCoroutine(WaitForText(sign));
        }

        private static IEnumerator WaitForText(Sign sign)
        {
            float until = Time.realtimeSinceStartup + 10f;
            while (sign != null && string.IsNullOrEmpty(sign.signTextOnline) && Time.realtimeSinceStartup < until)
                yield return null;
            if (sign != null && sign.GetComponent<MissingRckSign>() == null && IsTagged(sign))
                sign.gameObject.AddComponent<MissingRckSign>().Begin(sign);
        }

        private void Begin(Sign target)
        {
            sign = target;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                renderers.Add(r);
            }
            StartCoroutine(RemoveWhenLoaded());
        }

        private IEnumerator RemoveWhenLoaded()
        {
            GameController gc = sign.gc;
            do yield return null;
            while (!gc.loadComplete);

            if (!gc.serverPlayer)
            {
                hidden++;
                QueueLog();
                yield break;
            }
            Restore();
            removed++;
            QueueLog();
            // RemoveMe pools the sign in single player and unspawns it for clients in multiplayer.
            sign.RemoveMe();
            Destroy(this);
        }

        internal void Restore()
        {
            foreach (Renderer r in renderers)
                if (r != null) r.enabled = true;
            renderers.Clear();
        }

        private static void QueueLog()
        {
            if (logQueued || RckPlugin.Instance == null) return;
            logQueued = true;
            RckPlugin.Instance.StartCoroutine(LogNextFrame());
        }

        // Every sign's coroutine resumes in the same frame, so one line per level covers them all.
        private static IEnumerator LogNextFrame()
        {
            yield return null;
            if (removed > 0) Rck.Log.LogInfo($"Missing-RCK signs: removed {removed} at level start.");
            if (hidden > 0) Rck.Log.LogInfo($"Missing-RCK signs: hid {hidden} at level start; the host removes them.");
            removed = hidden = 0;
            logQueued = false;
        }
    }

    [HarmonyPatch(typeof(Sign), "Start")]
    internal static class Sign_Start_MissingRck
    {
        private static void Postfix(Sign __instance)
        {
            try { MissingRckSign.Handle(__instance); }
            catch (Exception e) { Rck.Log.LogError($"Missing-RCK sign: {e}"); }
        }
    }

    [HarmonyPatch(typeof(Sign), nameof(Sign.RecycleAwake))]
    internal static class Sign_RecycleAwake_MissingRck
    {
        // A pooled sign reused for another level must not stay hidden.
        private static void Postfix(Sign __instance)
        {
            MissingRckSign? marker = __instance.GetComponent<MissingRckSign>();
            if (marker == null) return;
            marker.Restore();
            UnityEngine.Object.Destroy(marker);
        }
    }
}
