<div align="left">

<img width="128" height="128" alt="DivisionEngineLogoR" src="https://github.com/user-attachments/assets/4495b5e3-012d-42e4-8e04-1eecbf22ee1d" />

# Division Engine

[![Version](https://img.shields.io/github/v/release/DivisionEngine/DivisionEngine?label=Version&include_prereleases)](https://github.com/DivisionEngine/DivisionEngine/releases)
[![License](https://img.shields.io/github/license/DivisionEngine/DivisionEngine)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![AvaloniaUI](https://img.shields.io/badge/Avalonia-UI-blue)](https://avaloniaui.net/)

</div>

Division Engine is an SDF-based game engine written entirely in C#. Utilizing Avalonia UI for the interface and Silk.NET for native rendering, Division Engine features a comprehensive shader pipeline that dynamically builds HLSL shader code from .NET code, thanks to [DivisionCompute](https://github.com/NorthRocStudios/DivisionCompute).

*Note: This engine is still in preview and has known issues; it is specifically built for experimentation and education only.*

Rendering is compute-driven: shaders are written as ordinary C# structs, translated to HLSL on demand by DivisionCompute, and dispatched through its Direct3D 11 backend, while Silk.NET's OpenGL context presents the finished frame. The vector and matrix types shared by CPU code and shaders come from [DivisionMath](https://github.com/NorthRocStudios/DivisionMath).

Picture this:
- SDF-based rendering
- GPU compute acceleration in C#
- Open source
- ECS data-oriented setup
- Convenient editor tooling

## Documentation

Feature documentation and API guides live in the [Division Engine wiki](https://github.com/NorthRocStudios/DivisionEngine/wiki):

- **Getting started:** [Editor Overview](https://github.com/NorthRocStudios/DivisionEngine/wiki/Editor-Overview), [Projects and Assets](https://github.com/NorthRocStudios/DivisionEngine/wiki/Projects-and-Assets), [ECS and Components](https://github.com/NorthRocStudios/DivisionEngine/wiki/ECS-and-Components), [Scripting](https://github.com/NorthRocStudios/DivisionEngine/wiki/Scripting), [Division CLI](https://github.com/NorthRocStudios/DivisionEngine/wiki/Division-CLI)
- **Scene:** [SDF Modeling](https://github.com/NorthRocStudios/DivisionEngine/wiki/SDF-Modeling), [Materials and Textures](https://github.com/NorthRocStudios/DivisionEngine/wiki/Materials-and-Textures), [Lighting and Environment](https://github.com/NorthRocStudios/DivisionEngine/wiki/Lighting-and-Environment), [Cameras and Post Processing](https://github.com/NorthRocStudios/DivisionEngine/wiki/Cameras-and-Post-Processing), [Area Lights](https://github.com/NorthRocStudios/DivisionEngine/wiki/Area-Lights), [Terrain System](https://github.com/NorthRocStudios/DivisionEngine/wiki/Terrain-System)
- **Rendering:** [Rendering Pipeline](https://github.com/NorthRocStudios/DivisionEngine/wiki/Rendering-Pipeline), [Voxel SDF Rendering](https://github.com/NorthRocStudios/DivisionEngine/wiki/Voxel-SDF-Rendering), [Cone-Traced Supersampling](https://github.com/NorthRocStudios/DivisionEngine/wiki/Cone-Traced-Supersampling), [Reflection and Refraction Denoising](https://github.com/NorthRocStudios/DivisionEngine/wiki/Reflection-and-Refraction-Denoising)
- **Editor tools:** [Node Editor](https://github.com/NorthRocStudios/DivisionEngine/wiki/Node-Editor), [Dockable Editor Windows](https://github.com/NorthRocStudios/DivisionEngine/wiki/Dockable-Editor-Windows)
- **API:** [DivisionMath and DivisionCompute](https://github.com/NorthRocStudios/DivisionEngine/wiki/DivisionMath-and-DivisionCompute), [API Overview](https://github.com/NorthRocStudios/DivisionEngine/wiki/API-Overview), [ECS API](https://github.com/NorthRocStudios/DivisionEngine/wiki/ECS-API), [Components API](https://github.com/NorthRocStudios/DivisionEngine/wiki/Components-API), [Rendering API](https://github.com/NorthRocStudios/DivisionEngine/wiki/Rendering-API), [Node Graph API](https://github.com/NorthRocStudios/DivisionEngine/wiki/Node-Graph-API), [Projects and Scripting API](https://github.com/NorthRocStudios/DivisionEngine/wiki/Projects-and-Scripting-API), [Runtime Services API](https://github.com/NorthRocStudios/DivisionEngine/wiki/Runtime-Services-API)

## What Are SDFs?

*Signed Distance Fields* are spatial fields that store information represented as a grid sampling of the closest distance to the surface of an object defined as a polygonal model. Usually, the convention of using negative values inside the object and positive values outside the object is applied. Signed distance fields are important in computer graphics and related fields. Often, they are used for collision detection in cloth animation, soft-body physics effects, malleable geometry, volumetric effects, and fluid simulation.
(https://developer.nvidia.com/gpugems/gpugems3/part-v-physics-simulation/chapter-34-signed-distance-fields-using-single-pass-gpu)

## How to Work with ECS

*ECS* or an entity-component-system framework is a way of organizing game data such that it is memory efficient and hyper-performant. Entities are simply IDs with components stored as a dictionary in an "ECS World" object. Systems are code files written that operate on an awake --> update --> fixed update --> render schedule, allowing components to be manipulated during different engine loops/stages. For more information on ECS, check out how the Unity game engine implemented its ECS framework here: https://unity.com/ecs

## Framework

Division Engine is built on four core packages: Silk.NET, DivisionCompute, DivisionMath, and AvaloniaUI.
Check them out here:
- [Silk.NET](https://github.com/dotnet/Silk.NET)
- [DivisionCompute](https://github.com/NorthRocStudios/DivisionCompute) - C# to HLSL source-to-source compiler and Direct3D 11 compute runtime
- [DivisionMath](https://github.com/NorthRocStudios/DivisionMath) - vectorized math library built for near 1:1 parity with HLSL
- [AvaloniaUI](https://github.com/AvaloniaUI/Avalonia)

> **A note on ComputeSharp:** earlier previews of Division Engine built shaders with [ComputeSharp](https://github.com/Sergio0694/ComputeSharp). The engine has since migrated to DivisionCompute and DivisionMath: shader translation, bytecode compilation, resource binding, and dispatch all live in DivisionCompute, and no engine project references ComputeSharp anymore. The new stack targets Direct3D 11 (HLSL compiled to DXBC at runtime, cached on disk between runs) and shares one math API between CPU and GPU code.

## Resources

Follow the development: https://trello.com/b/mWtyHBMf/division-engine

Tutorials by Inigo Quilez (Not sponsored, just useful for learning constructive geometry):
- Build mathematical worlds: https://youtu.be/0ifChJ0nJfM?si=ypKU1rz-8JloPlj2
- Build a 3D landscape: https://youtu.be/BFld4EBO2RE?si=EASXvq-ez2qBOIHN
- Paint a 3D character with math: https://youtu.be/8--5LwHRhjk?si=fH9QwvCz6dLptHE1

## Editor Preview Screenshots

Reflective Menger Sponge:
<img width="1919" height="1031" alt="Screenshot 2026-11-09 021502" src="https://github.com/user-attachments/assets/6044d156-ba3c-4a32-a943-e6a2c83050a9" />

Reflective and Refractive Glass Objects:
<img width="1919" height="1029" alt="Screenshot 2026-02-12 015857" src="https://github.com/user-attachments/assets/7fe0f9b6-5a65-4160-ad74-874034a14ee6" />

Texturing System:
<img width="1919" height="1029" alt="Screenshot 2026-07-18 013607" src="https://github.com/user-attachments/assets/86c5ae53-8e12-4164-98dd-418f4ff5bae1" />

Multiple Workflows:
<img width="1919" height="1028" alt="Screenshot 2026-09-02 020709" src="https://github.com/user-attachments/assets/353f6bb6-527a-4336-99b2-01b55e936383" />

## License

Division Engine is free and open-source software licensed under the **MIT License**. You can use it commercially, modify it, and redistribute it — including inside closed-source games and tools — with no obligation to publish your own code. Keep the copyright notice, and please don't use the NorthRoc Studios or Division Engine names to promote a fork.

See [LICENSE.txt](LICENSE.txt) for the full terms and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for bundled third-party components. The terrain erosion filter in `TerrainRendering.cs` contains MPL-2.0 code from Rune Skovbo Johansen; everything else is MIT.
