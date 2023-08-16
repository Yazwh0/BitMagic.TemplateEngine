using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BitMagic.TemplateEngine;
using BitMagic.TemplateEngine.Objects;
using BitMagic.Common;

namespace BigMagic.TemplateEngine.Compiler;

public static class MacroAssembler
{
    public static async Task<ProcessResult> ProcessFile(this ITemplateEngine engine, string content, string contentAssemblyName, string filename)
    {
        var newContent = PreProcessFile(engine, content, filename);
        return await CompileFile(newContent, contentAssemblyName, engine);
    }

    /// <summary>
    /// Takes a source file and creates c# file that can be compiled
    /// </summary>
    /// <param name="contents">Input file contents</param>
    /// <returns>File that can be compiled</returns>
    /// <exception cref="ArgumentNullException"></exception>
    private static PreProcessResult PreProcessFile(ITemplateEngine engine, string contents, string filename)
    {
        if (contents == null)
            throw new ArgumentNullException(nameof(contents));

        var lines = contents.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var output = new StringBuilder();
        var userHeader = new StringBuilder();
        List<string> references = new();
        List<string> assemblyFilenames = new();
        List<TemplateMap> map = new();
        
        var startLine = 5 + 6 + engine.Namespaces.Count();
        output.AppendLine("using System;");
        output.AppendLine("using System.Linq;");
        output.AppendLine("using System.Collections;");
        output.AppendLine("using System.Collections.Generic;");
        output.AppendLine("using System.Threading.Tasks;");

        foreach (var ns in engine.Namespaces)
        {
            output.AppendLine($"using {ns};");
        }

        //output.AppendLine($"// PreProcessor Result of {_project.Source.Filename}");
        output.AppendLine("namespace BitMagic.App");
        output.AppendLine("{");
        output.AppendLine("public class Template : BitMagic.TemplateEngine.Objects.ITemplateRunner");
        output.AppendLine("{");
        output.AppendLine("\tpublic async Task Execute()");
        output.AppendLine("\t{");

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // emtpy line
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                output.AppendLine(line);
                continue;
            }

            if (trimmed.StartsWith("using"))
            {
                userHeader.AppendLine(trimmed);
                output.AppendLine(line);
                continue;
            }

            if (trimmed.StartsWith("reference"))
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var name = parts[1];

                if (name.EndsWith(';'))
                    name = name.Substring(0, name.Length - 1);

