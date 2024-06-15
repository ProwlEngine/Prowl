using Prowl.Runtime.GUI;
using Prowl.Runtime;
using Prowl.Icons;

namespace Prowl.Editor
{

    // 1. rename the configs to be easier to understand
    // 2. Multi-Select, always return a List<FileSystemInfo> instead of a single file path

    public enum FileDialogType { OpenFile, SaveFile, Count }
    public enum FileDialogSortBy { Name, Date, Size, Type, None }

    public class FileDialogContext
    {
        public string title;
        public FileDialogType type;

        public string fileName;
        public DirectoryInfo directoryPath;
        public string resultPath;

        public bool refreshInfo;
        public ulong currentIndex;
        public List<FileInfo> currentFiles = [];
        public List<DirectoryInfo> currentDirectories = [];

        public Action<string>? OnComplete;
        public Action? OnCancel;
    }

    public class FileDialog : EditorWindow
    {
        public FileDialogContext Dialog;

        private FileDialogSortBy sortBy = FileDialogSortBy.None;
        private FileDialogSortBy sortByPrevious = FileDialogSortBy.None;
        private bool sortDown = false;

        private Stack<DirectoryInfo> _BackStack = new();
        private Stack<DirectoryInfo> _ForwardStack = new();

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
        }

        public static void Open(FileDialogContext dialogInfo)
        {
            new FileDialog(dialogInfo);
        }

        protected override void Draw()
        {
            bool complete = false;

            gui.CurrentNode.Layout(LayoutType.Row);
            gui.CurrentNode.ScaleChildren();

            using (gui.Node("Sidebar").Layout(LayoutType.Column).ExpandHeight().MaxWidth(200).Margin(10).Padding(10).Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 10);
                for (int i = 0; i < 10; i++)
                {
                    using (gui.Node("Content" + i).ExpandWidth().Height(GuiStyle.ItemHeight).ScaleChildren()
                               .Layout(LayoutType.Row).Enter())
                    {
                        var hovered = gui.IsNodeHovered();
                        Color bg = hovered ? GuiStyle.Base11 : GuiStyle.Base4;
                        gui.Draw2D.DrawText("Epic Feele.txt.json", 20, gui.CurrentNode.LayoutData.InnerRect, bg);
                    }
                }
            }

            using (gui.Node("Window").Layout(LayoutType.Column).ExpandHeight().ScaleChildren().Enter())
            {
                using (gui.Node("Header").ExpandWidth().MaxHeight(GuiStyle.ItemHeight + 20).Layout(LayoutType.Row)
                           .ScaleChildren().Padding(10).Enter())
                {
                    using (gui.Node("Title").ExpandHeight().Enter())
                    {
                        gui.Draw2D.DrawText(Dialog.title, 20, gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Base10);
                    }

                    using (gui.Node("BackBtn").ExpandHeight().MaxWidth(20).Enter())
                    {
                        var hovered = gui.IsNodeHovered() || _BackStack.Count > 0;
                        gui.Draw2D.DrawText(FontAwesome6.ChevronLeft, 20, gui.CurrentNode.LayoutData.InnerRect,
                            hovered ? GuiStyle.Base11 : GuiStyle.Base4);

                        if (gui.IsNodePressed() && _BackStack.Count > 0)
                        {
                            Debug.Log("popping top item");
                            Dialog.directoryPath = _BackStack.Pop();
                        }
                    }

                    using (gui.Node("ForwardBtn").ExpandHeight().MaxWidth(20).Enter())
                    {
                        var hovered = gui.IsNodeHovered();
                        gui.Draw2D.DrawText(FontAwesome6.ChevronRight, 20, gui.CurrentNode.LayoutData.InnerRect,
                            hovered ? GuiStyle.Base11 : GuiStyle.Base4);
                    }

                    using (gui.Node("Path").ExpandHeight().Enter())
                    {
                        var size = gui.CurrentNode.LayoutData.Rect.width;
                        var style = new GuiStyle();
                        var g = Runtime.GUI.Gui.ActiveGUI;

                        style.WidgetColor = GuiStyle.WindowBackground;
                        style.Border = GuiStyle.Borders;
                        style.WidgetRoundness = 8f;
                        style.BorderThickness = 1f;
                        gui.InputField("Path", ref _path, 0x100, Gui.InputFieldFlags.None, 0, 0, size, null, style);
                    }

                    using (gui.Node("FileName").ExpandHeight().Enter())
                    {
                        gui.Search("FileName", ref Dialog.fileName, 0, 0, Size.Percentage(1f), GuiStyle.ItemHeight);
                    }

                    using (gui.Node("NewFolderBtn").ExpandHeight().Enter())
                    {
                        var hovered = gui.IsNodeHovered();
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect,
                            hovered ? GuiStyle.Base11 : GuiStyle.Base4, 10);
                        gui.Draw2D.DrawText("New Folder", 20, gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Base10);
                        if (gui.IsNodePressed())
                        {
                            Debug.Log("creating new folder");
                            Dialog.directoryPath.CreateSubdirectory("NewFolder");
                            Dialog.refreshInfo = true;
                        }
                    }

                    using (gui.Node("SaveBtn").ExpandHeight().Enter())
                    {
                        var hovered = gui.IsNodeHovered();
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect,
                            hovered ? GuiStyle.Base11 : GuiStyle.Base4, 10);
                        gui.Draw2D.DrawText("Save", 20, gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Base10);
                        if (gui.IsNodePressed())
                        {
                            var path = Path.Combine(Dialog.directoryPath.FullName + "/" + Dialog.fileName);
                            Debug.Log("saving as" + path);
                            Dialog.OnComplete?.Invoke(path);
                            isOpened = false;
                        }
                    }

