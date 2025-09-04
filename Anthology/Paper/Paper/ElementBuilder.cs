using System.Diagnostics;
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
        public ElementHandle _handle { get; protected set; }

        protected StyleSetterBase(ElementHandle element)
        {
            _handle = element;
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
        public T Size(in UnitValue sizeUniform) => Size(sizeUniform, sizeUniform);

        /// <summary>Sets the width and height of the element.</summary>
        public T Size(in UnitValue width, in UnitValue height)
        {
            SetStyleProperty(GuiProp.Width, width);
            return SetStyleProperty(GuiProp.Height, height);
        }

        /// <summary>Sets the width of the element.</summary>
        public T Width(in UnitValue width) => SetStyleProperty(GuiProp.Width, width);

        /// <summary>Sets the height of the element.</summary>
        public T Height(in UnitValue height) => SetStyleProperty(GuiProp.Height, height);

        /// <summary>Sets the minimum width of the element.</summary>
        public T MinWidth(in UnitValue minWidth) => SetStyleProperty(GuiProp.MinWidth, minWidth);

        /// <summary>Sets the maximum width of the element.</summary>
        public T MaxWidth(in UnitValue maxWidth) => SetStyleProperty(GuiProp.MaxWidth, maxWidth);

        /// <summary>Sets the minimum height of the element.</summary>
        public T MinHeight(in UnitValue minHeight) => SetStyleProperty(GuiProp.MinHeight, minHeight);

        /// <summary>Sets the maximum height of the element.</summary>
        public T MaxHeight(in UnitValue maxHeight) => SetStyleProperty(GuiProp.MaxHeight, maxHeight);

        /// <summary>Sets the position of the element from the left and top edges.</summary>
        public T Position(in UnitValue left, in UnitValue top)
        {
            SetStyleProperty(GuiProp.Left, left);
            return SetStyleProperty(GuiProp.Top, top);
        }

        /// <summary>Sets the left position of the element.</summary>
        public T Left(in UnitValue left) => SetStyleProperty(GuiProp.Left, left);

        /// <summary>Sets the right position of the element.</summary>
        public T Right(in UnitValue right) => SetStyleProperty(GuiProp.Right, right);

        /// <summary>Sets the top position of the element.</summary>
        public T Top(in UnitValue top) => SetStyleProperty(GuiProp.Top, top);

        /// <summary>Sets the bottom position of the element.</summary>
        public T Bottom(in UnitValue bottom) => SetStyleProperty(GuiProp.Bottom, bottom);

        /// <summary>Sets the minimum left position of the element.</summary>
        public T MinLeft(in UnitValue minLeft) => SetStyleProperty(GuiProp.MinLeft, minLeft);

        /// <summary>Sets the maximum left position of the element.</summary>
        public T MaxLeft(in UnitValue maxLeft) => SetStyleProperty(GuiProp.MaxLeft, maxLeft);

        /// <summary>Sets the minimum right position of the element.</summary>
        public T MinRight(in UnitValue minRight) => SetStyleProperty(GuiProp.MinRight, minRight);

        /// <summary>Sets the maximum right position of the element.</summary>
        public T MaxRight(in UnitValue maxRight) => SetStyleProperty(GuiProp.MaxRight, maxRight);

        /// <summary>Sets the minimum top position of the element.</summary>
        public T MinTop(in UnitValue minTop) => SetStyleProperty(GuiProp.MinTop, minTop);

        /// <summary>Sets the maximum top position of the element.</summary>
        public T MaxTop(in UnitValue maxTop) => SetStyleProperty(GuiProp.MaxTop, maxTop);

        /// <summary>Sets the minimum bottom position of the element.</summary>
        public T MinBottom(in UnitValue minBottom) => SetStyleProperty(GuiProp.MinBottom, minBottom);

        /// <summary>Sets the maximum bottom position of the element.</summary>
        public T MaxBottom(in UnitValue maxBottom) => SetStyleProperty(GuiProp.MaxBottom, maxBottom);

        /// <summary>Sets uniform margin on all sides.</summary>
        public T Margin(in UnitValue all) => Margin(all, all, all, all);

        /// <summary>Sets horizontal and vertical margins.</summary>
        public T Margin(in UnitValue horizontal, in UnitValue vertical) =>
            Margin(horizontal, horizontal, vertical, vertical);

        /// <summary>Sets individual margins for each side.</summary>
        public T Margin(in UnitValue left, in UnitValue right, in UnitValue top, in UnitValue bottom)
        {
            SetStyleProperty(GuiProp.Left, left);
            SetStyleProperty(GuiProp.Right, right);
            SetStyleProperty(GuiProp.Top, top);
            return SetStyleProperty(GuiProp.Bottom, bottom);
        }

        /// <summary>Sets the left padding for child elements.</summary>
        public T ChildLeft(in UnitValue childLeft) => SetStyleProperty(GuiProp.ChildLeft, childLeft);

        /// <summary>Sets the right padding for child elements.</summary>
        public T ChildRight(in UnitValue childRight) => SetStyleProperty(GuiProp.ChildRight, childRight);

        /// <summary>Sets the top padding for child elements.</summary>
        public T ChildTop(in UnitValue childTop) => SetStyleProperty(GuiProp.ChildTop, childTop);

        /// <summary>Sets the bottom padding for child elements.</summary>
        public T ChildBottom(in UnitValue childBottom) => SetStyleProperty(GuiProp.ChildBottom, childBottom);

        /// <summary>Sets the spacing between rows in a container.</summary>
        public T RowBetween(in UnitValue rowBetween) => SetStyleProperty(GuiProp.RowBetween, rowBetween);

        /// <summary>Sets the spacing between columns in a container.</summary>
        public T ColBetween(in UnitValue colBetween) => SetStyleProperty(GuiProp.ColBetween, colBetween);

        /// <summary>Sets the left border width.</summary>
        public T BorderLeft(in UnitValue borderLeft) => SetStyleProperty(GuiProp.BorderLeft, borderLeft);

        /// <summary>Sets the right border width.</summary>
        public T BorderRight(in UnitValue borderRight) => SetStyleProperty(GuiProp.BorderRight, borderRight);

        /// <summary>Sets the top border width.</summary>
        public T BorderTop(in UnitValue borderTop) => SetStyleProperty(GuiProp.BorderTop, borderTop);

        /// <summary>Sets the bottom border width.</summary>
        public T BorderBottom(in UnitValue borderBottom) => SetStyleProperty(GuiProp.BorderBottom, borderBottom);

        /// <summary>Sets uniform border width on all sides.</summary>
        public T Border(in UnitValue all) => Border(all, all, all, all);

        /// <summary>Sets horizontal and vertical border widths.</summary>
        public T Border(in UnitValue horizontal, in UnitValue vertical) =>
            Border(horizontal, horizontal, vertical, vertical);

        /// <summary>Sets individual border widths for each side.</summary>
        public T Border(in UnitValue left, in UnitValue right, in UnitValue top, in UnitValue bottom)
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
        public T WordSpacing(double spacing) => SetStyleProperty(GuiProp.WordSpacing, spacing);
        /// <summary>Sets the spacing between letters in text.</summary>
        public T LetterSpacing(double spacing) => SetStyleProperty(GuiProp.LetterSpacing, spacing);
        /// <summary>Sets the height of a line in text.</summary>
        public T LineHeight(double height) => SetStyleProperty(GuiProp.LineHeight, height);

        /// <summary>Sets the size of a Tab character in spaces.</summary>
        public T TabSize(int size) => SetStyleProperty(GuiProp.TabSize, size);
        /// <summary>Sets the size of text in pixels.</summary>
        public T FontSize(double size) => SetStyleProperty(GuiProp.FontSize, size);

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
        public T Transition(GuiProp property, double duration, Func<double, double> easing = null) => SetTransition(property, duration, easing);

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
        private StateDrivenStyle() : base(default)
        {
            _owner = null;
            _isActive = false;
        }

        // Constructor for direct creation (used internally)
        private StateDrivenStyle(ElementBuilder owner, bool isActive) : base(owner._handle)
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
            _handle = owner._handle;

            // Set fields with new values
            _owner = owner;
            _isActive = isActive;
        }

        public StateDrivenStyle Style(StyleTemplate style)
        {
            if (_isActive)
                style.ApplyTo(_handle);

            return this;
        }

        public StateDrivenStyle Style(params string[] names)
        {
            if (_isActive)
                foreach (var styleName in names)
                    _owner._paper.ApplyStyleWithStates(_handle, styleName);

            return this;
        }

        public StateDrivenStyle StyleIf(bool condition, params string[] names)
        {
            if (condition)
            {
                foreach(var styleName in names)
                    _owner._paper.ApplyStyleWithStates(_handle, styleName);
            }
            return this;
        }

        public override StateDrivenStyle SetStyleProperty(GuiProp property, object value)
        {
            if (_isActive)
                _owner._paper.SetStyleProperty(_handle.Data.ID, property, value);
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
                _owner._paper.SetTransitionConfig(_handle.Data.ID, property, duration, easing);
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
        public StyleTemplate() : base(default) { }

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
        public void ApplyTo(ElementHandle element)
        {
            if (element.Owner == null) throw new ArgumentNullException(nameof(element));
            foreach (var kvp in _styleProperties)
            {
                element.Owner!.SetStyleProperty(element.Data.ID, kvp.Key, kvp.Value);
            }

            // Apply transitions
            foreach (var kvp in _transitions)
            {
                element.Owner!.SetTransitionConfig(element.Data.ID, kvp.Key, kvp.Value.duration, kvp.Value.easing);
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
        public StateDrivenStyle Hovered => StateDrivenStyle.Get(this, _paper.IsElementHovered(_handle.Data.ID));

        /// <summary>Style properties applied when the element is active (pressed).</summary>
        public StateDrivenStyle Active => StateDrivenStyle.Get(this, _paper.IsElementActive(_handle.Data.ID));

        /// <summary>Style properties applied when the element has focus.</summary>
        public StateDrivenStyle Focused => StateDrivenStyle.Get(this, _paper.IsElementFocused(_handle.Data.ID));

        public ElementBuilder(Paper paper, ElementHandle handle) : base(handle)
        {
            _paper = paper;
        }

        public override ElementBuilder SetStyleProperty(GuiProp property, object value)
        {
            _paper.SetStyleProperty(_handle.Data.ID, property, value);
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
            _paper.SetTransitionConfig(_handle.Data.ID, property, duration, easing);
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
        public ElementBuilder InheritStyle(ElementHandle? element = null)
        {
            if (element != null)
            {
                _handle.Data._elementStyle.SetParent(element.Value.Data._elementStyle);
                return this;
            }

            var parentHandle = _handle.GetParentHandle();
            if (parentHandle.IsValid)
                _handle.Data._elementStyle.SetParent(parentHandle.Data._elementStyle);

            return this;
        }

        public ElementBuilder Style(StyleTemplate style)
        {
            style.ApplyTo(_handle);
            return this;
        }

        public ElementBuilder Style(params string[] names)
        {
            foreach (var name in names)
                _paper.ApplyStyleWithStates(_handle, name);

            return this;
        }

        public ElementBuilder StyleIf(bool condition, params string[] names)
        {
            if(condition)
                foreach (var name in names)
                    _paper.ApplyStyleWithStates(_handle, name);
            return this;
        }

        #region Event Handlers

        /// <summary>Makes the element incapable of receiving focus.</summary>
        public ElementBuilder IsNotFocusable()
        {
            _handle.Data.IsFocusable = false;
            return this;
        }

        /// <summary>
        /// Hooks this element to its parent's interaction states.
        /// When the parent is hovered, active, focused, or dragging, this element will also be considered in those states and receive the events.
        /// </summary>
        public ElementBuilder HookToParent()
        {
            _handle.Data.IsHookedToParent = true;
            
            // Mark the parent as having hooked children for optimization
            ElementHandle parent = _handle.GetParentHandle();
            if (parent.IsValid)
            {
                parent.Data.IsAHookedParent = true;
            }
            
            return this;
        }

        /// <summary>
        /// Sets the tab index for keyboard navigation. 
        /// Elements with lower tab indices are focused first when pressing Tab.
        /// Use -1 to exclude from tab navigation (default).
        /// </summary>
        public ElementBuilder TabIndex(int index)
        {
            _handle.Data.TabIndex = index;
            return this;
        }

        /// <summary>Sets a callback that runs after layout calculation is complete.</summary>
        public ElementBuilder OnPostLayout(Action<ElementHandle, Rect> handler)
        {
            _handle.Data.OnPostLayout += handler;
            return this;
        }

        /// <summary>Sets a callback that runs after layout calculation is complete, with a captured value.</summary>
        public ElementBuilder OnPostLayout<T>(T capturedValue, Action<T, ElementHandle, Rect> handler) =>
            OnPostLayout((ElementHandle element, Rect rect) => handler(capturedValue, element, rect));

        /// <summary>Sets a callback that runs when the element is pressed.</summary>
        public ElementBuilder OnPress(Action<ClickEvent> handler)
        {
            _handle.Data.OnPress += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is pressed, with a captured value.</summary>
        public ElementBuilder OnPress<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnPress((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is held down.</summary>
        public ElementBuilder OnHeld(Action<ClickEvent> handler)
        {
            _handle.Data.OnHeld += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is held down, with a captured value.</summary>
        public ElementBuilder OnHeld<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnHeld((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is clicked.</summary>
        public ElementBuilder OnClick(Action<ClickEvent> handler)
        {
            _handle.Data.OnClick += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is clicked, with a captured value.</summary>
        public ElementBuilder OnClick<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnClick((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is dragged.</summary>
        public ElementBuilder OnDragStart(Action<DragEvent> handler)
        {
            _handle.Data.OnDragStart += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is dragged, with a captured value.</summary>
        public ElementBuilder OnDragStart<T>(T capturedValue, Action<T, DragEvent> handler) =>
            OnDragStart((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is dragged.</summary>
        public ElementBuilder OnDragging(Action<DragEvent> handler)
        {
            _handle.Data.OnDragging += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is dragged, with a captured value.</summary>
        public ElementBuilder OnDragging<T>(T capturedValue, Action<T, DragEvent> handler) =>
            OnDragging((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is released after dragging.</summary>
        public ElementBuilder OnDragEnd(Action<DragEvent> handler)
        {
            _handle.Data.OnDragEnd += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is released after dragging, with a captured value.</summary>
        public ElementBuilder OnDragEnd<T>(T capturedValue, Action<T, DragEvent> handler) =>
            OnDragEnd((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the mouse button is released after clicking this element.</summary>
        public ElementBuilder OnRelease(Action<ClickEvent> handler)
        {
            _handle.Data.OnRelease += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the mouse button is released after clicking this element, with a captured value.</summary>
        public ElementBuilder OnRelease<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnRelease((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is double-clicked.</summary>
        public ElementBuilder OnDoubleClick(Action<ClickEvent> handler)
        {
            _handle.Data.OnDoubleClick += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is double-clicked, with a captured value.</summary>
        public ElementBuilder OnDoubleClick<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnDoubleClick((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the element is right-clicked.</summary>
        public ElementBuilder OnRightClick(Action<ClickEvent> handler)
        {
            _handle.Data.OnRightClick += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the element is right-clicked, with a captured value.</summary>
        public ElementBuilder OnRightClick<T>(T capturedValue, Action<T, ClickEvent> handler) =>
            OnRightClick((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when scrolling occurs over the element.</summary>
        public ElementBuilder OnScroll(Action<ScrollEvent> handler)
        {
            _handle.Data.OnScroll += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when scrolling occurs over the element, with a captured value.</summary>
        public ElementBuilder OnScroll<T>(T capturedValue, Action<T, ScrollEvent> handler) =>
            OnScroll((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when a key is pressed while the element is focused.</summary>
        public ElementBuilder OnKeyPressed(Action<KeyEvent> handler)
        {
            _handle.Data.OnKeyPressed += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when a key is pressed while the element is focused, with a captured value.</summary>
        public ElementBuilder OnKeyPressed<T>(T capturedValue, Action<T, KeyEvent> handler) =>
            OnKeyPressed((key) => handler(capturedValue, key));

        /// <summary>Sets a callback that runs when a character is typed while the element is focused.</summary>
        public ElementBuilder OnTextInput(Action<TextInputEvent> handler)
        {
            _handle.Data.OnTextInput += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when a character is typed while the element is focused, with a captured value.</summary>
        public ElementBuilder OnTextInput<T>(T capturedValue, Action<T, TextInputEvent> handler) =>
            OnTextInput((character) => handler(capturedValue, character));

        /// <summary>Sets a callback that runs when the cursor hovers over the element.</summary>
        public ElementBuilder OnHover(Action<ElementEvent> handler)
        {
            _handle.Data.OnHover += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the cursor hovers over the element, with a captured value.</summary>
        public ElementBuilder OnHover<T>(T capturedValue, Action<T, ElementEvent> handler) =>
            OnHover((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the Focused state changes.</summary>
        public ElementBuilder OnFocusChange(Action<FocusEvent> handler)
        {
            _handle.Data.OnFocusChange += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the Focused state changes, with a captured value.</summary>
        public ElementBuilder OnFocusChange<T>(T capturedValue, Action<T, FocusEvent> handler) =>
            OnFocusChange((focused) => handler(capturedValue, focused));

        /// <summary>Sets a callback that runs when the cursor enters the element's bounds.</summary>
        public ElementBuilder OnEnter(Action<ElementEvent> handler)
        {
            _handle.Data.OnEnter += handler;
            return this;
        }

        /// <summary>Sets a callback that runs when the cursor enters the element's bounds, with a captured value.</summary>
        public ElementBuilder OnEnter<T>(T capturedValue, Action<T, ElementEvent> handler) =>
            OnEnter((e) => handler(capturedValue, e));

        /// <summary>Sets a callback that runs when the cursor leaves the element's bounds.</summary>
        public ElementBuilder OnLeave(Action<ElementEvent> handler)
        {
            _handle.Data.OnLeave += handler;
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
            _handle.Data.IsNotInteractable = true;
            return this;
        }

        /// <summary>Makes any event on this element not trigger any parent events.</summary>
        public ElementBuilder StopEventPropagation()
        {
            _handle.Data.StopPropagation = true;
            return this;
        }

        /// <summary>Sets the layout direction for child elements.</summary>
        /// <param name="layoutType">How child elements should be arranged (Row or Column)</param>
        public ElementBuilder LayoutType(LayoutType layoutType)
        {
            _handle.Data.LayoutType = layoutType;
            return this;
        }

        /// <summary>Sets how the element is positioned within its parent.</summary>
        /// <param name="positionType">Position strategy (SelfDirected or ParentDirected)</param>
        public ElementBuilder PositionType(PositionType positionType)
        {
            _handle.Data.PositionType = positionType;
            return this;
        }

        /// <summary>Sets whether the element is visible.</summary>
        /// <param name="visible">True to show the element, false to hide it</param>
        public ElementBuilder Visible(bool visible)
        {
            _handle.Data.Visible = visible;
            return this;
        }

        /// <summary>
        /// Sets a content sizing function for auto-sized elements.
        /// The function receives optional width and height constraints and should return the preferred content size.
        /// This is particularly useful for custom controls that need to calculate their own size based on content.
        /// </summary>
        /// <param name="sizer">
        /// Function that takes (maxWidth?, maxHeight?) and returns (preferredWidth, preferredHeight)?.
        /// Return null if the element cannot be sized with the given constraints.
        /// </param>
        /// <returns>This builder for method chaining</returns>
        /// <example>
        /// // Example: Size based on text content
        /// .ContentSizer((maxWidth, maxHeight) => {
        ///     var textSize = MeasureText("My content", font, fontSize);
        ///     return (textSize.Width + padding * 2, textSize.Height + padding * 2);
        /// })
        /// 
        /// // Example: Aspect ratio sizing
        /// .ContentSizer((maxWidth, maxHeight) => {
        ///     const double aspectRatio = 16.0 / 9.0;
        ///     if (maxWidth.HasValue) {
        ///         return (maxWidth.Value, maxWidth.Value / aspectRatio);
        ///     }
        ///     if (maxHeight.HasValue) {
        ///         return (maxHeight.Value * aspectRatio, maxHeight.Value);
        ///     }
        ///     return (320, 180); // Default size
        /// })
        /// </example>
        public ElementBuilder ContentSizer(Func<double?, double?, (double, double)?> sizer)
        {
            _handle.Data.ContentSizer = sizer;
            return this;
        }

        /// <summary>
        /// Sets a simple content sizing function that returns a fixed size.
        /// </summary>
        /// <param name="width">Fixed preferred width</param>
        /// <param name="height">Fixed preferred height</param>
        /// <returns>This builder for method chaining</returns>
        public ElementBuilder ContentSizer(double width, double height)
        {
            _handle.Data.ContentSizer = (_, _) => (width, height);
            return this;
        }

        /// <summary>
        /// Removes any content sizing function, allowing the element to use default sizing behavior.
        /// </summary>
        /// <returns>This builder for method chaining</returns>
        public ElementBuilder ClearContentSizer()
        {
            _handle.Data.ContentSizer = null;
            return this;
        }

        /// <summary>Enables content clipping to the element's bounds.</summary>
        public ElementBuilder Clip()
        {
            _handle.Data._scissorEnabled = true;
            return this;
        }

        /// <summary>Places the element on a specific rendering layer.</summary>
        /// <param name="layer">Layer on which the element should be rendered.</param>
        public ElementBuilder Layer(Layer layer)
        {
            _handle.Data.Layer = layer;
            return this;
        }

        /// <summary>Sets the text content of the element.</summary>
        /// <param name="text">The text to display</param>
        /// <param name="useMarkdown">Whether to parse the text as Markdown</param>
        public ElementBuilder Text(string text, FontFile font)
        {
            _handle.Data.IsMarkdown = false;
            _handle.Data.Paragraph = text;
            _handle.Data.Font = font;
            return this;
        }

        /// <summary>Sets the text content of the element.</summary>
        /// <param name="text">The text to display</param>
        /// <param name="useMarkdown">Whether to parse the text as Markdown</param>
        public ElementBuilder Markdown(string text, FontFile font, FontFile bold, FontFile italic, FontFile boldItalic, FontFile mono)
        {
            _handle.Data.IsMarkdown = true;
            _handle.Data.Paragraph = text;
            _handle.Data.Font = font;
            _handle.Data.FontBold = bold;
            _handle.Data.FontItalic = italic;
            _handle.Data.FontBoldItalic = boldItalic;
            _handle.Data.FontMono = mono;
            return this;
        }

        /// <summary>
        /// Sets the text Alignment mode of the element.
        /// </summary>
        /// <param name="mode">The Text Alignment mode to apply</param>
        public ElementBuilder Alignment(TextAlignment mode)
        {
            _handle.Data.TextAlignment = mode;
            return this;
        }

        /// <summary>
        /// Sets the text wrapping mode of the element.
        /// </summary>
        /// <param name="mode">The text wrapping mode to apply</param>
        public ElementBuilder Wrap(TextWrapMode mode)
        {
            _handle.Data.WrapMode = mode;
            return this;
        }

        /// <summary>
        /// Configures scrolling behavior for the element.
        /// </summary>
        /// <param name="flags">Flags to control scroll behavior</param>
        public ElementBuilder SetScroll(Scroll flags)
        {
            // Set the scroll flags directly on the element
            _handle.Data.ScrollFlags = flags;

            // Enable clipping for scrollable elements
            if (flags != Scroll.None)
            {
                _handle.Data._scissorEnabled = true;

                // Initialize scroll state if not already present
                if (!_paper.HasElementStorage(_handle, "ScrollState"))
                {
                    _paper.SetElementStorage(_handle, "ScrollState", new ScrollState());
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
            var state = _paper.GetElementStorage<ScrollState>(_handle, "ScrollState", new ScrollState());
            state.Position = position;
            _paper.SetElementStorage(_handle, "ScrollState", state);
            return this;
        }

        /// <summary>
        /// Sets a callback to customize the rendering of scrollbars.
        /// </summary>
        public ElementBuilder CustomScrollbarRenderer(Action<Canvas, Rect, ScrollState> renderer)
        {
            // Store the renderer directly on the element
            _handle.Data.CustomScrollbarRenderer = renderer;
            return this;
        }

        /// <summary>
        /// Configures event handlers for scrolling interactions.
        /// </summary>
        private void ConfigureScrollHandlers()
        {
            // Update content and viewport sizes after layout
            OnPostLayout((element, rect) => {
                var state = _paper.GetElementStorage<ScrollState>(_handle, "ScrollState", new ScrollState());

                // Set viewport size to current element size
                state.ViewportSize = new Vector2(rect.width, rect.height);

                // For content size, we need to measure all children
                // Here we're assuming content size is the max bounds of all children
                double maxX = 0;
                double maxY = 0;

                foreach (var childIndex in element.Data.ChildIndices)
                {
                    ref var child = ref element.Owner!.GetElementData(childIndex);
                    maxX = Math.Max(maxX, child.RelativeX + child.LayoutWidth);
                    maxY = Math.Max(maxY, child.RelativeY + child.LayoutHeight);
                }

                state.ContentSize = new Vector2(maxX, maxY);
                state.ClampScrollPosition();

                _paper.SetElementStorage(_handle, "ScrollState", state);
            });

            // Handle hover events to detect when cursor is over scrollbars
            OnHover((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_handle, "ScrollState", new ScrollState());
                Vector2 mousePos = _paper.PointerPos;

                // Check if pointer is over scrollbars
                state.IsVerticalScrollbarHovered = state.IsPointOverVerticalScrollbar(mousePos, e.ElementRect, _handle.Data.ScrollFlags);
                state.IsHorizontalScrollbarHovered = state.IsPointOverHorizontalScrollbar(mousePos, e.ElementRect, _handle.Data.ScrollFlags);

                _paper.SetElementStorage(_handle, "ScrollState", state);
            });

            // Handle mouse leaving the element
            OnLeave((rect) => {
                var state = _paper.GetElementStorage<ScrollState>(_handle, "ScrollState", new ScrollState());
                state.IsVerticalScrollbarHovered = false;
                state.IsHorizontalScrollbarHovered = false;
                _paper.SetElementStorage(_handle, "ScrollState", state);
            });

            // Handle scrolling with mouse wheel
            OnScroll((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_handle, "ScrollState", new ScrollState());

                // Don't handle wheel scrolling if actively dragging scrollbars
                if (state.IsDraggingVertical || state.IsDraggingHorizontal)
                    return;

                if ((_handle.Data.ScrollFlags & Scroll.ScrollY) != 0)
                {
                    state.Position = new Vector2(
                        state.Position.x,
                        state.Position.y - e.Delta * 30  // Adjust scroll speed as needed
                    );
                }
                else if ((_handle.Data.ScrollFlags & Scroll.ScrollX) != 0)
                {
                    state.Position = new Vector2(
                        state.Position.x - e.Delta * 30,
                        state.Position.y
                    );
                }

                state.ClampScrollPosition();
                _paper.SetElementStorage(_handle, "ScrollState", state);
            });

            // Handle start scrollbar drag
            OnDragStart((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_handle, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(_handle.Data.ScrollFlags)) return;

                Vector2 mousePos = _paper.PointerPos;

                // Check if click is on a scrollbar
                bool onVertical = state.IsPointOverVerticalScrollbar(mousePos, e.ElementRect, _handle.Data.ScrollFlags);
                bool onHorizontal = state.IsPointOverHorizontalScrollbar(mousePos, e.ElementRect, _handle.Data.ScrollFlags);

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

                _paper.SetElementStorage(_handle, "ScrollState", state);
            });

            // Handle dragging scrollbars
            OnDragging((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_handle, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(_handle.Data.ScrollFlags)) return;

                Vector2 mousePos = _paper.PointerPos;

                // Handle scrollbar dragging
                if (state.IsDraggingVertical)
                {
                    state.HandleVerticalScrollbarDrag(mousePos, e.ElementRect, _handle.Data.ScrollFlags);
                }
                else if (state.IsDraggingHorizontal)
                {
                    state.HandleHorizontalScrollbarDrag(mousePos, e.ElementRect, _handle.Data.ScrollFlags);
                }

                _paper.SetElementStorage(_handle, "ScrollState", state);
            });

            // Handle after dragging
            OnDragEnd((e) => {
                var state = _paper.GetElementStorage<ScrollState>(_handle, "ScrollState", new ScrollState());

                if (state.AreScrollbarsHidden(_handle.Data.ScrollFlags)) return;

                state.IsDraggingVertical = false;
                state.IsDraggingHorizontal = false;

                // Update hover state on release
                Vector2 mousePos = _paper.PointerPos;
                state.IsVerticalScrollbarHovered = state.IsPointOverVerticalScrollbar(mousePos, e.ElementRect, _handle.Data.ScrollFlags);
                state.IsHorizontalScrollbarHovered = state.IsPointOverHorizontalScrollbar(mousePos, e.ElementRect, _handle.Data.ScrollFlags);

                _paper.SetElementStorage(_handle, "ScrollState", state);
            });
        }

        #endregion

        #region Text Input

        /// <summary>
        /// Settings for text input controls (TextField and TextArea).
        /// </summary>
        public struct TextInputSettings
        {
            /// <summary>Font used to render the text</summary>
            public FontFile Font;
            
            /// <summary>Color of the text</summary>
            public Color TextColor;
            
            /// <summary>Placeholder text shown when the field is empty</summary>
            public string Placeholder;
            
            /// <summary>Color of the placeholder text</summary>
            public Color PlaceholderColor;
            
            /// <summary>Whether the input is read-only</summary>
            public bool ReadOnly;
            
            /// <summary>Maximum number of characters allowed (0 = no limit)</summary>
            public int MaxLength;

            /// <summary>Allow text to wrap instead of scrolling (For Multi-Line Only)</summary>
            public bool DoWrap;

            /// <summary>Creates default text input settings</summary>
            public static TextInputSettings Default => new TextInputSettings
            {
                Font = null,
                TextColor = Color.FromArgb(255, 250, 250, 250),
                Placeholder = "",
                PlaceholderColor = Color.FromArgb(160, 200, 200, 200),
                ReadOnly = false,
                MaxLength = 0,
                DoWrap = true
            };
        }

        /// <summary>
        /// Internal state container for text input data to reduce storage operations.
        /// Supports both single-line and multi-line text input.
        /// </summary>
        private struct TextInputState
        {
            public string Value;
            public int CursorPosition;
            public int SelectionStart;
            public int SelectionEnd;
            public double ScrollOffsetX;
            public double ScrollOffsetY;
            public bool IsFocused;
            public bool IsMultiLine;

            public readonly bool HasSelection => SelectionStart >= 0 && SelectionEnd >= 0 && SelectionStart != SelectionEnd;
            
            public void ClearSelection()
            {
                SelectionStart = -1;
                SelectionEnd = -1;
            }
            
            public void DeleteSelection()
            {
                if (!HasSelection) return;
                
                int start = Math.Min(SelectionStart, SelectionEnd);
                int end = Math.Max(SelectionStart, SelectionEnd);
                Value = Value.Remove(start, end - start);
                CursorPosition = start;
                ClearSelection();
            }
            
            public void ClampValues()
            {
                CursorPosition = Math.Clamp(CursorPosition, 0, Value.Length);
                SelectionStart = SelectionStart < 0 ? -1 : Math.Clamp(SelectionStart, 0, Value.Length);
                SelectionEnd = SelectionEnd < 0 ? -1 : Math.Clamp(SelectionEnd, 0, Value.Length);
            }
            
            /// <summary>Gets the current line that contains the cursor</summary>
            public readonly int GetCursorLine()
            {
                if (!IsMultiLine || string.IsNullOrEmpty(Value)) return 0;
                
                int line = 0;
                for (int i = 0; i < CursorPosition && i < Value.Length; i++)
                {
                    if (Value[i] == '\n') line++;
                }
                return line;
            }
            
            /// <summary>Gets all lines in the text</summary>
            public readonly string[] GetLines()
            {
                if (string.IsNullOrEmpty(Value)) return new[] { "" };
                return Value.Split('\n');
            }
            
            /// <summary>Gets the column position of the cursor within its line</summary>
            public readonly int GetCursorColumn()
            {
                if (string.IsNullOrEmpty(Value)) return 0;
                if (CursorPosition == 0) return 0;
                
                int lastNewline = Value.LastIndexOf('\n', Math.Min(CursorPosition - 1, Value.Length - 1));
                return CursorPosition - (lastNewline + 1);
            }
            
            /// <summary>Clamps scroll offsets to valid ranges for text input</summary>
            public void ClampScrollOffsets(double contentWidth, double contentHeight, double visibleWidth, double visibleHeight)
            {
                double maxScrollX = Math.Max(0, contentWidth - visibleWidth);
                double maxScrollY = Math.Max(0, contentHeight - visibleHeight);
                
                ScrollOffsetX = Math.Clamp(ScrollOffsetX, 0, maxScrollX);
                ScrollOffsetY = Math.Clamp(ScrollOffsetY, 0, maxScrollY);
            }
        }

        /// <summary>
        /// Helper methods for text input state management.
        /// </summary>
        private TextInputState LoadTextInputState(string initialValue, bool isMultiLine)
        {
            var defaultState = new TextInputState
            {
                Value = initialValue ?? "",
                CursorPosition = (initialValue ?? "").Length,
                SelectionStart = -1,
                SelectionEnd = -1,
                ScrollOffsetX = 0.0,
                ScrollOffsetY = 0.0,
                IsFocused = false,
                IsMultiLine = isMultiLine
            };
            
            var state = _paper.GetElementStorage(_handle, "TextInputState", defaultState);
            state.IsFocused = _paper.IsElementFocused(_handle.Data.ID);
            state.IsMultiLine = isMultiLine; // Ensure consistency
            state.ClampValues();
            return state;
        }
        
        private void SaveTextInputState(TextInputState state)
        {
            _paper.SetElementStorage(_handle, "TextInputState", state);
        }
        
        private TextLayoutSettings CreateTextLayoutSettings(TextInputSettings inputSettings, bool isMultiLine, double maxWidth = float.MaxValue)
        {
            var fontSize = (double)_handle.Data._elementStyle.GetValue(GuiProp.FontSize);
            var letterSpacing = (double)_handle.Data._elementStyle.GetValue(GuiProp.LetterSpacing);

            var settings = TextLayoutSettings.Default;
            settings.PixelSize = (float)fontSize;
            settings.Font = inputSettings.Font;
            settings.LetterSpacing = (float)letterSpacing;
            settings.Alignment = Scribe.TextAlignment.Left;
            settings.MaxWidth = (float)maxWidth;
            settings.WrapMode = (isMultiLine && inputSettings.DoWrap) ? Scribe.TextWrapMode.Wrap : TextWrapMode.NoWrap;
            
            return settings;
        }
        
        private bool IsShiftPressed() => _paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift);
        private bool IsControlPressed() => _paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl);

        /// <summary>
        /// Finds the start of the previous word from the current position
        /// </summary>
        private int FindPreviousWordStart(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position <= 0) return 0;
            
            int pos = Math.Min(position - 1, text.Length - 1);
            
            // Skip whitespace
            while (pos > 0 && char.IsWhiteSpace(text[pos]))
                pos--;
            
            // Skip word characters
            while (pos > 0 && !char.IsWhiteSpace(text[pos]))
                pos--;
            
            // Move to start of word if we stopped at whitespace
            if (pos > 0 && char.IsWhiteSpace(text[pos]))
                pos++;
                
            return pos;
        }

        /// <summary>
        /// Finds the end of the next word from the current position
        /// </summary>
        private int FindNextWordEnd(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position >= text.Length) return text?.Length ?? 0;
            
            int pos = position;
            
            // Skip whitespace
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                pos++;
            
            // Skip word characters
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]))
                pos++;
                
            return pos;
        }

        /// <summary>
        /// Finds the boundaries of the word at the given position
        /// </summary>
        private (int start, int end) FindWordBoundaries(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position < 0 || position >= text.Length)
                return (position, position);
            
            // If we're on whitespace, return the position as both start and end
            if (char.IsWhiteSpace(text[position]))
                return (position, position);
            
            int start = position;
            int end = position;
            
            // Find start of word
            while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
                start--;
            
            // Find end of word
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
                end++;
                
            return (start, end);
        }

        private void MoveCursorVertical(ref TextInputState state, int direction, TextInputSettings settings)
        {
            if (!state.IsMultiLine) return;
            
            var lines = state.GetLines();
            int currentLine = state.GetCursorLine();
            int targetLine = Math.Clamp(currentLine + direction, 0, lines.Length - 1);
            
            if (targetLine == currentLine) return;

            int currentColumn = state.GetCursorColumn();

            // Move to the same column in the target line, or end of line if shorter
            int targetColumn = Math.Min(currentColumn, lines[targetLine].Length);
            
            // Calculate new cursor position
            int newPosition = 0;
            for (int i = 0; i < targetLine; i++)
            {
                newPosition += lines[i].Length;
                // Only add +1 for newline if this isn't the last line in the original text
                if (i < lines.Length - 1 || state.Value.EndsWith('\n'))
                    newPosition += 1;
            }
            newPosition += targetColumn;
            
            if (IsShiftPressed())
            {
                if (state.SelectionStart < 0) state.SelectionStart = state.CursorPosition;
                state.CursorPosition = newPosition;
                state.SelectionEnd = newPosition;
            }
            else
            {
                state.CursorPosition = newPosition;
                state.ClearSelection();
            }
        }

        private bool ProcessKeyCommand(ref TextInputState state, PaperKey key, TextInputSettings settings)
        {
            bool valueChanged = false;
            
            switch (key)
            {
                case PaperKey.Backspace:
                    if (state.HasSelection)
                    {
                        state.DeleteSelection();
                        valueChanged = true;
                    }
                    else if (state.CursorPosition > 0)
                    {
                        state.Value = state.Value.Remove(state.CursorPosition - 1, 1);
                        state.CursorPosition--;
                        valueChanged = true;
                    }
                    break;
                    
                case PaperKey.Delete:
                    if (state.HasSelection)
                    {
                        state.DeleteSelection();
                        valueChanged = true;
                    }
                    else if (state.CursorPosition < state.Value.Length)
                    {
                        state.Value = state.Value.Remove(state.CursorPosition, 1);
                        valueChanged = true;
                    }
                    break;
                    
                case PaperKey.Left:
                    if (IsControlPressed())
                    {
                        // Ctrl+Left: Move to previous word
                        int newPos = FindPreviousWordStart(state.Value, state.CursorPosition);
                        if (IsShiftPressed())
                        {
                            if (state.SelectionStart < 0) state.SelectionStart = state.CursorPosition;
                            state.CursorPosition = newPos;
                            state.SelectionEnd = state.CursorPosition;
                        }
                        else
                        {
                            state.CursorPosition = newPos;
                            state.ClearSelection();
                        }
                    }
                    else if (IsShiftPressed())
                    {
                        if (state.SelectionStart < 0) state.SelectionStart = state.CursorPosition;
                        state.CursorPosition = Math.Max(0, state.CursorPosition - 1);
                        state.SelectionEnd = state.CursorPosition;
                    }
                    else
                    {
                        if (state.HasSelection)
                            state.CursorPosition = Math.Min(state.SelectionStart, state.SelectionEnd);
                        else
                            state.CursorPosition = Math.Max(0, state.CursorPosition - 1);
                        state.ClearSelection();
                    }
                    break;
                    
                case PaperKey.Right:
                    if (IsControlPressed())
                    {
                        // Ctrl+Right: Move to next word
                        int newPos = FindNextWordEnd(state.Value, state.CursorPosition);
                        if (IsShiftPressed())
                        {
                            if (state.SelectionStart < 0) state.SelectionStart = state.CursorPosition;
                            state.CursorPosition = newPos;
                            state.SelectionEnd = state.CursorPosition;
                        }
                        else
                        {
                            state.CursorPosition = newPos;
                            state.ClearSelection();
                        }
                    }
                    else if (IsShiftPressed())
                    {
                        if (state.SelectionStart < 0) state.SelectionStart = state.CursorPosition;
                        state.CursorPosition = Math.Min(state.Value.Length, state.CursorPosition + 1);
                        state.SelectionEnd = state.CursorPosition;
                    }
                    else
                    {
                        if (state.HasSelection)
                            state.CursorPosition = Math.Max(state.SelectionStart, state.SelectionEnd);
                        else
                            state.CursorPosition = Math.Min(state.Value.Length, state.CursorPosition + 1);
                        state.ClearSelection();
                    }
                    break;
                    
                case PaperKey.Home:
                    if (IsShiftPressed())
                    {
                        if (state.SelectionStart < 0) state.SelectionStart = state.CursorPosition;
                        state.CursorPosition = 0;
                        state.SelectionEnd = state.CursorPosition;
                    }
                    else
                    {
                        state.CursorPosition = 0;
                        state.ClearSelection();
                    }
                    break;
                    
                case PaperKey.End:
                    if (IsShiftPressed())
                    {
                        if (state.SelectionStart < 0) state.SelectionStart = state.CursorPosition;
                        state.CursorPosition = state.Value.Length;
                        state.SelectionEnd = state.CursorPosition;
                    }
                    else
                    {
                        state.CursorPosition = state.Value.Length;
                        state.ClearSelection();
                    }
                    break;
                    
                case PaperKey.A when IsControlPressed():
                    state.SelectionStart = 0;
                    state.SelectionEnd = state.Value.Length;
                    state.CursorPosition = state.SelectionEnd;
                    break;
                    
                case PaperKey.C when IsControlPressed() && state.HasSelection:
                    {
                        int start = Math.Min(state.SelectionStart, state.SelectionEnd);
                        int end = Math.Max(state.SelectionStart, state.SelectionEnd);
                        _paper.SetClipboard(state.Value.Substring(start, end - start));
                    }
                    break;
                    
                case PaperKey.X when IsControlPressed() && state.HasSelection:
                    {
                        int start = Math.Min(state.SelectionStart, state.SelectionEnd);
                        int end = Math.Max(state.SelectionStart, state.SelectionEnd);
                        _paper.SetClipboard(state.Value.Substring(start, end - start));
                        state.DeleteSelection();
                        valueChanged = true;
                    }
                    break;
                    
                case PaperKey.V when IsControlPressed():
                    {
                        string clipText = _paper.GetClipboard();
                        if (!string.IsNullOrEmpty(clipText))
                        {
                            // For single-line, replace newlines with spaces
                            if (!state.IsMultiLine)
                                clipText = clipText.Replace('\n', ' ').Replace('\r', ' ');
                                
                            // Check max length
                            if (settings.MaxLength > 0)
                            {
                                int availableLength = settings.MaxLength - state.Value.Length;
                                if (state.HasSelection)
                                {
                                    int selectionLength = Math.Abs(state.SelectionEnd - state.SelectionStart);
                                    availableLength += selectionLength;
                                }
                                if (availableLength > 0 && clipText.Length > availableLength)
                                    clipText = clipText.Substring(0, availableLength);
                            }
                            
                            if (!string.IsNullOrEmpty(clipText))
                            {
                                if (state.HasSelection) state.DeleteSelection();
                                state.Value = state.Value.Insert(state.CursorPosition, clipText);
                                state.CursorPosition += clipText.Length;
                                valueChanged = true;
                            }
                        }
                    }
                    break;

                // Seems a bit buggy in scribe so ignoring this for the time being
                case PaperKey.Tab:
                    if (!settings.ReadOnly)
                    {
                        if (state.HasSelection) state.DeleteSelection();
                        
                        // Check max length
                        if (settings.MaxLength == 0 || state.Value.Length < settings.MaxLength)
                        {
                            state.Value = state.Value.Insert(state.CursorPosition, "\t");
                            state.CursorPosition++;
                            valueChanged = true;
                        }
                    }
                    break;
                    
                case PaperKey.Enter when state.IsMultiLine:
                    if (state.HasSelection) state.DeleteSelection();
                    
                    // Check max length
                    // Check max length and read-only
                    if (!settings.ReadOnly && (settings.MaxLength == 0 || state.Value.Length < settings.MaxLength))
                    {
                        state.Value = state.Value.Insert(state.CursorPosition, "\n");
                        state.CursorPosition++;
                        valueChanged = true;
                    }
                    break;
                    
                case PaperKey.Up when state.IsMultiLine:
                    MoveCursorVertical(ref state, -1, settings);
                    break;
                    
                case PaperKey.Down when state.IsMultiLine:
                    MoveCursorVertical(ref state, 1, settings);
                    break;
            }
            
            return valueChanged;
        }

        /// <summary>
        /// Creates a single-line text field control that allows users to input and edit text.
        /// </summary>
        /// <param name="value">Current text value</param>
        /// <param name="settings">Text input settings</param>
        /// <param name="onChange">Optional callback when the text changes</param>
        /// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the text field</returns>
        public ElementBuilder TextField(
            string value,
            TextInputSettings settings,
            Action<string> onChange = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int intID = 0)
        {
            return CreateTextInput(value, settings, onChange, false, intID);
        }

        /// <summary>
        /// Creates a single-line text field control with simple parameters.
        /// For more control, use the overload that takes TextInputSettings.
        /// </summary>
        /// <param name="value">Current text value</param>
        /// <param name="font">Font used to render the text</param>
        /// <param name="onChange">Optional callback when the text changes</param>
        /// <param name="placeholder">Optional placeholder text shown when the field is empty</param>
        /// <param name="textColor">Color of the text</param>
        /// <param name="placeholderColor">Color of the placeholder text</param>
        /// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the text field</returns>
        public ElementBuilder TextField(
            string value,
            FontFile font,
            Action<string> onChange = null,
            Color? textColor = null,
            string placeholder = "",
            Color? placeholderColor = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int intID = 0)
        {
            var settings = TextInputSettings.Default;
            settings.Font = font;
            settings.TextColor = textColor ?? settings.TextColor;
            settings.Placeholder = placeholder;
            settings.PlaceholderColor = placeholderColor ?? settings.PlaceholderColor;
            
            return CreateTextInput(value, settings, onChange, false, intID);
        }

        /// <summary>
        /// Creates a multi-line text area control that allows users to input and edit text with vertical scrolling.
        /// </summary>
        /// <param name="value">Current text value</param>
        /// <param name="settings">Text input settings</param>
        /// <param name="onChange">Optional callback when the text changes</param>
        /// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the text area</returns>
        public ElementBuilder TextArea(
            string value,
            TextInputSettings settings,
            Action<string> onChange = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int intID = 0)
        {
            return CreateTextInput(value, settings, onChange, true, intID);
        }

        /// <summary>
        /// Creates a multi-line text area control with simple parameters.
        /// For more control, use the overload that takes TextInputSettings.
        /// </summary>
        /// <param name="value">Current text value</param>
        /// <param name="font">Font used to render the text</param>
        /// <param name="onChange">Optional callback when the text changes</param>
        /// <param name="placeholder">Optional placeholder text shown when the area is empty</param>
        /// <param name="textColor">Color of the text</param>
        /// <param name="placeholderColor">Color of the placeholder text</param>
        /// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the text area</returns>
        public ElementBuilder TextArea(
            string value,
            FontFile font,
            Action<string> onChange = null,
            string placeholder = "",
            Color? textColor = null,
            Color? placeholderColor = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int intID = 0)
        {
            var settings = TextInputSettings.Default;
            settings.Font = font;
            settings.TextColor = textColor ?? settings.TextColor;
            settings.Placeholder = placeholder;
            settings.PlaceholderColor = placeholderColor ?? settings.PlaceholderColor;
            
            return CreateTextInput(value, settings, onChange, true, intID);
        }

        private ElementBuilder CreateTextInput(
            string value,
            TextInputSettings settings,
            Action<string> onChange,
            bool isMultiLine,
            int intID)
        {
            Clip();

            if(_paper.IsParentFocused && isMultiLine)
            {
                // If a text input field is focused and its a Multi-Line input field, Then Tab navigation is disabled
                // Since we want tab to actually add the tab character
                _paper.SkipKeyboardNavigation = true;
            }

            // Initialize state
            var state = LoadTextInputState(value, isMultiLine);

            if (isMultiLine)
            {
                ContentSizer((width, height) =>
                {
                    var currentState = LoadTextInputState(value, isMultiLine);
                    var textSettings = CreateTextLayoutSettings(settings, true, (float)(width ?? float.MaxValue));
                    var textLayout = _paper.CreateLayout(currentState.Value, textSettings);

                    return (width ?? textSettings.PixelSize, Math.Max(height ?? textSettings.PixelSize * textSettings.LineHeight, textLayout.Size.Y));
                });
            }

            // Handle focus changes
            OnFocusChange((FocusEvent e) =>
            {
                var currentState = LoadTextInputState(value, isMultiLine);
                currentState.IsFocused = e.IsFocused;
                
                if (e.IsFocused)
                {
                    currentState.CursorPosition = currentState.Value.Length;
                    currentState.ClearSelection();
                    EnsureCursorVisible(ref currentState, settings, isMultiLine);
                }
                
                SaveTextInputState(currentState);
            });
        
            // Handle mouse clicks for cursor positioning and Shift+Click range selection
            OnPress((ClickEvent e) =>
            {
                var currentState = LoadTextInputState(value, isMultiLine);
                var clickPos = e.RelativePosition.x + currentState.ScrollOffsetX;
                var clickPosY = isMultiLine ? e.RelativePosition.y + currentState.ScrollOffsetY : 0;
                var newPosition = Math.Clamp(
                    CalculateTextPosition(currentState.Value, settings, isMultiLine, clickPos, clickPosY),
                    0, currentState.Value.Length);

                if (IsShiftPressed())
                {
                    // Shift+Click: Extend or create selection to clicked position
                    if (currentState.SelectionStart < 0)
                    {
                        // Start new selection from current cursor position
                        currentState.SelectionStart = currentState.CursorPosition;
                    }
                    currentState.SelectionEnd = newPosition;
                    currentState.CursorPosition = newPosition;
                }
                else
                {
                    // Regular click: Place cursor and clear selection
                    currentState.CursorPosition = newPosition;
                    currentState.ClearSelection();
                }
                
                EnsureCursorVisible(ref currentState, settings, isMultiLine);
                SaveTextInputState(currentState);
            });

            // Handle double-click for word selection
            OnDoubleClick((ClickEvent e) =>
            {
                var currentState = LoadTextInputState(value, isMultiLine);
                var clickPos = e.RelativePosition.x + currentState.ScrollOffsetX;
                var clickPosY = isMultiLine ? e.RelativePosition.y + currentState.ScrollOffsetY : 0;
                var clickPosition = Math.Clamp(
                    CalculateTextPosition(currentState.Value, settings, isMultiLine, clickPos, clickPosY),
                    0, currentState.Value.Length);

                // Select the word at the clicked position
                var (wordStart, wordEnd) = FindWordBoundaries(currentState.Value, clickPosition);
                if (wordStart != wordEnd)
                {
                    currentState.SelectionStart = wordStart;
                    currentState.SelectionEnd = wordEnd;
                    currentState.CursorPosition = wordEnd;
                    EnsureCursorVisible(ref currentState, settings, isMultiLine);
                    SaveTextInputState(currentState);
                }
            });
        
            // Handle dragging for text selection
            OnDragStart((DragEvent e) =>
            {
                var currentState = LoadTextInputState(value, isMultiLine);
                var dragPos = e.RelativePosition.x + currentState.ScrollOffsetX;
                var dragPosY = isMultiLine ? e.RelativePosition.y + currentState.ScrollOffsetY : 0;
                var pos = Math.Clamp(CalculateTextPosition(currentState.Value, settings, isMultiLine, dragPos, dragPosY), 0, currentState.Value.Length);
                
                currentState.CursorPosition = pos;
                currentState.SelectionStart = pos;
                currentState.SelectionEnd = pos;
                EnsureCursorVisible(ref currentState, settings, isMultiLine);
                SaveTextInputState(currentState);
            });
        
            OnDragging((DragEvent e) =>
            {
                var currentState = LoadTextInputState(value, isMultiLine);
                if (currentState.SelectionStart < 0) return;
                
                // Auto-scroll when dragging near edges
                const double edgeScrollSensitivity = 20.0;
                const double scrollSpeed = 2.0;
                
                if (e.RelativePosition.x < edgeScrollSensitivity)
                    currentState.ScrollOffsetX = Math.Max(0, currentState.ScrollOffsetX - scrollSpeed);
                else if (e.RelativePosition.x > e.ElementRect.width - edgeScrollSensitivity)
                    currentState.ScrollOffsetX += scrollSpeed;
                
                if (isMultiLine)
                {
                    if (e.RelativePosition.y < edgeScrollSensitivity)
                        currentState.ScrollOffsetY = Math.Max(0, currentState.ScrollOffsetY - scrollSpeed);
                    else if (e.RelativePosition.y > e.ElementRect.height - edgeScrollSensitivity)
                        currentState.ScrollOffsetY += scrollSpeed;
                }
                
                // Clamp scroll offsets after auto-scroll
                var layoutSettings = CreateTextLayoutSettings(settings, isMultiLine, e.ElementRect.width);
                var textLayout = _paper.CreateLayout(currentState.Value, layoutSettings);
                double visibleWidth = e.ElementRect.width;
                double visibleHeight = e.ElementRect.height;
                currentState.ClampScrollOffsets(textLayout.Size.X, textLayout.Size.Y, visibleWidth, visibleHeight);
                
                var dragPos = e.RelativePosition.x + currentState.ScrollOffsetX;
                var dragPosY = isMultiLine ? e.RelativePosition.y + currentState.ScrollOffsetY : 0;
                var pos = Math.Clamp(CalculateTextPosition(currentState.Value, settings, isMultiLine, dragPos, dragPosY), 0, currentState.Value.Length);
                
                currentState.CursorPosition = pos;
                currentState.SelectionEnd = pos;
                EnsureCursorVisible(ref currentState, settings, isMultiLine);
                SaveTextInputState(currentState);
            });
        
            // Handle keyboard input  
            OnKeyPressed((KeyEvent e) =>
            {
                var currentState = LoadTextInputState(value, isMultiLine);
                if (!currentState.IsFocused) return;
                
                bool valueChanged = ProcessKeyCommand(ref currentState, e.Key, settings);
                
                EnsureCursorVisible(ref currentState, settings, isMultiLine);
                SaveTextInputState(currentState);
                
                if (valueChanged)
                    onChange?.Invoke(currentState.Value);
            });
        
            // Handle character input
            OnTextInput((TextInputEvent e) =>
            {
                var currentState = LoadTextInputState(value, isMultiLine);
                if (!currentState.IsFocused || char.IsControl(e.Character) || settings.ReadOnly) return;
                
                // Check max length
                if (settings.MaxLength > 0 && currentState.Value.Length >= settings.MaxLength && !currentState.HasSelection)
                    return;
                
                if (currentState.HasSelection) currentState.DeleteSelection();
                
                // For single-line, don't allow newlines
                if (!isMultiLine && (e.Character == '\n' || e.Character == '\r'))
                    return;
                
                currentState.Value = currentState.Value.Insert(currentState.CursorPosition, e.Character.ToString());
                currentState.CursorPosition++;
                
                EnsureCursorVisible(ref currentState, settings, isMultiLine);
                SaveTextInputState(currentState);
                onChange?.Invoke(currentState.Value);
            });

            // Render cursor and selection
            OnPostLayout((ElementHandle elHandle, Rect rect) =>
            {
                _paper.AddActionElement(ref elHandle, (canvas, r) =>
                {
                    var renderState = LoadTextInputState(value, isMultiLine);
                    var layoutSettings = CreateTextLayoutSettings(settings, isMultiLine, r.width);
                    
                    canvas.SaveState();
                    canvas.TransformBy(Transform2D.CreateTranslation(-renderState.ScrollOffsetX, -renderState.ScrollOffsetY));

                    var fontSize = (double)elHandle.Data._elementStyle.GetValue(GuiProp.FontSize);

                    // Draw text or placeholder
                    if (string.IsNullOrEmpty(renderState.Value))
                    {
                        canvas.DrawText(settings.Placeholder, (float)(r.x), (float)r.y, settings.PlaceholderColor, layoutSettings);
                    }
                    else
                    {
                        canvas.DrawText(renderState.Value, (float)(r.x), (float)r.y, settings.TextColor, layoutSettings);
                    }
                    
                    // Draw selection and cursor if focused
                    if (renderState.IsFocused)
                    {
                        _paper.CaptureKeyboard();
                        
                        // Draw selection background
                        if (renderState.HasSelection)
                        {
                            int start = Math.Min(renderState.SelectionStart, renderState.SelectionEnd);
                            int end = Math.Max(renderState.SelectionStart, renderState.SelectionEnd);
                            
                            var textLayout = _paper.CreateLayout(renderState.Value, layoutSettings);
                            var startPos = textLayout.GetCursorPosition(start);
                            var endPos = textLayout.GetCursorPosition(end);
                            
                            canvas.SetFillColor(Color.FromArgb(100, 100, 150, 255));
                            
                            if (isMultiLine && Math.Abs(endPos.Y - startPos.Y) > fontSize / 2)
                            {
                                // Multi-line selection: Draw rectangles for each line
                                double lineHeight = fontSize * layoutSettings.LineHeight;
                                double currentY = startPos.Y;
                                
                                // Get line indices from Y positions
                                int startLineIndex = (int)(startPos.Y / lineHeight);
                                int endLineIndex = (int)(endPos.Y / lineHeight);

                                // First line: from start position to end of line
                                double firstLineWidth = startLineIndex < textLayout.Lines.Count ? textLayout.Lines[startLineIndex].Width : 0;
                                
                                canvas.BeginPath();
                                canvas.RoundedRect(
                                    r.x + startPos.X,
                                    r.y + currentY,
                                    firstLineWidth - startPos.X,
                                    lineHeight,
                                    2, 2, 2, 2);
                                canvas.Fill();
                                
                                // Middle lines: use actual line widths from textLayout
                                currentY += lineHeight;
                                int currentLineIndex = startLineIndex + 1;
                                while (currentY < endPos.Y && currentLineIndex < textLayout.Lines.Count)
                                {
                                    float lineWidth = textLayout.Lines[currentLineIndex].Width;
                                    
                                    canvas.BeginPath();
                                    canvas.RoundedRect(
                                        r.x,
                                        r.y + currentY,
                                        lineWidth,
                                        lineHeight,
                                        2, 2, 2, 2);
                                    canvas.Fill();
                                    currentY += lineHeight;
                                    currentLineIndex++;
                                }
                                
                                // Last line: from start of line to end position
                                if (endPos.X > 0)
                                {
                                    canvas.BeginPath();
                                    canvas.RoundedRect(
                                        r.x,
                                        r.y + endPos.Y,
                                        endPos.X,
                                        lineHeight,
                                        2, 2, 2, 2);
                                    canvas.Fill();
                                }
                            }
                            else
                            {
                                // Single-line selection: Draw one rectangle
                                canvas.BeginPath();
                                canvas.RoundedRect(
                                    r.x + startPos.X, 
                                    r.y + startPos.Y, 
                                    endPos.X - startPos.X,
                                    fontSize, 
                                    2, 2, 2, 2);
                                canvas.Fill();
                            }
                        }
                        
                        // Draw blinking cursor
                        if ((int)(_paper.Time * 2) % 2 == 0)
                        {
                            var textLayout = _paper.CreateLayout(renderState.Value, layoutSettings);
                            var cursorPos = textLayout.GetCursorPosition(renderState.CursorPosition);
                            double cursorX = r.x + cursorPos.X;
                            double cursorY = r.y + cursorPos.Y;
                            
                            canvas.BeginPath();
                            canvas.MoveTo(cursorX, cursorY);
                            canvas.LineTo(cursorX, cursorY + fontSize);
                            canvas.SetStrokeColor(settings.TextColor);
                            canvas.SetStrokeWidth(1);
                            canvas.Stroke();
                        }
                    }
                    
                    canvas.RestoreState();
                });
            });

            return this;
        }
        
        // Helper methods for text field functionality
        
        /// <summary>
        /// Ensures the cursor is visible by adjusting scroll position if needed.
        /// </summary>
        private void EnsureCursorVisible(ref TextInputState state, TextInputSettings settings, bool isMultiLine)
        {
            if (isMultiLine)
            {
                // For multi-line, we need both horizontal and vertical scrolling
                var textLayout = _paper.CreateLayout(state.Value, CreateTextLayoutSettings(settings, true, _handle.Data.LayoutWidth));
                var cursorPos = textLayout.GetCursorPosition(state.CursorPosition);
                
                double visibleWidth = _handle.Data.LayoutWidth;
                double visibleHeight = _handle.Data.LayoutHeight;
                
                const double margin = 10.0;
                
                // Horizontal scrolling
                if (cursorPos.X < state.ScrollOffsetX + margin)
                    state.ScrollOffsetX = Math.Max(0, cursorPos.X - margin);
                else if (cursorPos.X > state.ScrollOffsetX + visibleWidth - margin)
                    state.ScrollOffsetX = cursorPos.X - visibleWidth + margin;
                
                // Vertical scrolling
                if (cursorPos.Y < state.ScrollOffsetY + margin)
                    state.ScrollOffsetY = Math.Max(0, cursorPos.Y - margin);
                else if (cursorPos.Y > state.ScrollOffsetY + visibleHeight - margin)
                    state.ScrollOffsetY = cursorPos.Y - visibleHeight + margin;

                // Clamp scroll offsets to content bounds
                state.ClampScrollOffsets(textLayout.Size.X, textLayout.Size.Y, visibleWidth, visibleHeight);
            }
            else
            {
                // Single-line horizontal scrolling only
                var fontSize = (double)_handle.Data._elementStyle.GetValue(GuiProp.FontSize);
                var letterSpacing = (double)_handle.Data._elementStyle.GetValue(GuiProp.FontSize);
                var cursorPos = GetCursorPositionFromIndex(state.Value, settings.Font, fontSize, letterSpacing, state.CursorPosition);
                
                double visibleWidth = _handle.Data.LayoutWidth;
                const double margin = 20.0;
                
                if (cursorPos.x < state.ScrollOffsetX + margin)
                    state.ScrollOffsetX = Math.Max(0, cursorPos.x - margin);
                else if (cursorPos.x > state.ScrollOffsetX + visibleWidth - margin)
                    state.ScrollOffsetX = cursorPos.x - visibleWidth + margin;

                // Clamp horizontal scroll offset for single-line
                var textSize = _paper.MeasureText(state.Value, CreateTextLayoutSettings(settings, false, double.MaxValue));
                state.ClampScrollOffsets(textSize.x, textSize.y, visibleWidth, _handle.Data.LayoutHeight);
            }
        }
        
        /// <summary>
        /// Calculates the closest text position based on coordinates using TextLayout.
        /// </summary>
        private int CalculateTextPosition(string text, TextInputSettings settings, bool isMultiLine, double x, double y = 0)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var maxWidth = isMultiLine ? _handle.Data.LayoutWidth : double.MaxValue;
            var textLayout = _paper.CreateLayout(text, CreateTextLayoutSettings(settings, isMultiLine, maxWidth));
            return textLayout.GetCursorIndex(new Vector2(x, y));
        }
        
        /// <summary>
        /// Calculates the cursor position for a specific character index using TextLayout.
        /// </summary>
        private Vector2 GetCursorPositionFromIndex(string text, FontFile font, double fontSize, double letterSpacing, int index)
        {
            if (string.IsNullOrEmpty(text) || index <= 0) return Vector2.zero;
            var settings = TextLayoutSettings.Default;
            settings.Font = font;
            settings.PixelSize = (float)fontSize;
            settings.LetterSpacing = (float)letterSpacing;
            settings.MaxWidth = float.MaxValue;
            var textLayout = _paper.CreateLayout(text, settings);
            return textLayout.GetCursorPosition(index);
        }
        
        #endregion

        /// <summary>
        /// Begins a new parent scope with this element as the parent.
        /// Used with 'using' statements to create a hierarchical UI structure.
        /// </summary>
        /// <returns>This builder as an IDisposable to be used with 'using' statements</returns>
        public IDisposable Enter()
        {
            var currentParent = _paper.CurrentParent;
            if (currentParent.Equals(_handle))
                throw new InvalidOperationException("Cannot enter the same element twice.");

            // Push this element onto the stack
            _paper._elementStack.Push(_handle);

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
