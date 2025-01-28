// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Icons;

namespace Prowl.Runtime.Components.UI;

[AddComponentMenu($"{FontAwesome6.Tv}  UI/{FontAwesome6.UsersRectangle}  RectTransform")]
[ExecuteAlways]
public class RectTransform : MonoBehaviour
{
    public enum CanvasHorizontalAlignment
    {
        Left,
        Middle,
        Right,
        Stretch
    }

    public enum CanvasVerticalAlignment
    {
        Top,
        Middle,
        Bottom,
        Stretch
    }

    [SerializeField] private CanvasHorizontalAlignment _horizontalAlignment = CanvasHorizontalAlignment.Middle;
    [SerializeField] private CanvasVerticalAlignment _verticalAlignment = CanvasVerticalAlignment.Middle;
    [SerializeField] private Vector2 _pivot = new(0.5f, 0.5f);
    [SerializeField] private Vector2 _size = new(100, 100);
    [SerializeField] private Vector2 _anchoredPosition = Vector2.zero;

    private Canvas _canvas;
    private RectTransform _parentRect;
    private Rect _calculatedRect;

    // Cache children for faster access
    private List<RectTransform> _children = [];
    public IReadOnlyList<RectTransform> Children => _children;

    public Rect CalculatedRect => _calculatedRect;

    public Canvas TargetCanvas => _canvas;

    public override void OnEnable()
    {
        _canvas = GetComponentInParent<Canvas>();
        _parentRect = Transform.parent?.gameObject.GetComponent<RectTransform>();

        if (_parentRect != null)
            _parentRect.AddChild(this);
    }

    public override void OnDisable()
    {
        if (_parentRect != null)
            _parentRect.RemoveChild(this);
    }

    public override void OnTransformParentChanged()
    {
        if (_parentRect != null)
            _parentRect.RemoveChild(this); // Triggers a rebuild down the hierarchy from _parentRect

        _parentRect = Transform.parent?.gameObject.GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();

        if (_parentRect != null)
            _parentRect.AddChild(this); // Triggers a rebuild down the hierarchy from _parentRect

        // Dont need to set us to dirty if we have a parent, as the Parent will propagate a Rebuild down its hierarchy
        if (_parentRect == null)
            Rebuild();
    }

    public void AddChild(RectTransform child)
    {
        if (!_children.Contains(child))
            _children.Add(child);

        Rebuild();
    }

    public void RemoveChild(RectTransform child)
    {
        _children.Remove(child);
        Rebuild();
    }

    public void SetHorizontalAlignment(CanvasHorizontalAlignment value)
    {
        if (_horizontalAlignment != value)
        {
            _horizontalAlignment = value;
            Rebuild();
        }
    }

    public void SetVerticalAlignment(CanvasVerticalAlignment value)
    {
        if (_verticalAlignment != value)
        {
            _verticalAlignment = value;
            Rebuild();
        }
    }

    public void SetPivot(Vector2 value)
    {
        if (_pivot != value)
        {
            _pivot = value;
            Rebuild();
        }
    }

    public void SetSize(Vector2 value)
    {
        if (_size != value)
        {
            _size = value;
            Rebuild();
        }
    }

    public void SetAnchoredPosition(Vector2 value)
    {
        if (_anchoredPosition != value)
        {
            _anchoredPosition = value;
            Rebuild();
        }
    }

    public override void OnValidate() => Rebuild();

    public void Rebuild()
    {
        // Rebuild this rect
        CalculateLayout();

        // Rebuild all children
        foreach (RectTransform child in Children)
            child.Rebuild();

        // Rebuild Draw Data
        _canvas.SetCanvasDirty();
    }

    public void CalculateLayout()
    {
        Rect parentRect = _parentRect != null ?
            _parentRect._calculatedRect :
            new Rect(0, 0, Screen.Width, Screen.Height);

        Vector2 position = parentRect.Min;
        Vector2 finalSize = _size * _canvas.CanvasScale;

        // Horizontal
        switch (_horizontalAlignment)
        {
            case CanvasHorizontalAlignment.Left:
                position.x = parentRect.Min.x;
                break;
            case CanvasHorizontalAlignment.Middle:
                position.x = parentRect.Min.x + (parentRect.width * 0.5f);
                break;
            case CanvasHorizontalAlignment.Right:
                position.x = parentRect.Max.x;
                break;
            case CanvasHorizontalAlignment.Stretch:
                position.x = parentRect.Min.x;
                finalSize.x = parentRect.width;
                break;
        }

        // Vertical
        switch (_verticalAlignment)
        {
            case CanvasVerticalAlignment.Bottom:
                position.y = parentRect.Min.y;
                break;
            case CanvasVerticalAlignment.Middle:
                position.y = parentRect.Min.y + (parentRect.height * 0.5f);
                break;
            case CanvasVerticalAlignment.Top:
                position.y = parentRect.Max.y;
                break;
            case CanvasVerticalAlignment.Stretch:
                position.y = parentRect.Min.y;
                finalSize.y = parentRect.height;
                break;
        }

        // Apply pivot offset
        Vector2 pivotOffset = new Vector2(
            finalSize.x * _pivot.x,
            finalSize.y * _pivot.y
        );
        position -= pivotOffset;

        // Apply anchored position
        position += _anchoredPosition * _canvas.CanvasScale;

        _calculatedRect = new Rect(position, finalSize);
    }

