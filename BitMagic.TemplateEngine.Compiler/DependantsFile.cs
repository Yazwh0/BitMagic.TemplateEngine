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

    public DependantsFile(PreProcessResult result)
    {
        References = result.References;
        AssemblyFilenames = result.AssemblyFilenames;
        BuiltBy = BuildIdentity;
    }

    public DependantsFile()
    {
        References = new List<string>();
        AssemblyFilenames = new List<string>();
    }
}
