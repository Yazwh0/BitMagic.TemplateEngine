namespace BitMagic.TemplateEngine.Objects;

/// <summary>
/// Hooks around the main file's code, for libraries that need to write output before or
/// after it.
/// </summary>
public static class Build
{
    private static readonly List<Action> _start = new();
    private static readonly List<Action> _end = new();

    /// <summary>
    /// Runs after every library's Initialise(), before the main file's code.
    /// </summary>
    public static void OnStart(Action action) => _start.Add(action);

    /// <summary>
    /// Runs after the main file's code, before the sections are written.
    /// </summary>
    public static void OnEnd(Action action) => _end.Add(action);

    internal static void Start() => Run(_start);

    internal static void End() => Run(_end);

    // a hook can add another, so don't use foreach
    private static void Run(List<Action> actions)
    {
        for (var i = 0; i < actions.Count; i++)
            actions[i]();
    }

    internal static void Reset()
    {
        _start.Clear();
        _end.Clear();
    }
}
