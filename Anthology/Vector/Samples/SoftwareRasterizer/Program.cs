// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using static SoftwareRasterizer.Rasterizer.Shader;

namespace SoftwareRasterizer;

internal class Program
{
    const int width = 1080;
    const int height = 720;

    static void Main(string[] args)
    {
        var demo = new RaylibDemo();
        demo.Run();
    }
}
