using Prowl.Editor.Editor.Preferences;
using Prowl.Editor.EditorGUI.Docking;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using System.ComponentModel;

namespace Prowl.Editor
{
    public class EditorWindow
    {
        protected string Title = "Title";
        internal readonly int _id;

        protected virtual bool Center { get; } = false;
        protected virtual double Width { get; } = 256;
        protected virtual double Height { get; } = 256;
        protected virtual bool TitleBar { get; } = true;
        protected virtual bool IsDockable { get; } = true;
        protected virtual bool LockSize { get; } = false;
        protected virtual bool BackgroundFade { get; } = false;

        protected bool isOpened = true;
        protected Runtime.GUI.Gui g => Runtime.GUI.Gui.ActiveGUI;

        private double _width, _height;
        public double _x, _y;
        private bool _wasDragged = false;


        public bool bAllowTabs = true;

        public Vector2 DockPosition;
        public Vector2 DockSize;

        public bool IsDocked => m_Leaf != null;
        private DockNode m_Leaf;
        private Vector2 m_DockPosition;

        public DockNode Leaf {
            get => m_Leaf;
            internal set => m_Leaf = value;
        }

        public EditorWindow() : base()
        {
            EditorGui.Windows.Add(this);
            _id = GetHashCode();

            _width = Width;
            _height = Height;
        }

