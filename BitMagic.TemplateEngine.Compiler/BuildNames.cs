using System;
using System.Collections.Generic;
using System.IO;

namespace BitMagic.TemplateEngine.Compiler;

/// <summary>
/// The names used by one project build, so two files can't end up sharing one. A file's cached assembly and class are
/// named after its library name, which by default comes from its filename, so two files with the same name in
/// different folders would overwrite or load each other's unless one has a 'library' name.
/// Share one between every file a project builds; each template build without one only checks its own imports.
/// </summary>
public sealed class BuildNames
{
    // on Linux 'Main.bmasm' and 'main.bmasm' are different files that make different names, on Windows they'd be
    // the same file in the bin folder
    private static readonly StringComparer _comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // library name -> the source file that has it
    private readonly Dictionary<string, string> _libraries = new(_comparer);

    // output path on the SD card -> the source file that writes it. FAT, so case doesn't matter.
    private readonly Dictionary<string, string> _outputs = new(StringComparer.OrdinalIgnoreCase);

    // errors show paths relative to this, eg the project folder
    private readonly string _basePath;

    public BuildNames(string basePath = "")
    {
        _basePath = string.IsNullOrWhiteSpace(basePath) ? "" : Path.GetFullPath(basePath);
    }

    internal static bool SamePath(string a, string b) => _comparer.Equals(a, b);

    private string Show(string path) => _basePath == "" ? path : Path.GetRelativePath(_basePath, path);

    /// <summary>
    /// Records a source file and its library name (namespace.class). Returns the error if the name is already used by
    /// a different file, otherwise null. A file can be registered more than once, eg a library two files import.
    /// </summary>
    internal string? Register(string sourcePath, string libraryName)
    {
        if (_libraries.TryGetValue(libraryName, out var other) && !SamePath(other, sourcePath))
            return $"'{Show(other)}' and '{Show(sourcePath)}' both build as '{libraryName}'. Rename one, or give it a 'library' name.";

        _libraries[libraryName] = sourcePath;

        return null;
    }

    /// <summary>
    /// Records a file written to the SD card, eg 'BIN/GAME.PRG'. Returns the error if a different source file already
    /// writes it, otherwise null.
    /// </summary>
    public string? RegisterOutput(string outputPath, string sourcePath)
    {
        var path = outputPath.Replace('\\', '/').TrimStart('/');

        if (_outputs.TryGetValue(path, out var other) && !SamePath(other, sourcePath))
            return $"'{Show(other)}' and '{Show(sourcePath)}' both write '{path}'. Give one of them a different output filename.";

        _outputs[path] = sourcePath;

        return null;
    }
}
