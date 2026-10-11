namespace BitMagic.TemplateEngine.Objects;

/// <summary>
/// A file that starts with 'file &lt;kind&gt;', handed to the view's Process(ViewFile) method.
/// </summary>
public sealed class ViewFile
{
    private readonly Action _body;
    private IReadOnlyList<CapturedItem>? _captured;

    public ViewFile(string owner, string? arguments, Action body)
    {
        Owner = owner;
        Arguments = arguments;
        _body = body;

        // the file keeps its place among the libraries, even if the view runs it later
        Section.NoteOwner(owner);
    }

    /// <summary>
    /// The file's source path.
    /// </summary>
    public string Owner { get; }

    /// <summary>
    /// What follows 'file &lt;kind&gt;' on the first line, or null.
    /// </summary>
    public string? Arguments { get; }

    /// <summary>
    /// Runs the file and returns what it writes: its lines, the keywords the view claims, and
    /// any anchors placed in it. The file runs once; later calls return the same result.
    /// </summary>
    public IReadOnlyList<CapturedItem> Capture() => _captured ??= Template.Capture(_body);
}
