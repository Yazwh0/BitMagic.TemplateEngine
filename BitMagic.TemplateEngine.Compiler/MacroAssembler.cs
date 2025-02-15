﻿using Microsoft.CodeAnalysis;
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
            options.BinFolder = "bin";

        if (!Directory.Exists(options.BinFolder))
        {
            Directory.CreateDirectory(options.BinFolder);
            logger.LogLine($"  Creating output folder '{Path.GetFullPath(options.BinFolder)}'");
        }

        filename = filename.FixFilename();

        return (await ProcessFile(engine, source, filename, options, logger, new GlobalBuildState(Path.GetFullPath(options.BinFolder), options), "  ", false)).Result;
    }

    private static async Task<(ProcessResult Result, bool RequireBuild)> ProcessFile(this ITemplateEngine engine, ISourceFile source, string filename, TemplateOptions options,
        IEmulatorLogger logger, GlobalBuildState buildState, string indent, bool isLibrary)
    {
        var (assemblyData, @namespace, classname, requireBuild) = await GetAssembly(engine, source, filename, options, logger, buildState, indent, isLibrary);
        return (await CompileFile(assemblyData, buildState, filename, @namespace, classname, isLibrary), requireBuild);
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
            logger.LogLine($"{indent}Building assembly '{Path.GetFileNameWithoutExtension(binaryFilename)}'");
            var templateDefinition = await CreateTemplate(engine, lines, filename, buildState, logger, @namespace, classname, isLibrary);
            assemblyData = CreateAssembly(templateDefinition, assemblyName, engine, logger, buildState, indent);

            if (Directory.Exists(options.BinFolder))
            {
                await File.WriteAllBytesAsync(binaryFilename, assemblyData);
                buildState.BinaryFilenames.Add(binaryFilename);

                await File.WriteAllTextAsync(binaryFilename + ".deps", JsonConvert.SerializeObject(new DependantsFile(templateDefinition)));
            }
            else
            {
                throw new Exception($"Output bin folder '{options.BinFolder}' does not exist.");
            }
        }
        else
        {
            logger.LogLine($"{indent}Loading assembly '{Path.GetFileNameWithoutExtension(binaryFilename)}'");

            if (File.Exists(binaryFilename + ".deps"))
            {
                var dependants = JsonConvert.DeserializeObject<DependantsFile>(File.ReadAllText(binaryFilename + ".deps"));

                if (dependants != null)
                {
                    foreach (var i in dependants.AssemblyFilenames)
                    {
                        buildState.BinaryFilenames.Add(FindFile(i, buildState));
                    }

                    foreach (var i in dependants.References)
                    {
                        buildState.BinaryFilenames.Add(FindFile(i, buildState));
                    }
                }
            }

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
            if (line.EndsWith(';'))
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
            var searched = new List<string>();

            // check if its an absolute path               
            if (File.Exists(importFilename.Value))
            {
                sourceFilename = importFilename.Value;
            }
            else
            {
                searched.Add(Path.GetFullPath(importFilename.Value));
            }

            // check if its relative to the source file, if the source file is real
            if (string.IsNullOrWhiteSpace(sourceFilename) && !string.IsNullOrWhiteSpace(source.Path))
            {
                var thisPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source.Path), importFilename.Value));
                searched.Add(thisPath);
                if (File.Exists(thisPath))
                    sourceFilename = thisPath;
            }

            if (string.IsNullOrWhiteSpace(sourceFilename) && string.IsNullOrWhiteSpace(Path.GetDirectoryName(importFilename.Value)))
            {
                var thisPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "library", importFilename.Value));
                searched.Add(thisPath);
                if (File.Exists(thisPath))
                    sourceFilename = thisPath;
            }

            if (string.IsNullOrWhiteSpace(sourceFilename))
                throw new ImportNotFoundException(importFilename.Value, searched, source.Path);

            sourceFilename = sourceFilename.FixFilename();

            if (!buildState.ImportToFilename.ContainsKey(importFilename.Value))
                buildState.ImportToFilename.Add(importFilename.Value, sourceFilename);

            if (!buildState.FilenameToClassname.ContainsKey(sourceFilename))
            {
                var sourceFile = new BitMagicProjectFile(sourceFilename);
                await sourceFile.Load();

                var result = await ProcessFile(engine, sourceFile, sourceFile.Path, options, logger, buildState, "  " + indent, true);
                var filename = (Path.GetFileNameWithoutExtension(sourceFile.Path) + ".generated.bmasm"); //.FixFilename();

                result.Result.SetName(filename);
                result.Result.SetParentAndMap(sourceFile);
                buildState.FilenameToClassname.Add(sourceFilename, $"{result.Result.Namespace}.{result.Result.Classname}");
                buildState.References.Add(result.Result);

                buildState.SourceFiles.Add(sourceFile);
                buildState.SourceFiles.Add(result.Result);

                newBuild |= result.RequireBuild;
            }
        }

        var maxImportTouchDate = DateTime.MinValue;

        foreach (var line in lines.Where(i => i.Trim().StartsWith("include")))
        {
            var regexResult = _includeRegex.Matches(line);

            if (regexResult.Count != 1)
                continue;

            var match = regexResult[0];

            if (!match.Success)
                continue;

            if (!match.Groups.TryGetValue("filename", out Group includeFilename))
                continue;
            
            if (string.IsNullOrWhiteSpace(includeFilename.Value))
                continue;


            string sourceFilename = "";
            var searched = new List<string>();

            // check if its an absolute path               
            if (File.Exists(includeFilename.Value))
            {
                sourceFilename = includeFilename.Value;
            }
            else
            {
                searched.Add(Path.GetFullPath(includeFilename.Value));
            }

            // check if its relative to the source file, if the source file is real
            if (string.IsNullOrWhiteSpace(sourceFilename) && !string.IsNullOrWhiteSpace(source.Path))
            {
                var thisPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source.Path), includeFilename.Value));
                searched.Add(thisPath);
                if (File.Exists(thisPath))
                    sourceFilename = thisPath;
            }

            if (string.IsNullOrWhiteSpace(sourceFilename) && string.IsNullOrWhiteSpace(Path.GetDirectoryName(includeFilename.Value)))
            {
                var thisPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "library", includeFilename.Value));
                searched.Add(thisPath);
                if (File.Exists(thisPath))
                    sourceFilename = thisPath;
            }

            if (string.IsNullOrWhiteSpace(sourceFilename))
                throw new ImportNotFoundException(includeFilename.Value, searched, source.Path);

            sourceFilename = sourceFilename.FixFilename();

            var lastChange = File.GetLastWriteTimeUtc(sourceFilename);

            if (lastChange > maxImportTouchDate)
                maxImportTouchDate = lastChange;
        }

        var binaryFilename = Path.Combine(options.BinFolder, contentAssemblyName);

        if (newBuild)
            return (true, binaryFilename);

        if (!source.ActualFile)
            return (true, binaryFilename);

        if (!File.Exists(binaryFilename))
            return (true, binaryFilename);

        var binaryWriteTime = File.GetLastWriteTimeUtc(binaryFilename);
        var sourceWritetime = File.GetLastWriteTimeUtc(source.Path);

        return (binaryWriteTime < sourceWritetime || binaryWriteTime < maxImportTouchDate, binaryFilename);
    }

    private static Regex _importRegex = new Regex(@"^\s*import (?<importName>[\w]+)\s*=\s*\""(?<filename>[\/\\\w\-.: ]+)\""\s*\;", RegexOptions.Compiled);
    private static Regex _includeRegex = new Regex(@"^\s*include \s*\""(?<filename>[\/\\\w\-.: ]+)\""\s*\;");

    /// <summary>
    /// Takes a source file and creates c# file that can be compiled
    /// </summary>
    /// <param name="contents">Input file contents</param>
    /// <returns>File that can be compiled</returns>
    /// <exception cref="ArgumentNullException"></exception>
    private static async Task<PreProcessResult> CreateTemplate(ITemplateEngine engine, IReadOnlyList<string> lines, string filename, 
        GlobalBuildState buildState,
        IEmulatorLogger logger, string @namespace = "BitMagic.App", string className = "Template", bool isLibrary = false)
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
        List<string> includedCode = new();

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
        var processingHeader = true;

        foreach (var originalLine in lines)
        {
            var line = originalLine.Trim();
            // emtpy line
            if (string.IsNullOrWhiteSpace(line))
            {
                output.Add("");
                continue;
            }

            if (line.StartsWith(';'))
            {
                output.Add("//" + originalLine);
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

            if (line.StartsWith("include"))
            {
                var regexResult = _includeRegex.Matches(line);

                if (regexResult.Count != 1)
                    throw new IncludeParseException($"Incorrect syntax in '{line}' (1)");

                var match = regexResult[0];

                if (!match.Success)
                    throw new IncludeParseException($"Incorrect syntax in '{line}' (2)");

                if (!match.Groups.TryGetValue("filename", out Group includeName))
                    throw new IncludeParseException($"Incorrect syntax in '{line}' (3)");

                if (string.IsNullOrWhiteSpace(includeName.Value))
                    throw new IncludeParseException($"Incorrect syntax in '{line}' blank includename");

                var baseDir = Path.GetDirectoryName(filename);
                var fullPath = Path.Combine(baseDir, includeName.Value);

                if (!File.Exists(fullPath))
                    throw new IncludeParseException($"Cannot find file '{fullPath}'");
                
                includedCode.Add(fullPath);
                output.Add("");
                continue;
            }

            if (line.StartsWith("using") && processingHeader)
            {
                userHeader.Add(line);

                output.Add("");
                continue;
            }

            if (line.StartsWith("reference"))
            {
                var idx = line.IndexOf(' ');
                var name = line.Substring(idx + 1);

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

            if (line.StartsWith("nuget"))
            {
                var toParse = line.Substring("nuget ".Length);
                var idx = toParse.IndexOf(';');
                if (idx >= 0)
                    toParse = toParse[..idx];

                toParse = toParse.Trim();

                idx = toParse.IndexOf(',');

                var version = "";
                var name = "";

                if (idx >= 0)
                {
                    name = toParse[..idx].Trim();
                    version = toParse[(idx+1)..].Trim();
                }
                else
                    name = toParse;

                if (name.StartsWith('"') && name.EndsWith('"'))
                    name = name[1..^1];

                if (version.StartsWith('"') && version.EndsWith('"'))
                    version = version[1..^1];

                var files = await NugetManager.DownloadNugetPackage(buildState.BinaryFolder, name, version, logger);

                foreach(var i in files)
                {
                    if (i.EndsWith(".dll"))
                        assemblyFilenames.Add(i);
                }

                output.Add("");
                continue;
            }

            processingHeader = false;

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
        if (buildState.Options.SavePreGeneratedTemplate)
        {
            File.WriteAllLines(Path.Combine(buildState.BinaryFolder, Path.GetFileName(filename) + ".pretemplate.cs"), allLines);
        }

        var processResult = engine.Process(allLines, startLine, filename, isLibrary);

        if (buildState.Options.SaveGeneratedTemplate)
        {
            File.WriteAllText(Path.Combine(buildState.BinaryFolder, Path.GetFileName(filename) + ".template.cs"), processResult.Code);
        }

        // this is suspect, might not work for libraries
        var cnt = 0; //  isLibrary ? 0 : 1; // this dosen't appear to make a difference
        for (var i = 0; i < processResult.Map.Count; i++)
        {
            if (processResult.Map[i] > 0)
                map.Add(new TemplateMap(cnt, processResult.Map[i]));

            cnt++;
        }

        return new PreProcessResult(references, assemblyFilenames, includedCode, processResult.Code, map, filename, @namespace, className);
    }

    internal sealed record class PreProcessResult(
            List<string> References,
            List<string> AssemblyFilenames,
            List<string> Includes,
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
        public string BinaryFolder { get; }
        public TemplateOptions Options { get; }
        public GlobalBuildState(string binaryFolder, TemplateOptions options)
        {
            BinaryFolder = binaryFolder;
            Options = options;
        }
    }

    // used to map the generated code to the original file, eg for error reporting.
    internal record struct TemplateMap(int GeneratedLine, int OriginalLine);

    private static byte[] CreateAssembly(PreProcessResult content, string contentAssemblyName, ITemplateEngine engine, IEmulatorLogger logger, GlobalBuildState buildState, string indent)
    {
        var toProcess = content.Content;

        if (toProcess == null)
            throw new ArgumentNullException(nameof(toProcess));


        var syntaxTrees = new List<SyntaxTree>();
        syntaxTrees.Add(CSharpSyntaxTree.ParseText(toProcess));

        foreach(var i in content.Includes)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(i)));
        }

        var assemblies = new List<Assembly>();

        assemblies.AddRange(new[] {
                typeof(object).Assembly,
                Assembly.Load(new AssemblyName("Microsoft.CSharp")),
                Assembly.Load(new AssemblyName("System.Runtime")),
                Assembly.Load(new AssemblyName("mscorlib")),
                Assembly.Load(new AssemblyName("System")),
                Assembly.Load(new AssemblyName("System.Core")),
                typeof(System.Collections.IList).Assembly,
                typeof(System.Collections.Generic.IEnumerable<>).Assembly,
                Assembly.Load(new AssemblyName("System.Linq")),
                Assembly.Load(new AssemblyName("System.Memory")),
                Assembly.Load(new AssemblyName("System.Numerics.Vectors")),
                Assembly.Load(new AssemblyName("System.Numerics")),
                Assembly.Load(new AssemblyName("System.Collections")),
                Assembly.Load(new AssemblyName("System.Linq.Expressions")),
                Assembly.Load(new AssemblyName("System.ObjectModel")),
                Assembly.Load(new AssemblyName("netstandard")),
                typeof(Template).Assembly
                //,
        });

//        var t = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES").ToString();

        assemblies.AddRange(engine.Assemblies);

        foreach (var assemblyFilename in content.AssemblyFilenames)
        {
            var assemblyInclude = Assembly.LoadFrom(FindFile(assemblyFilename, buildState));
            logger.LogLine($"{indent}Adding File Assembly: {assemblyInclude.FullName}");

            buildState.BinaryFilenames.Add(assemblyInclude.Location);
            assemblies.Add(assemblyInclude);

            //var x = assemblyInclude.GetReferencedAssemblies();
            //Assembly.Load(
        }


        foreach (var include in content.References)
        {
            var assemblyInclude = Assembly.Load(new AssemblyName(include));
            logger.LogLine($"{indent}Adding Referenced Assembly: {assemblyInclude.FullName}");

            buildState.BinaryFilenames.Add(assemblyInclude.Location);
            assemblies.Add(assemblyInclude);
        }

        //foreach (var a in assemblyInclude.GetReferencedAssemblies())
        //{
        //    var am = Assembly.Load(a);
        //    assemblies.Add(am);

        //    var x = am.GetReferencedAssemblies();
        //}


        var builtMetadata = buildState.AllReferences.Select(i => MetadataReference.CreateFromImage(i.CompiledData));

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation compilation = CSharpCompilation.Create(
            contentAssemblyName,
            syntaxTrees,
            assemblies.Select(i => MetadataReference.CreateFromFile(i.Location)).Union(builtMetadata),
            options);

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

    internal static string FindFile(string filename, GlobalBuildState buildState)
    {
        var f = Path.Combine(buildState.Options.BasePath, filename);
        if (File.Exists(f))
            return f;

        f = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", filename);
        if (File.Exists(f))
            return f;

        if (File.Exists(filename))
            return filename;

        throw new AssemblyFileNotFoundException(filename);
    }


    private static async Task<ProcessResult> CompileFile(byte[] assemblyData, GlobalBuildState buildState, string sourceFilename, string @namespace, string className, bool isLibrary)
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

        var sourcePath = Path.GetFullPath(Path.GetDirectoryName(sourceFilename));

        sb.Append("-n ");
        sb.Append(@namespace);
        sb.Append(" -c ");
        sb.Append(@className);
        sb.Append(" -b \"");
        sb.Append(Directory.GetCurrentDirectory());
        sb.Append("\"");
        sb.Append(" -s \"");
        sb.Append(sourcePath);
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
