// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>
/// An interface for implementing audio effects.
/// </summary>
public interface IAudioEffect
{
    void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels);
    void OnDestroy();
}
