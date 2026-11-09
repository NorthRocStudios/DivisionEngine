//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Audio
{
    /// <summary>
    /// Decodes an encoded audio file into an <see cref="AudioClip"/>.
    /// </summary>
    public interface IAudioDecoder
    {
        /// <summary>
        /// Human readable decoder name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether this decoder can handle the given file extension / header bytes.
        /// </summary>
        bool CanDecode(string extension, ReadOnlySpan<byte> header);

        /// <summary>
        /// Decodes the file contents into interleaved float samples.
        /// </summary>
        AudioClip Decode(byte[] data, string fileName);
    }

    /// <summary>
    /// Picks a decoder based on the file extension and magic bytes, and caches
    /// nothing itself - <see cref="Projects.Assets.AudioAsset"/> owns the clip.
    /// </summary>
    public static class AudioDecoderRegistry
    {
        private static readonly List<IAudioDecoder> decoders = [];

        static AudioDecoderRegistry()
        {
            Register(new WavDecoder());
            Register(new OggVorbisDecoder());
        }

        /// <summary>
        /// All registered decoders, in priority order.
        /// </summary>
        public static IReadOnlyList<IAudioDecoder> Decoders => decoders;

        /// <summary>
        /// Adds a custom decoder. Custom decoders are tried before built-in ones.
        /// </summary>
        public static void Register(IAudioDecoder decoder)
        {
            if (decoder == null) throw new ArgumentNullException(nameof(decoder));
            decoders.Insert(0, decoder);
        }

        /// <summary>
        /// Whether any decoder can handle this file name.
        /// </summary>
        public static bool IsSupported(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            foreach (IAudioDecoder decoder in decoders)
                if (decoder.CanDecode(extension, ReadOnlySpan<byte>.Empty)) return true;
            return false;
        }

        /// <summary>
        /// Decodes audio bytes, returning null when no decoder matches or decoding fails.
        /// </summary>
        public static AudioClip? Decode(byte[] data, string fileName)
        {
            if (data == null || data.Length == 0) return null;

            string extension = Path.GetExtension(fileName);
            ReadOnlySpan<byte> header = data.AsSpan(0, Math.Min(data.Length, 64));

            foreach (IAudioDecoder decoder in decoders)
            {
                if (!decoder.CanDecode(extension, header)) continue;
                try
                {
                    AudioClip clip = decoder.Decode(data, fileName);
                    Debug.Info($"Audio: decoded '{fileName}' with {decoder.Name} " +
                               $"({clip.SampleRate} Hz, {clip.Channels} ch, {clip.Duration:F2}s)");
                    return clip;
                }
                catch (Exception ex)
                {
                    Debug.Error($"Audio: {decoder.Name} failed to decode '{fileName}'", ex);
                    return null;
                }
            }

            Debug.Warning($"Audio: no decoder for '{fileName}'");
            return null;
        }
    }
}
