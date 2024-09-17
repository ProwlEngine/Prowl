// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor;

public enum FileDialogType
{
    OpenFolder,
    OpenFile,
    SaveFile,
    Count
}


public enum FileDialogSortBy
{
    Name,
    Date,
    Size,
    Type,
    None
}


public class FileDialogContext
{
    public string title;
    public FileDialogType type;

    public DirectoryInfo parentDirectory;
    public string resultName;

    public string ResultPath => Path.Combine(parentDirectory.FullName, resultName);

    public List<FileInfo> currentFiles = [];
    public List<DirectoryInfo> currentDirectories = [];

    public Action<string>? OnComplete;
    public Action? OnCancel;


    internal void UpdateCache()
    {
        currentDirectories = [.. parentDirectory.EnumerateDirectories()];
        currentFiles = [.. parentDirectory.EnumerateFiles()];

        // remove files with .meta extension
        currentFiles = currentFiles.Where(f => f.Extension != ".meta").ToList();
    }
}


public class FileDialog : EditorWindow
{
    public readonly FileDialogContext Dialog;

    private FileDialogSortBy _sortBy = FileDialogSortBy.None;

    private bool _sortDown;
    private readonly Stack<DirectoryInfo> _backStack = new();

    protected override bool Center { get; } = true;
    protected override double Width { get; } = 512 + (512 / 2);
    protected override double Height { get; } = 512;
    protected override bool BackgroundFade { get; } = true;
    protected override bool IsDockable => false;
    protected override bool LockSize => true;
    protected override bool TitleBar => false;

    private string _path = "";
    private bool _pastFirstFrame;


    public FileDialog(FileDialogContext dialogInfo) : base()
    {
        Dialog = dialogInfo;
        Title = Dialog.title;
        _path = Dialog.parentDirectory.FullName;

        Dialog.UpdateCache();
    }


    public static void Open(FileDialogContext dialogInfo)
    {
        new FileDialog(dialogInfo);
    }


