namespace NeoVeldrid.D3D11
{
    internal class D3D11ResourceLayout : ResourceLayout
    {
        private readonly ResourceLayoutElementDescription[] _elements;
        private string _name;
        private bool _disposed;

        public ResourceLayoutElementDescription[] Elements => _elements;

        public D3D11ResourceLayout(ref ResourceLayoutDescription description)
            : base(ref description)
        {
            _elements = Util.ShallowClone(description.Elements);
        }

        public override string Name
        {
            get => _name;
            set => _name = value;
        }

        public override bool IsDisposed => _disposed;

        public override void Dispose()
        {
            _disposed = true;
        }
    }
}
