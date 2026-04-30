// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        #region Drag-and-Drop session state

        private sealed class DragSession
        {
            public object Payload;
            public Action<Canvas, Float2> GhostDrawer;
            public int SourceElementId;
        }

        private DragSession _activeDrag;

        // The element id of the topmost currently-valid drop target under the pointer this frame.
        // Refreshed by UpdateDropTargetUnderPointer; consumed by IsValidDropTarget and the .DropTargeted
        // state-driven style. 0 = no valid target.
        private int _dropTargetUnderPointerId;

        /// <summary>True while a drag-and-drop session is in flight.</summary>
        public bool IsDragging => _activeDrag != null;

        /// <summary>The payload of the active drag, or null when no drag is in flight.</summary>
        public object CurrentDragPayload => _activeDrag?.Payload;

        /// <summary>
        /// True when this element is currently the topmost valid drop target under the pointer
        /// during an active drag — its <c>AcceptDrop&lt;T&gt;</c> handler matched the payload type
        /// and its <c>canAccept</c> predicate returned true.
        /// </summary>
        public bool IsValidDropTarget(int elementId) => _dropTargetUnderPointerId == elementId;

        /// <summary>
        /// Begin a drag-and-drop session manually. Most callers should use
        /// <c>ElementBuilder.DragSource</c> instead — it triggers this automatically once the
        /// pointer crosses the drag threshold. Use this overload when initiating a drag from
        /// a non-element source (e.g. a keyboard shortcut, an external file drop).
        /// </summary>
        public void StartDrag(object payload, Action<Canvas, Float2> drawGhost = null)
        {
            if (payload == null) return;
            _activeDrag = new DragSession
            {
                Payload = payload,
                GhostDrawer = drawGhost,
                SourceElementId = 0,
            };
        }

        /// <summary>
        /// Cancel an active drag without firing any onDrop callback. Auto-fired when the user
        /// presses Escape.
        /// </summary>
        public void CancelDrag()
        {
            _activeDrag = null;
            _dropTargetUnderPointerId = 0;
        }

        // Internal: invoked by the drag-start path in HandleMouseEvents when an element with
        // a registered DragSourceFactory crosses the drag threshold.
        internal void BeginDragFromSource(ElementHandle source)
        {
            ref ElementData data = ref source.Data;
            if (data.DragSourceFactory == null) return;
            object payload = null;
            try { payload = data.DragSourceFactory(); } catch { /* swallow > drag never starts */ }
            if (payload == null) return;

            _activeDrag = new DragSession
            {
                Payload = payload,
                GhostDrawer = data.DragGhostDrawer,
                SourceElementId = source.Data.ID,
            };
        }

        // Internal: each frame, update which (if any) element under the pointer would accept the
        // current payload. Called from HandleInteractions while a drag is active.
        internal void UpdateDropTargetUnderPointer()
        {
            _dropTargetUnderPointerId = 0;
            if (_activeDrag == null || _theHoveredElementId == 0) return;

            // Walk topmost-down from the hovered element through its ancestors. The first
            // element with a matching acceptor wins. Lets a child be the "real" target while
            // a permissive parent still catches drops its children rejected.
            object payload = _activeDrag.Payload;
            ElementHandle current = FindElementByID(_theHoveredElementId);
            while (current.IsValid)
            {
                ref ElementData data = ref current.Data;
                if (data.DropAcceptors != null && FindMatchingAcceptor(data.DropAcceptors, payload) >= 0)
                {
                    _dropTargetUnderPointerId = data.ID;
                    return;
                }
                current = current.GetParentHandle();
            }
        }

        // Internal: pointer-up while a drag is active. Fire onDrop on the current valid target
        // (if any), then clear session.
        internal void FinalizeDragOnPointerUp()
        {
            if (_activeDrag == null) return;

            if (_dropTargetUnderPointerId != 0)
            {
                ElementHandle target = FindElementByID(_dropTargetUnderPointerId);
                if (target.IsValid && target.Data.DropAcceptors != null)
                {
                    int idx = FindMatchingAcceptor(target.Data.DropAcceptors, _activeDrag.Payload);
                    if (idx >= 0)
                    {
                        var ctx = new DropContext(target, target.Data.LayoutRect, PointerPos);
                        try { target.Data.DropAcceptors[idx].OnDrop(_activeDrag.Payload, ctx); }
                        catch (Exception) { /* swallow > avoid leaving session live on handler crash */ }
                    }
                }
            }

            _activeDrag = null;
            _dropTargetUnderPointerId = 0;
        }

        private static int FindMatchingAcceptor(List<DropAcceptor> acceptors, object payload)
        {
            for (int i = 0; i < acceptors.Count; i++)
            {
                if (acceptors[i].CanAccept != null && acceptors[i].CanAccept(payload))
                    return i;
            }
            return -1;
        }

        // Internal: end-of-frame draw for the drag ghost. Called from EndFrame after all
        // deferred layer passes so the ghost sits above everything (including Topmost popovers).
        internal void DrawDragGhostIfActive()
        {
            if (_activeDrag == null || _activeDrag.GhostDrawer == null) return;
            // Reset transform — the ghost lives in screen space, not whatever transform was
            // last on the canvas stack from a deferred render.
            _canvas.SaveState();
            _canvas.ResetState();
            try { _activeDrag.GhostDrawer(_canvas, PointerPos); }
            catch { /* swallow > one bad ghost shouldn't kill the frame */ }
            _canvas.RestoreState();
        }

        #endregion
    }
}
