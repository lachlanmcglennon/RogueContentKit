namespace RCK
{
    /// <summary>
    ///   A log that goes quiet after <c>max</c> lines until it is reset (usually once per level), so a busy system can't
    ///   flood Player.log.
    /// </summary>
    public sealed class CappedLog
    {
        private readonly int max;
        private int count;

        public CappedLog(int max)
        {
            this.max = max;
        }

        public void Reset() => count = 0;

        public void Info(string line)
        {
            if (Take()) Rck.Log.LogInfo(line);
        }

        public void Warn(string line)
        {
            if (Take()) Rck.Log.LogWarning(line);
        }

        private bool Take()
        {
            if (count >= max) return false;
            count++;
            return true;
        }
    }
}
