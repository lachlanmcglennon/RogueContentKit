#nullable disable
using System;
using System.Collections.Generic;

namespace RCK.Social
{
    /// <summary>Shared relationship operations: the one lookup, quiet changes, calming and forced hate.</summary>
    internal static class RelOps
    {
        /// <summary>How <paramref name="source"/> feels about <paramref name="target"/>, or null if there is no entry.</summary>
        internal static Relationship Of(Agent source, Agent target) => source == null ? null : Of(source.relationships, target);

        internal static Relationship Of(Relationships rels, Agent target)
        {
            List<Relationship> list = rels != null ? rels.RelList2 : null;
            int id = target != null ? target.agentID : -1;
            return list == null || id < 0 || id >= list.Count ? null : list[id];
        }

        /// <summary>
        ///   For <c>using</c>: relationship changes inside are RCK's own, so <see cref="HostilityDiagnostics"/> doesn't
        ///   report them and, with <paramref name="disguises"/>, <see cref="Disguises"/> doesn't treat them as anyone
        ///   noticing a disguise. Nests, and puts the old state back on dispose.
        /// </summary>
        internal static QuietScope Quietly(bool disguises = false) => new QuietScope(disguises);

        internal readonly struct QuietScope : IDisposable
        {
            private readonly bool previous, disguises;

            internal QuietScope(bool disguises)
            {
                this.disguises = disguises;
                if (disguises) Disguises.Quiet++;
                previous = HostilityDiagnostics.Suppress;
                HostilityDiagnostics.Suppress = true;
            }

            public void Dispose()
            {
                HostilityDiagnostics.Suppress = previous;
                if (disguises) Disguises.Quiet--;
            }
        }

        /// <summary>
        ///   Clears hate and strikes directly (vanilla <c>SetRelHate(0)</c> does nothing once hate has reached 5, and the
        ///   hate left behind would turn the tie hostile again on the next hate change), then turns Annoyed or Hateful
        ///   into <paramref name="to"/>.
        /// </summary>
        internal static void Calm(Agent source, Agent target, string to)
        {
            Relationship rel = Of(source, target);
            if (rel == null) return;
            rel.relHate = 0f;
            rel.relStrikes = 0;
            if (rel.relTypeCode == relStatus.Hostile || rel.relTypeCode == relStatus.Annoyed) source.relationships.SetRel(target, to);
        }

        /// <summary>
        ///   <paramref name="source"/> turns Hateful toward <paramref name="target"/> at hate 5. SetRel overrides Aligned
        ///   (hate alone leaves it), then hate 5 keeps the tie from cooling. False if it already was.
        /// </summary>
        internal static bool ForceHate(Agent source, Agent target, Relationship rel)
        {
            if (rel.relTypeCode == relStatus.Hostile && rel.relHate >= 5f) return false;
            if (rel.relTypeCode != relStatus.Hostile) source.relationships.SetRel(target, "Hateful");
            source.relationships.SetRelHate(target, 5);
            return true;
        }
    }
}
