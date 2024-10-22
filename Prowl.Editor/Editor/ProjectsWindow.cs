// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Xml.Linq;

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;

namespace Prowl.Editor;

public class ProjectsWindow : EditorWindow
{
    public Project? SelectedProject;

    private string _searchText = "";

    private string _createName = "";

    private readonly (string, Action)[] _tabs;
    private int _currentTab;
    private bool _createTabOpen;

    private FileDialog _dialog;
    private FileDialogContext _dialogContext;


    protected override bool Center { get; } = true;
    protected override double Width { get; } = 1024;
    protected override double Height { get; } = 640;
    protected override bool BackgroundFade { get; } = true;
    protected override bool TitleBar { get; } = false;
    protected override bool RoundCorners => false;
    protected override bool LockSize => true;
    protected override double Padding => 0;

    public ProjectsWindow()
    {
        Title = FontAwesome6.Book + " Project Window";

        _tabs = [
            (FontAwesome6.RectangleList + "  Projects", DrawProjectsTab),
            (FontAwesome6.BookOpen + "  Learn", () => {})
        ];
    }

    protected override void Draw()
    {
        if (Project.HasProject)
            isOpened = false;

        using (gui.Node("Content").ExpandWidth().Layout(LayoutType.Row).ScaleChildren().Enter())
        {
            using (gui.Node("Side").ExpandHeight().MaxWidth(150).Layout(LayoutType.Column).Spacing(5).Enter())
            {
                using (gui.Node("Name").Scale(150, 50).Enter())
                {
                    Rect rect = gui.CurrentNode.LayoutData.Rect;
                    gui.Draw2D.DrawText(Font.DefaultFont, "Prowl", 40, rect, Color.white);
                }

                DrawSidePanel();
            }

            gui.PushID((ulong)_currentTab);

            using (gui.Node("TabContent").ExpandHeight().Enter())
            {
                _tabs[_currentTab].Item2.Invoke();
            }

            gui.PopID();
        }
    }


    private void DrawProjectsTab()
    {
        Rect rect = gui.CurrentNode.LayoutData.Rect;
        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.WindowRoundness);

        Vector2 shadowA = new(rect.x, rect.y);
        Vector2 shadowB = new(rect.x, rect.y + (rect.height - 60));

