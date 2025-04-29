using System.Drawing;

using FontStashSharp;

using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Represents a reference to a style state that can be conditionally applied.
    /// Provides a fluent API for setting style properties based on element state conditions.
    /// </summary>
    /// <remarks>
    /// Creates a new style state reference.
    /// </remarks>
    /// <param name="owner">The element builder that owns this style state</param>
    /// <param name="isActive">Whether the style properties should be applied</param>
    public struct StyleStateRef(ElementBuilder owner, bool isActive)
    {

        /// <summary>
        /// Sets a style property value if the state is active.
        /// </summary>
        /// <param name="property">The property to set</param>
        /// <param name="value">The value to assign</param>
        /// <returns>This style state reference for chaining</returns>
        private StyleStateRef SetValue(GuiProp property, object value)
        {
            if (isActive)
                Paper.SetStyleProperty(owner.Element.ID, property, value);

            return this;
        }

        #region Appearance Properties

        /// <summary>Sets the background color of the element.</summary>
        public StyleStateRef BackgroundColor(Color color) => SetValue(GuiProp.BackgroundColor, color);

        /// <summary>Sets the border color of the element.</summary>
        public StyleStateRef BorderColor(Color color) => SetValue(GuiProp.BorderColor, color);

        /// <summary>Sets the border width of the element.</summary>
        public StyleStateRef BorderWidth(double width) => SetValue(GuiProp.BorderWidth, width);

        #endregion

        #region Corner Rounding

        /// <summary>Rounds the top corners of the element.</summary>
        public StyleStateRef RoundedTop(double radius) => SetValue(GuiProp.Rounded, new Vector4(radius, radius, 0, 0));

        /// <summary>Rounds the bottom corners of the element.</summary>
        public StyleStateRef RoundedBottom(double radius) => SetValue(GuiProp.Rounded, new Vector4(0, 0, radius, radius));

        /// <summary>Rounds the left corners of the element.</summary>
        public StyleStateRef RoundedLeft(double radius) => SetValue(GuiProp.Rounded, new Vector4(radius, 0, 0, radius));

        /// <summary>Rounds the right corners of the element.</summary>
        public StyleStateRef RoundedRight(double radius) => SetValue(GuiProp.Rounded, new Vector4(0, radius, radius, 0));

        /// <summary>Rounds all corners of the element with the same radius.</summary>
        public StyleStateRef Rounded(double radius) => SetValue(GuiProp.Rounded, new Vector4(radius, radius, radius, radius));

        /// <summary>Rounds each corner of the element with individual radii.</summary>
        /// <param name="tlRadius">Top-left radius</param>
        /// <param name="trRadius">Top-right radius</param>
        /// <param name="brRadius">Bottom-right radius</param>
        /// <param name="blRadius">Bottom-left radius</param>
        public StyleStateRef Rounded(double tlRadius, double trRadius, double brRadius, double blRadius) =>
            SetValue(GuiProp.Rounded, new Vector4(tlRadius, trRadius, brRadius, blRadius));

        #endregion

        #region Layout Properties

        /// <summary>Sets the aspect ratio (width/height) of the element.</summary>
        public StyleStateRef AspectRatio(double ratio) => SetValue(GuiProp.AspectRatio, ratio);

        /// <summary>Sets both width and height to the same value.</summary>
        public StyleStateRef Size(UnitValue sizeUniform) => Size(sizeUniform, sizeUniform);

        /// <summary>Sets the width and height of the element.</summary>
        public StyleStateRef Size(UnitValue width, UnitValue height)
        {
            SetValue(GuiProp.Width, width);
            SetValue(GuiProp.Height, height);
            return this;
        }

        /// <summary>Sets the width of the element.</summary>
        public StyleStateRef Width(UnitValue width) => SetValue(GuiProp.Width, width);

        /// <summary>Sets the height of the element.</summary>
        public StyleStateRef Height(UnitValue height) => SetValue(GuiProp.Height, height);

        /// <summary>Sets the minimum width of the element.</summary>
        public StyleStateRef MinWidth(UnitValue minWidth) => SetValue(GuiProp.MinWidth, minWidth);

        /// <summary>Sets the maximum width of the element.</summary>
        public StyleStateRef MaxWidth(UnitValue maxWidth) => SetValue(GuiProp.MaxWidth, maxWidth);

        /// <summary>Sets the minimum height of the element.</summary>
        public StyleStateRef MinHeight(UnitValue minHeight) => SetValue(GuiProp.MinHeight, minHeight);

        /// <summary>Sets the maximum height of the element.</summary>
        public StyleStateRef MaxHeight(UnitValue maxHeight) => SetValue(GuiProp.MaxHeight, maxHeight);

        /// <summary>Sets the position of the element from the left and top edges.</summary>
        public StyleStateRef Position(UnitValue left, UnitValue top)
        {
            SetValue(GuiProp.Left, left);
            SetValue(GuiProp.Top, top);
            return this;
        }

        /// <summary>Sets the left position of the element.</summary>
        public StyleStateRef Left(UnitValue left) => SetValue(GuiProp.Left, left);

        /// <summary>Sets the right position of the element.</summary>
        public StyleStateRef Right(UnitValue right) => SetValue(GuiProp.Right, right);

        /// <summary>Sets the top position of the element.</summary>
        public StyleStateRef Top(UnitValue top) => SetValue(GuiProp.Top, top);

        /// <summary>Sets the bottom position of the element.</summary>
        public StyleStateRef Bottom(UnitValue bottom) => SetValue(GuiProp.Bottom, bottom);

        /// <summary>Sets the minimum left position of the element.</summary>
        public StyleStateRef MinLeft(UnitValue minLeft) => SetValue(GuiProp.MinLeft, minLeft);

        /// <summary>Sets the maximum left position of the element.</summary>
        public StyleStateRef MaxLeft(UnitValue maxLeft) => SetValue(GuiProp.MaxLeft, maxLeft);

        /// <summary>Sets the minimum right position of the element.</summary>
        public StyleStateRef MinRight(UnitValue minRight) => SetValue(GuiProp.MinRight, minRight);

        /// <summary>Sets the maximum right position of the element.</summary>
        public StyleStateRef MaxRight(UnitValue maxRight) => SetValue(GuiProp.MaxRight, maxRight);

        /// <summary>Sets the minimum top position of the element.</summary>
        public StyleStateRef MinTop(UnitValue minTop) => SetValue(GuiProp.MinTop, minTop);

        /// <summary>Sets the maximum top position of the element.</summary>
        public StyleStateRef MaxTop(UnitValue maxTop) => SetValue(GuiProp.MaxTop, maxTop);

        /// <summary>Sets the minimum bottom position of the element.</summary>
        public StyleStateRef MinBottom(UnitValue minBottom) => SetValue(GuiProp.MinBottom, minBottom);

        /// <summary>Sets the maximum bottom position of the element.</summary>
        public StyleStateRef MaxBottom(UnitValue maxBottom) => SetValue(GuiProp.MaxBottom, maxBottom);

        /// <summary>Sets uniform margin on all sides.</summary>
        public StyleStateRef Margin(UnitValue all) => Margin(all, all, all, all);

        /// <summary>Sets horizontal and vertical margins.</summary>
        public StyleStateRef Margin(UnitValue horizontal, UnitValue vertical) =>
            Margin(horizontal, horizontal, vertical, vertical);

        /// <summary>Sets individual margins for each side.</summary>
        public StyleStateRef Margin(UnitValue left, UnitValue right, UnitValue top, UnitValue bottom)
        {
            SetValue(GuiProp.Left, left);
            SetValue(GuiProp.Right, right);
            SetValue(GuiProp.Top, top);
            SetValue(GuiProp.Bottom, bottom);
            return this;
        }

        /// <summary>Sets the left padding for child elements.</summary>
        public StyleStateRef ChildLeft(UnitValue childLeft) => SetValue(GuiProp.ChildLeft, childLeft);

        /// <summary>Sets the right padding for child elements.</summary>
        public StyleStateRef ChildRight(UnitValue childRight) => SetValue(GuiProp.ChildRight, childRight);

        /// <summary>Sets the top padding for child elements.</summary>
        public StyleStateRef ChildTop(UnitValue childTop) => SetValue(GuiProp.ChildTop, childTop);

        /// <summary>Sets the bottom padding for child elements.</summary>
        public StyleStateRef ChildBottom(UnitValue childBottom) => SetValue(GuiProp.ChildBottom, childBottom);

        /// <summary>Sets the spacing between rows in a container.</summary>
        public StyleStateRef RowBetween(UnitValue rowBetween) => SetValue(GuiProp.RowBetween, rowBetween);

        /// <summary>Sets the spacing between columns in a container.</summary>
        public StyleStateRef ColBetween(UnitValue colBetween) => SetValue(GuiProp.ColBetween, colBetween);

        /// <summary>Sets the left border width.</summary>
        public StyleStateRef BorderLeft(UnitValue borderLeft) => SetValue(GuiProp.BorderLeft, borderLeft);

        /// <summary>Sets the right border width.</summary>
        public StyleStateRef BorderRight(UnitValue borderRight) => SetValue(GuiProp.BorderRight, borderRight);

        /// <summary>Sets the top border width.</summary>
        public StyleStateRef BorderTop(UnitValue borderTop) => SetValue(GuiProp.BorderTop, borderTop);

        /// <summary>Sets the bottom border width.</summary>
        public StyleStateRef BorderBottom(UnitValue borderBottom) => SetValue(GuiProp.BorderBottom, borderBottom);

        /// <summary>Sets uniform border width on all sides.</summary>
        public StyleStateRef Border(UnitValue all) => Border(all, all, all, all);

        /// <summary>Sets horizontal and vertical border widths.</summary>
        public StyleStateRef Border(UnitValue horizontal, UnitValue vertical) =>
            Border(horizontal, horizontal, vertical, vertical);

        /// <summary>Sets individual border widths for each side.</summary>
        public StyleStateRef Border(UnitValue left, UnitValue right, UnitValue top, UnitValue bottom)
        {
            SetValue(GuiProp.BorderLeft, left);
            SetValue(GuiProp.BorderRight, right);
            SetValue(GuiProp.BorderTop, top);
            SetValue(GuiProp.BorderBottom, bottom);
            return this;
        }

        #endregion

        #region Transform Properties

        /// <summary>Sets horizontal translation.</summary>
        public StyleStateRef TranslateX(double x) => SetValue(GuiProp.TranslateX, x);

        /// <summary>Sets vertical translation.</summary>
        public StyleStateRef TranslateY(double y) => SetValue(GuiProp.TranslateY, y);

        /// <summary>Sets both horizontal and vertical translation.</summary>
        public StyleStateRef Translate(double x, double y)
        {
            SetValue(GuiProp.TranslateX, x);
            SetValue(GuiProp.TranslateY, y);
            return this;
        }

        /// <summary>Sets horizontal scaling factor.</summary>
        public StyleStateRef ScaleX(double x) => SetValue(GuiProp.ScaleX, x);

        /// <summary>Sets vertical scaling factor.</summary>
        public StyleStateRef ScaleY(double y) => SetValue(GuiProp.ScaleY, y);

        /// <summary>Sets uniform scaling in both directions.</summary>
        public StyleStateRef Scale(double scale) => Scale(scale, scale);

        /// <summary>Sets individual scaling factors for each axis.</summary>
        public StyleStateRef Scale(double x, double y)
        {
            SetValue(GuiProp.ScaleX, x);
            SetValue(GuiProp.ScaleY, y);
            return this;
        }

        /// <summary>Sets rotation angle in degrees.</summary>
        public StyleStateRef Rotate(double angleInDegrees) => SetValue(GuiProp.Rotate, angleInDegrees);

        /// <summary>Sets horizontal skew angle.</summary>
        public StyleStateRef SkewX(double angle) => SetValue(GuiProp.SkewX, angle);

        /// <summary>Sets vertical skew angle.</summary>
        public StyleStateRef SkewY(double angle) => SetValue(GuiProp.SkewY, angle);

        /// <summary>Sets both horizontal and vertical skew angles.</summary>
        public StyleStateRef Skew(double x, double y)
        {
            SetValue(GuiProp.SkewX, x);
            SetValue(GuiProp.SkewY, y);
            return this;
        }

        /// <summary>Sets the origin point for transformations.</summary>
        public StyleStateRef TransformOrigin(double x, double y)
        {
            SetValue(GuiProp.OriginX, x);
            SetValue(GuiProp.OriginY, y);
            return this;
        }

        /// <summary>Sets a complete transform matrix.</summary>
        public StyleStateRef Transform(Transform2D transform) => SetValue(GuiProp.Transform, transform);

        #endregion

        /// <summary>
        /// Returns to the element builder to continue the building chain.
        /// </summary>
        public ElementBuilder End() => owner;
    }

    /// <summary>
    /// Provides a fluent API for building and configuring UI elements.
    /// Implements IDisposable to support hierarchical element creation using 'using' blocks.
    /// </summary>
    public class ElementBuilder : IDisposable
    {
        private Element _element;

        /// <summary>Gets the underlying Element being built.</summary>
        public Element Element => _element;

        /// <summary>Style properties that are always applied.</summary>
        public StyleStateRef Normal => new StyleStateRef(this, true);

        /// <summary>Style properties applied when the element is hovered.</summary>
        public StyleStateRef Hovered => new StyleStateRef(this, Paper.IsElementHovered(_element.ID));

        /// <summary>Style properties applied when the element is active (pressed).</summary>
        public StyleStateRef Active => new StyleStateRef(this, Paper.IsElementActive(_element.ID));

        /// <summary>Style properties applied when the element has focus.</summary>
        public StyleStateRef Focused => new StyleStateRef(this, Paper.IsElementFocused(_element.ID));

        /// <summary>
        /// Creates a new element builder with the specified ID.
        /// </summary>
        /// <param name="storageHash">Unique identifier for this element</param>
        public ElementBuilder(ulong storageHash)
        {
            _element = new LayoutEngine.Element();
            _element.ID = storageHash;

            Paper.PushID(storageHash);
        }

        /// <summary>
        /// Configures a property transition with the specified duration and easing function.
        /// </summary>
        /// <param name="property">The property to animate</param>
        /// <param name="duration">Animation duration in seconds</param>
        /// <param name="easing">Optional easing function</param>
        public ElementBuilder Transition(GuiProp property, double duration, Func<double, double> easing = null)
        {
            Paper.SetTransitionConfig(_element.ID, property, duration, easing);
            return this;
        }

        /// <summary>
        /// Creates a conditional style state that only applies if the condition is true.
        /// </summary>
        /// <param name="condition">Boolean condition to evaluate</param>
        public StyleStateRef If(bool condition) => new StyleStateRef(this, condition);

        #region Normal Properties

        /// <summary>
        /// Inherits style properties from the specified element or from the parent if not specified.
        /// </summary>
        public ElementBuilder InheritStyle(Element? element = null)
        {
            if (element != null)
            {
                _element._elementStyle.SetParent(element._elementStyle);
                return this;
            }

            if (_element.Parent != null)
                _element._elementStyle.SetParent(_element.Parent._elementStyle);

            return this;
        }

        /// <summary>
        /// Sets a style property value directly.
        /// </summary>
        private ElementBuilder SetValue(GuiProp property, object value)
        {
            Paper.SetStyleProperty(_element.ID, property, value);
            return this;
        }

        #region Appearance Properties

        /// <summary>Sets the background color of the element.</summary>
        public ElementBuilder BackgroundColor(byte r, byte g, byte b, byte a = 255) => BackgroundColor(Color.FromArgb(a, r, g, b));
        public ElementBuilder BackgroundColor(Color color) => SetValue(GuiProp.BackgroundColor, color);

        /// <summary>Sets the border color of the element.</summary>
        public ElementBuilder BorderColor(byte r, byte g, byte b, byte a = 255) => BorderColor(Color.FromArgb(a, r, g, b));
        public ElementBuilder BorderColor(Color color) => SetValue(GuiProp.BorderColor, color);

        /// <summary>Sets the border width of the element.</summary>
        public ElementBuilder BorderWidth(double width) => SetValue(GuiProp.BorderWidth, width);

        #endregion

        #region Corner Rounding

        /// <summary>Rounds the top corners of the element.</summary>
        public ElementBuilder RoundedTop(double radius) => SetValue(GuiProp.Rounded, new Vector4(radius, radius, 0, 0));

        /// <summary>Rounds the bottom corners of the element.</summary>
        public ElementBuilder RoundedBottom(double radius) => SetValue(GuiProp.Rounded, new Vector4(0, 0, radius, radius));

        /// <summary>Rounds the left corners of the element.</summary>
        public ElementBuilder RoundedLeft(double radius) => SetValue(GuiProp.Rounded, new Vector4(radius, 0, 0, radius));

        /// <summary>Rounds the right corners of the element.</summary>
        public ElementBuilder RoundedRight(double radius) => SetValue(GuiProp.Rounded, new Vector4(0, radius, radius, 0));

        /// <summary>Rounds all corners of the element with the same radius.</summary>
        public ElementBuilder Rounded(double radius) => SetValue(GuiProp.Rounded, new Vector4(radius, radius, radius, radius));

        /// <summary>Rounds each corner of the element with individual radii.</summary>
        public ElementBuilder Rounded(double tlRadius, double trRadius, double brRadius, double blRadius) =>
            SetValue(GuiProp.Rounded, new Vector4(tlRadius, trRadius, brRadius, blRadius));

        #endregion

        #region Layout Properties

        /// <summary>Sets the aspect ratio (width/height) of the element.</summary>
        public ElementBuilder AspectRatio(double ratio) => SetValue(GuiProp.AspectRatio, ratio);

        /// <summary>Sets both width and height to the same value.</summary>
        public ElementBuilder Size(UnitValue sizeUniform) => Size(sizeUniform, sizeUniform);

        /// <summary>Sets the width and height of the element.</summary>
        public ElementBuilder Size(UnitValue width, UnitValue height)
        {
            SetValue(GuiProp.Width, width);
            SetValue(GuiProp.Height, height);
            return this;
        }

        /// <summary>Sets the width of the element.</summary>
        public ElementBuilder Width(UnitValue width) => SetValue(GuiProp.Width, width);

        /// <summary>Sets the height of the element.</summary>
        public ElementBuilder Height(UnitValue height) => SetValue(GuiProp.Height, height);

        /// <summary>Sets the minimum width of the element.</summary>
        public ElementBuilder MinWidth(UnitValue minWidth) => SetValue(GuiProp.MinWidth, minWidth);

        /// <summary>Sets the maximum width of the element.</summary>
        public ElementBuilder MaxWidth(UnitValue maxWidth) => SetValue(GuiProp.MaxWidth, maxWidth);

        /// <summary>Sets the minimum height of the element.</summary>
        public ElementBuilder MinHeight(UnitValue minHeight) => SetValue(GuiProp.MinHeight, minHeight);

        /// <summary>Sets the maximum height of the element.</summary>
        public ElementBuilder MaxHeight(UnitValue maxHeight) => SetValue(GuiProp.MaxHeight, maxHeight);

        /// <summary>Sets the position of the element from the left and top edges.</summary>
        public ElementBuilder Position(UnitValue left, UnitValue top)
        {
            SetValue(GuiProp.Left, left);
            SetValue(GuiProp.Top, top);
            return this;
        }

        /// <summary>Sets the left position of the element.</summary>
        public ElementBuilder Left(UnitValue left) => SetValue(GuiProp.Left, left);

        /// <summary>Sets the right position of the element.</summary>
        public ElementBuilder Right(UnitValue right) => SetValue(GuiProp.Right, right);

        /// <summary>Sets the top position of the element.</summary>
        public ElementBuilder Top(UnitValue top) => SetValue(GuiProp.Top, top);

        /// <summary>Sets the bottom position of the element.</summary>
        public ElementBuilder Bottom(UnitValue bottom) => SetValue(GuiProp.Bottom, bottom);

        /// <summary>Sets the minimum left position of the element.</summary>
        public ElementBuilder MinLeft(UnitValue minLeft) => SetValue(GuiProp.MinLeft, minLeft);

        /// <summary>Sets the maximum left position of the element.</summary>
        public ElementBuilder MaxLeft(UnitValue maxLeft) => SetValue(GuiProp.MaxLeft, maxLeft);

        /// <summary>Sets the minimum right position of the element.</summary>
        public ElementBuilder MinRight(UnitValue minRight) => SetValue(GuiProp.MinRight, minRight);

        /// <summary>Sets the maximum right position of the element.</summary>
        public ElementBuilder MaxRight(UnitValue maxRight) => SetValue(GuiProp.MaxRight, maxRight);

        /// <summary>Sets the minimum top position of the element.</summary>
        public ElementBuilder MinTop(UnitValue minTop) => SetValue(GuiProp.MinTop, minTop);

        /// <summary>Sets the maximum top position of the element.</summary>
        public ElementBuilder MaxTop(UnitValue maxTop) => SetValue(GuiProp.MaxTop, maxTop);

        /// <summary>Sets the minimum bottom position of the element.</summary>
        public ElementBuilder MinBottom(UnitValue minBottom) => SetValue(GuiProp.MinBottom, minBottom);

        /// <summary>Sets the maximum bottom position of the element.</summary>
        public ElementBuilder MaxBottom(UnitValue maxBottom) => SetValue(GuiProp.MaxBottom, maxBottom);

        /// <summary>Sets uniform margin on all sides.</summary>
        public ElementBuilder Margin(UnitValue all) => Margin(all, all, all, all);

        /// <summary>Sets horizontal and vertical margins.</summary>
        public ElementBuilder Margin(UnitValue horizontal, UnitValue vertical) =>
            Margin(horizontal, horizontal, vertical, vertical);

        /// <summary>Sets individual margins for each side.</summary>
        public ElementBuilder Margin(UnitValue left, UnitValue right, UnitValue top, UnitValue bottom)
        {
            SetValue(GuiProp.Left, left);
            SetValue(GuiProp.Right, right);
            SetValue(GuiProp.Top, top);
            SetValue(GuiProp.Bottom, bottom);
            return this;
        }

        /// <summary>Sets the left padding for child elements.</summary>
        public ElementBuilder ChildLeft(UnitValue childLeft) => SetValue(GuiProp.ChildLeft, childLeft);

        /// <summary>Sets the right padding for child elements.</summary>
        public ElementBuilder ChildRight(UnitValue childRight) => SetValue(GuiProp.ChildRight, childRight);

        /// <summary>Sets the top padding for child elements.</summary>
        public ElementBuilder ChildTop(UnitValue childTop) => SetValue(GuiProp.ChildTop, childTop);

        /// <summary>Sets the bottom padding for child elements.</summary>
        public ElementBuilder ChildBottom(UnitValue childBottom) => SetValue(GuiProp.ChildBottom, childBottom);

        /// <summary>Sets the spacing between rows in a container.</summary>
        public ElementBuilder RowBetween(UnitValue rowBetween) => SetValue(GuiProp.RowBetween, rowBetween);

        /// <summary>Sets the spacing between columns in a container.</summary>
        public ElementBuilder ColBetween(UnitValue colBetween) => SetValue(GuiProp.ColBetween, colBetween);

        /// <summary>Sets the left border width.</summary>
        public ElementBuilder BorderLeft(UnitValue borderLeft) => SetValue(GuiProp.BorderLeft, borderLeft);

        /// <summary>Sets the right border width.</summary>
        public ElementBuilder BorderRight(UnitValue borderRight) => SetValue(GuiProp.BorderRight, borderRight);

        /// <summary>Sets the top border width.</summary>
        public ElementBuilder BorderTop(UnitValue borderTop) => SetValue(GuiProp.BorderTop, borderTop);

        /// <summary>Sets the bottom border width.</summary>
        public ElementBuilder BorderBottom(UnitValue borderBottom) => SetValue(GuiProp.BorderBottom, borderBottom);

        /// <summary>Sets uniform border width on all sides.</summary>
        public ElementBuilder Border(UnitValue all) => Border(all, all, all, all);

        /// <summary>Sets horizontal and vertical border widths.</summary>
        public ElementBuilder Border(UnitValue horizontal, UnitValue vertical) =>
            Border(horizontal, horizontal, vertical, vertical);

        /// <summary>Sets individual border widths for each side.</summary>
        public ElementBuilder Border(UnitValue left, UnitValue right, UnitValue top, UnitValue bottom)
        {
            SetValue(GuiProp.BorderLeft, left);
            SetValue(GuiProp.BorderRight, right);
            SetValue(GuiProp.BorderTop, top);
            SetValue(GuiProp.BorderBottom, bottom);
            return this;
        }

        #endregion

        #region Transform Properties

        /// <summary>Sets horizontal translation.</summary>
        public ElementBuilder TranslateX(double x) => SetValue(GuiProp.TranslateX, x);

        /// <summary>Sets vertical translation.</summary>
        public ElementBuilder TranslateY(double y) => SetValue(GuiProp.TranslateY, y);

        /// <summary>Sets both horizontal and vertical translation.</summary>
        public ElementBuilder Translate(double x, double y)
        {
            SetValue(GuiProp.TranslateX, x);
            SetValue(GuiProp.TranslateY, y);
            return this;
        }

        /// <summary>Sets horizontal scaling factor.</summary>
        public ElementBuilder ScaleX(double x) => SetValue(GuiProp.ScaleX, x);

        /// <summary>Sets vertical scaling factor.</summary>
        public ElementBuilder ScaleY(double y) => SetValue(GuiProp.ScaleY, y);

        /// <summary>Sets uniform scaling in both directions.</summary>
        public ElementBuilder Scale(double scale) => Scale(scale, scale);

        /// <summary>Sets individual scaling factors for each axis.</summary>
        public ElementBuilder Scale(double x, double y)
        {
            SetValue(GuiProp.ScaleX, x);
            SetValue(GuiProp.ScaleY, y);
            return this;
        }

        /// <summary>Sets rotation angle in degrees.</summary>
        public ElementBuilder Rotate(double angleInDegrees) => SetValue(GuiProp.Rotate, angleInDegrees);

        /// <summary>Sets horizontal skew angle.</summary>
        public ElementBuilder SkewX(double angle) => SetValue(GuiProp.SkewX, angle);

        /// <summary>Sets vertical skew angle.</summary>
        public ElementBuilder SkewY(double angle) => SetValue(GuiProp.SkewY, angle);

        /// <summary>Sets both horizontal and vertical skew angles.</summary>
        public ElementBuilder Skew(double x, double y)
        {
            SetValue(GuiProp.SkewX, x);
            SetValue(GuiProp.SkewY, y);
            return this;
        }

        /// <summary>Sets the origin point for transformations.</summary>
        public ElementBuilder TransformOrigin(double x, double y)
        {
            SetValue(GuiProp.OriginX, x);
            SetValue(GuiProp.OriginY, y);
            return this;
        }

        /// <summary>Sets a complete transform matrix.</summary>
        public ElementBuilder Transform(Transform2D transform) => SetValue(GuiProp.Transform, transform);

        #endregion

        #endregion

        #region Event Handlers

        /// <summary>Makes the element incapable of receiving focus.</summary>
        public ElementBuilder IsNotFocusable()
        {
            _element.IsFocusable = false;
            return this;
        }

        /// <summary>Sets a callback that runs after layout calculation is complete.</summary>
        public ElementBuilder OnPostLayout(Action<Element, Rect> handler)
        {
            _element.OnPostLayout += handler;
            return this;
        }

        /// <summary>Sets a callback that runs after layout calculation is complete, with a captured value.</summary>
        public ElementBuilder OnPostLayout<T>(T capturedValue, Action<T, Element, Rect> handler) =>
            OnPostLayout((Element element, Rect rect) => handler(capturedValue, element, rect));

        /// <summary>Sets a callback that runs when the element is pressed.</summary>
        public ElementBuilder OnPress(Action<ClickEvent> handler)
        {
            _element.OnPress += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is pressed, with a captured value.</summary>
        public ElementBuilder OnPress<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnPress((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is held down.</summary>
        public ElementBuilder OnHeld(Action<ClickEvent> handler)
        {
            _element.OnHeld += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is held down, with a captured value.</summary>
        public ElementBuilder OnHeld<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnHeld((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is clicked.</summary>
        public ElementBuilder OnClick(Action<ClickEvent> handler)
        {
            _element.OnClick += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is clicked, with a captured value.</summary>
        public ElementBuilder OnClick<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnClick((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is dragged.</summary>
        public ElementBuilder OnDragStart(Action<DragEvent> handler)
        {
            _element.OnDragStart += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is dragged, with a captured value.</summary>
        public ElementBuilder OnDragStart<T>(T capturedValue, Action<T, DragEvent> handler) =>
            OnDragStart((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is dragged.</summary>
        public ElementBuilder OnDragging(Action<DragEvent> handler)
        {
            _element.OnDragging += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is dragged, with a captured value.</summary>
        public ElementBuilder OnDragging<T>(T capturedValue, Action<T, DragEvent> handler) =>
            OnDragging((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is released after dragging.</summary>
        public ElementBuilder OnDragEnd(Action<DragEvent> handler)
        {
            _element.OnDragEnd += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is released after dragging, with a captured value.</summary>
        public ElementBuilder OnDragEnd<T>(T capturedValue, Action<T, DragEvent> handler) =>
            OnDragEnd((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the mouse button is released after clicking this element.</summary>
        public ElementBuilder OnRelease(Action<ClickEvent> handler)
        {
            _element.OnRelease += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the mouse button is released after clicking this element, with a captured value.</summary>
        public ElementBuilder OnRelease<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnRelease((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is double-clicked.</summary>
        public ElementBuilder OnDoubleClick(Action<ClickEvent> handler)
        {
            _element.OnDoubleClick += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is double-clicked, with a captured value.</summary>
        public ElementBuilder OnDoubleClick<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnDoubleClick((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is right-clicked.</summary>
        public ElementBuilder OnRightClick(Action<ClickEvent> handler)
        {
            _element.OnRightClick += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is right-clicked, with a captured value.</summary>
        public ElementBuilder OnRightClick<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnRightClick((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when scrolling occurs over the element.</summary>
        public ElementBuilder OnScroll(Action<ScrollEvent> handler)
        {
            _element.OnScroll += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when scrolling occurs over the element, with a captured value.</summary>
        public ElementBuilder OnScroll<T>(T capturedValue, Action<T, ScrollEvent> handler) =>
            OnScroll((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when a key is pressed while the element is focused.</summary>
        public ElementBuilder OnKeyPressed(Action<KeyEvent> handler)
        {
            _element.OnKeyPressed += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when a key is pressed while the element is focused, with a captured value.</summary>
        public ElementBuilder OnKeyPressed<T>(T capturedValue, Action<T, KeyEvent> handler) =>
            OnKeyPressed((key) => handler(capturedValue, key));

        /// <summary>Sets a callback that runs when a character is typed while the element is focused.</summary>
        public ElementBuilder OnTextInput(Action<TextInputEvent> handler)
        {
            _element.OnTextInput += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when a character is typed while the element is focused, with a captured value.</summary>
        public ElementBuilder OnTextInput<T>(T capturedValue, Action<T, TextInputEvent> handler) =>
            OnTextInput((character) => handler(capturedValue, character));

        /// <summary>Sets a callback that runs when the cursor hovers over the element.</summary>
        public ElementBuilder OnHover(Action<ElementEvent> handler)
        {
            _element.OnHover += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the cursor hovers over the element, with a captured value.</summary>
        public ElementBuilder OnHover<T>(T capturedValue, Action<T, ElementEvent> handler) =>
            OnHover((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the Focused state changes.</summary>
        public ElementBuilder OnFocusChange(Action<FocusEvent> handler)
        {
            _element.OnFocusChange += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the Focused state changes, with a captured value.</summary>
        public ElementBuilder OnFocusChange<T>(T capturedValue, Action<T, FocusEvent> handler) =>
            OnFocusChange((focused) => handler(capturedValue, focused));

        /// <summary>Sets a callback that runs when the cursor enters the element's bounds.</summary>
        public ElementBuilder OnEnter(Action<ElementEvent> handler)
        {
            _element.OnEnter += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the cursor enters the element's bounds, with a captured value.</summary>
        public ElementBuilder OnEnter<T>(T capturedValue, Action<T, ElementEvent> handler) =>
            OnEnter((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the cursor leaves the element's bounds.</summary>
        public ElementBuilder OnLeave(Action<ElementEvent> handler)
        {
            _element.OnLeave += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the cursor leaves the element's bounds, with a captured value.</summary>
        public ElementBuilder OnLeave<T>(T capturedValue, Action<T, ElementEvent> handler) =>
            OnLeave((e) => handler(capturedValue, e));

        #endregion

        #region Behavior Configuration

        /// <summary>Makes the element non-interactive (ignores mouse/touch events).</summary>
        public ElementBuilder IsNotInteractable()
        {
            _element.IsNotInteractable = true;
            return this;
        }

        /// <summary>Sets the layout direction for child elements.</summary>
        /// <param name="layoutType">How child elements should be arranged (Row or Column)</param>
        public ElementBuilder LayoutType(LayoutType layoutType)
        {
            _element.LayoutType = layoutType;
            return this;
        }

        /// <summary>Sets how the element is positioned within its parent.</summary>
        /// <param name="positionType">Position strategy (SelfDirected or ParentDirected)</param>
        public ElementBuilder PositionType(PositionType positionType)
        {
            _element.PositionType = positionType;
            return this;
        }

        /// <summary>Sets whether the element is visible.</summary>
        /// <param name="visible">True to show the element, false to hide it</param>
        public ElementBuilder Visible(bool visible)
        {
            _element.Visible = visible;
            return this;
        }

        /// <summary>Adds text content to the element.</summary>
        /// <param name="text">The text to display</param>
        public ElementBuilder Text(Text text)
        {
            _element._renderCommands ??= new();
            _element._renderCommands.Add(new ElementRenderCommand {
                Type = ElementRenderCommand.ElementType.Text,
                Element = _element,
                Text = text,
            });
            return this;
        }

        /// <summary>Enables content clipping to the element's bounds.</summary>
        public ElementBuilder Clip()
        {
            _element._scissorEnabled = true;
            return this;
        }

        /// <summary>Sets the element's Z-order (depth) for rendering.</summary>
        /// <param name="depth">Z-order value (higher values appear on top)</param>
        public ElementBuilder Depth(double depth)
        {
            _element.ZLayer = depth;
            return this;
        }

        /// <summary>
        /// Configures scrolling behavior for the element.
        /// </summary>
        /// <param name="flags">Flags to control scroll behavior</param>
        public ElementBuilder SetScroll(Scroll flags)
        {
            // Set the scroll flags directly on the element
            _element.ScrollFlags = flags;

            // Enable clipping for scrollable elements
            if (flags != Scroll.None)
            {
                _element._scissorEnabled = true;

                // Initialize scroll state if not already present
                if (!Paper.HasElementStorage(_element, "ScrollState"))
                {
                    Paper.SetElementStorage(_element, "ScrollState", new ScrollState());
                }

                // Set up scrolling event handlers
                ConfigureScrollHandlers();
            }

            return this;
        }

        /// <summary>
        /// Sets the scroll position of the element.
        /// </summary>
        public ElementBuilder SetScrollPosition(Vector2 position)
        {
            var state = Paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());
            state.Position = position;
            Paper.SetElementStorage(_element, "ScrollState", state);
            return this;
        }

        /// <summary>
        /// Sets a callback to customize the rendering of scrollbars.
        /// </summary>
        public ElementBuilder CustomScrollbarRenderer(Action<Canvas, Rect, ScrollState> renderer)
        {
            // Store the renderer directly on the element
            _element.CustomScrollbarRenderer = renderer;
            return this;
        }

        /// <summary>
        /// Configures event handlers for scrolling interactions.
        /// </summary>
        private void ConfigureScrollHandlers()
        {
            // Update content and viewport sizes after layout
            OnPostLayout((element, rect) => {
                var state = Paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

                // Set viewport size to current element size
                state.ViewportSize = new Vector2(rect.width, rect.height);

                // For content size, we need to measure all children
                // Here we're assuming content size is the max bounds of all children
                double maxX = 0;
                double maxY = 0;

                foreach (var child in element.Children)
                {
                    maxX = Math.Max(maxX, child.RelativeX + child.LayoutWidth);
                    maxY = Math.Max(maxY, child.RelativeY + child.LayoutHeight);
                }

                state.ContentSize = new Vector2(maxX, maxY);
                state.ClampScrollPosition();

                Paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle hover events to detect when cursor is over scrollbars
            OnHover((e) => {
                var state = Paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());
                Vector2 mousePos = Paper.PointerPos;

                // Check if pointer is over scrollbars
                state.IsVerticalScrollbarHovered = state.IsPointOverVerticalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);
                state.IsHorizontalScrollbarHovered = state.IsPointOverHorizontalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);

                Paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle mouse leaving the element
            OnLeave((rect) => {
                var state = Paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());
                state.IsVerticalScrollbarHovered = false;
                state.IsHorizontalScrollbarHovered = false;
                Paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle scrolling with mouse wheel
            OnScroll((e) => {
                var state = Paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

                // Don't handle wheel scrolling if actively dragging scrollbars
                if (state.IsDraggingVertical || state.IsDraggingHorizontal)
                    return;

                if ((_element.ScrollFlags & Scroll.ScrollY) != 0)
                {
                    state.Position = new Vector2(
                        state.Position.x,
                        state.Position.y - e.Delta * 30  // Adjust scroll speed as needed
                    );
                }
                else if ((_element.ScrollFlags & Scroll.ScrollX) != 0)
                {
                    state.Position = new Vector2(
                        state.Position.x - e.Delta * 30,
                        state.Position.y
                    );
                }

                state.ClampScrollPosition();
                Paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle start scrollbar drag
            OnDragStart((e) => {
                var state = Paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(Element.ScrollFlags)) return;

                Vector2 mousePos = Paper.PointerPos;

                // Check if click is on a scrollbar
                bool onVertical = state.IsPointOverVerticalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);
                bool onHorizontal = state.IsPointOverHorizontalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);

                // Start dragging the appropriate scrollbar
                if (onVertical)
                {
                    state.IsDraggingVertical = true;
                    state.DragStartPosition = mousePos;
                    state.ScrollStartPosition = state.Position;
                }
                else if (onHorizontal)
                {
                    state.IsDraggingHorizontal = true;
                    state.DragStartPosition = mousePos;
                    state.ScrollStartPosition = state.Position;
                }

                Paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle dragging scrollbars
            OnDragging((e) => {
                var state = Paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(Element.ScrollFlags)) return;

                Vector2 mousePos = Paper.PointerPos;

                // Handle scrollbar dragging
                if (state.IsDraggingVertical)
                {
                    state.HandleVerticalScrollbarDrag(mousePos, e.ElementRect, _element.ScrollFlags);
                }
                else if (state.IsDraggingHorizontal)
                {
                    state.HandleHorizontalScrollbarDrag(mousePos, e.ElementRect, _element.ScrollFlags);
                }

                Paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle after dragging
            OnDragEnd((e) => {
                var state = Paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(Element.ScrollFlags)) return;

                state.IsDraggingVertical = false;
                state.IsDraggingHorizontal = false;

                // Update hover state on release
                Vector2 mousePos = Paper.PointerPos;
                state.IsVerticalScrollbarHovered = state.IsPointOverVerticalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);
                state.IsHorizontalScrollbarHovered = state.IsPointOverHorizontalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);

                Paper.SetElementStorage(_element, "ScrollState", state);
            });
        }

        #endregion

        #region Text Field

        /// <summary>
        /// Creates a text field control that allows users to input and edit text.
        /// </summary>
        /// <param name="value">Current text value</param>
        /// <param name="font">Font used to render the text</param>
        /// <param name="onChange">Optional callback when the text changes</param>
        /// <param name="placeholder">Optional placeholder text shown when the field is empty</param>
        /// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the text field</returns>
        public ElementBuilder TextField(
            string value,
            SpriteFontBase font,
            Action<string> onChange = null,
            string placeholder = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int intID = 0)
        {
            const double TextXPadding = 4;

            value = value ?? "";

            Clip();

            // Store the current value in element storage
            Paper.SetElementStorage(Element, "Value", value);

            // Get the text to display (value or placeholder)
            bool isEmpty = string.IsNullOrEmpty(value);
            string displayText = isEmpty ? placeholder : value;
            Color textColor = isEmpty ? Color.FromArgb(160, 200, 200, 200) : Color.FromArgb(255, 250, 250, 250);

            // Store focus state
            bool isFocused = Paper.IsElementFocused(Element.ID);
            Paper.SetElementStorage(Element, "IsFocused", isFocused);

            // Create a blinking cursor position tracker
            int cursorPosition = Paper.GetElementStorage(Element, "CursorPosition", value.Length);
            // Clamp cursor position to valid range
            cursorPosition = Math.Clamp(cursorPosition, 0, value.Length);
            Paper.SetElementStorage(Element, "CursorPosition", cursorPosition);

            // Selection range (if text is selected)
            int selectionStart = Paper.GetElementStorage(Element, "SelectionStart", -1);
            int selectionEnd = Paper.GetElementStorage(Element, "SelectionEnd", -1);
            // Clamp selection range to valid range
            selectionStart = Math.Clamp(selectionStart, 0, value.Length);
            selectionEnd = Math.Clamp(selectionEnd, 0, value.Length);
            Paper.SetElementStorage(Element, "SelectionStart", selectionStart);
            Paper.SetElementStorage(Element, "SelectionEnd", selectionEnd);

            // Text scroll offset (for horizontal scrolling)
            double scrollOffset = Paper.GetElementStorage(Element, "ScrollOffset", 0.0);
            Paper.SetElementStorage(Element, "ScrollOffset", scrollOffset);

            // Set the text content
            //Text(PaperUI.Text.Left($"  {displayText}", font, textColor));

            // Handle focus changes
            OnFocusChange((FocusEvent e) =>
            {
                Paper.SetElementStorage(Element, "IsFocused", e.IsFocused);

                // When gaining focus, place cursor at the end of text
                if (e.IsFocused)
                {
                    string currentValue = Paper.GetElementStorage<string>(Element, "Value", "");
                    int pos = currentValue.Length;
                    Paper.SetElementStorage(Element, "CursorPosition", pos);
                    Paper.SetElementStorage(Element, "SelectionStart", -1);
                    Paper.SetElementStorage(Element, "SelectionEnd", -1);

                    // Ensure cursor is visible
                    EnsureCursorVisible(currentValue, font, pos);
                }
            });

            // Handle mouse clicks for cursor positioning
            OnClick((ClickEvent e) =>
            {
                string currentValue = Paper.GetElementStorage<string>(Element, "Value", "");
                double scrollOffsetValue = Paper.GetElementStorage<double>(Element, "ScrollOffset", 0.0);

                // Calculate cursor position based on click position
                var clickPos = e.RelativePosition.x - TextXPadding + scrollOffsetValue; // Adjust for padding
                int newPosition = CalculateTextPosition(currentValue, font, clickPos);
                newPosition = Math.Clamp(newPosition, 0, currentValue.Length);

                Paper.SetElementStorage(Element, "CursorPosition", newPosition);

                // Clear selection on click
                Paper.SetElementStorage(Element, "SelectionStart", -1);
                Paper.SetElementStorage(Element, "SelectionEnd", -1);

                // Ensure cursor is visible
                EnsureCursorVisible(currentValue, font, newPosition);
            });

            // Handle dragging for text selection
            OnDragStart((DragEvent e) =>
            {
                string currentValue = Paper.GetElementStorage<string>(Element, "Value", "");
                double scrollOffsetValue = Paper.GetElementStorage<double>(Element, "ScrollOffset", 0.0);

                // Start selection at cursor position
                int pos = CalculateTextPosition(currentValue, font, e.RelativePosition.x - TextXPadding + scrollOffsetValue);
                pos = Math.Clamp(pos, 0, currentValue.Length);

                Paper.SetElementStorage(Element, "CursorPosition", pos);
                Paper.SetElementStorage(Element, "SelectionStart", pos);
                Paper.SetElementStorage(Element, "SelectionEnd", pos);

                // Ensure cursor is visible
                EnsureCursorVisible(currentValue, font, pos);
            });

            OnDragging((DragEvent e) =>
            {
                string currentValue = Paper.GetElementStorage<string>(Element, "Value", "");
                double scrollOffsetValue = Paper.GetElementStorage<double>(Element, "ScrollOffset", 0.0);

                // Update selection end while dragging
                int start = Paper.GetElementStorage<int>(Element, "SelectionStart", -1);
                if (start >= 0)
                {
                    // Auto-scroll when dragging near edges
                    double edgeScrollSensitivity = 20.0;
                    double scrollSpeed = 2.0;

                    if (e.RelativePosition.x < edgeScrollSensitivity)
                    {
                        // Scroll left
                        scrollOffsetValue = Math.Max(0, scrollOffsetValue - scrollSpeed);
                        Paper.SetElementStorage(Element, "ScrollOffset", scrollOffsetValue);
                    }
                    else if (e.RelativePosition.x > e.ElementRect.width - edgeScrollSensitivity)
                    {
                        // Scroll right
                        scrollOffsetValue += scrollSpeed;
                        Paper.SetElementStorage(Element, "ScrollOffset", scrollOffsetValue);
                    }

                    int pos = CalculateTextPosition(currentValue, font, e.RelativePosition.x - TextXPadding + scrollOffsetValue);
                    pos = Math.Clamp(pos, 0, currentValue.Length);

                    Paper.SetElementStorage(Element, "CursorPosition", pos);
                    Paper.SetElementStorage(Element, "SelectionEnd", pos);

                    // Ensure cursor is visible with updated scroll position
                    EnsureCursorVisible(currentValue, font, pos);
                }
            });

            // Handle keyboard input
            OnKeyPressed((KeyEvent e) =>
            {
                if (!isFocused) return;

                string currentValue = Paper.GetElementStorage<string>(Element, "Value", "");
                int curPos = Paper.GetElementStorage<int>(Element, "CursorPosition", 0);
                int selStart = Paper.GetElementStorage<int>(Element, "SelectionStart", -1);
                int selEnd = Paper.GetElementStorage<int>(Element, "SelectionEnd", -1);

                bool valueChanged = false;

                // Process key commands
                switch (e.Key)
                {
                    case PaperKey.Backspace:
                        if (HasSelection())
                        {
                            DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
                            valueChanged = true;
                        }
                        else if (curPos > 0)
                        {
                            currentValue = currentValue.Remove(curPos - 1, 1);
                            curPos--;
                            valueChanged = true;
                        }
                        break;

                    case PaperKey.Delete:
                        if (HasSelection())
                        {
                            DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
                            valueChanged = true;
                        }
                        else if (curPos < currentValue.Length)
                        {
                            currentValue = currentValue.Remove(curPos, 1);
                            valueChanged = true;
                        }
                        break;

                    case PaperKey.Left:
                        if (Paper.IsKeyDown(PaperKey.LeftShift) || Paper.IsKeyDown(PaperKey.RightShift))
                        {
                            // Shift+Left: extend selection
                            if (selStart < 0) selStart = curPos;
                            curPos = Math.Max(0, curPos - 1);
                            selEnd = curPos;
                        }
                        else
                        {
                            // Just move cursor
                            if (HasSelection())
                            {
                                // Move to beginning of selection
                                curPos = Math.Min(selStart, selEnd);
                                ClearSelection(ref selStart, ref selEnd);
                            }
                            else
                            {
                                curPos = Math.Max(0, curPos - 1);
                            }
                        }
                        break;

                    case PaperKey.Right:
                        if (Paper.IsKeyDown(PaperKey.LeftShift) || Paper.IsKeyDown(PaperKey.RightShift))
                        {
                            // Shift+Right: extend selection
                            if (selStart < 0) selStart = curPos;
                            curPos = Math.Min(currentValue.Length, curPos + 1);
                            selEnd = curPos;
                        }
                        else
                        {
                            // Just move cursor
                            if (HasSelection())
                            {
                                // Move to end of selection
                                curPos = Math.Max(selStart, selEnd);
                                ClearSelection(ref selStart, ref selEnd);
                            }
                            else
                            {
                                curPos = Math.Min(currentValue.Length, curPos + 1);
                            }
                        }
                        break;

                    case PaperKey.Home:
                        if (Paper.IsKeyDown(PaperKey.LeftShift) || Paper.IsKeyDown(PaperKey.RightShift))
                        {
                            if (selStart < 0) selStart = curPos;
                            curPos = 0;
                            selEnd = curPos;
                        }
                        else
                        {
                            curPos = 0;
                            ClearSelection(ref selStart, ref selEnd);
                        }
                        break;

                    case PaperKey.End:
                        if (Paper.IsKeyDown(PaperKey.LeftShift) || Paper.IsKeyDown(PaperKey.RightShift))
                        {
                            if (selStart < 0) selStart = curPos;
                            curPos = currentValue.Length;
                            selEnd = curPos;
                        }
                        else
                        {
                            curPos = currentValue.Length;
                            ClearSelection(ref selStart, ref selEnd);
                        }
                        break;

                    case PaperKey.A:
                        if (Paper.IsKeyDown(PaperKey.LeftControl) || Paper.IsKeyDown(PaperKey.RightControl))
                        {
                            // Select all
                            selStart = 0;
                            selEnd = currentValue.Length;
                            curPos = selEnd;
                        }
                        break;

                    case PaperKey.C:
                        if ((Paper.IsKeyDown(PaperKey.LeftControl) || Paper.IsKeyDown(PaperKey.RightControl)) && HasSelection())
                        {
                            // Copy selection
                            int start = Math.Min(selStart, selEnd);
                            int end = Math.Max(selStart, selEnd);
                            string selectedText = currentValue.Substring(start, end - start);
                            Paper.SetClipboard(selectedText);
                        }
                        break;

                    case PaperKey.X:
                        if ((Paper.IsKeyDown(PaperKey.LeftControl) || Paper.IsKeyDown(PaperKey.RightControl)) && HasSelection())
                        {
                            // Cut selection
                            int start = Math.Min(selStart, selEnd);
                            int end = Math.Max(selStart, selEnd);
                            string selectedText = currentValue.Substring(start, end - start);
                            Paper.SetClipboard(selectedText);
                            DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
                            valueChanged = true;
                        }
                        break;

                    case PaperKey.V:
                        if (Paper.IsKeyDown(PaperKey.LeftControl) || Paper.IsKeyDown(PaperKey.RightControl))
                        {
                            // Paste from clipboard
                            string clipText = Paper.GetClipboard();
                            if (!string.IsNullOrEmpty(clipText))
                            {
                                if (HasSelection())
                                {
                                    DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
                                }

                                currentValue = currentValue.Insert(curPos, clipText);
                                curPos += clipText.Length;
                                valueChanged = true;
                            }
                        }
                        break;
                }

                // Update stored values
                Paper.SetElementStorage(Element, "Value", currentValue);
                Paper.SetElementStorage(Element, "CursorPosition", curPos);
                Paper.SetElementStorage(Element, "SelectionStart", selStart);
                Paper.SetElementStorage(Element, "SelectionEnd", selEnd);

                // Ensure cursor is visible
                EnsureCursorVisible(currentValue, font, curPos);

                // Notify of changes if needed
                if (valueChanged && onChange != null)
                {
                    onChange(currentValue);
                }

                // Helper functions
                bool HasSelection()
                {
                    return selStart >= 0 && selEnd >= 0 && selStart != selEnd;
                }
            });

            // Handle character input
            OnTextInput((TextInputEvent e) =>
            {
                if (!isFocused) return;

                string currentValue = Paper.GetElementStorage<string>(Element, "Value", "");
                int curPos = Paper.GetElementStorage<int>(Element, "CursorPosition", 0);
                int selStart = Paper.GetElementStorage<int>(Element, "SelectionStart", -1);
                int selEnd = Paper.GetElementStorage<int>(Element, "SelectionEnd", -1);

                // If we have a selection, delete it first
                if (selStart >= 0 && selEnd >= 0 && selStart != selEnd)
                {
                    DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
                }

                // Insert the character
                if (!char.IsControl(e.Character))
                {
                    currentValue = currentValue.Insert(curPos, e.Character.ToString());
                    curPos++;

                    // Update the value
                    Paper.SetElementStorage(Element, "Value", currentValue);
                    Paper.SetElementStorage(Element, "CursorPosition", curPos);
                    Paper.SetElementStorage(Element, "SelectionStart", selStart);
                    Paper.SetElementStorage(Element, "SelectionEnd", selEnd);

                    // Ensure cursor is visible
                    EnsureCursorVisible(currentValue, font, curPos);

                    // Notify of changes
                    onChange?.Invoke(currentValue);
                }
            });

            // Render cursor and selection
            OnPostLayout((Element el, Rect rect) =>
            {
                Paper.AddActionElement(el, (canvas, r) =>
                {
                    string currentValue = Paper.GetElementStorage<string>(el, "Value", "");
                    double scrollOffsetValue = Paper.GetElementStorage<double>(el, "ScrollOffset", 0.0);

                    // Apply scroll transform to content
                    canvas.TransformBy(Transform2D.CreateTranslation(-scrollOffsetValue, 0));

                    // Draw text
                    double y = r.y + (r.height / 2);
                    if(string.IsNullOrEmpty(currentValue))
                    {
                        // Draw placeholder text
                        canvas.DrawText(font, placeholder, r.x + TextXPadding, y - font.LineHeight / 2, textColor);
                    }
                    else
                    {
                        // Draw actual text
                        canvas.DrawText(font, currentValue, r.x + TextXPadding, y - font.LineHeight / 2, textColor);
                    }

                    if (isFocused)
                    {
                        // Draw text selection if applicable
                        int selStart = Paper.GetElementStorage<int>(el, "SelectionStart", -1);
                        int selEnd = Paper.GetElementStorage<int>(el, "SelectionEnd", -1);

                        if (selStart >= 0 && selEnd >= 0 && selStart != selEnd)
                        {
                            // Ensure start < end
                            if (selStart > selEnd)
                            {
                                int temp = selStart;
                                selStart = selEnd;
                                selEnd = temp;
                            }

                            // Calculate selection rectangle
                            double startX = CalculateTextWidth(currentValue.Substring(0, selStart), font);
                            double endX = CalculateTextWidth(currentValue.Substring(0, selEnd), font);

                            // Draw selection background
                            canvas.BeginPath();
                            canvas.RoundedRect(
                                r.x + startX + TextXPadding,
                                r.y + (r.height - font.LineHeight) / 2,
                                endX - startX,
                                font.LineHeight,
                                2, 2, 2, 2);
                            canvas.SetFillColor(Color.FromArgb(100, 100, 150, 255));
                            canvas.Fill();
                        }

                        // Draw cursor if we have focus
                        int cursorPos = Paper.GetElementStorage<int>(el, "CursorPosition", 0);

                        // Only draw cursor during visible part of blink cycle
                        if ((int)(Paper.Time * 2) % 2 == 0)
                        {
                            double cursorX = r.x + TextXPadding + CalculateTextWidth(currentValue.Substring(0, cursorPos), font);
                            double cursorHeight = font.LineHeight;

                            canvas.BeginPath();
                            canvas.MoveTo(cursorX, r.y + (r.height - cursorHeight) / 2);
                            canvas.LineTo(cursorX, r.y + (r.height - cursorHeight) / 2 + cursorHeight);
                            canvas.SetStrokeColor(Color.FromArgb(255, 250, 250, 250));
                            canvas.SetStrokeWidth(1);
                            canvas.Stroke();
                        }
                    }
                });
            });

            return this;
        }

        // Helper methods for text field functionality

        /// <summary>
        /// Ensures the cursor is visible by adjusting scroll position if needed.
        /// </summary>
        private void EnsureCursorVisible(string text, SpriteFontBase font, int cursorPosition)
        {
            double scrollOffset = Paper.GetElementStorage<double>(Element, "ScrollOffset", 0.0);

            // Calculate current cursor position
            double cursorX = CalculateTextWidth(text.Substring(0, cursorPosition), font);

            // Get current visible area (estimate from last layout)
            double visibleWidth = Element.LayoutWidth - 8; // Subtract padding

            const double margin = 20.0; // Margin to keep cursor away from edge

            // If cursor is to the left of visible area
            if (cursorX < scrollOffset + margin)
            {
                // Scroll to show cursor with left margin
                scrollOffset = Math.Max(0, cursorX - margin);
            }
            // If cursor is to the right of visible area
            else if (cursorX > scrollOffset + visibleWidth - margin)
            {
                // Scroll to show cursor with right margin
                scrollOffset = cursorX - visibleWidth + margin;
            }

            // Update scroll position
            Paper.SetElementStorage(Element, "ScrollOffset", scrollOffset);
        }

        /// <summary>
        /// Calculates the closest text position based on an X coordinate in a text string.
        /// </summary>
        private static int CalculateTextPosition(string text, SpriteFontBase font, double x)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            double closestDistance = double.MaxValue;
            int closestPosition = 0;

            // Check each possible position
            for (int i = 0; i <= text.Length; i++)
            {
                double posWidth = CalculateTextWidth(text.Substring(0, i), font);
                double distance = Math.Abs(posWidth - x);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPosition = i;
                }
            }

            return closestPosition;
        }

        /// <summary>
        /// Calculates the width of a text string using the specified font.
        /// </summary>
        private static double CalculateTextWidth(string text, SpriteFontBase font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return font.MeasureString(text).X;
        }

        /// <summary>
        /// Clears the text selection.
        /// </summary>
        private static void ClearSelection(ref int selStart, ref int selEnd)
        {
            selStart = -1;
            selEnd = -1;
        }

        /// <summary>
        /// Deletes the selected text.
        /// </summary>
        private static void DeleteSelection(ref string value, ref int cursorPos, ref int selStart, ref int selEnd)
        {
            int start = Math.Min(selStart, selEnd);
            int end = Math.Max(selStart, selEnd);
            int length = end - start;

            value = value.Remove(start, length);
            cursorPos = start;

            // Clear selection
            selStart = -1;
            selEnd = -1;
        }

        #endregion

        /// <summary>
        /// Begins a new parent scope with this element as the parent.
        /// Used with 'using' statements to create a hierarchical UI structure.
        /// </summary>
        /// <returns>This builder as an IDisposable to be used with 'using' statements</returns>
        public IDisposable Enter()
        {
            var currentParent = Paper.CurrentParent;
            if (currentParent == _element)
                throw new InvalidOperationException("Cannot enter the same element twice.");

            // Push this element onto the stack
            Paper._elementStack.Push(_element);

            return this;
        }

        /// <summary>
        /// Ends the current parent scope by removing this element from the stack.
        /// Called automatically at the end of a 'using' block.
        /// </summary>
        void IDisposable.Dispose()
        {
            // Pop this element from the stack when the using block ends
            if (Paper._elementStack.Count > 1) // Don't pop the root
                Paper._elementStack.Pop();

            Paper.PopID();
        }
    }
}
