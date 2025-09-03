// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using OpenTK.Graphics.OpenGL4;

using Prowl.Vector;

using StbImageSharp;

namespace OpenTKSample;

public class TextureTK(int glHandle)
{
    public readonly int Handle = glHandle;

    public uint Width;
    public uint Height;


    public static TextureTK LoadFromFile(string path)
    {
        // Generate handle
        int handle = GL.GenTexture();

        // Bind the handle
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, handle);

        // OpenGL has it's texture origin in the lower left corner instead of the top left corner,
        // so we tell StbImageSharp to flip the image when loading.
        StbImage.stbi_set_flip_vertically_on_load(1);

        // Here we open a stream to the file and pass it to StbImageSharp to load.
        int width = 0;
        int height = 0;
        using (Stream stream = File.OpenRead(path))
        {
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            width = image.Width;
            height = image.Height;

            // Now that our pixels are prepared, it's time to generate a texture. We do this with GL.TexImage2D.
            // Arguments:
            //   The type of texture we're generating. There are various different types of textures, but the only one we need right now is Texture2D.
            //   Level of detail. We can use this to start from a smaller mipmap (if we want), but we don't need to do that, so leave it at 0.
            //   Target format of the pixels. This is the format OpenGL will store our image with.
            //   Width of the image
            //   Height of the image.
            //   Border of the image. This must always be 0; it's a legacy parameter that Khronos never got rid of.
            //   The format of the pixels, explained above. Since we loaded the pixels as RGBA earlier, we need to use PixelFormat.Rgba.
            //   Data type of the pixels.
            //   And finally, the actual pixels.
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
        }

        // Now that our texture is loaded, we can set a few settings to affect how the image appears on rendering.

        // First, we set the min and mag filter. These are used for when the texture is scaled down and up, respectively.
        // Here, we use Linear for both. This means that OpenGL will try to blend pixels, meaning that textures scaled too far will look blurred.
        // You could also use (amongst other options) Nearest, which just grabs the nearest pixel, which makes the texture look pixelated if scaled too far.
        // NOTE: The default settings for both of these are LinearMipmap. If you leave these as default but don't generate mipmaps,
        // your image will fail to render at all (usually resulting in pure black instead).
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        // Now, set the wrapping mode. S is for the X axis, and T is for the Y axis.
        // We set this to Repeat so that textures will repeat when wrapped. Not demonstrated here since the texture coordinates exactly match
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // This is the last step. We need to generate mipmaps for our texture. Mipmaps are smaller versions of the texture that OpenGL generates for us.
        // This is used for when the texture is scaled down. If we don't generate mipmaps, OpenGL will use the full texture for all sizes.
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

        return new TextureTK(handle) { Width = (uint)width, Height = (uint)height };
    }

    // Create a new texture with the specified width and height
    // This is used for when we want to create a texture from scratch, like for a fonts.
    // This is a bit different from the LoadFromFile method, since we don't have any pixels to load.
    // We just create an empty texture with the specified width and height.
    public static TextureTK CreateNew(uint width, uint height)
    {
        // Generate handle
        int handle = GL.GenTexture();
        // Bind the handle
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, handle);
        // Create empty texture
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, (int)width, (int)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        // Set texture parameters 
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return new TextureTK(handle) { Width = width, Height = height };
    }

    // Set the texture data for a specific region of the texture
    // This is used for when we want to update a specific region of the texture
    public void SetData(IntRect bounds, byte[] data)
    {
        // Bind the texture
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        // Set the texture data
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, bounds.x, bounds.y, bounds.width, bounds.height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
    }

    // Activate texture
    // Multiple textures can be bound, if your shader needs more than just one.
    // If you want to do that, use GL.ActiveTexture to set which slot GL.BindTexture binds to.
    // The OpenGL standard requires that there be at least 16, but there can be more depending on your graphics card.
    public void Use(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
    }

    // Dispose of the texture
    public void Dispose()
    {
        // Delete the texture
        GL.DeleteTexture(Handle);
    }
}
