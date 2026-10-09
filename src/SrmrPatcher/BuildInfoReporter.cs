using System.Diagnostics;
using System.Text.Json;

namespace SrmrPatcher
{
    internal sealed record BuildInfoReport(
        int SchemaVersion,
        DateTimeOffset CollectedAtUtc,
        IReadOnlyList<FileRecord> Game,
        IReadOnlyList<FileRecord> Loader);

    internal sealed record FileRecord(
        string Path,
        bool Present,
        long? Size = null,
        string? Sha256 = null,
        string? FileVersion = null,
        string? ProductVersion = null);

    internal static class BuildInfoReporter
    {
        private static readonly (string Path, bool IncludeVersion)[] GameFiles =
        [
            ("SRM-VR.exe", true),
            ("UnityPlayer.dll", true),
            ("GameAssembly.dll", true),
            ("SRM-VR_Data/il2cpp_data/Metadata/global-metadata.dat", false)
        ];

        private static readonly (string Path, bool IncludeVersion)[] LoaderFiles =
        [
            ("winhttp.dll", true),
            ("BepInEx/core/BepInEx.Unity.IL2CPP.dll", true),
            ("BepInEx/core/Il2CppInterop.Runtime.dll", true)
        ];

        public static string CreateJson(ToolPaths paths)
        {
            var report = new BuildInfoReport(
                1,
                DateTimeOffset.UtcNow,
                GameFiles.Select(file => CreateRecord(paths, file)).ToArray(),
                LoaderFiles.Select(file => CreateRecord(paths, file)).ToArray());
            return JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
        }

        private static FileRecord CreateRecord(
            ToolPaths paths,
            (string Path, bool IncludeVersion) file)
        {
            string normalizedPath = file.Path.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(paths.GameRoot, normalizedPath);
            if (!File.Exists(fullPath))
            {
                return new FileRecord(file.Path, Present: false);
            }

            var fileInfo = new FileInfo(fullPath);
            if (!file.IncludeVersion)
            {
                return new FileRecord(
                    file.Path,
                    Present: true,
                    fileInfo.Length,
                    Hashing.CalculateSha256(fullPath));
            }

            FileVersionInfo version = FileVersionInfo.GetVersionInfo(fullPath);
            return new FileRecord(
                file.Path,
                Present: true,
                fileInfo.Length,
                Hashing.CalculateSha256(fullPath),
                version.FileVersion,
                version.ProductVersion);
        }
    }
}
