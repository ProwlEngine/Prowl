using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.Rendering.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public static Gui ActiveGUI;

        public Rect ScreenRect { get; private set; }

        public LayoutNode CurrentNode => layoutNodeScopes.First!.Value._node;
        public LayoutNode PreviousNode { get; private set; }

        public UIDrawList DrawList => _drawList[CurrentNode.ZIndex];

        internal Dictionary<int, UIDrawList> _drawList = new();
        internal LinkedList<LayoutNodeScope> layoutNodeScopes = new();
        internal Stack<ulong> IDStack = new();
        internal bool layoutDirty = false;
        internal ulong frameCount = 0;

        private Dictionary<ulong, LayoutNode.PostLayoutData> _previousLayoutData;
        private LayoutNode rootNode;
        private Dictionary<ulong, ulong> _computedNodes;
        private HashSet<ulong> _createdNodes;

        public Gui()
        {
            rootNode = null;
            _computedNodes = [];
            _previousLayoutData = [];
            _createdNodes = [];
        }

        public void ProcessFrame(Rect screenRect, float uiScale, Vector2 frameBufferScale, Action<Gui> gui)
        {
            UpdateAnimations(Time.deltaTime);

            uiScale = 1f / uiScale;
            screenRect.width *= uiScale;
            screenRect.height *= uiScale;
            ScreenRect = screenRect;

            layoutNodeScopes.Clear();
            nodeCountPerLine.Clear();
            _createdNodes.Clear();
            IDStack.Clear();

            if (!_drawList.ContainsKey(0))
                _drawList[0] = new UIDrawList(); // Root Draw List

            // The second pass handles drawing
            // All the Nodes and such will have the same ID due to Hashing being consistent
            // So We match ID's and this time we Draw
            List<UIDrawList> drawListsOrdered = new();
            foreach (var index in _drawList.Keys.OrderBy(x => x))
            {
                _drawList[index].Clear();
                _drawList[index].PushTextureID(UIDrawList.DefaultFont.Texture.Handle);
                drawListsOrdered.Add(_drawList[index]);
            }

            rootNode = new LayoutNode(null, this, 0);
            rootNode.Width(screenRect.width).Height(screenRect.height);
            PreviousNode = rootNode;

            // The first pass Produces all the nodes and structure the user wants
            // Draw calls are Ignored
            // Reset Nodes
            layoutDirty = false;
            PushNode(new(rootNode));
            DoPass(gui, frameBufferScale);
            PopNode();

            UIDrawList.Draw(GLDevice.GL, new(screenRect.width, screenRect.height), drawListsOrdered.ToArray());

            // Look for any nodes whos HashCode does not match the previously computed nodes
            layoutDirty |= _createdNodes.Count != _computedNodes.Count;
            if (!layoutDirty)
                layoutDirty = MatchHash(rootNode);

            // Now that we have the nodes we can properly process their LayoutNode
            // Like if theres a GridLayout node we can process that here
            if (layoutDirty)
            {
                rootNode.UpdateCache();
                rootNode.ProcessLayout();
                rootNode.UpdateCache();
                // Cache layout data
                _previousLayoutData.Clear();
                CacheLayoutData(rootNode);
            }
        }

        private void CacheLayoutData(LayoutNode node)
        {
            _previousLayoutData[node.ID] = node.LayoutData;
            foreach (var child in node.Children)
                CacheLayoutData(child);
        }

        private void DoPass(Action<Gui> gui, Vector2 frameBufferScale)
        {
            try
            {
                ActiveGUI = this;
                StartInputFrame(frameBufferScale);
                StartInteractionFrame();
                gui?.Invoke(this);
                frameCount++;
            }
            catch (Exception e)
            {
                Debug.LogError("Something went wrong in the GUI Update: " + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                ActiveGUI = null;
                EndInteractionFrame();
                EndInputFrame();
            }
        }

        private bool MatchHash(LayoutNode node)
        {
            var newHash = node.GetHashCode64();
            bool dirty = !_computedNodes.TryGetValue(node.ID, out var hash) || hash != newHash;
            _computedNodes[node.ID] = newHash;
            foreach (var child in node.Children)
                dirty |= MatchHash(child);
            return dirty;
        }

        public LayoutNode Node(string stringID, [CallerLineNumber] int intID = 0) => Node(CurrentNode, stringID, intID);

        private Dictionary<ulong, uint> nodeCountPerLine = [];
        public LayoutNode Node(LayoutNode parent, string stringID, [CallerLineNumber] int intID = 0)
        {
            ulong lineHash = (ulong)HashCode.Combine(stringID, intID);
            ulong storageHash = (ulong)HashCode.Combine(IDStack.Peek(), lineHash);

            if (_createdNodes.Contains(storageHash))
                throw new InvalidOperationException("Node already exists with this ID: " + stringID + ":" + intID + " = " + storageHash);

            _createdNodes.Add(storageHash);

            LayoutNode node = new(parent, this, storageHash);
            // If we generated data for this node last frame, Use that data instead of having to recompute it
            // This is actually vital, since we don't store nodes between frames, so even if we didn't do this and recomputed every frame
            // We would never see the nodes we created/drawn since their data is computed at the end of the frame, after drawing, only to be discarded early next frame before drawing
            if (_previousLayoutData.TryGetValue(storageHash, out var data))
            {
                data._node = node;
                node.LayoutData = data;
            }
            else
            {
                // We didnt have this node last frame! So we need to recompute layout.
                layoutDirty = true;
            }

            return node;
        }

        internal void PushNode(LayoutNodeScope scope)
        {
            layoutNodeScopes.AddFirst(scope);
            IDStack.Push(scope._node.ID);

            if (CurrentNode._clipped != ClipType.None)
            {
                var rect = scope._node._clipped == ClipType.Inner ? scope._node.LayoutData.InnerRect : scope._node.LayoutData.Rect;
                _drawList[CurrentZIndex].PushClipRect(new Vector4(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height));
            }
        }

        internal void PopNode()
        {
            if (CurrentNode._clipped != ClipType.None)
                _drawList[CurrentZIndex].PopClipRect();
            PreviousNode = CurrentNode;
            IDStack.Pop();
            layoutNodeScopes.RemoveFirst();
        }
    }

    public enum LayoutType { None, Column, Row, Grid }

    public enum ClipType { None, Inner, Outer }

    public class LayoutNodeScope : IDisposable
    {
        public LayoutNode _node;

        public LayoutNodeScope(LayoutNode node)
        {
            _node = node;
        }

        public void Dispose()
        {
            _node.Gui.PopNode();
        }
    }

}