    public Vector2 GetWorldPosition()
    {
        return new Vector2(
            _calculatedRect.x + _calculatedRect.width * _pivot.x,
            _calculatedRect.y + _calculatedRect.height * _pivot.y
        );
    }

    public override void DrawGizmos()
    {
        if (_canvas == null)
            return;

        // Draw the calculated rect
        Debug.PushMatrix(_canvas.Transform.localToWorldMatrix);
        Debug.DrawLine(new Vector3(_calculatedRect.Min.x, _calculatedRect.Min.y, 0), new Vector3(_calculatedRect.Max.x, _calculatedRect.Min.y, 0), Color.white);
        Debug.DrawLine(new Vector3(_calculatedRect.Max.x, _calculatedRect.Min.y, 0), new Vector3(_calculatedRect.Max.x, _calculatedRect.Max.y, 0), Color.white);
        Debug.DrawLine(new Vector3(_calculatedRect.Max.x, _calculatedRect.Max.y, 0), new Vector3(_calculatedRect.Min.x, _calculatedRect.Max.y, 0), Color.white);
        Debug.DrawLine(new Vector3(_calculatedRect.Min.x, _calculatedRect.Max.y, 0), new Vector3(_calculatedRect.Min.x, _calculatedRect.Min.y, 0), Color.white);
        Debug.PopMatrix();
    }

    public override void DrawGizmosSelected()
    {
        if (_canvas == null)
            return;


        Debug.PushMatrix(_canvas.Transform.localToWorldMatrix);

        Rect parentRect = _parentRect != null ?
            _parentRect._calculatedRect :
            new Rect(0, 0, Screen.Width, Screen.Height);

        // Draw pivot point
        Vector3 pivotPos = new(
            _calculatedRect.x + _calculatedRect.width * _pivot.x,
            _calculatedRect.y + _calculatedRect.height * _pivot.y,
            0
        );
        Debug.DrawWireCircle(pivotPos, Vector3.forward, 5, Color.red);

        // Draw alignment indicators
        // Horizontal
        Vector3 horizontalAnchorPos = Vector3.zero;
        switch (_horizontalAlignment)
        {
            case CanvasHorizontalAlignment.Left:
                horizontalAnchorPos = new Vector3(parentRect.Min.x, _calculatedRect.Center.y, 0);
                Debug.DrawWireCircle(horizontalAnchorPos, Vector3.forward, 3, Color.blue);
                break;
            case CanvasHorizontalAlignment.Middle:
                horizontalAnchorPos = new Vector3(parentRect.Center.x, _calculatedRect.Center.y, 0);
                Debug.DrawWireCircle(horizontalAnchorPos, Vector3.forward, 3, Color.blue);
                break;
            case CanvasHorizontalAlignment.Right:
                horizontalAnchorPos = new Vector3(parentRect.Max.x, _calculatedRect.Center.y, 0);
                Debug.DrawWireCircle(horizontalAnchorPos, Vector3.forward, 3, Color.blue);
                break;
            case CanvasHorizontalAlignment.Stretch:
                Vector3 stretchStart = new(parentRect.Min.x, _calculatedRect.Center.y, 0);
                Vector3 stretchEnd = new(parentRect.Max.x, _calculatedRect.Center.y, 0);
                Debug.DrawLine(stretchStart, stretchEnd, Color.blue);
                break;
        }

        // Vertical
        Vector3 verticalAnchorPos = Vector3.zero;
        switch (_verticalAlignment)
        {
            case CanvasVerticalAlignment.Bottom:
                verticalAnchorPos = new Vector3(_calculatedRect.Center.x, parentRect.Min.y, 0);
                Debug.DrawWireCircle(verticalAnchorPos, Vector3.forward, 3, Color.blue);
                break;
            case CanvasVerticalAlignment.Middle:
                verticalAnchorPos = new Vector3(_calculatedRect.Center.x, parentRect.Center.y, 0);
                Debug.DrawWireCircle(verticalAnchorPos, Vector3.forward, 3, Color.blue);
                break;
            case CanvasVerticalAlignment.Top:
                verticalAnchorPos = new Vector3(_calculatedRect.Center.x, parentRect.Max.y, 0);
                Debug.DrawWireCircle(verticalAnchorPos, Vector3.forward, 3, Color.blue);
                break;
            case CanvasVerticalAlignment.Stretch:
                Vector3 stretchStart = new(_calculatedRect.Center.x, parentRect.Min.y, 0);
                Vector3 stretchEnd = new(_calculatedRect.Center.x, parentRect.Max.y, 0);
                Debug.DrawLine(stretchStart, stretchEnd, Color.blue);
                break;
        }

        // Draw offset indicator if not zero
        if (_anchoredPosition != Vector2.zero)
        {
            Vector3 basePos = new(_calculatedRect.Center.x - _anchoredPosition.x, _calculatedRect.Center.y - _anchoredPosition.y, 0);
            Debug.DrawArrow(basePos, new Vector3(_anchoredPosition.x, _anchoredPosition.y, 0), Color.green);
        }

        Debug.PopMatrix();
    }
}
