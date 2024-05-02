using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.Rendering.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                    g.DrawText(label, 20, g.CurrentNode.LayoutData.Rect.Position, Color.black);
            }
            return false;
        }

        public static Gui gui;
        public static float sizePanelAnim = 0f;
        public static void Test(Font font)
        {
            Rect screenRect = new Rect(0, 0, Runtime.Graphics.Resolution.x, Runtime.Graphics.Resolution.y);
            gui ??= new();

            gui.ProcessFrame(screenRect, (g) => {

                //int wrapNodeCount = (int)Mathf.Abs(Mathf.Sin(Time.time + 0.5f) * 25);
                int wrapNodeCount = 16;
                int columnNodeCount = (int)Mathf.Abs(Mathf.Sin(Time.time) * 10);
                //int panelWidth = (int)(500 * (1.0 + Mathf.Sin(Time.time) * 0.5));
                int panelWidth = 500;
                using (g.Node().Width(panelWidth).Height(500).TopLeft(Offset.Lerp(Offset.Percentage(0.10f), Offset.Percentage(0.20f), (float)Mathf.Sin(0.0))).Padding(5).Layout(LayoutType.Row).AutoScaleChildren().Enter())
                {
                    // A
                    using (g.Node().Height(Size.Percentage(0.90f)).Layout(LayoutType.Grid).Enter())
                    {
                        if(g.IsHovering())
                            g.PreviousNode.MaxWidth(100);
                        else
                            g.PreviousNode.MaxWidth(1000);

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
                    using (g.Node().Width(Size.Percentage(1.00f)).Height(Size.Percentage(0.10f)).Top(Offset.Percentage(0.90f)).IgnoreLayout().Clip().Enter())
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

                        var textRect = g.CurrentNode.LayoutData.InnerRect;
                        textRect.Reduce(new Vector2(5, 5));
                        g.DrawText(font, @"Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nam interdum nec ante et condimentum. Aliquam quis viverraodio. Etiam vel tortor in ante lobortis tristique non inmauris. Maecenas massa tellus, aliquet vel massa eget, commodo commodo neque. In at erat ut nisi aliquam condimentum eu vitae quam. Suspendisse tristique euismod libero. Cras non massa nibh.Suspendisse id justo nibh. Nam ut diam id nunc ultrices aliquam cursus at ipsum. Praesent dapibus mauris gravida massa dapibus, vitae posuere magna finibus. Phasellus dignissim libero metus, vitae tincidunt massa lacinia eget. Cras sed viverra tortor. Vivamus iaculis faucibus ex non suscipit. In fringilla tellus at lorem sollicitudin, ut placerat nibh mollis. Nullam tortor elit, aliquet ac efficitur vel, ornare eget nibh. Vivamus condimentum, dui id vehicula iaculis, velit velit pulvinar nisi, mollis blandit nibh arcu ut magna. Vivamus condimentum in magna in aliquam. Donec vitae elementum neque. Nam ac ipsum id orci finibus fringilla. Nulla non justo a augue congue dictum. Vestibulum in quam id nibh blandit laoreet.", 
                            20, textRect, Color.black);
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

        public enum Pass { AfterLayout }

        public Rect ScreenRect { get; private set; }

        public LayoutNode CurrentNode => layoutNodeScopes.First.Value._node;
        public LayoutNode PreviousNode => layoutNodeScopes.First.Next.Value._node;
        public GuiState CurrentState => guiStateScopes?.First?.Value ?? new GuiState();

        public UIDrawList DrawList {
            get {
                return _drawList[CurrentState.ZIndex];
            }
        }


        internal Dictionary<int, UIDrawList> _drawList = new();
        internal LinkedList<LayoutNodeScope> layoutNodeScopes = new();
        internal LinkedList<GuiState> guiStateScopes = new();
        internal Stack<ulong> IDStack = new();
        internal bool layoutDirty = false;

        private Dictionary<ulong, LayoutNode> _nodes;
        private Dictionary<ulong, ulong> _computedNodes;

        public Gui()
        {
            _nodes = [];
            _computedNodes = [];
        }

        public void ProcessFrame(Rect screenRect, Action<Gui> gui)
        {
            UpdateAnimations(Time.deltaTime);

            ScreenRect = screenRect;

            layoutNodeScopes.Clear();
            guiStateScopes.Clear();
            IDStack.Clear();

            if (!_drawList.ContainsKey(0))
                _drawList[0] = new UIDrawList(); // Root Draw List

            // The second pass handles drawing
            // All the Nodes and such will have the same ID due to Hashing being consistent
            // So We match ID's and this time we Draw
            var keys = _drawList.Keys;
            foreach (var index in keys.OrderBy(x => x))
            {
                _drawList[index].Clear();
                _drawList[index].PushTextureID(UIDrawList.DefaultFont.Texture.Handle);
            }

            LayoutNode root = null;
            if (!_nodes.TryGetValue(0, out root))
            {
                root = new LayoutNode(this, 0);
                _nodes[0] = root;
            }
            root.Width(screenRect.width).Height(screenRect.height);

            // The first pass Produces all the nodes and structure the user wants
            // Draw calls are Ignored
            // Reset Nodes
            layoutDirty = false;
            PushNode(new(root));
            DoPass(Pass.AfterLayout, gui);
            PopNode();

            UIDrawList.Draw(GLDevice.GL, new(screenRect.width, screenRect.height), [.. _drawList.Values]);

            // Look for any nodes whos HashCode does not match the previously computed nodes
            layoutDirty |= MatchHash(root);

            // Now that we have the nodes we can properly process their LayoutNode
            // Like if theres a GridLayout node we can process that here
            if (layoutDirty)
            {
                root.UpdateCache();
                root.ProcessLayout();
            }
        }

        private void DoPass(Pass pass, Action<Gui> gui)
        {
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

        private bool MatchHash(LayoutNode node)
        {
            var newHash = node.GetHashCode64();
            bool dirty = !_computedNodes.TryGetValue(node.ID, out var hash) || hash != newHash;
            _computedNodes[node.ID] = newHash;
            foreach (var child in node.Children)
                dirty |= MatchHash(child);
            return dirty;
        }

        public LayoutNode Node([CallerMemberName] string lineMethod = "", [CallerLineNumber] int lineNumber = 0)
        {
            int nodeId = layoutNodeScopes.First.Value.nodeIndex++;

            if (CurrentNode.Children.Count > nodeId)
            {
                return CurrentNode.Children[nodeId];
            }
            else
            {
                ulong storageHash = (ulong)HashCode.Combine(IDStack.Peek(), lineMethod, lineNumber, nodeId);
                var node = new LayoutNode(this, (ulong)storageHash);
                node.SetNewParent(CurrentNode);
                layoutDirty = true;
                return node;
            }
        }

        internal void PushNode(LayoutNodeScope scope)
        {
            layoutNodeScopes.AddFirst(scope);
            guiStateScopes.AddFirst(CurrentState.Clone());
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
        public int nodeIndex = 0;

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
