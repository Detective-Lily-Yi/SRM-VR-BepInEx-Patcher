using System.Text;
using System.Text.RegularExpressions;

namespace SrmrPatcher;

internal static partial class ConfigurationUpdater
{
    private const string MetadataSetting =
        "GlobalMetadataPath = {BepInEx}/cache/srm-vr-global-metadata.dat";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static void Update(string doorstopConfigPath, string bepInExConfigPath)
    {
        string doorstopText = File.ReadAllText(doorstopConfigPath);
        if (!DoorstopEnabledRegex().IsMatch(doorstopText))
        {
            throw new InvalidDataException(
                "doorstop_config.ini does not contain the expected enabled setting");
        }

        doorstopText = DoorstopEnabledRegex().Replace(doorstopText, "enabled = true");
        File.WriteAllText(doorstopConfigPath, doorstopText, Utf8WithoutBom);

        string configText;
        if (File.Exists(bepInExConfigPath))
        {
            configText = File.ReadAllText(bepInExConfigPath);
            string newline = configText.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n"
                : "\n";
            if (MetadataPathRegex().IsMatch(configText))
            {
                configText = MetadataPathRegex().Replace(configText, MetadataSetting);
            }
            else if (Il2CppSectionRegex().IsMatch(configText))
            {
                configText = Il2CppSectionRegex().Replace(
                    configText,
                    $"[IL2CPP]{newline}{newline}{MetadataSetting}",
                    count: 1);
            }
            else
            {
                configText = configText.TrimEnd() +
                    $"{newline}{newline}[IL2CPP]{newline}{newline}" +
                    MetadataSetting + newline;
            }
        }
        else
        {
            configText = $"[IL2CPP]\r\n\r\n{MetadataSetting}\r\n";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(bepInExConfigPath)!);
        File.WriteAllText(bepInExConfigPath, configText, Utf8WithoutBom);
    }

    [GeneratedRegex("^enabled\\s*=.*$", RegexOptions.Multiline)]
    private static partial Regex DoorstopEnabledRegex();

    [GeneratedRegex("^GlobalMetadataPath\\s*=.*$", RegexOptions.Multiline)]
    private static partial Regex MetadataPathRegex();

    [GeneratedRegex("^\\[IL2CPP\\]\\s*$", RegexOptions.Multiline)]
    private static partial Regex Il2CppSectionRegex();
}
