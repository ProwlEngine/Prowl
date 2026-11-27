// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.PaperUI.LayoutEngine;

/// <summary> A lightweight handle referencing an element managed by a Paper instance. Provides safe read/write access to element data via the Data property. The handle is valid when Owner is non-null and Index is within range; it converts to bool for validity checks. </summary>
public readonly struct ElementHandle : IEquatable<ElementHandle>
{
    /// <summary> The Paper instance that owns this handle. </summary>
    public readonly Paper Owner;
    public readonly int Index;

    public ElementHandle(Paper gui, int index)
    {
        Owner = gui;
        Index = index;
    }

    public bool IsValid => Owner != null && Index >= 0 && Index < Owner.ElementCount;

    /// <summary> Gets a reference to the element data for the element this handle identifies. </summary>
    public ref ElementData Data => ref Owner.GetElementData(Index);

    /// <summary> Returns a handle to the parent element, or an invalid handle if this element has no parent or is invalid. </summary>
    public ElementHandle GetParentHandle()
    {
        if (!IsValid || Data.ParentIndex == -1)
            return default;

        return new ElementHandle(Owner, Data.ParentIndex);
    }

    public bool Equals(ElementHandle other) => Owner == other.Owner && Index == other.Index;

    public override bool Equals(object obj) => obj is ElementHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Owner, Index);

    public static bool operator ==(ElementHandle left, ElementHandle right) => left.Equals(right);

    public static bool operator !=(ElementHandle left, ElementHandle right) => !left.Equals(right);

    /// <summary> Returns true if the handle is valid (the Owner is not null and Index is within the Owner's element range). Enables using an ElementHandle in a boolean context. </summary>
    public static implicit operator bool(ElementHandle handle) => handle.IsValid;
}
