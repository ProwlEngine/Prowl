using Prowl.Vector;

namespace NeoVeldrid.OpenGL.ManagedEntryList
{
    internal class ClearColorTargetEntry : OpenGLCommandEntry
    {
        public uint Index;
        public Color ClearColor;

        public ClearColorTargetEntry(uint index, Color clearColor)
        {
            Index = index;
            ClearColor = clearColor;
        }

        public ClearColorTargetEntry() { }

        public ClearColorTargetEntry Init(uint index, Color clearColor)
        {
            Index = index;
            ClearColor = clearColor;
            return this;
        }

        public override void ClearReferences()
        {
        }
    }
}
