using System;
using System.Collections.Generic;

namespace Prowl.Scribe
{

    internal class BinPacker
    {
        private int currentAtlasWidth;
        private int currentAtlasHeight;
        private readonly List<AtlasNode> skyline;

        public BinPacker(int initialWidth, int initialHeight)
        {
            currentAtlasWidth = initialWidth;
            currentAtlasHeight = initialHeight;
            skyline = new List<AtlasNode> { new AtlasNode(0, 0, initialWidth) };
        }

        public void Clear(int newWidth, int newHeight)
        {
            currentAtlasWidth = newWidth;
            currentAtlasHeight = newHeight;
            skyline.Clear();
            if (newWidth > 0 && newHeight > 0)
            {
                skyline.Add(new AtlasNode(0, 0, newWidth));
            }
        }

        public bool TryPack(int rectWidth, int rectHeight, out int packedX, out int packedY)
        {
            packedX = 0;
            packedY = 0;

            int bestHeight = int.MaxValue;
            int bestWastage = int.MaxValue;
            int bestIndex = -1;
            var bestPosition = (X: 0, Y: 0);

            // Try Best-Fit heuristic: minimize wasted area
            for (int i = 0; i < skyline.Count; i++)
            {
                var fit = RectangleFits(i, rectWidth, rectHeight);
                if (fit.Y == -1) continue;

                // Calculate horizontal wastage
                int wastage = 0;
                int x = skyline[i].X;
                int widthLeft = rectWidth;
                int j = i;

                while (widthLeft > 0 && j < skyline.Count)
                {
                    wastage += Math.Max(0, skyline[j].Y - fit.Y);
                    widthLeft -= skyline[j].Width;
                    j++;
                }

                // Prefer lower Y, then less wastage
                if (fit.Y < bestHeight || fit.Y == bestHeight && wastage < bestWastage)
                {
                    bestHeight = fit.Y;
                    bestWastage = wastage;
                    bestIndex = i;
                    bestPosition = fit;
                }
            }

            if (bestIndex == -1)
                return false;

            AddRectangleToSkyline(bestIndex, bestPosition.X, bestPosition.Y, rectWidth, rectHeight);

            packedX = bestPosition.X;
            packedY = bestPosition.Y;
            return true;
        }

        private (int X, int Y) RectangleFits(int skylineNodeIndex, int width, int height)
        {
            int x = skyline[skylineNodeIndex].X;

            // Check if rectangle would extend beyond right edge
            if (x + width > currentAtlasWidth)
                return (-1, -1);

            int y = skyline[skylineNodeIndex].Y;
            int widthLeft = width;
            int i = skylineNodeIndex;

            // Find the maximum Y coordinate across all nodes this rect would span
            while (widthLeft > 0 && i < skyline.Count)
            {
                y = Math.Max(y, skyline[i].Y);

                // Check if rectangle would extend beyond top edge
                if (y + height > currentAtlasHeight)
                    return (-1, -1);

                widthLeft -= skyline[i].Width;
                i++;
            }

            // Make sure we covered the entire width
            return widthLeft <= 0 ? (x, y) : (-1, -1);
        }

        private void AddRectangleToSkyline(int skylineNodeIndex, int x, int y, int width, int height)
        {
            // First, handle the node at the left edge
            if (skyline[skylineNodeIndex].X < x)
            {
                // Split the first node
                var left = skyline[skylineNodeIndex];
                int oldWidth = left.Width;
                int leftX = left.X;
                int originalY = left.Y;

                skyline[skylineNodeIndex] = new AtlasNode(leftX, originalY, x - leftX);
                skyline.Insert(skylineNodeIndex + 1,
                    new AtlasNode(x, originalY, oldWidth - (x - leftX)));
                skylineNodeIndex++;

                //int oldWidth = skyline[skylineNodeIndex].Width;
                //skyline[skylineNodeIndex] = new AtlasNode(
                //    skyline[skylineNodeIndex].X,
                //    skyline[skylineNodeIndex].Y,
                //    x - skyline[skylineNodeIndex].X);
                //skyline.Insert(skylineNodeIndex + 1, new AtlasNode(x, y, oldWidth - (x - skyline[skylineNodeIndex].X)));
                //skylineNodeIndex++;
            }

            // Remove or modify nodes that are completely or partially covered
            int currentIndex = skylineNodeIndex;
            while (currentIndex < skyline.Count && skyline[currentIndex].X < x + width)
            {
                if (skyline[currentIndex].X + skyline[currentIndex].Width <= x + width)
                {
                    // Completely covered - remove it
                    skyline.RemoveAt(currentIndex);
                }
                else
                {
                    // Partially covered - shrink it
                    int newX = x + width;
                    int newWidth = skyline[currentIndex].X + skyline[currentIndex].Width - newX;
                    skyline[currentIndex] = new AtlasNode(newX, skyline[currentIndex].Y, newWidth);
                    break;
                }
            }

            // Add the new skyline segment
            skyline.Insert(currentIndex, new AtlasNode(x, y + height, width));

            // Merge adjacent segments with the same height
            MergeSkylineNodes();
        }

        private void MergeSkylineNodes()
        {
            for (int i = 0; i < skyline.Count - 1;)
            {
                if (skyline[i].Y == skyline[i + 1].Y)
                {
                    // Merge the nodes
                    skyline[i] = new AtlasNode(
                        skyline[i].X,
                        skyline[i].Y,
                        skyline[i].Width + skyline[i + 1].Width);
                    skyline.RemoveAt(i + 1);
                }
                else
                {
                    i++;
                }
            }
        }
    }
}
