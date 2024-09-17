// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

public static class Mathf
{
    public static readonly float Epsilon = float.MinValue;

    public static bool ApproximatelyEquals(float value1, float value2) => Math.Abs(value1 - value2) < Epsilon;
}