                references.Add(name);
                output.AppendLine(line);
                continue;
            }

            if (trimmed.StartsWith("assembly"))
            {
                output.AppendLine(line);
                var name = trimmed.Substring("assembly ".Length);

                var idx = name.IndexOf(';');

                if (idx >= 0)
                    name = name.Substring(0, idx);

                name = name.Trim();

                if (name == "\"\"" || string.IsNullOrWhiteSpace(name))
                    continue;

                if (name.StartsWith('"') && name.EndsWith('"'))
                    name = name.Substring(1, name.Length - 2);

                assemblyFilenames.Add(name);
                continue;
            }

            output.AppendLine(line);
        }

        output.AppendLine("\t}");
        output.AppendLine("}");
        output.AppendLine("}");

        var processResult = engine.Process(userHeader.ToString() + output.ToString(), startLine);

        var cnt = 1;
        for(var i = 0; i < processResult.Map.Count; i ++)
        {
            if (processResult.Map[i] > 0)
                map.Add(new TemplateMap(cnt, processResult.Map[i]));

            cnt++;
        }

        return new PreProcessResult(references, assemblyFilenames, processResult.Code, map, filename);
    }

    private sealed record class PreProcessResult(List<string> References, List<string> AssemblyFilenames, string Content, IList<TemplateMap> CodeMap, string Filename);

    // used to map the generated code to the original file, eg for error reporting.
    private record struct TemplateMap(int GeneratedLine, int OriginalLine);

    private static async Task<ProcessResult> CompileFile(PreProcessResult content, string contentAssemblyName, ITemplateEngine engine)
    {
        var toProcess = content.Content;

        if (toProcess == null)
            throw new ArgumentNullException(nameof(toProcess));

        var syntaxTree = CSharpSyntaxTree.ParseText(toProcess);

        var assemblies = new List<Assembly>();

        assemblies.AddRange(new[] {
                typeof(object).Assembly,
                Assembly.Load(new AssemblyName("Microsoft.CSharp")),
                Assembly.Load(new AssemblyName("System.Runtime")),
                typeof(System.Collections.IList).Assembly,
                typeof(System.Collections.Generic.IEnumerable<>).Assembly,
                Assembly.Load(new AssemblyName("System.Linq")),
                Assembly.Load(new AssemblyName("System.Linq.Expressions")),
                Assembly.Load(new AssemblyName("netstandard")),
                typeof(Template).Assembly
                //,
        });

        assemblies.AddRange(engine.Assemblies);

        foreach (var assemblyFilename in content.AssemblyFilenames)
        {
            var assemblyInclude = Assembly.LoadFrom(assemblyFilename);
            Console.WriteLine($"Adding File Assembly: {assemblyInclude.FullName}");

            assemblies.Add(assemblyInclude);
        }

        foreach (var include in content.References)
        {
            var assemblyInclude = Assembly.Load(include);
            Console.WriteLine($"Adding Referenced Assembly: {include}");

            assemblies.Add(assemblyInclude);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            contentAssemblyName,
            new[] { syntaxTree },
            assemblies.Select(ass => MetadataReference.CreateFromFile(ass.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        MemoryStream memoryStream = new MemoryStream();

        EmitResult emitResult = compilation.Emit(memoryStream);

        if (!emitResult.Success)
        {
            var toThrow = new TemplateCompilationException
            {
                GeneratedCode = toProcess,
                CSharpErrors = emitResult.Diagnostics.Where(i => i.Severity == DiagnosticSeverity.Error).ToList(),
                Filename = content.Filename
            };

            foreach (var error in toThrow.CSharpErrors)
            {
                var location = error.Location.GetLineSpan();
                var lineNumber = -1;

                for(var i = 0; i < content.CodeMap.Count; i++)
                {
                    if (content.CodeMap[i].GeneratedLine == location.StartLinePosition.Line + 1)
                    {
                        lineNumber = content.CodeMap[i].OriginalLine;
                        break;
                    }
                }

                toThrow.Errors.Add(new CompilationError()
                {
                    ErrorText = error.GetMessage(),
                    LineNumber = lineNumber
                });
            }

            throw toThrow;
        }

        memoryStream.Position = 0;

        Template.StartProject();

        var assembly = Assembly.Load(memoryStream.ToArray());

        var runner = Activator.CreateInstance(assembly.GetType($"BitMagic.App.Template") ?? throw new Exception("BitMagic.App.Template not in compiled dll.")) as ITemplateRunner;

        if (runner == null)
            throw new Exception("Template is not a ITemplateRunner");

        await runner.Execute();

        return new ProcessResult(engine.Beautify(Template.GenerateCode()), memoryStream.ToArray());
    }

    public class ProcessResult : ISourceFile
    {
        public ISourceResult Source { get; }
        public byte[] CompiledData { get; }

        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public int? ReferenceId { get; set; } = null;
        public SourceFileOrigin Origin => SourceFileOrigin.Intermediary;
        public bool Volatile => false;
        public Action Generate => () => { };
        public bool ActualFile => false;
        public ISourceFile? Parent { get; set; }

        public ProcessResult(ISourceResult source, byte[] compiledData)
        {
            Source = source;
            CompiledData = compiledData;
        }

        public string GetContent()
        {
            return Source.Code;
        }
    }

    public record class SourceResult(string Code, int[] Map) : ISourceResult;
}
