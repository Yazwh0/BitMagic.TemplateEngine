using CommandLine;

namespace BitMagic.TemplateEngine.Runner;

internal class Options
{
    [Option('l', HelpText = "List of assemblies to load.")]
    public IEnumerable<string> Assemblies { get; set; } = Array.Empty<string>();

    [Option('b', HelpText = "Pase path.")]
    public string BasePath { get; set; } = "";

    [Option('n', HelpText = "Namespace.")]
    public string Namespace { get; set; } = "";

    [Option('c', HelpText = "Classname.")]
    public string Classname { get; set; } = "";

    [Option('s', HelpText = "Source Path")]
    public string SourcePath { get; set; } = "";
}
