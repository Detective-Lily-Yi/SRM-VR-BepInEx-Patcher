using System.Text;

namespace SrmrPatcher;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string[] commandArgs = args[1..];
            return command switch
            {
                "install" => Commands.Install(commandArgs),
                "patch-doorstop" => Commands.PatchDoorstop(commandArgs),
                "patch-interop" => Commands.PatchInterop(commandArgs),
                "rebuild-metadata" => Commands.RebuildMetadata(commandArgs),
                "build-info" => Commands.BuildInfo(commandArgs),
                "dump-metadata" => Commands.DumpMetadata(commandArgs),
                _ => throw new ArgumentException($"Unknown command: {args[0]}")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            SRM-VR BepInEx compatibility patcher

            Usage:
              SrmrPatcher.exe <command> [options]

            Commands:
              install             Validate, stage, and install the complete fix
              patch-doorstop      Patch only the UnityDoorstop proxy
              patch-interop       Patch only the managed BepInEx assemblies
              rebuild-metadata    Reconstruct canonical IL2CPP metadata
              build-info          Print installed build information as JSON
              dump-metadata       Dump metadata from a running process

            Run a command with --help to see its options.
            """);
    }
}
