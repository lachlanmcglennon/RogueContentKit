using HarmonyLib;

namespace RCK.Items
{
    /// <summary>
    ///   The Voodoo Doll takes over one person, like the Mind Control special ability, for as long as the game's
    ///   "Mind Controlling" effect lasts (30 seconds). The game had its description but no way to use it.
    /// </summary>
    internal static class VoodooDoll
    {
        internal const string ItemName = "VoodooDoll";

        /// <summary>The game prices the doll at 5, far too cheap for what it now does.</summary>
        internal const int Value = 60;

        /// <summary>The game's Target Object range.</summary>
        internal const float Range = 15f;

        internal static bool Enabled { get; private set; }

        internal static void Initialize()
        {
            if (Vanilla.Mentions(AccessTools.Method(typeof(ItemFunctions), nameof(ItemFunctions.TargetObject)), ItemName, "Voodoo Doll fix")) return;
            Enabled = UsableItems.Register(ItemName, "Voodoo Doll fix", static (item, _) => item.invInterface.ShowOrHideTarget(item));
        }

        internal static bool CanTarget(Agent user, Agent victim)
        {
            if (victim == null || victim == user || victim.dead || victim.ghost || victim.isPlayer != 0) return false;
            if (victim.butlerBot || victim.mechEmpty || victim.invisible || victim.preventsMindControl || victim.oma.bodyGuarded) return false;
            if (victim.oma.mindControlled || victim.statusEffects.hasStatusEffect("MindControlled")) return false;
            if (victim.statusEffects.hasSpecialAbility("MindControl")) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(ItemFunctions), nameof(ItemFunctions.TargetObject))]
    internal static class ItemFunctions_TargetObject_VoodooDoll
    {
        private static bool Prefix(InvItem item, Agent agent, PlayfieldObject otherObject, string combineType, ref bool __result)
        {
            if (!VoodooDoll.Enabled || item == null || item.invItemName != VoodooDoll.ItemName) return true;
            __result = false;
            if (agent == null || otherObject == null || otherObject.playfieldObjectType != "Agent" || otherObject.someoneInteracting) return false;
            if (UnityEngine.Vector2.Distance(agent.curPosition, otherObject.curPosition) > VoodooDoll.Range) return false;
            Agent victim = (Agent)otherObject;
            if (!VoodooDoll.CanTarget(agent, victim)) return false;
            __result = true;
            if (combineType != "Combine") return false;

            item.invInterface.HideTarget();
            item.database.SubtractFromItemCount(item, 1);
            // Lets the user's own movement steer the puppet, as the special ability does.
            agent.hasMindControl = true;
            victim.relationships.MindControl(agent);
            GameController.gameController.audioHandler.Play(agent, "MindControlSuccess");
            item.itemFunctions.UseItemAnim(item, agent);
            return false;
        }
    }

    /// <summary>
    ///   The game only frees puppets for users with the Mind Control ability, so a doll's puppet would stay under
    ///   control after "Mind Controlling" runs out. Frees them for doll users too.
    /// </summary>
    [HarmonyPatch(typeof(Relationships), nameof(Relationships.StopAgentsUnderMindControl))]
    internal static class Relationships_StopAgentsUnderMindControl_VoodooDoll
    {
        private static bool Prefix(Agent ___agent)
        {
            if (!VoodooDoll.Enabled || ___agent == null || ___agent.statusEffects.hasSpecialAbility("MindControl")) return true;
            GameController gc = GameController.gameController;
            if (gc.serverPlayer)
            {
                var agents = gc.agentList;
                for (int i = agents.Count - 1; i >= 0; i--)
                {
                    Agent puppet = agents[i];
                    if (puppet.oma.mindControlled && puppet.mindControlAgent == ___agent) puppet.relationships.StopMindControl();
                }
            }
            else if (___agent.localPlayer)
            {
                ___agent.objectMult.CmdStopAgentsUnderMindControl();
            }
            if (___agent.localPlayer || (gc.serverPlayer && ___agent.isPlayer == 0)) ___agent.hasMindControl = false;
            return false;
        }
    }
}
