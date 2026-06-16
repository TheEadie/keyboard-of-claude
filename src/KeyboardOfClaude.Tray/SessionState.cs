namespace KeyboardOfClaude.Tray;

internal enum SessionState
{
    None = 0,       // nothing waiting -> green
    Working = 1,    // work in progress -> amber
    TurnDone = 2,   // awaiting user -> flashing red
    Blocked = 3,    // needs permission -> flashing red (highest urgency)
}

internal static class SessionStateParser
{
    // Whitespace-trimmed, case-insensitive token match. Anything else => None.
    public static SessionState Parse(string? content)
    {
        var token = content?.Trim();
        if (string.Equals(token, "blocked", StringComparison.OrdinalIgnoreCase))
            return SessionState.Blocked;
        if (string.Equals(token, "turn-done", StringComparison.OrdinalIgnoreCase))
            return SessionState.TurnDone;
        if (string.Equals(token, "working", StringComparison.OrdinalIgnoreCase))
            return SessionState.Working;
        return SessionState.None;
    }
}
