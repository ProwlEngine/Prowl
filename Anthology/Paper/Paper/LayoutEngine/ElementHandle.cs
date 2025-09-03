namespace Prowl.PaperUI.LayoutEngine
{
    public readonly struct ElementHandle : IEquatable<ElementHandle>
    {
        public readonly Paper Owner;
        public readonly int Index;

        public ElementHandle(Paper gui, int index)
        {
            Owner = gui;
            Index = index;
        }

        public bool IsValid => Owner != null && Index >= 0 && Index < Owner.ElementCount;

        public ref ElementData Data => ref Owner.GetElementData(Index);

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

        public static implicit operator bool(ElementHandle handle) => handle.IsValid;
    }
}
