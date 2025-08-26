using System.Drawing;
using System.Runtime.Serialization.Formatters;

using Prowl.PaperUI.Events;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Quill.Canvas;

namespace Prowl.PaperUI.LayoutEngine
{
    public partial class Element
    {
        public Paper? Owner { get; internal set; } = null;
        public ulong ID { get; internal set; } = 0;

        // Events
        public bool IsFocusable { get; set; } = true;
        public bool IsNotInteractable { get; set; } = false;
        public bool StopPropagation { get; set; } = false;

        public Action<ClickEvent>? OnClick { get; set; }
        public Action<ClickEvent>? OnPress { get; set; }
        public Action<ClickEvent>? OnRelease { get; set; }
        public Action<ClickEvent>? OnDoubleClick { get; set; }
        public Action<ClickEvent>? OnRightClick { get; set; }
        public Action<ClickEvent>? OnHeld { get; set; }

        public Action<DragEvent>? OnDragStart { get; set; }
        public Action<DragEvent>? OnDragging { get; set; }
        public Action<DragEvent>? OnDragEnd { get; set; }

        public Action<ScrollEvent>? OnScroll { get; set; }
        public Action<ElementEvent>? OnHover { get; set; }
        public Action<ElementEvent>? OnEnter { get; set; }
        public Action<ElementEvent>? OnLeave { get; set; }

        public Action<KeyEvent>? OnKeyPressed { get; set; }
        public Action<TextInputEvent>? OnTextInput { get; set; }
        public Action<FocusEvent>? OnFocusChange { get; set; }

        public Action<Element, Rect>? OnPostLayout { get; set; }

        public Scroll ScrollFlags { get; set; } = Scroll.None;
        public Action<Canvas, Rect, ScrollState> CustomScrollbarRenderer { get; set; }

        public Element? Parent { get; internal set; }
        public List<Element> Children { get; } = [];

        public bool Visible = true;

        internal LayoutType LayoutType = LayoutType.Column;
        internal PositionType PositionType = PositionType.ParentDirected;

        internal bool IsMarkdown = false;
        internal string? Paragraph = null;
        internal string? FontFamily = null;
        internal string? FontMonoFamily = null;
        internal FontStyle FontStyle = FontStyle.Regular;
        internal TextWrapMode WrapMode = TextWrapMode.NoWrap;
        internal TextAlignment TextAlignment = TextAlignment.Left;

        internal QuillMarkdown? _quillMarkdown = null;
        internal TextLayout? _textLayout = null;

        // Rendering
        internal List<ElementRenderCommand>? _renderCommands = null;
        internal ElementStyle _elementStyle = new();
        internal bool _scissorEnabled = false;

        internal Layer Layer = Layer.Base;

        // Layout results
        internal bool ProcessedText = false;
        internal double X;
        internal double Y;
        internal double LayoutWidth;
        internal double LayoutHeight;
        internal double RelativeX;
        internal double RelativeY;
        internal Rect LayoutRect => new Rect(X, Y, LayoutWidth, LayoutHeight);

        // Content sizing for auto-sized elements
        internal Func<double?, double?, (double, double)?> ContentSizer { get; set; }

        internal void AddRenderElement(ElementRenderCommand element)
        {
            _renderCommands ??= new List<ElementRenderCommand>();
            _renderCommands.Add(element);
        }

        internal List<ElementRenderCommand>? GetRenderElements() => _renderCommands;

