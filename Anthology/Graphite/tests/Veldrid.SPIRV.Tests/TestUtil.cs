using System;
using System.IO;

namespace Veldrid.SPIRV.Tests
{
    internal static class TestUtil
    {
        public static string LoadShaderText(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestShaders", name));
        }

        public static byte[] LoadBytes(string name)
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestShaders", name));
        }
    }
}
