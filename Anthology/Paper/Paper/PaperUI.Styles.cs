using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI;
using System.Drawing;
using System.Numerics;
using Prowl.PaperUI;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Defines all available style properties for UI elements.
    /// </summary>
    public enum GuiProp
    {
        #region Visual Properties
        BackgroundColor,
        BorderColor,
        BorderWidth,
        Rounded,
        #endregion

        #region Layout Properties
        // Core sizing
        AspectRatio,
        Width,
        Height,
        MinWidth,
        MaxWidth,
        MinHeight,
        MaxHeight,

        // Positioning
        Left,
        Right,
        Top,
        Bottom,
        MinLeft,
        MaxLeft,
        MinRight,
        MaxRight,
        MinTop,
        MaxTop,
        MinBottom,
        MaxBottom,

        // Child layout
        ChildLeft,
        ChildRight,
        ChildTop,
        ChildBottom,

        // Spacing
        RowBetween,
        ColBetween,

        // Border spacing
        BorderLeft,
        BorderRight,
        BorderTop,
        BorderBottom,
        #endregion

        #region Transform Properties
        TranslateX,
        TranslateY,
        ScaleX,
        ScaleY,
        Rotate,
        OriginX,
        OriginY,
        SkewX,
        SkewY,
        Transform,
        #endregion
    }

    /// <summary>
    /// Builds transformation matrices for UI elements.
    /// </summary>
    public class TransformBuilder
    {
        #region Fields

        private float _translateX = 0;
        private float _translateY = 0;
        private float _scaleX = 1;
        private float _scaleY = 1;
        private float _rotate = 0;
        private float _skewX = 0;
        private float _skewY = 0;
        private float _originX = 0.5f; // Default to center (50%)
        private float _originY = 0.5f; // Default to center (50%)
        private Transform? _customTransform = null;

        #endregion

        #region Builder Methods

        /// <summary>
        /// Sets the X translation.
        /// </summary>
        public TransformBuilder SetTranslateX(float x)
        {
            _translateX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y translation.
        /// </summary>
        public TransformBuilder SetTranslateY(float y)
        {
            _translateY = y;
            return this;
        }

        /// <summary>
        /// Sets the X scale factor.
        /// </summary>
        public TransformBuilder SetScaleX(float x)
        {
            _scaleX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y scale factor.
        /// </summary>
        public TransformBuilder SetScaleY(float y)
        {
            _scaleY = y;
            return this;
        }

        /// <summary>
        /// Sets the rotation angle in degrees.
        /// </summary>
        public TransformBuilder SetRotate(float angleInDegrees)
        {
            _rotate = angleInDegrees;
            return this;
        }

        /// <summary>
        /// Sets the X skew angle.
        /// </summary>
        public TransformBuilder SetSkewX(float angle)
        {
            _skewX = angle;
            return this;
        }

        /// <summary>
        /// Sets the Y skew angle.
        /// </summary>
        public TransformBuilder SetSkewY(float angle)
        {
            _skewY = angle;
            return this;
        }

        /// <summary>
        /// Sets the X origin point (0-1 range).
        /// </summary>
        public TransformBuilder SetOriginX(float x)
        {
            _originX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y origin point (0-1 range).
        /// </summary>
        public TransformBuilder SetOriginY(float y)
        {
            _originY = y;
            return this;
        }

        /// <summary>
        /// Sets a custom transform to be applied.
        /// </summary>
        public TransformBuilder SetCustomTransform(Transform transform)
        {
            _customTransform = transform;
            return this;
        }

        #endregion

        #region Build Method

        /// <summary>
        /// Builds the final transform matrix following the order: translate, rotate, scale, skew.
        /// </summary>
        /// <param name="rect">The rectangle to transform.</param>
        /// <returns>The complete transformation matrix.</returns>
        public Transform Build(Rect rect)
        {
            // Calculate origin in actual pixels
            float originX = rect.X + _originX * rect.Width;
            float originY = rect.Y + _originY * rect.Height;

            // Create transformation matrix
            Transform result = Transform.Identity;

            // Create a matrix that transforms from origin
            Transform originMatrix = Transform.CreateTranslation(-originX, -originY);

            // Apply transforms in order: translate, rotate, scale, skew
            Transform transformMatrix = Transform.Identity;

            // 1. Translate
            if (_translateX != 0 || _translateY != 0)
                transformMatrix *= Transform.CreateTranslation(_translateX, _translateY);

            // 2. Rotate
            if (_rotate != 0)
                transformMatrix *= Transform.CreateRotate(_rotate);

            // 3. Scale
            if (_scaleX != 1 || _scaleY != 1)
                transformMatrix *= Transform.CreateScale(_scaleX, _scaleY);

            // 4. Skew
            if (_skewX != 0)
                transformMatrix *= Transform.CreateSkewX(_skewX);
            if (_skewY != 0)
                transformMatrix *= Transform.CreateSkewY(_skewY);

            // 5. Apply custom transform if specified
            if (_customTransform.HasValue)
                transformMatrix *= _customTransform.Value;

            // Complete transformation: move to origin, apply transform, move back from origin
            result = originMatrix * transformMatrix * Transform.CreateTranslation(originX, originY);

            return result;
        }

        #endregion
    }

    /// <summary>
    /// Configuration for property transitions.
    /// </summary>
    public class TransitionConfig
    {
        /// <summary>Duration of the transition in seconds.</summary>
        public float Duration { get; set; }

        /// <summary>Optional easing function to control transition timing.</summary>
        public Func<float, float>? EasingFunction { get; set; }
    }

    /// <summary>
    /// Manages styling and transitions for UI elements.
    /// </summary>
    internal class ElementStyle
    {
        #region Fields

        // State tracking
        private HashSet<GuiProp> _propertiesSetThisFrame = new();
        private HashSet<GuiProp> _propertiesWithTransitions = new();
        private bool _firstFrame = true;

        // Property values
        private Dictionary<GuiProp, object> _currentValues = new();
        private Dictionary<GuiProp, object> _targetValues = new();

        // Transition state
        private Dictionary<GuiProp, TransitionConfig> _transitionConfigs = new();
        private Dictionary<GuiProp, InterpolationState> _interpolations = new();

        // Inheritance
        private ElementStyle? _parent;

        #endregion

        #region Public Methods

        /// <summary>
        /// Marks the end of a frame, resetting per-frame state.
        /// </summary>
        public void EndOfFrame()
        {
            _propertiesSetThisFrame.Clear();
            _firstFrame = false;
        }

        /// <summary>
        /// Sets the parent style for inheritance.
        /// </summary>
        public void SetParent(ElementStyle? currentStyle)
        {
            _parent = currentStyle;
        }

        /// <summary>
        /// Checks if a property has a value.
        /// </summary>
        public bool HasValue(GuiProp property) => _currentValues.ContainsKey(property);

        /// <summary>
        /// Gets the current value of a property, falling back to parent or default.
        /// </summary>
        public object GetValue(GuiProp property)
        {
            // If we have the value, return it
            if (_currentValues.TryGetValue(property, out var value))
                return value;

            // Otherwise check parent
            if (_parent != null)
                return _parent.GetValue(property);

            // Otherwise return default
            return GetDefaultValue(property);
        }

        /// <summary>
        /// Sets a property value directly without transition.
        /// </summary>
        public void SetDirectValue(GuiProp property, object value)
        {
            _propertiesSetThisFrame.Add(property);

            // Set the value directly without transition
            _currentValues[property] = value;
            _targetValues[property] = value; // Ensure target matches current
            _interpolations.Remove(property); // Remove any existing interpolation state
        }

        /// <summary>
        /// Sets a property's target value for transition.
        /// </summary>
        public void SetNextValue(GuiProp property, object value)
        {
            _propertiesSetThisFrame.Add(property);

            // Store the target value - this is where we want to end up
            _targetValues[property] = value;
        }

        /// <summary>
        /// Configures a transition for a property.
        /// </summary>
        public void SetTransitionConfig(GuiProp property, float duration, Func<float, float>? easing = null)
        {
            // Store the transition configuration for this property
            _transitionConfigs[property] = new TransitionConfig {
                Duration = duration,
                EasingFunction = easing
            };

            // Mark this property as having a transition
            _propertiesWithTransitions.Add(property);
        }

        /// <summary>
        /// Removes a property value and any related transition state.
        /// </summary>
        public void ClearValue(GuiProp property)
        {
            _currentValues.Remove(property);
            _targetValues.Remove(property);
            _transitionConfigs.Remove(property);
            _interpolations.Remove(property);
        }

        /// <summary>
        /// Updates all property transitions for the current frame.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_firstFrame)
            {
                // Initialize values for properties with transitions
                InitializeTransitionProperties();
            }

            // Track completed transitions for cleanup
            List<GuiProp> completedInterpolations = new List<GuiProp>();

            // Process all properties that have target values
            foreach (var property in _targetValues.Keys)
            {
                // Get the target value based on what was set this frame or inherited
                object targetValue = GetTargetValue(property);

                // If the property has a transition config, set up an interpolation
                if (_transitionConfigs.TryGetValue(property, out var config))
                {
                    ProcessPropertyWithTransition(property, targetValue, config, deltaTime, completedInterpolations);
                }
                else
                {
                    // No transition config, set immediately
                    _currentValues[property] = targetValue;
                }
            }

            // Clean up completed interpolations
            foreach (var property in completedInterpolations)
            {
                _interpolations.Remove(property);
            }

            // Clear transition configs after processing - they don't persist across frames
            _transitionConfigs.Clear();
        }

        /// <summary>
        /// Gets the complete transform for an element.
        /// </summary>
        public Transform GetTransformForElement(Rect rect)
        {
            TransformBuilder builder = new TransformBuilder();

            // Set transform properties from the current values
            if (_currentValues.TryGetValue(GuiProp.TranslateX, out var translateX))
                builder.SetTranslateX((float)translateX);

            if (_currentValues.TryGetValue(GuiProp.TranslateY, out var translateY))
                builder.SetTranslateY((float)translateY);

            if (_currentValues.TryGetValue(GuiProp.ScaleX, out var scaleX))
                builder.SetScaleX((float)scaleX);

            if (_currentValues.TryGetValue(GuiProp.ScaleY, out var scaleY))
                builder.SetScaleY((float)scaleY);

            if (_currentValues.TryGetValue(GuiProp.Rotate, out var rotate))
                builder.SetRotate((float)rotate);

            if (_currentValues.TryGetValue(GuiProp.SkewX, out var skewX))
                builder.SetSkewX((float)skewX);

            if (_currentValues.TryGetValue(GuiProp.SkewY, out var skewY))
                builder.SetSkewY((float)skewY);

            if (_currentValues.TryGetValue(GuiProp.OriginX, out var originX))
                builder.SetOriginX((float)originX);

            if (_currentValues.TryGetValue(GuiProp.OriginY, out var originY))
                builder.SetOriginY((float)originY);

            if (_currentValues.TryGetValue(GuiProp.Transform, out var customTransform))
                builder.SetCustomTransform((Transform)customTransform);

            return builder.Build(rect);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Initializes values for properties with transitions.
        /// </summary>
        private void InitializeTransitionProperties()
        {
            foreach (var property in _propertiesWithTransitions)
            {
                // If we don't have a current value yet for a property with transition,
                // initialize it with the default or parent value
                if (!_currentValues.ContainsKey(property))
                {
                    if (_parent != null && _parent.HasValue(property))
                        _currentValues[property] = _parent.GetValue(property);
                    else
                        _currentValues[property] = GetDefaultValue(property);
                }
            }
        }

        /// <summary>
        /// Gets the target value for a property based on explicit setting or inheritance.
        /// </summary>
        private object GetTargetValue(GuiProp property)
        {
            if (_propertiesSetThisFrame.Contains(property)) // If property was set this frame, use the explicit value
                return _targetValues[property];
            else if (_parent != null && _parent.HasValue(property)) // If not set, but has parent, use parent value
                return _parent.GetValue(property);
            else // If not set and no parent, use default value
                return GetDefaultValue(property);
        }

        /// <summary>
        /// Processes transitions for a property.
        /// </summary>
        private void ProcessPropertyWithTransition(GuiProp property, object targetValue, TransitionConfig config,
            float deltaTime, List<GuiProp> completedInterpolations)
        {
            // If we don't have a current value yet, initialize it immediately
            if (!_currentValues.TryGetValue(property, out object? currentValue))
            {
                currentValue = targetValue;
                _currentValues[property] = currentValue;
                return;
            }

            // Skip if the values are already equal
            if (currentValue.Equals(targetValue))
                return;

            // Create or update interpolation state
            if (!_interpolations.TryGetValue(property, out var state))
            {
                state = new InterpolationState {
                    StartValue = currentValue,
                    TargetValue = targetValue,
                    Duration = config.Duration,
                    EasingFunction = config.EasingFunction,
                    CurrentTime = 0
                };
                _interpolations[property] = state;
            }
            else if (!state.TargetValue.Equals(targetValue))
            {
                // Target has changed, restart interpolation
                state.StartValue = currentValue;
                state.TargetValue = targetValue;
                state.Duration = config.Duration;
                state.EasingFunction = config.EasingFunction;
                state.CurrentTime = 0;
            }

            // Update the interpolation
            state.CurrentTime += deltaTime;

            if (state.CurrentTime >= state.Duration)
            {
                // Interpolation complete
                _currentValues[property] = targetValue;
                completedInterpolations.Add(property);
            }
            else
            {
                // Calculate interpolated value
                float t = state.CurrentTime / state.Duration;
                if (state.EasingFunction != null)
                    t = state.EasingFunction(t);

                _currentValues[property] = Interpolate(state.StartValue, state.TargetValue, t);
            }
        }

        /// <summary>
        /// Interpolates between two values based on their type.
        /// </summary>
        private object Interpolate(object start, object end, float t)
        {
            if (start is float floatStart && end is float floatEnd)
            {
                return floatStart + (floatEnd - floatStart) * t;
            }
            else if (start is int intStart && end is int intEnd)
            {
                return intStart + (int)((intEnd - intStart) * t);
            }
            else if (start is Color colorStart && end is Color colorEnd)
            {
                return InterpolateColor(colorStart, colorEnd, t);
            }
            else if (start is Vector2 vectorStart && end is Vector2 vectorEnd)
            {
                return Vector2.Lerp(vectorStart, vectorEnd, t);
            }
            else if (start is Vector3 vector3Start && end is Vector3 vector3End)
            {
                return Vector3.Lerp(vector3Start, vector3End, t);
            }
            else if (start is Vector4 vector4Start && end is Vector4 vector4End)
            {
                return Vector4.Lerp(vector4Start, vector4End, t);
            }
            else if (start is UnitValue unitStart && end is UnitValue unitEnd)
            {
                return UnitValue.Lerp(unitStart, unitEnd, t);
            }
            else if (start is Transform transformStart && end is Transform transformEnd)
            {
                return Transform.Lerp(transformStart, transformEnd, t);
            }

            // Default to just returning the end value
            return end;
        }

        /// <summary>
        /// Interpolates between two colors.
        /// </summary>
        private Color InterpolateColor(Color start, Color end, float t)
        {
            int r = (int)(start.R + (end.R - start.R) * t);
            int g = (int)(start.G + (end.G - start.G) * t);
            int b = (int)(start.B + (end.B - start.B) * t);
            int a = (int)(start.A + (end.A - start.A) * t);

            return Color.FromArgb(a, r, g, b);
        }

        /// <summary>
        /// Gets the default value for a property.
        /// </summary>
        private static object GetDefaultValue(GuiProp property)
        {
            return property switch {
                // Visual Properties
                GuiProp.BackgroundColor => Color.Transparent,
                GuiProp.BorderColor => Color.Transparent,
                GuiProp.BorderWidth => 0f,
                GuiProp.Rounded => new Vector4(0, 0, 0, 0),

                // Core Layout Properties
                GuiProp.AspectRatio => -1f,
                GuiProp.Width => UnitValue.Stretch(),
                GuiProp.Height => UnitValue.Stretch(),
                GuiProp.MinWidth => UnitValue.Pixels(0),
                GuiProp.MaxWidth => UnitValue.Pixels(float.MaxValue),
                GuiProp.MinHeight => UnitValue.Pixels(0),
                GuiProp.MaxHeight => UnitValue.Pixels(float.MaxValue),

                // Positioning Properties
                GuiProp.Left => UnitValue.Auto,
                GuiProp.Right => UnitValue.Auto,
                GuiProp.Top => UnitValue.Auto,
                GuiProp.Bottom => UnitValue.Auto,
                GuiProp.MinLeft => UnitValue.Pixels(0),
                GuiProp.MaxLeft => UnitValue.Pixels(float.MaxValue),
                GuiProp.MinRight => UnitValue.Pixels(0),
                GuiProp.MaxRight => UnitValue.Pixels(float.MaxValue),
                GuiProp.MinTop => UnitValue.Pixels(0),
                GuiProp.MaxTop => UnitValue.Pixels(float.MaxValue),
                GuiProp.MinBottom => UnitValue.Pixels(0),
                GuiProp.MaxBottom => UnitValue.Pixels(float.MaxValue),

                // Child Layout Properties
                GuiProp.ChildLeft => UnitValue.Auto,
                GuiProp.ChildRight => UnitValue.Auto,
                GuiProp.ChildTop => UnitValue.Auto,
                GuiProp.ChildBottom => UnitValue.Auto,
                GuiProp.RowBetween => UnitValue.Auto,
                GuiProp.ColBetween => UnitValue.Auto,
                GuiProp.BorderLeft => UnitValue.Pixels(0),
                GuiProp.BorderRight => UnitValue.Pixels(0),
                GuiProp.BorderTop => UnitValue.Pixels(0),
                GuiProp.BorderBottom => UnitValue.Pixels(0),

                // Transform Properties
                GuiProp.TranslateX => 0f,
                GuiProp.TranslateY => 0f,
                GuiProp.ScaleX => 1f,
                GuiProp.ScaleY => 1f,
                GuiProp.Rotate => 0f,
                GuiProp.SkewX => 0f,
                GuiProp.SkewY => 0f,
                GuiProp.OriginX => 0.5f, // Default is center
                GuiProp.OriginY => 0.5f, // Default is center
                GuiProp.Transform => Transform.Identity,

                _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Helper class to track interpolation state.
        /// </summary>
        private class InterpolationState
        {
            public object StartValue { get; set; }
            public object TargetValue { get; set; }
            public float Duration { get; set; }
            public Func<float, float>? EasingFunction { get; set; }
            public float CurrentTime { get; set; }
        }

        #endregion
    }

    /// <summary>
    /// Style management portion of the ImGui system.
    /// </summary>
    public static partial class Paper
    {
        #region Style Management

        /// <summary>
        /// A dictionary to keep track of active styles for each element.
        /// </summary>
        static Dictionary<ulong, ElementStyle> _activeStyles = new();

        /// <summary>
        /// Update the styles for all active elements.
        /// </summary>
        /// <param name="deltaTime">The time since the last frame.</param>
        /// <param name="element">The root element to start updating from.</param>
        private static void UpdateStyles(float deltaTime, Element element)
        {
            ulong id = element.ID;
            if (_activeStyles.TryGetValue(id, out var style))
            {
                // Update the style properties
                style.Update(deltaTime);
                element._elementStyle = style;
            }
            else
            {
                // Create a new style if it doesn't exist
                style = element._elementStyle ?? new ElementStyle();
                element._elementStyle = style;
                _activeStyles[id] = style;
            }

            // Update Children
            foreach (var child in element.Children)
                UpdateStyles(deltaTime, child);
        }

        /// <summary>
        /// Set a style property value (no transition).
        /// </summary>
        internal static void SetStyleProperty(ulong elementID, GuiProp property, object value)
        {
            if (!_activeStyles.TryGetValue(elementID, out var style))
            {
                // Create a new style if it doesn't exist
                style = new ElementStyle();
                _activeStyles[elementID] = style;
            }

            // Set the next value
            style.SetNextValue(property, value);
        }

        /// <summary>
        /// Configure a transition for a property.
        /// </summary>
        internal static void SetTransitionConfig(ulong elementID, GuiProp property, float duration, Func<float, float>? easing = null)
        {
            if (!_activeStyles.TryGetValue(elementID, out var style))
            {
                // Create a new style if it doesn't exist
                style = new ElementStyle();
                _activeStyles[elementID] = style;
            }

            // Set up the transition configuration
            style.SetTransitionConfig(property, duration, easing);
        }

        /// <summary>
        /// Clean up styles at the end of a frame.
        /// </summary>
        private static void OfOfFrameCleanupStyles(Dictionary<ulong, Element> createdElements)
        {
            // Clean up any elements that haven't been accessed this frame
            List<ulong> elementsToRemove = new List<ulong>();
            foreach (var kvp in _activeStyles)
            {
                if (!createdElements.ContainsKey(kvp.Key))
                    elementsToRemove.Add(kvp.Key);
                else
                    kvp.Value.EndOfFrame(); // Reset the style for the next frame
            }

            foreach (var id in elementsToRemove)
                _activeStyles.Remove(id);
        }

        #endregion
    }
}