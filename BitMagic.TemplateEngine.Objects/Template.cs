using System.Text;

namespace BitMagic.TemplateEngine.Objects;

public static class Template
{
    internal static readonly StringBuilder _output = new();
    internal static readonly List<ISourceResultMap> _map = new();

    public static void WriteLiteral(string literal, int lineNumber, string sourceFile)
    {
        _output.AppendLine(literal);
        _map.Add(new SourceResultMap(string.IsNullOrWhiteSpace(sourceFile) ? -1 : lineNumber, sourceFile));
    }

    public static void StartProject()
    {
        _output.Clear();
        _map.Clear();
    }

    public static ISourceResult GenerateCode() => new SourceResult(_output.ToString(), _map.ToArray());
    private sealed record class SourceResult(string Code, ISourceResultMap[] Map) : ISourceResult;
    private sealed record class SourceResultMap(int Line, string SourceFilename) : ISourceResultMap;
}