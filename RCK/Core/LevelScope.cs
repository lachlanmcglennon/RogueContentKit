using System;

namespace RCK
{
    /// <summary>
    ///   The one place that decides when a level ends and when the next has loaded (see <see cref="LevelTracker"/>).
    ///   Systems never compare level numbers themselves. They drop per-level state in <see cref="Ended"/>, or with
    ///   <see cref="ResetAtBoth"/> when the state is built from the finished level, and use <see cref="IsNew"/> for lazy
    ///   once-per-load setup that needs the level. Runs on every peer: <see cref="RckPlugin"/> checks each frame, and a
    ///   system calls <see cref="Check()"/> before it reads its state so a change is seen at once.
    /// </summary>
    public static class LevelScope
    {
        private static readonly LevelTracker tracker = new LevelTracker();
        private static bool raising;

        /// <summary>
        ///   The level ended: a load began (also a restart of the same level), or the controller or level changed with no
        ///   load seen. Handlers clear their own state only; they don't call other systems.
        /// </summary>
        public static event Action? Ended;

        /// <summary>The level finished loading. Runs after <see cref="Ended"/> when one check finds both.</summary>
        public static event Action? Loaded;

        /// <summary>One per level load: goes up at <see cref="Ended"/> and holds through the load and play. For per-agent marks.</summary>
        public static int Id => tracker.Id;

        /// <summary><see cref="Id"/> after a <see cref="Check()"/>, for marks made while a level loads.</summary>
        public static int CurrentId()
        {
            Check();
            return tracker.Id;
        }

        /// <summary>Goes up at <see cref="Ended"/> and again at <see cref="Loaded"/>.</summary>
        public static int Stamp => tracker.Stamp;

        /// <summary>True from <see cref="Ended"/> until <see cref="Loaded"/>.</summary>
        public static bool Loading => tracker.Loading;

        /// <summary>The level number when the current level finished loading.</summary>
        public static int Level => tracker.Level;

        /// <summary>Looks for a level change now, so state read straight after is current. Cheap; safe from any hook.</summary>
        public static void Check() => Check(GameController.gameController);

        /// <inheritdoc cref="Check()"/>
        public static void Check(GameController? gc)
        {
            if (gc == null || raising) return;
            int level = gc.sessionDataBig != null ? gc.sessionDataBig.curLevelEndless : 0;
            LevelStep step = tracker.Observe(gc, level, gc.loadComplete);
            if (step == LevelStep.None) return;
            raising = true;
            try
            {
                if ((step & LevelStep.Ended) != 0) Raise(Ended, nameof(Ended));
                if ((step & LevelStep.Loaded) != 0) Raise(Loaded, nameof(Loaded));
            }
            finally
            {
                raising = false;
            }
        }

        /// <summary>
        ///   Calls <paramref name="reset"/> at both <see cref="Ended"/> and <see cref="Loaded"/>: for state built from the
        ///   finished level, which anything gathered while it was loading would make stale.
        /// </summary>
        public static void ResetAtBoth(Action reset)
        {
            Ended += reset;
            Loaded += reset;
        }

        /// <summary>True the first time it's asked after <see cref="Stamp"/> changed, and notes the stamp in <paramref name="seen"/>.</summary>
        public static bool IsNew(ref int seen)
        {
            if (seen == tracker.Stamp) return false;
            seen = tracker.Stamp;
            return true;
        }

        // One failing handler mustn't stop the others from dropping their state.
        private static void Raise(Action? handlers, string name)
        {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception e) { Rck.Log.LogError($"Level {name}: {d.Method.DeclaringType?.Name}.{d.Method.Name} failed: {e}"); }
            }
        }
    }
}
