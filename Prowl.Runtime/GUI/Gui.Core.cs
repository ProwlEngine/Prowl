using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.Rendering.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime.GUI
{
    public static class TestGUI
    {
        public static bool Button(string? label, Offset x, Offset y, Size width, Size height, float roundness = 2f) => Button(label, x, y, width, height, out _, roundness);
        public static bool Button(string? label, Offset x, Offset y, Size width, Size height, out LayoutNode node, float roundness = 2f)
        {
            var g = Gui.ActiveGUI;
            using ((node = g.Node()).Left(x).Top(y).Width(width).Height(height).Enter())
            {
                Interactable interact = g.GetInteractable();
                if (interact.OnClick())
                {
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, Color.green, roundness);
                    return true;
                }
                else if (interact.OnPressed())
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, Color.red, roundness);
                else if (interact.OnHover())
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, Color.blue, roundness);
                else
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, Color.white, roundness);

                if(label != null)
                    g.DrawText(label, 18, g.CurrentNode.LayoutData.Rect.Position, Color.black);
            }
            return false;
        }

        public static Gui gui;
        public static bool hoveringTest = false;
        public static void Test()
        {
            Rect screenRect = new Rect(0, 0, Runtime.Graphics.Resolution.x, Runtime.Graphics.Resolution.y);
            gui ??= new();

            gui.ProcessFrame(screenRect, (g) => {

                //int wrapNodeCount = (int)Mathf.Abs(Mathf.Sin(Time.time + 0.5f) * 25);
                int wrapNodeCount = 16;
                int columnNodeCount = (int)Mathf.Abs(Mathf.Sin(Time.time) * 10);
                //int panelWidth = (int)(500 * (1.0 + Mathf.Sin(Time.time) * 0.5));
                int panelWidth = 500;
                using (g.Node().Width(panelWidth).Height(500).TopLeft(Offset.Lerp(Offset.Percentage(0.10f), Offset.Percentage(0.20f), (float)Mathf.Sin(Time.time))).Padding(5).Layout(LayoutType.Row).AutoScaleChildren().Enter())
                {
                    // A
                    float animatedBool = g.AnimateBool(hoveringTest, 2f, 2f, EaseType.BounceIn, EaseType.BounceOut);
                    using (g.Node().MaxWidth(60 + (int)((100 * animatedBool))).Height(Size.Percentage(0.90f)).Layout(LayoutType.Grid).Enter())
                    {
                        g.SetZIndex(1);
                        g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.white, 2f, 4f);
                        g.PushClip(g.CurrentNode.LayoutData.InnerRect);
                        for (int i = 0; i < wrapNodeCount; i++)
                            using (g.Node().Width(50).Height(50).Margin(5).Enter())
                            {
                                g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.white, 2f, 4f);
                            }
                        g.PopClip();

                        g.ScrollV();
                    }

                    // B
                    //using (g.Node().Padding(5).Width(Size.Percentage(0.25f)).Height(Size.Percentage(0.90f)).Layout(LayoutType.Column).AutoScaleChildren().CenterContent().Enter())
                    //{
                    //    g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.white, 2f, 4f);
                    //    for (int i = 0; i < columnNodeCount; i++)
                    //        using (g.Node().Width(50).Enter())
                    //            g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.white, 2f, 4f);
                    //}

                    // C
                    using (g.Node().Width(Size.Percentage(0.25f)).Height(Size.Percentage(0.90f)).Enter())
                    {
                        g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.white, 2f, 4f);

                        if (Button("Test", 0, 0, 50, 25))
                            Debug.Log("Yey");

                        if(Button("test button", 0, 50, 75, 25, out var buttonNode))
                            Debug.Log("Pressed");

                        //buttonNode.Interaction = (interact) => {
                        //    hoveringTest = interact.IsHovering();
                        //};
                        //
                        //buttonNode.Draw = (drawlist) => {
                        //    drawlist.AddCircle();
                        //};
                        //
                        //float anim = g.AnimateBool(hoveringTest, 0.1f, EaseType.SineInOut);
                        //buttonNode.Width(50 + (10 * anim));
                        //buttonNode.Height(50 + (10 * anim));



                    }

                    // Footer
                    using (g.Node().Width(Size.Percentage(1.00f)).Height(Size.Percentage(0.10f)).Top(Offset.Percentage(0.90f)).IgnoreLayout().Enter())
                    {
                        g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.white, 2f, 4f);
                        if (g.IsHovering())
                        {
                            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, g.IsPressed() ? Color.red  : Color.blue, 4f);
                            g.CurrentNode.Height(Size.Percentage(0.20f));
                        }
                        else
                        {
                            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, Color.white, 4f);
                        }
                    }
                }



                //int panelWidth = (int)(500 * (1.0 + Mathf.Sin(Time.time) * 0.5));
                //using (g.Node().FitContent().Width(panelWidth).TopLeft(Offset.Percentage(0.10f)).Padding(5).Layout(LayoutType.Wrap).Enter())
                //{
                //    for (int i = 0; i < 48; i++)
                //        using (g.Node().Width(50).Height(50).Margin(3).Enter())
                //        {
                //            // Draw Here
                //        }
                //}


                //using (g.Node().Width(500).Height(500).TopLeft(Offset.Percentage(0.10f)).Padding(5).Layout(LayoutType.Row).Enter())
                //{
                //    // A
                //    using (g.Node().Height(Size.Percentage(0.90f)).Enter())
                //    {
                //    }
                //
                //    // Footer
                //    using (g.Node().Width(Size.Percentage(1.00f)).Height(Size.Percentage(0.10f)).Top(Offset.Percentage(0.90f)).IgnoreLayout().Enter())
                //    {
                //        // Draw Here
                //    }
                //}


                //using (g.Node().Width(500).Height(500).TopLeft(Offset.Percentage(0.50f)).Padding(5).Layout(LayoutType.Row).Enter())
                //{
                //    // A
                //    using (g.Node().Width(Size.Percentage(1f)).Height(Size.Percentage(0.90f)).Layout(LayoutType.Wrap).Enter())
                //    {
                //        g.CurrentScope.CenterContent = true;
                //        for (int i = 0; i < 60; i++)
                //            using (g.Node().Width(8).Height(8).Margin(5).Enter())
                //            {
                //            }
                //    }
                //
                //    // Footer
                //    using (g.Node().Width(Size.Percentage(1.00f)).Height(Size.Percentage(0.10f)).Top(Offset.Percentage(0.90f)).IgnoreLayout().Enter())
                //    {
                //        // Draw Here
                //    }
                //}
            });
        }
    }

    public partial class Gui
    {
        public static Gui ActiveGUI;

        public enum Pass { BeforeLayout, AfterLayout }

        public Rect ScreenRect { get; private set; }
        public Pass CurrentPass { get; private set; }

        public LayoutNode CurrentNode => layoutNodeScopes.First.Value;
        public LayoutNode PreviousNode => layoutNodeScopes.First.Next.Value;
        public GuiState CurrentState => guiStateScopes?.First?.Value ?? new GuiState();

        public UIDrawList DrawList {
            get {
                if (CurrentPass == Pass.BeforeLayout) return null;
                return _drawList[CurrentState.ZIndex];
            }
        }


        internal Dictionary<int, UIDrawList> _drawList = new();
        internal LinkedList<LayoutNode> layoutNodeScopes = new();
        internal LinkedList<GuiState> guiStateScopes = new();
        internal Stack<ulong> IDStack = new();

        private Dictionary<ulong, LayoutNode> _nodes;

        public Gui()
        {
            _nodes = [];
        }

        public void ProcessFrame(Rect screenRect, Action<Gui> gui)
        {
            UpdateAnimations(Time.deltaTime);

            ScreenRect = screenRect;

            //_drawList.Clear(); // We dont clear the draw lists so they are reused
            _nodes.Clear();
            layoutNodeScopes.Clear();
            guiStateScopes.Clear();
            IDStack.Clear();
            CurrentPass = Pass.BeforeLayout;

            if (!_drawList.ContainsKey(0))
                _drawList[0] = new UIDrawList(); // Root Draw List

            var root = new LayoutNode(this, 0);
            root.Width(screenRect.width).Height(screenRect.height);
            _nodes[0] = root;

            PushNode(root);
            // The first pass Produces all the nodes and structure the user wants
            // Draw calls are Ignored
            // Reset Nodes
            DoPass(Pass.BeforeLayout, gui);

            // Now that we have the nodes we can properly process their LayoutNode
            // Like if theres a GridLayout node we can process that here
            //DoPass(Pass.Layout);
            root.UpdateCache();
            root.ProcessLayout();

            // The second pass handles drawing
            // All the Nodes and such will have the same ID due to Hashing being consistent
            // So We match ID's and this time we Draw
            var keys = _drawList.Keys;
            // sort keys
            foreach (var index in keys.OrderBy(x => x))
            {
                _drawList[index].Clear();
                _drawList[index].PushTextureID(UIDrawList._fontAtlas.TexID);
            }
                
            DoPass(Pass.AfterLayout, gui);

            PopNode();

            UIDrawList.Draw(GLDevice.GL, new(screenRect.width, screenRect.height), [.. _drawList.Values]);


            //if (DrawNodeRects)
            //{
            //    var drawlist = new UIDrawList();
            //    drawlist.PushClipRectFullScreen();
            //    drawlist.PushTextureID(UIDrawList._fontAtlas.TexID);
            //
            //    int id = 100;
            //    var rand = new System.Random(100);
            //    foreach (var node in _nodes.Values)
            //    {
            //        // Unique color per id
            //        int r = (id & 0xFF);
            //        int g = ((id >> 8) & 0xFF);
            //        int b = ((id >> 16) & 0xFF);
            //        uint col = (uint)(((id & 0xFF) << 16) | (((id >> 8) & 0xFF) << 8) | ((id >> 16) & 0xFF) | 0xFF000000);
            //        id = rand.Next();
            //
            //        drawlist.AddRect(node.LayoutData.GlobalPosition, node.LayoutData.GlobalPosition + node.LayoutData.Scale, col, 0, 0, 1.0f);
            //    }
            //
            //    drawlist.PopClipRect();
            //    drawlist.PopTextureID();
            //    UIDrawList.Draw(GLDevice.GL, Runtime.Graphics.Resolution, [drawlist]);
            //}
        }

        private void DoPass(Pass pass, Action<Gui> gui)
        {
            CurrentPass = pass;
            try
            {
                ActiveGUI = this;
                gui?.Invoke(this);
            }
            catch(Exception e)
            {
                Debug.LogError("Something went wrong in the GUI Update: " + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                ActiveGUI = null;
            }
        }

        public LayoutNode Node([CallerMemberName] string lineMethod = "", [CallerLineNumber] int lineNumber = 0)
        {
            int nodeId;
            if(CurrentPass == Pass.BeforeLayout)
            {
                CurrentNode._nextNodeIndexA++;
                nodeId = CurrentNode._nextNodeIndexA;
            }
            else
            {
                CurrentNode._nextNodeIndexB++;
                nodeId = CurrentNode._nextNodeIndexB;
            }

            ulong storageHash = (ulong)HashCode.Combine(IDStack.Peek(), lineMethod, lineNumber, nodeId);

            // First pass Creates the Nodes for this frame
            if (CurrentPass == Pass.BeforeLayout)
            {
                if(_nodes.ContainsKey(storageHash))
                    throw new InvalidOperationException("Node ID already exists, Use Gui.PushID() and Gui.PopID() to create a new ID. Note: This can occur naturally due to Hash Collisions.");

                var node = new LayoutNode(this, storageHash);
                node.SetNewParent(CurrentNode);
                _nodes[storageHash] = node;
                return node;
            }

            if (_nodes.TryGetValue(storageHash, out var existingNode))
                return existingNode;

            throw new InvalidOperationException("Node ID not found, Did state change during a single GUI Update?");
        }

        internal void PushNode(LayoutNode node)
        {
            layoutNodeScopes.AddFirst(node);
            guiStateScopes.AddFirst(CurrentState.Clone());
            IDStack.Push(node.ID);

            if (CurrentPass == Pass.AfterLayout && CurrentNode._clipped != ClipType.None)
            {
                var rect = node._clipped == ClipType.Inner ? node.LayoutData.InnerRect : node.LayoutData.Rect;
                _drawList[CurrentZIndex].PushClipRect(new Vector4(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height));
            }
        }

        internal void PopNode()
        {
            if (CurrentPass == Pass.AfterLayout && CurrentNode._clipped != ClipType.None)
                _drawList[CurrentZIndex].PopClipRect();

            IDStack.Pop();
            guiStateScopes.RemoveFirst();
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
            _node.Gui.PushNode(_node);
        }

        public void Dispose()
        {
            _node.Gui.PopNode();
        }
    }

}
