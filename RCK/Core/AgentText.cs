namespace RCK
{
    /// <summary>Short agent text for log lines.</summary>
    public static class AgentText
    {
        /// <summary><c>Gangbanger #12</c>, or <c>nobody</c>.</summary>
        public static string Describe(Agent? agent) => agent == null ? "nobody" : $"{agent.agentName} #{agent.agentID}";
    }
}
