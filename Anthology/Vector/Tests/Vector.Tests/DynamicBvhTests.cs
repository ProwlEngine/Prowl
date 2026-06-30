// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;
using Xunit;

namespace Prowl.Vector.Tests
{
    public class DynamicBvhTests
    {
        private static float Range(RNG rng, float a, float b) => a + rng.NextFloat() * (b - a);

        private static AABB BoxAt(Float3 c, float half) => new AABB(c - new Float3(half), c + new Float3(half));

        [Fact]
        public void QueryAABB_ReturnsAllTrueOverlaps()
        {
            var rng = new RNG(2024);
            var tree = new DynamicBvh<int>(margin: 0.0f);
            var tight = new List<AABB>();
            for (int i = 0; i < 300; i++)
            {
                var c = new Float3(Range(rng, -40, 40), Range(rng, -40, 40), Range(rng, -40, 40));
                var box = BoxAt(c, Range(rng, 0.5f, 3f));
                tight.Add(box);
                tree.Add(i, box);
            }

            for (int q = 0; q < 40; q++)
            {
                var c = new Float3(Range(rng, -40, 40), Range(rng, -40, 40), Range(rng, -40, 40));
                var query = BoxAt(c, Range(rng, 2f, 8f));

                var expected = Enumerable.Range(0, tight.Count).Where(i => query.Intersects(tight[i])).ToHashSet();
                var got = new List<int>();
                tree.QueryAABB(query, got);
                var gotSet = got.ToHashSet();

                // With zero margin the fat boxes equal the tight boxes, so the query is exact.
                Assert.Equal(expected, gotSet);
            }
        }

        [Fact]
        public void FatMargin_NeverMissesTrueOverlaps()
        {
            var rng = new RNG(55);
            var tree = new DynamicBvh<int>(margin: 1.5f);
            var tight = new List<AABB>();
            for (int i = 0; i < 200; i++)
            {
                var box = BoxAt(new Float3(Range(rng, -30, 30), Range(rng, -30, 30), Range(rng, -30, 30)), 1f);
                tight.Add(box);
                tree.Add(i, box);
            }

            for (int q = 0; q < 30; q++)
            {
                var query = BoxAt(new Float3(Range(rng, -30, 30), Range(rng, -30, 30), Range(rng, -30, 30)), 3f);
                var expected = Enumerable.Range(0, tight.Count).Where(i => query.Intersects(tight[i])).ToHashSet();
                var got = new List<int>();
                tree.QueryAABB(query, got);

                // Conservative: every true (tight) overlap is returned; extras from fattening are allowed.
                Assert.Subset(got.ToHashSet(), expected);
            }
        }

        [Fact]
        public void MovingWithinMargin_DoesNotReinsert()
        {
            var tree = new DynamicBvh<int>(margin: 1.0f);
            int id = tree.Add(0, BoxAt(Float3.Zero, 0.5f));

            // Small move: tight box stays inside the fat box -> no structural change.
            Assert.False(tree.Update(id, BoxAt(new Float3(0.2f, 0, 0), 0.5f)));
            // Large move: leaves the fat box -> re-inserted.
            Assert.True(tree.Update(id, BoxAt(new Float3(5f, 0, 0), 0.5f)));
        }

        [Fact]
        public void JitteringObjects_RarelyReinsert()
        {
            // Many objects jittering within their margin should almost never touch the tree.
            var rng = new RNG(7);
            var tree = new DynamicBvh<int>(margin: 1.0f);
            var ids = new int[500];
            var centers = new Float3[500];
            for (int i = 0; i < 500; i++)
            {
                centers[i] = new Float3(Range(rng, -50, 50), Range(rng, -50, 50), Range(rng, -50, 50));
                ids[i] = tree.Add(i, BoxAt(centers[i], 0.5f));
            }

            int reinsertions = 0;
            for (int step = 0; step < 10; step++)
            {
                for (int i = 0; i < 500; i++)
                {
                    centers[i] += new Float3(Range(rng, -0.05f, 0.05f), Range(rng, -0.05f, 0.05f), Range(rng, -0.05f, 0.05f));
                    if (tree.Update(ids[i], BoxAt(centers[i], 0.5f)))
                        reinsertions++;
                }
            }

            // 5000 small updates; the margin should absorb almost all of them.
            Assert.True(reinsertions < 250, $"too many reinsertions: {reinsertions}");
        }

        [Fact]
        public void AddRemove_KeepsQueriesConsistent()
        {
            var tree = new DynamicBvh<int>(margin: 0.5f);
            int a = tree.Add(1, BoxAt(Float3.Zero, 1f));
            int b = tree.Add(2, BoxAt(new Float3(10, 0, 0), 1f));
            Assert.Equal(2, tree.Count);

            tree.Remove(a);
            Assert.Equal(1, tree.Count);

            var got = new List<int>();
            tree.QueryAABB(BoxAt(Float3.Zero, 2f), got);
            Assert.DoesNotContain(1, got);

            got.Clear();
            tree.QueryAABB(BoxAt(new Float3(10, 0, 0), 2f), got);
            Assert.Contains(2, got);
        }

        [Fact]
        public void Raycast_HitsNearestThroughManyMoves()
        {
            var tree = new DynamicBvh<(Float3 center, float radius)>(margin: 0.5f);
            tree.Add((new Float3(10, 0, 0), 1f), BoxAt(new Float3(10, 0, 0), 1f));
            int mover = tree.Add((new Float3(40, 0, 0), 1f), BoxAt(new Float3(40, 0, 0), 1f));

            // Move the far sphere in front, with a displacement hint.
            var newCenter = new Float3(5, 0, 0);
            tree.Update(mover, BoxAt(newCenter, 1f), new Float3(-35, 0, 0));
            // Update the stored item to match its new position.
            tree.Remove(mover);
            tree.Add((newCenter, 1f), BoxAt(newCenter, 1f));

            var ray = new Ray(new Float3(-20, 0, 0), new Float3(1, 0, 0));
            bool hit = tree.Raycast(ray, (item, r) =>
                Intersection.RaySphere(r.Origin, r.Direction, item.center, item.radius, out float t0, out float t1) ? (t0 >= 0 ? t0 : t1) : -1f,
                out var hitItem, out _);

            Assert.True(hit);
            TestUtil.AssertClose(new Float3(5, 0, 0), hitItem.center);
        }
    }
}
