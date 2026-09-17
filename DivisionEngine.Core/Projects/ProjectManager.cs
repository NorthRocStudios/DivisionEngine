//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Projects.Assets;
using DivisionEngine.Projects.Scripting;
using DivisionEngine.Serialization;
using DivisionEngine.Settings;

namespace DivisionEngine.Projects
{
    /// <summary>
    /// Handles project state management in Division Engine.
    /// </summary>
    public class ProjectManager
    {
        /// <summary>
        /// Current project name.
        /// </summary>
        public static string? CurrentProjectName { get; private set; } = null;

        /// <summary>
        /// Current project directory path.
        /// </summary>
        public static string? CurrentProjectPath { get; private set; } = null;

        /// <summary>
        /// Current loaded project data (settings, layout, etc.)
        /// </summary>
        public static DivisionProject? CurrentProjectData { get; private set; } = null;

        /// <summary>
        /// If a project is currently loaded.
        /// </summary>
        public static bool IsCurrentLoaded =>
            !string.IsNullOrWhiteSpace(CurrentProjectPath) && !string.IsNullOrWhiteSpace(CurrentProjectName);

        /// <summary>
        /// Asset manager currently in use.
        /// </summary>
        public static AssetManager? AssetManager { get; private set; }

        // Project Events
        public static event Action? ProjectLoaded;
        public static event Action? ProjectClosing;
        public static event Action? ProjectClosed;

		// On project load we deserialize the world synchronously, but scripts compile
		// asynchronously. If the compile hasn't finished by then, custom components
		// fail to resolve and get dropped. These two fields let us remember the raw
		// world data and reload it once the first compile succeeds.
		private static WorldData? pendingWorldReload = null;
		private static bool firstCompileHandled = false;

		/// <summary>
		/// Searches project directory to find the project file (ex. NewProject.divp).
		/// </summary>
		/// <param name="projDir">Project directory to search</param>
		/// <returns>Project file name (includes .divp extension)</returns>
		public static string? GetProjectFile(string projDir)
        {
            DirectoryInfo projDirInfo = new DirectoryInfo(projDir);
            if (projDirInfo.Exists)
            {
                foreach (FileInfo file in projDirInfo.EnumerateFiles("*.divp", SearchOption.TopDirectoryOnly))
                    return file.Name;
            }
            return null;
        }

        /// <summary>
        /// Gets the project file path from the project directory.
        /// </summary>
        /// <param name="projDir">Project top level directory</param>
        /// <returns>The path of the project file</returns>
        public static string GetProjectPath(string projDir) => $"{projDir}\\{GetProjectFile(projDir)!}";

        /// <summary>
        /// Gets the project file path from the project directory and project name.
        /// </summary>
        /// <param name="projDir">Project top level directory</param>
        /// <param name="projName">Project name</param>
        /// <returns>The path of the project file</returns>
        public static string GetProjectPath(string projDir, string projName) => $"{projDir}\\{projName}.divp";

        /// <summary>
        /// Gets the path to the world data file.
        /// </summary>
        /// <param name="projDir">Project to look in</param>
        /// <param name="world">WorldData to find path for</param>
        /// <returns>Formatted world data file path</returns>
        public static string GetWorldPath(string projDir, WorldData world) => $"{projDir}\\{world.Name}.wld";

        /// <summary>
        /// Checks to see if the project directory is a valid Division Engine project.
        /// </summary>
        /// <param name="projDir">Project directory to check</param>
        /// <returns>If the project directory is a Division Engine project</returns>
        public static bool IsDivisionProject(string projDir) => File.Exists(GetProjectPath(projDir));

