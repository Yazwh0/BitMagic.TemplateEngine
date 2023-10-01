namespace BitMagic.TemplateEngine.Compiler;

public record TemplateOptions
{
    public string BinFolder { get; set; } = "bin";
    public bool Rebuild { get; set; }
}
