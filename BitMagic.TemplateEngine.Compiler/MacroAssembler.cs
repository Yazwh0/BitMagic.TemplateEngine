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
using BitMagic.TemplateEngine.Objects;
using BitMagic.Common;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Newtonsoft.Json;
using BitMagic.Compiler.Files;

namespace BitMagic.TemplateEngine.Compiler;

public static partial class MacroAssembler
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

        filename = filename.FixFilename();

        return (await ProcessFile(engine, source, filename, options, logger, new GlobalBuildState(), "  ", false)).Result;
    }

    private static async Task<(ProcessResult Result, bool RequireBuild)> ProcessFile(this ITemplateEngine engine, ISourceFile source, string filename, TemplateOptions options,
        IEmulatorLogger logger, GlobalBuildState buildState, string indent, bool isLibrary)
    {
        var (assemblyData, @namespace, classname, requireBuild) = await GetAssembly(engine, source, filename, options, logger, buildState, indent, isLibrary);
        return (await CompileFile(assemblyData, buildState, @namespace, classname, isLibrary), requireBuild);
    }

    private static async Task<(byte[] AssembleyData, string Namespace, string Classname, bool RequireBuild)> GetAssembly(this ITemplateEngine engine, ISourceFile source,
        string filename, TemplateOptions options, IEmulatorLogger logger, GlobalBuildState buildState, string indent, bool isLibrary)
    {
        var lines = source.Content;

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
                buildState.BinaryFilenames.Add(binaryFilename);
            }
        }
        else
        {
            logger.LogLine($"{indent}Loading assembly '{binaryFilename}'");

            assemblyData = await File.ReadAllBytesAsync(binaryFilename);
            buildState.BinaryFilenames.Add(binaryFilename);
        }

        return (assemblyData, @namespace, classname, requireBuild);
    }

    private static (string Namespace, string Classname, string AssemblyName) GetAssemblyName(ISourceFile source, IReadOnlyList<string> lines)
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

    private static async Task<(bool Rebuild, string BinaryFilename)> RequiresBuild(ITemplateEngine engine, ISourceFile source, IReadOnlyList<string> lines,
        string contentAssemblyName, TemplateOptions options, IEmulatorLogger logger, GlobalBuildState buildState, string indent)
    {
        bool newBuild = options.Rebuild;
        // check if there are any imports which could need building
        foreach (var import in lines.Where(i => i.Trim().StartsWith("import")))
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

            // build
            // create and register source file
            // call GetAssembly

            // search for the file
            string sourceFilename = "";

            // check if its an absolute path               
            if (File.Exists(importFilename.Value))
            {
                sourceFilename = importFilename.Value;
            }

            // check if its relative to the source file, if the source file is real
            var searched = "";
            if (sourceFilename == "" && source.ActualFile && !string.IsNullOrWhiteSpace(source.Path))
            {
                searched = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source.Path), importFilename.Value));
                if (File.Exists(searched))
                    sourceFilename = searched;
            }

            if (string.IsNullOrWhiteSpace(sourceFilename))
                throw new ImportNotFoundException(importFilename.Value, searched);

            sourceFilename = sourceFilename.FixFilename();

            if (!buildState.ImportToFilename.ContainsKey(importFilename.Value))
                buildState.ImportToFilename.Add(importFilename.Value, sourceFilename);

            if (!buildState.FilenameToClassname.ContainsKey(sourceFilename))
            {
                var sourceFile = new BitMagicProjectFile(sourceFilename);
                await sourceFile.Load();

                var result = await ProcessFile(engine, sourceFile, sourceFile.Path, options, logger, buildState, "  " + indent, true);
                var filename = (Path.GetFileNameWithoutExtension(sourceFile.Path) + ".generated.bmasm").FixFilename();

                result.Result.SetName(filename);
                result.Result.SetParentAndMap(sourceFile);
                buildState.FilenameToClassname.Add(sourceFilename, $"{result.Result.Namespace}.{result.Result.Classname}");
                buildState.References.Add(result.Result);

                buildState.SourceFiles.Add(sourceFile);
                buildState.SourceFiles.Add(result.Result);

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

    private static Regex _importRegex = new Regex(@"^\s*import (?<importName>[\w]+)\s*=\s*\""(?<filename>[\/\\\w\-.: ]+)\""\s*\;", RegexOptions.Compiled);

    /// <summary>
    /// Takes a source file and creates c# file that can be compiled
    /// </summary>
    /// <param name="contents">Input file contents</param>
    /// <returns>File that can be compiled</returns>
    /// <exception cref="ArgumentNullException"></exception>
    private static PreProcessResult CreateTemplate(ITemplateEngine engine, IReadOnlyList<string> lines, string filename, GlobalBuildState buildState,
        string @namespace = "BitMagic.App", string className = "Template", bool isLibrary = false)
    {
        if (lines == null)
            throw new ArgumentNullException(nameof(lines));

        var output = new List<string>();
        var userHeader = new List<string>();
        var initMethod = new List<string>();
        var libraries = new List<string>();
        List<string> references = new();
        List<string> assemblyFilenames = new();
        List<TemplateMap> map = new();

        output.Add("using System;");
        output.Add("using System.Linq;");
        output.Add("using System.Collections;");
        output.Add("using System.Collections.Generic;");
        output.Add("using System.Threading.Tasks;");
        output.Add("using BitMagic.TemplateEngine.Objects;");

        foreach (var ns in engine.Namespaces)
        {
            output.Add($"using {ns};");
        }

        output.Add($"namespace {@namespace}");
        output.Add("{");

        if (isLibrary)
        {
            output.Add($"public class {className} : BitMagic.TemplateEngine.Objects.LibraryBase");
            output.Add("{");
        }
        else
        {
            output.Add($"public class {className} : BitMagic.TemplateEngine.Objects.ITemplateRunner");
            output.Add("{");
            output.Add("\tasync Task ITemplateRunner.Execute()");
            output.Add("\t{");
        }

        var startLine = output.Count - 1; // zero based

        foreach (var originalLine in lines)
        {
            var line = originalLine.Trim();
            // emtpy line
            if (string.IsNullOrWhiteSpace(line))
            {
                output.Add("");
                continue;
            }

            if (line.StartsWith("library"))
            {
                output.Add("");
                continue;
            }

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

                if (!buildState.ImportToFilename.ContainsKey(importFilename.Value))
                    throw new ImportParseException($"File '{importFilename.Value}' does not appear to have been built.");

                var actualFile = buildState.ImportToFilename[importFilename.Value];
                var importClassName = buildState.FilenameToClassname[actualFile];
                userHeader.Add($"using {importName.Value} = {importClassName};");
                initMethod.Add($"{importName.Value}.Initialise();");
                libraries.Add($"private readonly {importClassName} {importName.Value} = new();");

                output.Add("");
                continue;
            }

            if (line.StartsWith("using"))
            {
                userHeader.Add(line);

                output.Add("");
                continue;
            }

            if (line.StartsWith("reference"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var name = parts[1];

                if (name.EndsWith(';'))
                    name = name.Substring(0, name.Length - 1);

                references.Add(name);

                output.Add("");
                continue;
            }

            if (line.StartsWith("assembly"))
            {
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

                output.Add("");
                continue;
            }

            output.Add(originalLine);
        }

        if (isLibrary)
        {
            output.Add("\t}"); // closes class
        }
        else
        {
            output.Add("\t}");

            output.AddRange(libraries);

            output.Add("\tvoid ITemplateRunner.Initialise()");
            output.Add("\t{");
            output.AddRange(initMethod);
            output.Add("\t}");

            output.Add("}"); // closes class
        }

        output.Add("}"); // closes namespace

        startLine += userHeader.Count;

        var allLines = userHeader.Concat(output);

#if DEBUG
        var allText = string.Join("\n", allLines);
#endif

        var processResult = engine.Process(allLines, startLine, filename, isLibrary);

        // this is suspect, might not work for libraries
        var cnt = 0; //  isLibrary ? 0 : 1; // this dosen't appear to make a difference
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

    internal sealed class GlobalBuildState
    {
        // Eg 'vera.bmasm' -> 'BitMagic.Vera.Template'
        public Dictionary<string, string> FilenameToClassname { get; } = new();
        public Dictionary<string, string> ImportToFilename { get; } = new();
        public List<ProcessResult> References { get; } = new();
        public IEnumerable<ProcessResult> AllReferences => References.Union(References.SelectMany(i => i.References));
        public List<string> BinaryFilenames { get; } = new();
        public List<SourceFileBase> SourceFiles { get; } = new();
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
                    if (content.CodeMap[i].GeneratedLine == location.StartLinePosition.Line)
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

    private static string _runnerLocation = null;

    private static async Task<string> LocateRunner()
    {
        if (_runnerLocation != null)
            return _runnerLocation;

        const string exeName = "BitMagic.TemplateEngine.Runner.exe";
        var path = Environment.GetEnvironmentVariable("BitMagic.TemplateEngine.Runner");

        if (path != null)
        {
            path = Path.Combine(path, exeName);

            if (File.Exists(path))
            {
                _runnerLocation = path;
                return path;
            }
        }

        var d = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        path = Path.Combine(d, exeName);

        if (File.Exists(path))
        {
            _runnerLocation = path;
            return path;
        }

        using var process = new Process();

        process.StartInfo.FileName = "dotnet";
        process.StartInfo.Arguments = "tool install --tool-path . BitMagic.TemplateRunner";
        process.StartInfo.WorkingDirectory = d;

        process.Start();
        await process.WaitForExitAsync();

        path = Path.Combine(d, exeName);

        if (File.Exists(path))
        {
            _runnerLocation = path;
            return path;
        }

        throw new Exception("Cannot find 'BitMagic.TemplateEngine.Runner.exe', consider manually installing and adding evironment variable 'BitMagic.TemplateEngine.Runner' to the path.");
    }

    private static async Task<ProcessResult> CompileFile(byte[] assemblyData, GlobalBuildState buildState, string @namespace, string className, bool isLibrary)
    {
        string code = "";

        if (isLibrary)
            return new ProcessResult(new SourceResult(code, Array.Empty<ISourceResultMap>()), assemblyData, buildState, @namespace, className);

        var exePath = await LocateRunner();
        var sb = new StringBuilder();

        sb.Append("-l ");

        foreach (var i in buildState.BinaryFilenames)
        {
            sb.Append($"\"{i}\" ");
        }

        sb.Append("-n ");
        sb.Append(@namespace);
        sb.Append(" -c ");
        sb.Append(@className);
        sb.Append(" -b \"");
        sb.Append(Directory.GetCurrentDirectory());
        sb.Append("\"");

        var arguments = sb.ToString();

        using var process = new Process();

        process.StartInfo.FileName = exePath;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
        process.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath); // Assembly.GetExecutingAssembly().Location);

        process.Start();

        var stdOut = process.StandardOutput;
        var stdErr = process.StandardError;

        var r = stdOut.ReadToEnd();
        var er = stdErr.ReadToEnd();

        if (!string.IsNullOrEmpty(er))
        {
            throw new Exception("Exception in the runner: \n" + er);
        }

        await process.WaitForExitAsync();

        var result = JsonConvert.DeserializeObject<SourceResult>(r, new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Auto });

        return new ProcessResult(result, assemblyData, buildState, @namespace, className);
    }
}
