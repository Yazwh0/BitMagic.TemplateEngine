using System.Text;

namespace BitMagic.TemplateEngine.Objects;

public static class Template
{
    internal static readonly StringBuilder _output = new();
    internal static readonly List<ISourceResultMap> _map = new();
    internal static int _linenumber;
    internal static string _sourceFile = "";

    public static void SetSourceMap(string sourceFile, int linenumber)
    {
        _linenumber = linenumber;
        _sourceFile = sourceFile;
    }

    public static void WriteLiteral(string literal)
    {
        _output.AppendLine(literal);
        _map.Add(new SourceResultMap(_linenumber, _sourceFile));
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