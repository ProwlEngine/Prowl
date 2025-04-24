using System.Drawing;

using Prowl.PaperUI.LayoutEngine;
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

        /// <summary>Makes the element capable of receiving focus.</summary>
        public ElementBuilder IsFocusable()
        {
            _element.IsFocusable = true;
            return this;
        }

        /// <summary>Sets a callback that runs after layout calculation is complete.</summary>
        public ElementBuilder OnPostLayout(Action<Element, Rect> handler)
        {
            _element.OnPostLayout = handler;
            return this;
        }

        /// <summary>Sets a callback that runs after layout calculation is complete, with a captured value.</summary>
        public ElementBuilder OnPostLayout<T>(T capturedValue, Action<T, Element, Rect> handler) =>
            OnPostLayout((Element element, Rect rect) => handler(capturedValue, element, rect));

        /// <summary>Sets a callback that runs when the element is pressed.</summary>
        public ElementBuilder OnPress(Action<Rect> handler)
        {
            _element.OnPress = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is pressed, with a captured value.</summary>
        public ElementBuilder OnPress<T>(T capturedValue, Action<T, Rect> handler) =>
            OnPress((rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when the element is held down.</summary>
        public ElementBuilder OnHeld(Action<Rect> handler)
        {
            _element.OnHeld = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is held down, with a captured value.</summary>
        public ElementBuilder OnHeld<T>(T capturedValue, Action<T, Rect> handler) =>
            OnHeld((rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when the element is clicked.</summary>
        public ElementBuilder OnClick(Action<Rect> handler)
        {
            _element.OnClick = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is clicked, with a captured value.</summary>
        public ElementBuilder OnClick<T>(T capturedValue, Action<T, Rect> handler) =>
            OnClick((rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when the element is dragged.</summary>
        public ElementBuilder OnDragStart(Action<Rect> handler)
        {
            _element.OnDragStart = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is dragged, with a captured value.</summary>
        public ElementBuilder OnDragStart<T>(T capturedValue, Action<T, Rect> handler) =>
            OnDragStart((Rect rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when the element is dragged.</summary>
        public ElementBuilder OnDragging(Action<Vector2, Rect> handler)
        {
            _element.OnDragging = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is dragged, with a captured value.</summary>
        public ElementBuilder OnDragging<T>(T capturedValue, Action<T, Vector2, Rect> handler) =>
            OnDragging((Vector2 start, Rect rect) => handler(capturedValue, start,  rect));

        /// <summary>Sets a callback that runs when the element is released after dragging.</summary>
        public ElementBuilder OnDragEnd(Action<Vector2, Vector2, Rect> handler)
        {
            _element.OnDragEnd = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is released after dragging, with a captured value.</summary>
        public ElementBuilder OnDragEnd<T>(T capturedValue, Action<T, Vector2, Vector2, Rect> handler) =>
            OnDragEnd((Vector2 start, Vector2 totalDelta, Rect rect) => handler(capturedValue, start, totalDelta, rect));

        /// <summary>Sets a callback that runs when the mouse button is released after clicking this element.</summary>
        public ElementBuilder OnRelease(Action<Rect> handler)
        {
            _element.OnRelease = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the mouse button is released after clicking this element, with a captured value.</summary>
        public ElementBuilder OnRelease<T>(T capturedValue, Action<T, Rect> handler) =>
            OnRelease((rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when the element is double-clicked.</summary>
        public ElementBuilder OnDoubleClick(Action<Rect> handler)
        {
            _element.OnDoubleClick = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is double-clicked, with a captured value.</summary>
        public ElementBuilder OnDoubleClick<T>(T capturedValue, Action<T, Rect> handler) =>
            OnDoubleClick((rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when the element is right-clicked.</summary>
        public ElementBuilder OnRightClick(Action<Rect> handler)
        {
            _element.OnRightClick = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is right-clicked, with a captured value.</summary>
        public ElementBuilder OnRightClick<T>(T capturedValue, Action<T, Rect> handler) =>
            OnRightClick((rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when scrolling occurs over the element.</summary>
        public ElementBuilder OnScroll(Action<double, Rect> handler)
        {
            _element.OnScroll = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when scrolling occurs over the element, with a captured value.</summary>
        public ElementBuilder OnScroll<T>(T capturedValue, Action<T, double, Rect> handler) =>
            OnScroll((double delta, Rect rect) => handler(capturedValue, delta, rect));

        /// <summary>Sets a callback that runs when the cursor hovers over the element.</summary>
        public ElementBuilder OnHover(Action<Rect> handler)
        {
            _element.OnHover = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the cursor hovers over the element, with a captured value.</summary>
        public ElementBuilder OnHover<T>(T capturedValue, Action<T, Rect> handler) =>
            OnHover((Rect rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when the cursor enters the element's bounds.</summary>
        public ElementBuilder OnEnter(Action<Rect> handler)
        {
            _element.OnEnter = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the cursor enters the element's bounds, with a captured value.</summary>
        public ElementBuilder OnEnter<T>(T capturedValue, Action<T, Rect> handler) =>
            OnEnter((rect) => handler(capturedValue, rect));

        /// <summary>Sets a callback that runs when the cursor leaves the element's bounds.</summary>
        public ElementBuilder OnLeave(Action<Rect> handler)
        {
            _element.OnLeave = handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the cursor leaves the element's bounds, with a captured value.</summary>
        public ElementBuilder OnLeave<T>(T capturedValue, Action<T, Rect> handler) =>
            OnLeave((rect) => handler(capturedValue, rect));

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
        }
    }
}
