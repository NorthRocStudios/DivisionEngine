//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DivisionEngine.Settings
{
    /// <summary>
    /// Stores general settings for different environments.
    /// </summary>
    public static class SettingsManager
    {
        /// <summary>
        /// Loaded settings files.
        /// </summary>
        public static Dictionary<string, ISettings> Loaded { get; private set; } = [];

        private static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>
        /// Path is : AppData/Roaming/DivisionEngine/[id].json
        /// </summary>
        /// <param name="id">ID of the settings object</param>
        /// <returns>Settings file path for id</returns>
        private static string GetSettingsFilePath(string id) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DivisionEngine", $"{id}.json");

        /// <summary>
        /// Saves settings object.
        /// </summary>
        /// <param name="settings">Settings to save</param>
        /// <returns>Whether or not settings were saved</returns>
        public static bool SaveSettings(ISettings settings)
        {
            try
            {
                settings.OnSave();
                string path = GetSettingsFilePath(settings.ID);
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(settings, settings.GetType(), serializerOptions);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to save settings {settings.ID}", ex);
                return false;
            }
        }

        /// <summary>
        /// Loads a settings object.
        /// </summary>
        /// <typeparam name="T">Type of settings to load</typeparam>
        /// <returns>Settings deserialized</returns>
        public static T GetSettings<T>() where T : ISettings, new()
        {
            string id = new T().ID;
            if (Loaded.TryGetValue(id, out ISettings? existing)) return (T)existing;

            T settings;
            string path = GetSettingsFilePath(id);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    settings = JsonSerializer.Deserialize<T>(json, serializerOptions) ?? new T();

                    // Merge with defaults
                    ((BaseSettings)(object)settings).MergeDefaults();
                }
                catch
                {
                    settings = new T();
                }
            }
            else settings = new T();

            settings.OnLoad();
            Loaded[id] = settings;
            return settings;
        }

        /// <summary>
        /// Saves all loaded settings.
        /// </summary>
        public static void SaveAll()
        {
            Debug.Info("Settings System: Saving all settings files");
            foreach (ISettings settings in Loaded.Values) SaveSettings(settings);
        }
    }
}
