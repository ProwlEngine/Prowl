// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime.UI;

[AddComponentMenu($"{FontAwesome6.Tv}  UI/{FontAwesome6.Image}  Image")]
public sealed class ImageElement : CanvasElement, ICanvasRaycastHandler
{
    public AssetRef<Texture2D> texture;
    public Color color = Color.white;
    public bool keepAspect = true;
    public bool ignoreRaycast = false;

    public override void Draw(UIDrawList drawList)
    {
        if (!texture.IsAvailable) return;
        if (RectTransform == null) return;

        Vector2 position = RectTransform.CalculatedRect.Position;
        Vector2 size = RectTransform.CalculatedRect.Size;

        if (keepAspect)
        {
            double aspectRatio = (double)texture.Res.Width / texture.Res.Height;
            double rectAspectRatio = size.x / size.y;

            if (aspectRatio < rectAspectRatio)
            {
                // Fit height, adjust width
                double adjustedWidth = size.y * aspectRatio;
                double widthDiff = (size.x - adjustedWidth) / 2.0;
                position.x += widthDiff;
                size.x = adjustedWidth;
            }
            else
            {
                // Fit width, adjust height
                double adjustedHeight = size.x / aspectRatio;
                double heightDiff = (size.y - adjustedHeight) / 2.0;
                position.y += heightDiff;
                size.y = adjustedHeight;
            }
        }

        drawList.AddImage(texture.Res, position, position + size, new(0, 1), new(1, 0), color);
    }

    public bool ProcessRaycast(Vector2 screenPosition) => !ignoreRaycast;
}
