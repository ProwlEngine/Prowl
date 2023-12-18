using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    public class RenderBuffer : IDisposable
    {
        public enum RenderbufferFormat
        {
            Color4b = 32856,

            Float = 33326,
            Float2 = 33328,
            Float4 = 34836,

            Int = 33333,
            Int2 = 33339,
            Int4 = 36226,

            UnsignedInt = 33334,
            UnsignedInt2 = 33340,
            UnsignedInt4 = 36208,

            Depth16 = 33189,
            Depth24 = 33190,
            Depth32f = 36012,
            Depth24Stencil8 = 35056,
            Depth32fStencil8 = 36013,
            Stencil8 = 36168,
        }

        /// <summary>The handle for the GL Renderbuffer Object.</summary>
        public readonly uint Handle;

        /// <summary>The width of this <see cref="RenderBuffer"/>.</summary>
        public readonly uint Width;

        /// <summary>The height of this <see cref="RenderBuffer"/>.</summary>
        public readonly uint Height;

        /// <summary>The amount of samples this <see cref="RenderBuffer"/> has.</summary>
        public readonly uint Samples;

        /// <summary>The format for this <see cref="RenderBuffer"/>.</summary>
        public readonly RenderbufferFormat Format;

        /// <summary>Gets whether the format of this <see cref="RenderBuffer"/> is depth-only.</summary>
        public bool IsDepthOnly => Format == RenderbufferFormat.Depth16 || Format == RenderbufferFormat.Depth24 || Format == RenderbufferFormat.Depth32f;

        /// <summary>Gets whether the format of this <see cref="RenderBuffer"/> is stencil-only.</summary>
        public bool IsStencilOnly => Format == RenderbufferFormat.Stencil8;

        /// <summary>Gets whether the format of this <see cref="RenderBuffer"/> is depth-stencil.</summary>
        public bool IsDepthStencil => Format == RenderbufferFormat.Depth24Stencil8 || Format == RenderbufferFormat.Depth32fStencil8;

        /// <summary>Gets whether the format of this <see cref="RenderBuffer"/> is color-renderable.</summary>
        public bool IsColorRenderableFormat => !(IsDepthOnly || IsStencilOnly || IsDepthStencil);

        /// <summary>
        /// Creates a <see cref="RenderBuffer"/> with the specified format.
        /// </summary>
        /// <param name="width">The width for the <see cref="RenderBuffer"/>.</param>
        /// <param name="height">The height for the <see cref="RenderBuffer"/>.</param>
        /// <param name="format">The format for the <see cref="RenderBuffer"/>'s storage.</param>
        /// <param name="samples">The amount of samples the <see cref="RenderBuffer"/> will have.</param>
        public RenderBuffer(uint width, uint height, RenderbufferFormat format, uint samples = 0)
        {
            if (!Enum.IsDefined(typeof(RenderbufferFormat), format))
                throw new ArgumentException("Invalid renderbuffer format", nameof(format));

            if (width <= 0 || width > Graphics.MaxRenderbufferSize)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be in the range (0, " + nameof(Graphics.MaxRenderbufferSize) + "]");

            if (height <= 0 || height > Graphics.MaxRenderbufferSize)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be in the range (0, " + nameof(Graphics.MaxRenderbufferSize) + "]");

            ValidateSampleCount(samples);

            Handle = Graphics.GL.GenRenderbuffer();
            Format = format;
            Width = width;
            Height = height;
            Samples = samples;
            Graphics.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, Handle);
            Graphics.GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples, (InternalFormat)format, Width, Height);
            Graphics.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

            Graphics.CheckGL();
        }

        public void Dispose()
        {
            Graphics.GL.DeleteRenderbuffer(Handle);
        }

        internal void ValidateSampleCount(uint samples)
        {
            if (samples < 0 || samples > Graphics.MaxSamples)
                throw new ArgumentOutOfRangeException(nameof(samples), samples, "The sample count must be in the range [0, " + nameof(Graphics.MaxSamples) + "]");
        }
    }
}
