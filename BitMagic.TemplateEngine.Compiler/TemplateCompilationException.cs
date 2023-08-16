using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BigMagic.TemplateEngine.Compiler;

public class TemplateCompilationException : Exception {
    public List<Diagnostic> CSharpErrors { get; set; } = new ();
    public List<CompilationError> Errors { get; set; } = new ();
    public string Filename { get; set; } = "";

    public string GeneratedCode { get; set; } = "";

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