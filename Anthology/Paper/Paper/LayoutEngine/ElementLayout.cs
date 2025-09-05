using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    public static class ElementLayout
    {
        private const double DEFAULT_MIN = double.MinValue;
        private const double DEFAULT_MAX = double.MaxValue;
        private const double DEFAULT_BORDER_WIDTH = 0f;

        internal static UISize Layout(ElementHandle elementHandle, Paper gui)
        {
            ref var data = ref elementHandle.Data;

            var wValue = (UnitValue)data._elementStyle.GetValue(GuiProp.Width);
            var hValue = (UnitValue)data._elementStyle.GetValue(GuiProp.Height);
            double width = wValue.IsPixels ? wValue.Value : throw new Exception("Root element must have fixed width");
            double height = hValue.IsPixels ? hValue.Value : throw new Exception("Root element must have fixed height");

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

        private static (double, double)? ContentSizing(ElementHandle elementHandle, LayoutType parentLayoutType, double? parentMain, double? parentCross)
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
        private static (double, double)? GetContentSize(ElementHandle elementHandle, LayoutType parentLayoutType, double? parentMain, double? parentCross)
        {
            ref var element = ref elementHandle.Data;

            // 1) Try ContentSizer if defined
            var contentSize = ContentSizing(elementHandle, parentLayoutType, parentMain, parentCross);

            // 2) Otherwise, try text processing
            if (!contentSize.HasValue && !string.IsNullOrEmpty(element.Paragraph))
            {
                // Available width = parent's main, respecting constraints
                double availableWidth = parentLayoutType == LayoutType.Row
                    ? (parentMain ?? double.MaxValue)
                    : (parentCross ?? double.MaxValue);

                var textSize = element.ProcessText(elementHandle.Owner, (float)availableWidth);

                if (textSize.x > 0 || textSize.y > 0)
                {
                    if (parentLayoutType == LayoutType.Row)
                        contentSize = (textSize.x, textSize.y);
                    else
                        contentSize = (textSize.y, textSize.x);
                }
            }

            return contentSize;
        }

        private static UISize DoLayout(ElementHandle elementHandle, LayoutType parentLayoutType, double parentMain, double parentCross)
        {
            ref var element = ref elementHandle.Data;
            LayoutType layoutType = element.LayoutType;

            UnitValue main = GetMain(ref element, parentLayoutType);
            UnitValue cross = GetCross(ref element, parentLayoutType);

            double minMain = main.IsStretch
                ? DEFAULT_MIN
                : GetMinMain(ref element, parentLayoutType).ToPx(parentMain, DEFAULT_MIN);

            double maxMain = main.IsStretch
                ? DEFAULT_MAX
                : GetMaxMain(ref element, parentLayoutType).ToPx(parentMain, DEFAULT_MAX);

            double minCross = cross.IsStretch
                ? DEFAULT_MIN
                : GetMinCross(ref element, parentLayoutType).ToPx(parentCross, DEFAULT_MIN);

            double maxCross = cross.IsStretch
                ? DEFAULT_MAX
                : GetMaxCross(ref element, parentLayoutType).ToPx(parentCross, DEFAULT_MAX);

            // Compute main-axis size
            double computedMain = 0;
            if (main.IsStretch)
                computedMain = parentMain;
            else if (main.IsPixels || main.IsPercentage)
                computedMain = main.ToPx(parentMain, 100f);
            // Auto stays at 0

            // Compute cross-axis size
            double computedCross = 0;
            if(cross.IsStretch)
                computedCross = parentCross;
            else if (cross.IsPixels || cross.IsPercentage)
                computedCross = cross.ToPx(parentCross, 100f);
            // Auto stays at 0


            // Apply aspect ratio if set
            var aspectRatio = (double)element._elementStyle.GetValue(GuiProp.AspectRatio);
            if (aspectRatio >= 0)
            {
                aspectRatio = Math.Max(0.001, aspectRatio); // Prevent divide-by-zero

                // Handle aspect ratio differently based on which dimension is more constrained
                if (main.IsAuto && !cross.IsAuto)
                {
                    // Cross is fixed, calculate main from it
                    double newMain = computedCross * aspectRatio;
                    computedMain = Math.Min(maxMain, Math.Max(minMain, newMain));
                }
                else if (!main.IsAuto && cross.IsAuto)
                {
                    // Main is fixed, calculate cross from it
                    double newCross = computedMain / aspectRatio;
                    computedCross = Math.Min(maxCross, Math.Max(minCross, newCross));
                }
                else if (main.IsAuto && cross.IsAuto)
                {
                    // Both auto, use the parent constraints to determine the limiting factor
                    double byWidth = parentMain;
                    double byHeight = parentCross * aspectRatio;

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
            double borderMainBefore = borderMainBeforeUnit.ToPx(computedMain, DEFAULT_BORDER_WIDTH);
            var borderMainAfterUnit = GetBorderMainAfter(ref element, parentLayoutType);
            double borderMainAfter = borderMainAfterUnit.ToPx(computedMain, DEFAULT_BORDER_WIDTH);
            var borderCrossBeforeUnit = GetBorderCrossBefore(ref element, parentLayoutType);
            double borderCrossBefore = borderCrossBeforeUnit.ToPx(computedCross, DEFAULT_BORDER_WIDTH);
            var borderCrossAfterUnit = GetBorderCrossAfter(ref element, parentLayoutType);
            double borderCrossAfter = borderCrossAfterUnit.ToPx(computedCross, DEFAULT_BORDER_WIDTH);

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

            double mainSum = 0f;
            double crossMax = 0f;

            // Apply content sizing for elements with no children
            if ((main.IsAuto || cross.IsAuto) && numParentDirectedChildren == 0)
            {
                double? pMain = main.IsAuto ? null : (double?)computedMain;
                double? pCross = cross.IsAuto ? null : (double?)computedCross;

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
                double? pMain = GetMinMain(ref element, parentLayoutType).IsAuto ? null : (double?)computedMain;
                double? pCross = GetMinCross(ref element, parentLayoutType).IsAuto ? null : (double?)computedCross;

                var contentSize = GetContentSize(elementHandle, parentLayoutType, pMain, pCross);
                if (contentSize.HasValue)
                {
                    minMain = contentSize.Value.Item1;
                    minCross = contentSize.Value.Item2;
                }
            }

            // Apply size constraints
            computedMain = Math.Min(maxMain, Math.Max(minMain, computedMain));
            computedCross = Math.Min(maxCross, Math.Max(minCross, computedCross));

            // Determine parent sizes for children based on layout types
            (double actualParentMain, double actualParentCross) = parentLayoutType == layoutType
                ? (computedMain, computedCross)
                : (computedCross, computedMain);

            // Sum of all space and size flex factors on the main-axis
            double mainFlexSum = 0f;

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
                double computedChildCrossBefore = childCrossBefore.ToPxClamped(
                    actualParentCross, 0f, childMinCrossBefore, childMaxCrossBefore);
                double computedChildCrossAfter = childCrossAfter.ToPxClamped(
                    actualParentCross, 0f, childMinCrossAfter, childMaxCrossAfter);
                double computedChildMainBefore = childMainBefore.ToPxClamped(
                    actualParentMain, 0f, childMinMainBefore, childMaxMainBefore);
                double computedChildMainAfter = childMainAfter.ToPxClamped(
                    actualParentMain, 0f, childMinMainAfter, childMaxMainAfter);

                double computedChildMain = 0f;
                double computedChildCross = childCross.ToPx(actualParentCross, 0f);

                // Get auto min cross size if needed
                if (GetMinCross(ref child, layoutType).IsAuto)
                {
                    double? pCross = GetMinCross(ref child, layoutType).IsAuto ? null : (double?)actualParentCross;
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
                crossMax = Math.Max(crossMax, computedChildCrossBefore + computedChildCross + computedChildCrossAfter);

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
                        actualParentMain = Math.Min(maxMain, Math.Max(minMain, actualParentMain));
                    }
                    else
                    {
                        minMain = crossMax + borderMainBefore + borderMainAfter;
                        actualParentCross = Math.Min(maxMain, Math.Max(minMain, actualParentCross));
                    }
                }

                if (cross.IsAuto || GetMinCross(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minCross = crossMax + borderCrossBefore + borderCrossAfter;
                        actualParentCross = Math.Min(maxCross, Math.Max(minCross, actualParentCross));
                    }
                    else
                    {
                        minCross = mainSum + borderCrossBefore + borderCrossAfter;
                        actualParentMain = Math.Min(maxCross, Math.Max(minCross, actualParentMain));
                    }
                }
            }

            computedMain = Math.Min(maxMain, Math.Max(minMain, computedMain));
            computedCross = Math.Min(maxCross, Math.Max(minCross, computedCross));

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

                double crossFlexSum = 0f;
                var crossAxis = new List<StretchItem>();

                // Collect stretch cross items
                if (childCrossBefore.IsStretch)
                {
                    double childMinCrossBefore = GetMinCrossBefore(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    double childMaxCrossBefore = GetMaxCrossBefore(ref child.Element.Data, layoutType)
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
                    double childMinCross = GetMinCross(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    double childMaxCross = GetMaxCross(ref child.Element.Data, layoutType)
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
                    double childMinCrossAfter = GetMinCrossAfter(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    double childMaxCrossAfter = GetMaxCrossAfter(ref child.Element.Data, layoutType)
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
                    double childCrossFreeSpace = actualParentCross
                        - borderCrossBefore
                        - borderCrossAfter
                        - child.CrossBefore
                        - child.Cross
                        - child.CrossAfter;

                    double totalViolation = 0f;

                    foreach (var item in crossAxis)
                    {
                        if (item.Frozen) continue;
                        double actualCross = (item.Factor * childCrossFreeSpace / crossFlexSum);

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

                        double clamped = Math.Min(item.Max, Math.Max(item.Min, actualCross));
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

                crossMax = Math.Max(crossMax, child.CrossBefore + child.Cross + child.CrossAfter);
            }

            // Update auto sizes based on children
            if (numParentDirectedChildren != 0)
            {
                if (main.IsAuto || GetMinMain(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minMain = mainSum + borderMainBefore + borderMainAfter;
                        actualParentMain = Math.Min(maxMain, Math.Max(minMain, actualParentMain));
                    }
                    else
                    {
                        minMain = crossMax + borderMainBefore + borderMainAfter;
                        actualParentCross = Math.Min(maxMain, Math.Max(minMain, actualParentCross));
                    }
                }

                if (cross.IsAuto || GetMinCross(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minCross = crossMax + borderCrossBefore + borderCrossAfter;
                        actualParentCross = Math.Min(maxCross, Math.Max(minCross, actualParentCross));
                    }
                    else
                    {
                        minCross = mainSum + borderCrossBefore + borderCrossAfter;
                        actualParentMain = Math.Min(maxCross, Math.Max(minCross, actualParentMain));
                    }
                }
            }

            computedMain = Math.Min(maxMain, Math.Max(minMain, computedMain));
            computedCross = Math.Min(maxCross, Math.Max(minCross, computedCross));

            // Calculate main-axis stretching for parent-directed children
            if (mainAxis.Count > 0)
            {
                int unfrozenMainCount = mainAxis.Count;
                while (unfrozenMainCount > 0)
                {
                    double freeMainSpace = actualParentMain - mainSum - borderMainBefore - borderMainAfter;
                    double totalViolation = 0f;

                    foreach (var item in mainAxis)
                    {
                        if (item.Frozen) continue;
                        double actualMain = (item.Factor * freeMainSpace / mainFlexSum);
                        var child = children[item.Index];

                        if (item.ItemType == StretchItem.ItemTypes.Size)
                        {
                            double childCross = GetCross(ref child.Element.Data, layoutType).IsStretch ?
                                child.Cross : actualParentCross;

                            var childSize = DoLayout(child.Element, layoutType, actualMain, childCross);
                            child.Cross = childSize.Cross;
                            crossMax = Math.Max(crossMax, child.CrossBefore + child.Cross + child.CrossAfter);

                            if (GetMinMain(ref child.Element.Data, layoutType).IsAuto)
                            {
                                item.Min = childSize.Main;
                            }
                        }

                        double clamped = Math.Min(item.Max, Math.Max(item.Min, actualMain));
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
                        actualParentMain = Math.Min(maxMain, Math.Max(minMain, actualParentMain));
                    }
                    else
                    {
                        minMain = crossMax + borderMainBefore + borderMainAfter;
                        actualParentCross = Math.Min(maxMain, Math.Max(minMain, actualParentCross));
                    }
                }

                if (cross.IsAuto || GetMinCross(ref element, parentLayoutType).IsAuto)
                {
                    if (parentLayoutType == layoutType)
                    {
                        minCross = crossMax + borderCrossBefore + borderCrossAfter;
                        actualParentCross = Math.Min(maxCross, Math.Max(minCross, actualParentCross));
                    }
                    else
                    {
                        minCross = mainSum + borderCrossBefore + borderCrossAfter;
                        actualParentMain = Math.Min(maxCross, Math.Max(minCross, actualParentMain));
                    }
                }
            }

            computedMain = Math.Min(maxMain, Math.Max(minMain, computedMain));
            computedCross = Math.Min(maxCross, Math.Max(minCross, computedCross));

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
                double computedChildCrossBefore = childCrossBefore.ToPxClamped(
                    actualParentCross, 0f, GetMinCrossBefore(ref childHandle.Data, layoutType), GetMaxCrossBefore(ref childHandle.Data, layoutType));
                double computedChildCrossAfter = childCrossAfter.ToPxClamped(
                    actualParentCross, 0f, GetMinCrossAfter(ref childHandle.Data, layoutType), GetMaxCrossAfter(ref childHandle.Data, layoutType));
                double computedChildMainBefore = childMainBefore.ToPxClamped(
                    actualParentMain, 0f, GetMinMainBefore(ref childHandle.Data, layoutType), GetMaxMainBefore(ref childHandle.Data, layoutType));
                double computedChildMainAfter = childMainAfter.ToPxClamped(
                    actualParentMain, 0f, GetMinMainAfter(ref childHandle.Data, layoutType), GetMaxMainAfter(ref childHandle.Data, layoutType));

                double computedChildMain = 0f;
                double computedChildCross = 0f;

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
                aspectRatio = Math.Max(0.001, aspectRatio); // Prevent divide-by-zero

                if (main.IsStretch && cross.IsStretch)
                {
                    // Both stretch - constrain by the smaller dimension
                    double byWidth = computedMain;
                    double byHeight = computedCross * aspectRatio;

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
                computedMain = Math.Min(maxMain, Math.Max(minMain, computedMain));
                computedCross = Math.Min(maxCross, Math.Max(minCross, computedCross));
            }

            // Set bounds for all children
            double mainPos = 0f;
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
            double parentCross,
            double parentMain,
            double borderCrossBefore,
            double borderCrossAfter,
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

            double crossFlexSum = 0f;
            var crossAxis = new List<StretchItem>();

            // Collect stretch cross items
            if (childCrossBefore.IsStretch)
            {
                double min = GetMinCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = GetMaxCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossBefore.Value;
                child.CrossBefore = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childCross.IsStretch)
            {
                double min = GetMinCross(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = GetMaxCross(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCross.Value;
                child.Cross = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCross.Value, StretchItem.ItemTypes.Size, min, max));
            }

            if (childCrossAfter.IsStretch)
            {
                double min = GetMinCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = GetMaxCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossAfter.Value;
                child.CrossAfter = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenCrossCount = crossAxis.Count;
            while (unfrozenCrossCount > 0)
            {
                double crossFreeSpace = parentCross - borderCrossBefore - borderCrossAfter
                    - child.CrossBefore - child.Cross - child.CrossAfter;

                double totalViolation = 0f;

                foreach (var item in crossAxis)
                {
                    if (item.Frozen) continue;
                    double actualCross = (item.Factor * crossFreeSpace / crossFlexSum);

                    if (item.ItemType == StretchItem.ItemTypes.Size && !GetMain(ref child.Element.Data, layoutType).IsStretch)
                    {
                        var childSize = DoLayout(child.Element, layoutType, parentMain, actualCross);
                        if (GetMinCross(ref child.Element.Data, layoutType).IsAuto)
                        {
                            item.Min = childSize.Cross;
                        }
                        child.Main = childSize.Main;
                    }

                    double clamped = Math.Min(item.Max, Math.Max(item.Min, actualCross));
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
            double parentMain,
            double parentCross,
            double borderMainBefore,
            double borderMainAfter,
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

            double mainFlexSum = 0f;
            var mainAxis = new List<StretchItem>();

            // Collect stretch main items
            if (childMainBefore.IsStretch)
            {
                double min = GetMinMainBefore(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                double max = GetMaxMainBefore(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMainBefore.Value;
                mainAxis.Add(new StretchItem(childIndex, childMainBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childMain.IsStretch)
            {
                double min = GetMinMain(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                double max = GetMaxMain(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMain.Value;
                mainAxis.Add(new StretchItem(childIndex, childMain.Value, StretchItem.ItemTypes.Size, min, max));
            }

            if (childMainAfter.IsStretch)
            {
                double min = GetMinMainAfter(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                double max = GetMaxMainAfter(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMainAfter.Value;
                mainAxis.Add(new StretchItem(childIndex, childMainAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenMainCount = mainAxis.Count;
            while (unfrozenMainCount > 0)
            {
                double mainFreeSpace = parentMain - borderMainBefore - borderMainAfter
                    - child.MainBefore - child.Main - child.MainAfter;

                double totalViolation = 0f;

                foreach (var item in mainAxis)
                {
                    if (item.Frozen) continue;
                    double actualMain = (item.Factor * mainFreeSpace / mainFlexSum);

                    if (item.ItemType == StretchItem.ItemTypes.Size)
                    {
                        double childCross = GetCross(ref child.Element.Data, layoutType).IsStretch ?
                            child.Cross : parentCross;

                        var childSize = DoLayout(child.Element, layoutType, actualMain, childCross);
                        child.Cross = childSize.Cross;

                        if (GetMinMain(ref child.Element.Data, layoutType).IsAuto)
                        {
                            item.Min = childSize.Main;
                        }
                    }

                    double clamped = Math.Min(item.Max, Math.Max(item.Min, actualMain));
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
            double parentCross,
            double borderCrossBefore,
            double borderCrossAfter,
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

            double crossFlexSum = 0f;
            var crossAxis = new List<StretchItem>();
            int childIndex = 0; // Just a placeholder since we're only dealing with this specific child

            // Collect stretch cross items
            if (childCrossBefore.IsStretch)
            {
                double min = GetMinCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = GetMaxCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossBefore.Value;
                child.CrossBefore = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childCrossAfter.IsStretch)
            {
                double min = GetMinCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = GetMaxCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossAfter.Value;
                child.CrossAfter = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenCrossCount = crossAxis.Count;
            while (unfrozenCrossCount > 0)
            {
                double crossFreeSpace = parentCross - borderCrossBefore - borderCrossAfter
                    - child.CrossBefore - child.Cross - child.CrossAfter;

                double totalViolation = 0f;

                foreach (var item in crossAxis)
                {
                    if (item.Frozen) continue;
                    double actualCross = (item.Factor * crossFreeSpace / crossFlexSum);

                    double clamped = Math.Min(item.Max, Math.Max(item.Min, actualCross));
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

        private static UISize DoLayout(ElementHandle elementHandle, LayoutType parentLayoutType, double parentMain, double parentCross, double? fixedCross)
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
        private static void SetElementBounds(ElementHandle elementHandle, LayoutType layoutType, double mainPos, double crossPos, double main, double cross)
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


        internal static Vector2 ProcessText(this ref ElementData element, Paper gui, float availableWidth)
        {
            //if (element.ProcessedText) return Vector2.zero;

            if (string.IsNullOrWhiteSpace(element.Paragraph)) return Vector2.zero;

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

                return element._textLayout.Size;
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

                var markdownResult = element._quillMarkdown as dynamic;
                return markdownResult?.Size ?? Vector2.zero;
            }
        }

        private static void ComputeAbsolutePositions(ref ElementData element, Paper gui, double parentX = 0f, double parentY = 0f)
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
