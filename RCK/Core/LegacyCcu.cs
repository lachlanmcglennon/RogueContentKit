namespace RCK
{
    /// <summary>
    ///   <para>
    ///     The legacy CCU compatibility vocabulary: every string that campaigns, chunks, characters and saves made for
    ///     Custom Content Utilities (CCU) store, and that RCK therefore reads unchanged. These are data formats, not
    ///     RCK's identity; RCK's own names use <see cref="Rck.Tag"/>, <see cref="Rck.ExtensionTag"/> and
    ///     <see cref="Rck.ExtensionPrefix"/>. Never rename anything listed here. docs/legacy-ccu-vocabulary.md is the
    ///     same list for modders.
    ///   </para>
    ///   <para>Kept in generated tables (from docs/ccu-interface.json):</para>
    ///   <list type="bullet">
    ///     <item><description>Trait IDs: <see cref="RckData.Traits"/> entries that are not <see cref="RckTraitKind.Extension"/>.</description></item>
    ///     <item><description>Renamed-trait conversions: <see cref="RckData.TraitConversions"/>.</description></item>
    ///     <item><description>Mutator names and conversions, including <c>[CCU] Homesickness ...</c>: <see cref="RckData.Mutators"/>, <see cref="RckData.MutatorConversions"/>.</description></item>
    ///     <item><description>Default-goal names: <see cref="RckData.Goals"/>. Item and status-effect names: <see cref="RckData.Items"/>, <see cref="RckData.Effects"/>.</description></item>
    ///     <item><description>The <c>[CCU]FactionRel::</c> spelling of the faction matrix: <see cref="RckData.FactionMatrixLegacyPrefixes"/>.</description></item>
    ///   </list>
    ///   <para>
    ///     Container items need no prefix: a container object (<see cref="RckData.ContainerObjects"/>) stores the item
    ///     name as its whole extraVarString.
    ///   </para>
    /// </summary>
    public static class LegacyCcu
    {
        /// <summary>Investigate text on an object (<see cref="RckData.InvestigateableObjects"/>): this prefix, then the text, in extraVarString.</summary>
        public const string InvestigatePrefix = "investigateable-message:::";

        /// <summary>Level Gate data mutator, e.g. <c>[CCU]LevelGate::Type=Entry;Label=1;Switch=Agent;Logic=AND;</c>. <see cref="Rck.LevelGatePrefix"/> is read too.</summary>
        public const string LevelGatePrefix = "[CCU]LevelGate::";

        /// <summary>The Level Gate heading in the level editor's mutator list.</summary>
        public const string LevelGateMenuHead = "LevelGateMenuHead";

        /// <summary>Level Gate agent-switch traits, one per switch label.</summary>
        public static readonly string[] AgentSwitchTraits = { "Agent_Switch_A", "Agent_Switch_B", "Agent_Switch_C", "Agent_Switch_D" };
    }
}
