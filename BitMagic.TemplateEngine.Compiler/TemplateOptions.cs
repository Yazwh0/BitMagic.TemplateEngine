namespace BitMagic.TemplateEngine.Compiler;

public record TemplateOptions
{
    public string BasePath { get; set; } = "";
    public string BinFolder { get; set; } = "bin";
    public bool Rebuild { get; set; }
    public bool SaveGeneratedTemplate { get; set; } = false;
    public bool SavePreGeneratedTemplate { get; set; } = false;
}
