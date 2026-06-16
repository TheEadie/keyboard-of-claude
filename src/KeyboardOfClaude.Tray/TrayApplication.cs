using System.Drawing;

namespace KeyboardOfClaude.Tray;

internal sealed class TrayApplication : ApplicationContext
{
    // Coalesce bursts of watcher events into a single recompute.
    private const int DebounceMs = 75;
    // Safety net: re-scan periodically so dropped watcher events (buffer
    // overflow) and keyboard hotplug eventually self-correct.
    private const int ResyncMs = 5000;
    // Half-period of the red attention flash: the keyboard alternates between
    // the lit colour and off every FlashMs.
    private const int FlashMs = 600;

    private readonly string _signalDir;
    private readonly NotifyIcon _notifyIcon;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly System.Windows.Forms.Timer _resyncTimer;
    private readonly System.Windows.Forms.Timer _flashTimer;
    private bool _flashOn;
    private readonly SynchronizationContext _syncContext;

    // One robot icon per distinct status colour, built on demand and cached.
    private readonly Dictionary<SessionState, Icon> _icons = new();

    private SessionState _lastPaintedState = SessionState.None;
    private bool _lastPaintOk;

    public TrayApplication()
    {
        // We are constructed before Application.Run pumps the message loop, so
        // the WinForms SynchronizationContext is not installed yet. Install one
        // now; it binds to this STA thread, which Application.Run then pumps, so
        // ScheduleRecompute's Post marshals watcher events onto the UI thread.
        if (SynchronizationContext.Current is null)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        _syncContext = SynchronizationContext.Current!;

        _signalDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "keyboard-of-claude",
            "signals");

        Directory.CreateDirectory(_signalDir);

        var resetItem = new ToolStripMenuItem("Reset");
        resetItem.Click += OnReset;

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += OnQuit;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(resetItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(quitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = IconFor(SessionState.None),
            Text = "keyboard-of-claude: resting",
            ContextMenuStrip = contextMenu,
            Visible = true,
        };

        // Timers run on the UI thread; their Tick coalesces work and triggers
        // the recompute. We schedule rather than paint inline so a burst of
        // watcher events collapses into one repaint.
        _debounceTimer = new System.Windows.Forms.Timer { Interval = DebounceMs };
        _debounceTimer.Tick += OnDebounceTick;

        _resyncTimer = new System.Windows.Forms.Timer { Interval = ResyncMs };
        _resyncTimer.Tick += (_, _) => Recompute();
        _resyncTimer.Start();

        // Drives the flashing-red attention states. Started/stopped by Recompute
        // depending on whether the current state is one that should flash.
        _flashTimer = new System.Windows.Forms.Timer { Interval = FlashMs };
        _flashTimer.Tick += OnFlashTick;

        // Initial scan before watching starts.
        Recompute();

        _watcher = new FileSystemWatcher(_signalDir)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };

        _watcher.Created += OnWatcherEvent;
        _watcher.Changed += OnWatcherEvent;
        _watcher.Deleted += OnWatcherEvent;
        _watcher.Renamed += OnWatcherEvent;
        // On buffer overflow the OS drops events; the resync timer recovers,
        // but kick a recompute immediately too.
        _watcher.Error += OnWatcherError;

