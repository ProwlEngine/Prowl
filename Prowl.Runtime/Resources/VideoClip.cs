// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

public sealed class VideoClip : Asset
{
    [SerializeField] internal byte[] _encodedData = [];
    [SerializeField] internal string _streamFilePath = string.Empty;
    [SerializeField] internal int _width;
    [SerializeField] internal int _height;
    [SerializeField] internal double _duration;
    [SerializeField] internal double _frameRate;

    public VideoClip() : base("New VideoClip") { }

    public VideoClip(byte[] encodedData, int width = 0, int height = 0, double duration = 0, double frameRate = 0) : base("New VideoClip")
    {
        _encodedData = encodedData ?? [];
        _width = width;
        _height = height;
        _duration = duration;
        _frameRate = frameRate;
    }

    public int Width { get { EnsureLoaded(); return _width; } }
    public int Height { get { EnsureLoaded(); return _height; } }
    public double Duration { get { EnsureLoaded(); return _duration; } }
    public double FrameRate { get { EnsureLoaded(); return _frameRate; } }

    public byte[] EncodedData
    {
        get
        {
            EnsureLoaded();
            return _encodedData;
        }
    }

    public string StreamFilePath
    {
        get
        {
            EnsureLoaded();
            return _streamFilePath;
        }
    }

    protected internal override long EstimateBytes()
        => _encodedData?.LongLength ?? 0;

    protected override void OnUnload()
    {
        _encodedData = [];
        _streamFilePath = string.Empty;
    }
}
