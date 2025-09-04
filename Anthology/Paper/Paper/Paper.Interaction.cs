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
        public bool IsElementHovered(ulong id) => _elementsInBubblePath.Contains(id) || IsHookedToHoveredParent(id);

        /// <summary>
        /// Checks if an element is currently active (pressed).
        /// </summary>
        public bool IsElementActive(ulong id) => _activeElementId == id || IsHookedToActiveParent(id);

        /// <summary>
        /// Checks if an element has input focus.
        /// </summary>
        public bool IsElementFocused(ulong id) => _focusedElementId == id || IsHookedToFocusedParent(id);

        /// <summary>
        /// Checks if an element is currently being dragged.
        /// </summary>
        public bool IsElementDragging(ulong id) =>
            (_isDragging.TryGetValue(id, out bool isDragging) && isDragging) || IsHookedToDraggingParent(id);

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

        #region Interaction Hooking

        /// <summary>
        /// Checks if an element is hooked to its parent's hover state.
        /// </summary>
        private bool IsHookedToHoveredParent(ulong childId)
        {
            ElementHandle childElement = FindElementByID(childId);
            if (!childElement.IsValid) return false;

            ref ElementData childData = ref childElement.Data;
            if (!childData.IsHookedToParent) return false;

            ElementHandle parent = childElement.GetParentHandle();
            if (!parent.IsValid) return false;

            return _elementsInBubblePath.Contains(parent.Data.ID);
        }

        /// <summary>
        /// Checks if an element is hooked to its parent's active state.
        /// </summary>
        private bool IsHookedToActiveParent(ulong childId)
        {
            ElementHandle childElement = FindElementByID(childId);
            if (!childElement.IsValid) return false;

            ref ElementData childData = ref childElement.Data;
            if (!childData.IsHookedToParent) return false;

            ElementHandle parent = childElement.GetParentHandle();
            if (!parent.IsValid) return false;

            return _activeElementId == parent.Data.ID;
        }

        /// <summary>
        /// Checks if an element is hooked to its parent's focus state.
        /// </summary>
        private bool IsHookedToFocusedParent(ulong childId)
        {
            ElementHandle childElement = FindElementByID(childId);
            if (!childElement.IsValid) return false;

            ref ElementData childData = ref childElement.Data;
            if (!childData.IsHookedToParent) return false;

            ElementHandle parent = childElement.GetParentHandle();
            if (!parent.IsValid) return false;

            return _focusedElementId == parent.Data.ID;
        }

        /// <summary>
        /// Checks if an element is hooked to its parent's dragging state.
        /// </summary>
        private bool IsHookedToDraggingParent(ulong childId)
        {
            ElementHandle childElement = FindElementByID(childId);
            if (!childElement.IsValid) return false;

            ref ElementData childData = ref childElement.Data;
            if (!childData.IsHookedToParent) return false;

            ElementHandle parent = childElement.GetParentHandle();
            if (!parent.IsValid) return false;

            return _isDragging.TryGetValue(parent.Data.ID, out bool isDragging) && isDragging;
        }

        #endregion

        #region Interaction State

        // Drag threshold - minimum distance to move before starting drag
        private const float DRAG_THRESHOLD = 5.0f; // pixels

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

        public bool SkipKeyboardNavigation = false;

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

            SkipKeyboardNavigation = false; // Reset keyboard navigation skip flag at end of frame
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

        /// <summary>
        /// Propagates an event to immediate hooked children of an element.
        /// </summary>
        private void PropagateEventToHookedChildren(in ElementHandle element, Action<ElementHandle> eventHandler)
        {
            ref ElementData data = ref element.Data;
            
            // Early exit optimization - if this element has no hooked children, skip entirely
            if (!data.IsAHookedParent)
                return;

            var childIndices = data.ChildIndices;
            foreach (int childIndex in childIndices)
            {
                ElementHandle childElement = new ElementHandle(element.Owner, childIndex);
                if (childElement.IsValid)
                {
                    ref ElementData childData = ref childElement.Data;
                    if (childData.IsHookedToParent)
                    {
                        eventHandler(childElement);
                    }
                }
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
                        
                        // Propagate leave event to hooked children
                        PropagateEventToHookedChildren(leftElement, child => {
                            ref ElementData childData = ref child.Data;
                            childData.OnLeave?.Invoke(new ElementEvent(child, childData.LayoutRect, PointerPos));
                        });
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
                    {
                        data.OnEnter(new ElementEvent(hoveredElement, data.LayoutRect, PointerPos));
                        
                        // Propagate enter event to hooked children
                        PropagateEventToHookedChildren(hoveredElement, child => {
                            ref ElementData childData = ref child.Data;
                            childData.OnEnter?.Invoke(new ElementEvent(child, childData.LayoutRect, PointerPos));
                        });
                    }

                    // Always trigger hover event
                    data.OnHover?.Invoke(new ElementEvent(hoveredElement, data.LayoutRect, PointerPos));
                    
                    // Propagate hover event to hooked children
                    PropagateEventToHookedChildren(hoveredElement, child => {
                        ref ElementData childData = ref child.Data;
                        childData.OnHover?.Invoke(new ElementEvent(child, childData.LayoutRect, PointerPos));
                    });

                    _wasHoveredState[hoveredId] = true;
                }
            }
        }

        /// <summary>
        /// Handles mouse button events including clicks, drags, and releases.
        /// </summary>
        private void HandleMouseEvents()
        {
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

                        // Propagate to hooked children
                        PropagateEventToHookedChildren(activeElement, child => {
                            ref ElementData childData = ref child.Data;
                            childData.OnPress?.Invoke(new ClickEvent(child, childData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                        });

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
                                
                                // Propagate focus gain to hooked children
                                PropagateEventToHookedChildren(activeElement, child => {
                                    ref ElementData childData = ref child.Data;
                                    childData.OnFocusChange?.Invoke(new FocusEvent(child, true));
                                });

                                if (_focusedElementId != 0)
                                {
                                    ElementHandle oldFocusedElement = FindElementByID(_focusedElementId);
                                    if (oldFocusedElement.IsValid)
                                    {
                                        ref ElementData oldData = ref oldFocusedElement.Data;
                                        oldData.OnFocusChange?.Invoke(new FocusEvent(oldFocusedElement, false));
                                        
                                        // Propagate focus loss to hooked children
                                        PropagateEventToHookedChildren(oldFocusedElement, child => {
                                            ref ElementData childData = ref child.Data;
                                            childData.OnFocusChange?.Invoke(new FocusEvent(child, false));
                                        });
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

                            // Propagate drag end to hooked children
                            PropagateEventToHookedChildren(activeElement, child => {
                                ref ElementData childData = ref child.Data;
                                childData.OnDragEnd?.Invoke(new DragEvent(child, childData.LayoutRect, PointerPos, startPos, PointerDelta, delta));
                            });

                            // Bubble event
                            BubbleEventToParents(activeElement, parent => parent.Data.OnDragEnd?.Invoke(new DragEvent(parent, parent.Data.LayoutRect, PointerPos, startPos, PointerDelta, delta)));

                            _isDragging[_activeElementId] = false;
                        }

                        // If released over the same element that was pressed AND not dragging, it's a click
                        if (_theHoveredElementId == _activeElementId && !wasDragging)
                        {
                            // Direct click
                            data.OnClick?.Invoke(new ClickEvent(activeElement, data.LayoutRect, PointerPos, PaperMouseBtn.Left));

                            // Propagate click to hooked children
                            PropagateEventToHookedChildren(activeElement, child => {
                                ref ElementData childData = ref child.Data;
                                childData.OnClick?.Invoke(new ClickEvent(child, childData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                            });

                            // Bubble click event
                            BubbleEventToParents(activeElement, parent => parent.Data.OnClick?.Invoke(new ClickEvent(parent, parent.Data.LayoutRect, PointerPos, PaperMouseBtn.Left)));
                        }

                        // Direct release event
                        data.OnRelease?.Invoke(new ClickEvent(activeElement, data.LayoutRect, PointerPos, PaperMouseBtn.Left));

                        // Propagate release to hooked children
                        PropagateEventToHookedChildren(activeElement, child => {
                            ref ElementData childData = ref child.Data;
                            childData.OnRelease?.Invoke(new ClickEvent(child, childData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                        });

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

                        // Propagate right-click to hooked children
                        PropagateEventToHookedChildren(hoveredElement, child => {
                            ref ElementData childData = ref child.Data;
                            childData.OnRightClick?.Invoke(new ClickEvent(child, childData.LayoutRect, PointerPos, PaperMouseBtn.Right));
                        });

                        // Bubble event
                        BubbleEventToParents(hoveredElement, parent => {
                            ref ElementData parentData = ref parent.Data;
                            parentData.OnRightClick?.Invoke(new ClickEvent(parent, parentData.LayoutRect, PointerPos, PaperMouseBtn.Right));
                        });
                    }
                }
            }

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

                        // Propagate double-click to hooked children
                        PropagateEventToHookedChildren(hoveredElement, child => {
                            ref ElementData childData = ref child.Data;
                            childData.OnDoubleClick?.Invoke(new ClickEvent(child, childData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                        });

                        // Bubble event
                        BubbleEventToParents(hoveredElement, parent => {
                            ref ElementData parentData = ref parent.Data;
                            parentData.OnDoubleClick?.Invoke(new ClickEvent(parent, parentData.LayoutRect, PointerPos, PaperMouseBtn.Left));
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

                    // Propagate held to hooked children
                    PropagateEventToHookedChildren(activeElement, child => {
                        ref ElementData childData = ref child.Data;
                        childData.OnHeld?.Invoke(new ClickEvent(child, childData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                    });

                    // Bubble event
                    BubbleEventToParents(activeElement, parent => {
                        ref ElementData parentData = ref parent.Data;
                        parentData.OnHeld?.Invoke(new ClickEvent(parent, parentData.LayoutRect, PointerPos, PaperMouseBtn.Left));
                    });

                    if (IsPointerMoving)
                    {
                        bool wasDragging = _isDragging.TryGetValue(_activeElementId, out bool isDragging) && isDragging;
                        Vector2 startPos = _dragStartPos[_activeElementId];
                        Vector2 currentPos = PointerPos;
                        double distanceMoved = (currentPos - startPos).magnitude;
                        Rect layoutRect = data.LayoutRect;

                        // Only start dragging if we've moved beyond the threshold
                        if (!wasDragging && distanceMoved >= DRAG_THRESHOLD)
                        {
                            data.OnDragStart?.Invoke(new DragEvent(activeElement, layoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                            
                            // Propagate drag start to hooked children
                            PropagateEventToHookedChildren(activeElement, child => {
                                ref ElementData childData = ref child.Data;
                                childData.OnDragStart?.Invoke(new DragEvent(child, childData.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                            });
                            
                            BubbleEventToParents(activeElement, parent => {
                                ref ElementData parentData = ref parent.Data;
                                parentData.OnDragStart?.Invoke(new DragEvent(parent, parentData.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                            });

                            _isDragging[_activeElementId] = true;
                        }

                        // Handle continuous dragging
                        data.OnDragging?.Invoke(new DragEvent(activeElement, layoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                        
                        // Propagate dragging to hooked children
                        PropagateEventToHookedChildren(activeElement, child => {
                            ref ElementData childData = ref child.Data;
                            childData.OnDragging?.Invoke(new DragEvent(child, childData.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                        });
                        
                        BubbleEventToParents(activeElement, parent => {
                            ref ElementData parentData = ref parent.Data;
                            parentData.OnDragging?.Invoke(new DragEvent(parent, parentData.LayoutRect, PointerPos, startPos, PointerDelta, PointerDelta));
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Process keyboard events for the focused element and global navigation
        /// </summary>
        private void HandleKeyboardEvents()
        {
            // Process global navigation keys first (Tab)
            if (!SkipKeyboardNavigation && IsKeyPressed(PaperKey.Tab))
            {
                HandleTabNavigation();
                return; // Don't process other keys when Tab is pressed
            }

            // If no element has focus, there's nothing to do for other keys
            if (_focusedElementId == 0)
                return;

            ElementHandle focusedElement = FindElementByID(_focusedElementId);
            if (!focusedElement.IsValid)
                return;

            ref ElementData data = ref focusedElement.Data;

            // Process key presses
            foreach (var key in KeyValues)
            {
                if (IsKeyPressed(key))
                {
                    data.OnKeyPressed?.Invoke(new KeyEvent(focusedElement, key, IsKeyRepeating(key)));

                    // Propagate Key Pressed to hooked children
                    PropagateEventToHookedChildren(focusedElement, child => {
                        ref ElementData childData = ref child.Data;
                        childData.OnKeyPressed?.Invoke(new KeyEvent(child, key, IsKeyRepeating(key)));
                    });
                }
            }

            // Process text input
            while (InputString.Count > 0)
            {
                char c = InputString.Dequeue();
                data.OnTextInput?.Invoke(new TextInputEvent(focusedElement, c));

                // Propagate Text Input to hooked children
                PropagateEventToHookedChildren(focusedElement, child => {
                    ref ElementData childData = ref child.Data;
                    childData.OnTextInput?.Invoke(new TextInputEvent(child, c));
                });
            }
        }

        /// <summary>
        /// Handles Tab key navigation between elements with TabIndex
        /// </summary>
        private void HandleTabNavigation()
        {
            // Get all elements with valid tab indices
            var tabbableElements = new List<(int tabIndex, ulong elementId)>();
            
            // Brute force search through all elements
            for (int i = 0; i < _elementCount; i++)
            {
                ref ElementData data = ref _elements[i];
                if (data.TabIndex >= 0 && data.IsFocusable && data.Visible)
                {
                    tabbableElements.Add((data.TabIndex, data.ID));
                }
            }

            // If no tabbable elements, do nothing
            if (tabbableElements.Count == 0)
                return;

            // Sort by tab index
            tabbableElements.Sort((a, b) => a.tabIndex.CompareTo(b.tabIndex));

            ulong nextElementId;

            if (_focusedElementId == 0)
            {
                // No element focused, focus the first one (lowest TabIndex)
                nextElementId = tabbableElements[0].elementId;
            }
            else
            {
                // Find current element in the list
                int currentIndex = -1;
                for (int i = 0; i < tabbableElements.Count; i++)
                {
                    if (tabbableElements[i].elementId == _focusedElementId)
                    {
                        currentIndex = i;
                        break;
                    }
                }

                if (currentIndex == -1)
                {
                    // Current focused element doesn't have a TabIndex, focus first tabbable element
                    nextElementId = tabbableElements[0].elementId;
                }
                else
                {
                    // Move to next element, wrapping around to first if at end
                    int nextIndex = (currentIndex + 1) % tabbableElements.Count;
                    nextElementId = tabbableElements[nextIndex].elementId;
                }
            }

            // Focus the next element
            ElementHandle nextElement = FindElementByID(nextElementId);
            if (nextElement.IsValid)
            {
                // Update focused element
                if (_focusedElementId != 0)
                {
                    ElementHandle oldFocusedElement = FindElementByID(_focusedElementId);
                    if (oldFocusedElement.IsValid)
                    {
                        ref ElementData oldData = ref oldFocusedElement.Data;
                        oldData.OnFocusChange?.Invoke(new FocusEvent(oldFocusedElement, false));
                        
                        // Propagate focus loss to hooked children
                        PropagateEventToHookedChildren(oldFocusedElement, child => {
                            ref ElementData childData = ref child.Data;
                            childData.OnFocusChange?.Invoke(new FocusEvent(child, false));
                        });
                    }
                }

                _focusedElementId = nextElementId;
                ref ElementData nextData = ref nextElement.Data;
                nextData.OnFocusChange?.Invoke(new FocusEvent(nextElement, true));
                
                // Propagate focus gain to hooked children
                PropagateEventToHookedChildren(nextElement, child => {
                    ref ElementData childData = ref child.Data;
                    childData.OnFocusChange?.Invoke(new FocusEvent(child, true));
                });
            }
        }

        #endregion
    }
}
