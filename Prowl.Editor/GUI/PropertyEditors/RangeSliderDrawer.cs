// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// Draws a float or int as a slider while its field's [Range] is in scope, and defers to the
/// built-in numeric drawer the rest of the time. Swapping only the control lets the property grid
/// keep drawing the row and label, so [Range] fields line up with every other field instead of
/// following a hand-copied row recipe that drifts as the grid's metrics change.
/// </summary>
public sealed class RangeSliderDrawer : OrigamiUI.FieldDrawer
{
    /// <summary>The [Range] of the field being drawn right now, published by
    /// <see cref="RangeAttributeHandler"/> around its one <c>DrawField</c> call. The grid draws
    /// fields one at a time on the UI thread, so at most one is ever in flight.</summary>
    [ThreadStatic] public static RangeAttribute? Pending;

    private readonly OrigamiUI.FieldDrawer _inner;

    private RangeSliderDrawer(OrigamiUI.FieldDrawer inner) => _inner = inner;

    /// <summary>Wraps the built-in float and int drawers, so must run after
    /// <c>BuiltInFieldDrawers.Register</c> has put them in the registry.</summary>
    public static void Register(OrigamiUI.FieldDrawerRegistry drawers)
    {
        Wrap(typeof(float));
        Wrap(typeof(int));

        void Wrap(Type type)
        {
            OrigamiUI.FieldDrawer inner = drawers.GetDrawer(type)
                ?? throw new InvalidOperationException(
                    $"No built-in drawer for {type.Name} to wrap - register RangeSliderDrawer after BuiltInFieldDrawers.");
            drawers.Register(type, new RangeSliderDrawer(inner));
        }
    }

    public override void Draw(Paper paper, string id, object? value, Type type,
        Action<object?> onChange, int depth)
    {
        // Consume on read: one publish draws one slider, so a range can never bleed into the next
        // numeric field even if this drawer is reached by some path the handler doesn't bracket.
        RangeAttribute? range = Pending;
        Pending = null;

        if (range == null)
        {
            _inner.Draw(paper, id, value, type, onChange, depth);
            return;
        }

        if (type == typeof(float))
            OrigamiUI.Origami.Slider(paper, $"{id}_sl", (float)(value ?? 0f),
                v => onChange(v), range.Min, range.Max)
                .Format("F2").Show();
        else
            OrigamiUI.Origami.Slider(paper, $"{id}_sl", (float)(int)(value ?? 0),
                v => onChange((int)MathF.Round(v)), range.Min, range.Max)
                .Format("F0").Step(1f).Show();
    }
}
