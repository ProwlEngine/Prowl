using System;
using System.Collections.Generic;

namespace Prowl.Drift
{
    public class SpatialHash<T>
    {
        private readonly float _cellSize;
        private readonly Dictionary<(int, int), List<T>> _cells = new();

        public SpatialHash(float cellSize)
        {
            _cellSize = cellSize;
        }

        private (int, int) GetCellCoord(Vec2 pos)
        {
            int x = (int)MathF.Floor(pos.X / _cellSize);
            int y = (int)MathF.Floor(pos.Y / _cellSize);
            return (x, y);
        }

        public void Clear()
        {
            _cells.Clear();
        }

        public void Insert(T item, Bounds bounds)
        {
            var min = GetCellCoord(bounds.Mins);
            var max = GetCellCoord(bounds.Maxs);
            for (int x = min.Item1; x <= max.Item1; x++)
            {
                for (int y = min.Item2; y <= max.Item2; y++)
                {
                    var key = (x, y);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<T>();
                        _cells[key] = list;
                    }
                    list.Add(item);
                }
            }
        }

        public IEnumerable<List<T>> GetPotentialPairs(Bounds bounds)
        {

            var min = GetCellCoord(bounds.Mins);
            var max = GetCellCoord(bounds.Maxs);
            for (int x = min.Item1; x <= max.Item1; x++)
            {
                for (int y = min.Item2; y <= max.Item2; y++)
                {
                    var key = (x, y);
                    if (_cells.TryGetValue(key, out var list) && list.Count > 0)
                        yield return list;
                }
            }
        }
    }
}
