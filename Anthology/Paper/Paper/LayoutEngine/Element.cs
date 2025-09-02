using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Prowl.PaperUI.Events;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    public partial class Element
    {
        internal ElementHandle _handle;

        public Element(ElementHandle handle)
        {
            _handle = handle;
        }

        public ElementHandle Handle => _handle;
        public bool IsValid => _handle.IsValid;

        // Properties that delegate to the underlying ElementData
        public Paper Owner => _handle.GUI;
        
        public ulong ID
        {
            get => _handle.Data.ID;
            internal set => _handle.Data.ID = value;
        }

        public bool IsFocusable
        {
            get => _handle.Data.IsFocusable;
            set => _handle.Data.IsFocusable = value;
        }

        public bool IsNotInteractable
        {
            get => _handle.Data.IsNotInteractable;
            set => _handle.Data.IsNotInteractable = value;
        }

        public bool StopPropagation
        {
            get => _handle.Data.StopPropagation;
            set => _handle.Data.StopPropagation = value;
        }

        public Action<ClickEvent> OnClick
        {
            get => _handle.Data.OnClick;
            set => _handle.Data.OnClick = value;
        }

        public Action<ClickEvent> OnPress
        {
            get => _handle.Data.OnPress;
            set => _handle.Data.OnPress = value;
        }

        public Action<ClickEvent> OnRelease
        {
            get => _handle.Data.OnRelease;
            set => _handle.Data.OnRelease = value;
        }

        public Action<ClickEvent> OnDoubleClick
        {
            get => _handle.Data.OnDoubleClick;
            set => _handle.Data.OnDoubleClick = value;
        }

        public Action<ClickEvent> OnRightClick
        {
            get => _handle.Data.OnRightClick;
            set => _handle.Data.OnRightClick = value;
        }

        public Action<ClickEvent> OnHeld
        {
            get => _handle.Data.OnHeld;
            set => _handle.Data.OnHeld = value;
        }

        public Action<DragEvent> OnDragStart
        {
            get => _handle.Data.OnDragStart;
            set => _handle.Data.OnDragStart = value;
        }

        public Action<DragEvent> OnDragging
        {
            get => _handle.Data.OnDragging;
            set => _handle.Data.OnDragging = value;
        }

        public Action<DragEvent> OnDragEnd
        {
            get => _handle.Data.OnDragEnd;
            set => _handle.Data.OnDragEnd = value;
        }

        public Action<ScrollEvent> OnScroll
        {
            get => _handle.Data.OnScroll;
            set => _handle.Data.OnScroll = value;
        }

        public Action<ElementEvent> OnHover
        {
            get => _handle.Data.OnHover;
            set => _handle.Data.OnHover = value;
        }

        public Action<ElementEvent> OnEnter
        {
            get => _handle.Data.OnEnter;
            set => _handle.Data.OnEnter = value;
        }

        public Action<ElementEvent> OnLeave
        {
            get => _handle.Data.OnLeave;
            set => _handle.Data.OnLeave = value;
        }

        public Action<KeyEvent> OnKeyPressed
        {
            get => _handle.Data.OnKeyPressed;
            set => _handle.Data.OnKeyPressed = value;
        }

        public Action<TextInputEvent> OnTextInput
        {
            get => _handle.Data.OnTextInput;
            set => _handle.Data.OnTextInput = value;
        }

        public Action<FocusEvent> OnFocusChange
        {
            get => _handle.Data.OnFocusChange;
            set => _handle.Data.OnFocusChange = value;
        }

        public Action<Element, Rect> OnPostLayout
        {
            get => _handle.Data.OnPostLayout != null 
                ? (elem, rect) => _handle.Data.OnPostLayout(_handle, rect) 
                : null;
            set => _handle.Data.OnPostLayout = value != null 
                ? (handle, rect) => value(handle.GUI.GetElementWrapper(handle), rect) 
                : null;
        }

        public Scroll ScrollFlags
        {
            get => _handle.Data.ScrollFlags;
            set => _handle.Data.ScrollFlags = value;
        }

        public Action<Canvas, Rect, ScrollState> CustomScrollbarRenderer
        {
            get => _handle.Data.CustomScrollbarRenderer;
            set => _handle.Data.CustomScrollbarRenderer = value;
        }

        public Element Parent
        {
            get
            {
                if (_handle.Data.ParentIndex == -1) return null;
                var parentHandle = new ElementHandle(_handle.GUI, _handle.Data.ParentIndex);
                return _handle.GUI.GetElementWrapper(parentHandle);
            }
            internal set
            {
                _handle.Data.ParentIndex = value?._handle.Index ?? -1;
            }
        }

        public IList<Element> Children
        {
            get
            {
                return new ElementChildrenList(_handle);
            }
        }

        public bool Visible
        {
            get => _handle.Data.Visible;
            set => _handle.Data.Visible = value;
        }

        internal LayoutType LayoutType
        {
            get => _handle.Data.LayoutType;
            set => _handle.Data.LayoutType = value;
        }

        internal PositionType PositionType
        {
            get => _handle.Data.PositionType;
            set => _handle.Data.PositionType = value;
        }

        internal bool IsMarkdown
        {
            get => _handle.Data.IsMarkdown;
            set => _handle.Data.IsMarkdown = value;
        }

        internal string Paragraph
        {
            get => _handle.Data.Paragraph;
            set => _handle.Data.Paragraph = value;
        }

        internal string FontFamily
        {
            get => _handle.Data.FontFamily;
            set => _handle.Data.FontFamily = value;
        }

        internal string FontMonoFamily
        {
            get => _handle.Data.FontMonoFamily;
            set => _handle.Data.FontMonoFamily = value;
        }

        internal FontStyle FontStyle
        {
            get => _handle.Data.FontStyle;
            set => _handle.Data.FontStyle = value;
        }

        internal TextWrapMode WrapMode
        {
            get => _handle.Data.WrapMode;
            set => _handle.Data.WrapMode = value;
        }

        internal TextAlignment TextAlignment
        {
            get => _handle.Data.TextAlignment;
            set => _handle.Data.TextAlignment = value;
        }

        internal object _quillMarkdown
        {
            get => _handle.Data._quillMarkdown;
            set => _handle.Data._quillMarkdown = value;
        }

        internal TextLayout _textLayout
        {
            get => _handle.Data._textLayout;
            set => _handle.Data._textLayout = value;
        }

        internal List<ElementRenderCommand> _renderCommands
        {
            get => _handle.Data._renderCommands;
            set => _handle.Data._renderCommands = value;
        }

        internal ElementStyle _elementStyle
        {
            get => _handle.Data._elementStyle;
            set => _handle.Data._elementStyle = value;
        }

        internal bool _scissorEnabled
        {
            get => _handle.Data._scissorEnabled;
            set => _handle.Data._scissorEnabled = value;
        }

        internal Layer Layer
        {
            get => _handle.Data.Layer;
            set => _handle.Data.Layer = value;
        }

        internal bool ProcessedText
        {
            get => _handle.Data.ProcessedText;
            set => _handle.Data.ProcessedText = value;
        }

        internal double X
        {
            get => _handle.Data.X;
            set => _handle.Data.X = value;
        }

        internal double Y
        {
            get => _handle.Data.Y;
            set => _handle.Data.Y = value;
        }

        internal double LayoutWidth
        {
            get => _handle.Data.LayoutWidth;
            set => _handle.Data.LayoutWidth = value;
        }

        internal double LayoutHeight
        {
            get => _handle.Data.LayoutHeight;
            set => _handle.Data.LayoutHeight = value;
        }

        internal double RelativeX
        {
            get => _handle.Data.RelativeX;
            set => _handle.Data.RelativeX = value;
        }

        internal double RelativeY
        {
            get => _handle.Data.RelativeY;
            set => _handle.Data.RelativeY = value;
        }

        internal Rect LayoutRect => _handle.Data.LayoutRect;

        internal Func<double?, double?, (double, double)?> ContentSizer
        {
            get => _handle.Data.ContentSizer;
            set => _handle.Data.ContentSizer = value;
        }
    }

    // Helper class to provide List<Element> interface for children
    public class ElementChildrenList : IList<Element>
    {
        private readonly ElementHandle _handle;

        public ElementChildrenList(ElementHandle handle)
        {
            _handle = handle;
        }

        public Element this[int index]
        {
            get
            {
                int childIndex = _handle.Data.ChildIndices[index];
                var childHandle = new ElementHandle(_handle.GUI, childIndex);
                return _handle.GUI.GetElementWrapper(childHandle);
            }
            set => throw new NotSupportedException("Cannot directly set children");
        }

        public int Count => _handle.Data.ChildIndices.Count;
        public bool IsReadOnly => false;

        public void Add(Element item)
        {
            if (item._handle.GUI != _handle.GUI)
                throw new InvalidOperationException("Element belongs to different GUI instance");

            _handle.Data.ChildIndices.Add(item._handle.Index);
            item._handle.Data.ParentIndex = _handle.Index;
        }

        public void Clear()
        {
            foreach (int childIndex in _handle.Data.ChildIndices)
            {
                var childHandle = new ElementHandle(_handle.GUI, childIndex);
                childHandle.Data.ParentIndex = -1;
            }
            _handle.Data.ChildIndices.Clear();
        }

        public bool Contains(Element item)
        {
            return _handle.Data.ChildIndices.Contains(item._handle.Index);
        }

        public void CopyTo(Element[] array, int arrayIndex)
        {
            for (int i = 0; i < _handle.Data.ChildIndices.Count; i++)
            {
                int childIndex = _handle.Data.ChildIndices[i];
                var childHandle = new ElementHandle(_handle.GUI, childIndex);
                array[arrayIndex + i] = _handle.GUI.GetElementWrapper(childHandle);
            }
        }

        public IEnumerator<Element> GetEnumerator()
        {
            foreach (int childIndex in _handle.Data.ChildIndices)
            {
                var childHandle = new ElementHandle(_handle.GUI, childIndex);
                yield return _handle.GUI.GetElementWrapper(childHandle);
            }
        }

        public int IndexOf(Element item)
        {
            return _handle.Data.ChildIndices.IndexOf(item._handle.Index);
        }

        public void Insert(int index, Element item)
        {
            if (item._handle.GUI != _handle.GUI)
                throw new InvalidOperationException("Element belongs to different GUI instance");

            _handle.Data.ChildIndices.Insert(index, item._handle.Index);
            item._handle.Data.ParentIndex = _handle.Index;
        }

        public bool Remove(Element item)
        {
            bool removed = _handle.Data.ChildIndices.Remove(item._handle.Index);
            if (removed)
            {
                item._handle.Data.ParentIndex = -1;
            }
            return removed;
        }

        public void RemoveAt(int index)
        {
            int childIndex = _handle.Data.ChildIndices[index];
            var childHandle = new ElementHandle(_handle.GUI, childIndex);
            childHandle.Data.ParentIndex = -1;
            _handle.Data.ChildIndices.RemoveAt(index);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
