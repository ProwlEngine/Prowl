using System;
using System.Diagnostics;
using System.Text;
using Silk.NET.OpenGL;

namespace NeoVeldrid.OpenGL
{
    internal static class OpenGLUtil
    {
        internal static GL GL;
        internal static bool HasGlObjectLabel;

        private static int? MaxLabelLength;

        [Conditional("DEBUG")]
        [DebuggerNonUserCode]
        internal static void CheckLastError()
        {
            uint error = (uint)GL.GetError();
            if (error != 0)
            {
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                throw new NeoVeldridException("glGetError indicated an error: " + (ErrorCode)error);
            }
        }

        internal static unsafe void SetObjectLabel(ObjectIdentifier identifier, uint target, string name)
        {
            if (HasGlObjectLabel)
            {
                int byteCount = Encoding.UTF8.GetByteCount(name);
                if (MaxLabelLength == null)
                {
                    GL.GetInteger(GetPName.MaxLabelLength, out int maxLabelLength);
                    CheckLastError();
                    MaxLabelLength = maxLabelLength;
                }
                if (byteCount >= MaxLabelLength)
                {
                    name = name.Substring(0, MaxLabelLength.Value - 4) + "...";
                    byteCount = Encoding.UTF8.GetByteCount(name);
                }

                Span<byte> utf8bytes = stackalloc byte[128];
                if(byteCount + 1 > 128) utf8bytes = new byte[byteCount + 1];

                fixed (char* namePtr = name)
                fixed (byte* utf8bytePtr = utf8bytes)
                {
                    int written = Encoding.UTF8.GetBytes(namePtr, name.Length, utf8bytePtr, byteCount);
                    utf8bytePtr[written] = 0;
                    GL.ObjectLabel(identifier, target, (uint)byteCount, utf8bytePtr);
                    CheckLastError();
                }
            }
        }

        internal static TextureTarget GetTextureTarget(OpenGLTexture glTex, uint arrayLayer)
        {
            if ((glTex.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
            {
                switch (arrayLayer % 6)
                {
                    case 0:
                        return TextureTarget.TextureCubeMapPositiveX;
                    case 1:
                        return TextureTarget.TextureCubeMapNegativeX;
                    case 2:
                        return TextureTarget.TextureCubeMapPositiveY;
                    case 3:
                        return TextureTarget.TextureCubeMapNegativeY;
                    case 4:
                        return TextureTarget.TextureCubeMapPositiveZ;
                    case 5:
                        return TextureTarget.TextureCubeMapNegativeZ;
                }
            }

            return glTex.TextureTarget;
        }
    }
}
