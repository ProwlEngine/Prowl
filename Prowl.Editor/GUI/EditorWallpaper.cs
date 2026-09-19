using System;
using System.IO;

using Prowl.Aperture;
using Prowl.Editor.Theming;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.GUI;

/// <summary>
/// The user's wallpaper image for the editor background. Read straight from disk, never imported into
/// the project, and reloaded only when the chosen path changes.
/// </summary>
public static class EditorWallpaper
{
    private static string? _path;
    private static Texture2D? _texture;

    /// <summary>The loaded wallpaper for <paramref name="path"/>, or null when it is unset, missing or can't be decoded.</summary>
    public static Texture2D? Get(string path)
    {
        if (path == _path) return _texture;

        Release();
        _path = path;
        if (string.IsNullOrEmpty(path)) return null;

        if (!File.Exists(path))
        {
            Debug.LogWarning($"Background image not found: {path}");
            return null;
        }

        try
        {
            using Aperture.Image image = Aperture.Image.Load(path, new DecodeOptions { TargetPixelFormat = PixelFormat.Rgba8 });
            var texture = new Texture2D((uint)image.Width, (uint)image.Height, false, TextureImageFormat.Color4b);
            texture.SetData<byte>(image.RootFrame.Pixels.ToArray());
            texture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            _texture = texture;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Background image could not be loaded: {path} ({ex.Message})");
        }
        return _texture;
    }

    /// <summary>Draw <paramref name="texture"/> over <paramref name="rect"/> the way a desktop wallpaper is fitted, then darken it by <paramref name="dim"/>.</summary>
    public static void Draw(Canvas canvas, Rect rect, Texture2D texture, BackgroundImageFit fit, float dim)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y, w = (float)rect.Size.X, h = (float)rect.Size.Y;
        float iw = texture.Width, ih = texture.Height;

        switch (fit)
        {
            case BackgroundImageFit.Stretch:
                canvas.DrawImage(texture, x, y, w, h);
                break;

            case BackgroundImageFit.Tile:
                for (float ty = y; ty < y + h; ty += ih)
                    for (float tx = x; tx < x + w; tx += iw)
                        canvas.DrawImage(texture, tx, ty, iw, ih);
                break;

            default:
                float scale = fit switch
                {
                    BackgroundImageFit.Fill => MathF.Max(w / iw, h / ih),
                    BackgroundImageFit.Fit => MathF.Min(w / iw, h / ih),
                    _ => 1f,
                };
                float dw = iw * scale, dh = ih * scale;
                canvas.DrawImage(texture, x + (w - dw) * 0.5f, y + (h - dh) * 0.5f, dw, dh);
                break;
        }

        if (dim > 0f)
        {
            canvas.BeginPath();
            canvas.Rect(x, y, w, h);
            canvas.SetFillColor(Color32.FromArgb((int)(Math.Clamp(dim, 0f, 1f) * 255), 0, 0, 0));
            canvas.Fill();
        }
    }

    private static void Release()
    {
        if (_texture.IsValid()) _texture.Dispose();
        _texture = null;
    }
}
