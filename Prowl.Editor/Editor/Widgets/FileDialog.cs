using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Editor
{
    public enum FileDialogType { OpenFile, SaveFile, Count }
    public enum FileDialogSortBy { Name, Date, Size, Type, None }

    public class FileDialogContext
    {
        public string title;
        public FileDialogType type;

        public string fileName;
        public DirectoryInfo directoryPath;
        public string resultPath;

        public List<FileInfo> currentFiles = [];
        public List<DirectoryInfo> currentDirectories = [];

        public Action<string>? OnComplete;
        public Action? OnCancel;

        internal void UpdateCache()
        {
            currentDirectories = directoryPath.GetDirectories().ToList();
            currentFiles = directoryPath.GetFiles().ToList();
            // remove files with .meta extension
            currentFiles = currentFiles.Where(f => f.Extension != ".meta").ToList();
        }
    }

    public class FileDialog : EditorWindow
    {
        public FileDialogContext Dialog;

        private FileDialogSortBy sortBy = FileDialogSortBy.None;
        private FileDialogSortBy sortByPrevious = FileDialogSortBy.None;
        private bool sortDown = false;

        private Stack<DirectoryInfo> _BackStack = new();

        protected override bool Center { get; } = true;
        protected override double Width { get; } = 512 + (512 / 2);
        protected override double Height { get; } = 512;
        protected override bool BackgroundFade { get; } = true;
        protected override bool IsDockable => false;
        protected override bool LockSize => true;
        protected override bool TitleBar => false;
        
        string _path = "";

        public FileDialog(FileDialogContext dialogInfo) : base()
        {
            Dialog = dialogInfo;
            Title = Dialog.title;
            _path = Dialog.directoryPath.FullName;

            Dialog.UpdateCache();
        }

        public static void Open(FileDialogContext dialogInfo)
        {
            new FileDialog(dialogInfo);
        }

        protected override void Draw()
        {
            bool complete = false;

            using (gui.Node("Root").Expand().Layout(LayoutType.Row).ScaleChildren().Padding(10).Enter())
            {

                using (gui.Node("Sidebar").Layout(LayoutType.Column).ExpandHeight().MaxWidth(125).MarginRight(10).Padding(10).Enter())
                {
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 10);

                    ShortcutOption("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                    ShortcutOption("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                    ShortcutOption("Downloads", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads");
                    ShortcutOption("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                    ShortcutOption("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
                    ShortcutOption("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
                }

                using (gui.Node("Window").ExpandHeight().Enter())
                {
                    using (gui.Node("Header").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                    {
                        double left = 0;
                        using (gui.Node("Title").ExpandHeight().Enter())
                        {
                            var pos = gui.CurrentNode.LayoutData.Rect.MiddleLeft;
                            pos.y -= 8;
                            gui.Draw2D.DrawText(Dialog.title, 20, pos, GuiStyle.Base10);
                            left += UIDrawList.DefaultFont.CalcTextSize(Dialog.title, 0).x;
                        }

                        left += 5;

                        using (gui.Node("BackBtn").ExpandHeight().Left(left).Width(GuiStyle.ItemHeight).Enter())
                        {
                            var hovered = gui.IsNodeHovered();
                            if(_BackStack.Count > 0)
                                hovered = !hovered;
                            gui.Draw2D.DrawText(FontAwesome6.ChevronLeft, 20, gui.CurrentNode.LayoutData.InnerRect,
                                hovered ? GuiStyle.Base11 : GuiStyle.Base4);

                            if (gui.IsNodePressed() && _BackStack.Count > 0)
                            {
                                Dialog.directoryPath = _BackStack.Pop();
                                Dialog.UpdateCache();
                            }
                            left += GuiStyle.ItemHeight;
                        }

                        using (gui.Node("UpBtn").ExpandHeight().Left(left).Width(GuiStyle.ItemHeight).Enter())
                        {
                            var hovered = gui.IsNodeHovered();
                            gui.Draw2D.DrawText(FontAwesome6.ChevronUp, 20, gui.CurrentNode.LayoutData.InnerRect,
                                hovered ? GuiStyle.Base4 : GuiStyle.Base11);
                            if (gui.IsNodePressed())
                            {
                                _BackStack.Push(Dialog.directoryPath);
                                Dialog.directoryPath = Dialog.directoryPath.Parent;
                                Dialog.UpdateCache();
                            }
                            left += GuiStyle.ItemHeight;
                        }

                        left += 5;

                        double pathWidth = gui.CurrentNode.LayoutData.Rect.width - left + 10;
                        using (gui.Node("Path").ExpandHeight().Left(left).Width(pathWidth).Enter())
                        {
                            var style = new GuiStyle();

                            style.WidgetColor = GuiStyle.WindowBackground;
                            style.Border = GuiStyle.Borders;
                            style.WidgetRoundness = 8f;
                            style.BorderThickness = 1f;
                            if (gui.InputField("Path", ref _path, 0x100, Gui.InputFieldFlags.EnterReturnsTrue, 0, 0, pathWidth, null, style))
                            {
                                Dialog.directoryPath = new DirectoryInfo(_path);
                            }
                        }

                        if (complete)
                        {
                            isOpened = false;
                        }
                    }

                    if (Dialog.directoryPath.Exists)
                    {
                        using (gui.Node("Content").ExpandWidth().Layout(LayoutType.Row).Height(Size.Percentage(1f, -GuiStyle.ItemHeight * 2)).Top(GuiStyle.ItemHeight).PaddingTop(10).Clip().Scroll().Enter())
                        {
                            // name = 50%, size = 20%, date = 20%, type = 10%

                            using (gui.Node("NameCol").FitContentHeight().Layout(LayoutType.Column).Enter())
                            {
                                gui.CurrentNode.Width(Size.Percentage(0.5f));
                                using (gui.Node("sortName").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                                {
                                    PressedSort(FileDialogSortBy.Name);
                                    DrawSortLabel("Name", FileDialogSortBy.Name);
                                }

                                DrawEntries(false, f => 
                                {
                                    if (f is DirectoryInfo)
                                        return FontAwesome6.Folder + f.Name;
                                    return FontAwesome6.File + f.Name;
                                });
                            }

                            using (gui.Node("SizeCol").FitContentHeight().Layout(LayoutType.Column).Enter())
                            {
                                gui.CurrentNode.Width(Size.Percentage(0.2f));
                                using (gui.Node("sortSize").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                                {
                                    PressedSort(FileDialogSortBy.Size);
                                    DrawSortLabel("Size", FileDialogSortBy.Size);
                                }

                                DrawEntries(true, f => (f is FileInfo file) ? toMemSizeReadable(file.Length) : "");
                            }

                            using (gui.Node("DateCol").FitContentHeight().Layout(LayoutType.Column).Enter())
                            {
                                gui.CurrentNode.Width(Size.Percentage(0.2f));
                                using (gui.Node("sortDate").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                                {
                                    PressedSort(FileDialogSortBy.Date);
                                    DrawSortLabel("Date", FileDialogSortBy.Date);
                                }


                                DrawEntries(true, f => f.LastWriteTime.ToString("dd/MM/yy HH:mm"));
                            }

                            using (gui.Node("TypeCol").FitContentHeight().Layout(LayoutType.Column).Enter())
                            {
                                gui.CurrentNode.Width(Size.Percentage(0.1f));
                                using (gui.Node("typeDate").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                                {
                                    PressedSort(FileDialogSortBy.Type);
                                    DrawSortLabel("Type", FileDialogSortBy.Type);
                                }


                                DrawEntries(true, f => f.Extension);
                            }
                        }

                        using (gui.Node("Footer").ExpandWidth().Height(GuiStyle.ItemHeight).Top(Offset.Percentage(1f, -GuiStyle.ItemHeight)).Enter())
                        {
                            using (gui.Node("FileName").ExpandHeight().Width(Size.Percentage(1f, -100)).Enter())
                            {
                                gui.Search("FileName", ref Dialog.fileName, 0, 0, Size.Percentage(1f), GuiStyle.ItemHeight);
                            }

                            using (gui.Node("SaveBtn").ExpandHeight().Left(Offset.Percentage(1f, -90)).Width(100).Enter())
                            {
                                var hovered = gui.IsNodeHovered();
                                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect,
                                    hovered ? GuiStyle.Base11 : GuiStyle.Base4, 10);
                                gui.Draw2D.DrawText("Save", 20, gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Base10);
                                if (gui.IsNodePressed())
                                {
                                    var path = Path.Combine(Dialog.directoryPath.FullName + "/" + Dialog.fileName);
                                    Dialog.resultPath = path;
                                    Dialog.OnComplete?.Invoke(path);
                                    isOpened = false;
                                }
                            }
                        }

                    }
                }
            }

            // Clicked outside Window
            if (gui.IsPointerClick(Silk.NET.Input.MouseButton.Left) && !gui.IsPointerHovering())
            {
                Debug.Log("clicked outside of window");
                isOpened = false;
                Dialog.OnCancel?.Invoke();
            }
        }

        private void ShortcutOption(string name, string path)
        {
            using (gui.Node("SideBar_" + name).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                Color bg = gui.IsNodeHovered() ? GuiStyle.Base8 : GuiStyle.Base11;
                gui.Draw2D.DrawText(name, gui.CurrentNode.LayoutData.InnerRect, bg);

                if(gui.IsNodePressed())
                {
                    _BackStack.Push(Dialog.directoryPath);
                    Dialog.directoryPath = new DirectoryInfo(path);
                    Dialog.UpdateCache();
                }
            }
        }

        private void PressedSort(FileDialogSortBy sortMode)
        {
            if (gui.IsNodePressed())
            {
                if(sortBy != sortMode)
                    sortBy = sortMode;
                else
                    sortDown = !sortDown;
                Sort();
            }
        }

        private void DrawSortLabel(string text, FileDialogSortBy sortMode)
        {
            if(sortBy == sortMode)
            {
                gui.Draw2D.DrawText(text + " " + (sortDown ? FontAwesome6.ChevronDown : FontAwesome6.ChevronUp), gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base4);
            }
            else
            {
                gui.Draw2D.DrawText(text, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base4);
            }
        }

        private static int hoveringIndex = -1;
        private static string hoveringPath;
        private static string selectedPath;

        private void DrawEntries(bool center, Func<FileSystemInfo, string> name)
        {
            int index = 0;
            foreach (var directory in Dialog.currentDirectories)
            {
                using (gui.Node("name", index++).ExpandWidth().Height(GuiStyle.ItemHeight).Clip().Enter())
                {
                    if (gui.IsNodeHovered())
                    {
                        hoveringPath = directory.FullName;

                        if (gui.IsPointerClick())
                            Dialog.resultPath = directory.FullName;

                        if (gui.IsPointerDoubleClick())
                        {
                            _BackStack.Push(Dialog.directoryPath);
                            Dialog.directoryPath = directory;

                            Dialog.UpdateCache();
                        }
                    }

                    if(Dialog.resultPath == directory.FullName)
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                    else if(hoveringPath == directory.FullName)
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.6f);

                    bool hovered = gui.IsNodeHovered() || hoveringPath == directory.FullName || Dialog.resultPath == directory.FullName;

                    if (!center)
                    {
                        var rect = gui.CurrentNode.LayoutData.Rect.MiddleLeft;
                        rect.y -= 8;
                        gui.Draw2D.DrawText(name.Invoke(directory), rect, hovered ? GuiStyle.Base11 : GuiStyle.Base8);
                    }
                    else
                    {
                        gui.Draw2D.DrawText(name.Invoke(directory), gui.CurrentNode.LayoutData.Rect, hovered ? GuiStyle.Base11 : GuiStyle.Base8);
                    }
                }
            }
            foreach (var file in Dialog.currentFiles)
            {
                using (gui.Node("name", index++).ExpandWidth().Height(GuiStyle.ItemHeight).Clip().Enter())
                {
                    if (gui.IsNodeHovered())
                        hoveringPath = file.FullName;

                    if (gui.IsNodePressed())
                        Dialog.resultPath = file.FullName;

                    if (Dialog.resultPath == file.FullName)
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                    else if (hoveringPath == file.FullName)
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.6f);

                    bool hovered = gui.IsNodeHovered() || hoveringPath == file.FullName || Dialog.resultPath == file.FullName;

                    if (!center)
                    {
                        var rect = gui.CurrentNode.LayoutData.Rect.MiddleLeft;
                        rect.y -= 8;
                        gui.Draw2D.DrawText(name.Invoke(file), rect, hovered ? GuiStyle.Base11 : GuiStyle.Base8);
                    }
                    else
                    {
                        gui.Draw2D.DrawText(name.Invoke(file), gui.CurrentNode.LayoutData.Rect, hovered ? GuiStyle.Base11 : GuiStyle.Base8);
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
            if (sortBy == FileDialogSortBy.Name)
                Dialog.currentDirectories = [.. Dialog.currentDirectories.OrderBy(i => i.Name)];
            else if (sortBy == FileDialogSortBy.Date)
                Dialog.currentDirectories.Sort((a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));

            // Sort files
            if (sortBy == FileDialogSortBy.Name)
                Dialog.currentFiles = [.. Dialog.currentFiles.OrderBy(i => i.Name)];
            else if (sortBy == FileDialogSortBy.Date)
                Dialog.currentFiles.Sort((a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));
            else if (sortBy == FileDialogSortBy.Size)
                Dialog.currentFiles.Sort((a, b) => a.Length.CompareTo(b.Length));
            else if (sortBy == FileDialogSortBy.Type)
                Dialog.currentFiles = [.. Dialog.currentFiles.OrderBy(i => i.Extension)];

            if (!sortDown)
            {
                Dialog.currentDirectories.Reverse();
                Dialog.currentFiles.Reverse();
            }
        }
    }
}