using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Video;

namespace Prowl.Editor.Importers;

/// Warning:
/// Uncle Bane Warning for anyone trying to add ".mp4" (H.264/H.265) Video Loading/Decoding here
/// The Via LA (previously known as MPEG LA Licensing and Patent Group) will take legal action
/// at somepoint if you decide to load "MP4s" in your software without paying them royalties/license
/// Just using FFMPEG H.264 decoder doesnt magically give you legal right to use the tech, if you for some
/// absolute reason must load/playback those codecs, here https://www.via-la.com/licensing-programs/avc-h-264
/// Otherwise save yourself the misery of this and use a royalty free video codec like VP9/AV1 for your video files.
[ImporterFor(".ogv", ".webm", ".mkv")]
public class VideoImporter : AssetImporter
{
    // Bump this version to force reimport of all videos if import logic changes.
    public override int Version => 1;

    public override EchoObject? DefaultSettings()
    {
        var settings = EchoObject.NewCompound();
        // Default to false to stream from disk and keep .prowlpak sizes small.
        settings["embedBytes"] = new EchoObject(false);
        return settings;
    }

    public override bool Import(ImportContext ctx)
    {
        try
        {
            bool embedBytes = ReadBool(ctx, "embedBytes", false);

            byte[] data = embedBytes ? System.IO.File.ReadAllBytes(ctx.AbsolutePath) : [];
            var clip = new VideoClip(data)
            {
                Name = ctx.FileName,
                _streamFilePath = embedBytes ? string.Empty : ctx.AbsolutePath
            };

            // Probe metadata so the Inspector can show Resolution, Duration, and FPS
            if (FFmpegLoader.IsAvailable)
            {
                using var decoder = new FFmpegVideoDecoder();
                bool opened = embedBytes && data.Length > 0
                    ? decoder.TryOpenMemory(data)
                    : decoder.TryOpenFile(ctx.AbsolutePath);

                if (opened)
                {
                    clip._width = decoder.Width;
                    clip._height = decoder.Height;
                    clip._duration = decoder.Duration;
                    clip._frameRate = decoder.FrameRate;
                }
            }

            ctx.SetMainAsset(clip);
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to import video: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
    }

    private static bool ReadBool(ImportContext ctx, string key, bool fallback)
        => ctx.Settings != null && ctx.Settings.TryGet(key, out EchoObject? value) && value != null ? value.BoolValue : fallback;
}
