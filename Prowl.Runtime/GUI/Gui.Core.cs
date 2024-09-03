using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Runtime.GUI.Layout;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public static Stack<Gui> ActiveGUIStack = [];
        public static Gui ActiveGUI => ActiveGUIStack.Count > 0 ? ActiveGUIStack.Peek() : null;

        public Rect ScreenRect { get; private set; }

        public LayoutNode CurrentNode => layoutNodeScopes.First!.Value._node;
        public LayoutNode PreviousNode { get; private set; }

        public double UIScale => 1.0 / uiScale;

        public readonly GuiDraw2D Draw2D;
        public readonly GuiDraw3D Draw3D;

        internal LinkedList<LayoutNodeScope> layoutNodeScopes = new();
        internal Stack<ulong> IDStack = new();
        internal bool layoutDirty = false;
        internal ulong frameCount = 0;
        internal readonly List<LayoutNode> ScollableNodes = new();

        private Dictionary<ulong, LayoutNode.PostLayoutData> _previousLayoutData;
        private LayoutNode rootNode;
        private Dictionary<ulong, ulong> _computedNodeHashes;
        private HashSet<ulong> _createdNodes;
        private double uiScale = 1;
        private double lastUIScale = -1;

        public Gui(bool antiAliasing)
        {
            rootNode = null;
            _computedNodeHashes = [];
            _previousLayoutData = [];
            _createdNodes = [];

            Draw2D = new(this, antiAliasing);
            Draw3D = new(this);
        }

        public void ProcessFrame(Veldrid.CommandList commandList, Rect screenRect, double uiScale, Vector2 frameBufferScale, bool antiAliasing, Action<Gui> gui)
        {
            UpdateAnimations(Time.deltaTime);

            uiScale = 1.0 / uiScale;
            this.uiScale = uiScale;
            screenRect.width *= uiScale;
            screenRect.height *= uiScale;
            ScreenRect = screenRect;

            layoutNodeScopes.Clear();
            nodeCountPerLine.Clear();
            _createdNodes.Clear();
            IDStack.Clear();

            ScollableNodes.Clear();

            nextPopupIndex = 0;

            Draw2D.BeginFrame(antiAliasing);

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

            Draw2D.EndFrame(commandList, screenRect);

            // Look for any nodes whos HashCode does not match the previously computed nodes
            layoutDirty |= _createdNodes.Count != (_computedNodeHashes.Count - 1); // -1 because createdNodes doesn't count the root node but computedNodeHashes does

            var newNodeHashes = new Dictionary<ulong, ulong>();
            if (MatchHash(rootNode, ref newNodeHashes))
                layoutDirty = true;
            _computedNodeHashes = newNodeHashes;

            if (lastUIScale == uiScale)
                layoutDirty = true;

            // Now that we have the nodes we can properly process their LayoutNode
            // Like if theres a GridLayout node we can process that here
            if (layoutDirty)
            {
                lastUIScale = uiScale;

                rootNode.UpdateCache();
                rootNode.ProcessLayout();
                //rootNode.UpdateCache();
                //rootNode.ProcessLayout(); // TODO: Why do we need to run the UI twice? - Commented this out and it seems to work fine 23/06/2024
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
                ActiveGUIStack.Push(this);
                StartInputFrame(frameBufferScale * uiScale);
                StartInteractionFrame();
                gui?.Invoke(this);
                // Draw Scrollbars
                foreach (var node in ScollableNodes)
                    node.DrawScrollbars();
                frameCount++;
            }
            catch (Exception e)
            {
                Debug.LogError("Something went wrong in the GUI Update: " + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                ActiveGUIStack.Pop();
                EndInteractionFrame();
                EndInputFrame();
            }
        }

        private bool MatchHash(LayoutNode node, ref Dictionary<ulong, ulong> newNodeHashes)
        {
            var newHash = node.GetHashCode64();
            bool dirty = !_computedNodeHashes.TryGetValue(node.ID, out var hash) || hash != newHash;
            newNodeHashes[node.ID] = newHash;
            foreach (var child in node.Children)
                dirty |= MatchHash(child, ref newNodeHashes);
            return dirty;
        }

        public LayoutNode Node(string stringID, [CallerLineNumber] int intID = 0) => Node(CurrentNode, stringID, intID);

        private Dictionary<ulong, uint> nodeCountPerLine = [];
        public LayoutNode Node(LayoutNode parent, string stringID, [CallerLineNumber] int intID = 0)
        {
            ulong storageHash = (ulong)HashCode.Combine(parent.ID, IDStack.Peek(), stringID, intID);

            if (_createdNodes.Contains(storageHash))
            {
                Debug.LogWarning("Node already exists with this ID: " + stringID + ":" + intID + " = " + storageHash + "\nForcing a new ID, This may cause increased flickering in the UI.");
                storageHash = (ulong)HashCode.Combine(storageHash, storageHash, intID);
                //throw new InvalidOperationException("Node already exists with this ID: " + stringID + ":" + intID + " = " + storageHash);
            }

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
                var rect = scope._node._clipped == ClipType.Inner ? scope._node.LayoutData.InnerRect_NoScroll : scope._node.LayoutData.Rect;
                Draw2D.PushClip(rect);
            }
        }

        internal void PopNode()
        {
            if (CurrentNode._clipped != ClipType.None)
                Draw2D.PopClip();
            PreviousNode = CurrentNode;
            IDStack.Pop();
            layoutNodeScopes.RemoveFirst();
        }
    }

    public enum LayoutType { None, Column, ColumnReversed, Row, RowReversed, Grid, GridReversed }

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
