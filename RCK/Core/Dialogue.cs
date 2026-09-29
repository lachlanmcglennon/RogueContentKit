using System;
using RogueLibsCore;

namespace RCK
{
    /// <summary>
    ///   Dialogue lines RCK adds for its own interactions. <c>Agent.SayDialogue(key)</c> looks up
    ///   <c>&lt;agentName&gt;_&lt;key&gt;</c>, then <c>NA_&lt;key&gt;</c>, and shows <c>E_&lt;key&gt;</c> when neither
    ///   exists, so every key RCK says is registered here as <c>NA_&lt;key&gt;</c>. Call <c>SayDialogue</c> with the
    ///   literal key so <c>tools\check-game-strings</c> can match it against these registrations.
    /// </summary>
    public static class RckDialogue
    {
        private static bool registered;

        public static void RegisterAll()
        {
            if (registered) return;
            registered = true;
            try
            {
                RogueLibs.CreateCustomName("NA_RCK_Thanks", NameTypes.Dialogue, new CustomNameInfo("Thanks!"));
                RogueLibs.CreateCustomName("NA_RCK_NeedItem", NameTypes.Dialogue, new CustomNameInfo("I don't have the right item."));
            }
            catch (ArgumentException e) { Rck.Log.LogDebug($"RCK dialogue was already registered: {e.Message}"); }
        }
    }
}