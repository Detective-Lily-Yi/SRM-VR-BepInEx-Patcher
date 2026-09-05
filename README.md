# Super Real Mahjong Venus Returns BepInEx compatibility fix

[![](https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/3718190/98ac8863e8362f32687934637fe7a776bff8b34b/library_header_japanese_2x.jpg)](https://store.steampowered.com/app/3718190/)

This package supports one exact game (スーパーリアル麻雀 Venus Returns / Stripjong - Super Real Mahjong Venus Returns / 脱衣麻将 - 超真实麻将 Venus Returns) and loader combination:

- SRM-VR build shipped on 2026-08-31
- Unity `2022.3.62f2`, Windows x64, IL2CPP
- BepInEx `6.0.0-be.788+5b766a3`, Unity.IL2CPP win-x64
- `GameAssembly.dll` SHA-256: `C721E373641239C9D75DBCD58E75BE89DBBF6D507D3A421ACE3754B4082272E3`
- `UnityPlayer.dll` SHA-256: `51DA2A05C3DC2BFFB4BEB43D6249C5C52DC702D250986DC5FB319120F1DFE938`
- protected metadata SHA-256: `8A8C41F65145C50DC1796BB2BB6221FE4110FF8199A059DF6144558726E46506`

The installer checks these hashes. If one does not match, it displays the
actual and expected hashes and asks whether to continue.

## Install

1. Download the official BepInEx nightly build from
   <https://builds.bepinex.dev/projects/bepinex_be> and select build 788,
   `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip`.
2. Extract BepInEx into the SRM-VR folder, beside `SRM-VR.exe`.
3. Extract the compatibility-fix ZIP into that same folder. Allow it to create
   `BepInEx\tools`.
4. From the game folder, run the self-contained patcher:

   ```console
   .\BepInEx\tools\SrmrPatcher.exe install
   ```

   When a hash does not match, review the warning and enter `y` only if you
   want to try the patch on that unsupported file. For unattended use,
   `--continue-on-hash-mismatch` accepts that risk without an interactive prompt;
   warnings are still displayed.

5. Start the game from Steam. Confirm that `BepInEx\LogOutput.log` contains
   `Chainloader startup complete`.

The first launch can take longer while BepInEx downloads the matching Unity
libraries and creates `BepInEx\interop`. Put mod DLLs in `BepInEx\plugins`.

## Patcher commands

`SrmrPatcher.exe` is a self-contained Windows x64 .NET application.

- `install` validates, stages, installs, and configures the complete fix.
- `patch-doorstop` patches only the UnityDoorstop proxy.
- `patch-interop` patches only the two managed BepInEx assemblies.
- `rebuild-metadata` reconstructs the canonical metadata file.
- `build-info` prints the installed-build report as JSON.
- `dump-metadata` is the optional process-memory diagnostic command.

Run `SrmrPatcher.exe --help` or `SrmrPatcher.exe <command> --help` for the
complete command-line interface.

## What the installer changes

The installer first creates and validates staged copies. It then changes only:

| Path                                       | Change                                                                  |
| ------------------------------------------ | ----------------------------------------------------------------------- |
| `winhttp.dll`                              | Remaps UnityDoorstop's three IL2CPP bootstrap exports.                  |
| `BepInEx\core\Il2CppInterop.Runtime.dll`   | Remaps 195 native imports and seven dynamic export lookups.             |
| `BepInEx\core\BepInEx.Unity.IL2CPP.dll`    | Remaps its remaining direct `il2cpp_runtime_invoke` lookup.             |
| `BepInEx\cache\srm-vr-global-metadata.dat` | Reconstructs standard v31 metadata from this user's installed game.     |
| `BepInEx\config\BepInEx.cfg`               | Sets `GlobalMetadataPath = {BepInEx}/cache/srm-vr-global-metadata.dat`. |
| `doorstop_config.ini`                      | Ensures `enabled = true`.                                               |

`redirect_output_log = true` is useful for diagnosis but is not required.
Leave `UpdateInteropAssemblies = true`, which is the BepInEx default.

Original loader files are saved under `BepInEx\tools\backups`.

## Reinstall versus a game update

You can rerun `SrmrPatcher.exe install` after reinstalling the same BepInEx
build. It is idempotent and regenerates the metadata and patched files for the
currently supported SRM-VR build.

However, A game update can change all randomized export
names, the Unity native layout, and the metadata protection algorithm. On an
unknown hash the installer warns and requires explicit confirmation before it
continues. Confirmation does not make an unknown build compatible. Supporting
a new build still requires a new export map and metadata profile, followed
by new input/output hashes in the installer and manifest.

## How the compatibility work was discovered

1. The game was confirmed as Windows x64, Unity `2022.3.62f2`, and IL2CPP. The
   selected BepInEx package was therefore correct.
2. A verbose UnityDoorstop build showed that proxy loading worked, but its
   lookup for the normal `il2cpp_init` export failed.
3. Inspection of `GameAssembly.dll` showed 241 randomized 11-character exports
   instead of the normal `il2cpp_*` API. Comparison with an unprotected
   `UnityPlayer.dll` from the exact same Unity revision recovered 234 mappings
   by corresponding string slots. Seven remaining profiler mappings were
   resolved by code/semantic comparison.
4. The three names needed by UnityDoorstop were identified as:

   ```text
   il2cpp_init             -> QsYNGHXuNss
   il2cpp_runtime_invoke   -> UWfOjjKmHmS
   il2cpp_method_get_name  -> NBXVYHQNAsj
   ```

5. Once Doorstop could start CoreCLR, Cpp2IL failed because the on-disk metadata
   had a custom header and protected sections rather than standard IL2CPP magic.
6. Static analysis of the game's metadata loader recovered its header transform,
   substitution table, table layout, and seven separately encrypted sections.
   The patcher reproduces that logic offline and constructs canonical v31
   metadata. This was not a dump of a contiguous plaintext process image.
7. Finally, the full map was applied to Il2CppInterop's native imports and
   runtime lookups, plus BepInEx's one direct lookup. No game binary was patched.
8. Validation succeeded when Cpp2IL consumed the rebuilt metadata, generated
   233 interop assemblies, and the log reached `Chainloader startup complete`
   without fatal or error entries.

The two normal Unity liveness imports that remain named in the managed assembly
are not exported by this Unity build and are not used in ordinary startup.

## Build from source

The project targets .NET 10 and references the Mono.Cecil assembly from the
installed BepInEx build:

```console
dotnet publish .\BepInEx\tools\src\SrmrPatcher\SrmrPatcher.csproj --configuration Release
```
