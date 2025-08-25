using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        #region State Query Methods

        /// <summary>
        /// Checks if an element is currently hovered.
        /// </summary>
        public bool IsElementHovered(ulong id) => _elementsInBubblePath.Contains(id);

        /// <summary>
        /// Checks if an element is currently active (pressed).
        /// </summary>
        public bool IsElementActive(ulong id) => _activeElementId == id;

        /// <summary>
        /// Checks if an element has input focus.
        /// </summary>
        public bool IsElementFocused(ulong id) => _focusedElementId == id;

        /// <summary>
        /// Checks if an element is currently being dragged.
        /// </summary>
        public bool IsElementDragging(ulong id) =>
            _isDragging.TryGetValue(id, out bool isDragging) && isDragging;

        /// <summary>
        /// Checks if the current parent element is hovered.
        /// </summary>
        public bool IsParentHovered => IsElementHovered(CurrentParent.ID);

        /// <summary>
        /// Checks if the current parent element is active.
        /// </summary>
        public bool IsParentActive => _activeElementId == CurrentParent.ID;

        /// <summary>
        /// Checks if the current parent element has input focus.
        /// </summary>
        public bool IsParentFocused => _focusedElementId == CurrentParent.ID;

        /// <summary>
        /// Checks if the current parent element is being dragged.
        /// </summary>
        public bool IsParentDragging => IsElementDragging(CurrentParent.ID);

        #endregion

        #region Interaction State

        // Element interaction state tracking
        private ulong _theHoveredElementId = 0;  // The ID of the element directly hovered by the pointer
        private ulong _activeElementId = 0;      // Currently active (pressed) element
        private ulong _focusedElementId = 0;     // Element with input focus

        // State tracking collections
        private Dictionary<ulong, bool> _wasHoveredState = new Dictionary<ulong, bool>();
        private Dictionary<ulong, Vector2> _dragStartPos = new Dictionary<ulong, Vector2>();
        private HashSet<ulong> _elementsInBubblePath = new HashSet<ulong>();
        private Dictionary<ulong, bool> _isDragging = new Dictionary<ulong, bool>();

        // Public access to interaction state
        public ulong HoveredElementId => _theHoveredElementId;
        public ulong ActiveElementId => _activeElementId;
        public ulong FocusedElementId => _focusedElementId;

        public bool WantsCapturePointer => _theHoveredElementId != 0 || _activeElementId != 0;

        #endregion

        #region Interaction Processing

        /// <summary>
        /// Main interaction handling method called each frame.
        /// </summary>
        private void HandleInteractions()
        {
            // Reset hover state
            _elementsInBubblePath.Clear();
            ulong previousHoveredElementId = _theHoveredElementId;
            _theHoveredElementId = 0;

            // Find the topmost element under the pointer
            var t = Transform2D.Identity;
            Element? topmostInteractable = FindTopmostInteractableElement(RootElement, t);

            if (topmostInteractable != null)
            {
                _theHoveredElementId = topmostInteractable.ID;
                BuildBubblePath(topmostInteractable);
            }

            // Process hover events
            HandleHoverEvents(previousHoveredElementId);

            // Process scroll events
            if (_theHoveredElementId != 0 && PointerWheel != 0)
            {
                Element? hoveredElement = FindElementByID(_theHoveredElementId);
                if (hoveredElement != null)
                {
                    hoveredElement.OnScroll?.Invoke(new ScrollEvent(hoveredElement, hoveredElement.LayoutRect, PointerPos, PointerWheel));
                    BubbleEventToParents(hoveredElement, parent => parent.OnScroll?.Invoke(new ScrollEvent(parent, parent.LayoutRect, PointerPos, PointerWheel)));
                }
            }

            // Process mouse button events
            HandleMouseEvents();

            // Process keyboard events for focused element
            HandleKeyboardEvents();
        }

        #endregion

        #region Element Hit Testing

        /// <summary>
        /// Finds the topmost interactable element under the pointer across all layers.
        /// </summary>
        private Element? FindTopmostInteractableElement(Element element, Transform2D parentTransform)
            => FindTopmostInteractableElementForLayer(element, parentTransform, Layer.Topmost)
            ?? FindTopmostInteractableElementForLayer(element, parentTransform, Layer.Overlay)
            ?? FindTopmostInteractableElementForLayer(element, parentTransform, Layer.Base);

        /// <summary>
        /// Recursively finds the topmost interactable element for the specified layer.
        /// </summary>
        private Element? FindTopmostInteractableElementForLayer(Element element, Transform2D parentTransform, Layer layer, bool inLayer = false)
        {
            if (element == null)
                return null;

            if (layer == Layer.Base && element.Layer != Layer.Base)
                return null;
            if (layer == Layer.Overlay && element.Layer == Layer.Topmost)
                return null;

            // Calculate the combined transform
            Transform2D combinedTransform = parentTransform;
            var rect = new Rect(element.X, element.Y, element.LayoutWidth, element.LayoutHeight);
            Transform2D styleTransform = element._elementStyle.GetTransformForElement(rect);
            combinedTransform.Premultiply(ref styleTransform);

            // Transform pointer position to element's local space
            var inverseTransform = combinedTransform.Inverse();
            inverseTransform.TransformPoint(out double localX, out double localY, PointerPos.x, PointerPos.y);

            // Check if pointer is over this element
            bool isPointerOverElement = IsPointOverElement(element, localX, localY);

            bool shouldCheckChildren = element._scissorEnabled == false || isPointerOverElement;

            // Scrollbars offset the position of children
            Transform2D childTransform = combinedTransform;
            if (shouldCheckChildren && this.HasElementStorage(element, "ScrollState"))
            {
                ScrollState scrollState = this.GetElementStorage<ScrollState>(element, "ScrollState");
                var transform = Transform2D.CreateTranslation(-scrollState.Position);
                childTransform.Premultiply(ref transform);
            }

            // Check children first (front to back, respecting z-order)
            if (shouldCheckChildren && element.Children != null && element.Children.Count > 0)
            {
                for (int i = element.Children.Count - 1; i >= 0; i--)
                {
                    var interactableChild = FindTopmostInteractableElementForLayer(element.Children[i], childTransform, layer, inLayer || element.Layer == layer);
                    if (interactableChild != null)
                        return interactableChild;
                }
            }

            bool isInLayer = inLayer || element.Layer == layer;
            if (!isPointerOverElement || element.IsNotInteractable || !isInLayer)
                return null;

            return element;
        }

        /// <summary>
        /// Tests if a point in local coordinates is within an element.
        /// </summary>
        private bool IsPointOverElement(Element element, double localX, double localY) =>
            localX >= element.X &&
            localX <= element.X + element.LayoutWidth &&
            localY >= element.Y &&
            localY <= element.Y + element.LayoutHeight;

        #endregion

        #region Event Propagation

        /// <summary>
        /// Builds the bubble path from an element to the root.
        /// </summary>
        private void BuildBubblePath(Element element)
        {
            if (element.StopPropagation)
                return;

            Element? current = element;
            while (current != null)
            {
                _elementsInBubblePath.Add(current.ID);
                if (current.StopPropagation)
                    break;
                current = current.Parent;
            }
        }

        /// <summary>
        /// Propagates an event up the element hierarchy.
        /// </summary>
        private void BubbleEventToParents(Element element, Action<Element> eventHandler)
        {
            if (element.StopPropagation)
                return;

            Element? current = element.Parent;
            while (current != null)
            {
                eventHandler(current);
                if (current.StopPropagation)
                    break;
                current = current.Parent;
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles hover, enter, and leave events.
        /// </summary>
        private void HandleHoverEvents(ulong previousHoveredElementId)
        {
            // Find elements that were previously hovered but are no longer in bubble path
            var leftElements = new HashSet<ulong>(_wasHoveredState.Keys);
            leftElements.ExceptWith(_elementsInBubblePath);

            // Trigger leave events
            foreach (var leftElementId in leftElements)
            {
                Element? leftElement = FindElementByID(leftElementId);
                if (leftElement != null && leftElement.OnLeave != null)
                {
                    leftElement.OnLeave(new ElementEvent(leftElement, leftElement.LayoutRect, PointerPos));
                }
                _wasHoveredState[leftElementId] = false;
            }

            // Trigger enter and hover events
            foreach (var hoveredId in _elementsInBubblePath)
            {
                Element? hoveredElement = FindElementByID(hoveredId);
                if (hoveredElement != null)
                {
                    bool wasHovered = _wasHoveredState.TryGetValue(hoveredId, out bool hoveredState) && hoveredState;

                    // Only trigger enter event if element wasn't hovered before
                    if (!wasHovered && hoveredElement.OnEnter != null)
                        hoveredElement.OnEnter(new ElementEvent(hoveredElement, hoveredElement.LayoutRect, PointerPos));

                    // Always trigger hover event
                    hoveredElement.OnHover?.Invoke(new ElementEvent(hoveredElement, hoveredElement.LayoutRect, PointerPos));

                    _wasHoveredState[hoveredId] = true;
                }
            }
        }

        /// <summary>
        /// Handles mouse button events including clicks, drags, and releases.
        /// </summary>
        private void HandleMouseEvents()
        {
            // Handle double-click
            if (IsPointerDoubleClick(PaperMouseBtn.Left))
            {
                if (_theHoveredElementId != 0)
                {
                    Element? hoveredElement = FindElementByID(_theHoveredElementId);
                    if (hoveredElement != null)
                    {
                        // Direct event
                        hoveredElement.OnDoubleClick?.Invoke(new ClickEvent(hoveredElement, hoveredElement.LayoutRect, PointerPos, PaperMouseBtn.Left));

                        // Bubble event
                        BubbleEventToParents(hoveredElement, parent => parent.OnDoubleClick?.Invoke(new ClickEvent(parent, parent.LayoutRect, PointerPos, PaperMouseBtn.Left)));
                    }
                }
            }

            // Handle press
            if (IsPointerPressed(PaperMouseBtn.Left))
            {
                if (_theHoveredElementId != 0)
                {
                    _activeElementId = _theHoveredElementId;
                    _dragStartPos[_activeElementId] = PointerPos;
                    _isDragging[_activeElementId] = false;

                    Element? activeElement = FindElementByID(_activeElementId);
                    if (activeElement != null)
                    {
                        // Direct event
                        activeElement.OnPress?.Invoke(new ClickEvent(activeElement, activeElement.LayoutRect, PointerPos, PaperMouseBtn.Left));

                        // Bubble event
                        BubbleEventToParents(activeElement, parent => parent.OnPress?.Invoke(new ClickEvent(parent, parent.LayoutRect, PointerPos, PaperMouseBtn.Left)));

                        // Update focus state
                        if (activeElement.IsFocusable)
                        {
                            if (_focusedElementId != _activeElementId)
                            {
                                activeElement.OnFocusChange?.Invoke(new FocusEvent(activeElement, true));

                                if (_focusedElementId != 0)
                                {
                                    Element? oldFocusedElement = FindElementByID(_focusedElementId);
                                    if (oldFocusedElement != null)
                                    {
                                        oldFocusedElement.OnFocusChange?.Invoke(new FocusEvent(oldFocusedElement, false));
                                    }
                                }

                                // Update focused element ID
                                _focusedElementId = _activeElementId;
                            }
                        }
                    }
                }
            }
            // Handle release
            else if (IsPointerReleased(PaperMouseBtn.Left))
            {
                if (_activeElementId != 0)
                {
                    Element? activeElement = FindElementByID(_activeElementId);

                    if (activeElement != null)
                    {
                        bool wasDragging = _isDragging.TryGetValue(_activeElementId, out bool isDragging) && isDragging;

                        // Handle drag end if element was being dragged
                        if (wasDragging)
                        {
                            Vector2 startPos = _dragStartPos[_activeElementId];
                            Vector2 endPos = PointerPos;
                            Vector2 delta = endPos - startPos;

                            // Direct event
                            activeElement.OnDragEnd?.Invoke(new DragEvent(activeElement, activeElement.LayoutRect, PointerPos, startPos, PointerDelta, delta));

                            // Bubble event
                            BubbleEventToParents(activeElement, parent => parent.OnDragEnd?.Invoke(new DragEvent(parent, parent.LayoutRect, PointerPos, startPos, PointerDelta, delta)));

                            _isDragging[_activeElementId] = false;
                        }

                        // If released over the same element that was pressed AND not dragging, it's a click
                        if (_theHoveredElementId == _activeElementId && !wasDragging)
                        {
                            // Direct click
                            activeElement.OnClick?.Invoke(new ClickEvent(activeElement, activeElement.LayoutRect, PointerPos, PaperMouseBtn.Left));

                            // Bubble click event
                            BubbleEventToParents(activeElement, parent => parent.OnClick?.Invoke(new ClickEvent(parent, parent.LayoutRect, PointerPos, PaperMouseBtn.Left)));
                        }

                        // Direct release event
                        activeElement.OnRelease?.Invoke(new ClickEvent(activeElement, activeElement.LayoutRect, PointerPos, PaperMouseBtn.Left));

                        // Bubble release event
                        BubbleEventToParents(activeElement, parent => parent.OnRelease?.Invoke(new ClickEvent(parent, parent.LayoutRect, PointerPos, PaperMouseBtn.Left)));
                    }

                    _activeElementId = 0;
                }
            }
            // Handle right-click
            else if (IsPointerPressed(PaperMouseBtn.Right))
            {
                if (_theHoveredElementId != 0)
                {
                    Element? hoveredElement = FindElementByID(_theHoveredElementId);
                    if (hoveredElement != null)
                    {
                        // Direct event
                        hoveredElement.OnRightClick?.Invoke(new ClickEvent(hoveredElement, hoveredElement.LayoutRect, PointerPos, PaperMouseBtn.Right));

                        // Bubble event
                        BubbleEventToParents(hoveredElement, parent => parent.OnRightClick?.Invoke(new ClickEvent(parent, parent.LayoutRect, PointerPos, PaperMouseBtn.Right)));
                    }
                }
            }

            // Handle dragging
            if (IsPointerDown(PaperMouseBtn.Left) && _activeElementId != 0)
            {
                Element? activeElement = FindElementByID(_activeElementId);
                if (activeElement != null)
                {
                    // Direct event
                    activeElement.OnHeld?.Invoke(new ClickEvent(activeElement, activeElement.LayoutRect, PointerPos, PaperMouseBtn.Left));

                    // Bubble event
                    BubbleEventToParents(activeElement, parent => parent.OnHeld?.Invoke(new ClickEvent(parent, parent.LayoutRect, PointerPos, PaperMouseBtn.Left)));

                    if (IsPointerMoving)
                    {
                        bool wasDragging = _isDragging.TryGetValue(_activeElementId, out bool isDragging) && isDragging;
                        Vector2 startPos = _dragStartPos[_activeElementId];

                        // Handle drag start
                        if (!wasDragging)
                        {
                            activeElement.OnDragStart?.Invoke(new DragEvent(activeElement, activeElement.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                            BubbleEventToParents(activeElement, parent => parent.OnDragStart?.Invoke(new DragEvent(parent, parent.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta)));

                            _isDragging[_activeElementId] = true;
                        }

                        // Handle continuous dragging
                        activeElement.OnDragging?.Invoke(new DragEvent(activeElement, activeElement.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                        BubbleEventToParents(activeElement, parent => parent.OnDragging?.Invoke(new DragEvent(parent, parent.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta)));
                    }
                }
            }
        }

        /// <summary>
        /// Process keyboard events for the focused element
        /// </summary>
        private void HandleKeyboardEvents()
        {
            // If no element has focus, there's nothing to do
            if (_focusedElementId == 0)
                return;

            Element? focusedElement = FindElementByID(_focusedElementId);
            if (focusedElement == null)
                return;

            // Process key presses
            foreach (var key in KeyValues)
            {
                if (IsKeyPressed(key) && focusedElement.OnKeyPressed != null)
                {
                    focusedElement.OnKeyPressed?.Invoke(new KeyEvent(focusedElement, key, IsKeyRepeating(key)));
                }
            }

            // Process text input
            while (InputString.Count > 0)
            {
                char c = InputString.Dequeue();
                focusedElement.OnTextInput?.Invoke(new TextInputEvent(focusedElement, c));
            }
        }

        #endregion
    }
}