        internal Vector2 ProcessText(float availableWidth)
        {
            if (string.IsNullOrWhiteSpace(Paragraph)) return Vector2.zero;

            ProcessedText = true;

            Canvas canvas = Owner?.Canvas ?? throw new InvalidOperationException("Owner paper or canvas is not set.");

            if (IsMarkdown == false)
            {
                var settings = TextLayoutSettings.Default;

                settings.WordSpacing = Convert.ToSingle(_elementStyle.GetValue(GuiProp.WordSpacing));
                settings.LetterSpacing = Convert.ToSingle(_elementStyle.GetValue(GuiProp.LetterSpacing));
                settings.LineHeight = Convert.ToSingle(_elementStyle.GetValue(GuiProp.LineHeight));

                settings.TabSize = (int)_elementStyle.GetValue(GuiProp.TabSize);
                settings.PixelSize = Convert.ToSingle(_elementStyle.GetValue(GuiProp.FontSize));

                if(TextAlignment == TextAlignment.Left || TextAlignment == TextAlignment.MiddleLeft || TextAlignment == TextAlignment.BottomLeft)
                    settings.Alignment = Scribe.TextAlignment.Left;

                else if (TextAlignment == TextAlignment.Center || TextAlignment == TextAlignment.MiddleCenter || TextAlignment == TextAlignment.BottomCenter)
                    settings.Alignment = Scribe.TextAlignment.Center;

                else if(TextAlignment == TextAlignment.Right || TextAlignment == TextAlignment.MiddleRight || TextAlignment == TextAlignment.BottomRight)
                    settings.Alignment = Scribe.TextAlignment.Right;

                settings.PreferredFont = canvas.GetFont(FontFamily, FontStyle);
                settings.WrapMode = WrapMode;

                settings.MaxWidth = availableWidth;

                _textLayout = canvas.CreateLayout(Paragraph, settings);

                return _textLayout.Size;
            }
            else
            {
                var r = canvas.GetFont(FontFamily, FontStyle);
                var m = canvas.GetFont(FontMonoFamily, FontStyle);
                var b = canvas.GetFont(FontFamily, FontStyle.Bold);
                var i = canvas.GetFont(FontFamily, FontStyle.Italic);
                var bi = canvas.GetFont(FontFamily, FontStyle.BoldItalic);
                var settings = MarkdownLayoutSettings.Default(r, m, b, i, bi, availableWidth);

                _quillMarkdown = canvas.CreateMarkdown(Paragraph, settings);

                return _quillMarkdown.Value.Size;
            }
        }

        internal void DrawText(double x, double y, float availableWidth, float availableHeight)
        {
            if (string.IsNullOrWhiteSpace(Paragraph)) return;

            if (!ProcessedText)
                ProcessText(availableWidth);

            Canvas canvas = Owner?.Canvas ?? throw new InvalidOperationException("Owner paper or canvas is not set.");

            var color = (Color)_elementStyle.GetValue(GuiProp.TextColor);

            FontColor fs = new FontColor(color.R, color.G, color.B, color.A);

            // Calculate vertical alignment offset
            double yOffset = 0;
            Vector2 textSize;

            if (IsMarkdown == false)
            {
                if(_textLayout == null) throw new InvalidOperationException("Text layout is not processed.");

                textSize = _textLayout.Size;
            }
            else
            {
                if (_quillMarkdown == null) throw new InvalidOperationException("Markdown layout is not processed.");

                textSize = _quillMarkdown.Value.Size;
            }

            // Apply vertical alignment based on TextAlignment
            switch (TextAlignment)
            {
                case TextAlignment.MiddleLeft:
                case TextAlignment.MiddleCenter:
                case TextAlignment.MiddleRight:
                    yOffset = (availableHeight - textSize.y) / 2.0;
                    break;
                    
                case TextAlignment.BottomLeft:
                case TextAlignment.BottomCenter:
                case TextAlignment.BottomRight:
                    yOffset = availableHeight - textSize.y;
                    break;
                    
                case TextAlignment.Left:
                case TextAlignment.Center:
                case TextAlignment.Right:
                default:
                    yOffset = 0; // Top alignment (default)
                    break;
            }

            // Apply the calculated offset to the y position
            double finalY = y + yOffset;

            if (IsMarkdown == false)
            {
                canvas.DrawLayout(_textLayout, x, finalY, fs);
            }
            else
            {
                canvas.DrawMarkdown(_quillMarkdown.Value, new Vector2(x, finalY));
            }
        }
    }
}
