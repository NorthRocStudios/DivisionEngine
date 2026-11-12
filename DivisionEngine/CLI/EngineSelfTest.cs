//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Editor.Undo;
using DivisionEngine.MathLib;
using DivisionEngine.Projects;
using DivisionEngine.Projects.Assets;
using DivisionEngine.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Headless regression checks for engine behaviour that has no visible surface.
    /// Invoked as <c>DivisionEngine.Editor.exe --cli selftest</c>.
    /// </summary>
    /// <remarks>
    /// These cover the fixes that are easy to regress and cheap to verify without a GPU or a
    /// window: locale-independent serialization, matrix accessors, world snapshots, undo
    /// trimming/coalescing, the play-mode single-step gate, and terrain noise range.
    /// </remarks>
    internal static class EngineSelfTest
    {
        private static int passed;
        private static int failed;

        internal static bool Run()
        {
            passed = 0;
            failed = 0;

            Console.WriteLine("Division Engine self test");

            // Runs first: nothing has touched the editor selection yet.
            Check("Selection defaults to no entity", () => Selection.Entity == uint.MaxValue);
            Check("Selection defaults to SelectionType.None", () => Selection.SelectedType == SelectionType.None);

            Check("Play mode can be single-stepped while paused", CheckStepWhilePaused);

            Check("Vector serialization is culture independent", CheckCultureRoundTrip);
            Check("Matrix accessors write through", CheckMatrixAccessors);
            Check("World snapshot preserves entity ids", CheckSnapshotIds);
            Check("World snapshot leaves the live editor camera alone", CheckSnapshotEditorCamera);
            Check("Restore keeps entity ids stable", CheckRestoreIds);
            Check("Undo keeps the newest commands on top", CheckUndoTrimming);
            Check("Undo coalesces repeated edits of one field", CheckUndoCoalescing);
            Check("Terrain noise spans a usable range", CheckTerrainNoise);
            Check("Asset types: .cs/.hlsl/.sdfLib", CheckAssetTypes);
            Check("World queries survive mutation while enumerating", CheckQuerySnapshot);
            Check("Log list is safe under concurrent writes", CheckLogConcurrency);

            Console.WriteLine();
            Console.WriteLine($"{{\"checks\":{passed + failed},\"passed\":{passed},\"failed\":{failed}}}");
            Console.WriteLine(failed == 0 ? "selftest: PASS" : "selftest: FAIL");

            DivisionCLI.SetExitCode(failed == 0 ? 0 : 1);
            return true;
        }

        private static void Check(string name, Func<bool> check)
        {
            try
            {
                if (check())
                {
                    passed++;
                    Console.WriteLine($"  PASS  {name}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"  FAIL  {name}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name} ({ex.GetType().Name}: {ex.Message})");
            }
        }

        /// <summary>
        /// RunFrame must refuse to advance while paused unless the caller explicitly steps.
        /// </summary>
        private static bool CheckStepWhilePaused()
        {
            bool wasRunning = EngineCore.IsRunning;
            try
            {
                EngineCore.Start();
                EngineCore.Pause();

                bool blocked = EngineCore.RunFrame();
                bool stepped = EngineCore.RunFrame(step: true);

                // With no world bound both paths are null-safe, so this isolates the paused gate.
                return !blocked && stepped;
            }
            finally
            {
                EngineCore.Stop();
                if (wasRunning) EngineCore.Start();
            }
        }

        /// <summary>
        /// float2/3/4 fields must round-trip on a machine whose decimal separator is ','.
        /// </summary>
        private static bool CheckCultureRoundTrip()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                Transform source = new()
                {
                    position = new float3(1.5f, -2.25f, 3.75f),
                    rotation = new float4(0.1f, 0.2f, 0.3f, 0.4f),
                    scaling = new float3(2.5f, 3.5f, 4.5f),
                };

                Dictionary<string, string> serialized = Serialize.Component(source);
                Transform copy = new();
                Deserialize.SetComponentProperties(copy, serialized);

                return Near(source.position, copy.position)
                    && Near(source.rotation, copy.rotation)
                    && Near(source.scaling, copy.scaling)
                    && serialized["position"].Contains('.');
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        /// <summary>
        /// SetVal used to mutate a by-value copy and report success, which silently discarded
        /// every edit made in the inspector's matrix editor.
        /// </summary>
        private static bool CheckMatrixAccessors()
        {
            float4x4 m = float4x4.identity;
            bool wrote = DivisionEngine.MathUtilities.Matrix.SetVal(ref m, 0, 3, 42f);
            bool rejectedRow4 = !DivisionEngine.MathUtilities.Matrix.SetVal(ref m, 4, 0, 1f);

            float4x4 product = DivisionEngine.MathUtilities.Matrix.Multiply(float4x4.identity, m);

            return wrote
                && Math.Abs(m[0, 3] - 42f) < 1e-5f
                && Math.Abs(DivisionEngine.MathUtilities.Matrix.GetVal(m, 0, 3) - 42f) < 1e-5f
                && rejectedRow4
                && Math.Abs(product[0, 3] - 42f) < 1e-5f;
        }

        private static World BuildSnapshotWorld(out uint first, out uint third, out uint nextBeforeClone)
        {
            World world = new("selftest_snapshot");
            first = world.CreateEntity("A");
            uint second = world.CreateEntity("B");
            third = world.CreateEntity("C");
            world.DestroyEntity(second); // leaves a gap so a renumbering clone would be visible

            world.AddEntityWithId(EditorCamera.EditorCameraId);
            world.AddComponent(EditorCamera.EditorCameraId, new Transform { position = new float3(5, 6, 7) });
            world.AddComponent(EditorCamera.EditorCameraId, new Camera());
            world.AddComponent(EditorCamera.EditorCameraId, new EditorCamera());

            nextBeforeClone = world.NextEntityId;
            return world;
        }

        private static bool CheckSnapshotIds()
        {
            World world = BuildSnapshotWorld(out uint first, out uint third, out uint nextBeforeClone);
            World clone = world.Clone();

            return clone.EntityExists(first)
                && clone.EntityExists(third)
                && clone.NextEntityId == nextBeforeClone
                && !clone.EntityExists(EditorCamera.EditorCameraId);
        }

        private static bool CheckSnapshotEditorCamera()
        {
            World world = BuildSnapshotWorld(out _, out _, out _);
            world.Clone();

            // Cloning must not delete or move the camera in the world it is cloning.
            return world.EntityExists(EditorCamera.EditorCameraId)
                && Near(world.GetComponent<Transform>(EditorCamera.EditorCameraId).position, new float3(5, 6, 7));
        }

        private static bool CheckRestoreIds()
        {
            World world = BuildSnapshotWorld(out uint first, out uint third, out uint nextBeforeClone);
            World clone = world.Clone();
            world.RestoreFrom(clone);

            return world.EntityExists(first)
                && world.EntityExists(third)
                && world.NextEntityId == nextBeforeClone
                && world.EntityExists(EditorCamera.EditorCameraId)
                && Near(world.GetComponent<Transform>(EditorCamera.EditorCameraId).position, new float3(5, 6, 7));
        }

        /// <summary>
        /// The undo stack used to keep the OLDEST commands after trimming and drop the newest.
        /// </summary>
        private static bool CheckUndoTrimming()
        {
            UndoManager.Clear();

            List<CountingCommand> commands = [];
            for (int i = 0; i < 105; i++)
            {
                CountingCommand command = new();
                commands.Add(command);
                UndoManager.Execute(command);
            }

            UndoManager.Undo();
            bool newestFirst = commands[104].Undone && !commands[103].Undone;

            int undone = 1;
            while (UndoManager.CanUndo)
            {
                UndoManager.Undo();
                undone++;
            }

            bool droppedOldest = !commands[0].Undone && !commands[4].Undone;
            bool keptNewest = commands[5].Undone;

            UndoManager.Clear();
            return newestFirst && droppedOldest && keptNewest && undone == 100;
        }

        /// <summary>
        /// Continuous edits of the same field (slider drags, colour pickers) must collapse to a
        /// single undo entry that restores the value from before the gesture.
        /// </summary>
        private static bool CheckUndoCoalescing()
        {
            World world = new("selftest_undo");
            uint entity = world.CreateEntity("Edited");
            world.AddComponent(entity, new Transform());

            World? previousWorld = WorldManager.CurrentWorld;
            WorldManager.SetWorld(world);
            WorldManager.SwitchWorld(world.Name);

            try
            {
                UndoManager.Clear();

                float3 start = new(0, 0, 0);
                float3 middle = new(1, 0, 0);
                float3 end = new(2, 3, 4);

                UndoManager.Execute(new ModifyFieldCommand(entity, typeof(Transform), "position", start, middle));
                UndoManager.Execute(new ModifyFieldCommand(entity, typeof(Transform), "position", middle, end));

                bool singleEntry = UndoManager.CanUndo;
                UndoManager.Undo();

                Transform transform = WorldManager.CurrentWorld!.GetComponent<Transform>(entity)!;
                bool restored = Near(transform.position, start) && !UndoManager.CanUndo;

                UndoManager.Clear();
                return singleEntry && restored;
            }
            finally
            {
                if (previousWorld != null)
                {
                    WorldManager.SetWorld(previousWorld);
                    WorldManager.SwitchWorld(previousWorld.Name);
                }
            }
        }

        /// <summary>
        /// Noise2D used a normalisation constant ~25x too small and returned a constant ~0.5,
        /// which baked every heightmap flat.
        /// </summary>
        private static bool CheckTerrainNoise()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < 48; y++)
            {
                for (int x = 0; x < 48; x++)
                {
                    float n = DivisionEngine.Rendering.ShaderUtilities.SimplexNoise.Noise2D(new float2(x * 0.15f, y * 0.15f));
                    min = Math.Min(min, n);
                    max = Math.Max(max, n);
                }
            }

            Console.WriteLine($"        noise range = [{min:F4}, {max:F4}]");
            return min < 0.30f && max > 0.70f;
        }

        private static bool CheckAssetTypes()
        {
            MethodInfo? determine = typeof(AssetDatabase).GetMethod(
                "DetermineAssetType", BindingFlags.NonPublic | BindingFlags.Static);
            if (determine == null) return false;

            AssetType cs = (AssetType)determine.Invoke(null, ["Player.cs"])!;
            AssetType hlsl = (AssetType)determine.Invoke(null, ["Sky.hlsl"])!;
            AssetType sdfLib = (AssetType)determine.Invoke(null, ["Lib.sdfLib"])!;

            return cs == AssetType.Script && hlsl == AssetType.Shader && sdfLib == AssetType.SDF;
        }

        /// <summary>
        /// Queries hand back snapshots, so destroying entities while a query is being
        /// enumerated must not throw "Collection was modified". The render thread relies on
        /// this while the editor thread edits the world.
        /// </summary>
        private static bool CheckQuerySnapshot()
        {
            World world = new("selftest_queries");
            for (int i = 0; i < 64; i++)
            {
                uint entity = world.CreateEntity();
                world.AddComponent(entity, new Transform());
            }

            int seen = 0;
            foreach (uint entity in world.Query<Transform>())
            {
                seen++;
                if (seen % 2 == 0) world.DestroyEntity(entity);
            }

            int dataSeen = 0;
            foreach (var (entity, _) in world.QueryData<Transform>())
            {
                dataSeen++;
                if (dataSeen % 3 == 0) world.DestroyEntity(entity);
            }

            int remaining = 0;
            foreach (uint _ in world.Query<Transform>()) remaining++;

            return seen == 64 && dataSeen == 32 && remaining > 0;
        }

        /// <summary>
        /// Log entries are appended from many threads while the console enumerates them on the
        /// UI thread; the snapshot list must never throw under that load.
        /// </summary>
        private static bool CheckLogConcurrency()
        {
            Exception? failure = null;

            Thread[] writers = new Thread[4];
            for (int t = 0; t < writers.Length; t++)
            {
                int writer = t;
                writers[t] = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < 40; i++)
                            Debug.Info($"selftest log line {writer}-{i}");
                    }
                    catch (Exception ex) { failure = ex; }
                });
                writers[t].Start();
            }

            try
            {
                for (int i = 0; i < 60; i++)
                {
                    IReadOnlyList<LogEntry> snapshot = Debug.Logs;
                    if (snapshot.Count < 0) return false; // force enumeration
                    foreach (LogEntry entry in snapshot) _ = entry.Message;
                }
            }
            catch (Exception ex) { failure = ex; }

            foreach (Thread writer in writers) writer.Join();
            return failure == null;
        }
        private static bool Near(float3 a, float3 b) =>
            Math.Abs(a.x - b.x) < 1e-4f && Math.Abs(a.y - b.y) < 1e-4f && Math.Abs(a.z - b.z) < 1e-4f;

        private static bool Near(float4 a, float4 b) =>
            Math.Abs(a.x - b.x) < 1e-4f && Math.Abs(a.y - b.y) < 1e-4f &&
            Math.Abs(a.z - b.z) < 1e-4f && Math.Abs(a.w - b.w) < 1e-4f;

        /// <summary>Command that records whether it was undone.</summary>
        private sealed class CountingCommand : IUndoCommand
        {
            public bool Undone { get; private set; }

            public string Description => "selftest command";

            public void Do() { }

            public void Undo() => Undone = true;
        }
    }
}

