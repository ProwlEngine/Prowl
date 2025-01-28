// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Icons;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.Rendering;

using Vortice.Direct3D11;

namespace Prowl.Runtime.Components.UI;

[AddComponentMenu($"{FontAwesome6.Tv}  UI/{FontAwesome6.WindowMaximize}  Canvas")]
[ExecutionOrder(int.MaxValue)] // Ensure canvas is updated last, this is important for UI otherwise it could be 1 frame behind
[ExecuteAlways]
public sealed class Canvas : MonoBehaviour
{
    public Vector2 referenceResolution = new(1280, 720);
    public enum ScaleMode
    {
        ConstantPixelSize,
        ScaleWithScreenSize
    }

    public enum RenderMode
    {
        ScreenSpace,
        WorldSpace
    }

    public RenderMode renderMode = RenderMode.ScreenSpace;
    [ShowIf(nameof(IsWorldSpace))] public Camera? worldCamera;
    public ScaleMode scaleMode = ScaleMode.ScaleWithScreenSize;
    [ShowIf(nameof(IsScaleWithScreenSize))] public double matchWidthOrHeight = 0.5f;
    public double sortOrder = 0;
    public CanvasEventHandler inputHandler;

    private Vector2 _lastScreenSize;
    private Vector2 _currentScale = Vector2.one;
    private bool _needsCanvasRebuild = true;
    private UIDrawList _drawList;
    private RenderTexture _renderTarget;

    public Vector2 CanvasScale => _currentScale;

    private bool IsWorldSpace => renderMode == RenderMode.WorldSpace;
    private bool IsScaleWithScreenSize => scaleMode == ScaleMode.ScaleWithScreenSize;

    public override void Awake()
    {
        _lastScreenSize = new Vector2(Screen.Width, Screen.Height);
        _currentScale = CalculateScaleFactor();

        ValidateInputHandler();

        _drawList = new UIDrawList(true);
    }

    public override void LateUpdate()
    {
        if (worldCamera == null && IsWorldSpace)
        {
            Debug.LogWarning("Canvas is in World Space but no Camera is assigned, Defaulting to Screen Space");
            renderMode = RenderMode.ScreenSpace;
        }

        ValidateInputHandler();

        // Check if canvas properties have changed
        if (HasScreenSizeChanged())
        {
            OnValidate();
        }

        if (_needsCanvasRebuild)
        {
            _needsCanvasRebuild = false;
            Console.WriteLine("Rebuilding Canvas");
            int width = Screen.Width;
            int height = Screen.Height;
            if (IsWorldSpace)
            {
                width = (int)referenceResolution.x;
                height = (int)referenceResolution.y;
            }
            width = MathD.Max(width, 1);
            height = MathD.Max(height, 1);

            if ((_renderTarget == null) || (width != _renderTarget.Width || height != _renderTarget.Height))
                RefreshRenderTexture(new(width, height));

            Veldrid.CommandList commandList = Graphics.GetCommandList();
            commandList.Name = "Canvas Command Buffer";

            commandList.SetFramebuffer(_renderTarget.Framebuffer);
            commandList.ClearColorTarget(0, Veldrid.RgbaFloat.Clear);
            commandList.ClearDepthStencil(1.0f);


            // Draw all UIElements
            _drawList.Clear();
            _drawList.PushTexture(Font.DefaultFont.Texture);
            System.Collections.Generic.IEnumerable<CanvasElement> elements = GetComponentsInChildren<CanvasElement>();
            foreach (CanvasElement element in elements)
            {
                element.Draw(_drawList);
            }
            UIDrawListRenderer.Draw(commandList, [_drawList], new Vector2(width, height), 1f);


            Graphics.SubmitCommandList(commandList);

            commandList.Dispose();
        }

        // Drawing, We will do this by pushing a Canvas Renderable
        if (IsWorldSpace)
        {
            // TODO: Draw the Canvas in World Space (Bassically just a quad mesh drawn like a MeshRenderer for this canvas)
        }
        else
        {
            // TODO: Draw the Canvas in Screen Space, I suppose we could use a matrix to align the canvas to the screen
        }
    }

