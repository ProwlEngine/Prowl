// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using Raylib_cs;

using SoftwareRasterizer.Rasterizer;

namespace SoftwareRasterizer;

public unsafe class RaylibPresenter : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private Image _image;
    private Raylib_cs.Texture2D _texture;

    public RaylibPresenter(int width, int height)
    {
        _width = width;
        _height = height;

        // Create image and texture
        _image = Raylib.GenImageColor(width, height, Raylib_cs.Color.Black);
        Raylib.ImageFormat(ref _image, PixelFormat.UncompressedR32G32B32A32);
        _texture = Raylib.LoadTextureFromImage(_image);
    }

    public void Initialize(int width, int height)
    {
    }

    public void Present(FrameBuffer colors)
    {
        if (colors == null || colors.ColorTexture.Data.Length == 0)
            return;

        // Update image pixels
        fixed (void* colorData = &colors.ColorTexture.Data[0])
        {
            var length = Raylib.GetPixelDataSize(_width, _height, _image.Format);
            var sizeOfSource = Marshal.SizeOf(colors.ColorTexture.Data[0]);
            Buffer.MemoryCopy(
                colorData,                                     // source
                _image.Data,                                   // destination
                length,                                        // destinationSizeInBytes
                colors.ColorTexture.Data.Length * sizeOfSource // sourceBytesToCopy
            );
        }

        // Update texture and draw
        Raylib.UpdateTexture(_texture, _image.Data);
        //Raylib.DrawTexture(_texture, 0, 0, Raylib_cs.Color.White);
        Raylib.DrawTexturePro(_texture, new Rectangle(0, 0, _width, _height), new Rectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), new System.Numerics.Vector2(0, 0), 0.0f, Raylib_cs.Color.White);
    }

    public void Dispose()
    {
        Raylib.UnloadTexture(_texture);
        Raylib.UnloadImage(_image);
    }
}
