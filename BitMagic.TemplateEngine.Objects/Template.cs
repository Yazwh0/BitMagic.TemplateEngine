using System.Text;

namespace BitMagic.TemplateEngine.Objects;

public static class Template
{
    internal static readonly StringBuilder _output = new();

    public static void WriteLiteral(string literal)
    {
        _output.AppendLine(literal);
    }

    public static void StartProject()
    {
        _output.Clear();
    }

    public static string GenerateCode() => _output.ToString();
}