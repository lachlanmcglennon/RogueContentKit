using System.Collections;
using System.Collections.Generic;
using RogueLibsCore;
using UnityEngine;

namespace RCK.Items
{
    /// <summary>
    ///   The Mood Ring (worn like armour) warns you about people nearby who secretly mean you harm: those who hate you
    ///   in secret, and hidden werewolves and shapeshifters. The game called it "Useless!" and it did nothing.
    /// </summary>
    internal static class MoodRing
    {
        internal const string ItemName = "MoodRing";

        /// <summary>How far the ring senses, in world units (about 12 tiles).</summary>
        internal const float Range = 8f;

        /// <summary>Seconds before the ring warns about the same person again.</summary>
        internal const float Repeat = 15f;

        private const string BadVibes = "RCK_MoodRingBadVibes";

        private static readonly Dictionary<Agent, float> lastWarned = new Dictionary<Agent, float>();

        internal static void Initialize()
        {
            ItemText.Description(ItemName,
                "Wear it and it warns you when someone nearby secretly means you harm, like a hidden hater, werewolf or shapeshifter.",
                "Наденьте его, и оно предупредит, когда кто-то рядом тайно желает вам зла: скрытый недоброжелатель, оборотень или перевёртыш.",
                "戴上后，当附近有人暗中想害你时（比如暗藏敌意的人、狼人或变形者），它会发出警告。");
            ItemText.Name(BadVibes, NameTypes.Interface, "Bad vibes...", "Плохая аура...", "不祥之感……");
            RckPlugin.Instance.StartCoroutine(Watch());
        }

        private static IEnumerator Watch()
        {
            WaitForSeconds wait = new WaitForSeconds(0.5f);
            WaitForSeconds backOff = new WaitForSeconds(5f);
            while (true)
            {
                yield return wait;
                bool failed = false;
                try
                {
                    Check();
                }
                catch (System.Exception e)
                {
                    Rck.Log.LogWarning($"Mood Ring check failed: {e.Message}");
                    failed = true;
                }
                if (failed) yield return backOff;
            }
        }

        private static void Check()
        {
            GameController gc = GameController.gameController;
            if (gc == null || !gc.loadComplete || gc.cinematic || gc.playerAgentList == null) return;
            if (lastWarned.Count > 64) lastWarned.Clear();
            float rangeSq = Range * Range;
            foreach (Agent player in gc.playerAgentList)
            {
                if (player == null || !player.localPlayer || player.dead || player.ghost || !IsWearing(player)) continue;
                for (int i = 0; i < gc.agentList.Count; i++)
                {
                    Agent npc = gc.agentList[i];
                    if (npc == null || npc == player || npc.isPlayer != 0 || npc.dead || npc.ghost) continue;
                    if (((Vector2)(npc.tr.position - player.tr.position)).sqrMagnitude > rangeSq) continue;
                    if (!MeansHarm(npc, player)) continue;
                    if (lastWarned.TryGetValue(npc, out float last) && Time.time - last < Repeat) continue;
                    if (!player.movement.HasLOSAgent360(npc)) continue;
                    lastWarned[npc] = Time.time;
                    gc.spawnerMain.SpawnStatusText(npc, "Debuff", BadVibes, NameTypes.Interface);
                }
            }
        }

        private static bool IsWearing(Agent agent)
        {
            InvDatabase inv = agent.inventory;
            return inv != null && ((inv.equippedArmor != null && inv.equippedArmor.invItemName == ItemName)
                || (inv.equippedArmorHead != null && inv.equippedArmorHead.invItemName == ItemName));
        }

        private static bool MeansHarm(Agent npc, Agent player)
        {
            if (npc.oma.secretWerewolf || npc.oma.secretShapeShifter || npc.secretShapeShifter) return true;
            Relationship rel = npc.relationships.GetRelationship(player);
            return rel != null && (rel.secretHate || rel.mechHate);
        }
    }
}
