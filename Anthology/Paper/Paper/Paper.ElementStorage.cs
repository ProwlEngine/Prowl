using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        private ElementData[] _elements = new ElementData[1024];
        private int _elementCount = 0;
        private Stack<int> _freeIndices = new Stack<int>();
        private int _rootElementIndex = -1;

        public int ElementCount => _elementCount;

        public ref ElementData GetElementData(int index)
        {
            if (index < 0 || index >= _elementCount)
                throw new IndexOutOfRangeException($"Element index {index} is out of range");
            return ref _elements[index];
        }

        public ElementHandle CreateElement(ulong id)
        {
            int index;
            ElementData elementData = ElementData.Create(id);

            if (_freeIndices.Count > 0)
            {
                index = _freeIndices.Pop();
                _elements[index] = elementData;
            }
            else
            {
                if (_elementCount >= _elements.Length)
                {
                    // Resize array if needed
                    var newElements = new ElementData[_elements.Length * 2];
                    Array.Copy(_elements, newElements, _elementCount);
                    _elements = newElements;
                }
                index = _elementCount;
                _elements[index] = elementData;
                _elementCount++;
            }

            return new ElementHandle(this, index);
        }

        public void DestroyElement(ElementHandle handle)
        {
            if (!handle.IsValid)
                return;

            ref var elementData = ref handle.Data;

            // Remove from parent's children list
            if (elementData.ParentIndex != -1)
            {
                ref var parentData = ref GetElementData(elementData.ParentIndex);
                parentData.ChildIndices.Remove(handle.Index);
            }

            // Recursively destroy all children
            var childIndices = new List<int>(elementData.ChildIndices);
            foreach (int childIndex in childIndices)
            {
                var childHandle = new ElementHandle(this, childIndex);
                DestroyElement(childHandle);
            }

            // Clear the element data
            elementData = default;
            _freeIndices.Push(handle.Index);
        }

        public ElementHandle GetRootElementHandle()
        {
            if (_rootElementIndex == -1)
                throw new InvalidOperationException("Root element not initialized");
            return new ElementHandle(this, _rootElementIndex);
        }

        internal void InitializeRootElement(double width, double height)
        {
            var rootHandle = CreateElement(0);
            _rootElementIndex = rootHandle.Index;

            ref var rootData = ref rootHandle.Data;
            rootData._elementStyle.SetDirectValue(GuiProp.Width, UnitValue.Pixels(width));
            rootData._elementStyle.SetDirectValue(GuiProp.Height, UnitValue.Pixels(height));
        }

        internal void ClearElements()
        {
            _elementCount = 0;
            _freeIndices.Clear();
            _rootElementIndex = -1;
        }

        // Helper method to find element by ID
        public ElementHandle FindElementHandleByID(ulong id)
        {
            for (int i = 0; i < _elementCount; i++)
            {
                if (_elements[i].ID == id)
                    return new ElementHandle(this, i);
            }
            return default;
        }

        // Validation method for debugging
        public void ValidateElementIntegrity()
        {
            for (int i = 0; i < _elementCount; i++)
            {
                if (_freeIndices.Contains(i))
                    continue;

                ref var element = ref _elements[i];
                
                // Validate parent-child relationships
                if (element.ParentIndex != -1)
                {
                    if (element.ParentIndex < 0 || element.ParentIndex >= _elementCount)
                        throw new InvalidOperationException($"Element {i} has invalid parent index {element.ParentIndex}");
                    
                    ref var parent = ref _elements[element.ParentIndex];
                    if (!parent.ChildIndices.Contains(i))
                        throw new InvalidOperationException($"Element {i} claims parent {element.ParentIndex} but parent doesn't list it as child");
                }

                foreach (int childIndex in element.ChildIndices)
                {
                    if (childIndex < 0 || childIndex >= _elementCount)
                        throw new InvalidOperationException($"Element {i} has invalid child index {childIndex}");
                    
                    ref var child = ref _elements[childIndex];
                    if (child.ParentIndex != i)
                        throw new InvalidOperationException($"Element {i} claims child {childIndex} but child doesn't reference it as parent");
                }
            }
        }
    }
}
