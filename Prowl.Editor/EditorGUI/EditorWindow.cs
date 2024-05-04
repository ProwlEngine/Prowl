using Hexa.NET.ImGui;
using Prowl.Editor.EditorWindows;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Editor
{
    public class EditorWindow
    {
        protected string Title = "Title";
        private readonly int _id;

        protected virtual bool Center { get; } = false;
        protected virtual int Width { get; } = 256;
        protected virtual int Height { get; } = 256;
        protected virtual bool LockSize { get; } = false;
        protected virtual bool BackgroundFade { get; } = false;
        protected virtual bool ForceOntop { get; } = false;

        protected bool isOpened = true;
        protected Runtime.GUI.Gui g => Runtime.GUI.Gui.ActiveGUI;

        public EditorWindow() : base()
        {
            Program.OnDrawEditor += DrawWindow;
            Program.OnUpdateEditor += UpdateWindow;
            _id = GetHashCode();

            var t = this.GetType();
        }

        private void DrawWindow()
        {
            try
            {
                isOpened = true;

                var oldZIndex = g.CurrentZIndex;
                //if (ForceOntop)
                //    g.SetZIndex(1000);

                if (BackgroundFade)
                    g.DrawRectFilled(g.ScreenRect, new System.Numerics.Vector4(0, 0, 0, 0.5f));


                Vector2 topleft = new Vector2(0, 0);
                if (Center)
                {
                    var vp_size = g.ScreenRect.Size / 2;
                    topleft = new Vector2(vp_size.x - (Width / 2), vp_size.y - (Height / 2));
                }

                using (g.Node().Width(Width).Height(Height).Left(topleft.x).Top(topleft.y).Enter())
                {
                    // TODO: Resize
                    // TODO: Drag
                    // TODO: Titlebar & Close button
                    // TODO: Tabs
                    // TODO: Docking
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBGColor);

                    //g.SetZIndex(1);
                    Draw();
                }


                if (!isOpened)
                {
                    Program.OnDrawEditor -= DrawWindow;
                    Program.OnUpdateEditor -= UpdateWindow;
                    Close();
                }

                //if (ForceOntop)
                //    g.SetZIndex(oldZIndex);
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError("Error in EditorWindow: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private void UpdateWindow()
        {
            try
            {
                Update();
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError("Error in UpdateWindow: " + e.Message + "\n" + e.StackTrace);
            }
        }

        protected virtual void Draw() { }
        protected virtual void Update() { }
        protected virtual void Close() { }

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
        protected override bool ForceOntop { get; } = true;
        protected override int Width { get; } = 512 + (512 / 2);
        protected override int Height { get; } = 512;
        protected override bool BackgroundFade { get; } = true;

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

            Gui.InputField(ref _searchText, 0x100, Gui.InputFieldFlags.None, 25, 50, 150);

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
            if (Gui.Button("Open", 455, 452, 162, 60, s))
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

            Gui.InputField(ref createName, 0x100, Gui.InputFieldFlags.None, 30, 450, 340);
            string path = Project.GetPath(createName).FullName;
            if (path.Length > 48)
                path = string.Concat("...", path.AsSpan(path.Length - 48));
            g.DrawText(UIDrawList.DefaultFont, path, 20, rect.Position + new Vector2(30, 480), Color.white * 0.5f);
            
            var s = new GuiStyle();
            s.WidgetRoundness = 0;
            s.BorderThickness = 0;
            s.BtnHoveredColor = GuiStyle.SelectedColor;
            s.WidgetColor = GuiStyle.HoveredColor;
            if (Gui.Button("Create", 445, 435, 172, 77, s))
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