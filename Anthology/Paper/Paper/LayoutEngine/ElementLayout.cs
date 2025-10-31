using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    public static class ElementLayout
    {
        private const float DEFAULT_MIN = float.MinValue;
        private const float DEFAULT_MAX = float.MaxValue;
        private const float DEFAULT_BORDER_WIDTH = 0f;

        internal static UISize Layout(ElementHandle elementHandle, Paper gui)
        {
            ref var data = ref elementHandle.Data;

            var wValue = (UnitValue)data._elementStyle.GetValue(GuiProp.Width);
            var hValue = (UnitValue)data._elementStyle.GetValue(GuiProp.Height);
            float width = wValue.IsPixels ? wValue.Value : throw new Exception("Root element must have fixed width");
            float height = hValue.IsPixels ? hValue.Value : throw new Exception("Root element must have fixed height");

            data.RelativeX = 0;
            data.RelativeY = 0;
            data.LayoutWidth = width;
            data.LayoutHeight = height;

            var size = DoLayout(elementHandle, LayoutType.Column, height, width);

            // Convert relative positions to absolute positions
            ComputeAbsolutePositions(ref data, gui);

            return size;
        }

        private static UnitValue GetProp(ref ElementData element, LayoutType parentType, GuiProp row, GuiProp column) 
            => (UnitValue)element._elementStyle.GetValue(parentType == LayoutType.Row ? row : column);

        private static UnitValue GetMain(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.Width, GuiProp.Height);
        private static UnitValue GetCross(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.Height, GuiProp.Width);
        private static UnitValue GetMinMain(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.MinWidth, GuiProp.MinHeight);
        private static UnitValue GetMaxMain(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.MaxWidth, GuiProp.MaxHeight);
        private static UnitValue GetMinCross(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.MinHeight, GuiProp.MinWidth);
        private static UnitValue GetMaxCross(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.MaxHeight, GuiProp.MaxWidth);
        private static UnitValue GetMainBefore(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.Left, GuiProp.Top);
        private static UnitValue GetMainAfter(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.Right, GuiProp.Bottom);
        private static UnitValue GetCrossBefore(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.Top, GuiProp.Left);
        private static UnitValue GetCrossAfter(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.Bottom, GuiProp.Right);
        private static UnitValue GetChildMainBefore(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.ChildLeft, GuiProp.ChildTop);
        private static UnitValue GetChildMainAfter(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.ChildRight, GuiProp.ChildBottom);
        private static UnitValue GetChildCrossBefore(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.ChildTop, GuiProp.ChildLeft);
        private static UnitValue GetChildCrossAfter(ref ElementData element, LayoutType parentType) => GetProp(ref element, parentType, GuiProp.ChildBottom, GuiProp.ChildRight);

        private static IEnumerable<ElementHandle> GetChildren(ElementHandle elementHandle)
        {
            foreach (int childIndex in elementHandle.Data.ChildIndices)
            {
                yield return new ElementHandle(elementHandle.Owner, childIndex);
            }
        }
        private static UnitValue GetMainBetween(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.RowBetween, GuiProp.ColBetween);
        private static UnitValue GetMinMainBefore(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.MinLeft, GuiProp.MinTop);
        private static UnitValue GetMaxMainBefore(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.MaxLeft, GuiProp.MaxTop);
        private static UnitValue GetMinMainAfter(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.MinRight, GuiProp.MinBottom);
        private static UnitValue GetMaxMainAfter(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.MaxRight, GuiProp.MaxBottom);
        private static UnitValue GetMinCrossBefore(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.MinTop, GuiProp.MinLeft);
        private static UnitValue GetMaxCrossBefore(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.MaxTop, GuiProp.MaxLeft);
        private static UnitValue GetMinCrossAfter(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.MinBottom, GuiProp.MinRight);
        private static UnitValue GetMaxCrossAfter(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.MaxBottom, GuiProp.MaxRight);
        private static UnitValue GetBorderMainBefore(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.BorderLeft, GuiProp.BorderTop);
        private static UnitValue GetBorderMainAfter(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.BorderRight, GuiProp.BorderBottom);
        private static UnitValue GetBorderCrossBefore(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.BorderTop, GuiProp.BorderLeft);
        private static UnitValue GetBorderCrossAfter(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.BorderBottom, GuiProp.BorderRight);

        private static (float, float)? ContentSizing(ElementHandle elementHandle, LayoutType parentLayoutType, float? parentMain, float? parentCross)
        {
            ref var element = ref elementHandle.Data;
            if (element.ContentSizer == null)
                return null;

            if(parentLayoutType == LayoutType.Row)
            {
                return element.ContentSizer(parentMain, parentCross);
            }
            else
            {
                var result = element.ContentSizer(parentCross, parentMain);
                if(result == null) return null;
                return (result.Value.Item2, result.Value.Item1);
            }
        }

        // CHANGE 05/09/2025: New method that handles both ContentSizing and text processing
        // This ensures both min constraints and auto sizing consider all content types
        private static (float, float)? GetContentSize(ElementHandle elementHandle, LayoutType parentLayoutType, float? parentMain, float? parentCross)
        {
            ref var element = ref elementHandle.Data;

            // 1) Try ContentSizer if defined
            var contentSize = ContentSizing(elementHandle, parentLayoutType, parentMain, parentCross);

            // 2) Otherwise, try text processing
            if (!contentSize.HasValue && !string.IsNullOrEmpty(element.Paragraph))
            {
                // Available width = parent's main, respecting constraints
                float availableWidth = parentLayoutType == LayoutType.Row
                    ? (parentMain ?? float.MaxValue)
                    : (parentCross ?? float.MaxValue);

                var textSize = element.ProcessText(elementHandle.Owner, (float)availableWidth);

                if (textSize.X > 0 || textSize.Y > 0)
                {
                    if (parentLayoutType == LayoutType.Row)
                        contentSize = (textSize.X, textSize.Y);
                    else
                        contentSize = (textSize.Y, textSize.X);
                }
            }

            return contentSize;
        }

        private static UISize DoLayout(ElementHandle elementHandle, LayoutType parentLayoutType, float parentMain, float parentCross)
        {
            ref var element = ref elementHandle.Data;
            LayoutType layoutType = element.LayoutType;

            UnitValue main = GetMain(ref element, parentLayoutType);
            UnitValue cross = GetCross(ref element, parentLayoutType);

            float minMain = main.IsStretch
                ? DEFAULT_MIN
                : GetMinMain(ref element, parentLayoutType).ToPx(parentMain, DEFAULT_MIN);

            float maxMain = main.IsStretch
                ? DEFAULT_MAX
                : GetMaxMain(ref element, parentLayoutType).ToPx(parentMain, DEFAULT_MAX);

            float minCross = cross.IsStretch
                ? DEFAULT_MIN
                : GetMinCross(ref element, parentLayoutType).ToPx(parentCross, DEFAULT_MIN);

            float maxCross = cross.IsStretch
                ? DEFAULT_MAX
                : GetMaxCross(ref element, parentLayoutType).ToPx(parentCross, DEFAULT_MAX);

            // Compute main-axis size
            float computedMain = 0;
            if (main.IsStretch)
                computedMain = parentMain;
            else if (main.IsPixels || main.IsPercentage)
                computedMain = main.ToPx(parentMain, 100f);
            // Auto stays at 0

            // Compute cross-axis size
            float computedCross = 0;
            if(cross.IsStretch)
                computedCross = parentCross;
            else if (cross.IsPixels || cross.IsPercentage)
                computedCross = cross.ToPx(parentCross, 100f);
            // Auto stays at 0


            // Apply aspect ratio if set
            var aspectRatio = (float)element._elementStyle.GetValue(GuiProp.AspectRatio);
            if (aspectRatio >= 0)
            {
                aspectRatio = Maths.Max(0.001f, aspectRatio); // Prevent divide-by-zero

                // Handle aspect ratio differently based on which dimension is more constrained
                if (main.IsAuto && !cross.IsAuto)
                {
                    // Cross is fixed, calculate main from it
                    float newMain = computedCross * aspectRatio;
                    computedMain = Maths.Min(maxMain, Maths.Max(minMain, newMain));
                }
                else if (!main.IsAuto && cross.IsAuto)
                {
                    // Main is fixed, calculate cross from it
                    float newCross = computedMain / aspectRatio;
                    computedCross = Maths.Min(maxCross, Maths.Max(minCross, newCross));
                }
                else if (main.IsAuto && cross.IsAuto)
                {
                    // Both auto, use the parent constraints to determine the limiting factor
                    float byWidth = parentMain;
                    float byHeight = parentCross * aspectRatio;

                    if (byWidth <= byHeight)
                    {
                        // Width is the limiting factor
                        computedMain = byWidth;
                        computedCross = byWidth / aspectRatio;
                    }
                    else
                    {
                        // Height is the limiting factor
                        computedCross = parentCross;
                        computedMain = parentCross * aspectRatio;
                    }
                }
                else if (main.IsStretch || cross.IsStretch)
                {
                    // One or both dimensions are stretching
                    // Apply aspect ratio after stretch calculations
                    // We'll need to add a post-processing step after all stretch calculations
                }
            }

            var borderMainBeforeUnit = GetBorderMainBefore(ref element, parentLayoutType);
            float borderMainBefore = borderMainBeforeUnit.ToPx(computedMain, DEFAULT_BORDER_WIDTH);
            var borderMainAfterUnit = GetBorderMainAfter(ref element, parentLayoutType);
            float borderMainAfter = borderMainAfterUnit.ToPx(computedMain, DEFAULT_BORDER_WIDTH);
            var borderCrossBeforeUnit = GetBorderCrossBefore(ref element, parentLayoutType);
            float borderCrossBefore = borderCrossBeforeUnit.ToPx(computedCross, DEFAULT_BORDER_WIDTH);
            var borderCrossAfterUnit = GetBorderCrossAfter(ref element, parentLayoutType);
            float borderCrossAfter = borderCrossAfterUnit.ToPx(computedCross, DEFAULT_BORDER_WIDTH);

            // Pre-allocate and filter in single pass to avoid LINQ overhead
            var visibleChildren = new List<int>();
            var parentDirectedChildren = new List<int>();
            
            foreach (int childIdx in element.ChildIndices)
            {
                var childData = elementHandle.Owner.GetElementData(childIdx);
                if (childData.Visible)
                {
                    visibleChildren.Add(childIdx);
                    if (childData.PositionType == PositionType.ParentDirected)
                        parentDirectedChildren.Add(childIdx);
                }
            }
            
            int numChildren = visibleChildren.Count;
            int numParentDirectedChildren = parentDirectedChildren.Count;

            float mainSum = 0f;
            float crossMax = 0f;

            // Apply content sizing for elements with no children
            if ((main.IsAuto || cross.IsAuto) && numParentDirectedChildren == 0)
            {
                float? pMain = main.IsAuto ? null : (float?)computedMain;
                float? pCross = cross.IsAuto ? null : (float?)computedCross;

                var contentSize = GetContentSize(elementHandle, parentLayoutType, pMain, pCross);

                if (contentSize.HasValue)
                {
                    computedMain = contentSize.Value.Item1;
                    computedCross = contentSize.Value.Item2;
                }
            }

            // CHANGE 05/09/2025: Fixed min constraint calculation to include both ContentSizing and text processing
            // OLD CODE: Only used ContentSizing, missing text content for min size calculations
            if ((GetMinMain(ref element, parentLayoutType).IsAuto || GetMinCross(ref element, parentLayoutType).IsAuto)
                && numParentDirectedChildren == 0)
            {
                float? pMain = GetMinMain(ref element, parentLayoutType).IsAuto ? null : (float?)computedMain;
                float? pCross = GetMinCross(ref element, parentLayoutType).IsAuto ? null : (float?)computedCross;

                var contentSize = GetContentSize(elementHandle, parentLayoutType, pMain, pCross);
                if (contentSize.HasValue)
                {
                    minMain = contentSize.Value.Item1;
                    minCross = contentSize.Value.Item2;
                }
            }

            // Apply size constraints
            computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
            computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));

            // Determine parent sizes for children based on layout types
            (float actualParentMain, float actualParentCross) = parentLayoutType == layoutType
                ? (computedMain, computedCross)
                : (computedCross, computedMain);

            // Sum of all space and size flex factors on the main-axis
            float mainFlexSum = 0f;

            // Lists for layout calculations
            var children = new List<ChildElementInfo>(numChildren);
            var mainAxis = new List<StretchItem>();

            // Parent overrides for child auto space
            UnitValue elementChildMainBefore = GetChildMainBefore(ref element, layoutType);
            UnitValue elementChildMainAfter = GetChildMainAfter(ref element, layoutType);
            UnitValue elementChildCrossBefore = GetChildCrossBefore(ref element, layoutType);
            UnitValue elementChildCrossAfter = GetChildCrossAfter(ref element, layoutType);
            UnitValue elementChildMainBetween = GetMainBetween(ref element, layoutType);

            // Get first and last parent-directed children for spacing
            int? first = parentDirectedChildren.Count > 0 ? 0 : null;
            int? last = parentDirectedChildren.Count > 0 ? parentDirectedChildren.Count - 1 : first;

            // Process each parent-directed child
            for (int i = 0; i < parentDirectedChildren.Count; i++)
            {
                int childIndex = parentDirectedChildren[i];
                ref var child = ref elementHandle.Owner.GetElementData(childIndex);
                var childHandle = new ElementHandle(elementHandle.Owner, childIndex);

                // Get desired space and size
                UnitValue childMainBefore = GetMainBefore(ref child, layoutType);
                UnitValue childMain = GetMain(ref child, layoutType);
                UnitValue childMainAfter = GetMainAfter(ref child, layoutType);

                UnitValue childCrossBefore = GetCrossBefore(ref child, layoutType);
                UnitValue childCross = GetCross(ref child, layoutType);
                UnitValue childCrossAfter = GetCrossAfter(ref child, layoutType);

                // Get constraints
                UnitValue childMinCrossBefore = GetMinCrossBefore(ref child, layoutType);
                UnitValue childMaxCrossBefore = GetMaxCrossBefore(ref child, layoutType);
                UnitValue childMinCrossAfter = GetMinCrossAfter(ref child, layoutType);
                UnitValue childMaxCrossAfter = GetMaxCrossAfter(ref child, layoutType);
                UnitValue childMinMainBefore = GetMinMainBefore(ref child, layoutType);
                UnitValue childMaxMainBefore = GetMaxMainBefore(ref child, layoutType);
                UnitValue childMinMainAfter = GetMinMainAfter(ref child, layoutType);
                UnitValue childMaxMainAfter = GetMaxMainAfter(ref child, layoutType);
                UnitValue childMinMain = GetMinMain(ref child, layoutType);
                UnitValue childMaxMain = GetMaxMain(ref child, layoutType);

                // Apply parent overrides to auto spacing
                if (childMainBefore.IsAuto && first == i)
                {
                    childMainBefore = elementChildMainBefore;
                }

                if (childMainAfter.IsAuto)
                {
                    if (last == i)
                    {
                        childMainAfter = elementChildMainAfter;
                    }
                    else if (i + 1 < parentDirectedChildren.Count)
                    {
                        var nextChildIndex = parentDirectedChildren[i + 1];
                        ref var nextChild = ref elementHandle.Owner.GetElementData(nextChildIndex);
                        var nextMainBefore = GetMainBefore(ref nextChild, layoutType);
                        if (nextMainBefore.IsAuto)
                        {
                            childMainAfter = elementChildMainBetween;
                        }
                    }
                }

                if (childCrossBefore.IsAuto)
                {
                    childCrossBefore = elementChildCrossBefore;
                }

                if (childCrossAfter.IsAuto)
                {
                    childCrossAfter = elementChildCrossAfter;
                }

                // Collect stretch main items
                if (childMainBefore.IsStretch)
                {
                    mainFlexSum += childMainBefore.Value;
                    mainAxis.Add(new StretchItem(
                        i, // Use list index for StretchItem
                        childMainBefore.Value,
                        StretchItem.ItemTypes.Before,
                        childMinMainBefore.ToPx(actualParentMain, DEFAULT_MIN),
                        childMaxMainBefore.ToPx(actualParentMain, DEFAULT_MAX)
                    ));
                }

                if (childMain.IsStretch)
                {
                    mainFlexSum += childMain.Value;
                    mainAxis.Add(new StretchItem(
                        i, // Use list index for StretchItem
                        childMain.Value,
                        StretchItem.ItemTypes.Size,
                        childMinMain.ToPx(actualParentMain, DEFAULT_MIN),
                        childMaxMain.ToPx(actualParentMain, DEFAULT_MAX)
                    ));
                }

                if (childMainAfter.IsStretch)
                {
                    mainFlexSum += childMainAfter.Value;
                    mainAxis.Add(new StretchItem(
                        i, // Use list index for StretchItem
                        childMainAfter.Value,
                        StretchItem.ItemTypes.After,
                        childMinMainAfter.ToPx(actualParentMain, DEFAULT_MIN),
                        childMaxMainAfter.ToPx(actualParentMain, DEFAULT_MAX)
                    ));
                }

                // Compute fixed-size child spaces
                float computedChildCrossBefore = childCrossBefore.ToPxClamped(
                    actualParentCross, 0f, childMinCrossBefore, childMaxCrossBefore);
                float computedChildCrossAfter = childCrossAfter.ToPxClamped(
                    actualParentCross, 0f, childMinCrossAfter, childMaxCrossAfter);
                float computedChildMainBefore = childMainBefore.ToPxClamped(
                    actualParentMain, 0f, childMinMainBefore, childMaxMainBefore);
                float computedChildMainAfter = childMainAfter.ToPxClamped(
                    actualParentMain, 0f, childMinMainAfter, childMaxMainAfter);

                float computedChildMain = 0f;
                float computedChildCross = childCross.ToPx(actualParentCross, 0f);

                // Get auto min cross size if needed
                if (GetMinCross(ref child, layoutType).IsAuto)
                {
                    float? pCross = GetMinCross(ref child, layoutType).IsAuto ? null : (float?)actualParentCross;
                    var contentSize = ContentSizing(childHandle, layoutType, pCross, pCross);
                    if (contentSize.HasValue)
                    {
                        computedChildCross = contentSize.Value.Item2;
                    }
                }

                // Compute fixed-size child main and cross for non-stretch children
                if (!childMain.IsStretch && !childCross.IsStretch)
                {
                    var childSize = DoLayout(childHandle, layoutType, actualParentMain, actualParentCross);
                    computedChildMain = childSize.Main;
                    computedChildCross = childSize.Cross;
                }

                mainSum += computedChildMain + computedChildMainBefore + computedChildMainAfter;
                crossMax = Maths.Max(crossMax, computedChildCrossBefore + computedChildCross + computedChildCrossAfter);

                children.Add(new ChildElementInfo(childHandle) {
                    CrossBefore = computedChildCrossBefore,
                    Cross = computedChildCross,
                    CrossAfter = computedChildCrossAfter,
                    MainBefore = computedChildMainBefore,
                    Main = computedChildMain,
                    MainAfter = computedChildMainAfter
                });
            }

            // Determine auto sizes from children
            if (numParentDirectedChildren != 0)
            {
                if (main.IsAuto || GetMinMain(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minMain = mainSum + borderMainBefore + borderMainAfter;
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, actualParentMain));
                    }
                    else
                    {
                        minMain = crossMax + borderMainBefore + borderMainAfter;
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, actualParentCross));
                    }
                }

                if (cross.IsAuto || GetMinCross(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minCross = crossMax + borderCrossBefore + borderCrossAfter;
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, actualParentCross));
                    }
                    else
                    {
                        minCross = mainSum + borderCrossBefore + borderCrossAfter;
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, actualParentMain));
                    }
                }
            }

            computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
            computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));

            // Process cross-axis stretching for parent-directed children
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.Element.Data.PositionType != PositionType.ParentDirected ||
                    GetCross(ref child.Element.Data, layoutType).IsAuto)
                    continue;

                UnitValue childCrossBefore = GetCrossBefore(ref child.Element.Data, layoutType);
                UnitValue childCross = GetCross(ref child.Element.Data, layoutType);
                UnitValue childCrossAfter = GetCrossAfter(ref child.Element.Data, layoutType);

                if (childCrossBefore.IsAuto)
                    childCrossBefore = elementChildCrossBefore;

                if (childCrossAfter.IsAuto)
                    childCrossAfter = elementChildCrossAfter;

                float crossFlexSum = 0f;
                var crossAxis = new List<StretchItem>();

                // Collect stretch cross items
                if (childCrossBefore.IsStretch)
                {
                    float childMinCrossBefore = GetMinCrossBefore(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    float childMaxCrossBefore = GetMaxCrossBefore(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MAX);

                    crossFlexSum += childCrossBefore.Value;
                    child.CrossBefore = 0f;

                    crossAxis.Add(new StretchItem(
                        i,
                        childCrossBefore.Value,
                        StretchItem.ItemTypes.Before,
                        childMinCrossBefore,
                        childMaxCrossBefore
                    ));
                }

                if (childCross.IsStretch)
                {
                    float childMinCross = GetMinCross(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    float childMaxCross = GetMaxCross(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MAX);

                    crossFlexSum += childCross.Value;
                    child.Cross = 0f;

                    crossAxis.Add(new StretchItem(
                        i,
                        childCross.Value,
                        StretchItem.ItemTypes.Size,
                        childMinCross,
                        childMaxCross
                    ));
                }

                if (childCrossAfter.IsStretch)
                {
                    float childMinCrossAfter = GetMinCrossAfter(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    float childMaxCrossAfter = GetMaxCrossAfter(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MAX);

                    crossFlexSum += childCrossAfter.Value;
                    child.CrossAfter = 0f;

                    crossAxis.Add(new StretchItem(
                        i,
                        childCrossAfter.Value,
                        StretchItem.ItemTypes.After,
                        childMinCrossAfter,
                        childMaxCrossAfter
                    ));
                }

                // Calculate cross stretch
                int unfrozenCrossCount = crossAxis.Count;
                while (unfrozenCrossCount > 0)
                {
                    float childCrossFreeSpace = actualParentCross
                        - borderCrossBefore
                        - borderCrossAfter
                        - child.CrossBefore
                        - child.Cross
                        - child.CrossAfter;

                    float totalViolation = 0f;

                    foreach (var item in crossAxis)
                    {
                        if (item.Frozen) continue;
                        float actualCross = (item.Factor * childCrossFreeSpace / crossFlexSum);

                        if (item.ItemType == StretchItem.ItemTypes.Size && !GetMain(ref child.Element.Data, layoutType).IsStretch)
                        {
                            var childSize = DoLayout(
                                child.Element,
                                layoutType,
                                actualParentMain,
                                actualCross,
                                actualCross);
                            child.Main = childSize.Main;
                            child.Cross = childSize.Cross;
                            mainSum += child.Main;
                        }

                        float clamped = Maths.Min(item.Max, Maths.Max(item.Min, actualCross));
                        item.Violation = clamped - actualCross;
                        totalViolation += item.Violation;

                        item.Computed = clamped;
                    }

                    foreach (var item in crossAxis)
                    {
                        if (item.Frozen) continue;
                        // Freeze over-stretched items
                        if (totalViolation > 0f)
                            item.Frozen = item.Violation > 0f;
                        else if (totalViolation < 0f)
                            item.Frozen = item.Violation < 0f;
                        else
                            item.Frozen = true;

                        if (item.Frozen)
                        {
                            crossFlexSum -= item.Factor;
                            unfrozenCrossCount--;

                            switch (item.ItemType)
                            {
                                case StretchItem.ItemTypes.Size:
                                    child.Cross = item.Computed;
                                    break;
                                case StretchItem.ItemTypes.Before:
                                    child.CrossBefore = item.Computed;
                                    break;
                                case StretchItem.ItemTypes.After:
                                    child.CrossAfter = item.Computed;
                                    break;
                            }
                        }
                    }
                }

                crossMax = Maths.Max(crossMax, child.CrossBefore + child.Cross + child.CrossAfter);
            }

            // Update auto sizes based on children
            if (numParentDirectedChildren != 0)
            {
                if (main.IsAuto || GetMinMain(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minMain = mainSum + borderMainBefore + borderMainAfter;
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, actualParentMain));
                    }
                    else
                    {
                        minMain = crossMax + borderMainBefore + borderMainAfter;
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, actualParentCross));
                    }
                }

                if (cross.IsAuto || GetMinCross(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minCross = crossMax + borderCrossBefore + borderCrossAfter;
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, actualParentCross));
                    }
                    else
                    {
                        minCross = mainSum + borderCrossBefore + borderCrossAfter;
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, actualParentMain));
                    }
                }
            }

            computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
            computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));

            // Calculate main-axis stretching for parent-directed children
            if (mainAxis.Count > 0)
            {
                int unfrozenMainCount = mainAxis.Count;
                while (unfrozenMainCount > 0)
                {
                    float freeMainSpace = actualParentMain - mainSum - borderMainBefore - borderMainAfter;
                    float totalViolation = 0f;

                    foreach (var item in mainAxis)
                    {
                        if (item.Frozen) continue;
                        float actualMain = (item.Factor * freeMainSpace / mainFlexSum);
                        var child = children[item.Index];

                        if (item.ItemType == StretchItem.ItemTypes.Size)
                        {
                            float childCross = GetCross(ref child.Element.Data, layoutType).IsStretch ?
                                child.Cross : actualParentCross;

                            var childSize = DoLayout(child.Element, layoutType, actualMain, childCross);
                            child.Cross = childSize.Cross;
                            crossMax = Maths.Max(crossMax, child.CrossBefore + child.Cross + child.CrossAfter);

                            if (GetMinMain(ref child.Element.Data, layoutType).IsAuto)
                            {
                                item.Min = childSize.Main;
                            }
                        }

                        float clamped = Maths.Min(item.Max, Maths.Max(item.Min, actualMain));
                        item.Violation = clamped - actualMain;
                        totalViolation += item.Violation;
                        item.Computed = clamped;
                    }

                    foreach (var item in mainAxis)
                    {
                        if (item.Frozen) continue;
                        var child = children[item.Index];

                        // Freeze over-stretched items
                        if (totalViolation > 0f)
                            item.Frozen = item.Violation > 0f;
                        else if (totalViolation < 0f)
                            item.Frozen = item.Violation < 0f;
                        else
                            item.Frozen = true;

                        if (item.Frozen)
                        {
                            mainFlexSum -= item.Factor;
                            mainSum += item.Computed;
                            unfrozenMainCount--;

                            switch (item.ItemType)
                            {
                                case StretchItem.ItemTypes.Size:
                                    child.Main = item.Computed;
                                    break;
                                case StretchItem.ItemTypes.Before:
                                    child.MainBefore = item.Computed;
                                    break;
                                case StretchItem.ItemTypes.After:
                                    child.MainAfter = item.Computed;
                                    break;
                            }
                        }
                    }
                }
            }

            // Final update of auto sizes
            if (numParentDirectedChildren != 0)
            {
                if (main.IsAuto || GetMinMain(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minMain = mainSum + borderMainBefore + borderMainAfter;
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, actualParentMain));
                    }
                    else
                    {
                        minMain = crossMax + borderMainBefore + borderMainAfter;
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, actualParentCross));
                    }
                }

                if (cross.IsAuto || GetMinCross(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minCross = crossMax + borderCrossBefore + borderCrossAfter;
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, actualParentCross));
                    }
                    else
                    {
                        minCross = mainSum + borderCrossBefore + borderCrossAfter;
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, actualParentMain));
                    }
                }
            }

            computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
            computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));

            // Handle self-directed children
            foreach (var childHandle in GetChildren(elementHandle))
            {
                if (!childHandle.Data.Visible || childHandle.Data.PositionType != PositionType.SelfDirected) continue;
                UnitValue childMainBefore = GetMainBefore(ref childHandle.Data, layoutType);
                UnitValue childMain = GetMain(ref childHandle.Data, layoutType);
                UnitValue childMainAfter = GetMainAfter(ref childHandle.Data, layoutType);
                UnitValue childCrossBefore = GetCrossBefore(ref childHandle.Data, layoutType);
                UnitValue childCross = GetCross(ref childHandle.Data, layoutType);
                UnitValue childCrossAfter = GetCrossAfter(ref childHandle.Data, layoutType);

                // Apply parent overrides
                if (childMainBefore.IsAuto)
                    childMainBefore = elementChildMainBefore;
                if (childMainAfter.IsAuto)
                    childMainAfter = elementChildMainAfter;
                if (childCrossBefore.IsAuto)
                    childCrossBefore = elementChildCrossBefore;
                if (childCrossAfter.IsAuto)
                    childCrossAfter = elementChildCrossAfter;

                // Compute fixed spaces
                float computedChildCrossBefore = childCrossBefore.ToPxClamped(
                    actualParentCross, 0f, GetMinCrossBefore(ref childHandle.Data, layoutType), GetMaxCrossBefore(ref childHandle.Data, layoutType));
                float computedChildCrossAfter = childCrossAfter.ToPxClamped(
                    actualParentCross, 0f, GetMinCrossAfter(ref childHandle.Data, layoutType), GetMaxCrossAfter(ref childHandle.Data, layoutType));
                float computedChildMainBefore = childMainBefore.ToPxClamped(
                    actualParentMain, 0f, GetMinMainBefore(ref childHandle.Data, layoutType), GetMaxMainBefore(ref childHandle.Data, layoutType));
                float computedChildMainAfter = childMainAfter.ToPxClamped(
                    actualParentMain, 0f, GetMinMainAfter(ref childHandle.Data, layoutType), GetMaxMainAfter(ref childHandle.Data, layoutType));

                float computedChildMain = 0f;
                float computedChildCross = 0f;

                // Compute fixed-size child sizes
                if (!childMain.IsStretch && !childCross.IsStretch)
                {
                    var childSize = DoLayout(childHandle, layoutType, actualParentMain, actualParentCross);
                    computedChildMain = childSize.Main;
                    computedChildCross = childSize.Cross;
                }

                children.Add(new ChildElementInfo(childHandle) {
                    CrossBefore = computedChildCrossBefore,
                    Cross = computedChildCross,
                    CrossAfter = computedChildCrossAfter,
                    MainBefore = computedChildMainBefore,
                    Main = computedChildMain,
                    MainAfter = computedChildMainAfter
                });
            }

            // Process cross-axis stretching for self-directed children
            for (int i = parentDirectedChildren.Count; i < children.Count; i++)
            {
                var child = children[i];
                ProcessChildCrossStretching(child, layoutType, actualParentCross, actualParentMain,
                    borderCrossBefore, borderCrossAfter, elementChildCrossBefore, elementChildCrossAfter, i);
            }

            // Process main-axis stretching for self-directed children
            for (int i = parentDirectedChildren.Count; i < children.Count; i++)
            {
                var child = children[i];
                ProcessChildMainStretching(child, layoutType, actualParentMain, actualParentCross,
                    borderMainBefore, borderMainAfter, elementChildMainBefore, elementChildMainAfter, i);
            }

            // Compute stretch cross spacing for auto-sized children
            foreach (var child in children)
            {
                ProcessChildCrossSpacing(child, layoutType, actualParentCross,
                    borderCrossBefore, borderCrossAfter, elementChildCrossBefore, elementChildCrossAfter);
            }

            if (aspectRatio >= 0)
            {
                aspectRatio = Maths.Max(0.001f, aspectRatio); // Prevent divide-by-zero

                if (main.IsStretch && cross.IsStretch)
                {
                    // Both stretch - constrain by the smaller dimension
                    float byWidth = computedMain;
                    float byHeight = computedCross * aspectRatio;

                    if (byHeight < byWidth)
                    {
                        computedMain = byHeight;
                    }
                    else
                    {
                        computedCross = computedMain / aspectRatio;
                    }
                }
                else if (main.IsStretch)
                {
                    // Main stretches, cross is fixed
                    computedMain = computedCross * aspectRatio;
                }
                else if (cross.IsStretch)
                {
                    // Cross stretches, main is fixed
                    computedCross = computedMain / aspectRatio;
                }

                // Ensure we're still respecting min/max constraints
                computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
            }

            // Set bounds for all children
            float mainPos = 0f;
            foreach (var child in children)
            {
                if (child.Element.Data.PositionType == PositionType.SelfDirected)
                {
                    SetElementBounds(
                        child.Element,
                        layoutType,
                        child.MainBefore + borderMainBefore,
                        child.CrossBefore + borderCrossBefore,
                        child.Main,
                        child.Cross
                    );
                }
                else // ParentDirected
                {
                    mainPos += child.MainBefore;
                    SetElementBounds(
                        child.Element,
                        layoutType,
                        mainPos + borderMainBefore,
                        child.CrossBefore + borderCrossBefore,
                        child.Main,
                        child.Cross
                    );
                    mainPos += child.Main + child.MainAfter;
                }
            }

            return new UISize(computedMain, computedCross);
        }

        private static void ProcessChildCrossStretching(
            ChildElementInfo child,
            LayoutType layoutType,
            float parentCross,
            float parentMain,
            float borderCrossBefore,
            float borderCrossAfter,
            UnitValue elementChildCrossBefore,
            UnitValue elementChildCrossAfter,
            int childIndex)
        {
            UnitValue childCrossBefore = GetCrossBefore(ref child.Element.Data, layoutType);
            UnitValue childCross = GetCross(ref child.Element.Data, layoutType);
            UnitValue childCrossAfter = GetCrossAfter(ref child.Element.Data, layoutType);

            // Apply parent overrides
            if (childCrossBefore.IsAuto)
                childCrossBefore = elementChildCrossBefore;
            if (childCrossAfter.IsAuto)
                childCrossAfter = elementChildCrossAfter;

            float crossFlexSum = 0f;
            var crossAxis = new List<StretchItem>();

            // Collect stretch cross items
            if (childCrossBefore.IsStretch)
            {
                float min = GetMinCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossBefore.Value;
                child.CrossBefore = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childCross.IsStretch)
            {
                float min = GetMinCross(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCross(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCross.Value;
                child.Cross = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCross.Value, StretchItem.ItemTypes.Size, min, max));
            }

            if (childCrossAfter.IsStretch)
            {
                float min = GetMinCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossAfter.Value;
                child.CrossAfter = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenCrossCount = crossAxis.Count;
            while (unfrozenCrossCount > 0)
            {
                float crossFreeSpace = parentCross - borderCrossBefore - borderCrossAfter
                    - child.CrossBefore - child.Cross - child.CrossAfter;

                float totalViolation = 0f;

                foreach (var item in crossAxis)
                {
                    if (item.Frozen) continue;
                    float actualCross = (item.Factor * crossFreeSpace / crossFlexSum);

                    if (item.ItemType == StretchItem.ItemTypes.Size && !GetMain(ref child.Element.Data, layoutType).IsStretch)
                    {
                        var childSize = DoLayout(child.Element, layoutType, parentMain, actualCross);
                        if (GetMinCross(ref child.Element.Data, layoutType).IsAuto)
                        {
                            item.Min = childSize.Cross;
                        }
                        child.Main = childSize.Main;
                    }

                    float clamped = Maths.Min(item.Max, Maths.Max(item.Min, actualCross));
                    item.Violation = clamped - actualCross;
                    totalViolation += item.Violation;
                    item.Computed = clamped;
                }

                foreach (var item in crossAxis)
                {
                    if (item.Frozen) continue;
                    // Freeze over-stretched items
                    if (totalViolation > 0f)
                        item.Frozen = item.Violation > 0f;
                    else if (totalViolation < 0f)
                        item.Frozen = item.Violation < 0f;
                    else
                        item.Frozen = true;

                    if (item.Frozen)
                    {
                        crossFlexSum -= item.Factor;
                        unfrozenCrossCount--;

                        switch (item.ItemType)
                        {
                            case StretchItem.ItemTypes.Size:
                                child.Cross = item.Computed;
                                break;
                            case StretchItem.ItemTypes.Before:
                                child.CrossBefore = item.Computed;
                                break;
                            case StretchItem.ItemTypes.After:
                                child.CrossAfter = item.Computed;
                                break;
                        }
                    }
                }
            }
        }

        private static void ProcessChildMainStretching(
            ChildElementInfo child,
            LayoutType layoutType,
            float parentMain,
            float parentCross,
            float borderMainBefore,
            float borderMainAfter,
            UnitValue elementChildMainBefore,
            UnitValue elementChildMainAfter,
            int childIndex)
        {
            UnitValue childMainBefore = GetMainBefore(ref child.Element.Data, layoutType);
            UnitValue childMain = GetMain(ref child.Element.Data, layoutType);
            UnitValue childMainAfter = GetMainAfter(ref child.Element.Data, layoutType);

            // Apply parent overrides
            if (childMainBefore.IsAuto)
                childMainBefore = elementChildMainBefore;
            if (childMainAfter.IsAuto)
                childMainAfter = elementChildMainAfter;

            float mainFlexSum = 0f;
            var mainAxis = new List<StretchItem>();

            // Collect stretch main items
            if (childMainBefore.IsStretch)
            {
                float min = GetMinMainBefore(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                float max = GetMaxMainBefore(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMainBefore.Value;
                mainAxis.Add(new StretchItem(childIndex, childMainBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childMain.IsStretch)
            {
                float min = GetMinMain(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                float max = GetMaxMain(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMain.Value;
                mainAxis.Add(new StretchItem(childIndex, childMain.Value, StretchItem.ItemTypes.Size, min, max));
            }

            if (childMainAfter.IsStretch)
            {
                float min = GetMinMainAfter(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                float max = GetMaxMainAfter(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMainAfter.Value;
                mainAxis.Add(new StretchItem(childIndex, childMainAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenMainCount = mainAxis.Count;
            while (unfrozenMainCount > 0)
            {
                float mainFreeSpace = parentMain - borderMainBefore - borderMainAfter
                    - child.MainBefore - child.Main - child.MainAfter;

                float totalViolation = 0f;

                foreach (var item in mainAxis)
                {
                    if (item.Frozen) continue;
                    float actualMain = (item.Factor * mainFreeSpace / mainFlexSum);

                    if (item.ItemType == StretchItem.ItemTypes.Size)
                    {
                        float childCross = GetCross(ref child.Element.Data, layoutType).IsStretch ?
                            child.Cross : parentCross;

                        var childSize = DoLayout(child.Element, layoutType, actualMain, childCross);
                        child.Cross = childSize.Cross;

                        if (GetMinMain(ref child.Element.Data, layoutType).IsAuto)
                        {
                            item.Min = childSize.Main;
                        }
                    }

                    float clamped = Maths.Min(item.Max, Maths.Max(item.Min, actualMain));
                    item.Violation = clamped - actualMain;
                    totalViolation += item.Violation;
                    item.Computed = clamped;
                }

                foreach (var item in mainAxis)
                {
                    if (item.Frozen) continue;
                    // Freeze over-stretched items
                    if (totalViolation > 0f)
                        item.Frozen = item.Violation > 0f;
                    else if (totalViolation < 0f)
                        item.Frozen = item.Violation < 0f;
                    else
                        item.Frozen = true;

                    if (item.Frozen)
                    {
                        mainFlexSum -= item.Factor;
                        unfrozenMainCount--;

                        switch (item.ItemType)
                        {
                            case StretchItem.ItemTypes.Before:
                                child.MainBefore = item.Computed;
                                break;
                            case StretchItem.ItemTypes.Size:
                                child.Main = item.Computed;
                                break;
                            case StretchItem.ItemTypes.After:
                                child.MainAfter = item.Computed;
                                break;
                        }
                    }
                }
            }
        }

        private static void ProcessChildCrossSpacing(
            ChildElementInfo child,
            LayoutType layoutType,
            float parentCross,
            float borderCrossBefore,
            float borderCrossAfter,
            UnitValue elementChildCrossBefore,
            UnitValue elementChildCrossAfter)
        {
            UnitValue childCrossBefore = GetCrossBefore(ref child.Element.Data, layoutType);
            UnitValue childCrossAfter = GetCrossAfter(ref child.Element.Data, layoutType);

            // Apply parent overrides
            if (childCrossBefore.IsAuto)
                childCrossBefore = elementChildCrossBefore;
            if (childCrossAfter.IsAuto)
                childCrossAfter = elementChildCrossAfter;

            // Only process if we have stretch items
            if (!childCrossBefore.IsStretch && !childCrossAfter.IsStretch)
                return;

            float crossFlexSum = 0f;
            var crossAxis = new List<StretchItem>();
            int childIndex = 0; // Just a placeholder since we're only dealing with this specific child

            // Collect stretch cross items
            if (childCrossBefore.IsStretch)
            {
                float min = GetMinCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossBefore.Value;
                child.CrossBefore = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childCrossAfter.IsStretch)
            {
                float min = GetMinCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossAfter.Value;
                child.CrossAfter = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenCrossCount = crossAxis.Count;
            while (unfrozenCrossCount > 0)
            {
                float crossFreeSpace = parentCross - borderCrossBefore - borderCrossAfter
                    - child.CrossBefore - child.Cross - child.CrossAfter;

                float totalViolation = 0f;

                foreach (var item in crossAxis)
                {
                    if (item.Frozen) continue;
                    float actualCross = (item.Factor * crossFreeSpace / crossFlexSum);

                    float clamped = Maths.Min(item.Max, Maths.Max(item.Min, actualCross));
                    item.Violation = clamped - actualCross;
                    totalViolation += item.Violation;
                    item.Computed = clamped;
                }

                foreach (var item in crossAxis)
                {
                    if (item.Frozen) continue;
                    // Freeze over-stretched items
                    if (totalViolation > 0f)
                        item.Frozen = item.Violation > 0f;
                    else if (totalViolation < 0f)
                        item.Frozen = item.Violation < 0f;
                    else
                        item.Frozen = true;

                    if (item.Frozen)
                    {
                        crossFlexSum -= item.Factor;
                        unfrozenCrossCount--;

                        switch (item.ItemType)
                        {
                            case StretchItem.ItemTypes.Before:
                                child.CrossBefore = item.Computed;
                                break;
                            case StretchItem.ItemTypes.After:
                                child.CrossAfter = item.Computed;
                                break;
                        }
                    }
                }
            }
        }

        private static UISize DoLayout(ElementHandle elementHandle, LayoutType parentLayoutType, float parentMain, float parentCross, float? fixedCross)
        {
            var size = DoLayout(elementHandle, parentLayoutType, parentMain, parentCross);

            // If we're calculating a particular cross size constraint, update the element with the fixed cross size
            if (fixedCross.HasValue)
            {
                ref var element = ref elementHandle.Data;
                if (parentLayoutType == LayoutType.Row)
                {
                    element.LayoutHeight = fixedCross.Value;
                }
                else
                {
                    element.LayoutWidth = fixedCross.Value;
                }
            }

            return size;
        }
        private static void SetElementBounds(ElementHandle elementHandle, LayoutType layoutType, float mainPos, float crossPos, float main, float cross)
        {
            ref ElementData element = ref elementHandle.Data;
            if (layoutType == LayoutType.Row)
            {
                element.RelativeX = mainPos;
                element.RelativeY = crossPos;
                element.LayoutWidth = main;
                element.LayoutHeight = cross;
            }
            else
            {
                element.RelativeX = crossPos;
                element.RelativeY = mainPos;
                element.LayoutWidth = cross;
                element.LayoutHeight = main;
            }
        }


        internal static Float2 ProcessText(this ref ElementData element, Paper gui, float availableWidth)
        {
            //if (element.ProcessedText) return Vector2.zero;

            if (string.IsNullOrWhiteSpace(element.Paragraph)) return Float2.Zero;

            element.ProcessedText = true;

            var canvas = gui.Canvas;
            if (canvas == null) throw new InvalidOperationException("Canvas is not set.");

            if (element.IsMarkdown == false)
            {
                var settings = TextLayoutSettings.Default;

                settings.WordSpacing = Convert.ToSingle(element._elementStyle.GetValue(GuiProp.WordSpacing));
                settings.LetterSpacing = Convert.ToSingle(element._elementStyle.GetValue(GuiProp.LetterSpacing));
                settings.LineHeight = Convert.ToSingle(element._elementStyle.GetValue(GuiProp.LineHeight));
                settings.TabSize = (int)element._elementStyle.GetValue(GuiProp.TabSize);
                settings.PixelSize = Convert.ToSingle(element._elementStyle.GetValue(GuiProp.FontSize));

                if(element.TextAlignment == TextAlignment.Left || element.TextAlignment == TextAlignment.MiddleLeft || element.TextAlignment == TextAlignment.BottomLeft)
                    settings.Alignment = Scribe.TextAlignment.Left;
                else if (element.TextAlignment == TextAlignment.Center || element.TextAlignment == TextAlignment.MiddleCenter || element.TextAlignment == TextAlignment.BottomCenter)
                    settings.Alignment = Scribe.TextAlignment.Center;
                else if(element.TextAlignment == TextAlignment.Right || element.TextAlignment == TextAlignment.MiddleRight || element.TextAlignment == TextAlignment.BottomRight)
                    settings.Alignment = Scribe.TextAlignment.Right;

                settings.Font = element.Font;
                settings.WrapMode = element.WrapMode;
                settings.MaxWidth = availableWidth;

                element._textLayout = canvas.CreateLayout(element.Paragraph, settings);

                return (Float2)element._textLayout.Size;
            }
            else
            {
                var r = element.Font;
                var m = element.FontMono;
                var b = element.FontBold;
                var i = element.FontItalic;
                var bi = element.FontBoldItalic;
                var settings = MarkdownLayoutSettings.Default(r, availableWidth, m, b, i, bi);

                element._quillMarkdown = canvas.CreateMarkdown(element.Paragraph, settings);

                var markdownResult = element._quillMarkdown;
                return markdownResult?.Size ?? Float2.Zero;
            }
        }

        private static void ComputeAbsolutePositions(ref ElementData element, Paper gui, float parentX = 0f, float parentY = 0f)
        {
            // Set absolute position based on parent's position
            element.X = parentX + element.RelativeX;
            element.Y = parentY + element.RelativeY;

            // Recursively set absolute positions for all children
            foreach (int childIndex in element.ChildIndices)
            {
                ref var child = ref gui.GetElementData(childIndex);
                ComputeAbsolutePositions(ref child, gui, element.X, element.Y);
            }
        }

        public static T Let<T, R>(this T self, Func<T, R> block)
        {
            block(self);
            return self;
        }
    }
}
