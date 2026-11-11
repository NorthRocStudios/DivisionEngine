//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine
{
    /// <summary>
    /// Core class that responsible for scheduling calls in Division Engine.
    /// </summary>
    public static class EngineCore
    {
        /// <summary>
        /// Is the engine running?
        /// </summary>
        public static bool IsRunning { get; private set; }

        /// <summary>
        /// Is the engine paused?
        /// </summary>
        public static bool IsPaused { get; private set; }

        /// <summary>
        /// Is the engine in play mode?
        /// </summary>
        public static bool IsInPlayMode { get; private set; }

        /// <summary>
        /// Saved world state for restoring after play mode.
        /// </summary>
        private static World? savedWorld;

        /// <summary>
        /// Event fired when play mode changes, true if in play mode, false if not.
        /// </summary>
        public static event Action<bool>? PlayModeChanged;

        /// <summary>
        /// Start the engine.
        /// </summary>
        public static void Start()
        {
            if (IsRunning) return;

            IsRunning = true;
            IsPaused = false;
            Debug.Info("Engine Core: Starting");
            WorldManager.CurrentWorld?.CallAwake();
        }

        /// <summary>
        /// Stop the engine.
        /// </summary>
        public static void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            IsPaused = false;
            Debug.Info("Engine Core: Stopping");
            WorldManager.CurrentWorld?.CallUnload();
        }

        /// <summary>
        /// Pause the engine.
        /// </summary>
        public static void Pause()
        {
            if (!IsRunning) return;
            IsPaused = true;
            Debug.Info("Engine Core: Paused");
            
        }

        /// <summary>
        /// Resume the engine.
        /// </summary>
        public static void Resume()
        {
            if (!IsRunning) return;
            IsPaused = false;
            Debug.Info("Engine Core: Resumed");
        }

        /// <summary>
        /// Execute a frame in the engine.
        /// </summary>
        /// <param name="step">Run exactly one frame even while paused (editor "Advance Frame").</param>
        /// <returns>Whether the frame was successfully executed</returns>
        public static bool RunFrame(bool step = false)
        {
            if (!IsRunning) return false;
            if (IsPaused && !step) return false;

            WorldManager.CurrentWorld?.CallUpdate();
            WorldManager.CurrentWorld?.CallFixedUpdate(); // Fixed update loop runs after update loop
            return true;
        }

        /// <summary>
        /// Saves the current world state for later restoration.
        /// </summary>
        public static void SaveWorldState()
        {
            if (WorldManager.CurrentWorld == null)
            {
                Debug.Warning("Cannot save world state: No current world");
                return;
            }

            savedWorld = WorldManager.CurrentWorld.Clone();
            Debug.Info($"World state saved: {savedWorld.Name}");
        }

        /// <summary>
        /// Restores the previously saved world state.
        /// </summary>
        public static void RestoreWorldState()
        {
            if (savedWorld == null)
            {
                Debug.Warning("Cannot restore world state: No saved world");
                return;
            }

            // Replace the current world with the saved one
            WorldManager.RestoreWorldState(savedWorld);
            savedWorld = null;
            Debug.Info("World state restored");
        }

        /// <summary>
        /// Enters play mode - saves current state and starts simulation.
        /// </summary>
        public static void EnterPlayMode()
        {
            if (IsInPlayMode)
            {
                Debug.Warning("Already in play mode");
                return;
            }

            // Save current world state
            SaveWorldState();
            Start();
            IsInPlayMode = true;
            PlayModeChanged?.Invoke(true);
            Debug.Info("Entered Play Mode");
        }

        /// <summary>
        /// Exits play mode - stops simulation and restores saved state.
        /// </summary>
        public static void ExitPlayMode()
        {
            if (!IsInPlayMode)
            {
                Debug.Warning("Not in play mode");
                return;
            }

            Stop();
            RestoreWorldState();
            IsInPlayMode = false;
            PlayModeChanged?.Invoke(false);
            Debug.Info("Exited Play Mode");
        }

        /// <summary>
        /// Resets the play mode without restoring state (for new game starts).
        /// </summary>
        public static void ResetPlayMode()
        {
            if (!IsInPlayMode) return;

            // Just stop without restoring
            Stop();
            IsInPlayMode = false;
            PlayModeChanged?.Invoke(false);
            Debug.Info("Play Mode Reset");
        }
    }
}
