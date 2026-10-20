// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Video;

/// <summary>
/// Imports video files (.ogv, .webm, .mkv) into "VideoClip" assets.
/// </summary>
public sealed class VideoRuntimeImporter : RuntimeImporter
{
    /// Warning:
    /// Uncle Bane Warning for anyone trying to add ".mp4" (H.264/H.265) Video Loading/Decoding here
    /// The Via LA (previously known as MPEG LA Licensing and Patent Group) will take legal action
    /// at somepoint if you decide to load "MP4s" in your software without paying them royalties/license
    /// Just using FFMPEG H.264 decoder doesnt magically give you legal right to use the tech, if you for some
    /// absolute reason must load/playback those codecs, here https://www.via-la.com/licensing-programs/avc-h-264
    /// Otherwise save yourself the misery of this and use a royalty free video codec like VP9/AV1 for your video files.
    public override IReadOnlyList<string> Extensions { get; } =
        [".ogv", ".webm", ".mkv"];

    public override Type AssetType => typeof(VideoClip);

    public override void Import(RuntimeImportContext context)
    {
        var clip = new VideoClip();

        // If the source is a local folder on disk, we can store LocalPath to stream directly
        // without keeping a huge video file in RAM. Otherwise (Zip/HTTP/Pak), store the bytes.
        if (context.LocalPath is { } localPath)
        {
            clip._streamFilePath = localPath;
            if (FFmpegLoader.IsAvailable)
            {
                using var probe = new FFmpegVideoDecoder();
                if (probe.TryOpenFile(localPath))
                {
                    clip._width = probe.Width;
                    clip._height = probe.Height;
                    clip._duration = probe.Duration;
                    clip._frameRate = probe.FrameRate;
                }
            }
        }
        else
        {
            byte[] bytes = context.ReadAllBytes();
            clip._encodedData = bytes;
            if (FFmpegLoader.IsAvailable)
            {
                using var probe = new FFmpegVideoDecoder();
                if (probe.TryOpenMemory(bytes))
                {
                    clip._width = probe.Width;
                    clip._height = probe.Height;
                    clip._duration = probe.Duration;
                    clip._frameRate = probe.FrameRate;
                }
            }
        }

        context.SetMain(clip);
    }
}
