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
        public bool IsParentHovered => IsElementHovered(CurrentParent.Data.ID);

        /// <summary>
        /// Checks if the current parent element is active.
        /// </summary>
        public bool IsParentActive => _activeElementId == CurrentParent.Data.ID;

        /// <summary>
        /// Checks if the current parent element has input focus.
        /// </summary>
        public bool IsParentFocused => _focusedElementId == CurrentParent.Data.ID;

        /// <summary>
        /// Checks if the current parent element is being dragged.
        /// </summary>
        public bool IsParentDragging => IsElementDragging(CurrentParent.Data.ID);

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
            ElementHandle topmostInteractable = FindTopmostInteractableElement(ref _rootElementHandle, t);

            if (topmostInteractable.IsValid)
            {
                _theHoveredElementId = topmostInteractable.Data.ID;
                BuildBubblePath(topmostInteractable);
            }

            // Process hover events
            HandleHoverEvents(previousHoveredElementId);

            // Process scroll events
            if (_theHoveredElementId != 0 && PointerWheel != 0)
            {
                ElementHandle hoveredElement = FindElementByID(_theHoveredElementId);
                if (hoveredElement.IsValid)
                {
                    ref ElementData data = ref hoveredElement.Data;
                    data.OnScroll?.Invoke(new ScrollEvent(hoveredElement, data.LayoutRect, PointerPos, PointerWheel));
                    BubbleEventToParents(hoveredElement, parent => {
                        ref ElementData parentData = ref parent.Data;
                        parentData.OnScroll?.Invoke(new ScrollEvent(parent, parentData.LayoutRect, PointerPos, PointerWheel));
                    });
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
        private ElementHandle FindTopmostInteractableElement(ref ElementHandle handle, Transform2D parentTransform)
        {
            var found = FindTopmostInteractableElementForLayer(ref handle, parentTransform, Layer.Topmost);
            if(found.IsValid == false) found = FindTopmostInteractableElementForLayer(ref handle, parentTransform, Layer.Overlay);
            if(found.IsValid == false) found = FindTopmostInteractableElementForLayer(ref handle, parentTransform, Layer.Base);
            return found;
        }

        /// <summary>
        /// Recursively finds the topmost interactable element for the specified layer.
        /// </summary>
        private ElementHandle FindTopmostInteractableElementForLayer(ref ElementHandle handle, Transform2D parentTransform, Layer layer, bool inLayer = false)
        {
            if (!handle.IsValid) return default;

            ref ElementData data = ref handle.Data;

            if (layer == Layer.Base && data.Layer != Layer.Base)         return default;
            if (layer == Layer.Overlay && data.Layer == Layer.Topmost)   return default;

            // Calculate the combined transform
            Transform2D combinedTransform = parentTransform;
            var rect = new Rect(data.X, data.Y, data.LayoutWidth, data.LayoutHeight);
            Transform2D styleTransform = data._elementStyle.GetTransformForElement(rect);
            combinedTransform.Premultiply(ref styleTransform);

            // Transform pointer position to element's local space
            var inverseTransform = combinedTransform.Inverse();
            inverseTransform.TransformPoint(out double localX, out double localY, PointerPos.x, PointerPos.y);

            // Check if pointer is over this element
            bool isPointerOverElement = IsPointOverElementData(data, localX, localY);

            bool shouldCheckChildren = data._scissorEnabled == false || isPointerOverElement;

            // Scrollbars offset the position of children
            Transform2D childTransform = combinedTransform;
            if (shouldCheckChildren && this.HasElementStorage(handle, "ScrollState"))
            {
                ScrollState scrollState = this.GetElementStorage<ScrollState>(handle, "ScrollState");
                var transform = Transform2D.CreateTranslation(-scrollState.Position);
                childTransform.Premultiply(ref transform);
            }

            // Check children first (front to back, respecting z-order)
            var childIndices = data.ChildIndices;
            if (shouldCheckChildren && childIndices.Count > 0)
            {
                for (int i = childIndices.Count - 1; i >= 0; i--)
                {
                    var childHandle = new ElementHandle(handle.Owner, childIndices[i]);

                    var interactableChild = FindTopmostInteractableElementForLayer(ref childHandle, childTransform, layer, inLayer || data.Layer == layer);
                    if (interactableChild.IsValid)
                        return interactableChild;
                }
            }

            bool isInLayer = inLayer || data.Layer == layer;
            if (!isPointerOverElement || data.IsNotInteractable || !isInLayer)
                return default;

            return handle;
        }

        /// <summary>
        /// Tests if a point in local coordinates is within an element.
        /// </summary>
        private bool IsPointOverElementData(in ElementData data, double localX, double localY)
        {
            return  localX >= data.X &&
                    localX <= data.X + data.LayoutWidth &&
                    localY >= data.Y &&
                    localY <= data.Y + data.LayoutHeight;
        }

        #endregion

        #region Event Propagation

        /// <summary>
        /// Builds the bubble path from an element to the root.
        /// </summary>
        private void BuildBubblePath(in ElementHandle handle)
        {
            ref ElementData data = ref handle.Data;
            if (data.StopPropagation)
                return;

            ElementHandle current = handle;
            while (current.IsValid)
            {
                data = ref current.Data;

                _elementsInBubblePath.Add(data.ID);
                if (data.StopPropagation)
                    break;
                current = current.GetParentHandle();
            }
        }

        /// <summary>
        /// Propagates an event up the element hierarchy.
        /// </summary>
        private void BubbleEventToParents(in ElementHandle element, Action<ElementHandle> eventHandler)
        {
            ref ElementData data = ref element.Data;
            if (data.StopPropagation)
                return;

            ElementHandle current = element.GetParentHandle();
            while (current.IsValid)
            {
                eventHandler(current);
                data = ref current.Data;
                if (data.StopPropagation)
                    break;
                current = current.GetParentHandle();
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
                ElementHandle leftElement = FindElementByID(leftElementId);
                if (leftElement.IsValid)
                {
                    ref ElementData data = ref leftElement.Data;
                    if (data.OnLeave != null)
                    {
                        data.OnLeave(new ElementEvent(leftElement, data.LayoutRect, PointerPos));
                    }
                }
                _wasHoveredState[leftElementId] = false;
            }

            // Trigger enter and hover events
            foreach (var hoveredId in _elementsInBubblePath)
            {
                ElementHandle hoveredElement = FindElementByID(hoveredId);
                if (hoveredElement.IsValid)
                {
                    ref ElementData data = ref hoveredElement.Data;
                    bool wasHovered = _wasHoveredState.TryGetValue(hoveredId, out bool hoveredState) && hoveredState;

                    // Only trigger enter event if element wasn't hovered before
                    if (!wasHovered && data.OnEnter != null)
                        data.OnEnter(new ElementEvent(hoveredElement, data.LayoutRect, PointerPos));

                    // Always trigger hover event
                    data.OnHover?.Invoke(new ElementEvent(hoveredElement, data.LayoutRect, PointerPos));

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
                    ElementHandle hoveredElement = FindElementByID(_theHoveredElementId);
                    if (hoveredElement.IsValid)
                    {
                        ref ElementData data = ref hoveredElement.Data;
                        // Direct event
                        data.OnDoubleClick?.Invoke(new ClickEvent(hoveredElement, data.LayoutRect, PointerPos, PaperMouseBtn.Left));

                        // Bubble event
                        BubbleEventToParents(hoveredElement, parent => {
                            ref ElementData parentData = ref parent.Data;
                            parentData.OnDoubleClick?.Invoke(new ClickEvent(parent, parentData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                        });
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

                    ElementHandle activeElement = FindElementByID(_activeElementId);
                    if (activeElement.IsValid)
                    {
                        ref ElementData data = ref activeElement.Data;
                        // Direct event
                        data.OnPress?.Invoke(new ClickEvent(activeElement, data.LayoutRect, PointerPos, PaperMouseBtn.Left));

                        // Bubble event
                        BubbleEventToParents(activeElement, parent => {
                            ref ElementData parentData = ref parent.Data;
                            parentData.OnPress?.Invoke(new ClickEvent(parent, parentData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                        });

                        // Update focus state
                        if (data.IsFocusable)
                        {
                            if (_focusedElementId != _activeElementId)
                            {
                                data.OnFocusChange?.Invoke(new FocusEvent(activeElement, true));

                                if (_focusedElementId != 0)
                                {
                                    ElementHandle oldFocusedElement = FindElementByID(_focusedElementId);
                                    if (oldFocusedElement.IsValid)
                                    {
                                        ref ElementData oldData = ref oldFocusedElement.Data;
                                        oldData.OnFocusChange?.Invoke(new FocusEvent(oldFocusedElement, false));
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
                    ElementHandle activeElement = FindElementByID(_activeElementId);

                    if (activeElement.IsValid)
                    {
                        ref ElementData data = ref activeElement.Data;

                        bool wasDragging = _isDragging.TryGetValue(_activeElementId, out bool isDragging) && isDragging;

                        // Handle drag end if element was being dragged
                        if (wasDragging)
                        {
                            Vector2 startPos = _dragStartPos[_activeElementId];
                            Vector2 endPos = PointerPos;
                            Vector2 delta = endPos - startPos;

                            // Direct event
                            data.OnDragEnd?.Invoke(new DragEvent(activeElement, data.LayoutRect, PointerPos, startPos, PointerDelta, delta));

                            // Bubble event
                            BubbleEventToParents(activeElement, parent => parent.Data.OnDragEnd?.Invoke(new DragEvent(parent, parent.Data.LayoutRect, PointerPos, startPos, PointerDelta, delta)));

                            _isDragging[_activeElementId] = false;
                        }

                        // If released over the same element that was pressed AND not dragging, it's a click
                        if (_theHoveredElementId == _activeElementId && !wasDragging)
                        {
                            // Direct click
                            data.OnClick?.Invoke(new ClickEvent(activeElement, data.LayoutRect, PointerPos, PaperMouseBtn.Left));

                            // Bubble click event
                            BubbleEventToParents(activeElement, parent => parent.Data.OnClick?.Invoke(new ClickEvent(parent, parent.Data.LayoutRect, PointerPos, PaperMouseBtn.Left)));
                        }

                        // Direct release event
                        data.OnRelease?.Invoke(new ClickEvent(activeElement, data.LayoutRect, PointerPos, PaperMouseBtn.Left));

                        // Bubble release event
                        BubbleEventToParents(activeElement, parent => parent.Data.OnRelease?.Invoke(new ClickEvent(parent, parent.Data.LayoutRect, PointerPos, PaperMouseBtn.Left)));
                    }

                    _activeElementId = 0;
                }
            }
            // Handle right-click
            else if (IsPointerPressed(PaperMouseBtn.Right))
            {
                if (_theHoveredElementId != 0)
                {
                    ElementHandle hoveredElement = FindElementByID(_theHoveredElementId);
                    if (hoveredElement.IsValid)
                    {
                        ref ElementData data = ref hoveredElement.Data;
                        // Direct event
                        data.OnRightClick?.Invoke(new ClickEvent(hoveredElement, data.LayoutRect, PointerPos, PaperMouseBtn.Right));

                        // Bubble event
                        BubbleEventToParents(hoveredElement, parent => {
                            ref ElementData parentData = ref parent.Data;
                            parentData.OnRightClick?.Invoke(new ClickEvent(parent, parentData.LayoutRect, PointerPos, PaperMouseBtn.Right));
                        });
                    }
                }
            }

            // Handle dragging
            if (IsPointerDown(PaperMouseBtn.Left) && _activeElementId != 0)
            {
                ElementHandle activeElement = FindElementByID(_activeElementId);
                if (activeElement.IsValid)
                {
                    ref ElementData data = ref activeElement.Data;

                    // Direct event
                    data.OnHeld?.Invoke(new ClickEvent(activeElement, data.LayoutRect, PointerPos, PaperMouseBtn.Left));

                    // Bubble event
                    BubbleEventToParents(activeElement, parent => {
                        ref ElementData parentData = ref parent.Data;
                        parentData.OnHeld?.Invoke(new ClickEvent(parent, parentData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                    });

                    if (IsPointerMoving)
                    {
                        bool wasDragging = _isDragging.TryGetValue(_activeElementId, out bool isDragging) && isDragging;
                        Vector2 startPos = _dragStartPos[_activeElementId];
                        Rect layoutRect = data.LayoutRect;

                        // Handle drag start
                        if (!wasDragging)
                        {
                            data.OnDragStart?.Invoke(new DragEvent(activeElement, layoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                            BubbleEventToParents(activeElement, parent => {
                                ref ElementData parentData = ref parent.Data;
                                parentData.OnDragStart?.Invoke(new DragEvent(parent, parentData.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                            });

                            _isDragging[_activeElementId] = true;
                        }

                        // Handle continuous dragging
                        data.OnDragging?.Invoke(new DragEvent(activeElement, layoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                        BubbleEventToParents(activeElement, parent => {
                            ref ElementData parentData = ref parent.Data;
                            parentData.OnDragging?.Invoke(new DragEvent(parent, parentData.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                        });
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

            ElementHandle focusedElement = FindElementByID(_focusedElementId);
            if (!focusedElement.IsValid)
                return;

            ref ElementData data = ref focusedElement.Data;

            // Process key presses
            foreach (var key in KeyValues)
            {
                if (IsKeyPressed(key) && data.OnKeyPressed != null)
                {
                    data.OnKeyPressed?.Invoke(new KeyEvent(focusedElement, key, IsKeyRepeating(key)));
                }
            }

            // Process text input
            while (InputString.Count > 0)
            {
                char c = InputString.Dequeue();
                data.OnTextInput?.Invoke(new TextInputEvent(focusedElement, c));
            }
        }

        #endregion
    }
}
