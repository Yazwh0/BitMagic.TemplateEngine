namespace BitMagic.TemplateEngine.Objects;

public interface ITemplateRunner
{
    Task Execute();
}

public interface ISourceResult
{
    string Code { get; }
    ISourceResultMap[] Map { get; }
}

public interface ISourceResultMap
{
    int Line { get; }
    string SourceFilename { get; }
}

public sealed record class SourceResult(string Code, ISourceResultMap[] Map) : ISourceResult;
public sealed record class SourceResultMap(int Line, string SourceFilename) : ISourceResultMap;
