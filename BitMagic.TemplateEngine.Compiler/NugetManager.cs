using BitMagic.Common;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace BitMagic.TemplateEngine.Compiler;

internal static class NugetManager
{
    public static async Task<IEnumerable<string>> DownloadNugetPackage(string destination, string packageId, string version, IEmulatorLogger logger)
    {
        var log = new NugetLogger(logger);
        var cancellationToken = CancellationToken.None;

        var cache = new SourceCacheContext();
        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

        var packageVersion = new NuGetVersion(version);
        using var packageStream = new MemoryStream();

        await resource.CopyNupkgToStreamAsync(
            packageId,
            packageVersion,
            packageStream,
            cache,
            log,
            cancellationToken);

        using var packageReader = new PackageArchiveReader(packageStream);

        var c = GetCurrentFramework();

        var framework = packageReader.GetSupportedFrameworks().Last(i => DefaultCompatibilityProvider.Instance.IsCompatible(c, i));

        var files = (await packageReader.GetFilesAsync(cancellationToken)).Where(i => i.StartsWith("lib/") && ParseFramework(i) == framework).ToList();

        await packageReader.CopyFilesAsync(destination, files, (string _, string targetPath, Stream fileStream) => ExtractFile(targetPath, fileStream, destination), log, cancellationToken);

        foreach (var i in packageReader.GetPackageDependencies())
        {
            if (i.TargetFramework != framework)
                continue;

            foreach(var p in i.Packages)
            {
                files.AddRange(await DownloadNugetPackage(destination, p.Id, p.VersionRange.ToShortString(), logger));
            }

            var a = 0;
        }

        return files.Select(i => Path.Combine(destination, Path.GetFileName(i)));
    }

    private static NuGetFramework ParseFramework(string framework)
    {
        if (string.IsNullOrWhiteSpace(framework))
            return NuGetFramework.UnsupportedFramework;

        var idx = framework.IndexOf('/', 4);

        if (idx < 0)
            return NuGetFramework.UnsupportedFramework;

        return NuGetFramework.ParseFolder(framework[4..idx]);
    }

    private static string ExtractFile(string targetPath, Stream fileStream, string outputPath)
    {
        var path = Path.Combine(outputPath, Path.GetFileName(targetPath));
        if (File.Exists(path))
            return null;

        using var toWrite = File.Create(path);
        fileStream.CopyTo(toWrite);
        return path;
    }

    private static NuGetFramework GetCurrentFramework() =>
    NuGetFramework.Parse(
        Assembly
            .GetEntryAssembly()?
            .GetCustomAttribute<TargetFrameworkAttribute>()?
            .FrameworkName ?? throw new Exception("Cannot determine the current version of .net")
    );

    private sealed class NugetLogger : ILogger
    {
        private readonly IEmulatorLogger _logger;

        public NugetLogger(IEmulatorLogger logger)
        {
            _logger = logger;
        }

        public void Log(LogLevel level, string data)
        {
            _logger.LogLine($"{level}: data");
        }

        public void Log(ILogMessage message)
        {
            _logger.LogLine($"{message.WarningLevel} : {message.Message}");
        }

        public Task LogAsync(LogLevel level, string data)
        {
            _logger.LogLine($"{level}: data");
            return Task.CompletedTask;
        }

        public Task LogAsync(ILogMessage message)
        {
            _logger.LogLine($"{message.WarningLevel} : {message.Message}");
            return Task.CompletedTask;
        }

        public void LogDebug(string data)
        {
            _logger.LogLine($"ERROR : {data}");
        }

        public void LogError(string data)
        {
            _logger.LogLine($"ERROR : {data}");
        }

        public void LogInformation(string data)
        {
            _logger.LogLine($"INFO : {data}");
        }

        public void LogInformationSummary(string data)
        {
            _logger.LogLine($"INFO SUM : {data}");
        }

        public void LogMinimal(string data)
        {
            _logger.LogLine($"MIN : {data}");
        }

        public void LogVerbose(string data)
        {
            _logger.LogLine($"VERB : {data}");
        }

        public void LogWarning(string data)
        {
            _logger.LogLine($"WARN : {data}");
        }
    }
}
