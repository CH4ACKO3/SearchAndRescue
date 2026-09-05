# Search and Rescue

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3796056278) · RimWorld 1.6 · Alpha · [MIT License](LICENSE)

Coordinate battlefield treatment, evacuation, capture, and medical supplies through a dedicated **Field Rescue** work type.

- **Treat** casualties where they fall.
- **Rescue** them to a bed or a designated rescue point.
- **Capture** downed hostile humanlikes without requiring a prisoner bed first.

Use **Orders → Search and rescue** to mark casualties. Responders prioritize patients by injury, distance, skill, and work priorities. Manual orders take precedence; drafted pawns are not assigned.

Subscribe on Steam Workshop and enable **Harmony**. Supports English, 简体中文, and 繁體中文.

[Compatibility](Docs/compatibility/README.md) · [兼容性](Docs/compatibility/README.zh-CN.md) · [Changelog](CHANGELOG.md) · [Development & testing](Docs/README.md)

## Build

Requires the .NET SDK, .NET Framework 4.8 targeting support, and your own RimWorld/Harmony assemblies:

```powershell
dotnet build Source/SearchAndRescue/SearchAndRescue.csproj -c Release `
  -p:RimWorldManagedDir="C:\Games\RimWorld\RimWorldWin64_Data\Managed" `
  -p:HarmonyAssemblyPath="C:\Games\Harmony\Current\Assemblies\0Harmony.dll"
```

Adjust the paths to your installation. Output: `Assemblies/SearchAndRescue.dll`. Compiled binaries are not committed.

Run the standalone matching regressions with .NET 8:

```shell
dotnet run --project Tools/SchedulerSimulation/SchedulerSimulation.csproj -c Release
```
