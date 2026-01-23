using System;
using Prowl.Vector;
using System.Collections.Generic;
using Prowl.Vector.Geometry;

namespace Prowl.Quill
{
    /// <summary>
    /// Interface for implementing a canvas renderer backend.
    /// Implement this interface to provide rendering support for different graphics APIs (OpenGL, DirectX, Vulkan, etc.).
    /// </summary>
    public interface ICanvasRenderer : IDisposable
    {
        /// <summary>
        /// Creates a new texture with the specified dimensions.
        /// </summary>
        /// <param name="width">The width of the texture in pixels.</param>
        /// <param name="height">The height of the texture in pixels.</param>
        /// <returns>A backend-specific texture object.</returns>
        public object CreateTexture(uint width, uint height);

        /// <summary>
        /// Gets the dimensions of a texture.
        /// </summary>
        /// <param name="texture">The texture object to query.</param>
        /// <returns>The size of the texture in pixels.</returns>
        public Int2 GetTextureSize(object texture);

        /// <summary>
        /// Updates a region of a texture with new pixel data.
        /// </summary>
        /// <param name="texture">The texture to update.</param>
        /// <param name="bounds">The rectangular region to update.</param>
        /// <param name="data">The pixel data in RGBA format (4 bytes per pixel).</param>
        public void SetTextureData(object texture, IntRect bounds, byte[] data);

        /// <summary>
        /// Renders the accumulated draw calls to the screen or render target.
        /// </summary>
        /// <param name="canvas">The canvas containing vertices and indices.</param>
        /// <param name="drawCalls">The list of draw calls to render.</param>
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls);
    }
}
