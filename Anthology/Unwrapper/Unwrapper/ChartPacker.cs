using System.Drawing;
using System.Numerics;

namespace Prowl.Unwrapper
{
    public class ChartPacker
    {
        private List<Vector2> chartSizes = [];
        private List<(Vector2 position, Vector2 size, bool rotated)> results = [];

        // Configuration parameters
        private readonly float initialAreaGuessFactor = 1.1f;
        private readonly float textureSizeGrowFactor = 0.05f;
        private readonly float floatToIntFactor = 10000f;
        private readonly float paddingSize = 0.002f;
        private readonly int maxTryNum = 100;

        private int tryNum = 0;
        private float textureSizeFactor = 1.0f;

        public void SetCharts(List<Vector2> sizes)
        {
            chartSizes = sizes;
        }

        public IReadOnlyList<(Vector2 position, Vector2 size, bool rotated)> GetResults()
        {
            return results;
        }

        private double CalculateTotalArea()
        {
            return chartSizes.Sum(size => size.X * size.Y);
        }

        public float Pack()
        {
            float textureSize;
            float initialGuessSize = (float)Math.Sqrt((float)CalculateTotalArea() * initialAreaGuessFactor);

            while (true)
            {
                textureSize = initialGuessSize * textureSizeFactor;
                tryNum++;

                if (TryPack(textureSize))
                    break;

                textureSizeFactor += textureSizeGrowFactor;

                if (tryNum >= maxTryNum)
                    break;
            }

            return textureSize;
        }

        public bool TryPack(float textureSize)
        {
            int width = (int)(textureSize * floatToIntFactor);
            int height = width;

            float paddingSize2 = paddingSize * width * 2;
            float singlePadding = paddingSize * width;

            // Convert chart sizes to rectangles with padding
            var inputRects = new List<Rectangle>();
            foreach (var chartSize in chartSizes)
            {
                inputRects.Add(new Rectangle(
                    0, 0,
                    (int)(chartSize.X * floatToIntFactor + paddingSize2),
                    (int)(chartSize.Y * floatToIntFactor + paddingSize2)
                ));
            }

            // Different packing methods to try
            var methods = new[]
            {
                FreeRectChoiceHeuristic.RectBestShortSideFit,
                FreeRectChoiceHeuristic.RectBestLongSideFit,
                FreeRectChoiceHeuristic.RectBestAreaFit,
                FreeRectChoiceHeuristic.RectBottomLeftRule,
                FreeRectChoiceHeuristic.RectContactPointRule
            };

            float bestOccupancy = 0;
            List<Rectangle> bestResult = null;
            var packer = new MaxRectsBinPack(width, height, true);

            // Try each packing method
            foreach (var method in methods)
            {
                packer.Init(width, height, true);
                var result = new List<Rectangle>();

                var rectsCopy = new List<Rectangle>(inputRects);
                packer.Insert(rectsCopy, result, method);

                if (result.Count == inputRects.Count)
                {
                    float occupancy = packer.Occupancy();
                    if (occupancy > bestOccupancy)
                    {
                        bestResult = result;
                        bestOccupancy = occupancy;
                    }
                }
            }

            if (bestResult == null || bestResult.Count != inputRects.Count)
                return false;

            // Convert results to normalized coordinates
            results.Clear();
            for (int i = 0; i < bestResult.Count; i++)
            {
                var rect = bestResult[i];
                var originalSize = chartSizes[i];

                // Check if the rectangle was rotated by comparing aspect ratios
                bool isRotated = Math.Abs((float)rect.Width / rect.Height - originalSize.X / originalSize.Y) > 0.01f;

                var position = new Vector2(
                    (rect.X + singlePadding) / width,
                    (rect.Y + singlePadding) / height
                );

                var size = new Vector2(
                    (rect.Width - paddingSize2) / width,
                    (rect.Height - paddingSize2) / height
                );

                results.Add((position, size, isRotated));
            }

            return true;
        }
    }
}