        public void ProcessFrame()
        {
            try
            {
                Update();
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError("Error in UpdateWindow: " + e.Message + "\n" + e.StackTrace);
            }

            try
            {
                isOpened = true;

                if (BackgroundFade)
                {
                    g.DrawRectFilled(g.ScreenRect, new System.Numerics.Vector4(0, 0, 0, 0.5f));
                    // Ensure were at the start of the EditorWindows List
                    EditorGui.FocusWindow(this);
                }


                if (Center)
                {
                    var vp_size = g.ScreenRect.Size / 2;
                    _x = vp_size.x - (_width / 2);
                    _y = vp_size.y - (_height / 2);
                }

                var width = _width;
                var height = _height;
                if(IsDocked)
                {
                    _x = DockPosition.x;
                    _y = DockPosition.y;
                    width = DockSize.x;
                    height = DockSize.y;
                }

                using (g.Node().Width(width).Height(height).Left(_x).Top(_y).Layout(LayoutType.Column).AutoScaleChildren().Enter())
                {
                    // TODO: Resize

                    if (TitleBar)
                    {
                        using (g.Node().Width(Size.Percentage(1f)).MaxHeight(20).Enter())
                        {
                            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.HeaderColor);

                            if (IsDocked && Leaf.LeafWindows.Count > 0)
                            {
                                // Draw Tabs
                                var tabWidth = g.CurrentNode.LayoutData.Rect.width / Leaf.LeafWindows.Count;
                                tabWidth = Math.Min(tabWidth, 75);

                                for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                                {
                                    var window = Leaf.LeafWindows[i];
                                    using (g.Node().Width(tabWidth).Height(20).Left(i * (tabWidth + 5)).Enter())
                                    {
                                        if (window != this)
                                        {
                                            var interact = g.GetInteractable();
                                            if (interact.TakeFocus())
                                            {
                                                Leaf.WindowNum = i;
                                            }
                                            if (interact.IsHovered())
                                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new(1f, 1f, 1f, 0.5f));
                                        }
                                        g.DrawText(UIDrawList.DefaultFont, window.Title, 20, g.CurrentNode.LayoutData.Rect, Color.white);
                                    }
                                }
                            }
                            else
                            {
                                g.DrawText(UIDrawList.DefaultFont, Title, 20, g.CurrentNode.LayoutData.Rect, Color.white);
                            }

                            // Close Button
                            using (g.Node().Width(20).Height(20).Left(Offset.Percentage(1f, -20)).Enter())
                            {
                                var interact = g.GetInteractable();
                                if (interact.TakeFocus())
                                    isOpened = false;
                                if (interact.IsHovered())
                                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new(1f, 1f, 1f, 0.5f));
                                g.DrawText(UIDrawList.DefaultFont, FontAwesome6.X, 20, g.CurrentNode.LayoutData.Rect, Color.white);
                            }

                            var titleInteract = g.GetInteractable();
                            if (EditorGui.DragSplitter == null)
                            {
                                if (titleInteract.TakeFocus() || (titleInteract.IsFocused() && titleInteract.IsActive()))
                                {
                                    _wasDragged = true;

                                    _x += Input.MouseDelta.x;
                                    _y += Input.MouseDelta.y;
                                    EditorGui.DraggingWindow = this;

                                    if (g.IsPointerMoving && IsDocked)
                                    {
                                        EditorGui.Container.DetachWindow(this);
                                        // Position the window so the mouse is over the title bar
                                        _x = g.PointerPos.x - (_width / 2);
                                        _y = g.PointerPos.y - 10;
                                    }

                                    if (IsDockable && !IsDocked)
                                    {
                                        var oldZ = g.CurrentZIndex;
                                        g.SetZIndex(10000);
                                        // Draw Docking Placement
                                        Vector2 cursorPos = g.PointerPos;
                                        DockPlacement placement = EditorGui.Container.GetPlacement(cursorPos.x, cursorPos.y);
                                        if (placement)
                                        {
                                            g.DrawList.PathLineTo(placement.PolygonVerts[0]);
                                            g.DrawList.PathLineTo(placement.PolygonVerts[1]);
                                            g.DrawList.PathLineTo(placement.PolygonVerts[2]);
                                            g.DrawList.PathLineTo(placement.PolygonVerts[3]);
                                            g.DrawList.PathFill(UIDrawList.ColorConvertFloat4ToU32(Color.yellow * 0.5f));

                                            g.DrawList.PathLineTo(placement.PolygonVerts[0]);
                                            g.DrawList.PathLineTo(placement.PolygonVerts[1]);
                                            g.DrawList.PathLineTo(placement.PolygonVerts[2]);
                                            g.DrawList.PathLineTo(placement.PolygonVerts[3]);
                                            //g.DrawList.PathLineTo(placement.PolygonVerts[0]);
                                            g.DrawList.PathStroke(UIDrawList.ColorConvertFloat4ToU32(Color.yellow * 0.5f), true, 2f);
                                        }
                                        g.SetZIndex(oldZ);
                                    }
                                }
                                else
                                {
                                    if (_wasDragged)
                                    {
                                        _wasDragged = false;
                                        if (IsDockable && !IsDocked)
                                        {
                                            Vector2 cursorPos = g.PointerPos;
                                            EditorGui.Container.AttachWindowAt(this, cursorPos.x, cursorPos.y);
                                        }
                                    }

                                    if (EditorGui.DraggingWindow == this)
                                    {
                                        EditorGui.DraggingWindow = null;
                                    }
                                }
                            }
                        }

                        using (g.Node().Width(Size.Percentage(1f)).Clip().Enter())
                        {
                            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBGColor);
                            Draw();
                        }
                    }
                    else
                    {
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBGColor);
                        Draw();
                    }
                }


                if (!isOpened)
                {
                    if (IsDocked)
                        EditorGui.Container.DetachWindow(this);
                    EditorGui.Remove(this);
                    Close();
                }
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError("Error in EditorWindow: " + e.Message + "\n" + e.StackTrace);
            }
        }

        protected virtual void Draw() { }
        protected virtual void Update() { }
        protected virtual void Close() { }

    }

    public class ConsoleWindow : EditorWindow
    {
        protected override double Width { get; } = 512 + (512 / 2);
        protected override double Height { get; } = 256;

        private uint _logCount;
        private readonly List<LogMessage> _logMessages;
        private int _maxLogs = 100;

        public ConsoleWindow() : base()
        {
            Title = FontAwesome6.Terminal + " Console";
            _logMessages = new List<LogMessage>();
            Debug.OnLog += OnLog;
        }

        private void OnLog(string message, LogSeverity logSeverity)
        {
            if (logSeverity == LogSeverity.Normal && !GeneralPreferences.Instance.ShowDebugLogs) return;
            else if (logSeverity == LogSeverity.Warning && !GeneralPreferences.Instance.ShowDebugWarnings) return;
            else if (logSeverity == LogSeverity.Error && !GeneralPreferences.Instance.ShowDebugErrors) return;
            else if (logSeverity == LogSeverity.Success && !GeneralPreferences.Instance.ShowDebugSuccess) return;

            _logMessages.Add(new LogMessage(message, logSeverity));
            if (_logMessages.Count > _maxLogs)
                _logMessages.RemoveAt(0);
            _logCount++;
        }

        protected override void Draw()
        {
            g.CurrentNode.Layout(LayoutType.Column);
            g.CurrentNode.AutoScaleChildren();

            using(g.Node().Width(Size.Percentage(1f)).MaxHeight(20).Enter())
            {
                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.SelectedColor);
            }
            if(_logMessages.Count< 1000)
                _logMessages.Add(new LogMessage("Test", LogSeverity.Normal));
            using (g.Node().Width(Size.Percentage(1f)).Padding(0, 3, 3, 3).Clip().Enter())
            {
                double height = 5;
                for (int i = _logMessages.Count; i-- > 0;)
                {
                    var logSeverity = _logMessages[i].LogSeverity;
                    if (logSeverity == LogSeverity.Normal && !GeneralPreferences.Instance.ShowDebugLogs) continue;
                    else if (logSeverity == LogSeverity.Warning && !GeneralPreferences.Instance.ShowDebugWarnings) continue;
                    else if (logSeverity == LogSeverity.Error && !GeneralPreferences.Instance.ShowDebugErrors) continue;
                    else if (logSeverity == LogSeverity.Success && !GeneralPreferences.Instance.ShowDebugSuccess) continue;

                    int width = (int)g.CurrentNode.LayoutData.InnerRect.width;
                    var pos = g.CurrentNode.LayoutData.InnerRect.Position;
                    var size = UIDrawList.DefaultFont.CalcTextSize(_logMessages[i].Message, width);

                    var rect = new Rect(pos.x, pos.y + height, width, size.y);
                    g.DrawRectFilled(rect, new(0.2f, 0.2f, 0.2f, 1.0f));

                    _logMessages[i].Draw(pos + new Vector2(5, height), width);
                    height += size.y + 5;
                }

                // Dummy node to set the height of the scroll area
                g.Node().Height(height).IgnoreLayout();

                g.ScrollV();
            }
        }

        private record LogMessage(string Message, LogSeverity LogSeverity)
        {
            public readonly string Message = Message;
            public readonly LogSeverity LogSeverity = LogSeverity;

            public void Draw(Vector2 position, double wrapWidth)
            {
                var color = ToColor(LogSeverity);
                Gui.ActiveGUI.DrawText(UIDrawList.DefaultFont, Message, 20, position, color, wrapWidth);
            }

            private static System.Numerics.Vector4 ToColor(LogSeverity logSeverity) => logSeverity switch {
                LogSeverity.Normal => new System.Numerics.Vector4(1, 1, 1, 1),
                LogSeverity.Success => new System.Numerics.Vector4(0, 1, 0, 1),
                LogSeverity.Warning => new System.Numerics.Vector4(1, 1, 0, 1),
                LogSeverity.Error => new System.Numerics.Vector4(1, 0, 0, 1),
                _ => throw new NotImplementedException("log level not implemented")
            };
        }
    }

    public class ProjectsWindow : EditorWindow
    {
        public static bool WindowDrawnThisFrame = false;
        public string SelectedProject = "";
        private string _searchText = "";
        private string createName = "";
        private string[] tabNames = [FontAwesome6.RectangleList + "  Projects", FontAwesome6.PuzzlePiece + "  Create", FontAwesome6.BookOpen + "  Learn", FontAwesome6.DoorOpen + "  Quit"];
        private int currentTab = 0;

        protected override bool Center { get; } = true;
        protected override double Width { get; } = 512 + (512 / 2);
        protected override double Height { get; } = 512;
        protected override bool BackgroundFade { get; } = true;
        protected override bool TitleBar { get; } = false;

        public ProjectsWindow() : base()
        {
            Title = FontAwesome6.Book + " Project Window";
        }

        protected override void Draw()
        {
            if (Project.HasProject)
                isOpened = false;

            g.CurrentNode.Layout(LayoutType.Row);
            g.CurrentNode.AutoScaleChildren();

            using (g.Node().Height(Size.Percentage(1f)).MaxWidth(150).Layout(LayoutType.Column).Enter())
            {
                DrawSidePanel();
            }

            using (g.Node().Height(Size.Percentage(1f)).Enter())
            {
                g.PushID((ulong)currentTab);
                if (currentTab == 0) // Projects tab
                    DrawProjectsTab();
                else if (currentTab == 1) // Create tab
                    CreateProjectTab();
                g.PopID();
            }

        }
        private void DrawProjectsTab()
        {
            var rect = g.CurrentNode.LayoutData.Rect;
            g.DrawRectFilled(rect, GuiStyle.WindowBGColor * 0.25f);
            Vector2 shadowA = new(rect.x, rect.y);
            Vector2 shadowB = new(rect.x, rect.y + (rect.height - 60));
            g.DrawVerticalShadow(shadowA, shadowB, 30, 0.25f);
            Rect footer = new(shadowB.x, shadowB.y, rect.width, 60);
            g.DrawRectFilled(footer, GuiStyle.WindowBGColor);
            Vector2 shadowC = new(rect.x, rect.y + rect.height);
            g.DrawVerticalShadow(shadowB, shadowC, 20, 0.25f);

            g.InputField(ref _searchText, 0x100, Gui.InputFieldFlags.None, 25, 50, 150);

            using (g.Node().Width(565).Height(345).Left(25).Top(80).Layout(LayoutType.Column).Clip().Enter())
            {
                Directory.CreateDirectory(Project.Projects_Directory);
                var folders = new DirectoryInfo(Project.Projects_Directory).EnumerateDirectories();
                folders = folders.OrderByDescending((x) => x.LastWriteTimeUtc);

                foreach (var projectFolder in folders)
                    if (string.IsNullOrEmpty(_searchText) || projectFolder.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                        DisplayProject(projectFolder.Name);

                g.ScrollV();
            }

            var s = new GuiStyle();
            s.WidgetRoundness = 0;
            s.BorderThickness = 0;
            s.BtnHoveredColor = GuiStyle.SelectedColor;
            s.WidgetColor = GuiStyle.HoveredColor;
            if (g.Button("Open", 455, 452, 162, 60, s))
            {
                Project.Open(SelectedProject);
                isOpened = false;
            }
        }

        private void DisplayProject(string name)
        {
            var proj = Project.GetPath(name);

            using (g.Node().Height(48).Width(Size.Percentage(1f, -17)).Margin(5).Enter())
            {
                var rect = g.CurrentNode.LayoutData.Rect;
                g.DrawText(UIDrawList.DefaultFont, name, 20, rect.Position + new Vector2(8, 5), Color.white);
                
                string path = proj.FullName;
                // Cut of the path if it's too long
                if (path.Length > 48)
                    path = string.Concat("...", path.AsSpan(path.Length - 48));
                g.DrawText(UIDrawList.DefaultFont, path, 20, rect.Position + new Vector2(8, 22), Color.white * 0.5f);

                g.DrawText(UIDrawList.DefaultFont, GetFormattedLastModifiedTime(proj.LastWriteTime), 20, rect.Position + new Vector2(rect.width - 125, 14), Color.white * 0.5f);

                var interact = g.GetInteractable();
                if (interact.TakeFocus() || SelectedProject == name)
                {
                    SelectedProject = name;
                    g.DrawRect(g.CurrentNode.LayoutData.Rect, new(0.7f, 0.7f, 0.7f, 1f), 1, 2);
                }
                else if (interact.IsHovered())
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new(0.1f, 0.1f, 0.1f, 0.4f), 2);
                //else if (interact.IsDoubleClicked())
                //{
                //    Project.Open(name);
                //    isOpened = false;
                //}
            }

        }

        private string GetFormattedLastModifiedTime(DateTime lastModified)
        {
            TimeSpan timeSinceLastModified = DateTime.Now - lastModified;

            if (timeSinceLastModified.TotalMinutes < 1)
                return "Just now";
            else if (timeSinceLastModified.TotalMinutes < 60)
                return $"{(int)timeSinceLastModified.TotalMinutes} minutes ago";
            else if (timeSinceLastModified.TotalHours < 24)
                return $"{(int)timeSinceLastModified.TotalHours} hours ago";
            else
                return $"{(int)timeSinceLastModified.TotalDays} days ago";
        }

        private void CreateProjectTab()
        {
            var rect = g.CurrentNode.LayoutData.Rect;
            g.DrawRectFilled(rect, GuiStyle.WindowBGColor * 0.25f);
            Vector2 shadowA = new(rect.x, rect.y);
            Vector2 shadowB = new(rect.x, rect.y + (rect.height - 77));
            g.DrawVerticalShadow(shadowA, shadowB, 30, 0.25f);
            Rect footer = new(shadowB.x, shadowB.y, rect.width, 77);
            g.DrawRectFilled(footer, GuiStyle.WindowBGColor);
            Vector2 shadowC = new(rect.x, rect.y + rect.height);
            g.DrawVerticalShadow(shadowB, shadowC, 20, 0.25f);

            g.InputField(ref createName, 0x100, Gui.InputFieldFlags.None, 30, 450, 340);
            string path = Project.GetPath(createName).FullName;
            if (path.Length > 48)
                path = string.Concat("...", path.AsSpan(path.Length - 48));
            g.DrawText(UIDrawList.DefaultFont, path, 20, rect.Position + new Vector2(30, 480), Color.white * 0.5f);
            
            var s = new GuiStyle();
            s.WidgetRoundness = 0;
            s.BorderThickness = 0;
            s.BtnHoveredColor = GuiStyle.SelectedColor;
            s.WidgetColor = GuiStyle.HoveredColor;
            if (g.Button("Create", 445, 435, 172, 77, s))
            {
                Project.CreateNew(createName);
                currentTab = 0;
            }
        }

        private void DrawSidePanel()
        {
            using (g.Node().Height(40).Width(Size.Percentage(1f)).Enter())
            {
                Rect rect = g.CurrentNode.LayoutData.Rect;
                g.DrawText(UIDrawList.DefaultFont, "Prowl", 40, rect, Color.white);
            }

            for (int i = 0; i < tabNames.Length; i++)
            {
                bool isQuit = i == tabNames.Length - 1;
                using (g.Node().Height(40).Width(Size.Percentage(1f)).Top(Offset.Percentage(isQuit ? 1 : 0, isQuit ? -40 : 0)).Enter())
                {
                    if (isQuit) g.CurrentNode.IgnoreLayout();
                    var interact = g.GetInteractable();
                    if (interact.TakeFocus() || currentTab == i)
                    {
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.SelectedColor);
                        currentTab = i;
                        if (i == 3)
                            Application.Quit();
                    }
                    else if (interact.IsHovered())
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.HoveredColor);

                    Rect rect = g.CurrentNode.LayoutData.Rect;
                    g.DrawText(UIDrawList.DefaultFont, tabNames[i], 20, rect, Color.white);
                }
            }
        }
    }
}