    private void RefreshRenderTexture(Vector2 renderSize)
    {
        _renderTarget?.DestroyImmediate();

        _renderTarget = new RenderTexture(
            (uint)renderSize.x,
            (uint)renderSize.y,
            true);
    }

    private void ValidateInputHandler()
    {
        if (inputHandler == null)
        {
            // find one in scene
            inputHandler = FindObjectsOfType<CanvasEventHandler>().FirstOrDefault();
            // Create one if none found
            if (inputHandler == null)
            {
                Debug.Log($"Canvas {GameObject.Name} Could not find a InputHandler in the scene, Creating default one...");
                var obj = new GameObject("Canvas Input Handler");
                inputHandler = obj.AddComponent<CanvasEventHandler>();
                obj.AddComponent<MouseInputModule>();
                Scene.Add(obj);
            }
        }
    }

    private bool HasScreenSizeChanged()
    {
        Vector2 currentScreenSize = new(Screen.Width, Screen.Height);
        if (currentScreenSize != _lastScreenSize)
        {
            _lastScreenSize = currentScreenSize;
            return true;
        }
        return false;
    }

    public override void OnValidate()
    {
        _currentScale = CalculateScaleFactor();
        ForceRebuildAllRects();
    }

    public void SetCanvasDirty()
    {
        _needsCanvasRebuild = true;
    }

    public void ForceRebuildAllRects()
    {
        // Since Rects rebuild their children, we only need to rebuild the root Rects in each path
        TrickleRebuild(GameObject);
    }

    private static void TrickleRebuild(GameObject obj)
    {
        RectTransform? rect = obj.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.Rebuild();
            return;
        }

        foreach (GameObject child in obj.children)
            TrickleRebuild(child);
    }

    private Vector2 CalculateScaleFactor()
    {
        Vector2 screenSize = new(Screen.Width, Screen.Height);

        switch (scaleMode)
        {
            case ScaleMode.ScaleWithScreenSize:
                double logWidth = MathD.Log(screenSize.x / referenceResolution.x, 2);
                double logHeight = MathD.Log(screenSize.y / referenceResolution.y, 2);
                double logWeighted = MathD.Lerp(logWidth, logHeight, matchWidthOrHeight);
                return Vector2.one * MathD.Pow(2, logWeighted);

            default:
                return Vector2.one;
        }
    }

    public override void DrawGizmos()
    {
        Debug.PushMatrix(Transform.localToWorldMatrix);

        // Draw screen bounds as a wire cube
        Vector3 screenCenter = new(Screen.Width * 0.5f, Screen.Height * 0.5f, 0);
        Vector3 screenSize = new(Screen.Width, Screen.Height, 0);
        Debug.DrawWireCube(screenCenter, screenSize * 0.5f, Color.white);

        // Draw reference resolution bounds
        Vector3 refCenter = new(referenceResolution.x * 0.5f, referenceResolution.y * 0.5f, 0);
        Vector3 refSize = new(referenceResolution.x, referenceResolution.y, 0);
        Debug.DrawWireCube(refCenter, refSize * 0.5f, Color.yellow);

        // Draw scale indicator
        Vector3 scaleStart = Vector3.zero;
        Vector3 scaleEnd = new(_currentScale.x * 100, 0, 0); // 100 pixel reference line
        Debug.DrawLine(scaleStart, scaleEnd, Color.green);
        Debug.DrawArrow(scaleEnd, Vector3.right * 10, Color.green);

        Debug.PopMatrix();

        Debug.DrawImage(_renderTarget.ColorBuffers[0], new Vector3(Screen.Width / 2.0, Screen.Height / 2.0, 0), new Vector2(Screen.Width, Screen.Height), Color.white, Transform.localToWorldMatrix);
    }
}

[RequireComponent(typeof(RectTransform))]
public abstract class CanvasElement : MonoBehaviour
{
    private RectTransform? _rectTransform;
    public RectTransform RectTransform => (_rectTransform == null) ?_rectTransform = GetComponent<RectTransform>() : _rectTransform;

    public abstract void Draw(UIDrawList drawList);

    public override void OnValidate() => RectTransform?.TargetCanvas.SetCanvasDirty();
}

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
