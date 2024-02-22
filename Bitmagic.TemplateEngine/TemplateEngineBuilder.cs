using BitMagic.TemplateEngine.Objects;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BitMagic.TemplateEngine;

public interface ITemplateEngineBuilder
{
    public ITemplateEngineBuilder WithUnderlying(Regex search);
    public ITemplateEngineBuilder WithCSharpInline(Regex search, Regex substitue);
    public ITemplateEngine Build();
    public ITemplateEngineBuilder RequiresTidyup(string marker);
    public ITemplateEngineBuilder WithBeautifier(Func<ISourceResult, ISourceResult> beautify);
    public ITemplateEngineBuilder WithNamespace(string namespaceLine);
    public ITemplateEngineBuilder WithAssembly(Assembly assembly);
    public ITemplateEngineBuilder WithCSharpRawVariablePrefix(string variable);
}

public static class TemplateEngineBuilder
{
    public static ITemplateEngineBuilder As(string name)
    {
        return new TemplateEngineBuilderStep(name);
    }
}

public class TemplateEngineBuilderStep : ITemplateEngineBuilder
{
    internal List<Regex> _asmLines = new List<Regex>();
    internal List<(Regex Seach, Regex Subtituet)> _csharpLines = new List<(Regex Search, Regex Substitue)>();
    internal string _name;
    internal bool _requiresTidyup = false;
    internal string _tidyMarker = "";
    internal Func<ISourceResult, ISourceResult> _beautify = (x) => x;
    internal List<string> _namespaces = new();
    internal List<Assembly> _assemblies = new();
    internal List<string> _prefixes = new();

    internal TemplateEngineBuilderStep(string name)
    {
        _name = name;
    }

    public ITemplateEngineBuilder WithUnderlying(Regex search)
    {
        _asmLines.Add(search);
        return this;
    }

    public ITemplateEngineBuilder WithCSharpInline(Regex search, Regex substitue)
    {
        _csharpLines.Add((search, substitue));
        return this;
    }

    public ITemplateEngineBuilder RequiresTidyup(string marker)
    {
        _requiresTidyup = true;
        _tidyMarker = marker;
        return this;
    }

    public ITemplateEngineBuilder WithBeautifier(Func<ISourceResult, ISourceResult> beautify)
    {
        _beautify = beautify;
        return this;
    }

    public ITemplateEngineBuilder WithNamespace(string namespaceLine)
    {
        _namespaces.Add(namespaceLine);
        return this;
    }

    public ITemplateEngineBuilder WithAssembly(Assembly assembly)
    {
        _assemblies.Add(assembly);
        return this;
    }

    public ITemplateEngineBuilder WithCSharpRawVariablePrefix(string variablePrefix)
    {
        _prefixes.Add(variablePrefix);
        return this;
    }

    public ITemplateEngine Build()
    {
        return new TemplateEngine(_name, _asmLines, _csharpLines, _beautify, _namespaces, _assemblies, _prefixes, _requiresTidyup, _tidyMarker);
    }
}
