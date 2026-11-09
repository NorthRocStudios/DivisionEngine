//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Buffers.Binary;
using System.Text;

namespace DivisionEngine.Audio
{
    /// <summary>
    /// Decoder for RIFF/WAVE files. Supports PCM 8/16/24/32-bit, IEEE float 32/64-bit
    /// and WAVE_FORMAT_EXTENSIBLE containers.
    /// </summary>
    public sealed class WavDecoder : IAudioDecoder
    {
        public string Name => "WAV";

        public bool CanDecode(string extension, ReadOnlySpan<byte> header)
        {
            if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) return true;
            return header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8);
        }

        public AudioClip Decode(byte[] data, string fileName)
        {
            if (data.Length < 12) throw new InvalidDataException("WAV file is too small.");
            if (!data.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !data.AsSpan(8, 4).SequenceEqual("WAVE"u8))
                throw new InvalidDataException("Not a RIFF/WAVE file.");

            int format = 0;
            int channels = 0;
            int sampleRate = 0;
            int bitsPerSample = 0;
            int dataOffset = -1;
            int dataLength = 0;

            int offset = 12;
            while (offset + 8 <= data.Length)
            {
                string chunkId = Encoding.ASCII.GetString(data, offset, 4);
                int chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
                int body = offset + 8;
                if (chunkSize < 0) break;

                if (chunkId == "fmt " && body + 16 <= data.Length)
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body, 2));
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 2, 2));
                    sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(body + 4, 4));
                    bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 14, 2));

                    // WAVE_FORMAT_EXTENSIBLE: real format code lives in the sub-format GUID.
                    if (format == 0xFFFE && chunkSize >= 40 && body + 26 <= data.Length)
                        format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 24, 2));
                }
                else if (chunkId == "data")
                {
                    dataOffset = body;
                    dataLength = Math.Min(chunkSize, data.Length - body);
                }

                offset = body + chunkSize + (chunkSize & 1); // chunks are word aligned
                if (dataOffset >= 0 && format != 0) break;
            }

            if (format == 0) throw new InvalidDataException("WAV file has no fmt chunk.");
            if (dataOffset < 0) throw new InvalidDataException("WAV file has no data chunk.");
            if (channels <= 0) throw new InvalidDataException("WAV file has no channels.");

            int bytesPerSample = bitsPerSample / 8;
            if (bytesPerSample <= 0) throw new InvalidDataException("Unsupported WAV bit depth.");
            int frameSize = bytesPerSample * channels;
            int frameCount = frameSize > 0 ? dataLength / frameSize : 0;
            if (frameCount <= 0) throw new InvalidDataException("WAV file contains no frames.");

            float[] samples = new float[frameCount * channels];
            ReadOnlySpan<byte> source = data.AsSpan(dataOffset, frameCount * frameSize);

            switch (format)
            {
                case 1: // PCM integer
                    ConvertPcm(source, samples, bitsPerSample, frameSize);
                    break;
                case 3: // IEEE float
                    ConvertFloat(source, samples, bitsPerSample, frameSize);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported WAV format code 0x{format:X4}.");
            }

            return new AudioClip(Path.GetFileName(fileName), sampleRate, channels, samples);
        }

        private static void ConvertPcm(ReadOnlySpan<byte> source, float[] destination, int bitsPerSample, int frameSize)
        {
            switch (bitsPerSample)
            {
                case 8:
                    for (int i = 0, bytes = 0; i < destination.Length; i++, bytes++)
                        destination[i] = (source[bytes] - 128) / 128f;
                    break;
                case 16:
                    for (int i = 0, bytes = 0; i < destination.Length; i++, bytes += 2)
                        destination[i] = BinaryPrimitives.ReadInt16LittleEndian(source[bytes..]) / 32768f;
                    break;
                case 24:
                    for (int i = 0, bytes = 0; i < destination.Length; i++, bytes += 3)
                    {
                        int value = source[bytes] | (source[bytes + 1] << 8) | (source[bytes + 2] << 16);
                        if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                        destination[i] = value / 8388608f;
                    }
                    break;
                case 32:
                    for (int i = 0, bytes = 0; i < destination.Length; i++, bytes += 4)
                        destination[i] = BinaryPrimitives.ReadInt32LittleEndian(source[bytes..]) / 2147483648f;
                    break;
                default:
                    throw new InvalidDataException($"Unsupported PCM bit depth {bitsPerSample}.");
            }
        }

        private static void ConvertFloat(ReadOnlySpan<byte> source, float[] destination, int bitsPerSample, int frameSize)
        {
            if (bitsPerSample == 32)
            {
                for (int i = 0, bytes = 0; i < destination.Length; i++, bytes += 4)
                    destination[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source[bytes..]));
            }
            else if (bitsPerSample == 64)
            {
                for (int i = 0, bytes = 0; i < destination.Length; i++, bytes += 8)
                    destination[i] = (float)BinaryPrimitives.ReadDoubleLittleEndian(source[bytes..]);
            }
            else
            {
                throw new InvalidDataException($"Unsupported float bit depth {bitsPerSample}.");
            }
        }
    }
}
