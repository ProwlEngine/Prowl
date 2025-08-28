using System.Drawing;

using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.Utilities;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    public interface IStyleSetter<T> where T : IStyleSetter<T>
    {
        T SetStyleProperty(GuiProp property, object value);
    }

    public abstract class StyleSetterBase<T> : IStyleSetter<T> where T : StyleSetterBase<T>
    {
        public Element _element { get; protected set; }

        protected StyleSetterBase(Element element)
        {
            _element = element;
        }

        public abstract T SetStyleProperty(GuiProp property, object value);

        // Shared implementation methods

        #region Appearance Properties

        /// <summary>Sets the background color of the element.</summary>
        public T BackgroundColor(Color color) => SetStyleProperty(GuiProp.BackgroundColor, color);

        /// <summary>Sets a linear gradient background gradient.</summary>
        public T BackgroundLinearGradient(double x1, double y1, double x2, double y2, Color color1, Color color2) =>
            SetStyleProperty(GuiProp.BackgroundGradient, Gradient.Linear(x1, y1, x2, y2, color1, color2));

        /// <summary>Sets a radial gradient background gradient.</summary>
        public T BackgroundRadialGradient(double centerX, double centerY, double innerRadius, double outerRadius, Color innerColor, Color outerColor) =>
            SetStyleProperty(GuiProp.BackgroundGradient, Gradient.Radial(centerX, centerY, innerRadius, outerRadius, innerColor, outerColor));

        /// <summary>Sets a box gradient background gradient.</summary>
        public T BackgroundBoxGradient(double centerX, double centerY, double width, double height, float radius, float feather, Color innerColor, Color outerColor) =>
            SetStyleProperty(GuiProp.BackgroundGradient, Gradient.Box(centerX, centerY, width, height, radius, feather, innerColor, outerColor));

        /// <summary>Clears any background gradient on the element.</summary>
        public T ClearBackgroundGradient() => SetStyleProperty(GuiProp.BackgroundGradient, Gradient.None);

        /// <summary>Sets the border color of the element.</summary>
        public T BorderColor(Color color) => SetStyleProperty(GuiProp.BorderColor, color);

        /// <summary>Sets the border width of the element.</summary>
        public T BorderWidth(double width) => SetStyleProperty(GuiProp.BorderWidth, width);

        /// <summary>Sets a box shadow on the element.</summary>
        public T BoxShadow(double offsetX, double offsetY, double blur, double spread, Color color) =>
            SetStyleProperty(GuiProp.BoxShadow, new BoxShadow(offsetX, offsetY, blur, spread, color));

        /// <summary>Sets a box shadow on the element.</summary>
        public T BoxShadow(BoxShadow shadow) => SetStyleProperty(GuiProp.BoxShadow, shadow);

        #endregion

        #region Corner Rounding

        /// <summary>Rounds the top corners of the element.</summary>
        public T RoundedTop(double radius) => SetStyleProperty(GuiProp.Rounded, new Vector4(radius, radius, 0, 0));

        /// <summary>Rounds the bottom corners of the element.</summary>
        public T RoundedBottom(double radius) => SetStyleProperty(GuiProp.Rounded, new Vector4(0, 0, radius, radius));

        /// <summary>Rounds the left corners of the element.</summary>
        public T RoundedLeft(double radius) => SetStyleProperty(GuiProp.Rounded, new Vector4(radius, 0, 0, radius));

        /// <summary>Rounds the right corners of the element.</summary>
        public T RoundedRight(double radius) => SetStyleProperty(GuiProp.Rounded, new Vector4(0, radius, radius, 0));

        /// <summary>Rounds all corners of the element with the same radius.</summary>
        public T Rounded(double radius) => SetStyleProperty(GuiProp.Rounded, new Vector4(radius, radius, radius, radius));

        /// <summary>Rounds each corner of the element with individual radii.</summary>
        /// <param name="tlRadius">Top-left radius</param>
        /// <param name="trRadius">Top-right radius</param>
        /// <param name="brRadius">Bottom-right radius</param>
        /// <param name="blRadius">Bottom-left radius</param>
        public T Rounded(double tlRadius, double trRadius, double brRadius, double blRadius) =>
            SetStyleProperty(GuiProp.Rounded, new Vector4(tlRadius, trRadius, brRadius, blRadius));

        #endregion

        #region Layout Properties

        /// <summary>Sets the aspect ratio (width/height) of the element.</summary>
        public T AspectRatio(double ratio) => SetStyleProperty(GuiProp.AspectRatio, ratio);

        /// <summary>Sets both width and height to the same value.</summary>
        public T Size(UnitValue sizeUniform) => Size(sizeUniform, sizeUniform);

        /// <summary>Sets the width and height of the element.</summary>
        public T Size(UnitValue width, UnitValue height)
        {
            SetStyleProperty(GuiProp.Width, width);
            return SetStyleProperty(GuiProp.Height, height);
        }

        /// <summary>Sets the width of the element.</summary>
        public T Width(UnitValue width) => SetStyleProperty(GuiProp.Width, width);

        /// <summary>Sets the height of the element.</summary>
        public T Height(UnitValue height) => SetStyleProperty(GuiProp.Height, height);

        /// <summary>Sets the minimum width of the element.</summary>
        public T MinWidth(UnitValue minWidth) => SetStyleProperty(GuiProp.MinWidth, minWidth);

        /// <summary>Sets the maximum width of the element.</summary>
        public T MaxWidth(UnitValue maxWidth) => SetStyleProperty(GuiProp.MaxWidth, maxWidth);

        /// <summary>Sets the minimum height of the element.</summary>
        public T MinHeight(UnitValue minHeight) => SetStyleProperty(GuiProp.MinHeight, minHeight);

        /// <summary>Sets the maximum height of the element.</summary>
        public T MaxHeight(UnitValue maxHeight) => SetStyleProperty(GuiProp.MaxHeight, maxHeight);

        /// <summary>Sets the position of the element from the left and top edges.</summary>
        public T Position(UnitValue left, UnitValue top)
        {
            SetStyleProperty(GuiProp.Left, left);
            return SetStyleProperty(GuiProp.Top, top);
        }

        /// <summary>Sets the left position of the element.</summary>
        public T Left(UnitValue left) => SetStyleProperty(GuiProp.Left, left);

        /// <summary>Sets the right position of the element.</summary>
        public T Right(UnitValue right) => SetStyleProperty(GuiProp.Right, right);

        /// <summary>Sets the top position of the element.</summary>
        public T Top(UnitValue top) => SetStyleProperty(GuiProp.Top, top);

        /// <summary>Sets the bottom position of the element.</summary>
        public T Bottom(UnitValue bottom) => SetStyleProperty(GuiProp.Bottom, bottom);

        /// <summary>Sets the minimum left position of the element.</summary>
        public T MinLeft(UnitValue minLeft) => SetStyleProperty(GuiProp.MinLeft, minLeft);

        /// <summary>Sets the maximum left position of the element.</summary>
        public T MaxLeft(UnitValue maxLeft) => SetStyleProperty(GuiProp.MaxLeft, maxLeft);

        /// <summary>Sets the minimum right position of the element.</summary>
        public T MinRight(UnitValue minRight) => SetStyleProperty(GuiProp.MinRight, minRight);

        /// <summary>Sets the maximum right position of the element.</summary>
        public T MaxRight(UnitValue maxRight) => SetStyleProperty(GuiProp.MaxRight, maxRight);

        /// <summary>Sets the minimum top position of the element.</summary>
        public T MinTop(UnitValue minTop) => SetStyleProperty(GuiProp.MinTop, minTop);

        /// <summary>Sets the maximum top position of the element.</summary>
        public T MaxTop(UnitValue maxTop) => SetStyleProperty(GuiProp.MaxTop, maxTop);

        /// <summary>Sets the minimum bottom position of the element.</summary>
        public T MinBottom(UnitValue minBottom) => SetStyleProperty(GuiProp.MinBottom, minBottom);

        /// <summary>Sets the maximum bottom position of the element.</summary>
        public T MaxBottom(UnitValue maxBottom) => SetStyleProperty(GuiProp.MaxBottom, maxBottom);

        /// <summary>Sets uniform margin on all sides.</summary>
        public T Margin(UnitValue all) => Margin(all, all, all, all);

        /// <summary>Sets horizontal and vertical margins.</summary>
        public T Margin(UnitValue horizontal, UnitValue vertical) =>
            Margin(horizontal, horizontal, vertical, vertical);

        /// <summary>Sets individual margins for each side.</summary>
        public T Margin(UnitValue left, UnitValue right, UnitValue top, UnitValue bottom)
        {
            SetStyleProperty(GuiProp.Left, left);
            SetStyleProperty(GuiProp.Right, right);
            SetStyleProperty(GuiProp.Top, top);
            return SetStyleProperty(GuiProp.Bottom, bottom);
        }

        /// <summary>Sets the left padding for child elements.</summary>
        public T ChildLeft(UnitValue childLeft) => SetStyleProperty(GuiProp.ChildLeft, childLeft);

        /// <summary>Sets the right padding for child elements.</summary>
        public T ChildRight(UnitValue childRight) => SetStyleProperty(GuiProp.ChildRight, childRight);

        /// <summary>Sets the top padding for child elements.</summary>
        public T ChildTop(UnitValue childTop) => SetStyleProperty(GuiProp.ChildTop, childTop);

        /// <summary>Sets the bottom padding for child elements.</summary>
        public T ChildBottom(UnitValue childBottom) => SetStyleProperty(GuiProp.ChildBottom, childBottom);

        /// <summary>Sets the spacing between rows in a container.</summary>
        public T RowBetween(UnitValue rowBetween) => SetStyleProperty(GuiProp.RowBetween, rowBetween);

        /// <summary>Sets the spacing between columns in a container.</summary>
        public T ColBetween(UnitValue colBetween) => SetStyleProperty(GuiProp.ColBetween, colBetween);

        /// <summary>Sets the left border width.</summary>
        public T BorderLeft(UnitValue borderLeft) => SetStyleProperty(GuiProp.BorderLeft, borderLeft);

        /// <summary>Sets the right border width.</summary>
        public T BorderRight(UnitValue borderRight) => SetStyleProperty(GuiProp.BorderRight, borderRight);

        /// <summary>Sets the top border width.</summary>
        public T BorderTop(UnitValue borderTop) => SetStyleProperty(GuiProp.BorderTop, borderTop);

        /// <summary>Sets the bottom border width.</summary>
        public T BorderBottom(UnitValue borderBottom) => SetStyleProperty(GuiProp.BorderBottom, borderBottom);

        /// <summary>Sets uniform border width on all sides.</summary>
        public T Border(UnitValue all) => Border(all, all, all, all);

        /// <summary>Sets horizontal and vertical border widths.</summary>
        public T Border(UnitValue horizontal, UnitValue vertical) =>
            Border(horizontal, horizontal, vertical, vertical);

        /// <summary>Sets individual border widths for each side.</summary>
        public T Border(UnitValue left, UnitValue right, UnitValue top, UnitValue bottom)
        {
            SetStyleProperty(GuiProp.BorderLeft, left);
            SetStyleProperty(GuiProp.BorderRight, right);
            SetStyleProperty(GuiProp.BorderTop, top);
            return SetStyleProperty(GuiProp.BorderBottom, bottom);
        }

        #endregion

        #region Text Properties

        /// <summary>Sets the color of text.</summary>
        public T TextColor(Color color) => SetStyleProperty(GuiProp.TextColor, color);

        /// <summary>Sets the spacing between words in text.</summary>
        public T WordSpacing(float spacing) => SetStyleProperty(GuiProp.WordSpacing, spacing);
        /// <summary>Sets the spacing between letters in text.</summary>
        public T LetterSpacing(float spacing) => SetStyleProperty(GuiProp.LetterSpacing, spacing);
        /// <summary>Sets the height of a line in text.</summary>
        public T LineHeight(float height) => SetStyleProperty(GuiProp.LineHeight, height);

        /// <summary>Sets the size of a Tab character in spaces.</summary>
        public T TabSize(int size) => SetStyleProperty(GuiProp.TabSize, size);
        /// <summary>Sets the size of text in pixels.</summary>
        public T FontSize(float size) => SetStyleProperty(GuiProp.FontSize, size);

        #endregion

        #region Transform Properties

        /// <summary>Sets horizontal translation.</summary>
        public T TranslateX(double x) => SetStyleProperty(GuiProp.TranslateX, x);

        /// <summary>Sets vertical translation.</summary>
        public T TranslateY(double y) => SetStyleProperty(GuiProp.TranslateY, y);

        /// <summary>Sets both horizontal and vertical translation.</summary>
        public T Translate(double x, double y)
        {
            SetStyleProperty(GuiProp.TranslateX, x);
            return SetStyleProperty(GuiProp.TranslateY, y);
        }

        /// <summary>Sets horizontal scaling factor.</summary>
        public T ScaleX(double x) => SetStyleProperty(GuiProp.ScaleX, x);

        /// <summary>Sets vertical scaling factor.</summary>
        public T ScaleY(double y) => SetStyleProperty(GuiProp.ScaleY, y);

        /// <summary>Sets uniform scaling in both directions.</summary>
        public T Scale(double scale) => Scale(scale, scale);

        /// <summary>Sets individual scaling factors for each axis.</summary>
        public T Scale(double x, double y)
        {
            SetStyleProperty(GuiProp.ScaleX, x);
            return SetStyleProperty(GuiProp.ScaleY, y);
        }

        /// <summary>Sets rotation angle in degrees.</summary>
        public T Rotate(double angleInDegrees) => SetStyleProperty(GuiProp.Rotate, angleInDegrees);

        /// <summary>Sets horizontal skew angle.</summary>
        public T SkewX(double angle) => SetStyleProperty(GuiProp.SkewX, angle);

        /// <summary>Sets vertical skew angle.</summary>
        public T SkewY(double angle) => SetStyleProperty(GuiProp.SkewY, angle);

        /// <summary>Sets both horizontal and vertical skew angles.</summary>
        public T Skew(double x, double y)
        {
            SetStyleProperty(GuiProp.SkewX, x);
            return SetStyleProperty(GuiProp.SkewY, y);
        }

        /// <summary>Sets the origin point for transformations.</summary>
        public T TransformOrigin(double x, double y)
        {
            SetStyleProperty(GuiProp.OriginX, x);
            return SetStyleProperty(GuiProp.OriginY, y);
        }

        /// <summary>Sets a complete transform matrix.</summary>
        public T Transform(Transform2D transform) => SetStyleProperty(GuiProp.Transform, transform);

        #endregion

        #region Transition Properties

        /// <summary>
        /// Configures a property transition with the specified duration and easing function.
        /// </summary>
        /// <param name="property">The property to animate</param>
        /// <param name="duration">Animation duration in seconds</param>
        /// <param name="easing">Optional easing function</param>
        public T Transition(GuiProp property, double duration, Func<double, double> easing = null)
        {
            return SetTransition(property, duration, easing);
        }

        /// <summary>
        /// Abstract method to handle transition setting - implemented by derived classes
        /// </summary>
        protected abstract T SetTransition(GuiProp property, double duration, Func<double, double> easing);

        #endregion
    }

    /// <summary>
    /// Represents a reference to a style state that can be conditionally applied.
    /// Provides a fluent API for setting style properties based on element state conditions.
    /// </summary>
    public class StateDrivenStyle : StyleSetterBase<StateDrivenStyle>
    {
        private ElementBuilder _owner;
        private bool _isActive;
        private static readonly ObjectPool<StateDrivenStyle> _pool = new ObjectPool<StateDrivenStyle>(() => new StateDrivenStyle());

        // Private constructor for the pool
        private StateDrivenStyle() : base(null)
        {
            _owner = null;
            _isActive = false;
        }

        // Constructor for direct creation (used internally)
        private StateDrivenStyle(ElementBuilder owner, bool isActive) : base(owner._element)
        {
            _owner = owner;
            _isActive = isActive;
        }

        /// <summary>
        /// Gets a StateDrivenStyle from the pool
        /// </summary>
        internal static StateDrivenStyle Get(ElementBuilder owner, bool isActive)
        {
            var style = _pool.Get();
            style.Initialize(owner, isActive);
            return style;
        }

        /// <summary>
        /// Initializes a pooled StateDrivenStyle with new values
        /// </summary>
        private void Initialize(ElementBuilder owner, bool isActive)
        {
            // Use reflection or another method to set base.Element
            _element = owner._element;

            // Set fields with new values
            _owner = owner;
            _isActive = isActive;
        }

        public StateDrivenStyle Style(StyleTemplate style)
        {
            if (_isActive)
                style.ApplyTo(_element);

            return this;
        }

        public StateDrivenStyle Style(params string[] names)
        {
            if (_isActive)
                foreach (var styleName in names)
                    _owner._paper.ApplyStyleWithStates(_element, styleName);

            return this;
        }

        public StateDrivenStyle StyleIf(bool condition, params string[] names)
        {
            if (condition)
            {
                foreach(var styleName in names)
                    _owner._paper.ApplyStyleWithStates(_element, styleName);
            }
            return this;
        }

        public override StateDrivenStyle SetStyleProperty(GuiProp property, object value)
        {
            if (_isActive)
                _owner._paper.SetStyleProperty(_element.ID, property, value);
            return this;
        }

        /// <summary>
        /// Configures a property transition with the specified duration and easing function.
        /// </summary>
        /// <param name="property">The property to animate</param>
        /// <param name="duration">Animation duration in seconds</param>
        /// <param name="easing">Optional easing function</param>
        protected override StateDrivenStyle SetTransition(GuiProp property, double duration, Func<double, double> easing)
        {
            if (_isActive)
                _owner._paper.SetTransitionConfig(_element.ID, property, duration, easing);
            return this;
        }

        /// <summary>
        /// Returns to the element builder to continue the building chain and
        /// returns this object to the pool
        /// </summary>
        public ElementBuilder End()
        {
            var owner = _owner;
            _pool.Return(this);
            return owner;
        }
    }

    /// <summary>
    /// A template that can store and apply a collection of style properties
    /// </summary>
    public class StyleTemplate : StyleSetterBase<StyleTemplate>
    {
        private readonly Dictionary<GuiProp, object> _styleProperties = new Dictionary<GuiProp, object>();
        private readonly Dictionary<GuiProp, (double duration, Func<double, double> easing)> _transitions = new Dictionary<GuiProp, (double, Func<double, double>)>();

        /// <summary>
        /// Creates a new style template
        /// </summary>
        public StyleTemplate() : base(null) { }

        /// <summary>
        /// Sets a style property in the template
        /// </summary>
        public override StyleTemplate SetStyleProperty(GuiProp property, object value)
        {
            _styleProperties[property] = value;
            return this;
        }

        /// <summary>
        /// Configures a property transition with the specified duration and easing function.
        /// </summary>
        /// <param name="property">The property to animate</param>
        /// <param name="duration">Animation duration in seconds</param>
        /// <param name="easing">Optional easing function</param>
        protected override StyleTemplate SetTransition(GuiProp property, double duration, Func<double, double> easing)
        {
            _transitions[property] = (duration, easing);
            return this;
        }

        /// <summary>
        /// Applies all style properties in this template to an element
        /// </summary>
        /// <param name="element">The element to apply styles to</param>
        public void ApplyTo(Element element)
        {
            if (element.Owner == null) throw new ArgumentNullException(nameof(element));
            foreach (var kvp in _styleProperties)
            {
                element.Owner!.SetStyleProperty(element.ID, kvp.Key, kvp.Value);
            }

            // Apply transitions
            foreach (var kvp in _transitions)
            {
                element.Owner!.SetTransitionConfig(element.ID, kvp.Key, kvp.Value.duration, kvp.Value.easing);
            }
        }

        /// <summary>
        /// Applies a template to another template
        /// </summary>
        public StyleTemplate ApplyTo(StyleTemplate other)
        {
            foreach (var kvp in _styleProperties)
            {
                other.SetStyleProperty(kvp.Key, kvp.Value);
            }

            // Apply transitions
            foreach (var kvp in _transitions)
            {
                other.SetTransition(kvp.Key, kvp.Value.duration, kvp.Value.easing);
            }
            return other;
        }

        /// <summary>
        /// Creates a copy of this template
        /// </summary>
        public StyleTemplate Clone()
        {
            var clone = new StyleTemplate();
            foreach (var kvp in _styleProperties)
            {
                clone._styleProperties[kvp.Key] = kvp.Value;
            }

            // Clone transitions
            foreach (var kvp in _transitions)
            {
                clone._transitions[kvp.Key] = kvp.Value;
            }
            return clone;
        }
    }

    /// <summary>
    /// Provides a fluent API for building and configuring UI elements.
    /// Implements IDisposable to support hierarchical element creation using 'using' blocks.
    /// </summary>
    public class ElementBuilder : StyleSetterBase<ElementBuilder>, IDisposable
    {
        internal Paper _paper;

        /// <summary>Style properties that are always applied.</summary>
        public StateDrivenStyle Normal => StateDrivenStyle.Get(this, true);

        /// <summary>Style properties applied when the element is hovered.</summary>
        public StateDrivenStyle Hovered => StateDrivenStyle.Get(this, _paper.IsElementHovered(_element.ID));

        /// <summary>Style properties applied when the element is active (pressed).</summary>
        public StateDrivenStyle Active => StateDrivenStyle.Get(this, _paper.IsElementActive(_element.ID));

        /// <summary>Style properties applied when the element has focus.</summary>
        public StateDrivenStyle Focused => StateDrivenStyle.Get(this, _paper.IsElementFocused(_element.ID));

        public ElementBuilder(Paper paper, ulong storageHash) : base(new Element { Owner = paper, ID = storageHash })
        {
            _paper = paper;
        }

        public override ElementBuilder SetStyleProperty(GuiProp property, object value)
        {
            _paper.SetStyleProperty(_element.ID, property, value);
            return this;
        }

        /// <summary>
        /// Configures a property transition with the specified duration and easing function.
        /// </summary>
        /// <param name="property">The property to animate</param>
        /// <param name="duration">Animation duration in seconds</param>
        /// <param name="easing">Optional easing function</param>
        protected override ElementBuilder SetTransition(GuiProp property, double duration, Func<double, double> easing)
        {
            _paper.SetTransitionConfig(_element.ID, property, duration, easing);
            return this;
        }

        /// <summary>
        /// Creates a conditional style state that only applies if the condition is true.
        /// </summary>
        /// <param name="condition">Boolean condition to evaluate</param>
        public StateDrivenStyle If(bool condition) => StateDrivenStyle.Get(this, condition);

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

        public ElementBuilder Style(StyleTemplate style)
        {
            style.ApplyTo(_element);
            return this;
        }

        public ElementBuilder Style(params string[] names)
        {
            foreach (var name in names)
                _paper.ApplyStyleWithStates(_element, name);

            return this;
        }

        public ElementBuilder StyleIf(bool condition, params string[] names)
        {
            if(condition)
                foreach (var name in names)
                    _paper.ApplyStyleWithStates(_element, name);
            return this;
        }

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

        /// <summary>Makes any event on this element not trigger any parent events.</summary>
        public ElementBuilder StopEventPropagation()
        {
            _element.StopPropagation = true;
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

        /// <summary>Enables content clipping to the element's bounds.</summary>
        public ElementBuilder Clip()
        {
            _element._scissorEnabled = true;
            return this;
        }

        /// <summary>Places the element on a specific rendering layer.</summary>
        /// <param name="layer">Layer on which the element should be rendered.</param>
        public ElementBuilder Layer(Layer layer)
        {
            _element.Layer = layer;
            return this;
        }

        /// <summary>Sets the text content of the element.</summary>
        /// <param name="text">The text to display</param>
        /// <param name="useMarkdown">Whether to parse the text as Markdown</param>
        public ElementBuilder Text(string text, bool useMarkdown = false)
        {
            _element.IsMarkdown = useMarkdown;
            _element.Paragraph = text;
            return this;
        }

        /// <summary>
        /// Sets the text Alignment mode of the element.
        /// </summary>
        /// <param name="mode">The Text Alignment mode to apply</param>
        public ElementBuilder Alignment(TextAlignment mode)
        {
            _element.TextAlignment = mode;
            return this;
        }

        /// <summary>
        /// Sets the text wrapping mode of the element.
        /// </summary>
        /// <param name="mode">The text wrapping mode to apply</param>
        public ElementBuilder Wrap(TextWrapMode mode)
        {
            _element.WrapMode = mode;
            return this;
        }

        /// <summary> Sets the font family and style for the element's text. </summary>
        /// <param name="family">The font family name (null for default)</param>
        /// <param name="style">The font style to apply</param>
        /// <param name="monoFamily">A mono font family, used in Markdown as the Mono Font</param>
        public ElementBuilder Font(string? family, FontStyle style, string monoFamily)
        {
            _element.FontFamily = family;
            _element.FontStyle = style;
            _element.FontMonoFamily = monoFamily;
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
                if (!_paper.HasElementStorage(_element, "ScrollState"))
                {
                    _paper.SetElementStorage(_element, "ScrollState", new ScrollState());
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
            var state = _paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());
            state.Position = position;
            _paper.SetElementStorage(_element, "ScrollState", state);
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
                var state = _paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

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

                _paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle hover events to detect when cursor is over scrollbars
            OnHover((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());
                Vector2 mousePos = _paper.PointerPos;

                // Check if pointer is over scrollbars
                state.IsVerticalScrollbarHovered = state.IsPointOverVerticalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);
                state.IsHorizontalScrollbarHovered = state.IsPointOverHorizontalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);

                _paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle mouse leaving the element
            OnLeave((rect) => {
                var state = _paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());
                state.IsVerticalScrollbarHovered = false;
                state.IsHorizontalScrollbarHovered = false;
                _paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle scrolling with mouse wheel
            OnScroll((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

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
                _paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle start scrollbar drag
            OnDragStart((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(_element.ScrollFlags)) return;

                Vector2 mousePos = _paper.PointerPos;

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

                _paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle dragging scrollbars
            OnDragging((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(_element.ScrollFlags)) return;

                Vector2 mousePos = _paper.PointerPos;

                // Handle scrollbar dragging
                if (state.IsDraggingVertical)
                {
                    state.HandleVerticalScrollbarDrag(mousePos, e.ElementRect, _element.ScrollFlags);
                }
                else if (state.IsDraggingHorizontal)
                {
                    state.HandleHorizontalScrollbarDrag(mousePos, e.ElementRect, _element.ScrollFlags);
                }

                _paper.SetElementStorage(_element, "ScrollState", state);
            });

            // Handle after dragging
            OnDragEnd((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_element, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(_element.ScrollFlags)) return;

                state.IsDraggingVertical = false;
                state.IsDraggingHorizontal = false;

                // Update hover state on release
                Vector2 mousePos = _paper.PointerPos;
                state.IsVerticalScrollbarHovered = state.IsPointOverVerticalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);
                state.IsHorizontalScrollbarHovered = state.IsPointOverHorizontalScrollbar(mousePos, e.ElementRect, _element.ScrollFlags);

                _paper.SetElementStorage(_element, "ScrollState", state);
            });
        }

        #endregion

        //#region Text Field
        //
        ///// <summary>
        ///// Creates a text field control that allows users to input and edit text.
        ///// </summary>
        ///// <param name="value">Current text value</param>
        ///// <param name="font">Font used to render the text</param>
        ///// <param name="onChange">Optional callback when the text changes</param>
        ///// <param name="placeholder">Optional placeholder text shown when the field is empty</param>
        ///// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
        ///// <returns>A builder for configuring the text field</returns>
        //public ElementBuilder TextField(
        //    string value,
        //    FontInfo? font = null,
        //    Action<string> onChange = null,
        //    Color? textColor = null,
        //    string placeholder = "",
        //    Color? placeholderColor = null,
        //    [System.Runtime.CompilerServices.CallerLineNumber] int intID = 0)
        //{
        //    const double TextXPadding = 12;
        //
        //    value = value ?? "";
        //
        //    Clip();
        //
        //    // Store the current value in element storage
        //    _paper.SetElementStorage(_element, "Value", value);
        //
        //    // Get the text to display (value or placeholder)
        //    bool isEmpty = string.IsNullOrEmpty(value);
        //    string displayText = isEmpty ? placeholder : value;
        //    textColor ??= Color.FromArgb(255, 250, 250, 250);
        //    placeholderColor ??= Color.FromArgb(160, 200, 200, 200);
        //    Color tColor = isEmpty ? placeholderColor.Value : textColor.Value;
        //
        //    // Store focus state
        //    bool isFocused = _paper.IsElementFocused(_element.ID);
        //    _paper.SetElementStorage(_element, "IsFocused", isFocused);
        //
        //    // Create a blinking cursor position tracker
        //    int cursorPosition = _paper.GetElementStorage(_element, "CursorPosition", value.Length);
        //    // Clamp cursor position to valid range
        //    cursorPosition = Math.Clamp(cursorPosition, 0, value.Length);
        //    _paper.SetElementStorage(_element, "CursorPosition", cursorPosition);
        //
        //    // Selection range (if text is selected)
        //    int selectionStart = _paper.GetElementStorage(_element, "SelectionStart", -1);
        //    int selectionEnd = _paper.GetElementStorage(_element, "SelectionEnd", -1);
        //    // Clamp selection range to valid range
        //    selectionStart = Math.Clamp(selectionStart, 0, value.Length);
        //    selectionEnd = Math.Clamp(selectionEnd, 0, value.Length);
        //    _paper.SetElementStorage(_element, "SelectionStart", selectionStart);
        //    _paper.SetElementStorage(_element, "SelectionEnd", selectionEnd);
        //
        //    // Text scroll offset (for horizontal scrolling)
        //    double scrollOffset = _paper.GetElementStorage(_element, "ScrollOffset", 0.0);
        //    _paper.SetElementStorage(_element, "ScrollOffset", scrollOffset);
        //
        //    // Set the text content
        //    //Text(PaperUI.Text.Left($"  {displayText}", font, textColor));
        //
        //    // Handle focus changes
        //    OnFocusChange((FocusEvent e) =>
        //    {
        //        _paper.SetElementStorage(_element, "IsFocused", e.IsFocused);
        //
        //        // When gaining focus, place cursor at the end of text
        //        if (e.IsFocused)
        //        {
        //            string currentValue = _paper.GetElementStorage<string>(_element, "Value", "");
        //            int pos = currentValue.Length;
        //            _paper.SetElementStorage(_element, "CursorPosition", pos);
        //            _paper.SetElementStorage(_element, "SelectionStart", -1);
        //            _paper.SetElementStorage(_element, "SelectionEnd", -1);
        //
        //            // Ensure cursor is visible
        //            EnsureCursorVisible(currentValue, font, pos);
        //        }
        //    });
        //
        //    // Handle mouse clicks for cursor positioning
        //    OnClick((ClickEvent e) =>
        //    {
        //        string currentValue = _paper.GetElementStorage<string>(_element, "Value", "");
        //        double scrollOffsetValue = _paper.GetElementStorage<double>(_element, "ScrollOffset", 0.0);
        //
        //        // Calculate cursor position based on click position
        //        var clickPos = e.RelativePosition.x - TextXPadding + scrollOffsetValue; // Adjust for padding
        //        int newPosition = CalculateTextPosition(currentValue, font, clickPos);
        //        newPosition = Math.Clamp(newPosition, 0, currentValue.Length);
        //
        //        _paper.SetElementStorage(_element, "CursorPosition", newPosition);
        //
        //        // Clear selection on click
        //        _paper.SetElementStorage(_element, "SelectionStart", -1);
        //        _paper.SetElementStorage(_element, "SelectionEnd", -1);
        //
        //        // Ensure cursor is visible
        //        EnsureCursorVisible(currentValue, font, newPosition);
        //    });
        //
        //    // Handle dragging for text selection
        //    OnDragStart((DragEvent e) =>
        //    {
        //        string currentValue = _paper.GetElementStorage<string>(_element, "Value", "");
        //        double scrollOffsetValue = _paper.GetElementStorage<double>(_element, "ScrollOffset", 0.0);
        //
        //        // Start selection at cursor position
        //        int pos = CalculateTextPosition(currentValue, font, e.RelativePosition.x - TextXPadding + scrollOffsetValue);
        //        pos = Math.Clamp(pos, 0, currentValue.Length);
        //
        //        _paper.SetElementStorage(_element, "CursorPosition", pos);
        //        _paper.SetElementStorage(_element, "SelectionStart", pos);
        //        _paper.SetElementStorage(_element, "SelectionEnd", pos);
        //
        //        // Ensure cursor is visible
        //        EnsureCursorVisible(currentValue, font, pos);
        //    });
        //
        //    OnDragging((DragEvent e) =>
        //    {
        //        string currentValue = _paper.GetElementStorage<string>(_element, "Value", "");
        //        double scrollOffsetValue = _paper.GetElementStorage<double>(_element, "ScrollOffset", 0.0);
        //
        //        // Update selection end while dragging
        //        int start = _paper.GetElementStorage<int>(_element, "SelectionStart", -1);
        //        if (start >= 0)
        //        {
        //            // Auto-scroll when dragging near edges
        //            double edgeScrollSensitivity = 20.0;
        //            double scrollSpeed = 2.0;
        //
        //            if (e.RelativePosition.x < edgeScrollSensitivity)
        //            {
        //                // Scroll left
        //                scrollOffsetValue = Math.Max(0, scrollOffsetValue - scrollSpeed);
        //                _paper.SetElementStorage(_element, "ScrollOffset", scrollOffsetValue);
        //            }
        //            else if (e.RelativePosition.x > e.ElementRect.width - edgeScrollSensitivity)
        //            {
        //                // Scroll right
        //                scrollOffsetValue += scrollSpeed;
        //                _paper.SetElementStorage(_element, "ScrollOffset", scrollOffsetValue);
        //            }
        //
        //            int pos = CalculateTextPosition(currentValue, font, e.RelativePosition.x - TextXPadding + scrollOffsetValue);
        //            pos = Math.Clamp(pos, 0, currentValue.Length);
        //
        //            _paper.SetElementStorage(_element, "CursorPosition", pos);
        //            _paper.SetElementStorage(_element, "SelectionEnd", pos);
        //
        //            // Ensure cursor is visible with updated scroll position
        //            EnsureCursorVisible(currentValue, font, pos);
        //        }
        //    });
        //
        //    // Handle keyboard input
        //    OnKeyPressed((KeyEvent e) =>
        //    {
        //        if (!isFocused) return;
        //
        //        string currentValue = _paper.GetElementStorage<string>(_element, "Value", "");
        //        int curPos = _paper.GetElementStorage<int>(_element, "CursorPosition", 0);
        //        int selStart = _paper.GetElementStorage<int>(_element, "SelectionStart", -1);
        //        int selEnd = _paper.GetElementStorage<int>(_element, "SelectionEnd", -1);
        //
        //        bool valueChanged = false;
        //
        //        // Process key commands
        //        switch (e.Key)
        //        {
        //            case PaperKey.Backspace:
        //                if (HasSelection())
        //                {
        //                    DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
        //                    valueChanged = true;
        //                }
        //                else if (curPos > 0)
        //                {
        //                    currentValue = currentValue.Remove(curPos - 1, 1);
        //                    curPos--;
        //                    valueChanged = true;
        //                }
        //                break;
        //
        //            case PaperKey.Delete:
        //                if (HasSelection())
        //                {
        //                    DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
        //                    valueChanged = true;
        //                }
        //                else if (curPos < currentValue.Length)
        //                {
        //                    currentValue = currentValue.Remove(curPos, 1);
        //                    valueChanged = true;
        //                }
        //                break;
        //
        //            case PaperKey.Left:
        //                if (_paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift))
        //                {
        //                    // Shift+Left: extend selection
        //                    if (selStart < 0) selStart = curPos;
        //                    curPos = Math.Max(0, curPos - 1);
        //                    selEnd = curPos;
        //                }
        //                else
        //                {
        //                    // Just move cursor
        //                    if (HasSelection())
        //                    {
        //                        // Move to beginning of selection
        //                        curPos = Math.Min(selStart, selEnd);
        //                        ClearSelection(ref selStart, ref selEnd);
        //                    }
        //                    else
        //                    {
        //                        curPos = Math.Max(0, curPos - 1);
        //                    }
        //                }
        //                break;
        //
        //            case PaperKey.Right:
        //                if (_paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift))
        //                {
        //                    // Shift+Right: extend selection
        //                    if (selStart < 0) selStart = curPos;
        //                    curPos = Math.Min(currentValue.Length, curPos + 1);
        //                    selEnd = curPos;
        //                }
        //                else
        //                {
        //                    // Just move cursor
        //                    if (HasSelection())
        //                    {
        //                        // Move to end of selection
        //                        curPos = Math.Max(selStart, selEnd);
        //                        ClearSelection(ref selStart, ref selEnd);
        //                    }
        //                    else
        //                    {
        //                        curPos = Math.Min(currentValue.Length, curPos + 1);
        //                    }
        //                }
        //                break;
        //
        //            case PaperKey.Home:
        //                if (_paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift))
        //                {
        //                    if (selStart < 0) selStart = curPos;
        //                    curPos = 0;
        //                    selEnd = curPos;
        //                }
        //                else
        //                {
        //                    curPos = 0;
        //                    ClearSelection(ref selStart, ref selEnd);
        //                }
        //                break;
        //
        //            case PaperKey.End:
        //                if (_paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift))
        //                {
        //                    if (selStart < 0) selStart = curPos;
        //                    curPos = currentValue.Length;
        //                    selEnd = curPos;
        //                }
        //                else
        //                {
        //                    curPos = currentValue.Length;
        //                    ClearSelection(ref selStart, ref selEnd);
        //                }
        //                break;
        //
        //            case PaperKey.A:
        //                if (_paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl))
        //                {
        //                    // Select all
        //                    selStart = 0;
        //                    selEnd = currentValue.Length;
        //                    curPos = selEnd;
        //                }
        //                break;
        //
        //            case PaperKey.C:
        //                if ((_paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl)) && HasSelection())
        //                {
        //                    // Copy selection
        //                    int start = Math.Min(selStart, selEnd);
        //                    int end = Math.Max(selStart, selEnd);
        //                    string selectedText = currentValue.Substring(start, end - start);
        //                    _paper.SetClipboard(selectedText);
        //                }
        //                break;
        //
        //            case PaperKey.X:
        //                if ((_paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl)) && HasSelection())
        //                {
        //                    // Cut selection
        //                    int start = Math.Min(selStart, selEnd);
        //                    int end = Math.Max(selStart, selEnd);
        //                    string selectedText = currentValue.Substring(start, end - start);
        //                    _paper.SetClipboard(selectedText);
        //                    DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
        //                    valueChanged = true;
        //                }
        //                break;
        //
        //            case PaperKey.V:
        //                if (_paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl))
        //                {
        //                    // Paste from clipboard
        //                    string clipText = _paper.GetClipboard();
        //                    if (!string.IsNullOrEmpty(clipText))
        //                    {
        //                        if (HasSelection())
        //                        {
        //                            DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
        //                        }
        //
        //                        currentValue = currentValue.Insert(curPos, clipText);
        //                        curPos += clipText.Length;
        //                        valueChanged = true;
        //                    }
        //                }
        //                break;
        //        }
        //
        //        // Update stored values
        //        _paper.SetElementStorage(_element, "Value", currentValue);
        //        _paper.SetElementStorage(_element, "CursorPosition", curPos);
        //        _paper.SetElementStorage(_element, "SelectionStart", selStart);
        //        _paper.SetElementStorage(_element, "SelectionEnd", selEnd);
        //
        //        // Ensure cursor is visible
        //        EnsureCursorVisible(currentValue, font, curPos);
        //
        //        // Notify of changes if needed
        //        if (valueChanged && onChange != null)
        //        {
        //            onChange(currentValue);
        //        }
        //
        //        // Helper functions
        //        bool HasSelection()
        //        {
        //            return selStart >= 0 && selEnd >= 0 && selStart != selEnd;
        //        }
        //    });
        //
        //    // Handle character input
        //    OnTextInput((TextInputEvent e) =>
        //    {
        //        if (!isFocused) return;
        //
        //        string currentValue = _paper.GetElementStorage<string>(_element, "Value", "");
        //        int curPos = _paper.GetElementStorage<int>(_element, "CursorPosition", 0);
        //        int selStart = _paper.GetElementStorage<int>(_element, "SelectionStart", -1);
        //        int selEnd = _paper.GetElementStorage<int>(_element, "SelectionEnd", -1);
        //
        //        // If we have a selection, delete it first
        //        if (selStart >= 0 && selEnd >= 0 && selStart != selEnd)
        //        {
        //            DeleteSelection(ref currentValue, ref curPos, ref selStart, ref selEnd);
        //        }
        //
        //        // Insert the character
        //        if (!char.IsControl(e.Character))
        //        {
        //            currentValue = currentValue.Insert(curPos, e.Character.ToString());
        //            curPos++;
        //
        //            // Update the value
        //            _paper.SetElementStorage(_element, "Value", currentValue);
        //            _paper.SetElementStorage(_element, "CursorPosition", curPos);
        //            _paper.SetElementStorage(_element, "SelectionStart", selStart);
        //            _paper.SetElementStorage(_element, "SelectionEnd", selEnd);
        //
        //            // Ensure cursor is visible
        //            EnsureCursorVisible(currentValue, font, curPos);
        //
        //            // Notify of changes
        //            onChange?.Invoke(currentValue);
        //        }
        //    });
        //
        //    // Render cursor and selection
        //    OnPostLayout((Element el, Rect rect) =>
        //    {
        //        _paper.AddActionElement(el, (canvas, r) =>
        //        {
        //            string currentValue = _paper.GetElementStorage<string>(el, "Value", "");
        //            double scrollOffsetValue = _paper.GetElementStorage<double>(el, "ScrollOffset", 0.0);
        //
        //            // Apply scroll transform to content
        //            canvas.TransformBy(Transform2D.CreateTranslation(-scrollOffsetValue, 0));
        //
        //            // Draw text
        //            double y = r.y + (r.height / 2);
        //            if(string.IsNullOrEmpty(currentValue))
        //            {
        //                // Draw placeholder text
        //                canvas.DrawText(font, placeholder, r.x + TextXPadding, y - font.LineHeight / 2, tColor);
        //            }
        //            else
        //            {
        //                // Draw actual text
        //                canvas.DrawText(font, currentValue, r.x + TextXPadding, y - font.LineHeight / 2, tColor);
        //            }
        //
        //            if (isFocused)
        //            {
        //                _paper.CaptureKeyboard();
        //
        //                // Draw text selection if applicable
        //                int selStart = _paper.GetElementStorage<int>(el, "SelectionStart", -1);
        //                int selEnd = _paper.GetElementStorage<int>(el, "SelectionEnd", -1);
        //
        //                if (selStart >= 0 && selEnd >= 0 && selStart != selEnd)
        //                {
        //                    // Ensure start < end
        //                    if (selStart > selEnd)
        //                    {
        //                        int temp = selStart;
        //                        selStart = selEnd;
        //                        selEnd = temp;
        //                    }
        //
        //                    // Calculate selection rectangle
        //                    double startX = CalculateTextWidth(currentValue.Substring(0, selStart), font);
        //                    double endX = CalculateTextWidth(currentValue.Substring(0, selEnd), font);
        //
        //                    // Draw selection background
        //                    canvas.BeginPath();
        //                    canvas.RoundedRect(
        //                        r.x + startX + TextXPadding,
        //                        r.y + (r.height - font.LineHeight) / 2,
        //                        endX - startX,
        //                        font.LineHeight,
        //                        2, 2, 2, 2);
        //                    canvas.SetFillColor(Color.FromArgb(100, 100, 150, 255));
        //                    canvas.Fill();
        //                }
        //
        //                // Draw cursor if we have focus
        //                int cursorPos = _paper.GetElementStorage<int>(el, "CursorPosition", 0);
        //
        //                // Only draw cursor during visible part of blink cycle
        //                if ((int)(_paper.Time * 2) % 2 == 0)
        //                {
        //                    double cursorX = r.x + TextXPadding + CalculateTextWidth(currentValue.Substring(0, cursorPos), font);
        //                    double cursorHeight = font.LineHeight;
        //
        //                    canvas.BeginPath();
        //                    canvas.MoveTo(cursorX, r.y + (r.height - cursorHeight) / 2);
        //                    canvas.LineTo(cursorX, r.y + (r.height - cursorHeight) / 2 + cursorHeight);
        //                    canvas.SetStrokeColor(Color.FromArgb(255, 250, 250, 250));
        //                    canvas.SetStrokeWidth(1);
        //                    canvas.Stroke();
        //                }
        //            }
        //        });
        //    });
        //
        //    return this;
        //}
        //
        //// Helper methods for text field functionality
        //
        ///// <summary>
        ///// Ensures the cursor is visible by adjusting scroll position if needed.
        ///// </summary>
        //private void EnsureCursorVisible(string text, FontInfo font, int cursorPosition)
        //{
        //    double scrollOffset = _paper.GetElementStorage<double>(_element, "ScrollOffset", 0.0);
        //
        //    // Calculate current cursor position
        //    double cursorX = CalculateTextWidth(text.Substring(0, cursorPosition), font);
        //
        //    // Get current visible area (estimate from last layout)
        //    double visibleWidth = _element.LayoutWidth - 8; // Subtract padding
        //
        //    const double margin = 20.0; // Margin to keep cursor away from edge
        //
        //    // If cursor is to the left of visible area
        //    if (cursorX < scrollOffset + margin)
        //    {
        //        // Scroll to show cursor with left margin
        //        scrollOffset = Math.Max(0, cursorX - margin);
        //    }
        //    // If cursor is to the right of visible area
        //    else if (cursorX > scrollOffset + visibleWidth - margin)
        //    {
        //        // Scroll to show cursor with right margin
        //        scrollOffset = cursorX - visibleWidth + margin;
        //    }
        //
        //    // Update scroll position
        //    _paper.SetElementStorage(_element, "ScrollOffset", scrollOffset);
        //}
        //
        ///// <summary>
        ///// Calculates the closest text position based on an X coordinate in a text string.
        ///// </summary>
        //private static int CalculateTextPosition(string text, FontInfo font, double x)
        //{
        //    if (string.IsNullOrEmpty(text)) return 0;
        //
        //    double closestDistance = double.MaxValue;
        //    int closestPosition = 0;
        //
        //    // Check each possible position
        //    for (int i = 0; i <= text.Length; i++)
        //    {
        //        double posWidth = CalculateTextWidth(text.Substring(0, i), font);
        //        double distance = Math.Abs(posWidth - x);
        //
        //        if (distance < closestDistance)
        //        {
        //            closestDistance = distance;
        //            closestPosition = i;
        //        }
        //    }
        //
        //    return closestPosition;
        //}
        //
        ///// <summary>
        ///// Calculates the width of a text string using the specified font.
        ///// </summary>
        //private static double CalculateTextWidth(string text, FontInfo font)
        //{
        //    if (string.IsNullOrEmpty(text)) return 0;
        //    return font.MeasureString(text).X;
        //}
        //
        ///// <summary>
        ///// Clears the text selection.
        ///// </summary>
        //private static void ClearSelection(ref int selStart, ref int selEnd)
        //{
        //    selStart = -1;
        //    selEnd = -1;
        //}
        //
        ///// <summary>
        ///// Deletes the selected text.
        ///// </summary>
        //private static void DeleteSelection(ref string value, ref int cursorPos, ref int selStart, ref int selEnd)
        //{
        //    int start = Math.Min(selStart, selEnd);
        //    int end = Math.Max(selStart, selEnd);
        //    int length = end - start;
        //
        //    value = value.Remove(start, length);
        //    cursorPos = start;
        //
        //    // Clear selection
        //    selStart = -1;
        //    selEnd = -1;
        //}
        //
        //#endregion

        /// <summary>
        /// Begins a new parent scope with this element as the parent.
        /// Used with 'using' statements to create a hierarchical UI structure.
        /// </summary>
        /// <returns>This builder as an IDisposable to be used with 'using' statements</returns>
        public IDisposable Enter()
        {
            var currentParent = _paper.CurrentParent;
            if (currentParent == _element)
                throw new InvalidOperationException("Cannot enter the same element twice.");

            // Push this element onto the stack
            _paper._elementStack.Push(_element);

            return this;
        }

        /// <summary>
        /// Ends the current parent scope by removing this element from the stack.
        /// Called automatically at the end of a 'using' block.
        /// </summary>
        void IDisposable.Dispose()
        {
            // Pop this element from the stack when the using block ends
            if (_paper._elementStack.Count > 1) // Don't pop the root
                _paper._elementStack.Pop();
        }
    }
}
