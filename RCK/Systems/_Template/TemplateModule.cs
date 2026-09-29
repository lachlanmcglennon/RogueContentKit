using HarmonyLib;

namespace RCK.Template
{
    /// <summary>Example module. Every public IRckModule is created and initialized after all traits are registered.</summary>
    public sealed class TemplateModule : IRckModule
    {
        public string Name => "Template";

        public void Initialize()
        {
            Rck.Describe("Big", "Blows up when it dies.");
            Rck.TraitAdded += static (agent, trait, _) =>
            {
                if (trait == "Big") Rck.Log.LogDebug($"{agent.agentName} got {trait}");
            };
        }
    }

    // Any [HarmonyPatch] class in the module is applied on its own; a missing target only logs an error.
    [HarmonyPatch(typeof(Agent), nameof(Agent.Awake))]
    internal static class Agent_Awake_Example
    {
        private static void Postfix(Agent __instance)
        {
            if (AgentTraits.Has(__instance, "Big")) { /* ... */ }
        }
    }
}

