using System.Collections.Generic;
using static BitMagic.TemplateEngine.Compiler.MacroAssembler;

namespace BitMagic.TemplateEngine.Compiler;

/// <summary>
/// Used to store which binaries should be included if the code isn't rebuilt.
/// </summary>
internal class DependantsFile
{
    public List<string> References { get; set; }
    public List<string> AssemblyFilenames { get; set; }

    /// <summary>
    /// The template engine that built the binary, see MacroAssembler.BuildIdentity. Blank for binaries built before it
    /// was recorded.
    /// </summary>
    public string BuiltBy { get; set; } = "";

    /// <summary>
    /// Full path of the source file the binary was built from, as another file with the same name builds to the same
    /// binary. Blank for binaries built before it was recorded.
    /// </summary>
    public string Source { get; set; } = "";

    public DependantsFile(PreProcessResult result, string source)
    {
        References = result.References;
        AssemblyFilenames = result.AssemblyFilenames;
        BuiltBy = BuildIdentity;
        Source = source;
    }

    public DependantsFile()
    {
        References = new List<string>();
        AssemblyFilenames = new List<string>();
    }
}
