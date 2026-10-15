using BitMagic.TemplateEngine.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BitMagic.TemplateEngine.Compiler;

// 'file <kind>' files, whose view is the library <kind>.bmasm, and 'place' lines
public static partial class MacroAssembler
{
    /// <summary>
    /// A 'file &lt;kind&gt; [arguments]' header. LineIndex is zero based.
    /// </summary>
    internal sealed record class FileHeader(string Kind, string? Arguments, int LineIndex)
    {
        public string ViewFilename => Kind + ".bmasm";
    }

    /// <summary>
    /// A view's class, the keywords it claims, and the keyword, if any, that starts a block of C#
    /// members: lines that go in the file's class, rather than in its body.
    /// </summary>
    internal sealed record class ViewInfo(string ClassName, HashSet<string> Keywords, string? Members);

    private static readonly Regex _fileRegex = new(@"^\s*file\s+(?<kind>[A-Za-z_][\w\-]*)(?<arguments>[^;]*)(;.*)?$", RegexOptions.Compiled);
    private static readonly Regex _placeRegex = new(@"^\s*place\s+(?:(?<alias>[A-Za-z_]\w*)\.)?(?<name>[A-Za-z_]\w*)\s*(;.*)?$", RegexOptions.Compiled);
    private static readonly Regex _placeStartRegex = new(@"^\s*place\s+[A-Za-z_]", RegexOptions.Compiled);
    private static readonly Regex _keywordRegex = new(@"^\s*(?<keyword>[A-Za-z_]\w*)(?<arguments>[^;]*)(;.*)?$", RegexOptions.Compiled);

    // C#'s 'file class X' modifier, not a header
    private static readonly HashSet<string> _csharpTypeWords = new(StringComparer.Ordinal)
    {
        "class", "struct", "record", "interface", "enum", "delegate", "static", "sealed", "abstract", "partial", "readonly", "unsafe", "ref"
    };

    private const string ObjectsNamespace = "global::BitMagic.TemplateEngine.Objects";

    /// <summary>
    /// The 'file &lt;kind&gt;' header, if the first line that isn't blank or a comment is one.
    /// </summary>
    internal static FileHeader? GetFileHeader(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//"))
                continue;

            var match = _fileRegex.Match(lines[i]);

            if (!match.Success || _csharpTypeWords.Contains(match.Groups["kind"].Value))
                return null;

            var arguments = match.Groups["arguments"].Value.Trim();

            return new FileHeader(match.Groups["kind"].Value, arguments.Length == 0 ? null : arguments, i);
        }

