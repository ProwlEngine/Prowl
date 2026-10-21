using System.Collections;
using System.Collections.Generic;

namespace Prowl.Warp;

/// <summary>A live, read-only peer view. Direct foreach enumeration does not allocate.</summary>
/// <remarks>Enumeration through IEnumerable interfaces boxes the enumerator. Membership must not change during enumeration.</remarks>
public readonly struct NetPeerCollection : IReadOnlyCollection<NetPeer>
{
    private readonly Dictionary<int, NetPeer>.ValueCollection? _values;

    internal NetPeerCollection(Dictionary<int, NetPeer>.ValueCollection values) => _values = values;

    public int Count => _values?.Count ?? 0;

    public Enumerator GetEnumerator() => new(_values);

    IEnumerator<NetPeer> IEnumerable<NetPeer>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<NetPeer>
    {
        private Dictionary<int, NetPeer>.ValueCollection.Enumerator _inner;
        private readonly bool _hasValues;
        internal Enumerator(Dictionary<int, NetPeer>.ValueCollection? values)
        {
            _hasValues = values != null;
            _inner = values?.GetEnumerator() ?? default;
        }
        public NetPeer Current => _inner.Current;
        object IEnumerator.Current => Current;
        public bool MoveNext() => _hasValues && _inner.MoveNext();
        public void Dispose() => _inner.Dispose();
        void IEnumerator.Reset() => throw new System.NotSupportedException();
    }
}
