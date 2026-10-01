using System;

namespace RCK
{
    /// <summary>What one look at the game found: nothing new, the level ended, it finished loading, or both.</summary>
    [Flags]
    public enum LevelStep
    {
        None = 0,
        /// <summary>A load began, or the controller or level changed without a load being seen.</summary>
        Ended = 1,
        /// <summary>The level finished loading.</summary>
        Loaded = 2,
    }

    /// <summary>
    ///   The level-boundary state machine behind <see cref="LevelScope"/>, with no game types so the tests can drive it.
    ///   The level number alone can repeat (a new run, a restarted level), so a level also ends whenever the game is
    ///   loading (loadComplete is false from LevelTransition.ChangeLevel until the load finishes) or the controller
    ///   changes. A level number that changes mid-load is part of the same load.
    /// </summary>
    public sealed class LevelTracker
    {
        private object? controller;
        private int level = int.MinValue;

        /// <summary>Between <see cref="LevelStep.Ended"/> and <see cref="LevelStep.Loaded"/>.</summary>
        public bool Loading { get; private set; }

        /// <summary>One per level load: goes up when a level ends and holds through the load and play.</summary>
        public int Id { get; private set; }

        /// <summary>Goes up at both boundaries: when a level ends and when the next one has loaded.</summary>
        public int Stamp { get; private set; }

        /// <summary>The level number seen when the current level finished loading.</summary>
        public int Level => level;

        public LevelStep Observe(object controller, int level, bool loadComplete)
        {
            if (loadComplete && !Loading && ReferenceEquals(controller, this.controller) && level == this.level) return LevelStep.None;
            LevelStep step = LevelStep.None;
            if (!Loading)
            {
                Loading = true;
                Id++;
                Stamp++;
                step = LevelStep.Ended;
            }
            if (!loadComplete) return step;
            Loading = false;
            this.controller = controller;
            this.level = level;
            Stamp++;
            return step | LevelStep.Loaded;
        }
    }
}
