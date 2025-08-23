namespace Prowl.Scribe
{
    sealed class LruCache<K, V> where K : notnull
    {
        int _capacity;
        readonly Dictionary<K, LinkedListNode<(K key, V val)>> _map = new();
        readonly LinkedList<(K key, V val)> _lru = new();

        public LruCache(int capacity) { _capacity = Math.Max(1, capacity); }
        public int Capacity { get => _capacity; set { _capacity = Math.Max(1, value); Trim(); } }

        public bool TryGetValue(K k, out V v)
        {
            if (_map.TryGetValue(k, out var node))
            {
                _lru.Remove(node); _lru.AddFirst(node);
                v = node.Value.val; return true;
            }
            v = default!; return false;
        }

        public void Add(K k, V v)
        {
            if (_map.TryGetValue(k, out var node))
            {
                node.Value = (k, v);
                _lru.Remove(node); _lru.AddFirst(node);
            }
            else
            {
                var nn = new LinkedListNode<(K, V)>((k, v));
                _lru.AddFirst(nn);
                _map[k] = nn;
                Trim();
            }
        }

        void Trim()
        {
            while (_map.Count > _capacity)
            {
                var last = _lru.Last; if (last == null) break;
                _map.Remove(last.Value.key);
                _lru.RemoveLast();
            }
        }

        public void Clear() { _map.Clear(); _lru.Clear(); }
    }
}
