using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BitMagic.TemplateEngine.Compiler;
public static class ReferenceResolver
{
    // Entry point: resolve references from namespaces
    public static IReadOnlyList<MetadataReference> ResolveFromNamespaces(
        IEnumerable<string> namespaces,
        string targetFramework)
    {
        var refDir = FindReferenceAssemblyDirectory(targetFramework);
        return ResolveReferences(namespaces, refDir).ToList();
    }

    // ------------------------------------------------------------
    // 1. Find the .NET SDK reference assembly directory
    // ------------------------------------------------------------
    private static string FindReferenceAssemblyDirectory(string targetFramework)
    {
        var runtimeVersion = GetCurrentRuntimeVersion();
        var refPackVersion = NormalizeToRefPackVersion(runtimeVersion);

        // dotnet root is always the parent of the runtime directory
        // Example: C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.1\
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        return runtimeDir;

        // Go up to: C:\Program Files\dotnet\
        var dotnetRoot = Directory.GetParent(runtimeDir)!.Parent!.FullName;

        var refPackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", refPackVersion);

        if (!Directory.Exists(refPackRoot))
            throw new DirectoryNotFoundException($"Reference pack not found: {refPackRoot}");

        var refDir = Path.Combine(refPackRoot, "ref", targetFramework);

        if (!Directory.Exists(refDir))
            throw new DirectoryNotFoundException($"Reference assemblies not found: {refDir}");

        return refDir;
    }

    private static string NormalizeToRefPackVersion(string runtimeVersion)
    {
        var parts = runtimeVersion.Split('.');
        if (parts.Length < 2)
            throw new Exception($"Invalid runtime version: {runtimeVersion}");

        return $"{parts[0]}.{parts[1]}.0";
    }

    private static string GetCurrentRuntimeVersion()
    {
        // Example: ".NET 8.0.1"
        var desc = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        // Extract the version number
        var version = desc.Split(' ').Last().Trim();

        return version;
    }

    private static string? _basePath = null;

    private static string GetDotnetSdkBasePath()
    {
        if (_basePath != null) return _basePath;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--info",
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var basePathLine = output.Split('\n')
            .FirstOrDefault(l => l.TrimStart().StartsWith("Base Path:", StringComparison.OrdinalIgnoreCase));

        if (basePathLine == null)
            throw new Exception("Unable to locate .NET SDK base path via `dotnet --info`.");

        _basePath = basePathLine.Split(':', 2)[1].Trim();
        return _basePath;
    }

    // ------------------------------------------------------------
    // 2. Resolve assemblies that contain (or forward) the namespaces
    // ------------------------------------------------------------
    private static IEnumerable<MetadataReference> ResolveReferences(
        IEnumerable<string> namespaces,
        string referenceAssemblyDirectory)
    {
        var namespaceList = namespaces.ToList();

        foreach (var dll in Directory.EnumerateFiles(referenceAssemblyDirectory, "*.dll"))
        {
            MetadataReference reference;

            try
            {
                reference = MetadataReference.CreateFromFile(dll);
            }
            catch
            {
                continue;
            }

            var asm = LoadAssemblySymbol(reference);

            if (asm == null)
                continue;

            if (AssemblyContainsAnyNamespace(asm, namespaceList))
                yield return reference;
        }
    }

    private static IAssemblySymbol? LoadAssemblySymbol(MetadataReference reference)
    {
        try
        {
            var temp = CSharpCompilation.Create("Temp", references: new[] { reference });
            return (IAssemblySymbol)temp.GetAssemblyOrModuleSymbol(reference)!;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------
    // 3. Check if an assembly contains or forwards a namespace
    // ------------------------------------------------------------
    private static bool AssemblyContainsAnyNamespace(
        IAssemblySymbol asm,
        IEnumerable<string> namespaces)
    {
        foreach (var ns in namespaces)
        {
            if (NamespaceExists(asm.GlobalNamespace, ns))
                return true;
        }

        return false;
    }

    private static bool NamespaceExists(INamespaceSymbol root, string ns)
    {
        var parts = ns.Split('.');
        var current = root;

        foreach (var part in parts)
        {
            current = current.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
            if (current == null)
                return false;
        }

        return true;
    }
}