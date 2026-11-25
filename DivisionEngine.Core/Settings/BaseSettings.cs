//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using DivisionEngine.Systems;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DivisionEngine.Settings
{
    /// <summary>
    /// Base class for settings with helper methods.
    /// </summary>
    public abstract class BaseSettings : ISettings
    {
        /// <summary>
        /// Identifier of the settings file.
        /// </summary>
        public abstract string ID { get; }

        [JsonInclude]
        public Dictionary<string, object> Settings { get; private set; } = [];

        protected BaseSettings()
        {
            InitializeDefaults();
        }

        /// <summary>
        /// Initializes default values.
        /// </summary>
        protected virtual void InitializeDefaults() { }

        /// <summary>
        /// Merges default settings with any new settings created.
        /// </summary>
        public void MergeDefaults()
        {
            // Create a temporary instance with defaults
            BaseSettings defaults = (BaseSettings)Activator.CreateInstance(GetType())!;

            // Add any missing keys from defaults
            foreach (var kvp in defaults.Settings)
                if (!Settings.ContainsKey(kvp.Key))
                    Settings[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Get a setting value with type safety.
        /// </summary>
        protected T Get<T>(string key, T defaultValue)
        {
            if (Settings.TryGetValue(key, out object? value))
            {
                try
                {
                    // Handle JsonElement from deserialization
                    if (value is JsonElement jsonElement) return jsonElement.Deserialize<T>() ?? defaultValue;
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    Debug.Info($"Settings System: Could not load setting {key} of type {typeof(T)}");
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Set a setting value.
        /// </summary>
        protected void Set<T>(string key, T value)
        {
            if (value == null) Debug.Warning($"Settings System: Value of {key} setting is null");
            SettingsSystem.MarkDirty(); // Don't forget to mark settings dirty
            Settings[key] = value!;
        }

        public virtual void OnLoad() { }
        public virtual void OnSave() { }
    }
}
