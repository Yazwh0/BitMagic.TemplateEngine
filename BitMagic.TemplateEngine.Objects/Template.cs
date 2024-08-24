using System.Text;

namespace BitMagic.TemplateEngine.Objects;

public static class Template
{
    internal static readonly StringBuilder _output = new();
    internal static readonly List<ISourceResultMap> _map = new();

    public static void WriteLiteral(string literal, int lineNumber, string sourceFile)
    {
        if (literal.IndexOf('\n') != -1)    // if the literal is multiple lines, then dont map as we can't
        {
            var lines = literal.Split('\n');

            foreach (var l in lines)
            {
                _output.AppendLine(l);
                _map.Add(new SourceResultMap(0, ""));
                //_map.Add(new SourceResultMap(string.IsNullOrWhiteSpace(sourceFile) ? -1 : lineNumber, sourceFile));
            }

            return;
        }
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