        return null;
    }

    /// <summary>
    /// The files to build before this one: its imports, and its view. LineIndex is zero based.
    /// </summary>
    private static IEnumerable<(string Filename, int LineIndex)> GetImportFilenames(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Trim().StartsWith("import"))
                continue;

            var match = _importRegex.Match(lines[i]);

            if (!match.Success || string.IsNullOrWhiteSpace(match.Groups["importName"].Value) || string.IsNullOrWhiteSpace(match.Groups["filename"].Value))
                continue;

            yield return (match.Groups["filename"].Value, i);
        }

        var header = GetFileHeader(lines);

        if (header != null)
            yield return (header.ViewFilename, header.LineIndex);
    }

    /// <summary>
    /// Import alias -> the imported file's source path, for 'place Alias.section'.
    /// </summary>
    private static Dictionary<string, string> GetImportAliases(IReadOnlyList<string> lines, string filename, GlobalBuildState buildState)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in lines.Where(i => i.Trim().StartsWith("import")))
        {
            var match = _importRegex.Match(line);

            if (match.Success && buildState.ImportToFilename.TryGetValue((filename, match.Groups["filename"].Value), out var path))
                aliases[match.Groups["importName"].Value] = path;
        }

        return aliases;
    }

    /// <summary>
    /// 'place section' or 'place Alias.section' as C#, or null if the line isn't a 'place'.
    /// </summary>
    private static string? PlaceLine(string line, int lineIndex, string filename, Dictionary<string, string> aliases)
    {
        if (!_placeStartRegex.IsMatch(line))
            return null;

        var match = _placeRegex.Match(line);

        if (!match.Success)
            throw SourceError(filename, lineIndex, $"Expected 'place <section>' or 'place <alias>.<section>', not '{line.Trim()}'.");

        var name = match.Groups["name"].Value;
        var alias = match.Groups["alias"].Value;

        if (alias.Length == 0)
            return $"{ObjectsNamespace}.Section.Get(\"{name}\").Place({Verbatim(filename)}, {lineIndex});";

        if (!aliases.TryGetValue(alias, out var owner))
            throw SourceError(filename, lineIndex, $"'{alias}' isn't an import in this file.");

        return $"{ObjectsNamespace}.Section.Get(\"{name}\").Place({Verbatim(filename)}, {lineIndex}, {Verbatim(owner)});";
    }

    /// <summary>
    /// A keyword the view claims as C#, or null if the line isn't one.
    /// </summary>
    private static string? ViewKeywordLine(string line, int lineIndex, string filename, ViewInfo view)
    {
        var match = _keywordRegex.Match(line);

        if (!match.Success || !view.Keywords.Contains(match.Groups["keyword"].Value))
            return null;

        return $"{ObjectsNamespace}.Template.Keyword(\"{match.Groups["keyword"].Value}\", {Verbatim(match.Groups["arguments"].Value.Trim())}, {lineIndex}, {Verbatim(filename)});";
    }

    /// <summary>
    /// The line that starts the view's members block, eg 'members', with nothing else on it but a comment.
    /// </summary>
    private static bool IsMembersLine(string line, ViewInfo view)
    {
        if (view.Members == null)
            return false;

        var comment = line.IndexOf(';');
        var text = (comment == -1 ? line : line[..comment]).Trim();

        return text == view.Members;
    }

    /// <summary>
    /// The view's class and keywords. The view is built as an import before the file that uses it.
    /// </summary>
    private static ViewInfo GetViewInfo(FileHeader header, string filename, GlobalBuildState buildState)
    {
        if (!buildState.ImportToFilename.TryGetValue((filename, header.ViewFilename), out var viewPath) ||
            !buildState.FilenameToClassname.TryGetValue(viewPath, out var className))
            throw SourceError(filename, header.LineIndex, $"The view '{header.ViewFilename}' for 'file {header.Kind}' hasn't been built.");

        if (buildState.Views.TryGetValue(className, out var info))
            return info;

        var view = buildState.AllReferences.First(i => $"{i.Namespace}.{i.Classname}" == className);
        var context = new CustomAssemblyLoadContext();

        try
        {
            var assembly = LoadWithReferences(context, view.CompiledData, buildState, view);
            var type = assembly.GetType(className) ?? throw SourceError(filename, header.LineIndex, $"Could not find the view class '{className}' in '{header.ViewFilename}'.");

            var process = type.GetMethod("Process", BindingFlags.Public | BindingFlags.Static, new[] { typeof(ViewFile) });

            if (process == null || process.ReturnType != typeof(void))
                throw SourceError(filename, header.LineIndex, $"'{header.ViewFilename}' can't be used with 'file {header.Kind}': it has no 'public static void Process(ViewFile file)'.");

            var keywords = type.GetProperty("Keywords", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IEnumerable<string>;
            var members = type.GetProperty("Members", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;

            info = new ViewInfo(className, new HashSet<string>(keywords ?? Array.Empty<string>(), StringComparer.Ordinal), string.IsNullOrWhiteSpace(members) ? null : members.Trim());
        }
        finally
        {
            context.Unload();
        }

        buildState.Views.Add(className, info);

        return info;
    }

    private static Assembly LoadWithReferences(CustomAssemblyLoadContext context, byte[] assemblyData, GlobalBuildState buildState, ProcessResult? skip = null)
    {
        Assembly assembly;

        using (var ms = new MemoryStream(assemblyData))
            assembly = context.LoadFromStream(ms);

        foreach (var i in buildState.AllReferences)
        {
            if (i == skip)
                continue;

            using var ims = new MemoryStream(i.CompiledData);
            context.LoadFromStream(ims);
        }

        foreach (var i in buildState.BinaryFilenames)
        {
            if (!context.HasLoadedAssemblyFromFile(i))
            {
                // LoadFromStream copies the image, so close the file straight away rather than leave it locked.
                using var fs = new FileStream(i, FileMode.Open, FileAccess.Read);
                context.LoadFromStream(fs);
            }
        }

        return assembly;
    }

    private static string Verbatim(string? value) => value == null ? "null" : $"@\"{value.Replace("\"", "\"\"")}\"";

    /// <summary>
    /// An error at a line of a source file, reported like a C# error in the template.
    /// </summary>
    private static TemplateCompilationException SourceError(string filename, int lineIndex, string message)
    {
        var e = new TemplateCompilationException { Filename = filename };
        e.Errors.Add(new CompilationError { ErrorText = message, LineNumber = lineIndex < 0 ? -1 : lineIndex + 1 });
        return e;
    }

    /// <summary>
    /// A section or view error from the running template, as a compilation error at its line.
    /// </summary>
    private static TemplateCompilationException SourceError(TemplateSourceException e, string mainFilename) =>
        SourceError(string.IsNullOrWhiteSpace(e.SourceFile) ? mainFilename : e.SourceFile, e.Line, e.Message);
}
