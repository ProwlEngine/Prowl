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
    public class BvhTests
    {
        private static float Range(RNG rng, float a, float b) => a + rng.NextFloat() * (b - a);

        private static List<AABB> MakeBoxes(int n, int seed)
        {
            var rng = new RNG(seed);
            var boxes = new List<AABB>(n);
            for (int i = 0; i < n; i++)
            {
                var c = new Float3(Range(rng, -50, 50), Range(rng, -50, 50), Range(rng, -50, 50));
                var h = new Float3(Range(rng, 0.5f, 4f), Range(rng, 0.5f, 4f), Range(rng, 0.5f, 4f));
                boxes.Add(new AABB(c - h, c + h));
            }
            return boxes;
        }

        [Theory]
        [InlineData(BvhBuildQuality.Fast)]
        [InlineData(BvhBuildQuality.Balanced)]
        [InlineData(BvhBuildQuality.High)]
        public void QueryAABB_MatchesBruteForce(BvhBuildQuality quality)
        {
            var boxes = MakeBoxes(400, 12345);
            var bvh = new Bvh<int>(Enumerable.Range(0, boxes.Count).ToList(), i => boxes[i],
                new BvhBuildSettings { Quality = quality, MaxLeafSize = 4 });

            var rng = new RNG(99);
            for (int q = 0; q < 50; q++)
            {
                var c = new Float3(Range(rng, -50, 50), Range(rng, -50, 50), Range(rng, -50, 50));
                var h = new Float3(Range(rng, 1, 10), Range(rng, 1, 10), Range(rng, 1, 10));
                var query = new AABB(c - h, c + h);

                var expected = new HashSet<int>();
                for (int i = 0; i < boxes.Count; i++)
                    if (query.Intersects(boxes[i])) expected.Add(i);

                var got = new List<int>();
                bvh.QueryAABB(query, got);

                Assert.Equal(expected, got.ToHashSet());
            }
        }

        [Fact]
        public void QueryPoint_FindsContainingBoxes()
        {
            var boxes = MakeBoxes(200, 7);
            var bvh = new Bvh<int>(Enumerable.Range(0, boxes.Count).ToList(), i => boxes[i]);

            var probe = new Float3(0, 0, 0);
            var expected = Enumerable.Range(0, boxes.Count).Where(i => boxes[i].Contains(probe)).ToHashSet();

            var got = new List<int>();
            bvh.QueryPoint(probe, got);

            Assert.Equal(expected, got.ToHashSet());
        }

        [Fact]
        public void Raycast_FindsNearestSphere()
        {
            // Spheres along +X; a ray from -X must hit the closest one.
            var centers = new[] { new Float3(10, 0, 0), new Float3(20, 0, 0), new Float3(30, 0, 0) };
            const float radius = 1.0f;

            var bvh = new Bvh<Float3>(centers, c => new AABB(c - new Float3(radius), c + new Float3(radius)));

            var ray = new Ray(new Float3(-10, 0, 0), new Float3(1, 0, 0));
            bool hit = bvh.Raycast(ray, (c, r) =>
                Intersection.RaySphere(r.Origin, r.Direction, c, radius, out float t0, out float t1) ? (t0 >= 0 ? t0 : t1) : -1f,
                out var hitCenter, out float distance);

            Assert.True(hit);
            TestUtil.AssertClose(new Float3(10, 0, 0), hitCenter);
            TestUtil.AssertClose(19f, distance, 1e-2f); // -10 -> 9 (sphere front at x=9)
        }

        [Fact]
        public void Raycast_MissReturnsFalse()
        {
            var centers = new[] { new Float3(0, 100, 0) };
            var bvh = new Bvh<Float3>(centers, c => new AABB(c - new Float3(1), c + new Float3(1)));

            var ray = new Ray(new Float3(0, 0, 0), new Float3(1, 0, 0));
            bool hit = bvh.Raycast(ray, (c, r) =>
                Intersection.RaySphere(r.Origin, r.Direction, c, 1f, out float t0, out float t1) ? t0 : -1f,
                out _, out _);

            Assert.False(hit);
        }

        [Fact]
        public void EmptyBvh_QueriesAreSafe()
        {
            var bvh = new Bvh<int>(new List<int>(), i => new AABB());
            var got = new List<int>();
            bvh.QueryAABB(new AABB(new Float3(-1), new Float3(1)), got);
            Assert.Empty(got);
            Assert.False(bvh.Raycast(new Ray(Float3.Zero, Float3.UnitX), (_, _) => -1f, out _, out _));
        }
    }
}
