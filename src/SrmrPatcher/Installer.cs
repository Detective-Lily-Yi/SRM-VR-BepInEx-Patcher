using System.Diagnostics;

namespace SrmrPatcher;

internal sealed record InstallResult(
    string GameRoot,
    string GameAssemblySha256,
    string UnityPlayerSha256,
    string ProtectedMetadataSha256,
    string UnityDoorstopSha256,
    string RebuiltMetadata)
{
    public IEnumerable<KeyValuePair<string, object?>> AsSummary()
    {
        yield return new("Status", "Installed");
        yield return new("Game root", GameRoot);
        yield return new("GameAssembly SHA-256", GameAssemblySha256);
        yield return new("UnityPlayer SHA-256", UnityPlayerSha256);
        yield return new("Protected metadata SHA-256", ProtectedMetadataSha256);
        yield return new("UnityDoorstop SHA-256", UnityDoorstopSha256);
        yield return new("Rebuilt metadata", RebuiltMetadata);
        yield return new(
            "Next step",
            "Start SRM-VR from Steam and check BepInEx\\LogOutput.log.");
    }
}

internal static class Installer
{
    public static InstallResult Install(
        ToolPaths paths,
        bool skipUnblock,
        bool continueOnHashMismatch)
    {
        EnsureRequiredFiles(paths);
        EnsureGameIsNotRunning(
            "Close all SRM-VR processes before installing the compatibility fix");

        var verifier = new HashVerifier(continueOnHashMismatch);
        string gameAssemblyHash = verifier.Verify(
            paths.GameAssembly,
            SupportedBuild.GameAssemblyHashes,
            "GameAssembly.dll");
        string unityPlayerHash = verifier.Verify(
            paths.UnityPlayer,
            SupportedBuild.UnityPlayerHashes,
            "UnityPlayer.dll");
        string protectedMetadataHash = verifier.Verify(
            paths.ProtectedMetadata,
            SupportedBuild.ProtectedMetadataHashes,
            "global-metadata.dat");
        verifier.Verify(paths.ExportMap, SupportedBuild.ExportMapHashes, "SRM-VR export map");
        verifier.Verify(
            paths.DoorstopProxy,
            SupportedBuild.DoorstopInputHashes,
            "UnityDoorstop winhttp.dll");
        verifier.Verify(
            paths.RuntimeAssembly,
            SupportedBuild.RuntimeInputHashes,
            "Il2CppInterop.Runtime.dll");
        verifier.Verify(
            paths.BepInExAssembly,
            SupportedBuild.BepInExInputHashes,
            "BepInEx.Unity.IL2CPP.dll");

        if (!skipUnblock)
        {
            FileUnblocker.UnblockTree(Path.Combine(paths.GameRoot, "BepInEx"));
            FileUnblocker.UnblockTree(Path.Combine(paths.GameRoot, "dotnet"));
            FileUnblocker.Unblock(paths.DoorstopProxy);
            FileUnblocker.Unblock(paths.DoorstopConfig);
        }

        MetadataRebuilder.Rebuild(
            new MetadataRebuildOptions(
                paths.GameAssembly,
                paths.ProtectedMetadata,
                paths.RebuiltMetadata,
                verifier.ContinueOnMismatch));
        verifier.Verify(
            paths.RebuiltMetadata,
            SupportedBuild.RebuiltMetadataHashes,
            "rebuilt metadata");

        string stagingToken = Guid.NewGuid().ToString("N");
        string stagedDoorstop = Path.Combine(
            paths.ToolsDirectory,
            $"winhttp.{stagingToken}.staging.dll");
        string stagedRuntime = Path.Combine(
            paths.ToolsDirectory,
            $"Il2CppInterop.Runtime.{stagingToken}.staging.dll");
        string stagedBepInEx = Path.Combine(
            paths.ToolsDirectory,
            $"BepInEx.Unity.IL2CPP.{stagingToken}.staging.dll");
        string[] stagedFiles = [stagedDoorstop, stagedRuntime, stagedBepInEx];

        try
        {
            File.Copy(paths.DoorstopProxy, stagedDoorstop);
            File.Copy(paths.RuntimeAssembly, stagedRuntime);
            File.Copy(paths.BepInExAssembly, stagedBepInEx);

            ExportMap exportMap = ExportMap.Load(paths.ExportMap);
            DoorstopPatchResult doorstopResult = DoorstopPatcher.Patch(
                stagedDoorstop,
                exportMap,
                Path.Combine(paths.BackupDirectory, "winhttp.before-srmr-patch.dll"));
            ConsoleOutput.PrintSummary(doorstopResult.AsSummary());

            InteropPatchResult interopResult = InteropPatcher.Patch(
                stagedRuntime,
                stagedBepInEx,
                exportMap,
                Path.Combine(paths.BackupDirectory, "Il2CppInterop.Runtime.original.dll"),
                Path.Combine(paths.BackupDirectory, "BepInEx.Unity.IL2CPP.original.dll"));
            ConsoleOutput.PrintSummary(interopResult.AsSummary());

            verifier.Verify(
                stagedDoorstop,
                SupportedBuild.DoorstopOutputHashes,
                "patched UnityDoorstop winhttp.dll");
            verifier.Verify(
                stagedRuntime,
                SupportedBuild.RuntimeOutputHashes,
                "patched Il2CppInterop.Runtime.dll");
            verifier.Verify(
                stagedBepInEx,
                SupportedBuild.BepInExOutputHashes,
                "patched BepInEx.Unity.IL2CPP.dll");
            EnsureGameIsNotRunning(
                "SRM-VR started while files were being prepared. " +
                "Close it and run the installer again");

            File.Copy(stagedDoorstop, paths.DoorstopProxy, overwrite: true);
            File.Copy(stagedRuntime, paths.RuntimeAssembly, overwrite: true);
            File.Copy(stagedBepInEx, paths.BepInExAssembly, overwrite: true);
        }
        finally
        {
            foreach (string stagedFile in stagedFiles)
            {
                File.Delete(stagedFile);
            }
        }

        string installedDoorstopHash = verifier.Verify(
            paths.DoorstopProxy,
            SupportedBuild.DoorstopOutputHashes,
            "installed UnityDoorstop winhttp.dll");
        verifier.Verify(
            paths.RuntimeAssembly,
            SupportedBuild.RuntimeOutputHashes,
            "installed Il2CppInterop.Runtime.dll");
        verifier.Verify(
            paths.BepInExAssembly,
            SupportedBuild.BepInExOutputHashes,
            "installed BepInEx.Unity.IL2CPP.dll");
        ConfigurationUpdater.Update(paths.DoorstopConfig, paths.BepInExConfig);

        return new InstallResult(
            paths.GameRoot,
            gameAssemblyHash,
            unityPlayerHash,
            protectedMetadataHash,
            installedDoorstopHash,
            paths.RebuiltMetadata);
    }

    private static void EnsureRequiredFiles(ToolPaths paths)
    {
        string[] requiredFiles =
        [
            paths.GameAssembly,
            paths.UnityPlayer,
            paths.ProtectedMetadata,
            paths.DoorstopProxy,
            paths.DoorstopConfig,
            paths.RuntimeAssembly,
            paths.BepInExAssembly,
            Path.Combine(paths.GameRoot, "dotnet", "coreclr.dll"),
            paths.ExportMap
        ];
        foreach (string path in requiredFiles)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Required file is missing", path);
            }
        }
    }

    private static void EnsureGameIsNotRunning(string message)
    {
        Process[] processes = Process.GetProcessesByName("SRM-VR");
        try
        {
            if (processes.Length > 0)
            {
                throw new InvalidOperationException(message);
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}

internal static class FileUnblocker
{
    public static void UnblockTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            Unblock(path);
        }
    }

    public static void Unblock(string path)
    {
        try
        {
            File.Delete(path + ":Zone.Identifier");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
