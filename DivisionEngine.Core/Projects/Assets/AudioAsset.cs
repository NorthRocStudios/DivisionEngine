//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Audio;

namespace DivisionEngine.Projects.Assets
{
    /// <summary>
    /// An audio file in the project. Files are decoded into an
    /// <see cref="AudioClip"/> on load and handed straight to the audio engine.
    /// </summary>
    [AssetType(AssetType.Audio)]
    public class AudioAsset(AssetMetadata metadata) : Asset(metadata)
    {
        private byte[]? audioData;

        /// <summary>
        /// Raw audio file data (WAV/MP3/OGG bytes).
        /// </summary>
        public byte[]? AudioData => audioData;

        /// <summary>
        /// Decoded clip, or null when the asset is not loaded or decoding failed.
        /// </summary>
        public AudioClip? Clip { get; private set; }

        /// <summary>
        /// Length of the decoded clip in seconds.
        /// </summary>
        public double Duration => Clip?.Duration ?? 0d;

        public override async Task<bool> LoadAsync()
        {
            if (IsLoaded) return true;

            try
            {
                string fullPath = Path.Combine(AssetDatabase.ProjectPath, RelativePath);
                audioData = await File.ReadAllBytesAsync(fullPath);

                Clip = AudioDecoderRegistry.Decode(audioData, Metadata.FileName);
                if (Clip == null)
                {
                    Debug.Error($"Failed to decode audio {Metadata.FileName}");
                    IsLoaded = false;
                    return false;
                }

                IsLoaded = true;
                Debug.Info($"Audio loaded: {Metadata.FileName} " +
                           $"({Clip.SampleRate} Hz, {Clip.Channels} ch, {Clip.Duration:F2}s)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load audio {Metadata.FileName}: {ex.Message}");
                IsLoaded = false;
                return false;
            }
        }

        public override void Unload()
        {
            audioData = null;
            Clip = null;
            IsLoaded = false;
            Debug.Info($"Audio unloaded: {Metadata.FileName}");
        }
    }
}
