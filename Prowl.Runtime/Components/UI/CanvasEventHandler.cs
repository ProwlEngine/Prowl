// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Icons;

namespace Prowl.Runtime.UI;

public enum CanvasInputType { PointerEnter, PointerExit, PointerDown, PointerUp, Scroll }

public class CanvasInputEvent(CanvasInputType type, Vector2? scrollDelta, CanvasInputModule source)
{
    public CanvasInputType Type { get; } = type;
    public Vector2? ScrollDelta { get; } = scrollDelta;
    public CanvasInputModule Source { get; } = source;
}

[AddComponentMenu($"{FontAwesome6.Tv}  UI/{FontAwesome6.Fingerprint}  Canvas Event Handler")]
public class CanvasEventHandler : MonoBehaviour
{
    private readonly List<CanvasInputModule> _activeModules = new();
    // Track last hovered element per module
    private readonly Dictionary<CanvasInputModule, RectTransform?> _lastHoveredElements = new();
    private readonly Dictionary<CanvasInputModule, RectTransform?> _hoveredElements = new();

    private void ProcessRaycast(CanvasInputModule module)
    {
        _hoveredElements[module] = null;
        Canvas?[] canvases = FindObjectsOfType<Canvas>();
        foreach (Canvas? canvas in canvases.OrderByDescending(x => x.sortOrder))
        {
            if (!module.TryGetPointerPosition(canvas, out Vector2 screenPosition))
                continue;

            ProcessRaycastRecursive(canvas.GameObject, screenPosition, module);
            if (_hoveredElements[module] != null)
                break;
        }
    }

    private void ProcessRaycastRecursive(GameObject obj, Vector2 screenPosition, CanvasInputModule module)
    {
        RectTransform? rect = obj.GetComponent<RectTransform>();
        ICanvasRaycastHandler handler = obj.GetComponent(typeof(ICanvasRaycastHandler)) as ICanvasRaycastHandler;
        if (rect != null && handler != null)
        {
            if (rect.CalculatedRect.Contains(screenPosition))
            {
                if (handler.ProcessRaycast(screenPosition))
                    _hoveredElements[module] = rect;
            }
        }

        foreach (GameObject child in obj.children)
            ProcessRaycastRecursive(child, screenPosition, module);
    }

    public override void Update()
    {
        foreach (CanvasInputModule module in _activeModules)
        {
            module.UpdateModule();

            // Ensure module exists in tracking dictionaries
            if (!_lastHoveredElements.ContainsKey(module))
                _lastHoveredElements[module] = null;
            if (!_hoveredElements.ContainsKey(module))
                _hoveredElements[module] = null;

            ProcessRaycast(module);

            // Handle pointer enter/exit events
            if (_hoveredElements[module] != _lastHoveredElements[module])
            {
                // Exit event for previously hovered element
                if (_lastHoveredElements[module] != null)
                {
                    var exitEvent = new CanvasInputEvent(
                        CanvasInputType.PointerExit,
                        null,
                        module
                    );
                    DispatchInputEvent(exitEvent, _lastHoveredElements[module]);
                }

                // Enter event for newly hovered element
                if (_hoveredElements[module] != null)
                {
                    var enterEvent = new CanvasInputEvent(
                        CanvasInputType.PointerEnter,
                        null,
                        module
                    );
                    DispatchInputEvent(enterEvent, _hoveredElements[module]);
                }
            }

            // Handle other events
            if (module.IsPointerDownThisFrame)
            {
                var evt = new CanvasInputEvent(CanvasInputType.PointerDown, null, module);
                DispatchInputEvent(evt, _hoveredElements[module]);
            }
            else if (module.IsPointerUpThisFrame)
            {
                var evt = new CanvasInputEvent(CanvasInputType.PointerUp, null, module);
                DispatchInputEvent(evt, _hoveredElements[module]);
            }

            if (module.HasScroll)
            {
                var evt = new CanvasInputEvent(CanvasInputType.Scroll, module.ScrollDelta, module);
                DispatchInputEvent(evt, _hoveredElements[module]);
            }

            // Update last hovered element for next frame
            _lastHoveredElements[module] = _hoveredElements[module];
        }
    }

