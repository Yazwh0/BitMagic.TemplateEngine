namespace BitMagic.TemplateEngine.Objects;

public interface ITemplateRunner
{
    Task Execute();
    void Initialise();
}

public abstract class TemplateRunner : ITemplateRunner
{
    protected abstract Task Execute();
    protected abstract void Initialise();

    public async Task<ISourceResult> Run()
    {
        Template.StartProject();
        this.Initialise();
        await this.Execute();
        return Template.GenerateCode();
    }

    Task ITemplateRunner.Execute() => Execute();

    void ITemplateRunner.Initialise() => Initialise();
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
