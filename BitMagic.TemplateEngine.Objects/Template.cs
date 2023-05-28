using System.Text;

namespace BitMagic.TemplateEngine.Objects;

public static class Template
{
    internal static readonly StringBuilder _output = new();
    internal static readonly List<int> _map = new();
    internal static int _linenumber;

    public static void SetLineNumber(int linenumber)
    {
        _linenumber = linenumber;
    }

    public static void WriteLiteral(string literal)
    {
        _output.AppendLine(literal);
        _map.Add(_linenumber);
    }

    public static void StartProject()
    {
        _output.Clear();
        _map.Clear();
    }

    public static ISourceResult GenerateCode() => new SourceResult(_output.ToString(), _map.ToArray());
    private sealed record class SourceResult(string Code, int[] Map) : ISourceResult;
}