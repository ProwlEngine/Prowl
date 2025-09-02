namespace Prowl.PaperUI.LayoutEngine
{
    public readonly struct ElementHandle : IEquatable<ElementHandle>
    {
        public readonly Paper GUI;
        public readonly int Index;

        public ElementHandle(Paper gui, int index)
        {
            GUI = gui;
            Index = index;
        }

        public bool IsValid => GUI != null && Index >= 0 && Index < GUI.ElementCount;

        public ref ElementData Data => ref GUI.GetElementData(Index);

        public bool Equals(ElementHandle other) => GUI == other.GUI && Index == other.Index;

        public override bool Equals(object obj) => obj is ElementHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(GUI, Index);

        public static bool operator ==(ElementHandle left, ElementHandle right) => left.Equals(right);

        public static bool operator !=(ElementHandle left, ElementHandle right) => !left.Equals(right);

        public static implicit operator bool(ElementHandle handle) => handle.IsValid;
    }
}
