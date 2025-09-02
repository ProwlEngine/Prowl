using System;
using System.Drawing;
using System.Linq;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using static Prowl.Quill.Canvas;

namespace Prowl.PaperUI.LayoutEngine
{
    public partial class Element
    {
        internal UISize Layout()
        {
            ref var data = ref _handle.Data;
            return data.Layout(_handle.GUI);
        }

        internal void AddRenderElement(ElementRenderCommand element)
        {
            ref var data = ref _handle.Data;
            data._renderCommands ??= new List<ElementRenderCommand>();
            data._renderCommands.Add(element);
        }

        internal List<ElementRenderCommand>? GetRenderElements() => _handle.Data._renderCommands;

        internal Vector2 ProcessText(float availableWidth)
        {
            if (_handle.Data.ProcessedText) return Vector2.zero;

            if (string.IsNullOrWhiteSpace(_handle.Data.Paragraph)) return Vector2.zero;

            _handle.Data.ProcessedText = true;

            Canvas canvas = Owner?.Canvas ?? throw new InvalidOperationException("Owner paper or canvas is not set.");

            if (_handle.Data.IsMarkdown == false)
            {
                var settings = TextLayoutSettings.Default;

                settings.WordSpacing = Convert.ToSingle(_handle.Data._elementStyle.GetValue(GuiProp.WordSpacing));
                settings.LetterSpacing = Convert.ToSingle(_handle.Data._elementStyle.GetValue(GuiProp.LetterSpacing));
                settings.LineHeight = Convert.ToSingle(_handle.Data._elementStyle.GetValue(GuiProp.LineHeight));

                settings.TabSize = (int)_handle.Data._elementStyle.GetValue(GuiProp.TabSize);
                settings.PixelSize = Convert.ToSingle(_handle.Data._elementStyle.GetValue(GuiProp.FontSize));

                if(_handle.Data.TextAlignment == TextAlignment.Left || _handle.Data.TextAlignment == TextAlignment.MiddleLeft || _handle.Data.TextAlignment == TextAlignment.BottomLeft)
                    settings.Alignment = Scribe.TextAlignment.Left;

                else if (_handle.Data.TextAlignment == TextAlignment.Center || _handle.Data.TextAlignment == TextAlignment.MiddleCenter || _handle.Data.TextAlignment == TextAlignment.BottomCenter)
                    settings.Alignment = Scribe.TextAlignment.Center;

                else if(_handle.Data.TextAlignment == TextAlignment.Right || _handle.Data.TextAlignment == TextAlignment.MiddleRight || _handle.Data.TextAlignment == TextAlignment.BottomRight)
                    settings.Alignment = Scribe.TextAlignment.Right;

                settings.PreferredFont = canvas.GetFont(_handle.Data.FontFamily, _handle.Data.FontStyle);
                settings.WrapMode = _handle.Data.WrapMode;

                settings.MaxWidth = availableWidth;

                _handle.Data._textLayout = canvas.CreateLayout(_handle.Data.Paragraph, settings);

                return _handle.Data._textLayout.Size;
            }
            else
            {
                var r = canvas.GetFont(_handle.Data.FontFamily, _handle.Data.FontStyle);
                var m = canvas.GetFont(_handle.Data.FontMonoFamily, _handle.Data.FontStyle);
                var b = canvas.GetFont(_handle.Data.FontFamily, FontStyle.Bold);
                var i = canvas.GetFont(_handle.Data.FontFamily, FontStyle.Italic);
                var bi = canvas.GetFont(_handle.Data.FontFamily, FontStyle.BoldItalic);
                var settings = MarkdownLayoutSettings.Default(r, m, b, i, bi, availableWidth);

                _handle.Data._quillMarkdown = canvas.CreateMarkdown(_handle.Data.Paragraph, settings);

                var markdownResult = _handle.Data._quillMarkdown as dynamic;
                return markdownResult?.Size ?? Vector2.zero;
            }
        }

        internal int GetIndexAtPosition(Rect rect, Vector2 screenPosition)
        {
            if (string.IsNullOrWhiteSpace(_handle.Data.Paragraph)) return 0;
            if (_handle.Data.IsMarkdown) throw new Exception("Markdown text cannot be edited!");

            if (!_handle.Data.ProcessedText)
                ProcessText((float)rect.width);

            Vector2 relPosition = screenPosition - rect.Position;

            return _handle.Data._textLayout!.GetCursorIndex(relPosition);
        }

        internal Vector2 GetPositionAtIndex(Rect rect, int index)
        {
            if (string.IsNullOrWhiteSpace(_handle.Data.Paragraph)) return Vector2.zero;
            if (_handle.Data.IsMarkdown) throw new Exception("Markdown text cannot be edited!");

            if (!_handle.Data.ProcessedText)
                ProcessText((float)rect.width);

            Vector2 relPosition = _handle.Data._textLayout!.GetCursorPosition(index);

            return rect.Position + relPosition;
        }

        internal void DrawText(double x, double y, float availableWidth, float availableHeight)
        {
            if (string.IsNullOrWhiteSpace(_handle.Data.Paragraph)) return;

            if (!_handle.Data.ProcessedText)
                ProcessText(availableWidth);

            Canvas canvas = Owner?.Canvas ?? throw new InvalidOperationException("Owner paper or canvas is not set.");

            var color = (Color)_handle.Data._elementStyle.GetValue(GuiProp.TextColor);

            FontColor fs = new FontColor(color.R, color.G, color.B, color.A);

            // Calculate vertical alignment offset
            double yOffset = 0;
            Vector2 textSize;

            if (_handle.Data.IsMarkdown == false)
            {
                if(_handle.Data._textLayout == null) throw new InvalidOperationException("Text layout is not processed.");

                textSize = _handle.Data._textLayout.Size;
            }
            else
            {
                if (_handle.Data._quillMarkdown == null) throw new InvalidOperationException("Markdown layout is not processed.");

                var markdownResult = _handle.Data._quillMarkdown as dynamic;
                textSize = markdownResult?.Size ?? Vector2.zero;
            }

            // Apply vertical alignment based on TextAlignment
            switch (_handle.Data.TextAlignment)
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

            if (_handle.Data.IsMarkdown == false)
            {
                canvas.DrawLayout(_handle.Data._textLayout, x, finalY, fs);
            }
            else
            {
                var markdownResult = _handle.Data._quillMarkdown as dynamic;
                canvas.DrawMarkdown(markdownResult, new Vector2(x, finalY));
            }
        }
    }
}