                    if (complete)
                    {
                        isOpened = false;
                    }
                }

                using (gui.Node("Content").ExpandWidth().Layout(LayoutType.Column).Padding(10).Clip().Enter())
                {
                    DirectoryInfo[] directories = Dialog.directoryPath.GetDirectories();
                    int index = 0;
                    foreach (var directory in directories)
                    {
                        using (gui.Node("Content" + index).ExpandWidth().Height(GuiStyle.ItemHeight).ScaleChildren()
                                   .Layout(LayoutType.Row).Enter())
                        {
                            if (gui.IsNodePressed())
                            {
                                _BackStack.Push(Dialog.directoryPath);
                                Dialog.directoryPath = directory;
                            }

                            var hovered = gui.IsNodeHovered();
                            Color bg = hovered ? GuiStyle.Base11 : GuiStyle.Base4;
                            using (gui.Node("FileName").Expand().Enter())
                            {
                                gui.Draw2D.DrawText(FontAwesome6.Folder + directory.Name, 20,
                                    gui.CurrentNode.LayoutData.InnerRect.Min, bg);
                            }

                            using (gui.Node("FileDate").Expand().Enter())
                            {
                                gui.Draw2D.DrawText(directory.LastWriteTime.ToString(), 20,
                                    gui.CurrentNode.LayoutData.InnerRect.Min, bg);
                            }

                            using (gui.Node("SaveBtn").Expand().Enter())
                            {
                                gui.Draw2D.DrawText("Save", 20, gui.CurrentNode.LayoutData.InnerRect, bg);
                            }

                            index++;
                        }
                    }

                    FileInfo[] files = Dialog.directoryPath.GetFiles();
                    foreach (var file in files)
                    {
                        using (gui.Node("Content" + index).ExpandWidth().Height(GuiStyle.ItemHeight).ScaleChildren()
                                   .Layout(LayoutType.Row).Enter())
                        {
                            var hovered = gui.IsNodeHovered();
                            Color bg = hovered ? GuiStyle.Base11 : GuiStyle.Base4;
                            using (gui.Node("FileName").Expand().Enter())
                            {
                                gui.Draw2D.DrawText(file.Name, 20, gui.CurrentNode.LayoutData.InnerRect.Min, bg);
                            }

                            using (gui.Node("FileDate").Expand().Enter())
                            {
                                gui.Draw2D.DrawText(file.LastWriteTime.ToString(), 20,
                                    gui.CurrentNode.LayoutData.InnerRect.Min, bg);
                            }

                            using (gui.Node("FileDate").Expand().Enter())
                            {
                                gui.Draw2D.DrawText(toMemSizeReadable(file.Length), 20,
                                    gui.CurrentNode.LayoutData.InnerRect.Min, bg);
                            }

                            using (gui.Node("SaveBtn").Expand().Enter())
                            {
                                gui.Draw2D.DrawText("Save", 20, gui.CurrentNode.LayoutData.InnerRect, bg);
                            }

                            index++;
                        }
                    }

                    gui.ScrollV();
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

        private void Sort(bool forceSort = false)
        {
            if (sortBy == sortByPrevious && !forceSort)
                return;

            sortByPrevious = sortBy;

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
                Dialog.currentFiles = Dialog.currentFiles.OrderBy(i => i.Extension).ToList();

            if (!sortDown)
            {
                Dialog.currentDirectories.Reverse();
                Dialog.currentFiles.Reverse();
            }
        }
    }
}