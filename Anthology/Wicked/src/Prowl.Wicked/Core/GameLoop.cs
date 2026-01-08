namespace Prowl.Wicked.Core;

/// <summary>
/// Manages the game loop timing and update cycles.
/// Call Tick() once per frame with the delta time.
/// </summary>
public static class GameLoop
{
    private static double _fixedTimeAccumulator;
    private static readonly List<Action> _pendingActions = new();
    private static readonly List<Action> _actionsToProcess = new();

    /// <summary>
    /// Time in seconds since the last frame.
    /// </summary>
    public static float DeltaTime { get; private set; }

    /// <summary>
    /// Fixed time step in seconds (default: 1/60 = ~16.67ms).
    /// </summary>
    public static float FixedDeltaTime { get; set; } = 1f / 60f;

    /// <summary>
    /// Total time in seconds since the game started.
    /// </summary>
    public static double Time { get; private set; }

    /// <summary>
    /// Total number of frames since the game started.
    /// </summary>
    public static ulong FrameCount { get; private set; }

    /// <summary>
    /// Total number of fixed updates since the game started.
    /// </summary>
    public static ulong FixedFrameCount { get; private set; }

    /// <summary>
    /// Maximum number of fixed updates per frame to prevent spiral of death.
    /// </summary>
    public static int MaxFixedUpdatesPerFrame { get; set; } = 5;

    /// <summary>
    /// Event raised before each Update.
    /// </summary>
    public static event Action? OnPreUpdate;

    /// <summary>
    /// Event raised after each Update.
    /// </summary>
    public static event Action? OnPostUpdate;

    /// <summary>
    /// Event raised before each FixedUpdate.
    /// </summary>
    public static event Action? OnPreFixedUpdate;

    /// <summary>
    /// Event raised after each FixedUpdate.
    /// </summary>
    public static event Action? OnPostFixedUpdate;

    /// <summary>
    /// Event raised before each LateUpdate.
    /// </summary>
    public static event Action? OnPreLateUpdate;

    /// <summary>
    /// Event raised after each LateUpdate.
    /// </summary>
    public static event Action? OnPostLateUpdate;

    /// <summary>
    /// Main update method. Call this once per frame with the elapsed time.
    /// Handles FixedUpdate, Update, LateUpdate, and network ticking.
    /// </summary>
    /// <param name="deltaTime">Time in seconds since the last frame.</param>
    public static void Tick(float deltaTime)
    {
        DeltaTime = deltaTime;
        Time += deltaTime;
        FrameCount++;

        // Process pending actions
        ProcessPendingActions();

        // Fixed update accumulator
        _fixedTimeAccumulator += deltaTime;
        int fixedUpdateCount = 0;

        while (_fixedTimeAccumulator >= FixedDeltaTime && fixedUpdateCount < MaxFixedUpdatesPerFrame)
        {
            OnPreFixedUpdate?.Invoke();
            World.Active?.FixedUpdate();
            OnPostFixedUpdate?.Invoke();

            _fixedTimeAccumulator -= FixedDeltaTime;
            FixedFrameCount++;
            fixedUpdateCount++;
        }

        // Cap accumulator to prevent spiral of death
        if (_fixedTimeAccumulator > FixedDeltaTime * MaxFixedUpdatesPerFrame)
        {
            _fixedTimeAccumulator = FixedDeltaTime * MaxFixedUpdatesPerFrame;
        }

        // Regular update
        OnPreUpdate?.Invoke();
        World.Active?.Update();
        OnPostUpdate?.Invoke();

        // Late update
        OnPreLateUpdate?.Invoke();
        World.Active?.LateUpdate();
        OnPostLateUpdate?.Invoke();

        // Network tick is handled by NetworkManager subscribing to OnPostUpdate
    }

    /// <summary>
    /// Resets the game loop state.
    /// </summary>
    public static void Reset()
    {
        DeltaTime = 0;
        Time = 0;
        FrameCount = 0;
        FixedFrameCount = 0;
        _fixedTimeAccumulator = 0;

        lock (_pendingActions)
        {
            _pendingActions.Clear();
        }
    }

    /// <summary>
    /// Schedules an action to be executed on the next update.
    /// Thread-safe.
    /// </summary>
    public static void ScheduleAction(Action action)
    {
        lock (_pendingActions)
        {
            _pendingActions.Add(action);
        }
    }

    private static void ProcessPendingActions()
    {
        lock (_pendingActions)
        {
            if (_pendingActions.Count == 0)
                return;

            _actionsToProcess.AddRange(_pendingActions);
            _pendingActions.Clear();
        }

        foreach (var action in _actionsToProcess)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error executing scheduled action: {ex}");
            }
        }

        _actionsToProcess.Clear();
    }
}
