namespace BitMagic.TemplateEngine.Objects;

/// <summary>
/// A file that starts with 'file &lt;kind&gt;', handed to the view's Process(ViewFile) method.
/// </summary>
public sealed class ViewFile(string owner, string? arguments, Action body)
{
    private IReadOnlyList<CapturedItem>? _captured;

    /// <summary>
    /// The file's source path.
    /// </summary>
    public string Owner { get; } = owner;

    /// <summary>
    /// What follows 'file &lt;kind&gt;' on the first line, or null.
    /// </summary>
    public string? Arguments { get; } = arguments;

    /// <summary>
    /// Runs the file and returns what it writes: its lines, the keywords the view claims, and
    /// any anchors placed in it. The file runs once; later calls return the same result.
    /// </summary>
    public IReadOnlyList<CapturedItem> Capture() => _captured ??= Template.Capture(body);
}
