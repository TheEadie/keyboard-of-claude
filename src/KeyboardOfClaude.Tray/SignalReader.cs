namespace KeyboardOfClaude.Tray;

internal static class SignalReader
{
    /// <summary>
    /// Scans the directory's regular files, parses each file's content, and
    /// returns the maximum urgency. Empty/unrecognized files count as None;
    /// unreadable files (mid-write / locked / vanished) also count as None.
    /// A missing directory returns None.
    /// </summary>
    public static SessionState Aggregate(string signalDirectory)
    {
        var max = SessionState.None;
        string[] files;
        try
        {
            if (!Directory.Exists(signalDirectory))
                return SessionState.None;
            files = Directory.GetFiles(signalDirectory); // top-level regular files only
        }
        catch
        {
            return SessionState.None;
        }

        foreach (var path in files)
        {
            SessionState state;
            try
            {
                // Open with FileShare.ReadWrite so a file being written mid-scan
                // doesn't throw a sharing violation.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                state = SessionStateParser.Parse(reader.ReadToEnd());
            }
            catch
            {
                // mid-write / locked / vanished: treat as None (absent) for
                // this recomputation, per spec. The state self-corrects on the
                // next change once the file is fully written.
                state = SessionState.None;
            }
            if (state > max) max = state;
            if (max == SessionState.Blocked) break; // can't get more urgent
        }
        return max;
    }
}
