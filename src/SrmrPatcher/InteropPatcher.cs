using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace SrmrPatcher
{
    internal sealed record InteropPatchResult(
        string RuntimeAssembly,
        string RuntimeBackup,
        int PatchedEntryPoints,
        int AlreadyPatchedEntryPoints,
        int PatchedDynamicEntryPoints,
        int AlreadyPatchedDynamicEntryPoints,
        IReadOnlyList<string> UnmappedEntryPoints,
        string BepInExAssembly,
        string BepInExBackup,
        int PatchedBepInExLookups,
        int AlreadyPatchedBepInExLookups)
    {
        public IEnumerable<KeyValuePair<string, object?>> AsSummary()
        {
            yield return new("Runtime assembly", RuntimeAssembly);
            yield return new("Runtime backup", RuntimeBackup);
            yield return new("Patched entry points", PatchedEntryPoints);
            yield return new("Already patched entry points", AlreadyPatchedEntryPoints);
            yield return new("Patched dynamic entry points", PatchedDynamicEntryPoints);
            yield return new("Already patched dynamic entry points", AlreadyPatchedDynamicEntryPoints);
            yield return new("Unmapped entry points", string.Join(", ", UnmappedEntryPoints));
            yield return new("BepInEx assembly", BepInExAssembly);
            yield return new("BepInEx backup", BepInExBackup);
            yield return new("Patched BepInEx lookups", PatchedBepInExLookups);
            yield return new("Already patched BepInEx lookups", AlreadyPatchedBepInExLookups);
        }
    }

    internal static class InteropPatcher
    {
        public static InteropPatchResult Patch(
            string runtimePath,
            string bepInExPath,
            ExportMap exportMap,
            string runtimeBackupPath,
            string bepInExBackupPath)
        {
            runtimePath = Path.GetFullPath(runtimePath);
            bepInExPath = Path.GetFullPath(bepInExPath);
            EnsureFileExists(runtimePath);
            EnsureFileExists(bepInExPath);
            CreateBackup(runtimePath, runtimeBackupPath);
            CreateBackup(bepInExPath, bepInExBackupPath);

            RuntimePatchCounts runtimeCounts = PatchRuntime(
                runtimePath,
                exportMap.Names,
                exportMap.ProtectedNames);
            BepInExPatchCounts bepInExCounts = PatchBepInEx(
                bepInExPath,
                exportMap.Names,
                exportMap.ProtectedNames);

            return new InteropPatchResult(
                runtimePath,
                runtimeBackupPath,
                runtimeCounts.Patched,
                runtimeCounts.AlreadyPatched,
                runtimeCounts.PatchedDynamic,
                runtimeCounts.AlreadyPatchedDynamic,
                runtimeCounts.Unmapped,
                bepInExPath,
                bepInExBackupPath,
                bepInExCounts.Patched,
                bepInExCounts.AlreadyPatched);
        }

        private static RuntimePatchCounts PatchRuntime(
            string path,
            IReadOnlyDictionary<string, string> exportMap,
            IReadOnlySet<string> protectedNames)
        {
            string temporaryPath = path + ".srmr-patched";
            int patched = 0;
            int alreadyPatched = 0;
            int patchedDynamic = 0;
            int alreadyPatchedDynamic = 0;
            var unmapped = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);
                foreach (TypeDefinition type in assembly.MainModule.Types)
                {
                    foreach (MethodDefinition method in type.Methods)
                    {
                        if (method.HasBody)
                        {
                            Collection<Instruction> instructions = method.Body.Instructions;
                            for (int index = 0; index < instructions.Count; index++)
                            {
                                Instruction instruction = instructions[index];
                                if (instruction.OpCode.Code != Code.Ldstr ||
                                    !IsDynamicExportLookup(instructions, index))
                                {
                                    continue;
                                }

                                string dynamicName = (string)instruction.Operand;
                                if (exportMap.TryGetValue(dynamicName, out string? protectedName))
                                {
                                    instruction.Operand = protectedName;
                                    patchedDynamic++;
                                }
                                else if (protectedNames.Contains(dynamicName))
                                {
                                    alreadyPatchedDynamic++;
                                }
                            }
                        }

                        if (!method.IsPInvokeImpl ||
                            method.PInvokeInfo.Module.Name != "GameAssembly")
                        {
                            continue;
                        }

                        string entryPoint = method.PInvokeInfo.EntryPoint;
                        if (exportMap.TryGetValue(entryPoint, out string? protectedEntryPoint))
                        {
                            method.PInvokeInfo.EntryPoint = protectedEntryPoint;
                            patched++;
                        }
                        else if (protectedNames.Contains(entryPoint))
                        {
                            alreadyPatched++;
                        }
                        else
                        {
                            unmapped.Add(entryPoint);
                        }
                    }
                }

                if (patched == 0 && alreadyPatched == 0)
                {
                    throw new InvalidDataException(
                        "No mapped GameAssembly P/Invoke entry points were found " +
                        "in the runtime assembly");
                }

                if (patched + patchedDynamic > 0)
                {
                    assembly.Write(temporaryPath);
                }
            }
            catch
            {
                File.Delete(temporaryPath);
                throw;
            }

            if (patched + patchedDynamic > 0)
            {
                File.Move(temporaryPath, path, overwrite: true);
            }

            return new RuntimePatchCounts(
                patched,
                alreadyPatched,
                patchedDynamic,
                alreadyPatchedDynamic,
                unmapped.Order(StringComparer.Ordinal).ToArray());
        }

        private static bool IsDynamicExportLookup(
            Collection<Instruction> instructions,
            int stringIndex)
        {
            int lastLookAhead = Math.Min(instructions.Count - 1, stringIndex + 3);
            for (int index = stringIndex + 1; index <= lastLookAhead; index++)
            {
                Instruction instruction = instructions[index];
                if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt))
                {
                    continue;
                }

                string calledMethod = ((MethodReference)instruction.Operand).FullName;
                return calledMethod.Contains("InjectorHelpers::GetIl2CppExport", StringComparison.Ordinal) ||
                       calledMethod.Contains("InjectorHelpers::TryGetIl2CppExport", StringComparison.Ordinal);
            }

            return false;
        }

        private static BepInExPatchCounts PatchBepInEx(
            string path,
            IReadOnlyDictionary<string, string> exportMap,
            IReadOnlySet<string> protectedNames)
        {
            string temporaryPath = path + ".srmr-patched";
            int patched = 0;
            int alreadyPatched = 0;

            try
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);
                foreach (TypeDefinition type in assembly.MainModule.Types)
                {
                    foreach (MethodDefinition method in type.Methods.Where(method => method.HasBody))
                    {
                        Collection<Instruction> instructions = method.Body.Instructions;
                        for (int index = 0; index < instructions.Count - 1; index++)
                        {
                            Instruction instruction = instructions[index];
                            Instruction nextInstruction = instructions[index + 1];
                            if (instruction.OpCode.Code != Code.Ldstr ||
                                nextInstruction.OpCode.Code != Code.Call ||
                                nextInstruction.Operand is not MethodReference calledMethod ||
                                !calledMethod.FullName.Contains(
                                    "System.Runtime.InteropServices.NativeLibrary::GetExport",
                                    StringComparison.Ordinal))
                            {
                                continue;
                            }

                            string exportName = (string)instruction.Operand;
                            if (exportMap.TryGetValue(exportName, out string? protectedName))
                            {
                                instruction.Operand = protectedName;
                                patched++;
                            }
                            else if (protectedNames.Contains(exportName))
                            {
                                alreadyPatched++;
                            }
                        }
                    }
                }

                if (patched + alreadyPatched == 0)
                {
                    throw new InvalidDataException(
                        "No mapped NativeLibrary.GetExport call was found " +
                        "in BepInEx.Unity.IL2CPP");
                }

                if (patched > 0)
                {
                    assembly.Write(temporaryPath);
                }
            }
            catch
            {
                File.Delete(temporaryPath);
                throw;
            }

            if (patched > 0)
            {
                File.Move(temporaryPath, path, overwrite: true);
            }

            return new BepInExPatchCounts(patched, alreadyPatched);
        }

        private static void CreateBackup(string source, string backup)
        {
            if (File.Exists(backup))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(source, backup, overwrite: false);
        }

        private static void EnsureFileExists(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Required file does not exist", path);
            }
        }

        private sealed record RuntimePatchCounts(
            int Patched,
            int AlreadyPatched,
            int PatchedDynamic,
            int AlreadyPatchedDynamic,
            IReadOnlyList<string> Unmapped);

        private sealed record BepInExPatchCounts(int Patched, int AlreadyPatched);
    }
}
