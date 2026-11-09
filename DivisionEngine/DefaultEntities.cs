//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Components.Audio;
using DivisionEngine.Components.Lights;
using DivisionEngine.Components.SDFs;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Components.SDFs.Fractals;
using DivisionEngine.Components.SDFs.Primitives;
using DivisionEngine.Editor.Undo;
using Environment = DivisionEngine.Components.Environment;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Utility used for creating default entities from the editor.
    /// </summary>
    public static class DefaultEntities
    {
        public static uint Empty(string name = "New Entity")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint entity = W.CreateEntity(name);
            var components = WorldManager.CurrentWorld!.GetClonedComponents(entity);
            AddEntityCommand command = new AddEntityCommand(entity, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(entity);
            return entity;
        }

        public static uint EmptyTransform(string name = "New Entity")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint entity = W.CreateTransformEntity(name);
            var components = WorldManager.CurrentWorld!.GetClonedComponents(entity);
            AddEntityCommand command = new AddEntityCommand(entity, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(entity);
            return entity;
        }

        public static uint Camera(string name = "New Camera", bool hasPlayerControls = true)
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint camera = W.CreateTransformEntity(name);
            W.AddComponent(camera, new Camera());
            if (hasPlayerControls) W.AddComponent(camera, new Player());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(camera);
            AddEntityCommand command = new AddEntityCommand(camera, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(camera);
            return camera;
        }

        public static uint Environment(string name = "New Environment")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint environment = W.CreateEntity(name);
            W.AddComponent(environment, new Environment());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(environment);
            AddEntityCommand command = new AddEntityCommand(environment, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(environment);
            return environment;
        }

        public static uint DirectionalLight(string name = "New Directional Light")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint directionalLight = W.CreateTransformEntity(name);
            W.AddComponent(directionalLight, new DirectionalLight());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(directionalLight);
            AddEntityCommand command = new AddEntityCommand(directionalLight, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(directionalLight);
            return directionalLight;
        }

        public static uint PointLight(string name = "New Point Light")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint pointLight = W.CreateTransformEntity(name);
            W.AddComponent(pointLight, new PointLight());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(pointLight);
            AddEntityCommand command = new AddEntityCommand(pointLight, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(pointLight);
            return pointLight;
        }

        public static uint AudioSource(string name = "New Audio Source")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint audioSource = W.CreateTransformEntity(name);
            W.AddComponent(audioSource, new AudioSource());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(audioSource);
            AddEntityCommand command = new AddEntityCommand(audioSource, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(audioSource);
            return audioSource;
        }

        public static uint AudioListener(string name = "New Audio Listener")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint listener = W.CreateTransformEntity(name);
            W.AddComponent(listener, new AudioListener());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(listener);
            AddEntityCommand command = new AddEntityCommand(listener, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(listener);
            return listener;
        }

        public static uint SDFSphere(string name = "New Sphere")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFSphere());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        /// <summary>
        /// Uniform scale applied to freshly created fractals so they read at a
        /// similar size to the primitive shapes next to them.
        /// </summary>
        private const float FractalDefaultScale = 1.75f;

        /// <summary>
        /// Creates an entity with a transform and the supplied components, registers it
        /// with the undo stack and selects it. Unity equivalent: GameObject + AddComponent.
        /// </summary>
        private static uint CreateEntity(string name, params IComponent[] components)
        {
            World? world = WorldManager.RequireCurrentWorld();
            if (world == null) return uint.MaxValue;

            uint entity = W.CreateTransformEntity(name);
            foreach (IComponent component in components) W.AddComponent(entity, component);

            var cloned = world.GetClonedComponents(entity);
            UndoManager.Execute(new AddEntityCommand(entity, cloned, name));
            Selection.SelectEntity(entity);
            return entity;
        }

        /// <summary>
        /// Creates a fractal entity with the default material/shadow components and
        /// the uniform scale shared by every fractal preset.
        /// </summary>
        private static uint CreateFractal(string name, IComponent fractal)
        {
            uint entity = CreateEntity(name, fractal, new SDFMaterial(), new Shadows());
            if (entity != uint.MaxValue)
                W.GetComponent<Transform>(entity)!.scaling = new float3(FractalDefaultScale, FractalDefaultScale, FractalDefaultScale);
            return entity;
        }
        public static uint SDFMandelbulb(string name = "New Mandelbulb") => CreateFractal(name, new SDFMandelbulb());

        public static uint SDFMengerSponge(string name = "New Menger Sponge") => CreateFractal(name, new SDFMengerSponge());

        public static uint SDFMandelbox(string name = "New Mandelbox") => CreateFractal(name, new SDFMandelbox());

        public static uint SDFBox(string name = "New Box")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFBox());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        public static uint SDFRoundedBox(string name = "New Rounded Box")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFRoundedBox());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        public static uint SDFTorus(string name = "New Donut")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFTorus());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        public static uint SDFPyramid(string name = "New Pyramid")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFPyramid());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        public static uint SDFPlane(string name = "New Plane")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFPlane());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        public static uint SDFCylinder(string name = "New Cylinder")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFCylinder());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        public static uint SDFCapsule(string name = "New Capsule")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFCapsule());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        public static uint SDFCone(string name = "New Cone")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFCone());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }

        public static uint Terrain(string name = "New Terrain")
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("No world is currently loaded to add entities to");
                return uint.MaxValue;
            }
            uint sdf = W.CreateTransformEntity(name);
            W.AddComponent(sdf, new SDFTerrain());
            W.AddComponent(sdf, new SDFMaterial());
            W.AddComponent(sdf, new Shadows());
            var components = WorldManager.CurrentWorld!.GetClonedComponents(sdf);
            AddEntityCommand command = new AddEntityCommand(sdf, components, name);
            UndoManager.Execute(command);
            Selection.SelectEntity(sdf);
            return sdf;
        }
    }
}



