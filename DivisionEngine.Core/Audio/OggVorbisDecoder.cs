//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using NVorbis;

namespace DivisionEngine.Audio
{
    /// <summary>
    /// Decoder for Ogg Vorbis files, backed by NVorbis.
    /// </summary>
    public sealed class OggVorbisDecoder : IAudioDecoder
    {
        public string Name => "OGG Vorbis";

        public bool CanDecode(string extension, ReadOnlySpan<byte> header)
        {
            if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)) return true;
            return header.Length >= 4 && header[..4].SequenceEqual("OggS"u8);
        }

        public AudioClip Decode(byte[] data, string fileName)
        {
            using MemoryStream stream = new(data, writable: false);
            using VorbisReader reader = new(stream, false);

            int channels = reader.Channels;
            int sampleRate = reader.SampleRate;
            if (channels <= 0 || sampleRate <= 0) throw new InvalidDataException("Invalid Vorbis stream.");

            // Pre-size when the stream reports a total length, otherwise grow.
            int initial = reader.TotalSamples is > 0 and < int.MaxValue
                ? (int)reader.TotalSamples * channels
                : 0;
            List<float> samples = initial > 0 ? new List<float>(initial) : [];

            float[] buffer = new float[channels * 8192];
            int read;
            while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                samples.AddRange(buffer.AsSpan(0, read));

            if (samples.Count == 0) throw new InvalidDataException("Vorbis stream contained no samples.");

            return new AudioClip(Path.GetFileName(fileName), sampleRate, channels, [.. samples]);
        }
    }
}

