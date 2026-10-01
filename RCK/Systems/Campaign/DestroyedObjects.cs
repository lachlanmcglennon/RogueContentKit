using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RCK.Campaign
{
    /// <summary>
    ///   Which object names were destroyed on this level, for the <c>Destroyed=</c> level-gate condition. Wrecked
    ///   objects leave the game's object list, so the gate can't count them afterwards.
    /// </summary>
    internal static class DestroyedObjects
    {
        private static readonly HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static DestroyedObjects() => LevelScope.Ended += ClearNames;

        private static void ClearNames() => names.Clear();

        internal static void Record(ObjectReal obj)
        {
            if (obj == null || string.IsNullOrEmpty(obj.objectName)) return;
            LevelScope.Check();
            names.Add(obj.objectName);
        }

        /// <summary>At least one <paramref name="name"/> was destroyed this level and none is left standing.</summary>
        internal static bool AllDestroyed(GameController gc, string name)
        {
            LevelScope.Check();
            if (!names.Contains(name)) return false;
            foreach (ObjectReal o in gc.objectRealList)
            {
                if (o != null && !o.destroyed && !o.destroying && string.Equals(o.objectName, name, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }

    /// <summary>Every object's DestroyMe except Door's ends in ObjectReal's (the others call base).</summary>
    [HarmonyPatch(typeof(ObjectReal), nameof(ObjectReal.DestroyMe), new[] { typeof(PlayfieldObject) })]
    internal static class ObjectReal_DestroyMe_Gate
    {
        private static void Prefix(ObjectReal __instance)
        {
            try
            {
                if (!__instance.destroying) DestroyedObjects.Record(__instance);
            }
            catch (Exception e)
            {
                if (logged) return;
                logged = true;
                Rck.Log.LogWarning($"Could not record a destroyed object: {e.Message}");
            }
        }

        private static bool logged;
    }
}
