# Getting Started

## Requirements

- .NET 10 SDK
- Windows (the renderer currently requires DirectX 12 via ComputeSharp)

## Build and run

```bash
git clone https://github.com/Rex-J-W/DivisionEngine.git
cd DivisionEngine
dotnet build DivisionEngine.sln
dotnet run --project DivisionEngine/DivisionEngine.Editor.csproj
```

The Player project runs a packaged project without the editor:

```bash
dotnet run --project DivisionEngine.Player/DivisionEngine.Player.csproj -- <path-to-project>
```

> **Note:** `DivisionKey.snk` is not committed. Builds skip strong-name signing
> automatically when the key is missing (see `Directory.Build.targets`).

## ECS

Division Engine uses an entity-component-system architecture. A <xref:DivisionEngine.World>
owns entities, the components attached to them, and the systems that run each frame.

- **Entities** are IDs with components stored in dictionaries.
- **Components** implement <xref:DivisionEngine.IComponent> and are plain classes
  with public fields; the editor builds its inspector from those fields and their
  attributes (`[Range]`, `[Tooltip]`, `[Header]`, ...).
- **Systems** implement <xref:DivisionEngine.SystemBase> and run in a schedule:
  `AppStart` -> `Awake` -> `Update` / `FixedUpdate` -> `Render` -> `Unload` -> `AppExit`.
  Systems are discovered by reflection, so adding a `SystemBase` subclass is enough
  to register it.

```csharp
public class SpinSystem : SystemBase
{
    public override void Update()
    {
        foreach ((uint id, Transform transform) in W.QueryData<Transform>())
            transform.rotation = ...;
    }
}
```

## Project layout

```
Project/
  ProjectDatabase.divb
  ProjectFile.divp
  CurrentWorld.wld
  Assets/
    FolderMetadata.divmeta
    ...
```

## Where to go next

- [API Reference](../api/DivisionEngine.yml)
- [Audio System](audio.md)