        /// <summary>
        /// Loads a project via its top level directory.
        /// </summary>
        /// <param name="projDir">Project directory to load</param>
        /// <returns>If the project was successfully loaded</returns>
        public static bool LoadProject(string projDir)
        {
            // If a project is already loaded, close it before getting new path and name
            if (IsCurrentLoaded) CloseProject();

            CurrentProjectPath = projDir;
            CurrentProjectName = GetProjectFile(projDir)?.Replace(".divp", "");

            if (IsCurrentLoaded)
            {
                // Force project validation
                bool validationStep = ForceValidateProjectDirectory(CurrentProjectName!, projDir);
                if (!validationStep)
                {
                    Debug.Error($"Project Failed Validation! | Path: {projDir}");
                    return false;
                }

				InitializeAssetSystem();

				// Reset reload tracking for this project.
				pendingWorldReload = null;
				firstCompileHandled = false;

				ScriptCompilationPipeline.Initialize();
				ScriptCompilationPipeline.CompilationStarted += OnCompilationStarted;
				ScriptCompilationPipeline.ScriptsLoaded += OnScriptsLoaded;
				_ = ScriptCompilationPipeline.RefreshAndCompileAsync();

				// Load project file
				CurrentProjectData = null;
                foreach (string projPath in Directory.EnumerateFiles(projDir, "*.divp", SearchOption.TopDirectoryOnly))
                {
                    string projJson = File.ReadAllText(projPath);
                    if (!string.IsNullOrEmpty(projJson)) CurrentProjectData = Deserialize.Default<DivisionProject>(projJson);
                    break; // Break after first project file
                }

                if (CurrentProjectData != null) Debug.Info("Project Manager: Loaded project settings.");
                else CurrentProjectData = new DivisionProject(CurrentProjectName!); // Create new project data if none exists

				// Load world data file
				WorldData? tempWorldData = null;
				foreach (string worldPath in Directory.EnumerateFiles(projDir, "*.wld", SearchOption.TopDirectoryOnly))
				{
					string worldJson = File.ReadAllText(worldPath);
					if (!string.IsNullOrEmpty(worldJson))
						tempWorldData = Deserialize.Default<WorldData>(worldJson);
					break;
				}

				if (tempWorldData != null)
				{
					Debug.Info("Project Manager: World data deserialized.");

					// Stash the raw data so OnScriptsLoaded can replay it if the initial
					// deserialize couldn't resolve custom component types.
					if (!firstCompileHandled) pendingWorldReload = tempWorldData;

					LoadWorldDataIntoCurrent(tempWorldData);
					ResolveAssetReferencesInWorld(WorldManager.CurrentWorld);
				}

				ProjectLoaded?.Invoke(); // Notify that a project was loaded
                return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves asset references in world components (called after world load).
        /// </summary>
        private static void ResolveAssetReferencesInWorld(World? world)
        {
            if (world == null || AssetManager == null) return;

            // This would iterate through all components and resolve AssetReference fields
            Debug.Info("Project Manager: Asset references ready for loading");
        }

		/// <summary>
		/// Fires at the start of every compile pass. For the first compile after
		/// project load, we skip the snapshot — LoadProject has already stashed the
		/// on-disk WorldData, and that's what should be replayed. For every
		/// subsequent compile, snapshot the live world so it can be re-materialized
		/// against the fresh assembly once the compile succeeds.
		/// </summary>
		private static void OnCompilationStarted()
		{
			if (firstCompileHandled && WorldManager.CurrentWorld != null)
				pendingWorldReload = WorldData.Current;
		}

		/// <summary>
		/// Called when the script pipeline finishes a compile and a new assembly
		/// context has been loaded. Registers new systems on the current world, and
		/// — for the first successful compile after project load — replays the world
		/// data so any custom components that failed to deserialize before the
		/// assembly was available are restored.
		/// </summary>
		private static void OnScriptsLoaded(ScriptLoadContext context)
		{
            bool isFirstCompile = !firstCompileHandled;
            bool shouldReload = isFirstCompile || EngineSettings.Instance.ReloadWorldOnRecompile;

            if (shouldReload && pendingWorldReload != null)
            {
                WorldData snapshot = pendingWorldReload;
                pendingWorldReload = null;

                LoadWorldDataIntoCurrent(snapshot);
                ResolveAssetReferencesInWorld(WorldManager.CurrentWorld);

                // The world instance changed — the renderer must rebind or it will
                // keep showing the previous, incomplete world.
                Rendering.RenderPipeline.Instance?.BindCurrentWorld();
            }
            else
            {
                pendingWorldReload = null;
                WorldManager.CurrentWorld?.RegisterNewSystems();
            }

            firstCompileHandled = true;
        }

		/// <summary>
		/// Loads a WorldData object into the current world.
		/// </summary>
		/// <param name="worldData">WorldData to parse and load</param>
		private static void LoadWorldDataIntoCurrent(WorldData worldData)
        {
            // Tear down the outgoing world's systems first. This releases their event
            // subscriptions, timers, and any other resources they own, so they can't
            // fire into a world we're about to replace.
            World? outgoing = WorldManager.CurrentWorld;
            if (outgoing != null)
            {
                try { outgoing.CallUnload(); }
                catch (Exception ex) { Debug.Warning("ProjectManager: CallUnload threw during world reload", ex); }

                try { outgoing.CallAppExit(); }
                catch (Exception ex) { Debug.Warning("ProjectManager: CallAppExit threw during world reload", ex); }
            }

            World newWorld = new World(worldData.Name)
            {
                entities = [],
                NextEntityId = worldData.NextEntityId
            };

            foreach (EntityData entityData in worldData.Entities)
            {
                newWorld.entities.Add(entityData.Id);
                foreach (ComponentData componentData in entityData.Components)
                    newWorld.AddComponentFromData(entityData.Id, componentData);
            }

            newWorld.RegisterAllSystems();

            // Symmetry with teardown: every system in this fresh world gets its
            // lifecycle hooks. Without this, no system's AppStart ever runs and
            // nothing subscribes to ProjectLoaded/ProjectClosed/AssetsUpdated.
            newWorld.CallAwake();
            newWorld.CallAppStart();

            WorldManager.SetWorld(newWorld);
            WorldManager.SwitchWorld(newWorld.Name);
        }

        /// <summary>
        /// Saves a new project with a specified name and project directory.
        /// </summary>
        /// <param name="projName">New project name</param>
        /// <param name="projDir">New project directory</param>
        /// <returns>If new project creation was successful</returns>
        public static bool SaveNewProject(string projName, string projDir)
        {
			if (string.IsNullOrWhiteSpace(projDir) || string.IsNullOrEmpty(projName))
				return false;

			// Ensure any existing project is fully torn down first.
			if (IsCurrentLoaded) CloseProject();

			CurrentProjectName = projName;
			CurrentProjectPath = projDir;
			CurrentProjectData = new DivisionProject(projName);

			bool saved = SaveProject(projName, projDir);
			if (!saved) return false;

			// SaveProject created the directory structure. Now bring up the asset
			// system and script pipeline just like LoadProject does, so the new
			// project is fully functional without a close/reopen cycle.
			InitializeAssetSystem();

			pendingWorldReload = null;
			firstCompileHandled = false;

			ScriptCompilationPipeline.Initialize();
			ScriptCompilationPipeline.CompilationStarted += OnCompilationStarted;
			ScriptCompilationPipeline.ScriptsLoaded += OnScriptsLoaded;
			_ = ScriptCompilationPipeline.RefreshAndCompileAsync();

			ProjectLoaded?.Invoke();
			return true;
		}

        /// <summary>
        /// Saves the current project.
        /// </summary>
        /// <returns>If a project is loaded</returns>
        public static bool SaveCurrentProject()
        {
            if (IsCurrentLoaded) return SaveProject(CurrentProjectName!, CurrentProjectPath!);
            return false;
        }

        private static bool SaveProject(string projName, string projDir)
        {
            if (WorldManager.CurrentWorld != null)
            {
                // Force project validation
                bool validationStep = ForceValidateProjectDirectory(projName, projDir);
                if (!validationStep)
                {
                    Debug.Error($"Project Failed Validation! | Path: {projDir}");
                    return false;
                }

                // Update project data before saving
                if (CurrentProjectData != null)
                {
                    CurrentProjectData.Name = projName;
                    CurrentProjectData.LastSaved = DateTime.Now;
                }
                else CurrentProjectData = new DivisionProject(projName);

                WorldData worldData = WorldData.Current; // Serialize world
                string serializedWorld = Serialize.Default(worldData);
                string serializedProjectData = Serialize.Default(CurrentProjectData);

                File.WriteAllText(GetProjectPath(projDir, projName), serializedProjectData); // Write project file
                File.WriteAllText(GetWorldPath(projDir, worldData), serializedWorld); // Write single world file

                // Asset database doesn't need saving, it's auto-saved via .divmeta files

                return true;
            }
            return false;
        }

        /// <summary>
        /// Validates correct project directory setup.
        /// </summary>
        /// <param name="projName">The name of the project folder</param>
        /// <param name="projectDir">The directiory of the project</param>
        /// <returns>Whether the project directory formatting executed successfully</returns>
        private static bool ForceValidateProjectDirectory(string projName, string projectDir)
        {
            if (!string.IsNullOrEmpty(projName) && !string.IsNullOrEmpty(projectDir))
            {
                // Validate project directory
                DirectoryInfo projDirInfo = new DirectoryInfo(projectDir);
                if (!projDirInfo.Exists)
                {
                    projDirInfo.Create();
                    Debug.Info($"Created Project Directory: {projDirInfo.FullName}");
                }

                // Validate assets directory
                DirectoryInfo assetsDir = new DirectoryInfo($"{projectDir}\\Assets\\");
                if (!assetsDir.Exists) assetsDir.Create();

                // Generate / refresh the Visual Studio solution and player/editor
                // .csproj files. Idempotent - content-only rewrites
                ProjectScaffolder.EnsureScaffolding(projName, projectDir);

                return true;
            }
            return false;
        }

        /// <summary>
        /// Sets up the asset database and asset manager.
        /// </summary>
        private static void InitializeAssetSystem()
        {
            // Initialize the asset database
            AssetManager = new AssetManager(); // Create asset manager instance
            AssetDatabase.Initialize();
            AssetDatabase.StartFileWatcher();
        }

        /// <summary>
        /// Called when projects are closed.
        /// </summary>
        public static void CloseProject()
        {
            Debug.Info($"Project Manager: Closing {CurrentProjectName}");
            ProjectClosing?.Invoke();

            Rendering.RenderPipeline.Instance?.UnbindWorld();
            World? outgoingWorld = WorldManager.CurrentWorld;
            if (outgoingWorld != null)
            {
                try { outgoingWorld.CallUnload(); }
                catch (Exception ex) { Debug.Warning("Project Manager: CallUnload threw during close", ex); }

                try { outgoingWorld.CallAppExit(); }
                catch (Exception ex) { Debug.Warning("Project Manager: CallAppExit threw during close", ex); }

                // Drop the system instances entirely. Their types still exist, but nothing in this world can reach them
                outgoingWorld.systems.Clear();
            }

            ScriptCompilationPipeline.CompilationStarted -= OnCompilationStarted;
            ScriptCompilationPipeline.ScriptsLoaded -= OnScriptsLoaded;
            ScriptCompilationPipeline.Shutdown();

            AssetDatabase.StopFileWatcher();
            AssetDatabase.SaveAll();
            AssetManager?.UnloadAll();

            // Now safe to forget the world.
            WorldManager.ClearAllWorlds();

            pendingWorldReload = null;
            firstCompileHandled = false;

            AssetManager = null;
            CurrentProjectName = null;
            CurrentProjectPath = null;
            CurrentProjectData = null;

            ProjectClosed?.Invoke();
        }
    }
}
