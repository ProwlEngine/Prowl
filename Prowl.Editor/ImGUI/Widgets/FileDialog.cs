using Hexa.NET.ImGuizmo;
using ImageMagick;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

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

        private Stack<object> _BackStack = new();
        private Stack<object> _ForwardStack = new();

        protected override bool Center { get; } = true;
        protected override double Width { get; } = 740;
        protected override double Height { get; } = 410;
        protected override bool BackgroundFade { get; } = true;
        protected override bool IsDockable => false;
        protected override bool LockSize => true;
        protected override bool TitleBar => base.TitleBar;

        public FileDialog(FileDialogContext dialogInfo) : base()
        {
            Dialog = dialogInfo;
            Title = Dialog.title;
        }

        public static void Open(FileDialogContext dialogInfo)
        {
            new FileDialog(dialogInfo);
        }

        protected override void Draw()
        {
            //bool complete = false;
            //
            //g.CurrentNode.Layout(LayoutType.Column);
            //g.CurrentNode.ScaleChildren();
            //g.CurrentNode.Padding(10, 10, 10, 10);
            //
            //using (g.Node("Header").ExpandWidth().MaxHeight(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            //{
            //    var textSize = UIDrawList.DefaultFont.CalcTextSize(Dialog.title, 0);
            //    using (g.Node("Title").Expand().MaxWidth(textSize.x).Enter())
            //    {
            //        g.DrawText(UIDrawList.DefaultFont, Dialog.title, 20, g.CurrentNode.LayoutData.InnerRect, GuiStyle.Base10);
            //    }
            //
            //    using (g.ButtonNode("BackBtn", out var pressed, out var hovered).Expand().MaxWidth(GuiStyle.ItemHeight).Enter())
            //    {
            //        g.DrawText(UIDrawList.DefaultFont, FontAwesome6.ChevronLeft, 20, g.CurrentNode.LayoutData.InnerRect, hovered ? GuiStyle.Base11 : GuiStyle.Base4);
            //    }
            //
            //    using (g.ButtonNode("ForwardBtn", out var pressed, out var hovered).Expand().MaxWidth(GuiStyle.ItemHeight).Enter())
            //    {
            //        g.DrawText(UIDrawList.DefaultFont, FontAwesome6.ChevronRight, 20, g.CurrentNode.LayoutData.InnerRect, hovered ? GuiStyle.Base11 : GuiStyle.Base4);
            //    }
            //
            //    using (g.Node("Search").Expand().Enter())
            //    {
            //        g.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f), GuiStyle.ItemHeight);
            //    }
            //    using (g.Node("Search").Expand().Enter())
            //    {
            //        g.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f), GuiStyle.ItemHeight);
            //    }
            //
            //
            //    g.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f), GuiStyle.ItemHeight);
            //}
            //
            //// Clicked outside Window
            //if (g.IsPointerClick(Silk.NET.Input.MouseButton.Left) && !g.IsHovering())
            //{
            //    isOpened = false;
            //    Dialog.OnCancel?.Invoke();
            //}
            //
            //if (complete)
            //    isOpened = false;
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