        _watcher.EnableRaisingEvents = true;
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) => ScheduleRecompute();

    private void OnWatcherError(object sender, ErrorEventArgs e) => ScheduleRecompute();

    private void ScheduleRecompute()
    {
        // FileSystemWatcher raises events on a thread-pool thread; marshal onto
        // the UI thread, then (re)start the debounce timer there so a burst of
        // events collapses into a single repaint.
        _syncContext.Post(_ =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }, null);
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        Recompute();
    }

    private void Recompute()
    {
        try
        {
            var state = SignalReader.Aggregate(_signalDir);

            if (state == _lastPaintedState && _lastPaintOk)
                return;

            _lastPaintedState = state;

            bool ok;
            if (IsFlashing(state))
            {
                // Start (or keep) the flash cycle lit; OnFlashTick toggles from here.
                _flashOn = true;
                var (fr, fg, fb) = ColourFor(state);
                ok = G213Keyboard.TryPaint(fr, fg, fb);
                _flashTimer.Start();
            }
            else
            {
                _flashTimer.Stop();
                _flashOn = false;
                var (r, g, b) = ColourFor(state);
                ok = G213Keyboard.TryPaint(r, g, b);
            }
            _lastPaintOk = ok;

            _notifyIcon.Icon = IconFor(state);

            var tooltip = $"keyboard-of-claude: {LabelFor(state)}";
            if (!ok)
                tooltip += " (keyboard not found)";

            _notifyIcon.Text = tooltip;
        }
        catch
        {
            // A transient failure must never tear down the resident tray
            // process; swallow and let the next event / resync retry.
        }
    }

    // Blocked and TurnDone both demand attention and flash; everything else is solid.
    private static bool IsFlashing(SessionState state) =>
        state is SessionState.Blocked or SessionState.TurnDone;

    private void OnFlashTick(object? sender, EventArgs e)
    {
        // Alternate the current flashing state's colour with off. Painting
        // directly bypasses Recompute's skip-if-unchanged guard.
        _flashOn = !_flashOn;
        var (r, g, b) = _flashOn ? ColourFor(_lastPaintedState) : ((byte)0, (byte)0, (byte)0);
        _lastPaintOk = G213Keyboard.TryPaint(r, g, b);
    }

    private Icon IconFor(SessionState state)
    {
        // Blocked and TurnDone both paint red, and every "resting" variant shares
        // green, so cache by the distinct colours ColourFor actually produces.
        var key = state switch
        {
            SessionState.Blocked  => SessionState.Blocked,
            SessionState.TurnDone => SessionState.Blocked,
            SessionState.Working  => SessionState.Working,
            _                     => SessionState.None,
        };

        if (!_icons.TryGetValue(key, out var icon))
        {
            var (r, g, b) = ColourFor(key);
            icon = IconFactory.RobotIcon(Color.FromArgb(r, g, b));
            _icons[key] = icon;
        }

        return icon;
    }

    private static (byte r, byte g, byte b) ColourFor(SessionState state) => state switch
    {
        SessionState.Blocked  => (255, 0,   0),    // red (flashing)
        SessionState.TurnDone => (255, 0,   0),    // red (flashing)
        SessionState.Working  => (255, 128, 0),    // amber
        _                     => (0,   255, 0),    // green
    };

    private static string LabelFor(SessionState state) => state switch
    {
        SessionState.Blocked  => "blocked",
        SessionState.TurnDone => "turn-done",
        SessionState.Working  => "working",
        _                     => "resting",
    };

    private void OnReset(object? sender, EventArgs e)
    {
        // Manual escape hatch: best-effort delete every file in the signal
        // directory, then recompute. No confirmation dialog. Files that cannot be
        // deleted (locked, vanished, mid-write temp files) are skipped silently —
        // consistent with the tray's swallow-and-continue error handling — so a
        // single bad file never aborts the pass or tears down the tray process.
        try
        {
            foreach (var path in Directory.GetFiles(_signalDir))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Locked / vanished / unreadable: skip and continue.
                }
            }
        }
        catch
        {
            // Directory enumeration itself failed (e.g. dir vanished): nothing to
            // delete. Fall through to Recompute, which tolerates a missing dir.
        }

        // Repaint from whatever remains. With the directory emptied this aggregates
        // to None -> green. Recompute already swallows its own exceptions.
        Recompute();
    }

    private void OnQuit(object? sender, EventArgs e)
    {
        // Best-effort reset to green; ignore the result.
        _flashTimer.Stop();
        var (r, g, b) = ColourFor(SessionState.None);
        G213Keyboard.TryPaint(r, g, b);

        _notifyIcon.Visible = false;
        _watcher.EnableRaisingEvents = false;

        ExitThread(); // teardown happens in Dispose(bool)
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resyncTimer.Dispose();
            _debounceTimer.Dispose();
            _flashTimer.Dispose();
            _watcher.Dispose();
            _notifyIcon.Dispose();

            foreach (var icon in _icons.Values)
                icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