    protected override void Draw()
    {
        bool complete = false;

        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        using (gui.Node("Root").Expand().Layout(LayoutType.Row).ScaleChildren().Padding(10).Enter())
        {
            using (gui.Node("Sidebar").Layout(LayoutType.Column).ExpandHeight().MaxWidth(125).MarginRight(10).Padding(10).Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, (float)EditorStylePrefs.Instance.WindowRoundness);

                string userProf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                ShortcutOption("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop, Environment.SpecialFolderOption.Create));
                ShortcutOption("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.Create));
                ShortcutOption("Downloads", Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                ShortcutOption("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures, Environment.SpecialFolderOption.Create));
                ShortcutOption("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic, Environment.SpecialFolderOption.Create));
                ShortcutOption("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos, Environment.SpecialFolderOption.Create));
            }

            using (gui.Node("Window").ExpandHeight().Enter())
            {
                using (gui.Node("Header").ExpandWidth().Height(ItemSize).Enter())
                {
                    double left = 0;
                    using (gui.Node("Title").ExpandHeight().Enter())
                    {
                        var pos = gui.CurrentNode.LayoutData.Rect.MiddleLeft;
                        pos.y -= 8;
                        gui.Draw2D.DrawText(Dialog.title, 20, pos, EditorStylePrefs.Instance.LesserText);
                        left += Font.DefaultFont.CalcTextSize(Dialog.title, 0).x;
                    }

                    left += 5;

                    using (gui.Node("BackBtn").ExpandHeight().Left(left).Width(ItemSize).Enter())
                    {
                        bool hovered = gui.IsNodeHovered();

                        if (_backStack.Count > 0)
                            hovered = !hovered;

                        gui.Draw2D.DrawText(FontAwesome6.ChevronLeft, 20, gui.CurrentNode.LayoutData.InnerRect,
                            hovered ? EditorStylePrefs.Instance.Hovering : EditorStylePrefs.Instance.LesserText);

                        if (gui.IsNodePressed() && _backStack.Count > 0)
                        {
                            Dialog.parentDirectory = _backStack.Pop();
                            Dialog.UpdateCache();
                        }

                        left += ItemSize;
                    }

                    using (gui.Node("UpBtn").ExpandHeight().Left(left).Width(ItemSize).Enter())
                    {
                        bool hovered = gui.IsNodeHovered();

                        gui.Draw2D.DrawText(FontAwesome6.ChevronUp, 20, gui.CurrentNode.LayoutData.InnerRect,
                            hovered ? EditorStylePrefs.Instance.Hovering : EditorStylePrefs.Instance.LesserText);

                        if (gui.IsNodePressed())
                        {
                            _backStack.Push(Dialog.parentDirectory);
                            Dialog.parentDirectory = Dialog.parentDirectory.Parent ?? Dialog.parentDirectory;
                            Dialog.UpdateCache();
                        }

                        left += ItemSize;
                    }

                    left += 5;

                    double pathWidth = gui.CurrentNode.LayoutData.Rect.width - left + 10;
                    using (gui.Node("Path").ExpandHeight().Left(left).Width(pathWidth).Enter())
                    {
                        Gui.WidgetStyle style = EditorGUI.GetInputStyle();

                        style.Roundness = 8f;
                        style.BorderThickness = 1f;

                        if (gui.InputField("Path", ref _path, 0x100, Gui.InputFieldFlags.EnterReturnsTrue, 0, 0, pathWidth, null, style))
                        {
                            Dialog.parentDirectory = new DirectoryInfo(_path);
                        }
                    }

                    if (complete)
                    {
                        isOpened = false;
                    }
                }

                if (Dialog.parentDirectory.Exists)
                {
                    using (gui.Node("Content").ExpandWidth().Layout(LayoutType.Row).Height(Size.Percentage(1f, -ItemSize * 2)).Top(ItemSize).PaddingTop(10).Clip().Scroll().Enter())
                    {
                        // name = 50%, size = 20%, date = 20%, type = 10%

                        using (gui.Node("NameCol").FitContentHeight().Layout(LayoutType.Column).Enter())
                        {
                            gui.CurrentNode.Width(Size.Percentage(0.5f));
                            using (gui.Node("sortName").ExpandWidth().Height(ItemSize).Enter())
                            {
                                PressedSort(FileDialogSortBy.Name);
                                DrawSortLabel("Name", FileDialogSortBy.Name);
                            }

                            DrawEntries(false, Dialog.type == FileDialogType.OpenFolder, f =>
                            {
                                if (f is DirectoryInfo)
                                    return FontAwesome6.Folder + " " + f.Name;

                                return FontAwesome6.File + " " + f.Name;
                            });
                        }

                        using (gui.Node("SizeCol").FitContentHeight().Layout(LayoutType.Column).Enter())
                        {
                            gui.CurrentNode.Width(Size.Percentage(0.2f));
                            using (gui.Node("sortSize").ExpandWidth().Height(ItemSize).Enter())
                            {
                                PressedSort(FileDialogSortBy.Size);
                                DrawSortLabel("Size", FileDialogSortBy.Size);
                            }

                            DrawEntries(true, Dialog.type == FileDialogType.OpenFolder, f => (f is FileInfo file) ? toMemSizeReadable(file.Length) : "");
                        }

                        using (gui.Node("DateCol").FitContentHeight().Layout(LayoutType.Column).Enter())
                        {
                            gui.CurrentNode.Width(Size.Percentage(0.2f));
                            using (gui.Node("sortDate").ExpandWidth().Height(ItemSize).Enter())
                            {
                                PressedSort(FileDialogSortBy.Date);
                                DrawSortLabel("Date", FileDialogSortBy.Date);
                            }


                            DrawEntries(true, Dialog.type == FileDialogType.OpenFolder, f => f.LastWriteTime.ToString("dd/MM/yy HH:mm"));
                        }

                        using (gui.Node("TypeCol").FitContentHeight().Layout(LayoutType.Column).Enter())
                        {
                            gui.CurrentNode.Width(Size.Percentage(0.1f));
                            using (gui.Node("typeDate").ExpandWidth().Height(ItemSize).Enter())
                            {
                                PressedSort(FileDialogSortBy.Type);
                                DrawSortLabel("Type", FileDialogSortBy.Type);
                            }


                            DrawEntries(true, Dialog.type == FileDialogType.OpenFolder, f => f.Extension);
                        }
                    }

                    using (gui.Node("Footer").ExpandWidth().Height(ItemSize).Top(Offset.Percentage(1f, -ItemSize)).Enter())
                    {
                        string name = Dialog.type == FileDialogType.OpenFolder ? "Folder" : "Filename";
                        string prompt = Dialog.type == FileDialogType.OpenFolder || Dialog.type == FileDialogType.OpenFile ? "Open" : "Save";

                        using (gui.Node("SearchName").ExpandHeight().Width(Size.Percentage(1f, -100)).Enter())
                        {
                            gui.Search(name, ref Dialog.resultName, 0, 0, Size.Percentage(1f), ItemSize);
                        }

                        using (gui.Node("PromptBtn").ExpandHeight().Left(Offset.Percentage(1f, -90)).Width(100).Enter())
                        {
                            bool hovered = gui.IsNodeHovered();

                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect,
                                hovered ? EditorStylePrefs.Instance.Hovering : EditorStylePrefs.Instance.LesserText, (float)EditorStylePrefs.Instance.ButtonRoundness);
                            gui.Draw2D.DrawText(prompt, 20, gui.CurrentNode.LayoutData.InnerRect, Color.white);

                            if (gui.IsNodePressed())
                            {
                                Dialog.OnComplete?.Invoke(Dialog.ResultPath);

                                isOpened = false;
                            }
                        }
                    }

                }
            }
        }

        // Clicked outside Window
        if (_pastFirstFrame && gui.IsPointerClick(MouseButton.Left) && !gui.IsPointerHovering())
        {
            isOpened = false;
            Dialog.OnCancel?.Invoke();
        }

        _pastFirstFrame = true;
    }

    private void ShortcutOption(string name, string path)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        using (gui.Node("SideBar_" + name).ExpandWidth().Height(ItemSize).Enter())
        {
            Color bg = gui.IsNodeHovered() ? EditorStylePrefs.Instance.Hovering : Color.white;
            gui.Draw2D.DrawText(name, gui.CurrentNode.LayoutData.InnerRect, bg);

            if (gui.IsNodePressed())
            {
                _backStack.Push(Dialog.parentDirectory);
                Dialog.parentDirectory = new DirectoryInfo(path);
                Dialog.UpdateCache();
            }
        }
    }

    private void PressedSort(FileDialogSortBy sortMode)
    {
        if (gui.IsNodePressed())
        {
            if (_sortBy != sortMode)
                _sortBy = sortMode;
            else
                _sortDown = !_sortDown;
            Sort();
        }
    }

    private void DrawSortLabel(string text, FileDialogSortBy sortMode)
    {
        if (_sortBy == sortMode)
        {
            gui.Draw2D.DrawText(text + " " + (_sortDown ? FontAwesome6.ChevronDown : FontAwesome6.ChevronUp), gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? EditorStylePrefs.Instance.Hovering : EditorStylePrefs.Instance.LesserText);
        }
        else
        {
            gui.Draw2D.DrawText(text, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? EditorStylePrefs.Instance.Hovering : EditorStylePrefs.Instance.LesserText);
        }
    }


    private static string hoveringPath;

    private void DrawEntries(bool center, bool ignoreFiles, Func<FileSystemInfo, string> name)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        int index = 0;

        foreach (DirectoryInfo directory in Dialog.currentDirectories)
        {
            using (gui.Node("name", index++).ExpandWidth().Height(ItemSize).Clip().Enter())
            {
                if (gui.IsNodeHovered())
                {
                    hoveringPath = directory.FullName;

                    if (gui.IsPointerClick())
                    {
                        Dialog.parentDirectory = directory.Parent ?? directory;
                        Dialog.resultName = directory.Name;
                        _path = Dialog.ResultPath;
                    }

                    if (gui.IsPointerDoubleClick())
                    {
                        _backStack.Push(Dialog.parentDirectory);
                        Dialog.parentDirectory = directory;
                        Dialog.resultName = "";
                        _path = Dialog.ResultPath;

                        Dialog.UpdateCache();
                    }
                }

                if (Dialog.resultName == directory.FullName)
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted);
                else if (hoveringPath == directory.FullName)
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering * 0.6f);

                bool hovered = gui.IsNodeHovered() || hoveringPath == directory.FullName || Dialog.resultName == directory.FullName;

                if (!center)
                {
                    Vector2 rect = gui.CurrentNode.LayoutData.Rect.MiddleLeft;
                    rect.y -= 8;
                    gui.Draw2D.DrawText(name.Invoke(directory), rect, hovered ? EditorStylePrefs.Instance.Hovering : Color.white);
                }
                else
                {
                    gui.Draw2D.DrawText(name.Invoke(directory), gui.CurrentNode.LayoutData.Rect, hovered ? EditorStylePrefs.Instance.Hovering : Color.white);
                }
            }
        }

        if (ignoreFiles)
            return;

        foreach (var file in Dialog.currentFiles)
        {
            using (gui.Node("name", index++).ExpandWidth().Height(ItemSize).Clip().Enter())
            {
                if (gui.IsNodeHovered())
                    hoveringPath = file.FullName;

                if (gui.IsNodePressed())
                {
                    Dialog.resultName = file.FullName;
                    _path = Dialog.ResultPath;
                }

                if (Dialog.resultName == file.FullName)
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted);
                else if (hoveringPath == file.FullName)
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering * 0.6f);

                bool hovered = gui.IsNodeHovered() || hoveringPath == file.FullName || Dialog.resultName == file.FullName;

                if (!center)
                {
                    Vector2 rect = gui.CurrentNode.LayoutData.Rect.MiddleLeft;
                    rect.y -= 8;
                    gui.Draw2D.DrawText(name.Invoke(file), rect, hovered ? EditorStylePrefs.Instance.Hovering : Color.white);
                }
                else
                {
                    gui.Draw2D.DrawText(name.Invoke(file), gui.CurrentNode.LayoutData.Rect, hovered ? EditorStylePrefs.Instance.Hovering : Color.white);
                }
            }
        }
    }

    private string toMemSizeReadable(long length)
    {
        if (length < 1000)
        {
            // bytes
            return length + "bytes";
        }
        // if smaller than megabyte => kilobyte
        if (length < 1000000)
        {
            // kilobyte
            return length / 1000f + "kb";
        }
        if (length < 1000000000)
        {
            // megabyte
            return length / 1000000f + "mb";
        }

        // gigabyte
        return length / 1000000000f + "gb";
    }

    private void Sort()
    {
        // Sort directories
        if (_sortBy == FileDialogSortBy.Name)
            Dialog.currentDirectories = [.. Dialog.currentDirectories.OrderBy(i => i.Name)];
        else if (_sortBy == FileDialogSortBy.Date)
            Dialog.currentDirectories.Sort((a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));

        // Sort files
        if (_sortBy == FileDialogSortBy.Name)
            Dialog.currentFiles = [.. Dialog.currentFiles.OrderBy(i => i.Name)];
        else if (_sortBy == FileDialogSortBy.Date)
            Dialog.currentFiles.Sort((a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));
        else if (_sortBy == FileDialogSortBy.Size)
            Dialog.currentFiles.Sort((a, b) => a.Length.CompareTo(b.Length));
        else if (_sortBy == FileDialogSortBy.Type)
            Dialog.currentFiles = [.. Dialog.currentFiles.OrderBy(i => i.Extension)];

        if (!_sortDown)
        {
            Dialog.currentDirectories.Reverse();
            Dialog.currentFiles.Reverse();
        }
    }
}
