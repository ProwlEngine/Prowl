using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    public partial class Element
    {
        private const double DEFAULT_MIN = double.MinValue;
        private const double DEFAULT_MAX = double.MaxValue;
        private const double DEFAULT_BORDER_WIDTH = 0f;

        internal UISize Layout()
        {
            var wValue = (UnitValue)_elementStyle.GetValue(GuiProp.Width);
            var hValue = (UnitValue)_elementStyle.GetValue(GuiProp.Height);
            double width = wValue.IsPixels ? wValue.Value : throw new Exception("Root element must have fixed width");
            double height = hValue.IsPixels ? hValue.Value : throw new Exception("Root element must have fixed height");

            RelativeX = 0;
            RelativeY = 0;
            LayoutWidth = width;
            LayoutHeight = height;

            var size = DoLayout(this, LayoutType.Column, height, width);

            // Convert relative positions to absolute positions
            ComputeAbsolutePositions();

            return size;
        }

        private UnitValue GetProp(LayoutType parentType, GuiProp row, GuiProp column) => (UnitValue)_elementStyle.GetValue(parentType == LayoutType.Row ? row : column);

        private UnitValue GetMain(LayoutType parentType) => GetProp(parentType, GuiProp.Width, GuiProp.Height);
        private UnitValue GetCross(LayoutType parentType) => GetProp(parentType, GuiProp.Height, GuiProp.Width);
        private UnitValue GetMinMain(LayoutType parentType) => GetProp(parentType, GuiProp.MinWidth, GuiProp.MinHeight);
        private UnitValue GetMaxMain(LayoutType parentType) => GetProp(parentType, GuiProp.MaxWidth, GuiProp.MaxHeight);
        private UnitValue GetMinCross(LayoutType parentType) => GetProp(parentType, GuiProp.MinHeight, GuiProp.MinWidth);
        private UnitValue GetMaxCross(LayoutType parentType) => GetProp(parentType, GuiProp.MaxHeight, GuiProp.MaxWidth);
        private UnitValue GetMainBefore(LayoutType parentType) => GetProp(parentType, GuiProp.Left, GuiProp.Top);
        private UnitValue GetMainAfter(LayoutType parentType) => GetProp(parentType, GuiProp.Right, GuiProp.Bottom);
        private UnitValue GetCrossBefore(LayoutType parentType) => GetProp(parentType, GuiProp.Top, GuiProp.Left);
        private UnitValue GetCrossAfter(LayoutType parentType) => GetProp(parentType, GuiProp.Bottom, GuiProp.Right);
        private UnitValue GetChildMainBefore(LayoutType parentType) => GetProp(parentType, GuiProp.ChildLeft, GuiProp.ChildTop);
        private UnitValue GetChildMainAfter(LayoutType parentType) => GetProp(parentType, GuiProp.ChildRight, GuiProp.ChildBottom);
        private UnitValue GetChildCrossBefore(LayoutType parentType) => GetProp(parentType, GuiProp.ChildTop, GuiProp.ChildLeft);
        private UnitValue GetChildCrossAfter(LayoutType parentType) => GetProp(parentType, GuiProp.ChildBottom, GuiProp.ChildRight);
        private UnitValue GetMainBetween(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.RowBetween, GuiProp.ColBetween);
        private UnitValue GetMinMainBefore(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.MinLeft, GuiProp.MinTop);
        private UnitValue GetMaxMainBefore(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.MaxLeft, GuiProp.MaxTop);
        private UnitValue GetMinMainAfter(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.MinRight, GuiProp.MinBottom);
        private UnitValue GetMaxMainAfter(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.MaxRight, GuiProp.MaxBottom);
        private UnitValue GetMinCrossBefore(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.MinTop, GuiProp.MinLeft);
        private UnitValue GetMaxCrossBefore(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.MaxTop, GuiProp.MaxLeft);
        private UnitValue GetMinCrossAfter(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.MinBottom, GuiProp.MinRight);
        private UnitValue GetMaxCrossAfter(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.MaxBottom, GuiProp.MaxRight);
        private UnitValue GetBorderMainBefore(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.BorderLeft, GuiProp.BorderTop);
        private UnitValue GetBorderMainAfter(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.BorderRight, GuiProp.BorderBottom);
        private UnitValue GetBorderCrossBefore(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.BorderTop, GuiProp.BorderLeft);
        private UnitValue GetBorderCrossAfter(LayoutType parentLayoutType) => GetProp(parentLayoutType, GuiProp.BorderBottom, GuiProp.BorderRight);

        private (double, double)? ContentSizing(LayoutType parentLayoutType, double? parentMain, double? parentCross)
        {
            if (ContentSizer == null)
                return null;

            return parentLayoutType == LayoutType.Row
                ? ContentSizer(parentMain, parentCross)
                : ContentSizer(parentCross, parentMain)?.Let(t => (t.Item2, t.Item1));
        }

        private static UISize DoLayout(Element element, LayoutType parentLayoutType, double parentMain, double parentCross)
        {
            LayoutType layoutType = element.LayoutType;

            UnitValue main = element.GetMain(parentLayoutType);
            UnitValue cross = element.GetCross(parentLayoutType);

            double minMain = main.IsStretch
                ? DEFAULT_MIN
                : element.GetMinMain(parentLayoutType).ToPx(parentMain, DEFAULT_MIN);

            double maxMain = main.IsStretch
                ? DEFAULT_MAX
                : element.GetMaxMain(parentLayoutType).ToPx(parentMain, DEFAULT_MAX);

            double minCross = cross.IsStretch
                ? DEFAULT_MIN
                : element.GetMinCross(parentLayoutType).ToPx(parentCross, DEFAULT_MIN);

            double maxCross = cross.IsStretch
                ? DEFAULT_MAX
                : element.GetMaxCross(parentLayoutType).ToPx(parentCross, DEFAULT_MAX);

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

            double borderMainBefore = element.GetBorderMainBefore(parentLayoutType)
                .ToPx(computedMain, DEFAULT_BORDER_WIDTH);
            double borderMainAfter = element.GetBorderMainAfter(parentLayoutType)
                .ToPx(computedMain, DEFAULT_BORDER_WIDTH);
            double borderCrossBefore = element.GetBorderCrossBefore(parentLayoutType)
                .ToPx(computedCross, DEFAULT_BORDER_WIDTH);
            double borderCrossAfter = element.GetBorderCrossAfter(parentLayoutType)
                .ToPx(computedCross, DEFAULT_BORDER_WIDTH);

            var visibleChildren = element.Children.Where(c => c.Visible).ToList();
            int numChildren = visibleChildren.Count;
            var parentDirectedChildren = visibleChildren
                .Where(c => c.PositionType == PositionType.ParentDirected)
                .ToList();
            int numParentDirectedChildren = parentDirectedChildren.Count;

            double mainSum = 0f;
            double crossMax = 0f;

            // Apply content sizing for elements with no children
            if ((main.IsAuto || cross.IsAuto) && numParentDirectedChildren == 0)
            {
                double? pMain = main.IsAuto ? null : (double?)computedMain;
                double? pCross = cross.IsAuto ? null : (double?)computedCross;

                // 1) Try ContentSizer if defined
                var contentSize = element.ContentSizing(parentLayoutType, pMain, pCross);

                // 2) Otherwise, try text processing
                if (!contentSize.HasValue && !string.IsNullOrEmpty(element.Paragraph))
                {
                    // Available width = parent's main, respecting constraints
                    double availableWidth = parentLayoutType == LayoutType.Row
                        ? (pMain ?? parentMain)
                        : (pCross ?? parentCross);

                    var textSize = element.ProcessText((float)availableWidth);

                    if (textSize.x > 0 || textSize.y > 0)
                    {
                        if (parentLayoutType == LayoutType.Row)
                            contentSize = (textSize.x, textSize.y);
                        else
                            contentSize = (textSize.y, textSize.x);
                    }
                }

                if (contentSize.HasValue)
                {
                    computedMain = contentSize.Value.Item1;
                    computedCross = contentSize.Value.Item2;
                }
            }

            if ((element.GetMinMain(parentLayoutType).IsAuto || element.GetMinCross(parentLayoutType).IsAuto)
                && numParentDirectedChildren == 0)
            {
                double? pMain = element.GetMinMain(parentLayoutType).IsAuto ? null : (double?)computedMain;
                double? pCross = element.GetMinCross(parentLayoutType).IsAuto ? null : (double?)computedCross;

                var contentSize = element.ContentSizing(parentLayoutType, pMain, pCross);
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
            UnitValue elementChildMainBefore = element.GetChildMainBefore(layoutType);
            UnitValue elementChildMainAfter = element.GetChildMainAfter(layoutType);
            UnitValue elementChildCrossBefore = element.GetChildCrossBefore(layoutType);
            UnitValue elementChildCrossAfter = element.GetChildCrossAfter(layoutType);
            UnitValue elementChildMainBetween = element.GetMainBetween(layoutType);

            // Get first and last parent-directed children for spacing
            var parentDirected = parentDirectedChildren.Select((c, i) => (Child: c, Index: i)).ToList();
            int? first = parentDirected.FirstOrDefault().Index;
            int? last = parentDirected.Count > 0 ? parentDirected.Last().Index : first;

            // Process each parent-directed child
            for (int i = 0; i < parentDirected.Count; i++)
            {
                var (child, index) = parentDirected[i];

                // Get desired space and size
                UnitValue childMainBefore = child.GetMainBefore(layoutType);
                UnitValue childMain = child.GetMain(layoutType);
                UnitValue childMainAfter = child.GetMainAfter(layoutType);

                UnitValue childCrossBefore = child.GetCrossBefore(layoutType);
                UnitValue childCross = child.GetCross(layoutType);
                UnitValue childCrossAfter = child.GetCrossAfter(layoutType);

                // Get constraints
                UnitValue childMinCrossBefore = child.GetMinCrossBefore(layoutType);
                UnitValue childMaxCrossBefore = child.GetMaxCrossBefore(layoutType);
                UnitValue childMinCrossAfter = child.GetMinCrossAfter(layoutType);
                UnitValue childMaxCrossAfter = child.GetMaxCrossAfter(layoutType);
                UnitValue childMinMainBefore = child.GetMinMainBefore(layoutType);
                UnitValue childMaxMainBefore = child.GetMaxMainBefore(layoutType);
                UnitValue childMinMainAfter = child.GetMinMainAfter(layoutType);
                UnitValue childMaxMainAfter = child.GetMaxMainAfter(layoutType);
                UnitValue childMinMain = child.GetMinMain(layoutType);
                UnitValue childMaxMain = child.GetMaxMain(layoutType);

                // Apply parent overrides to auto spacing
                if (childMainBefore.IsAuto && first == index)
                {
                    childMainBefore = elementChildMainBefore;
                }

                if (childMainAfter.IsAuto)
                {
                    if (last == index)
                    {
                        childMainAfter = elementChildMainAfter;
                    }
                    else if (i + 1 < parentDirected.Count)
                    {
                        var nextChild = parentDirected[i + 1].Child;
                        var nextMainBefore = nextChild.GetMainBefore(layoutType);
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
                        index,
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
                        index,
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
                        index,
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
                if (child.GetMinCross(layoutType).IsAuto)
                {
                    double? pCross = child.GetMinCross(layoutType).IsAuto ? null : (double?)actualParentCross;
                    var contentSize = child.ContentSizing(layoutType, pCross, pCross);
                    if (contentSize.HasValue)
                    {
                        computedChildCross = contentSize.Value.Item2;
                    }
                }

                // Compute fixed-size child main and cross for non-stretch children
                if (!childMain.IsStretch && !childCross.IsStretch)
                {
                    var childSize = DoLayout(child, layoutType, actualParentMain, actualParentCross);
                    computedChildMain = childSize.Main;
                    computedChildCross = childSize.Cross;
                }

                mainSum += computedChildMain + computedChildMainBefore + computedChildMainAfter;
                crossMax = Math.Max(crossMax, computedChildCrossBefore + computedChildCross + computedChildCrossAfter);

                children.Add(new ChildElementInfo(child) {
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
                if (main.IsAuto || element.GetMinMain(parentLayoutType).IsAuto)
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

                if (cross.IsAuto || element.GetMinCross(parentLayoutType).IsAuto)
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
                if (child.Element.PositionType != PositionType.ParentDirected ||
                    child.Element.GetCross(layoutType).IsAuto)
                    continue;

                UnitValue childCrossBefore = child.Element.GetCrossBefore(layoutType);
                UnitValue childCross = child.Element.GetCross(layoutType);
                UnitValue childCrossAfter = child.Element.GetCrossAfter(layoutType);

                if (childCrossBefore.IsAuto)
                    childCrossBefore = elementChildCrossBefore;

                if (childCrossAfter.IsAuto)
                    childCrossAfter = elementChildCrossAfter;

                double crossFlexSum = 0f;
                var crossAxis = new List<StretchItem>();

                // Collect stretch cross items
                if (childCrossBefore.IsStretch)
                {
                    double childMinCrossBefore = child.Element.GetMinCrossBefore(layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    double childMaxCrossBefore = child.Element.GetMaxCrossBefore(layoutType)
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
                    double childMinCross = child.Element.GetMinCross(layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    double childMaxCross = child.Element.GetMaxCross(layoutType)
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
                    double childMinCrossAfter = child.Element.GetMinCrossAfter(layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    double childMaxCrossAfter = child.Element.GetMaxCrossAfter(layoutType)
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
                while (crossAxis.Any(item => !item.Frozen))
                {
                    double childCrossFreeSpace = actualParentCross
                        - borderCrossBefore
                        - borderCrossAfter
                        - child.CrossBefore
                        - child.Cross
                        - child.CrossAfter;

                    double totalViolation = 0f;

                    foreach (var item in crossAxis.Where(item => !item.Frozen))
                    {
                        double actualCross = (item.Factor * childCrossFreeSpace / crossFlexSum);

                        if (item.ItemType == StretchItem.ItemTypes.Size && !child.Element.GetMain(layoutType).IsStretch)
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

                    foreach (var item in crossAxis.Where(item => !item.Frozen))
                    {
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
                if (main.IsAuto || element.GetMinMain(parentLayoutType).IsAuto)
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

                if (cross.IsAuto || element.GetMinCross(parentLayoutType).IsAuto)
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
                while (mainAxis.Any(item => !item.Frozen))
                {
                    double freeMainSpace = actualParentMain - mainSum - borderMainBefore - borderMainAfter;
                    double totalViolation = 0f;

                    foreach (var item in mainAxis.Where(item => !item.Frozen))
                    {
                        double actualMain = (item.Factor * freeMainSpace / mainFlexSum);
                        var child = children[item.Index];

                        if (item.ItemType == StretchItem.ItemTypes.Size)
                        {
                            double childCross = child.Element.GetCross(layoutType).IsStretch ?
                                child.Cross : actualParentCross;

                            var childSize = DoLayout(child.Element, layoutType, actualMain, childCross);
                            child.Cross = childSize.Cross;
                            crossMax = Math.Max(crossMax, child.CrossBefore + child.Cross + child.CrossAfter);

                            if (child.Element.GetMinMain(layoutType).IsAuto)
                            {
                                item.Min = childSize.Main;
                            }
                        }

                        double clamped = Math.Min(item.Max, Math.Max(item.Min, actualMain));
                        item.Violation = clamped - actualMain;
                        totalViolation += item.Violation;
                        item.Computed = clamped;
                    }

                    foreach (var item in mainAxis.Where(item => !item.Frozen))
                    {
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
                if (main.IsAuto || element.GetMinMain(parentLayoutType).IsAuto)
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

                if (cross.IsAuto || element.GetMinCross(parentLayoutType).IsAuto)
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
            foreach (var child in element.Children.Where(c => c.Visible && c.PositionType == PositionType.SelfDirected))
            {
                UnitValue childMainBefore = child.GetMainBefore(layoutType);
                UnitValue childMain = child.GetMain(layoutType);
                UnitValue childMainAfter = child.GetMainAfter(layoutType);
                UnitValue childCrossBefore = child.GetCrossBefore(layoutType);
                UnitValue childCross = child.GetCross(layoutType);
                UnitValue childCrossAfter = child.GetCrossAfter(layoutType);

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
                    actualParentCross, 0f, child.GetMinCrossBefore(layoutType), child.GetMaxCrossBefore(layoutType));
                double computedChildCrossAfter = childCrossAfter.ToPxClamped(
                    actualParentCross, 0f, child.GetMinCrossAfter(layoutType), child.GetMaxCrossAfter(layoutType));
                double computedChildMainBefore = childMainBefore.ToPxClamped(
                    actualParentMain, 0f, child.GetMinMainBefore(layoutType), child.GetMaxMainBefore(layoutType));
                double computedChildMainAfter = childMainAfter.ToPxClamped(
                    actualParentMain, 0f, child.GetMinMainAfter(layoutType), child.GetMaxMainAfter(layoutType));

                double computedChildMain = 0f;
                double computedChildCross = 0f;

                // Compute fixed-size child sizes
                if (!childMain.IsStretch && !childCross.IsStretch)
                {
                    var childSize = DoLayout(child, layoutType, actualParentMain, actualParentCross);
                    computedChildMain = childSize.Main;
                    computedChildCross = childSize.Cross;
                }

                children.Add(new ChildElementInfo(child) {
                    CrossBefore = computedChildCrossBefore,
                    Cross = computedChildCross,
                    CrossAfter = computedChildCrossAfter,
                    MainBefore = computedChildMainBefore,
                    Main = computedChildMain,
                    MainAfter = computedChildMainAfter
                });
            }

            // Process cross-axis stretching for self-directed children
            for (int i = parentDirected.Count; i < children.Count; i++)
            {
                var child = children[i];
                ProcessChildCrossStretching(child, layoutType, actualParentCross, actualParentMain,
                    borderCrossBefore, borderCrossAfter, elementChildCrossBefore, elementChildCrossAfter, i);
            }

            // Process main-axis stretching for self-directed children
            for (int i = parentDirected.Count; i < children.Count; i++)
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
                if (child.Element.PositionType == PositionType.SelfDirected)
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
            UnitValue childCrossBefore = child.Element.GetCrossBefore(layoutType);
            UnitValue childCross = child.Element.GetCross(layoutType);
            UnitValue childCrossAfter = child.Element.GetCrossAfter(layoutType);

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
                double min = child.Element.GetMinCrossBefore(layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = child.Element.GetMaxCrossBefore(layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossBefore.Value;
                child.CrossBefore = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childCross.IsStretch)
            {
                double min = child.Element.GetMinCross(layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = child.Element.GetMaxCross(layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCross.Value;
                child.Cross = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCross.Value, StretchItem.ItemTypes.Size, min, max));
            }

            if (childCrossAfter.IsStretch)
            {
                double min = child.Element.GetMinCrossAfter(layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = child.Element.GetMaxCrossAfter(layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossAfter.Value;
                child.CrossAfter = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            while (crossAxis.Any(item => !item.Frozen))
            {
                double crossFreeSpace = parentCross - borderCrossBefore - borderCrossAfter
                    - child.CrossBefore - child.Cross - child.CrossAfter;

                double totalViolation = 0f;

                foreach (var item in crossAxis.Where(item => !item.Frozen))
                {
                    double actualCross = (item.Factor * crossFreeSpace / crossFlexSum);

                    if (item.ItemType == StretchItem.ItemTypes.Size && !child.Element.GetMain(layoutType).IsStretch)
                    {
                        var childSize = DoLayout(child.Element, layoutType, parentMain, actualCross);
                        if (child.Element.GetMinCross(layoutType).IsAuto)
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

                foreach (var item in crossAxis.Where(item => !item.Frozen))
                {
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
            UnitValue childMainBefore = child.Element.GetMainBefore(layoutType);
            UnitValue childMain = child.Element.GetMain(layoutType);
            UnitValue childMainAfter = child.Element.GetMainAfter(layoutType);

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
                double min = child.Element.GetMinMainBefore(layoutType).ToPx(parentMain, DEFAULT_MIN);
                double max = child.Element.GetMaxMainBefore(layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMainBefore.Value;
                mainAxis.Add(new StretchItem(childIndex, childMainBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childMain.IsStretch)
            {
                double min = child.Element.GetMinMain(layoutType).ToPx(parentMain, DEFAULT_MIN);
                double max = child.Element.GetMaxMain(layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMain.Value;
                mainAxis.Add(new StretchItem(childIndex, childMain.Value, StretchItem.ItemTypes.Size, min, max));
            }

            if (childMainAfter.IsStretch)
            {
                double min = child.Element.GetMinMainAfter(layoutType).ToPx(parentMain, DEFAULT_MIN);
                double max = child.Element.GetMaxMainAfter(layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMainAfter.Value;
                mainAxis.Add(new StretchItem(childIndex, childMainAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            while (mainAxis.Any(item => !item.Frozen))
            {
                double mainFreeSpace = parentMain - borderMainBefore - borderMainAfter
                    - child.MainBefore - child.Main - child.MainAfter;

                double totalViolation = 0f;

                foreach (var item in mainAxis.Where(item => !item.Frozen))
                {
                    double actualMain = (item.Factor * mainFreeSpace / mainFlexSum);

                    if (item.ItemType == StretchItem.ItemTypes.Size)
                    {
                        double childCross = child.Element.GetCross(layoutType).IsStretch ?
                            child.Cross : parentCross;

                        var childSize = DoLayout(child.Element, layoutType, actualMain, childCross);
                        child.Cross = childSize.Cross;

                        if (child.Element.GetMinMain(layoutType).IsAuto)
                        {
                            item.Min = childSize.Main;
                        }
                    }

                    double clamped = Math.Min(item.Max, Math.Max(item.Min, actualMain));
                    item.Violation = clamped - actualMain;
                    totalViolation += item.Violation;
                    item.Computed = clamped;
                }

                foreach (var item in mainAxis.Where(item => !item.Frozen))
                {
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
            UnitValue childCrossBefore = child.Element.GetCrossBefore(layoutType);
            UnitValue childCrossAfter = child.Element.GetCrossAfter(layoutType);

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
                double min = child.Element.GetMinCrossBefore(layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = child.Element.GetMaxCrossBefore(layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossBefore.Value;
                child.CrossBefore = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossBefore.Value, StretchItem.ItemTypes.Before, min, max));
            }

            if (childCrossAfter.IsStretch)
            {
                double min = child.Element.GetMinCrossAfter(layoutType).ToPx(parentCross, DEFAULT_MIN);
                double max = child.Element.GetMaxCrossAfter(layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossAfter.Value;
                child.CrossAfter = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossAfter.Value, StretchItem.ItemTypes.After, min, max));
            }

            while (crossAxis.Any(item => !item.Frozen))
            {
                double crossFreeSpace = parentCross - borderCrossBefore - borderCrossAfter
                    - child.CrossBefore - child.Cross - child.CrossAfter;

                double totalViolation = 0f;

                foreach (var item in crossAxis.Where(item => !item.Frozen))
                {
                    double actualCross = (item.Factor * crossFreeSpace / crossFlexSum);

                    double clamped = Math.Min(item.Max, Math.Max(item.Min, actualCross));
                    item.Violation = clamped - actualCross;
                    totalViolation += item.Violation;
                    item.Computed = clamped;
                }

                foreach (var item in crossAxis.Where(item => !item.Frozen))
                {
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

        private static UISize DoLayout(Element element, LayoutType parentLayoutType, double parentMain, double parentCross, double? fixedCross = null)
        {
            var size = DoLayout(element, parentLayoutType, parentMain, parentCross);

            // If we're calculating a particular cross size constraint, update the element with the fixed cross size
            if (fixedCross.HasValue)
            {
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

        private static void SetElementBounds(Element element, LayoutType layoutType, double mainPos, double crossPos, double main, double cross)
        {
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

        private void ComputeAbsolutePositions(double parentX = 0f, double parentY = 0f)
        {
            // Set absolute position based on parent's position
            X = parentX + RelativeX;
            Y = parentY + RelativeY;

            // Recursively set absolute positions for all children
            foreach (var child in Children)
            {
                child.ComputeAbsolutePositions(X, Y);
            }
        }
    }

    public static class Extensions
    {
        public static T Let<T, R>(this T self, Func<T, R> block)
        {
            block(self);
            return self;
        }
    }
}
