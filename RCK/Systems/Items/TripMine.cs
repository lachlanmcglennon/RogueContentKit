using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace RCK.Items
{
    /// <summary>
    ///   Makes the Tripmine a real trap. The game defines it as a throwable with no image and no effect. RCK gives it its
    ///   sprite; once thrown it arms after a moment and blows up when anyone but its owner and their allies comes near,
    ///   when it's stepped on, or when fire reaches it.
    /// </summary>
    internal static class TripMine
    {
        internal const string ItemName = "TripMine";

        /// <summary>Seconds after landing before it can go off.</summary>
        internal const float ArmTime = 1f;

        /// <summary>Trigger distance in world units (about 2 tiles).</summary>
        internal const float TriggerRadius = 1.3f;

        internal static bool Enabled { get; private set; }

        internal static void Initialize()
        {
            Enabled = !Vanilla.Mentions(AccessTools.Method(typeof(Item), nameof(Item.Explode), Type.EmptyTypes), ItemName, "Tripmine fix");
            ItemText.Description(ItemName,
                "Throw it down and it arms itself. It goes off when an enemy comes near, but leaves you and your allies alone.",
                "Бросьте её, и она сама встанет на боевой взвод. Срабатывает, когда рядом оказывается враг, но не трогает вас и ваших союзников.",
                "扔到地上后会自动布防。敌人靠近时就会引爆，但不会伤到你和你的盟友。");
        }

        internal static void Setup(InvItem item)
        {
            item.LoadItemSprite(ItemName);
            item.reactOnTouch = true;
            item.specialDamage = true;
            item.incendiaryDamage = true;
            item.thiefCantSteal = true;
            item.shadowOffset = 3;
        }

        internal static bool Is(Item item) => Enabled && item != null && (item.itemName == ItemName || (item.invItem != null && item.invItem.invItemName == ItemName));

        // The Land Mine's set-up, without the danger zone the game gave the Tripmine, so people don't steer clear of it.
        internal static void Arm(Item item)
        {
            GameController gc = GameController.gameController;
            if (item.danger != null)
            {
                item.danger.DestroyMe(false);
                item.danger = null;
            }
            item.hasDanger = false;
            item.interactable = false;
            item.SetCantPickUp(true);
            item.objectSprite.dangerous = true;
            item.dangerous = true;
            item.makeObjectsHaveColliders = true;
            if (!gc.objectModifyEnvironmentList.Contains(item)) gc.objectModifyEnvironmentList.Add(item);
            if (gc.serverPlayer) item.StartCoroutine(Watch(item));
        }

        // The host decides when it goes off; the explosion reaches every machine.
        private static IEnumerator Watch(Item mine)
        {
            yield return new WaitForSeconds(ArmTime);
            if (mine == null || mine.didExplode) yield break;
            GameController gc = GameController.gameController;
            gc.audioHandler.Play(mine, "ArmedMine");
            float radiusSq = TriggerRadius * TriggerRadius;
            while (mine != null && !mine.didExplode && mine.dangerous)
            {
                if (!gc.cinematic && !mine.airborne)
                {
                    Vector2 position = mine.tr.position;
                    for (int i = 0; i < gc.agentList.Count; i++)
                    {
                        Agent a = gc.agentList[i];
                        if (a == null || a.dead || a.ghost || a.disappeared || a.jumped || a.fellInHole) continue;
                        if (((Vector2)a.tr.position - position).sqrMagnitude > radiusSq) continue;
                        if (a.statusEffects.hasTrait("DontTriggerFloorHazards")) continue;
                        if (mine.owner != null && CutGun.IsFriendly(mine.owner, a)) continue;
                        gc.audioHandler.Play(mine, "TripLaser");
                        Explode(mine);
                        yield break;
                    }
                }
                yield return new WaitForSeconds(0.1f);
            }
        }

        // The game's Land Mine explosion.
        internal static void Explode(Item mine)
        {
            if (mine.didExplode) return;
            mine.didExplode = true;
            GameController gc = GameController.gameController;
            Explosion explosion = gc.spawnerMain.SpawnExplosion(mine, mine.tr.position, "Normal", false, -1, false, gc.serverPlayer);
            if (explosion != null)
            {
                explosion.agent = mine.owner;
                explosion.realSource = mine;
            }
            mine.DestroyMeFromClient();
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.ThrowAsWeapon))]
    internal static class Item_ThrowAsWeapon_TripMine
    {
        private static void Postfix(Item __instance)
        {
            if (TripMine.Is(__instance)) TripMine.Arm(__instance);
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.Explode))]
    internal static class Item_Explode_TripMine
    {
        private static bool Prefix(Item __instance, ref bool __result)
        {
            if (!TripMine.Is(__instance)) return true;
            __result = !__instance.didExplode;
            TripMine.Explode(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.TouchMe))]
    internal static class Item_TouchMe_TripMine
    {
        private static void Postfix(Item __instance, PlayfieldObject playfieldObject)
        {
            if (!TripMine.Is(__instance) || !__instance.dangerous || __instance.airborne || playfieldObject == null) return;
            GameController gc = GameController.gameController;
            if (gc.cinematic || __instance.didExplode) return;
            string type = playfieldObject.playfieldObjectType;
            if (type == "Agent")
            {
                Agent a = (Agent)playfieldObject;
                if (a.jumped || a.statusEffects.hasTrait("DontTriggerFloorHazards")) return;
                if (__instance.owner != null && CutGun.IsFriendly(__instance.owner, a)) return;
            }
            else if (type != "Item" && type != "ObjectReal")
            {
                return;
            }
            TripMine.Explode(__instance);
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.IncendiaryDamage))]
    internal static class Item_IncendiaryDamage_TripMine
    {
        private static void Postfix(Item __instance)
        {
            if (TripMine.Is(__instance) && __instance.dangerous) TripMine.Explode(__instance);
        }
    }
}
