using System;
using System.IO;
using System.Text;

namespace BitMagic.TemplateEngine.Compiler;

/// <summary>
/// Sends what's written to it to a logger, a line at a time. Used as Console.Out and Console.Error
/// while a template runs, so its console output goes to the build log, eg the debugger's console.
/// </summary>
internal sealed class LoggerTextWriter(Action<string> log, string indent, TextWriter original) : TextWriter
{
    // set while any of these writers is logging. A logger can write to the console itself, eg
    // X16D's ConsoleLogger, so that goes to the original writer rather than back to the logger.
    [ThreadStatic]
    private static bool _logging;

    private readonly StringBuilder _line = new();

    public override Encoding Encoding => original.Encoding;

    public override void Write(char value)
    {
        if (_logging)
        {
            original.Write(value);
            return;
        }

        if (value == '\r')
            return;

        if (value == '\n')
        {
            WriteLine();
            return;
        }

        _line.Append(value);
    }

    public override void Flush()
    {
        if (_logging)
        {
            original.Flush();
            return;
        }

        if (_line.Length != 0)
            WriteLine();
    }

    public override void WriteLine()
    {
        if (_logging)
        {
            original.WriteLine();
            return;
        }

        var line = indent + _line.ToString();
        _line.Clear();

        _logging = true;
        try
        {
            log(line);
        }
        finally
        {
            _logging = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Flush();

        base.Dispose(disposing);
    }
}
