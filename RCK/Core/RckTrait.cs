using System;
using RogueLibsCore;

namespace RCK
{
    /// <summary>
    ///   Base type for every RCK trait. Each trait ID has its own sealed subclass (see Generated\TraitTypes.g.cs)
    ///   because RogueLibs takes the trait name from the type. Behaviour lives in the system modules, which react to
    ///   <see cref="Rck.TraitAdded"/> / <see cref="Rck.TraitRemoved"/> or query <see cref="AgentTraits"/>.
    /// </summary>
    public abstract class RckTrait : CustomTrait
    {
        public override void OnAdded()
        {
            Agent? owner = SafeOwner();
            if (owner is null) return;
            AgentTraits.Invalidate(owner);
            Rck.RaiseTraitAdded(owner, Trait.traitName, this);
        }

        public override void OnRemoved()
        {
            Agent? owner = SafeOwner();
            if (owner is null) return;
            AgentTraits.Invalidate(owner);
            Rck.RaiseTraitRemoved(owner, Trait.traitName, this);
        }

        private Agent? SafeOwner()
        {
            try { return Owner; }
            catch (Exception) { return null; }
        }
    }
}
