using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RCK.Items
{
    /// <summary>
    ///   The Five-Leaf and Six-Leaf Clovers work like the game's Four-Leaf Clover, only stronger: carrying one adds
    ///   two (Five) or three (Six) times the Four-Leaf bonus to every luck roll. They stack with each other and with
    ///   the Four-Leaf Clover.
    /// </summary>
    internal static class Clovers
    {
        internal const string FiveLeaf = "FiveLeafClover";
        internal const string SixLeaf = "SixLeafClover";

        internal const int FiveLeafMultiplier = 2;
        internal const int SixLeafMultiplier = 3;

        internal static bool Enabled { get; private set; }

        // The Four-Leaf Clover's bonus for each luck roll, from PlayfieldObject.DetermineLuck.
        private static readonly Dictionary<string, int> FourLeafBonus = new Dictionary<string, int>
        {
            ["CritChance"] = 3,
            ["ChanceToKnockWeapons"] = 5,
            ["ChanceToSlowEnemies"] = 4,
            ["ChanceAttacksDoZeroDamage"] = 4,
            ["AttacksDamageAttacker"] = 10,
            ["GunAim"] = 5,
            ["DoorDetonator"] = 10,
            ["Hack"] = 10,
            ["DestroyGravestone"] = -5,
            ["SecurityCam"] = 10,
            ["SlotMachine"] = 8,
            ["TurnTables"] = 10,
            ["FindThreat"] = 8,
            ["FindAskPercentage"] = 8,
            ["FindAskMayorHatPercentage"] = 8,
            ["Joke"] = 10,
            ["FreeShopItem"] = 10,
            ["FreeShopItem2"] = 10,
            ["ThiefToolsMayNotSubtract"] = 10,
        };

        internal static void Initialize()
        {
            MethodInfo luck = AccessTools.Method(typeof(PlayfieldObject), nameof(PlayfieldObject.DetermineLuck));
            Enabled = !Vanilla.Mentions(luck, FiveLeaf, "Five-Leaf Clover fix")
                && !Vanilla.Mentions(luck, SixLeaf, "Six-Leaf Clover fix");
        }

        internal static int Bonus(Agent agent, string luckType)
        {
            if (luckType == null || !FourLeafBonus.TryGetValue(luckType, out int perClover) || agent.inventory == null) return 0;
            int clovers = 0;
            if (agent.inventory.HasItem(FiveLeaf)) clovers += FiveLeafMultiplier;
            if (agent.inventory.HasItem(SixLeaf)) clovers += SixLeafMultiplier;
            return perClover * clovers;
        }
    }

    [HarmonyPatch(typeof(PlayfieldObject), nameof(PlayfieldObject.DetermineLuck))]
    internal static class PlayfieldObject_DetermineLuck_Clovers
    {
        private static void Postfix(PlayfieldObject __instance, int originalLuck, string luckType, ref int __result)
        {
            if (!Clovers.Enabled || originalLuck == 0) return;
            Agent agent = __instance.playfieldObjectAgent;
            if (agent == null || agent.isPlayer == 0) return;
            int bonus = Clovers.Bonus(agent, luckType);
            if (bonus == 0) return;
            int luck = __result + bonus;
            __result = luck > 100 ? 100 : luck < 0 ? 0 : luck;
        }
    }
}
