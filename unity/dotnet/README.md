# Exporter Core: development builds outside Unity

The avatar exporter (`unity/Packages/com.milkjugg24.factorio-avatar-exporter/`) is split in two:

- `Core/`: everything that decides what to draw and writes the pack: poses, camera, matting, sheets,
  Lua and PNG writers. It must not reference UnityEngine (its asmdef sets `noEngineReferences`), so it
  builds and is tested here.
- `Editor/`: the thin Unity layer that clones and poses the avatar and renders frames.

The projects here compile the package's `Core/*.cs` the way Unity 2022.3 does (C# 9, .NET Standard 2.1)
and test them with NUnit.

```sh
dotnet test unity/dotnet/Core.Tests          # needs the .NET 8 SDK
```

| Project | What it is |
| --- | --- |
| `Core/` | The package's Core sources as a `netstandard2.1` library. |
| `Core.Tests/` | NUnit tests (`net8.0`). `Goldens/` holds reference data written by Python tools. |

Rules for Core code, because Unity compiles it too:

- C# 9 without records or `init` setters (Unity's .NET Standard 2.1 profile lacks `IsExternalInit`).
- Only .NET Standard 2.1 APIs: no `System.Text.Json`, no `ZLibStream`.
- Doubles for geometry (`Vec3`, `Quat`); the Unity layer converts to float.
- Invariant culture whenever numbers become text.
