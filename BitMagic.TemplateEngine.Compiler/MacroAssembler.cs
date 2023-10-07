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
using BitMagic.TemplateEngine.Compiler;
using System.Text.RegularExpressions;
using BitMagic.Compiler;

namespace BigMagic.TemplateEngine.Compiler;

public static class MacroAssembler
{
    public static async Task<ProcessResult> ProcessFile(this ITemplateEngine engine, ISourceFile source, string filename, TemplateOptions options, IEmulatorLogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.BinFolder))
            options.BinFolder = "Bin";

        if (!Directory.Exists(options.BinFolder))
        {
            Directory.CreateDirectory(options.BinFolder);
            logger.LogLine($"  Creating output folder '{Path.GetFullPath(options.BinFolder)}'");
        }

        return (await ProcessFile(engine, source, filename, options, logger, new GlobalBuildState(), "  ", false)).Result;
    }

    private static async Task<(ProcessResult Result, bool RequireBuild)> ProcessFile(this ITemplateEngine engine, ISourceFile source, string filename, TemplateOptions options, 
        IEmulatorLogger logger, GlobalBuildState buildState, string indent, bool isLibrary)
    {
        var (assemblyData, @namespace, classname, requireBuild) = await GetAssembly(engine, source, filename, options, logger, buildState, indent, isLibrary);
        return (await CompileFile(assemblyData, engine, buildState, @namespace, classname, isLibrary), requireBuild);
    }

    private static async Task<(byte[] AssembleyData, string Namespace, string Classname, bool RequireBuild)> GetAssembly(this ITemplateEngine engine, ISourceFile source, 
        string filename, TemplateOptions options, IEmulatorLogger logger, GlobalBuildState buildState, string indent, bool isLibrary)
    {
        var lines = source.GetContent().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.TrimEntries);

        var (@namespace, classname, assemblyName) = GetAssemblyName(source, lines);
        var (requireBuild, binaryFilename) = await RequiresBuild(engine, source, lines, assemblyName, options, logger, buildState, indent);

        byte[] assemblyData;
        if (requireBuild)
        {
            logger.LogLine($"{indent}Building assembly '{binaryFilename}'");
            var templateDefinition = CreateTemplate(engine, lines, filename, buildState, @namespace, classname, isLibrary);
            assemblyData = CreateAssembly(templateDefinition, assemblyName, engine, logger, buildState, indent);

            if (Directory.Exists(options.BinFolder))
            {
                await File.WriteAllBytesAsync(binaryFilename, assemblyData);
                Assembly.LoadFrom(binaryFilename); // we need to _read from disk_ so the activator works later!!
            }
        }
        else
        {
            logger.LogLine($"{indent}Loading assembly '{binaryFilename}'");

            Assembly.LoadFrom(binaryFilename); // we need to _read from disk_ so the activator works later!!

            assemblyData = await File.ReadAllBytesAsync(binaryFilename);
        }

        return (assemblyData, @namespace, classname, requireBuild);
    }

    private static (string Namespace, string Classname, string AssemblyName) GetAssemblyName(ISourceFile source, string[] lines)
    {
        var line = lines.FirstOrDefault(i => i.StartsWith("library"));

        var @namespace = "BitMagic.App." + Path.GetFileNameWithoutExtension(source.Name);
        var className = "Template";

        if (line != null)
        {
            if (line.EndsWith(";"))
                line = line[..^1];

            var parts = line.Split(' ');
            if (parts.Length > 1)
            {

                var trimmed = parts[1].Trim();
                if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
                    trimmed = trimmed[1..-1].Trim();

                var idx = trimmed.LastIndexOf('.');

                if (idx != -1)
                {
                    @namespace = trimmed[..idx];
                    className = trimmed[(idx + 1)..];
                }
                else
                {
                    className = trimmed;
                }
            }
        }

        return (@namespace, className, @namespace + ".dll");
    }

    // todo: this needs to work down the tree and build if necessary
    private static async Task<(bool Rebuild, string BinaryFilename)> RequiresBuild(ITemplateEngine engine, ISourceFile source, string[] lines, string contentAssemblyName, TemplateOptions options,
        IEmulatorLogger logger, GlobalBuildState buildState, string indent)
    {
        bool newBuild = false;
        // check if there are any imports which could need building
        foreach (var import in lines.Where(i => i.StartsWith("import")))
        {
            var regexResult = _importRegex.Matches(import);

            if (regexResult.Count != 1)
                continue;

            var match = regexResult[0];

            if (!match.Success)
                continue;

            if (!match.Groups.TryGetValue("importName", out Group importName))
                continue;

            if (!match.Groups.TryGetValue("filename", out Group importFilename))
                continue;

            if (string.IsNullOrWhiteSpace(importName.Value))
                continue;

            if (string.IsNullOrWhiteSpace(importFilename.Value))
                continue;

            if (!buildState.FilenameToClassname.ContainsKey(importFilename.Value))
            {
                // build
                // create and register source file
                // call GetAssembly

                var sourceFile = new ProjectTextFile(importFilename.Value);
                await sourceFile.Load();

                var result = await ProcessFile(engine, sourceFile, importFilename.Value, options, logger, buildState, "  " + indent, true);

                buildState.FilenameToClassname.Add(importFilename.Value, $"{result.Result.Namespace}.{result.Result.Classname}");
                buildState.References.Add(result.Result);

                newBuild |= result.RequireBuild;
            }
        }

        var binaryFilename = Path.Combine(options.BinFolder, contentAssemblyName);

        if (newBuild)
            return (true, binaryFilename);

        if (!source.ActualFile)
            return (true, binaryFilename);

        if (!File.Exists(binaryFilename))
            return (true, binaryFilename);

        return (File.GetLastWriteTimeUtc(binaryFilename) < File.GetLastWriteTimeUtc(source.Path), binaryFilename);
    }

    private static Regex _importRegex = new Regex(@"^import (?<importName>[\w]+)\s*=\s*\""(?<filename>[\/\\\w\-.: ]+)\""\s*\;", RegexOptions.Compiled);

    /// <summary>
    /// Takes a source file and creates c# file that can be compiled
    /// </summary>
    /// <param name="contents">Input file contents</param>
    /// <returns>File that can be compiled</returns>
    /// <exception cref="ArgumentNullException"></exception>
    private static PreProcessResult CreateTemplate(ITemplateEngine engine, string[] lines, string filename, GlobalBuildState buildState,
        string @namespace = "BitMagic.App", string className = "Template", bool isLibrary = false)
    {
        if (lines == null)
            throw new ArgumentNullException(nameof(lines));

        var output = new StringBuilder();
        var userHeader = new StringBuilder();
        var initMethod = new StringBuilder();
        List<string> references = new();
        List<string> assemblyFilenames = new();
        List<TemplateMap> map = new();

        var startLine = isLibrary ? 5 + 3 + engine.Namespaces.Count() : 5 + 6 + engine.Namespaces.Count();

        output.AppendLine("using System;");
        output.AppendLine("using System.Linq;");
        output.AppendLine("using System.Collections;");
        output.AppendLine("using System.Collections.Generic;");
        output.AppendLine("using System.Threading.Tasks;");
        output.AppendLine("using BitMagic.TemplateEngine.Objects;");

        foreach (var ns in engine.Namespaces)
        {
            output.AppendLine($"using {ns};");
        }

        //output.AppendLine($"// PreProcessor Result of {_project.Source.Filename}");
        output.AppendLine($"namespace {@namespace}");
        output.AppendLine("{");

        if (isLibrary)
        {
            output.AppendLine($"public class {className} : BitMagic.TemplateEngine.Objects.LibraryBase");
            output.AppendLine("{");
        }
        else
        {
            output.AppendLine($"public class {className} : BitMagic.TemplateEngine.Objects.ITemplateRunner");
            output.AppendLine("{");
            output.AppendLine("\tasync Task ITemplateRunner.Execute()");
            output.AppendLine("\t{");
        }

        foreach (var line in lines)
        {
            // emtpy line
            if (string.IsNullOrWhiteSpace(line))
            {
                output.AppendLine(line);
                continue;
            }

            if (line.StartsWith("library"))
                continue;

            if (line.StartsWith("import"))
            {
                var regexResult = _importRegex.Matches(line);

                if (regexResult.Count != 1)
                    throw new ImportParseException($"Incorrect syntax in '{line}' (1)");

                var match = regexResult[0];

                if (!match.Success)
                    throw new ImportParseException($"Incorrect syntax in '{line}' (2)");

                if (!match.Groups.TryGetValue("importName", out Group importName))
                    throw new ImportParseException($"Incorrect syntax in '{line}' (3)");

                if (!match.Groups.TryGetValue("filename", out Group importFilename))
                    throw new ImportParseException($"Incorrect syntax in '{line}' (4)");

                if (string.IsNullOrWhiteSpace(importName.Value))
                    throw new ImportParseException($"Incorrect syntax in '{line}' blank importname");

                if (string.IsNullOrWhiteSpace(importFilename.Value))
                    throw new ImportParseException($"Incorrect syntax in '{line}' blank filename");

                if (!buildState.FilenameToClassname.ContainsKey(importFilename.Value))
                    throw new ImportParseException($"File '{importFilename.Value}' does not appear to have been built.");

                userHeader.AppendLine($"using {importName.Value} = {buildState.FilenameToClassname[importFilename.Value]};");
                initMethod.AppendLine($"(new {importName.Value}()).Initialise();");
                //startLine++;
                continue;
            }

            if (line.StartsWith("using"))
            {
                userHeader.AppendLine(line);
                output.AppendLine(line);
                continue;
            }

            if (line.StartsWith("reference"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var name = parts[1];

                if (name.EndsWith(';'))
                    name = name.Substring(0, name.Length - 1);

                references.Add(name);
                //output.AppendLine(line); // do we need this..??
                continue;
            }

            if (line.StartsWith("assembly"))
            {
                //output.AppendLine(line); // do we need this..??
                var name = line.Substring("assembly ".Length);

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

        if (isLibrary)
        {
            output.AppendLine("\t}"); // closes class
        }
        else
        {
            output.AppendLine("\t}");

            output.AppendLine("\tvoid ITemplateRunner.Initialise()");
            output.AppendLine("\t{");
            output.AppendLine(initMethod.ToString());
            output.AppendLine("\t}");

            output.AppendLine("}"); // closes class
        }

        output.AppendLine("}"); // closes namespace

        var processResult = engine.Process(userHeader.ToString() + output.ToString(), startLine, filename, isLibrary);

        var cnt = 1;
        for (var i = 0; i < processResult.Map.Count; i++)
        {
            if (processResult.Map[i] > 0)
                map.Add(new TemplateMap(cnt, processResult.Map[i]));

            cnt++;
        }

        return new PreProcessResult(references, assemblyFilenames, processResult.Code, map, filename, @namespace, className);
    }

    private sealed record class PreProcessResult(
            List<string> References,
            List<string> AssemblyFilenames,
            string Content,
            IList<TemplateMap> CodeMap,
            string Filename,
            string Namespace,
            string Classname
        );

    private sealed class GlobalBuildState
    {
        //public Dictionary<string, byte[]> Assemblies { get; } = new();

        // Eg 'vera.bmasm' -> 'BitMagic.Vera.Template'
        public Dictionary<string, string> FilenameToClassname { get; } = new();
        public List<ProcessResult> References { get; } = new();
        public IEnumerable<ProcessResult> AllReferences => References.Union(References.SelectMany(i => i.References));
    }

    // used to map the generated code to the original file, eg for error reporting.
    private record struct TemplateMap(int GeneratedLine, int OriginalLine);

    private static byte[] CreateAssembly(PreProcessResult content, string contentAssemblyName, ITemplateEngine engine, IEmulatorLogger logger, GlobalBuildState buildState, string indent)
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
            logger.LogLine($"{indent}Adding File Assembly: {assemblyInclude.FullName}");

            assemblies.Add(assemblyInclude);
        }

        foreach (var include in content.References)
        {
            var assemblyInclude = Assembly.Load(include);
            logger.LogLine($"{indent}Adding Referenced Assembly: {include}");

            assemblies.Add(assemblyInclude);
        }

        var builtMetadata = buildState.AllReferences.Select(i => MetadataReference.CreateFromImage(i.CompiledData));

        CSharpCompilation compilation = CSharpCompilation.Create(
            contentAssemblyName,
            new[] { syntaxTree },
            assemblies.Select(i => MetadataReference.CreateFromFile(i.Location)).Union(builtMetadata),
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

                for (var i = 0; i < content.CodeMap.Count; i++)
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

        return memoryStream.ToArray();
    }

    private static async Task<ProcessResult> CompileFile(byte[] assemblyData, ITemplateEngine engine, GlobalBuildState buildState, string @namespace, string className, bool isLibrary)
    {
        if (!isLibrary)
        {
            Template.StartProject();

            var runner = AppDomain.CurrentDomain.CreateInstance($"{@namespace}.dll", $"{@namespace}.{className}").Unwrap() as ITemplateRunner;

            if (runner == null)
                throw new Exception($"{className} is not a ITemplateRunner");

            runner.Initialise();
            await runner.Execute();
        }

        return new ProcessResult(engine.Beautify(Template.GenerateCode()), assemblyData, buildState.References, @namespace, className);
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

        public ProcessResult[] References { get; }
        public string Classname { get; }
        public string Namespace { get; }

        public ProcessResult(ISourceResult source, byte[] compiledData, IEnumerable<ProcessResult> references, string @namespace, string classname)
        {
            Source = source;
            CompiledData = compiledData;
            References = references.ToArray();
            Classname = classname;
            Namespace = @namespace;
        }

        public string GetContent()
        {
            return Source.Code;
        }
    }

    //public record class SourceResult(string Code, int[] Map) : ISourceResult;
}
