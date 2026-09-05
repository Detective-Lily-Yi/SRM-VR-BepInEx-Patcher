namespace SrmrPatcher;

internal static class Commands
{
    public static int Install(string[] args)
    {
        CommandLine commandLine = CommandLine.Parse(
            args,
            ["--game-root"],
            ["--skip-unblock", "--continue-on-hash-mismatch"]);
        if (commandLine.HasFlag("--help"))
        {
            Console.WriteLine(
                """
                Usage: SrmrPatcher.exe install [options]

                Options:
                  --game-root PATH                 Override the detected game folder
                  --skip-unblock                   Keep downloaded-file markers
                  --continue-on-hash-mismatch      Accept unsupported input hashes
                  --help                           Show this help
                """);
            return 0;
        }

        ToolPaths paths = ToolPaths.Create(commandLine.GetOptional("--game-root"));
        InstallResult result = Installer.Install(
            paths,
            commandLine.HasFlag("--skip-unblock"),
            commandLine.HasFlag("--continue-on-hash-mismatch"));
        ConsoleOutput.PrintSummary(result.AsSummary());
        return 0;
    }

    public static int PatchDoorstop(string[] args)
    {
        CommandLine commandLine = CommandLine.Parse(
            args,
            ["--game-root", "--doorstop-path", "--export-map-path"],
            []);
        if (commandLine.HasFlag("--help"))
        {
            Console.WriteLine(
                """
                Usage: SrmrPatcher.exe patch-doorstop [options]

                Options:
                  --game-root PATH         Override the detected game folder
                  --doorstop-path PATH     Override winhttp.dll
                  --export-map-path PATH   Override the export map
                  --help                   Show this help
                """);
            return 0;
        }

        ToolPaths paths = ToolPaths.Create(commandLine.GetOptional("--game-root"));
        string doorstopPath = commandLine.GetOptional("--doorstop-path") ??
            paths.DoorstopProxy;
        string exportMapPath = commandLine.GetOptional("--export-map-path") ??
            paths.ExportMap;
        DoorstopPatchResult result = DoorstopPatcher.Patch(
            doorstopPath,
            ExportMap.Load(exportMapPath),
            Path.Combine(paths.BackupDirectory, "winhttp.before-srmr-patch.dll"));
        ConsoleOutput.PrintSummary(result.AsSummary());
        return 0;
    }

    public static int PatchInterop(string[] args)
    {
        CommandLine commandLine = CommandLine.Parse(
            args,
            [
                "--game-root",
                "--runtime-assembly-path",
                "--bepinex-assembly-path",
                "--export-map-path"
            ],
            []);
        if (commandLine.HasFlag("--help"))
        {
            Console.WriteLine(
                """
                Usage: SrmrPatcher.exe patch-interop [options]

                Options:
                  --game-root PATH                  Override the detected game folder
                  --runtime-assembly-path PATH      Override Il2CppInterop.Runtime.dll
                  --bepinex-assembly-path PATH      Override BepInEx.Unity.IL2CPP.dll
                  --export-map-path PATH            Override the export map
                  --help                            Show this help
                """);
            return 0;
        }

        ToolPaths paths = ToolPaths.Create(commandLine.GetOptional("--game-root"));
        string runtimePath = commandLine.GetOptional("--runtime-assembly-path") ??
            paths.RuntimeAssembly;
        string bepInExPath = commandLine.GetOptional("--bepinex-assembly-path") ??
            paths.BepInExAssembly;
        string exportMapPath = commandLine.GetOptional("--export-map-path") ??
            paths.ExportMap;
        InteropPatchResult result = InteropPatcher.Patch(
            runtimePath,
            bepInExPath,
            ExportMap.Load(exportMapPath),
            Path.Combine(paths.BackupDirectory, "Il2CppInterop.Runtime.original.dll"),
            Path.Combine(paths.BackupDirectory, "BepInEx.Unity.IL2CPP.original.dll"));
        ConsoleOutput.PrintSummary(result.AsSummary());
        return 0;
    }

    public static int RebuildMetadata(string[] args)
    {
        CommandLine commandLine = CommandLine.Parse(
            args,
            ["--game-assembly", "--metadata", "--output"],
            ["--allow-hash-mismatch", "--analyze-layout", "--verbose"]);
        if (commandLine.HasFlag("--help"))
        {
            Console.WriteLine(
                """
                Usage: SrmrPatcher.exe rebuild-metadata [options]

                Required options:
                  --game-assembly PATH      GameAssembly.dll input
                  --metadata PATH           Protected global-metadata.dat input
                  --output PATH             Canonical metadata output

                Optional flags:
                  --allow-hash-mismatch     Accept unsupported input hashes
                  --analyze-layout          Print layout-recovery diagnostics
                  --verbose                 Print decrypted protected-header words
                  --help                    Show this help
                """);
            return 0;
        }

        MetadataRebuilder.Rebuild(
            new MetadataRebuildOptions(
                commandLine.GetRequired("--game-assembly"),
                commandLine.GetRequired("--metadata"),
                commandLine.GetRequired("--output"),
                commandLine.HasFlag("--allow-hash-mismatch"),
                commandLine.HasFlag("--analyze-layout"),
                commandLine.HasFlag("--verbose")));
        return 0;
    }

    public static int BuildInfo(string[] args)
    {
        CommandLine commandLine = CommandLine.Parse(args, ["--game-root"], []);
        if (commandLine.HasFlag("--help"))
        {
            Console.WriteLine(
                """
                Usage: SrmrPatcher.exe build-info [options]

                Options:
                  --game-root PATH   Override the detected game folder
                  --help             Show this help
                """);
            return 0;
        }

        ToolPaths paths = ToolPaths.Create(commandLine.GetOptional("--game-root"));
        Console.WriteLine(BuildInfoReporter.CreateJson(paths));
        return 0;
    }

    public static int DumpMetadata(string[] args)
    {
        CommandLine commandLine = CommandLine.Parse(
            args,
            ["--process-id", "--output-path"],
            []);
        if (commandLine.HasFlag("--help"))
        {
            Console.WriteLine(
                """
                Usage: SrmrPatcher.exe dump-metadata [options]

                Required options:
                  --process-id ID      Running SRM-VR process ID
                  --output-path PATH   Metadata output path

                Optional flags:
                  --help               Show this help
                """);
            return 0;
        }

        if (!int.TryParse(commandLine.GetRequired("--process-id"), out int processId) ||
            processId <= 0)
        {
            throw new ArgumentException("--process-id must be a positive integer");
        }

        MetadataDumpResult result = MetadataDumper.Dump(
            processId,
            commandLine.GetRequired("--output-path"));
        ConsoleOutput.PrintSummary(result.AsSummary());
        return 0;
    }
}
