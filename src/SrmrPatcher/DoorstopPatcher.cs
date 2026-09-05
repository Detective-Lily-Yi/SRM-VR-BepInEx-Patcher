using System.Text;

namespace SrmrPatcher;

internal sealed record DoorstopPatchResult(
    string DoorstopPath,
    string? BackupCreated,
    IReadOnlyList<string> PatchedNames,
    IReadOnlyList<string> AlreadyPatchedNames)
{
    public IEnumerable<KeyValuePair<string, object?>> AsSummary()
    {
        yield return new("Doorstop", DoorstopPath);
        yield return new("Backup created", BackupCreated);
        yield return new("Patched names", string.Join(", ", PatchedNames));
        yield return new("Already patched names", string.Join(", ", AlreadyPatchedNames));
    }
}

internal static class DoorstopPatcher
{
    private static readonly string[] RequiredExports =
    [
        "il2cpp_init",
        "il2cpp_runtime_invoke",
        "il2cpp_method_get_name"
    ];

    public static DoorstopPatchResult Patch(
        string doorstopPath,
        ExportMap exportMap,
        string? backupPath = null)
    {
        doorstopPath = Path.GetFullPath(doorstopPath);
        if (!File.Exists(doorstopPath))
        {
            throw new FileNotFoundException("Doorstop proxy does not exist", doorstopPath);
        }

        byte[] data = File.ReadAllBytes(doorstopPath);
        var replacements = new List<(string Canonical, string Protected, int Offset)>();
        var alreadyPatched = new List<string>();
        foreach (string canonicalName in RequiredExports)
        {
            if (!exportMap.Names.TryGetValue(canonicalName, out string? protectedName))
            {
                throw new InvalidDataException($"Missing export mapping for {canonicalName}");
            }

            IReadOnlyList<int> canonicalPositions = FindCString(data, canonicalName);
            if (canonicalPositions.Count == 1)
            {
                if (protectedName.Length > canonicalName.Length)
                {
                    throw new InvalidDataException(
                        $"Protected name {protectedName} does not fit in {canonicalName}");
                }

                replacements.Add((canonicalName, protectedName, canonicalPositions[0]));
            }
            else if (canonicalPositions.Count > 1)
            {
                throw new InvalidDataException(
                    $"Found multiple exact {canonicalName} strings in {doorstopPath}");
            }
            else if (FindCString(data, protectedName).Count > 0)
            {
                alreadyPatched.Add(canonicalName);
            }
            else
            {
                throw new InvalidDataException(
                    $"Could not find either {canonicalName} or {protectedName} " +
                    $"in {doorstopPath}");
            }
        }

        string? backupCreated = null;
        if (replacements.Count > 0)
        {
            if (backupPath is not null && !File.Exists(backupPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(doorstopPath, backupPath, overwrite: false);
                backupCreated = backupPath;
            }

            foreach ((string canonicalName, string protectedName, int offset) in replacements)
            {
                Span<byte> target = data.AsSpan(offset, canonicalName.Length);
                target.Clear();
                Encoding.ASCII.GetBytes(protectedName, target);
            }

            File.WriteAllBytes(doorstopPath, data);
        }

        return new DoorstopPatchResult(
            doorstopPath,
            backupCreated,
            replacements.Select(item => item.Canonical).ToArray(),
            alreadyPatched);
    }

    private static IReadOnlyList<int> FindCString(byte[] data, string value)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(value + '\0');
        var positions = new List<int>();
        for (int offset = 0; offset <= data.Length - pattern.Length; offset++)
        {
            if (data.AsSpan(offset, pattern.Length).SequenceEqual(pattern))
            {
                positions.Add(offset);
            }
        }

        return positions;
    }
}
