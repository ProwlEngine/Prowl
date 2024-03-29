using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using Prowl.Runtime;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Utils;
using Silk.NET.SDL;
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

                AudioClip audioClip = AudioClip.Create(file.Name, audioData, numChannels, bitsPerSample, sampleRate);
                return audioClip;
            }
        }

        #endregion

    }

    [CustomEditor(typeof(AudioClipImporter))]
    public class AudioClipEditor : ScriptedEditor
    {
        enum Res { _1, _2, _4, _8, _16, _32, _64, _128, _256 }
        Res visRes = Res._64;

        ActiveAudio? preview;

        byte[] data8L;
        byte[] data8R;

        short[] data16;
        short[] data16R;
        float[] dataF;
        float[] dataFR;

        SerializedAsset serialized;

        public override void OnEnable()
        {
            serialized = AssetDatabase.LoadAsset((target as MetaFile).AssetPath);
            var audioClip = (AudioClip)serialized.Main;

            if (audioClip.Format == BufferAudioFormat.Mono8)
            {
                // nothing to do data is already in byte format
            }
            else if (audioClip.Format == BufferAudioFormat.Mono16)
            {
                short[] data = new short[audioClip.Data.Length / 2];
                Buffer.BlockCopy(audioClip.Data, 0, data, 0, audioClip.Data.Length);
                data16 = data;
            }
            else if (audioClip.Format == BufferAudioFormat.MonoF)
            {
                float[] data = new float[audioClip.Data.Length / 4];
                Buffer.BlockCopy(audioClip.Data, 0, data, 0, audioClip.Data.Length);
                dataF = data;
            }
            // Stereo
            else if (audioClip.Format == BufferAudioFormat.Stereo8)
            {
                // Handle Stereo
                data8L = new byte[audioClip.Data.Length / 2];
                data8R = new byte[audioClip.Data.Length / 2];

                // Separate interleaved stereo data into left and right channels
                for (int i = 0; i < audioClip.Data.Length; i++)
                {
                    if (i % 2 == 0)
                        data8L[i / 2] = audioClip.Data[i]; // Left channel
                    else
                        data8R[i / 2] = audioClip.Data[i]; // Right channel
                }

            }
            else if (audioClip.Format == BufferAudioFormat.Stereo16)
            {
                short[] data = new short[audioClip.Data.Length / 2];
                Buffer.BlockCopy(audioClip.Data, 0, data, 0, audioClip.Data.Length);
                data16 = new short[data.Length / 2];
                data16R = new short[data.Length / 2];

                // Separate interleaved stereo data into left and right channels
                for (int i = 0; i < data.Length; i++)
                {
                    if (i % 2 == 0)
                        data16[i / 2] = data[i]; // Left channel
                    else
                        data16R[i / 2] = data[i]; // Right channel
                }
            }
            else if (audioClip.Format == BufferAudioFormat.StereoF)
            {
                float[] data = new float[audioClip.Data.Length / 4];
                Buffer.BlockCopy(audioClip.Data, 0, data, 0, audioClip.Data.Length);
                dataF = new float[data.Length / 2];
                dataFR = new float[data.Length / 2];

                // Separate interleaved stereo data into left and right channels
                for (int i = 0; i < data.Length; i++)
                {
                    if (i % 2 == 0)
                        dataF[i / 2] = data[i]; // Left channel
                    else
                        dataFR[i / 2] = data[i]; // Right channel
                }
            }

        }

        public override void OnDisable()
        {
            if (preview != null)
            {
                preview.Stop();
                preview = null;
            }
        }

        public override void OnInspectorGUI()
        {
            var importer = (AudioClipImporter)(target as MetaFile).importer;

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
                ImPlotFlags flags = ImPlotFlags.None;
                float height = 250;
                // Mono has NoLegends flag
                if (audioClip.Format == BufferAudioFormat.Mono8 || audioClip.Format == BufferAudioFormat.Mono16 || audioClip.Format == BufferAudioFormat.MonoF)
                {
                    flags |= ImPlotFlags.NoLegend;
                    height = 150;
                }
                if (ImPlot.BeginPlot("Audio Waveform", new System.Numerics.Vector2(reg.X, height), flags))
                {
                    ImPlot.SetupAxes("Time (s)", "Amplitude", ImPlotAxisFlags.None, ImPlotAxisFlags.AutoFit);
                    ImPlot.SetupAxesLimits(0, audioClip.Duration, -1, 1);
                    ImPlot.SetupMouseText(ImPlotLocation.SouthEast, 0);
                    
                    int res = 1 << (int)visRes;
                    if (audioClip.Format == BufferAudioFormat.Mono8)
                    {
                        float xScale = ((1.0f / audioClip.Data.Length) * audioClip.Duration) * res;
                        ImPlot.PlotLine("Audio", ref audioClip.Data[0], audioClip.Data.Length / res, xScale, 0, 0, 0, res);
                    }
                    else if (audioClip.Format == BufferAudioFormat.Mono16)
                    {
                        float xScale = ((1.0f / data16.Length) * audioClip.Duration) * res;
                        ImPlot.PlotLine("Audio", ref data16[0], data16.Length / res, xScale, 0, 0, 0, 2 * res);
                    }
                    else if (audioClip.Format == BufferAudioFormat.MonoF)
                    {
                        float xScale = ((1.0f / dataF.Length) * audioClip.Duration) * res;
                        ImPlot.PlotLine("Audio", ref dataF[0], dataF.Length / res, xScale, 0, 0, 0, 4 * res);
                    }
                    else if (audioClip.Format == BufferAudioFormat.Stereo8)
                    {
                        float xScale = ((1.0f / data8L.Length) * audioClip.Duration) * res;
                        ImPlot.PlotLine("Left Channel", ref data8L[0], data8L.Length / res, xScale, 0, 0, 0, res);
                        ImPlot.PlotLine("Right Channel", ref data8R[0], data8R.Length / res, xScale, 0, 0, 0, res);
                    }
                    else if (audioClip.Format == BufferAudioFormat.Stereo16)
                    {
                        float xScale = ((1.0f / data16.Length) * audioClip.Duration) * res;
                        ImPlot.PlotLine("Left Channel", ref data16[0], data16.Length / res, xScale, 0, 0, 0, 2 * res);
                        ImPlot.PlotLine("Right Channel", ref data16R[0], data16R.Length / res, xScale, 0, 0, 0, 2 * res);
                    }
                    else if (audioClip.Format == BufferAudioFormat.StereoF)
                    {
                        float xScale = ((1.0f / dataF.Length) * audioClip.Duration) * res;
                        ImPlot.PlotLine("Left Channel", ref dataF[0], dataF.Length / res, xScale, 0, 0, 0, 4 * res);
                        ImPlot.PlotLine("Right Channel", ref dataFR[0], dataFR.Length / res, xScale, 0, 0, 0, 4 * res);
                    }


                    // playback position
                    if (preview != null && preview.IsPlaying)
                    {
                        // from 0-1
                        float playbackPos = preview.PlaybackPosition;

                        // Draw a vertical line
                        double pos = playbackPos * audioClip.Duration;

                        if(ImPlot.DragLineX(0, ref pos, new System.Numerics.Vector4(1f, 1f, 1f, 1f)))
                            preview.PlaybackPosition = (float)Mathf.Clamp01(pos / audioClip.Duration);

                        // If click on plot set playback position
                        if (ImPlot.IsPlotHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            double x = ImPlot.GetPlotMousePos().X;
                            preview.PlaybackPosition = (float)Mathf.Clamp01(x / audioClip.Duration);
                        }
                    }
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
