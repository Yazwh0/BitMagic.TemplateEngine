using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BitMagic.TemplateEngine.Compiler;

public abstract class TemplateException : Exception
{
    public TemplateException(string message) : base(message)
    {
    }

    public TemplateException(string message, Exception? inner) : base(message, inner)
    {
    }
}

/// <summary>
/// The template engine couldn't run because of its environment or set up, eg the bin folder or .NET SDK is missing.
/// </summary>
public class TemplateBuildException(string message, Exception? inner = null) : TemplateException(message, inner)
{
}

/// <summary>
/// The user's template C# code threw while it was running.
/// </summary>
public class TemplateRuntimeException(string filename, Exception inner)
    : TemplateException($"Template '{Path.GetFileName(filename)}' threw {inner.GetType().Name}: {inner.Message}", inner)
{
    public string Filename { get; } = filename;
}

public class TemplateCompilationException : TemplateException
{
    public List<Diagnostic> CSharpErrors { get; set; } = new ();
    public List<CompilationError> Errors { get; set; } = new ();
    public string Filename { get; set; } = "";

    public string GeneratedCode { get; set; } = "";

    public TemplateCompilationException() : base ("C# Compiler Exception")
    {
    }

    public override string Message
    {
        get
        {
            string errors = string.Join("\n", this.Errors.Select(i => i.ErrorText));
            return "Unable to compile template: " + errors;
        }
    }
}

public sealed class CompilationError
{
    public string ErrorText { get; set; } = "";
    public int LineNumber { get; set; }
}

public class ImportParseException(string message) : TemplateException(message)
{
}

public class ImportNotFoundException(string filename, IEnumerable<string> searched, string path) : TemplateException($"Import file not found : '{filename}', searched {string.Join(", ", searched.Select(i => $"\"{i}\""))}, starting path '{path}'.")
{
}

public class IncludeParseException(string message) : TemplateException(message)
{
}

public class AssemblyFileNotFoundException(string message) : TemplateException(message)
{
}