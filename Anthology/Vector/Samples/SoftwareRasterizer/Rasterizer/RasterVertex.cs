// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace SoftwareRasterizer.Rasterizer;

public struct RasterVertex
{
    public Float4 Position;
    public Float4[] Varyings;
    public Float3 ScreenPosition;
}
