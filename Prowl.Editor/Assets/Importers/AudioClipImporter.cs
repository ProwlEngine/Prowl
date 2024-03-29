using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using Prowl.Runtime;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Utils;
using System.Buffers.Binary;

namespace Prowl.Editor.Assets.Importers
{
    [Importer("FileIcon.png", typeof(AudioClip), ".wav")]
    public class AudioClipImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            ctx.SetMainObject(assetPath.Extension.ToLower() switch {
                ".wav" => LoadWav(assetPath),
                ".wave" => LoadWav(assetPath),
                _ => throw new InvalidOperationException("Unsupported audio format: " + assetPath.Extension.ToLower()),
            });
        }

        #region Wave Format

        private static AudioClip LoadWav(FileInfo file)
        {
            using (FileStream stream = file.OpenRead())
            {
                var buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                ReadOnlySpan<byte> fileSpan = new ReadOnlySpan<byte>(buffer);

                int index = 0;
                if (fileSpan[index++] != 'R' || fileSpan[index++] != 'I' || fileSpan[index++] != 'F' || fileSpan[index++] != 'F')
                {
                    throw new InvalidDataException("Given file is not in RIFF format");
                }

                var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(fileSpan.Slice(index, 4));
                index += 4;

                if (fileSpan[index++] != 'W' || fileSpan[index++] != 'A' || fileSpan[index++] != 'V' || fileSpan[index++] != 'E')
                {
                    throw new InvalidDataException("Given file is not in WAVE format");
                }

                short numChannels = -1;
                int sampleRate = -1;
                int byteRate = -1;
                short blockAlign = -1;
                short bitsPerSample = -1;
                byte[] audioData = null;

                while (index + 4 < fileSpan.Length)
                {
                    var identifier = "" + (char)fileSpan[index++] + (char)fileSpan[index++] + (char)fileSpan[index++] + (char)fileSpan[index++];
                    var size = BinaryPrimitives.ReadInt32LittleEndian(fileSpan.Slice(index, 4));
                    index += 4;

                    if (identifier == "fmt ")
                    {
                        if (size != 16)
                        {
                            throw new InvalidDataException($"Unknown Audio Format with subchunk1 size {size}");
                        }
                        else
                        {
                            var audioFormat = BinaryPrimitives.ReadInt16LittleEndian(fileSpan.Slice(index, 2));
                            index += 2;
                            if (audioFormat != 1)
                            {
                                throw new InvalidDataException($"Unknown Audio Format with ID {audioFormat}");
                            }
                            else
                            {
                                numChannels = BinaryPrimitives.ReadInt16LittleEndian(fileSpan.Slice(index, 2));
                                index += 2;
                                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fileSpan.Slice(index, 4));
                                index += 4;
                                byteRate = BinaryPrimitives.ReadInt32LittleEndian(fileSpan.Slice(index, 4));
                                index += 4;
                                blockAlign = BinaryPrimitives.ReadInt16LittleEndian(fileSpan.Slice(index, 2));
                                index += 2;
                                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(fileSpan.Slice(index, 2));
                                index += 2;
                            }
                        }
                    }
                    else if (identifier == "data")
                    {
                        audioData = fileSpan.Slice(index, size).ToArray();
                        index += size;
                    }
                    else
                    {
                        index += size;
                    }
                }

                if (audioData == null)
                {
                    throw new InvalidDataException("WAV file does not contain a data chunk");
                }

                float[] floatData = ConvertToFloatData(audioData, bitsPerSample);
                AudioClip audioClip = AudioClip.Create(file.Name, floatData, numChannels, bitsPerSample, sampleRate);
                return audioClip;
            }
        }

        private static float[] ConvertToFloatData(byte[] audioData, short bitsPerSample)
        {
            float[] floatData = new float[audioData.Length / (bitsPerSample / 8)];
            int sampleIndex = 0;

            for (int i = 0; i < audioData.Length; i += bitsPerSample / 8)
            {
                float sample = 0;

                if (bitsPerSample == 8)
                {
                    sample = (float)audioData[i] / 128.0f - 1.0f;
                }
                else if (bitsPerSample == 16)
                {
                    short sample16 = BitConverter.ToInt16(audioData, i);
                    sample = (float)sample16 / 32768.0f;
                }
                else if (bitsPerSample == 32)
                {
                    int sample32 = BitConverter.ToInt32(audioData, i);
                    sample = (float)sample32 / 2147483648.0f;
                }

                floatData[sampleIndex++] = sample;
            }

            return floatData;
        }

        #endregion

    }

    [CustomEditor(typeof(AudioClipImporter))]
    public class AudioClipEditor : ScriptedEditor
    {
        enum Res { _1, _2, _4, _8, _16, _32, _64, _128, _256 }
        Res visRes = Res._64;

        ActiveAudio? preview;

        public override void OnInspectorGUI()
        {
            var importer = (AudioClipImporter)(target as MetaFile).importer;
            var serialized = AssetDatabase.LoadAsset((target as MetaFile).AssetPath);

            try
            {
                // Show Audio information
                var audioClip = (AudioClip)serialized.Main;
                ImGui.Text("Name: " + audioClip.Name);
                ImGui.Text("Channels: " + audioClip.Channels);
                ImGui.Text("Bits Per Sample: " + audioClip.BitsPerSample);
                ImGui.Text("Sample Rate: " + audioClip.SampleRate);
                ImGui.Text("Duration: " + audioClip.Duration + "s");
                ImGui.Separator();
                ImGui.Text("Sample Rate: " + audioClip.SampleRate);
                ImGui.Text("Size in Bytes: " + audioClip.SizeInBytes);
                ImGui.Text("Format: " + audioClip.Format.ToString());

                ImGui.Separator();
                // Show Audio Data with ImPlots
                if (audioClip.Data == null)
                {
                    ImGui.Text("Audioclip Data is Null!");
                    return;
                }

                ImGui.Text("Audio Data");

                // Show res
                GUIHelper.EnumComboBox("Resolution", ref visRes);

                var reg = ImGui.GetContentRegionAvail();
                if (ImPlot.BeginPlot("Audio Waveform", new System.Numerics.Vector2(reg.X, 150), ImPlotFlags.NoLegend))
                {
                    ImPlot.SetupAxes("Time (s)", "Amplitude", ImPlotAxisFlags.None, ImPlotAxisFlags.AutoFit);
                    ImPlot.SetupAxesLimits(0, audioClip.Duration, -1, 1);
                    ImPlot.SetupMouseText(ImPlotLocation.SouthEast, 0);
                    
                    int res = 1 << (int)visRes;
                    ImPlot.PlotLine("Audio Data", ref audioClip.Data[0], audioClip.Data.Length / res, ((1.0f / audioClip.Data.Length) * audioClip.Duration) * res, 0, ImPlotLineFlags.None, 0, sizeof(float) * res);
                    ImPlot.EndPlot();
                }

                // Play
                if(preview != null && preview.IsPlaying)
                {
                    if (ImGui.Button("Stop"))
                    {
                        preview.Stop();
                        preview = null;
                    }
                }
                else
                {
                    if (ImGui.Button("Play"))
                    {
                        preview = AudioSystem.PlaySound(audioClip);
                    }
                }

            }
            catch
            {
                ImGui.Text("Failed to display AudioClip Data");
            }
        }
    }
}
