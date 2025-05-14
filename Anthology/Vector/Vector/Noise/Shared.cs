// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector;

public static partial class Noise
{
    // Common utility functions needed by all noise types
    static Float3 Mod289(Float3 x)
    {
        return x - Maths.Floor(x * (1.0f / 289.0f)) * 289.0f;
    }

    static Float2 Mod289(Float2 x)
    {
        return x - Maths.Floor(x * (1.0f / 289.0f)) * 289.0f;
    }

    static Float4 Mod289(Float4 x)
    {
        return x - Maths.Floor(x * (1.0f / 289.0f)) * 289.0f;
    }

    static float Mod289(float x)
    {
        return x - Maths.Floor(x * (1.0f / 289.0f)) * 289.0f;
    }

    static Float3 Mod7(Float3 x)
    {
        return x - Maths.Floor(x * (1.0f / 7.0f)) * 7.0f;
    }

    static Float4 Mod7(Float4 x)
    {
        return x - Maths.Floor(x * (1.0f / 7.0f)) * 7.0f;
    }

    static Float4 Mod(Float4 x, Float4 y)
    {
        return x - y * Maths.Floor(x / y);
    }

    static Float3 Mod(Float3 x, Float3 y)
    {
        return x - y * Maths.Floor(x / y);
    }

    static Float2 Mod(Float2 x, Float2 y)
    {
        return x - y * Maths.Floor(x / y);
    }

    static Float3 Permute(Float3 x)
    {
        return Mod289((34.0f * x + 10.0f) * x);
    }

    static Float4 Permute(Float4 x)
    {
        return Mod289((34.0f * x + 10.0f) * x);
    }

    static float Permute(float x)
    {
        return Mod289((34.0f * x + 10.0f) * x);
    }

    static float TaylorInvSqrt(float r)
    {
        return 1.79284291400159f - 0.85373472095314f * r;
    }

    static Float4 TaylorInvSqrt(Float4 r)
    {
        return new Float4(1.79284291400159f) - new Float4(0.85373472095314f) * r;
    }

    static Float3 Fade(Float3 t)
    {
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

    static Float2 Fade(Float2 t)
    {
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

    static Float4 Fade(Float4 t)
    {
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }
}