    private static void DispatchInputEvent(CanvasInputEvent evt, RectTransform? target)
    {
        if (target == null) return;

        IEnumerable<ICanvasInputHandler> handlers = target.GetComponents(typeof(ICanvasRaycastHandler)).Cast<ICanvasInputHandler>();
        foreach (ICanvasInputHandler handler in handlers)
            handler?.HandleInput(evt);
    }

    public void RegisterInputModule(CanvasInputModule module)
    {
        if (!_activeModules.Contains(module))
        {
            _activeModules.Add(module);
            _lastHoveredElements[module] = null;
            _hoveredElements[module] = null;
        }
    }

    public void UnregisterInputModule(CanvasInputModule module)
    {
        _activeModules.Remove(module);
        _lastHoveredElements.Remove(module);
        _hoveredElements.Remove(module);
    }
}

public abstract class CanvasInputModule : MonoBehaviour
{
    public abstract Vector2? ScrollDelta { get; }
    public abstract bool IsPointerDownThisFrame { get; }
    public abstract bool IsPointerUpThisFrame { get; }
    public abstract bool HasScroll { get; }

    public override void OnEnable() => GetComponent<CanvasEventHandler>().RegisterInputModule(this);
    public override void OnDisable() => GetComponent<CanvasEventHandler>().UnregisterInputModule(this);

    public abstract void UpdateModule();
    public abstract bool TryGetPointerPosition(Canvas canvas, out Vector2 screenPosition);
}

[AddComponentMenu($"{FontAwesome6.Tv}  UI/Modules/{FontAwesome6.ComputerMouse}  Mouse Input Module")]
[RequireComponent(typeof(CanvasEventHandler))]
public class MouseInputModule : CanvasInputModule
{
    private Vector2? _scrollDelta;
    private bool _isPointerDownThisFrame;
    private bool _isPointerUpThisFrame;
    private bool _wasPointerDown;

    public override Vector2? ScrollDelta => _scrollDelta;
    public override bool IsPointerDownThisFrame => _isPointerDownThisFrame;
    public override bool IsPointerUpThisFrame => _isPointerUpThisFrame;
    public override bool HasScroll => _scrollDelta.HasValue && _scrollDelta.Value != Vector2.zero;

    public override void UpdateModule()
    {
        // Update scroll
        _scrollDelta = new Vector2(0, Input.MouseWheelDelta);

        // Update pointer state
        bool isPointerDown = Input.GetMouseButton(0);
        _isPointerDownThisFrame = !_wasPointerDown && isPointerDown;
        _isPointerUpThisFrame = _wasPointerDown && !isPointerDown;
        _wasPointerDown = isPointerDown;
    }

    public override bool TryGetPointerPosition(Canvas canvas, out Vector2 screenPosition)
    {
        screenPosition = Vector2.zero;

        switch (canvas.renderMode)
        {
            case Canvas.RenderMode.ScreenSpace:
                screenPosition = Input.MousePosition;
                return true;

            case Canvas.RenderMode.WorldSpace:
                return TryGetWorldSpacePosition(canvas, Input.MousePosition, out screenPosition);

            default:
                return false;
        }
    }

    private static bool TryGetWorldSpacePosition(Canvas canvas, Vector2 mousePosition, out Vector2 screenPosition)
    {
        screenPosition = Vector2.zero;

        Camera? camera = canvas.worldCamera;
        if (camera == null) return false;

        Ray ray = camera.ScreenPointToRay(mousePosition, new(Screen.Width, Screen.Height));
        // Calculate the distance along the normal from the origin to the plane
        Plane canvasPlane = new(canvas.Transform.forward, Vector3.Dot(canvas.Transform.position, canvas.Transform.forward));

        double? distance = ray.Intersects(canvasPlane);
        if (distance.HasValue == false)
            return false;

        Vector3 worldHitPoint = ray.Position(distance.Value);
        Vector3 localHitPoint = canvas.Transform.InverseTransformPoint(worldHitPoint);
        screenPosition = new(localHitPoint.x, localHitPoint.y);

        return true;
    }
}
