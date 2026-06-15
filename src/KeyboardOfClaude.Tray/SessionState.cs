namespace KeyboardOfClaude.Tray;

internal enum SessionState
{
    None = 0,       // nothing waiting -> green
    TurnDone = 1,   // amber
    Blocked = 2,    // red (highest urgency)
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
        return SessionState.None;
    }
}