        gui.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, 30, 0.25f);

        Rect footer = new(shadowB.x, shadowB.y, rect.width, 60);
        gui.Draw2D.DrawRectFilled(footer, EditorStylePrefs.Instance.WindowBGOne, (float)EditorStylePrefs.Instance.WindowRoundness, 4);

        shadowA = shadowB;
        shadowB = new(rect.x, rect.y + rect.height);
        gui.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, 20, 0.25f);

        shadowA = rect.TopRight;
        shadowB = rect.BottomRight;

        gui.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, 30, 0.25f);

        if (_createTabOpen)
        {
            shadowA = new(rect.x + rect.width - 250, rect.y);
            shadowB = new(rect.x + rect.width - 250, rect.y + (rect.height - 60));

            gui.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, -30, 0.15f);
        }

        using (gui.Node("ProjectsContent").Top(50).ExpandWidth(_createTabOpen ? -250 : 0).ExpandHeight(-60).Layout(LayoutType.Column).Enter())
        {
            using (gui.Node("TopBar").ExpandWidth().MaxHeight(40).Enter())
            {
                gui.Search("SearchInput", ref _searchText, 30, 10, 150, null, EditorGUI.GetInputStyle());

                if (!_createTabOpen)
                {
                    using (gui.Node("Add").Top(7.5).Scale(75, 30).Enter())
                    {
                        gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 87.5));

                        Interactable interact = gui.GetInteractable();

                        Color openCol = Color.white * 0.4f;

                        if (interact.TakeFocus())
                        {
                            openCol = EditorStylePrefs.Instance.Highlighted;
                            OpenDialog("Add Existing Project", (x) => ProjectCache.Instance.AddProject(new Project(new DirectoryInfo(x))));
                        }
                        else if (interact.IsHovered())
                            openCol = EditorStylePrefs.Instance.Hovering;

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, openCol, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.All);

                        gui.Draw2D.DrawText("Add", 20, gui.CurrentNode.LayoutData.Rect);
                    }


                    using (gui.Node("Create").Top(7.5).Scale(75, 30).Enter())
                    {
                        gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 7.5));

                        Interactable interact = gui.GetInteractable();

                        Color createCol = EditorStylePrefs.Instance.Highlighted;

                        if (interact.TakeFocus())
                            _createTabOpen = true;
                        else if (interact.IsHovered())
                            createCol = EditorStylePrefs.Instance.Hovering;

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, createCol, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.All);

                        gui.Draw2D.DrawText("Create", 20, gui.CurrentNode.LayoutData.Rect);
                    }
                }
            }

            using (gui.Node("Projects").ExpandWidth(-7.5).ExpandHeight().Enter())
            {
                using (gui.Node("List").TopLeft(25, 45).ExpandWidth(-7.5).ExpandHeight(-45).Layout(LayoutType.Column).Spacing(5).Clip().Scroll().Enter())
                {
                    for (int i = 0; i < ProjectCache.Instance.ProjectsCount; i++)
                    {
                        Project? project = ProjectCache.Instance.GetProject(i);

                        if (project == null)
                            continue;

                        if (string.IsNullOrEmpty(_searchText) || project.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                            DisplayProject(project);
                    }
                }
            }
        }

        if (_createTabOpen)
        {
            using (gui.Node("Sidebar").Width(250).ExpandHeight(-60).Enter())
            {
                gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x));

                rect = gui.CurrentNode.LayoutData.Rect;

                shadowA = new(rect.x, rect.y + 20);
                shadowB = new(rect.x + rect.width, rect.y + 20);

                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne);
                gui.Draw2D.DrawHorizontalBlackGradient(shadowA, shadowB, -20, 0.25f);

                shadowA = new(rect.x, rect.y + rect.height - 20);
                shadowB = new(rect.x + rect.width, rect.y + rect.height - 20);
                gui.Draw2D.DrawHorizontalBlackGradient(shadowA, shadowB, 20, 0.25f);

                DrawCreateProject();
            }
        }

        using (gui.Node("Footer").TopLeft(Offset.Percentage(1f, -162), Offset.Percentage(1f, -60)).Scale(162, 60).Enter())
        {
            Color col = Color.white * 0.4f;

            bool isSelectable = _createTabOpen ? !string.IsNullOrEmpty(_createName) && Directory.Exists(ProjectCache.Instance.SavedProjectsFolder) && !Path.Exists(_createName) :
                SelectedProject != null;

            string text = _createTabOpen ? "Create" : "Open";

            if (isSelectable)
            {
                if (gui.IsNodePressed())
                {
                    if (_createTabOpen)
                    {
                        Project project = Project.CreateNew(new DirectoryInfo(Path.Join(ProjectCache.Instance.SavedProjectsFolder, _createName)));
                        ProjectCache.Instance.AddProject(project);
                        _createTabOpen = false;
                    }
                    else if (Project.Open(SelectedProject))
                    {
                        isOpened = false;
                    }
                }

                col = gui.IsNodeActive() ? EditorStylePrefs.Instance.Highlighted :
                    gui.IsNodeHovered() ? EditorStylePrefs.Instance.Highlighted * 0.8f : EditorStylePrefs.Instance.Highlighted;
            }

            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, col, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
            gui.Draw2D.DrawText(text, gui.CurrentNode.LayoutData.Rect);
        }
    }


    private void OpenDialog(string title, Action<string> onComplete)
    {
        _dialogContext ??= new();

        _dialogContext.title = "Open Existing Project";
        _dialogContext.parentDirectory = new DirectoryInfo(ProjectCache.Instance.SavedProjectsFolder);
        _dialogContext.OnComplete = onComplete;
        _dialogContext.OnCancel = () => _dialogContext.OnComplete = (x) => { };

        EditorGuiManager.Remove(_dialog);
        _dialog = new FileDialog(_dialogContext);
        EditorGuiManager.FocusWindow(_dialog);
    }


    private void DrawCreateProject()
    {
        gui.CurrentNode.Layout(LayoutType.Column);
        gui.CurrentNode.ScaleChildren();
        gui.CurrentNode.Padding(5);
        gui.CurrentNode.Spacing(15);

        using (gui.Node("TopBar").ExpandWidth().MaxHeight(20).Enter())
        {
            using (gui.Node("Close").Scale(20).Enter())
            {
                gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x));

                Interactable closeInteract = gui.GetInteractable();

                Rect rect = gui.CurrentNode.LayoutData.Rect;

                if (closeInteract.TakeFocus())
                {
                    gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.All);
                    _createTabOpen = false;
                }
                else if (closeInteract.IsHovered())
                    gui.Draw2D.DrawRectFilled(rect, Color.white * 0.4f, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.All);

                Rect xrect = gui.CurrentNode.LayoutData.Rect;
                xrect.x += 1;
                xrect.y += 1;
                gui.Draw2D.DrawText(Font.DefaultFont, FontAwesome6.Xmark, 30, xrect, Color.white);
            }
        }

        using (gui.Node("Banner").ExpandWidth().MaxHeight(200).Enter())
        {
            using (gui.Node("CreateProjectLabel").Expand().Enter())
            {
                Rect rect = gui.CurrentNode.LayoutData.Rect;

                gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.WindowRoundness);

                Vector2 gradLeft = rect.Position;
                gradLeft.y += rect.height * 0.05;
                Vector2 gradRight = rect.Position;
                gradRight.x += rect.width;
                gradRight.y += rect.height * 0.05;

                Color gradBottom = EditorStylePrefs.Instance.Highlighted * 0.6f;
                gradBottom.a = 1.0f;

                gui.Draw2D.DrawHorizontalGradient(gradLeft, gradRight, (float)rect.height * 0.9f, gradBottom, EditorStylePrefs.Instance.Highlighted);

                gui.Draw2D.DrawRectFilled(new(rect.Position.x, rect.Position.y + rect.height * 0.94f, rect.width, rect.height * 0.06f), gradBottom, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.Bottom);

                gui.Draw2D.DrawText(FontAwesome6.PuzzlePiece, 50, rect);
            }
        }

        using (gui.Node("ProjectFolder").ExpandWidth().MaxHeight(40).Enter())
        {
            gui.Draw2D.DrawText("Location", 20, gui.CurrentNode.LayoutData.Rect.Position, color: Color.white * 0.65f);

            Vector2 pos = gui.CurrentNode.LayoutData.Rect.Position;
            pos.y += 20;

            string path = ProjectCache.Instance.SavedProjectsFolder;

            if (path.Length > 32)
                path = string.Concat("...", path.AsSpan(path.Length - 32));

            gui.Draw2D.DrawText(path, pos);

            using (gui.Node("SelectProject").Top(20).Scale(10, 20).Enter())
            {
                gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x));

                Interactable closeInteract = gui.GetInteractable();

                Rect rect = gui.CurrentNode.LayoutData.Rect;

                if (closeInteract.TakeFocus())
                {
                    gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, 5, CornerRounding.All);
                    OpenDialog("Select Folder", (x) => ProjectCache.Instance.SavedProjectsFolder = x);
                }
                else if (closeInteract.IsHovered())
                    gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Hovering, 5, CornerRounding.All);

                gui.Draw2D.DrawText(FontAwesome6.EllipsisVertical, 20, rect);
            }
        }

        using (gui.Node("NameInput").ExpandWidth().MaxHeight(50).Enter())
        {
            gui.Draw2D.DrawText("Project Name", 20, gui.CurrentNode.LayoutData.Rect.Position, color: Color.white * 0.65f);

            gui.InputField("SearchInput", ref _createName, 0x100, Gui.InputFieldFlags.None, 0, 20, gui.CurrentNode.LayoutData.Rect.width, null, EditorGUI.GetInputStyle());
        }

        if (Input.GetKeyDown(Key.Escape))
            _createTabOpen = false;
    }

    Project contextMenuProject = null;
    private void DisplayProject(Project project)
    {
        Rect rootRect = gui.CurrentNode.LayoutData.Rect;
        using (gui.Node(project.Name).Height(40).Width(Size.Percentage(1f, -17)).Layout(LayoutType.Row).Enter())
        {
            Interactable interact = gui.GetInteractable();

            if (interact.TakeFocus() || SelectedProject == project)
            {
                SelectedProject = project;
                gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.Rect, new(0.7f, 0.7f, 0.7f, 1f), 1, 2);
            }

            if (interact.IsHovered())
            {
                if (gui.IsPointerDoubleClick(MouseButton.Left))
                {
                    Project.Open(project);
                    isOpened = false;
                }

                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, new(0.1f, 0.1f, 0.1f, 0.4f), 2);
            }

            if (!project.IsValid())
            {
                using (gui.Node("WarningLabel").Width(40).ExpandHeight().Enter())
                {
                    Interactable warnInteract = gui.GetInteractable();

                    Rect wrect = gui.CurrentNode.LayoutData.Rect;
                    wrect.y += 1;
                    gui.Draw2D.DrawText(Font.DefaultFont, FontAwesome6.TriangleExclamation, 35, wrect, Color.yellow);

                    Vector2 warningPos = gui.CurrentNode.LayoutData.Rect.Position;
                    warningPos.y += gui.CurrentNode.LayoutData.Rect.height / 2;
                    gui.Tooltip("Project is invalid and may not open correctly.", warningPos, align: Gui.TooltipAlign.BottomRight);
                }
            }

            using (gui.Node("ProjectText").ExpandWidth(-35).ExpandHeight().Enter())
            {
                Rect rect = gui.CurrentNode.LayoutData.Rect;

                gui.Draw2D.DrawText(Font.DefaultFont, project.Name, 20, rect.Position + new Vector2(8, 5), Color.white);

                string path = project.ProjectPath;

                // Cut off the path if it's too long
                if (path.Length > 48)
                    path = string.Concat("...", path.AsSpan(path.Length - 48));

                gui.Draw2D.DrawText(Font.DefaultFont, path, 20, rect.Position + new Vector2(8, 22), Color.white * 0.5f);

                if (project.IsValid())
                    gui.Draw2D.DrawText(Font.DefaultFont, GetFormattedLastModifiedTime(project.ProjectDirectory.LastAccessTime), 20, rect.Position + new Vector2(rect.width - 125, 14), Color.white * 0.5f);
            }

            using (gui.Node("ProjectOptionsBtn").Scale(20).Top(10).Enter())
            {
                gui.CurrentNode.IgnoreLayout();
                gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 10));

                Rect rect = gui.CurrentNode.LayoutData.Rect;

                Interactable optionsInteract = gui.GetInteractable();

                bool focused = optionsInteract.TakeFocus();

                if (optionsInteract.TakeFocus())
                {
                    gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.All);
                    contextMenuProject = project;
                    gui.OpenPopup("ProjectOptionsContextMenu", null, gui.CurrentNode.Parent);
                }
                else if (optionsInteract.IsHovered())
                {
                    gui.Draw2D.DrawRectFilled(rect, Color.white * 0.4f, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.All);
                }

                gui.Draw2D.DrawText(Font.DefaultFont, FontAwesome6.Ellipsis, 20, rect, Color.white);
            }

            // Outside ProjectOptionsBtn node as it wouldn't render (or too small in size?)
            if (contextMenuProject != null && contextMenuProject.Equals(project))
            {
                DrawProjectContextMenu(project);
            }
        }
    }

    private void DrawProjectContextMenu(Project project)
    {
        bool closePopup = false;
        if (gui.BeginPopup("ProjectOptionsContextMenu", out LayoutNode? popupHolder) && popupHolder != null)
        {
            using (popupHolder.Width(180).Padding(5).Layout(LayoutType.Column).Spacing(5).FitContentHeight().Enter())
            {
                // Add options
                // - Delete project (with popup confirmation)
                if (EditorGUI.StyledButton("Show In Explorer"))
                {
                    AssetDatabase.OpenPath(project.ProjectDirectory, type: FileOpenType.FileExplorer);
                    closePopup = true;
                }
                if (EditorGUI.StyledButton("Remove from list"))
                {
                    ProjectCache.Instance.RemoveProject(project);
                    closePopup = true;
                }
            }
        }
        if (closePopup)
        {
            contextMenuProject = null;
            gui.CloseAllPopups();
        }
    }

    private void DrawSidePanel()
    {
        for (int i = 0; i < _tabs.Length; i++)
        {
            using (gui.Node(_tabs[i].Item1).Height(40).Width(Size.Percentage(1f)).Top(Offset.Percentage(0, 0)).Enter())
            {
                Interactable interact = gui.GetInteractable();

                if (interact.TakeFocus() || _currentTab == i)
                {
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted);
                    _currentTab = i;
                }
                else if (interact.IsHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, 0);

                Rect rect = gui.CurrentNode.LayoutData.Rect;
                gui.Draw2D.DrawText(Font.DefaultFont, _tabs[i].Item1, 20, rect, Color.white);
            }
        }
    }


    private static string GetFormattedLastModifiedTime(DateTime lastModified)
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
}
