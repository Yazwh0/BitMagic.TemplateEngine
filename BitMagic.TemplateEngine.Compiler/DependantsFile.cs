using System.Collections.Generic;
using static BitMagic.TemplateEngine.Compiler.MacroAssembler;

namespace BitMagic.TemplateEngine.Compiler;

internal class DependantsFile
{
    public List<string> References { get; set; }
    public List<string> AssemblyFilenames { get; set; }

    public DependantsFile(PreProcessResult result)
    {
        References = result.References;
        AssemblyFilenames = result.AssemblyFilenames;
    }

    public DependantsFile()
    {
        References = new List<string>();
        AssemblyFilenames = new List<string>();
    }
}
