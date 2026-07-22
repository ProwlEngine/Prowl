using System;
using System.Collections.Generic;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    public static class ElementLayout
    {
        private const float DEFAULT_MIN = float.MinValue;
        private const float DEFAULT_MAX = float.MaxValue;
        private const float DEFAULT_PADDING = 0f;

        // Element IDs already warned about cross-stretch inside a wrap container, so the warning
        // fires once per element instead of every frame.
        private static readonly HashSet<int> _warnedWrapCrossStretch = new HashSet<int>();

        internal static UISize Layout(ElementHandle elementHandle, Paper gui)
        {
            ref var data = ref elementHandle.Data;

            var wValue = data._elementStyle.GetUnit(GuiProp.Width);
            var hValue = data._elementStyle.GetUnit(GuiProp.Height);
            float width = wValue.IsPixels ? wValue.Px : throw new Exception("Root element must have fixed width");
            float height = hValue.IsPixels ? hValue.Px : throw new Exception("Root element must have fixed height");

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
            => element._elementStyle.GetUnit(parentType == LayoutType.Row ? row : column);

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
        private static UnitValue GetPaddingMainBefore(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.PaddingLeft, GuiProp.PaddingTop);
        private static UnitValue GetPaddingMainAfter(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.PaddingRight, GuiProp.PaddingBottom);
        private static UnitValue GetPaddingCrossBefore(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.PaddingTop, GuiProp.PaddingLeft);
        private static UnitValue GetPaddingCrossAfter(ref ElementData element, LayoutType parentLayoutType) => GetProp(ref element, parentLayoutType, GuiProp.PaddingBottom, GuiProp.PaddingRight);

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

        // Timing wrapper: when DevTools deep-profiling is on, record this call's inclusive layout time
        // (which includes text measurement via ProcessText). An element is laid out several times per
        // frame during stretch resolution, so DevTools sums these per element.
        private static UISize DoLayout(ElementHandle elementHandle, LayoutType parentLayoutType, float parentMain, float parentCross)
        {
            var dev = elementHandle.Owner.DevTools;
            if (!dev.DeepProfiling)
                return DoLayoutInner(elementHandle, parentLayoutType, parentMain, parentCross);

            int id = elementHandle.Data.ID;
            int pi = elementHandle.Data.ParentIndex;
            int pid = pi >= 0 ? elementHandle.Owner.GetElementData(pi).ID : 0;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            var size = DoLayoutInner(elementHandle, parentLayoutType, parentMain, parentCross);
            dev.RecordLayout(id, pid, System.Diagnostics.Stopwatch.GetTimestamp() - start);
            return size;
        }

        private static UISize DoLayoutInner(ElementHandle elementHandle, LayoutType parentLayoutType, float parentMain, float parentCross)
        {
            ref var element = ref elementHandle.Data;
            LayoutType layoutType = element.LayoutType;

            UnitValue main = GetMain(ref element, parentLayoutType);
            UnitValue cross = GetCross(ref element, parentLayoutType);

            float minMain = main.HasGrow
                ? DEFAULT_MIN
                : GetMinMain(ref element, parentLayoutType).Floor(parentMain);

            float maxMain = main.HasGrow
                ? DEFAULT_MAX
                : GetMaxMain(ref element, parentLayoutType).ToPx(parentMain, DEFAULT_MAX);

            float minCross = cross.HasGrow
                ? DEFAULT_MIN
                : GetMinCross(ref element, parentLayoutType).Floor(parentCross);

            float maxCross = cross.HasGrow
                ? DEFAULT_MAX
                : GetMaxCross(ref element, parentLayoutType).ToPx(parentCross, DEFAULT_MAX);

            // Floor (Px + Pct%) for both axes; Grow path snaps to parent (refined later by stretch pass);
            // Auto contribution is layered on after content measurement below.
            float computedMain = main.HasGrow ? parentMain : main.Floor(parentMain);
            float computedCross = cross.HasGrow ? parentCross : cross.Floor(parentCross);

            // Apply aspect ratio if set
            var aspectRatio = element._elementStyle.GetAspectRatio();
            if (aspectRatio >= 0)
            {
                aspectRatio = Maths.Max(0.001f, aspectRatio); // Prevent divide-by-zero

                // Handle aspect ratio differently based on which dimension is more constrained
                if (main.HasAuto && !cross.HasAuto)
                {
                    // Cross is fixed, calculate main from it
                    float newMain = computedCross * aspectRatio;
                    computedMain = Maths.Min(maxMain, Maths.Max(minMain, newMain));
                }
                else if (!main.HasAuto && cross.HasAuto)
                {
                    // Main is fixed, calculate cross from it
                    float newCross = computedMain / aspectRatio;
                    computedCross = Maths.Min(maxCross, Maths.Max(minCross, newCross));
                }
                else if (main.HasAuto && cross.HasAuto)
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
                else if (main.HasGrow || cross.HasGrow)
                {
                    // One or both dimensions are stretching
                    // Apply aspect ratio after stretch calculations
                    // We'll need to add a post-processing step after all stretch calculations
                }
            }

            var paddingMainBeforeUnit = GetPaddingMainBefore(ref element, parentLayoutType);
            float paddingMainBefore = paddingMainBeforeUnit.ToPx(computedMain, DEFAULT_PADDING);
            var paddingMainAfterUnit = GetPaddingMainAfter(ref element, parentLayoutType);
            float paddingMainAfter = paddingMainAfterUnit.ToPx(computedMain, DEFAULT_PADDING);
            var paddingCrossBeforeUnit = GetPaddingCrossBefore(ref element, parentLayoutType);
            float paddingCrossBefore = paddingCrossBeforeUnit.ToPx(computedCross, DEFAULT_PADDING);
            var paddingCrossAfterUnit = GetPaddingCrossAfter(ref element, parentLayoutType);
            float paddingCrossAfter = paddingCrossAfterUnit.ToPx(computedCross, DEFAULT_PADDING);

            // The padding values above are resolved against the PARENT's axes, correct for this
            // element's own auto-size (which is measured in the parent's frame, see childrenMain/Cross).
            // But this element's CHILDREN flow along its OWN axis (`layoutType`); when this element's
            // direction differs from its parent's, the main/cross axes are swapped (mirroring the
            // actualParentMain/Cross swap below). Use these own-axis values everywhere children are
            // positioned or have free space distributed, so e.g. a Row's PaddingLeft insets its
            // children horizontally instead of vertically.
            bool paddingAxesFlipped = layoutType != parentLayoutType;
            float ownPaddingMainBefore = paddingAxesFlipped ? paddingCrossBefore : paddingMainBefore;
            float ownPaddingMainAfter = paddingAxesFlipped ? paddingCrossAfter : paddingMainAfter;
            float ownPaddingCrossBefore = paddingAxesFlipped ? paddingMainBefore : paddingCrossBefore;
            float ownPaddingCrossAfter = paddingAxesFlipped ? paddingMainAfter : paddingCrossAfter;

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
            if ((main.HasAuto || cross.HasAuto) && numParentDirectedChildren == 0)
            {
                float? pMain = main.HasAuto ? null : (float?)computedMain;
                float? pCross = cross.HasAuto ? null : (float?)computedCross;

                var contentSize = GetContentSize(elementHandle, parentLayoutType, pMain, pCross);

                if (contentSize.HasValue)
                {
                    // Recompute from floor so aspect-ratio output (if any) doesn't double-count.
                    // Padding is folded in so an auto-sized leaf (e.g. a padded text chip) reserves room
                    // for its padding, mirroring the children path (childrenMain/Cross include padding).
                    if (main.HasAuto)
                        computedMain = main.Floor(parentMain) + main.AutoFactor * (contentSize.Value.Item1 + paddingMainBefore + paddingMainAfter);
                    if (cross.HasAuto)
                        computedCross = cross.Floor(parentCross) + cross.AutoFactor * (contentSize.Value.Item2 + paddingCrossBefore + paddingCrossAfter);
                }
            }

            // Min constraint sizing layers AutoFactor*content on top of the floor
            var minMainUnit = GetMinMain(ref element, parentLayoutType);
            var minCrossUnit = GetMinCross(ref element, parentLayoutType);
            if ((minMainUnit.HasAuto || minCrossUnit.HasAuto) && numParentDirectedChildren == 0)
            {
                float? pMain = minMainUnit.HasAuto ? null : (float?)computedMain;
                float? pCross = minCrossUnit.HasAuto ? null : (float?)computedCross;

                var contentSize = GetContentSize(elementHandle, parentLayoutType, pMain, pCross);
                if (contentSize.HasValue)
                {
                    if (minMainUnit.HasAuto)
                        minMain = minMainUnit.Floor(parentMain) + minMainUnit.AutoFactor * (contentSize.Value.Item1 + paddingMainBefore + paddingMainAfter);
                    if (minCrossUnit.HasAuto)
                        minCross = minCrossUnit.Floor(parentCross) + minCrossUnit.AutoFactor * (contentSize.Value.Item2 + paddingCrossBefore + paddingCrossAfter);
                }
            }

            // Apply size constraints
            computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
            computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));

            // Determine parent sizes for children based on layout types
            (float actualParentMain, float actualParentCross) = parentLayoutType == layoutType
                ? (computedMain, computedCross)
                : (computedCross, computedMain);

            // Content-box sizes (padding excluded). Percentage / fixed children resolve against these
            // so they stay inside the parent's padding matching how stretch free-space is computed
            // below and how children are positioned at the padding offset. Stretch children keep using
            // the full actualParent* (the stretch loops subtract padding themselves).
            float innerParentMain = Maths.Max(0f, actualParentMain - ownPaddingMainBefore - ownPaddingMainAfter);
            float innerParentCross = Maths.Max(0f, actualParentCross - ownPaddingCrossBefore - ownPaddingCrossAfter);

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
            int? first = parentDirectedChildren.Count > 0 ? 0 : default(int?);
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
                if (childMainBefore.HasGrow)
                {
                    mainFlexSum += childMainBefore.Grow;
                    mainAxis.Add(new StretchItem(
                        i, // Use list index for StretchItem
                        childMainBefore.Grow,
                        StretchItem.ItemTypes.Before,
                        childMinMainBefore.ToPx(actualParentMain, DEFAULT_MIN),
                        childMaxMainBefore.ToPx(actualParentMain, DEFAULT_MAX)
                    ));
                }

                if (childMain.HasGrow)
                {
                    mainFlexSum += childMain.Grow;
                    mainAxis.Add(new StretchItem(
                        i, // Use list index for StretchItem
                        childMain.Grow,
                        StretchItem.ItemTypes.Size,
                        childMinMain.ToPx(actualParentMain, DEFAULT_MIN),
                        childMaxMain.ToPx(actualParentMain, DEFAULT_MAX)
                    ));
                }

                if (childMainAfter.HasGrow)
                {
                    mainFlexSum += childMainAfter.Grow;
                    mainAxis.Add(new StretchItem(
                        i, // Use list index for StretchItem
                        childMainAfter.Grow,
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
                float computedChildCross = childCross.ToPx(innerParentCross, 0f);

                // Get auto min cross size if needed
                var childMinCrossUnit = GetMinCross(ref child, layoutType);
                if (childMinCrossUnit.HasAuto)
                {
                    float? pCross = childMinCrossUnit.HasAuto ? null : (float?)innerParentCross;
                    var contentSize = ContentSizing(childHandle, layoutType, pCross, pCross);
                    if (contentSize.HasValue)
                    {
                        computedChildCross = childMinCrossUnit.Floor(innerParentCross) + childMinCrossUnit.AutoFactor * contentSize.Value.Item2;
                    }
                }

                // Compute fixed-size child main and cross for non-stretch children
                if (!childMain.HasGrow && !childCross.HasGrow)
                {
                    var childSize = DoLayout(childHandle, layoutType, innerParentMain, innerParentCross);
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
                // Children-derived size on each axis (account for layout direction).
                float childrenMain = (parentLayoutType == layoutType ? mainSum : crossMax)
                                     + paddingMainBefore + paddingMainAfter;
                float childrenCross = (parentLayoutType == layoutType ? crossMax : mainSum)
                                      + paddingCrossBefore + paddingCrossAfter;

                // main.HasAuto: parent's size IS Floor + AutoFactor * children. Replaces computedMain.
                if (main.HasAuto)
                {
                    computedMain = main.Floor(parentMain) + main.AutoFactor * childrenMain;
                    if (parentLayoutType == layoutType)
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                    else
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                }

                // MinMain.HasAuto: lifts the floor only. Pure Auto min => content-size minimum (today's behaviour).
                var autoMinMainUnit = GetMinMain(ref element, parentLayoutType);
                if (autoMinMainUnit.HasAuto)
                {
                    minMain = autoMinMainUnit.Floor(parentMain) + autoMinMainUnit.AutoFactor * childrenMain;
                    if (parentLayoutType == layoutType)
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, actualParentMain));
                    else
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, actualParentCross));
                }

                if (cross.HasAuto)
                {
                    computedCross = cross.Floor(parentCross) + cross.AutoFactor * childrenCross;
                    if (parentLayoutType == layoutType)
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
                    else
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
                }

                var autoMinCrossUnit = GetMinCross(ref element, parentLayoutType);
                if (autoMinCrossUnit.HasAuto)
                {
                    minCross = autoMinCrossUnit.Floor(parentCross) + autoMinCrossUnit.AutoFactor * childrenCross;
                    if (parentLayoutType == layoutType)
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, actualParentCross));
                    else
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, actualParentMain));
                }
            }

            computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
            computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));

            // Process cross-axis stretching for parent-directed children
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.Element.Data.PositionType != PositionType.ParentDirected ||
                    GetCross(ref child.Element.Data, layoutType).HasAuto)
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
                if (childCrossBefore.HasGrow)
                {
                    float childMinCrossBefore = GetMinCrossBefore(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    float childMaxCrossBefore = GetMaxCrossBefore(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MAX);

                    crossFlexSum += childCrossBefore.Grow;
                    child.CrossBefore = 0f;

                    crossAxis.Add(new StretchItem(
                        i,
                        childCrossBefore.Grow,
                        StretchItem.ItemTypes.Before,
                        childMinCrossBefore,
                        childMaxCrossBefore
                    ));
                }

                if (childCross.HasGrow)
                {
                    float childMinCross = GetMinCross(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    float childMaxCross = GetMaxCross(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MAX);

                    crossFlexSum += childCross.Grow;
                    child.Cross = 0f;

                    crossAxis.Add(new StretchItem(
                        i,
                        childCross.Grow,
                        StretchItem.ItemTypes.Size,
                        childMinCross,
                        childMaxCross
                    ));
                }

                if (childCrossAfter.HasGrow)
                {
                    float childMinCrossAfter = GetMinCrossAfter(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MIN);
                    float childMaxCrossAfter = GetMaxCrossAfter(ref child.Element.Data, layoutType)
                        .ToPx(actualParentCross, DEFAULT_MAX);

                    crossFlexSum += childCrossAfter.Grow;
                    child.CrossAfter = 0f;

                    crossAxis.Add(new StretchItem(
                        i,
                        childCrossAfter.Grow,
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
                        - ownPaddingCrossBefore
                        - ownPaddingCrossAfter
                        - child.CrossBefore
                        - child.Cross
                        - child.CrossAfter;

                    float totalViolation = 0f;

                    foreach (var item in crossAxis)
                    {
                        if (item.Frozen) continue;
                        float actualCross = (item.Factor * childCrossFreeSpace / crossFlexSum);

                        if (item.ItemType == StretchItem.ItemTypes.Size && !GetMain(ref child.Element.Data, layoutType).HasGrow)
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
                // Children-derived size on each axis (account for layout direction).
                float childrenMain = (parentLayoutType == layoutType ? mainSum : crossMax)
                                     + paddingMainBefore + paddingMainAfter;
                float childrenCross = (parentLayoutType == layoutType ? crossMax : mainSum)
                                      + paddingCrossBefore + paddingCrossAfter;

                // main.HasAuto: parent's size IS Floor + AutoFactor * children. Replaces computedMain.
                if (main.HasAuto)
                {
                    computedMain = main.Floor(parentMain) + main.AutoFactor * childrenMain;
                    if (parentLayoutType == layoutType)
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                    else
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                }

                // MinMain.HasAuto: lifts the floor only. Pure Auto min => content-size minimum (today's behaviour).
                var autoMinMainUnit = GetMinMain(ref element, parentLayoutType);
                if (autoMinMainUnit.HasAuto)
                {
                    minMain = autoMinMainUnit.Floor(parentMain) + autoMinMainUnit.AutoFactor * childrenMain;
                    if (parentLayoutType == layoutType)
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, actualParentMain));
                    else
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, actualParentCross));
                }

                if (cross.HasAuto)
                {
                    computedCross = cross.Floor(parentCross) + cross.AutoFactor * childrenCross;
                    if (parentLayoutType == layoutType)
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
                    else
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
                }

                var autoMinCrossUnit = GetMinCross(ref element, parentLayoutType);
                if (autoMinCrossUnit.HasAuto)
                {
                    minCross = autoMinCrossUnit.Floor(parentCross) + autoMinCrossUnit.AutoFactor * childrenCross;
                    if (parentLayoutType == layoutType)
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, actualParentCross));
                    else
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, actualParentMain));
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
                    float freeMainSpace = actualParentMain - mainSum - ownPaddingMainBefore - ownPaddingMainAfter;
                    float totalViolation = 0f;

                    foreach (var item in mainAxis)
                    {
                        if (item.Frozen) continue;
                        float actualMain = (item.Factor * freeMainSpace / mainFlexSum);
                        var child = children[item.Index];

                        if (item.ItemType == StretchItem.ItemTypes.Size)
                        {
                            float childCross = GetCross(ref child.Element.Data, layoutType).HasGrow ?
                                child.Cross : actualParentCross;

                            var childSize = DoLayout(child.Element, layoutType, actualMain, childCross);
                            child.Cross = childSize.Cross;
                            crossMax = Maths.Max(crossMax, child.CrossBefore + child.Cross + child.CrossAfter);

                            if (GetMinMain(ref child.Element.Data, layoutType).HasAuto)
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
                // Children-derived size on each axis (account for layout direction).
                float childrenMain = (parentLayoutType == layoutType ? mainSum : crossMax)
                                     + paddingMainBefore + paddingMainAfter;
                float childrenCross = (parentLayoutType == layoutType ? crossMax : mainSum)
                                      + paddingCrossBefore + paddingCrossAfter;

                // main.HasAuto: parent's size IS Floor + AutoFactor * children. Replaces computedMain.
                if (main.HasAuto)
                {
                    computedMain = main.Floor(parentMain) + main.AutoFactor * childrenMain;
                    if (parentLayoutType == layoutType)
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                    else
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                }

                // MinMain.HasAuto: lifts the floor only. Pure Auto min => content-size minimum (today's behaviour).
                var autoMinMainUnit = GetMinMain(ref element, parentLayoutType);
                if (autoMinMainUnit.HasAuto)
                {
                    minMain = autoMinMainUnit.Floor(parentMain) + autoMinMainUnit.AutoFactor * childrenMain;
                    if (parentLayoutType == layoutType)
                        actualParentMain = Maths.Min(maxMain, Maths.Max(minMain, actualParentMain));
                    else
                        actualParentCross = Maths.Min(maxMain, Maths.Max(minMain, actualParentCross));
                }

                if (cross.HasAuto)
                {
                    computedCross = cross.Floor(parentCross) + cross.AutoFactor * childrenCross;
                    if (parentLayoutType == layoutType)
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
                    else
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
                }

                var autoMinCrossUnit = GetMinCross(ref element, parentLayoutType);
                if (autoMinCrossUnit.HasAuto)
                {
                    minCross = autoMinCrossUnit.Floor(parentCross) + autoMinCrossUnit.AutoFactor * childrenCross;
                    if (parentLayoutType == layoutType)
                        actualParentCross = Maths.Min(maxCross, Maths.Max(minCross, actualParentCross));
                    else
                        actualParentMain = Maths.Min(maxCross, Maths.Max(minCross, actualParentMain));
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
                if (!childMain.HasGrow && !childCross.HasGrow)
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
                    ownPaddingCrossBefore, ownPaddingCrossAfter, elementChildCrossBefore, elementChildCrossAfter, i);
            }

            // Process main-axis stretching for self-directed children
            for (int i = parentDirectedChildren.Count; i < children.Count; i++)
            {
                var child = children[i];
                ProcessChildMainStretching(child, layoutType, actualParentMain, actualParentCross,
                    ownPaddingMainBefore, ownPaddingMainAfter, elementChildMainBefore, elementChildMainAfter, i);
            }

            // Compute stretch cross spacing for auto-sized children
            foreach (var child in children)
            {
                ProcessChildCrossSpacing(child, layoutType, actualParentCross,
                    ownPaddingCrossBefore, ownPaddingCrossAfter, elementChildCrossBefore, elementChildCrossAfter);
            }

            if (aspectRatio >= 0)
            {
                aspectRatio = Maths.Max(0.001f, aspectRatio); // Prevent divide-by-zero

                if (main.HasGrow && cross.HasGrow)
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
                else if (main.HasGrow)
                {
                    // Main stretches, cross is fixed
                    computedMain = computedCross * aspectRatio;
                }
                else if (cross.HasGrow)
                {
                    // Cross stretches, main is fixed
                    computedCross = computedMain / aspectRatio;
                }

                // Ensure we're still respecting min/max constraints
                computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
            }

            // ── Flex-wrap (opt-in via WrapContent) ─────────────────────
            // Only runs for containers that requested it; the normal single-line path below is
            // untouched for everything else. Parent-directed children flow onto new lines when they
            // overrun the main axis; the container auto-grows on the cross axis to fit all lines.
            //
            // LIMITATION: children should use a fixed or auto CROSS size. A child that is Stretch/Grow
            // on the cross axis was already sized against the whole container's cross extent (that pass
            // runs before lines exist here), so it spans the full container height and overlaps the
            // lines below it rather than filling only its own line. Detected below and warned once.
            if (element.ContentWrap && numParentDirectedChildren > 0)
            {
                float availMain = Maths.Max(0f, actualParentMain - ownPaddingMainBefore - ownPaddingMainAfter);
                float betweenMain = elementChildMainBetween.ToPx(actualParentMain, 0f);
                var justify = element.WrapJustify;

                float PackMain(ChildElementInfo c)
                {
                    var cm = GetMain(ref c.Element.Data, layoutType);
                    if (cm.HasGrow)
                    {
                        float mn = GetMinMain(ref c.Element.Data, layoutType).ToPx(availMain, 0f);
                        return mn > 0f ? mn : 1f; // grow children need a MinWidth to pack sensibly
                    }
                    return c.Main;
                }

                // Group parent-directed children into lines by their footprint.
                var lines = new List<(int Start, int Count, float Used, float Cross)>();
                int li = 0;
                while (li < numParentDirectedChildren)
                {
                    int start = li;
                    float used = 0f, lineCross = 0f;
                    while (li < numParentDirectedChildren)
                    {
                        var c = children[li];
                        if (GetCross(ref c.Element.Data, layoutType).HasGrow && _warnedWrapCrossStretch.Add(c.Element.Data.ID))
                            Console.WriteLine($"Warning: WrapContent child (id {c.Element.Data.ID}) is Stretch/Grow on the cross axis; it will span the full container and overlap other lines. Use a fixed or auto cross size inside a wrapping container.");
                        float fp = c.MainBefore + PackMain(c) + c.MainAfter;
                        float add = li == start ? fp : betweenMain + fp;
                        if (li > start && used + add > availMain + 0.5f) break; // always keep at least one per line
                        used += add;
                        lineCross = Maths.Max(lineCross, c.CrossBefore + c.Cross + c.CrossAfter);
                        li++;
                    }
                    lines.Add((start, li - start, used, lineCross));
                }

                // Fill: grow every item on the line so it consumes the full main axis (re-layout each
                // at its new width so stretch content reflows). Items should be Grow/Stretch on main.
                if (justify == WrapJustify.Fill)
                {
                    foreach (var ln in lines)
                    {
                        if (ln.Count == 0) continue;
                        float spacing = betweenMain * (ln.Count - 1);
                        float margins = 0f;
                        for (int k = ln.Start; k < ln.Start + ln.Count; k++)
                            margins += children[k].MainBefore + children[k].MainAfter;
                        float per = Maths.Max(1f, (availMain - spacing - margins) / ln.Count);
                        for (int k = ln.Start; k < ln.Start + ln.Count; k++)
                        {
                            var c = children[k];
                            var cs = DoLayout(c.Element, layoutType, per, actualParentCross);
                            c.Main = cs.Main; // honor the child's own min/max clamp, not the raw share
                            c.Cross = cs.Cross;
                        }
                    }
                }

                // Cross size = stacked line heights (+ inter-line gaps). The wrapping/stacking axis is
                // this element's OWN cross axis; map it back to the returned main/cross using the same
                // parent-vs-own swap the rest of the layout uses (a Row inside a Column reports its
                // vertical extent as `computedMain`).
                float totalCross = 0f;
                for (int k = 0; k < lines.Count; k++)
                    totalCross += lines[k].Cross + (k > 0 ? betweenMain : 0f);

                if (paddingAxesFlipped)
                {
                    if (main.HasAuto)
                    {
                        computedMain = main.Floor(parentMain) + main.AutoFactor * (totalCross + paddingMainBefore + paddingMainAfter);
                        computedMain = Maths.Min(maxMain, Maths.Max(minMain, computedMain));
                    }
                }
                else if (cross.HasAuto)
                {
                    computedCross = cross.Floor(parentCross) + cross.AutoFactor * (totalCross + paddingCrossBefore + paddingCrossAfter);
                    computedCross = Maths.Min(maxCross, Maths.Max(minCross, computedCross));
                }

                // Position each line, applying the justify to leftover main space.
                float crossPos = ownPaddingCrossBefore;
                foreach (var ln in lines)
                {
                    float leftover = Maths.Max(0f, availMain - ln.Used);
                    float mainStart = ownPaddingMainBefore;
                    float extraGap = 0f;
                    switch (justify)
                    {
                        case WrapJustify.Center: mainStart += leftover * 0.5f; break;
                        case WrapJustify.End: mainStart += leftover; break;
                        case WrapJustify.SpaceBetween: if (ln.Count > 1) extraGap = leftover / (ln.Count - 1); break;
                        case WrapJustify.SpaceAround: if (ln.Count > 0) { extraGap = leftover / ln.Count; mainStart += extraGap * 0.5f; } break;
                    }

                    float mp = mainStart;
                    for (int k = ln.Start; k < ln.Start + ln.Count; k++)
                    {
                        var c = children[k];
                        mp += c.MainBefore;
                        SetElementBounds(c.Element, layoutType, mp, crossPos + c.CrossBefore, c.Main, c.Cross);
                        mp += c.Main + c.MainAfter + betweenMain + extraGap;
                    }
                    crossPos += ln.Cross + betweenMain;
                }

                // Self-directed children keep their normal placement.
                for (int k = numParentDirectedChildren; k < children.Count; k++)
                {
                    var c = children[k];
                    if (c.Element.Data.PositionType == PositionType.SelfDirected)
                        SetElementBounds(c.Element, layoutType,
                            c.MainBefore + ownPaddingMainBefore, c.CrossBefore + ownPaddingCrossBefore, c.Main, c.Cross);
                }

                return new UISize(computedMain, computedCross);
            }

            // Set bounds for all children (positioned along this element's own axes; see ownPadding*).
            float mainPos = 0f;
            foreach (var child in children)
            {
                if (child.Element.Data.PositionType == PositionType.SelfDirected)
                {
                    SetElementBounds(
                        child.Element,
                        layoutType,
                        child.MainBefore + ownPaddingMainBefore,
                        child.CrossBefore + ownPaddingCrossBefore,
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
                        mainPos + ownPaddingMainBefore,
                        child.CrossBefore + ownPaddingCrossBefore,
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
            float paddingCrossBefore,
            float paddingCrossAfter,
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
            if (childCrossBefore.HasGrow)
            {
                float min = GetMinCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossBefore.Grow;
                child.CrossBefore = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossBefore.Grow, StretchItem.ItemTypes.Before, min, max));
            }

            if (childCross.HasGrow)
            {
                float min = GetMinCross(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCross(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCross.Grow;
                child.Cross = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCross.Grow, StretchItem.ItemTypes.Size, min, max));
            }

            if (childCrossAfter.HasGrow)
            {
                float min = GetMinCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossAfter.Grow;
                child.CrossAfter = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossAfter.Grow, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenCrossCount = crossAxis.Count;
            while (unfrozenCrossCount > 0)
            {
                float crossFreeSpace = parentCross - paddingCrossBefore - paddingCrossAfter
                    - child.CrossBefore - child.Cross - child.CrossAfter;

                float totalViolation = 0f;

                foreach (var item in crossAxis)
                {
                    if (item.Frozen) continue;
                    float actualCross = (item.Factor * crossFreeSpace / crossFlexSum);

                    if (item.ItemType == StretchItem.ItemTypes.Size && !GetMain(ref child.Element.Data, layoutType).HasGrow)
                    {
                        var childSize = DoLayout(child.Element, layoutType, parentMain, actualCross);
                        if (GetMinCross(ref child.Element.Data, layoutType).HasAuto)
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
            float paddingMainBefore,
            float paddingMainAfter,
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
            if (childMainBefore.HasGrow)
            {
                float min = GetMinMainBefore(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                float max = GetMaxMainBefore(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMainBefore.Grow;
                mainAxis.Add(new StretchItem(childIndex, childMainBefore.Grow, StretchItem.ItemTypes.Before, min, max));
            }

            if (childMain.HasGrow)
            {
                float min = GetMinMain(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                float max = GetMaxMain(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMain.Grow;
                mainAxis.Add(new StretchItem(childIndex, childMain.Grow, StretchItem.ItemTypes.Size, min, max));
            }

            if (childMainAfter.HasGrow)
            {
                float min = GetMinMainAfter(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MIN);
                float max = GetMaxMainAfter(ref child.Element.Data, layoutType).ToPx(parentMain, DEFAULT_MAX);

                mainFlexSum += childMainAfter.Grow;
                mainAxis.Add(new StretchItem(childIndex, childMainAfter.Grow, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenMainCount = mainAxis.Count;
            while (unfrozenMainCount > 0)
            {
                float mainFreeSpace = parentMain - paddingMainBefore - paddingMainAfter
                    - child.MainBefore - child.Main - child.MainAfter;

                float totalViolation = 0f;

                foreach (var item in mainAxis)
                {
                    if (item.Frozen) continue;
                    float actualMain = (item.Factor * mainFreeSpace / mainFlexSum);

                    if (item.ItemType == StretchItem.ItemTypes.Size)
                    {
                        float childCross = GetCross(ref child.Element.Data, layoutType).HasGrow ?
                            child.Cross : parentCross;

                        var childSize = DoLayout(child.Element, layoutType, actualMain, childCross);
                        child.Cross = childSize.Cross;

                        if (GetMinMain(ref child.Element.Data, layoutType).HasAuto)
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
            float paddingCrossBefore,
            float paddingCrossAfter,
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
            if (!childCrossBefore.HasGrow && !childCrossAfter.HasGrow)
                return;

            float crossFlexSum = 0f;
            var crossAxis = new List<StretchItem>();
            int childIndex = 0; // Just a placeholder since we're only dealing with this specific child

            // Collect stretch cross items
            if (childCrossBefore.HasGrow)
            {
                float min = GetMinCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCrossBefore(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossBefore.Grow;
                child.CrossBefore = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossBefore.Grow, StretchItem.ItemTypes.Before, min, max));
            }

            if (childCrossAfter.HasGrow)
            {
                float min = GetMinCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MIN);
                float max = GetMaxCrossAfter(ref child.Element.Data, layoutType).ToPx(parentCross, DEFAULT_MAX);

                crossFlexSum += childCrossAfter.Grow;
                child.CrossAfter = 0f;

                crossAxis.Add(new StretchItem(childIndex, childCrossAfter.Grow, StretchItem.ItemTypes.After, min, max));
            }

            int unfrozenCrossCount = crossAxis.Count;
            while (unfrozenCrossCount > 0)
            {
                float crossFreeSpace = parentCross - paddingCrossBefore - paddingCrossAfter
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


        // Records this frame's ProcessText result so the element's later layout/render passes can
        // reuse it (see the memo early-out at the top of ProcessText). widthIndependent marks text
        // whose size does not depend on availableWidth (plain left/no-wrap/no-truncate).
        private static Float2 Memo(ref ElementData element, Float2 size, float availableWidth, bool widthIndependent)
        {
            element._textMemoSize = size;
            element._textMemoWidth = availableWidth;
            element._textMemoWidthIndependent = widthIndependent;
            element._textMemoValid = true;
            return size;
        }

        internal static Float2 ProcessText(this ref ElementData element, Paper gui, float availableWidth)
        {
            //if (element.ProcessedText) return Vector2.zero;

            if (string.IsNullOrWhiteSpace(element.Paragraph)) return Float2.Zero;

            // Per-frame memo: this runs several times per frame (stretch resolution) plus once at
            // render. Once we've computed the size this frame, reuse it while the width still applies
            // (width-independent plain text always applies; width-dependent text must match). The
            // cached _textLayout/_quillRichText/_quillMarkdown set on the first compute stay valid on
            // the same per-frame ElementData, so the render pass still finds them.
            if (element._textMemoValid
                && (element._textMemoWidthIndependent || element._textMemoWidth == availableWidth))
                return element._textMemoSize;

            element.ProcessedText = true;

            var canvas = gui.Canvas;
            if (canvas == null) throw new InvalidOperationException("Canvas is not set.");

            // TextLayout/markdown sizes returned by the canvas are in pixel space
            // (scaled by FramebufferScale). The layout engine works in logical units.
            float invScale = 1.0f / canvas.FramebufferScale;

            if (element.IsRichText)
            {
                var settings = new RichTextLayoutSettings {
                    RegularFont = element.Font,
                    BoldFont = element.FontBold,
                    ItalicFont = element.FontItalic,
                    BoldItalicFont = element.FontBoldItalic,
                    MonoFont = element.FontMono,
                    PixelSize = element._elementStyle.GetFontSize(),
                    Quality = element._elementStyle.GetTextQuality(),
                    LineHeight = element._elementStyle.GetLineHeight(),
                    LetterSpacing = element._elementStyle.GetLetterSpacing(),
                    WordSpacing = element._elementStyle.GetWordSpacing(),
                    TabSize = element._elementStyle.GetTabSize(),
                    WrapMode = element.WrapMode,
                    MaxWidth = element.WrapMode == TextWrapMode.Wrap ? availableWidth : 0f,
                    Alignment = ToScribeAlignment(element.TextAlignment),
                };

                // Reuse the cached layout when source + width haven't changed so the layout's
                // animation start time (set on first Draw) survives across frames.
                var cached = gui.GetElementStorageById<Quill.Canvas.QuillRichText?>(element.ID, Paper.RichTextLayoutKey, null);
                var cachedSrc = gui.GetElementStorageById<string>(element.ID, Paper.RichTextSourceKey, null);
                float cachedWidth = gui.GetElementStorageById<float>(element.ID, Paper.RichTextWidthKey, -1f);

                Quill.Canvas.QuillRichText richText;
                if (cached.HasValue && cachedSrc == element.Paragraph && cachedWidth == settings.MaxWidth)
                {
                    richText = cached.Value;
                }
                else
                {
                    richText = canvas.CreateRichText(element.Paragraph, settings);
                    gui.SetElementStorageById<Quill.Canvas.QuillRichText?>(element.ID, Paper.RichTextLayoutKey, richText);
                    gui.SetElementStorageById<string>(element.ID, Paper.RichTextSourceKey, element.Paragraph);
                    gui.SetElementStorageById<float>(element.ID, Paper.RichTextWidthKey, settings.MaxWidth);
                }
                element._quillRichText = richText;

                // QuillRichText.Size is already in logical units. Width matters only when wrapping.
                return Memo(ref element, richText.Size, availableWidth, element.WrapMode != TextWrapMode.Wrap);
            }

            if (element.IsMarkdown == false)
            {
                var settings = TextLayoutSettings.Default;

                settings.WordSpacing = element._elementStyle.GetWordSpacing();
                settings.LetterSpacing = element._elementStyle.GetLetterSpacing();
                settings.LineHeight = element._elementStyle.GetLineHeight();
                settings.TabSize = element._elementStyle.GetTabSize();
                settings.PixelSize = element._elementStyle.GetFontSize();
                settings.Quality = element._elementStyle.GetTextQuality();

                settings.Alignment = ToScribeAlignment(element.TextAlignment);

                settings.Font = element.Font;
                settings.WrapMode = element.WrapMode;
                settings.MaxWidth = availableWidth;

                // Per-element cache for width-independent text: left-aligned, no-wrap, non-truncated
                // text lays out identically regardless of the element width (MaxWidth only affects
                // wrapping and center/right alignment). Keying purely on content + font/metrics lets
                // us reuse the same TextLayout across frames and across the measure/draw passes,
                // surviving Scribe's global LRU cap (a long scrolling list of distinct labels would
                // otherwise thrash it). Width-dependent text (wrap / truncate / center / right) keeps
                // going straight through CreateLayout so its width changes are always honoured.
                if (settings.Alignment == Scribe.TextAlignment.Left
                    && element.WrapMode == TextWrapMode.NoWrap && !element.Truncate)
                {
                    var ptKey = new PlainTextKey(element.Paragraph, settings, canvas.FramebufferScale);
                    var cachedLayout = gui.GetElementStorageById<TextLayout>(element.ID, Paper.PlainTextLayoutKey, null);
                    var cachedKey = gui.GetElementStorageById<PlainTextKey>(element.ID, Paper.PlainTextKeyKey, default);

                    if (cachedLayout != null && cachedKey.Equals(ptKey))
                    {
                        element._textLayout = cachedLayout;
                        return Memo(ref element, (Float2)cachedLayout.Size * invScale, availableWidth, true);
                    }

                    element._textLayout = canvas.CreateLayout(element.Paragraph, settings);
                    gui.SetElementStorageById<TextLayout>(element.ID, Paper.PlainTextLayoutKey, element._textLayout);
                    gui.SetElementStorageById<PlainTextKey>(element.ID, Paper.PlainTextKeyKey, ptKey);
                    return Memo(ref element, (Float2)element._textLayout.Size * invScale, availableWidth, true);
                }

                string text = element.Paragraph;

                // Single-line truncation: if the text overflows the element, drop trailing characters
                // and append an ellipsis that is guaranteed to fit. The result depends on the element
                // width, so it uses a width-keyed cache (the width-independent cache above skips
                // truncated text) to avoid re-running the binary search + layout every frame.
                if (element.Truncate && element.WrapMode != TextWrapMode.Wrap && availableWidth > 0f)
                {
                    var trKey = new PlainTextKey(element.Paragraph, settings, canvas.FramebufferScale, availableWidth);
                    var cachedLayout = gui.GetElementStorageById<TextLayout>(element.ID, Paper.TruncTextLayoutKey, null);
                    var cachedKey = gui.GetElementStorageById<PlainTextKey>(element.ID, Paper.TruncTextKeyKey, default);
                    if (cachedLayout != null && cachedKey.Equals(trKey))
                    {
                        element._textLayout = cachedLayout;
                        return Memo(ref element, (Float2)cachedLayout.Size * invScale, availableWidth, false);
                    }

                    var measure = settings;
                    measure.WrapMode = TextWrapMode.NoWrap;
                    measure.MaxWidth = 0f;
                    float LogicalW(string s) => canvas.MeasureText(s, measure).X * invScale;

                    if (LogicalW(text) > availableWidth)
                    {
                        const string ell = "...";
                        float ellW = LogicalW(ell);
                        // Binary-search the longest prefix whose width + ellipsis still fits.
                        int lo = 0, hi = text.Length;
                        while (lo < hi)
                        {
                            int mid = (lo + hi + 1) / 2;
                            if (LogicalW(text[..mid]) + ellW <= availableWidth) lo = mid;
                            else hi = mid - 1;
                        }
                        text = lo > 0 ? text[..lo] + ell : ell;
                    }

                    element._textLayout = canvas.CreateLayout(text, settings);
                    gui.SetElementStorageById<TextLayout>(element.ID, Paper.TruncTextLayoutKey, element._textLayout);
                    gui.SetElementStorageById<PlainTextKey>(element.ID, Paper.TruncTextKeyKey, trKey);
                    return Memo(ref element, (Float2)element._textLayout.Size * invScale, availableWidth, false);
                }

                element._textLayout = canvas.CreateLayout(text, settings);

                return Memo(ref element, (Float2)element._textLayout.Size * invScale, availableWidth, false);
            }
            else
            {
                var r = element.Font;
                var m = element.FontMono;
                var b = element.FontBold;
                var i = element.FontItalic;
                var bi = element.FontBoldItalic;
                var settings = MarkdownLayoutSettings.Default(r, availableWidth, m, b, i, bi);
                settings.Quality = element._elementStyle.GetTextQuality();

                element._quillMarkdown = canvas.CreateMarkdown(element.Paragraph, settings);

                var markdownResult = element._quillMarkdown;
                return Memo(ref element, (markdownResult?.Size ?? Float2.Zero) * invScale, availableWidth, false);
            }
        }

        private static Scribe.TextAlignment ToScribeAlignment(TextAlignment a)
        {
            if (a == TextAlignment.Left || a == TextAlignment.MiddleLeft || a == TextAlignment.BottomLeft)
                return Scribe.TextAlignment.Left;
            if (a == TextAlignment.Center || a == TextAlignment.MiddleCenter || a == TextAlignment.BottomCenter)
                return Scribe.TextAlignment.Center;
            if (a == TextAlignment.Right || a == TextAlignment.MiddleRight || a == TextAlignment.BottomRight)
                return Scribe.TextAlignment.Right;
            return Scribe.TextAlignment.Left;
        }

        private static void ComputeAbsolutePositions(ref ElementData element, Paper gui, float parentX = 0f, float parentY = 0f)
        {
            // Set absolute position based on parent's position
            element.X = parentX + element.RelativeX;
            element.Y = parentY + element.RelativeY;

            // Clamp to screen bounds if requested
            if (element._clampToScreen)
            {
                float screenW = gui.RootElement.Data.LayoutWidth;
                float screenH = gui.RootElement.Data.LayoutHeight;
                float margin = 4f;

                float maxX = screenW - element.LayoutWidth - margin;
                float maxY = screenH - element.LayoutHeight - margin;

                float clampedX = Maths.Max(margin, Maths.Min(maxX, element.X));
                float clampedY = Maths.Max(margin, Maths.Min(maxY, element.Y));

                // Update RelativeX/Y so children compute from the clamped position
                element.RelativeX += clampedX - element.X;
                element.RelativeY += clampedY - element.Y;
                element.X = clampedX;
                element.Y = clampedY;
            }

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

    /// <summary>
    /// Fingerprint of the inputs that determine a width-independent plain-text layout. Stored
    /// alongside the cached <see cref="TextLayout"/> in element storage; a mismatch rebuilds it.
    /// Width, wrap, truncation and alignment are intentionally absent: this key is only used for
    /// left-aligned, non-wrapping, non-truncating text, whose layout does not depend on them.
    /// </summary>
    internal readonly struct PlainTextKey : IEquatable<PlainTextKey>
    {
        readonly string Text;
        readonly float PixelSize, LetterSpacing, WordSpacing, LineHeight, FbScale, Width;
        readonly int TabSize;
        readonly FontQuality Quality;
        readonly FontFile Font;

        // Width is only used by the width-dependent (truncation) cache; the width-independent cache
        // leaves it at 0.
        public PlainTextKey(string text, in TextLayoutSettings s, float fbScale, float width = 0f)
        {
            Text = text;
            PixelSize = s.PixelSize;
            LetterSpacing = s.LetterSpacing;
            WordSpacing = s.WordSpacing;
            LineHeight = s.LineHeight;
            TabSize = s.TabSize;
            Quality = s.Quality;
            Font = s.Font;
            FbScale = fbScale;
            Width = width;
        }

        public bool Equals(PlainTextKey o) =>
            ReferenceEquals(Font, o.Font) && TabSize == o.TabSize && Quality == o.Quality
            && PixelSize == o.PixelSize && LetterSpacing == o.LetterSpacing && WordSpacing == o.WordSpacing
            && LineHeight == o.LineHeight && FbScale == o.FbScale && Width == o.Width
            && (ReferenceEquals(Text, o.Text) || string.Equals(Text, o.Text));

        public override bool Equals(object obj) => obj is PlainTextKey o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(Text, PixelSize, TabSize, (int)Quality, Font, FbScale, Width);
    }
}
