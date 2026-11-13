//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.MathLib;
using System.Text.Json.Serialization;

namespace DivisionEngine.Settings
{
    /// <summary>
    /// Stores all common settings for the Division Engine.
    /// </summary>
    public class EngineSettings : BaseSettings
    {
        private static EngineSettings? instance;

        /// <summary>
        /// Gets the singleton instance of the engine settings used to configure the application.
        /// </summary>
        /// <remarks>Accessing this property ensures that only one instance of the engine settings is
        /// created and used throughout the application's lifetime. The settings are retrieved via the settings manager,
        /// which may load them from a configuration file or other persistent storage. This property is thread-safe and
        /// intended for global access to engine configuration.</remarks>
        [JsonIgnore]
        public static EngineSettings Instance
        {
            get
            {
                instance ??= SettingsManager.GetSettings<EngineSettings>();
                return instance;
            }
        }

        /// <summary>
        /// Engine settings file identifier.
        /// </summary>
        public override string ID => "EngineSettings";

        // Load will happen through SettingsManager
        public EngineSettings() { }

        protected override void InitializeDefaults()
        {
            ResolutionWidth = 1920;
            ResolutionHeight = 1080;
            Fullscreen = false;
            VSync = true;
            MaxFPS = 0;
            MouseSensitivity = 0.5f;

            AutoRecompileOnScriptChange = true;
            ReloadWorldOnRecompile = true;
        }

        [JsonIgnore]
        public int ResolutionWidth
        {
            get => Get(nameof(ResolutionWidth), 1920);
            set => Set(nameof(ResolutionWidth), value);
        }

        [JsonIgnore]
        public int ResolutionHeight
        {
            get => Get(nameof(ResolutionHeight), 1080);
            set => Set(nameof(ResolutionHeight), value);
        }

        [JsonIgnore]
        public bool Fullscreen
        {
            get => Get(nameof(Fullscreen), false);
            set => Set(nameof(Fullscreen), value);
        }

        [JsonIgnore]
        public bool VSync
        {
            get => Get(nameof(VSync), true);
            set => Set(nameof(VSync), value);
        }

        [JsonIgnore]
        public bool CheckerboardRendering
        {
            get => Get(nameof(CheckerboardRendering), true);
            set => Set(nameof(CheckerboardRendering), value);
        }

        /// <summary>Raised whenever <see cref="VoxelSdfRendering"/> changes.</summary>
        public static event Action? VoxelRenderingChanged;

        /// <summary>
        /// When true, the renderer rebuilds the scene SDF into a voxel grid and traces
        /// the trilinearly reconstructed ("de-voxelized") surface instead of evaluating
        /// the exact scene SDF at every ray step. Toggling is live; the next frame uses
        /// the other path.
        /// </summary>
        [JsonIgnore]
        public bool VoxelSdfRendering
        {
            get => Get(nameof(VoxelSdfRendering), false);
            set
            {
                bool changed = Get(nameof(VoxelSdfRendering), false) != value;
                Set(nameof(VoxelSdfRendering), value);
                if (changed) VoxelRenderingChanged?.Invoke();
            }
        }

        /// <summary>
        /// Number of grid points along each axis of a clipmap level. The cell size is
        /// derived from this so higher values preserve finer detail at the cost of
        /// memory and bake time.
        /// </summary>
        [JsonIgnore]
        public int VoxelGridResolution
        {
            get => Get(nameof(VoxelGridResolution), 96);
            set => Set(nameof(VoxelGridResolution), math.clamp(value, 32, 128));
        }

        /// <summary>
        /// Maximum number of newly baked voxel cells processed per frame. Shells
        /// produced by camera motion and object edits are spread across frames using
        /// this budget; a static scene queues no work.
        /// </summary>
        [JsonIgnore]
        public int VoxelUpdateCellBudget
        {
            get => Get(nameof(VoxelUpdateCellBudget), 2_000_000);
            set => Set(nameof(VoxelUpdateCellBudget), math.clamp(value, 100_000, 8_000_000));
        }

        [JsonIgnore]
        public int MaxFPS
        {
            get => Get(nameof(MaxFPS), 0);
            set => Set(nameof(MaxFPS), value);
        }

        [JsonIgnore]
        public float MouseSensitivity
        {
            get => Get(nameof(MouseSensitivity), 1f);
            set => Set(nameof(MouseSensitivity), math.clamp(value, 0.01f, 20f));
        }

        [JsonIgnore]
        public bool AutoRecompileOnScriptChange
        {
            get => Get(nameof(AutoRecompileOnScriptChange), true);
            set => Set(nameof(AutoRecompileOnScriptChange), value);
        }

        [JsonIgnore]
        public bool ReloadWorldOnRecompile
        {
            get => Get(nameof(ReloadWorldOnRecompile), true);
            set => Set(nameof(ReloadWorldOnRecompile), value);
        }

        // Ideas for later:

        //[JsonIgnore]
        //public int MaxRaySteps
        //{
        //    get => Get(nameof(MaxRaySteps), 128);
        //    set => Set(nameof(MaxRaySteps), value);
        //}

        //[JsonIgnore]
        //public int ShadowQuality
        //{
        //    get => Get(nameof(ShadowQuality), 2);
        //    set => Set(nameof(ShadowQuality), math.clamp(value, 0, 2));
        //}

        //[JsonIgnore]
        //public float RenderScale
        //{
        //    get => Get(nameof(RenderScale), 1.0f);
        //    set => Set(nameof(RenderScale), math.clamp(value, 0.25f, 2.0f));
        //}

        //[JsonIgnore]
        //public float MasterVolume
        //{
        //    get => Get(nameof(MasterVolume), 1.0f);
        //    set => Set(nameof(MasterVolume), math.clamp(value, 0f, 1f));
        //}

        //[JsonIgnore]
        //public float MusicVolume
        //{
        //    get => Get(nameof(MusicVolume), 0.8f);
        //    set => Set(nameof(MusicVolume), math.clamp(value, 0f, 1f));
        //}

        //[JsonIgnore]
        //public float SFXVolume
        //{
        //    get => Get(nameof(SFXVolume), 1.0f);
        //    set => Set(nameof(SFXVolume), math.clamp(value, 0f, 1f));
        //}

        //[JsonIgnore]
        //public bool InvertY
        //{
        //    get => Get(nameof(InvertY), false);
        //    set => Set(nameof(InvertY), value);
        //}

        //[JsonIgnore]
        //public Dictionary<string, string> KeyBindings
        //{
        //    get => Get(nameof(KeyBindings), new Dictionary<string, string>());
        //    set => Set(nameof(KeyBindings), value);
        //}

        //[JsonIgnore]
        //public string Language
        //{
        //    get => Get(nameof(Language), "en-US");
        //    set => Set(nameof(Language), value);
        //}

        //[JsonIgnore]
        //public bool ShowTutorials
        //{
        //    get => Get(nameof(ShowTutorials), true);
        //    set => Set(nameof(ShowTutorials), value);
        //}

        /// <summary>
        /// Ensures that the resolution width and height meet the minimum required values when the component is loaded.
        /// </summary>
        /// <remarks>This method is typically called during the component's initialization process to
        /// enforce minimum resolution constraints. Setting the resolution to at least 640x480 pixels may be necessary
        /// for correct rendering or to meet application requirements.</remarks>
        public override void OnLoad()
        {
            if (ResolutionWidth < 640) ResolutionWidth = 640;
            if (ResolutionHeight < 480) ResolutionHeight = 480;
        }
    }
}
