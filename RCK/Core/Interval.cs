namespace RCK
{
    /// <summary>Throttles a tick to once every few seconds.</summary>
    public static class Interval
    {
        /// <summary>
        ///   True at most once every <paramref name="seconds"/>, and then moves <paramref name="next"/> on. Also true
        ///   when the clock has gone back before the last tick (a new level restarts the time) or when
        ///   <paramref name="force"/> is set. A <paramref name="next"/> of 0 (or any time far behind) is due at once.
        /// </summary>
        public static bool Due(ref float next, float now, float seconds, bool force = false)
        {
            if (!force && now < next && now >= next - seconds) return false;
            next = now + seconds;
            return true;
        }
    }
}
