namespace SrmrPatcher;

internal sealed class ToolPaths
{
    private ToolPaths(string gameRoot)
    {
        GameRoot = Path.GetFullPath(gameRoot);
        ToolsDirectory = Path.Combine(GameRoot, "BepInEx", "tools");
    }

    public string GameRoot { get; }

    public string ToolsDirectory { get; }

    public string ExportMap => Path.Combine(ToolsDirectory, "srmr-il2cpp-export-map.csv");

    public string GameAssembly => Path.Combine(GameRoot, "GameAssembly.dll");

    public string UnityPlayer => Path.Combine(GameRoot, "UnityPlayer.dll");

    public string ProtectedMetadata => Path.Combine(
        GameRoot,
        "SRM-VR_Data",
        "il2cpp_data",
        "Metadata",
        "global-metadata.dat");

    public string RebuiltMetadata => Path.Combine(
        GameRoot,
        "BepInEx",
        "cache",
        "srm-vr-global-metadata.dat");

    public string DoorstopProxy => Path.Combine(GameRoot, "winhttp.dll");

    public string DoorstopConfig => Path.Combine(GameRoot, "doorstop_config.ini");

    public string BepInExConfig => Path.Combine(
        GameRoot,
        "BepInEx",
        "config",
        "BepInEx.cfg");

    public string RuntimeAssembly => Path.Combine(
        GameRoot,
        "BepInEx",
        "core",
        "Il2CppInterop.Runtime.dll");

    public string BepInExAssembly => Path.Combine(
        GameRoot,
        "BepInEx",
        "core",
        "BepInEx.Unity.IL2CPP.dll");

    public string BackupDirectory => Path.Combine(ToolsDirectory, "backups");

    public static ToolPaths Create(string? gameRoot = null)
    {
        string resolvedRoot = gameRoot ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", ".."));
        return new ToolPaths(resolvedRoot);
    }
}
