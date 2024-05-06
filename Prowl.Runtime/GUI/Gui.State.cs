using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public int CurrentZIndex => CurrentNode.ZIndex;

        private static Dictionary<ulong, Hashtable> _storage = [];

        public void SetZIndex(int index)
        {
            if (!_drawList.ContainsKey(index))
            {
                _drawList[index] = new UIDrawList();
                _drawList[index].PushTextureID(UIDrawList.DefaultFont.Texture.Handle);
            }

            // Copy over the clip rect from the previous list
            var previousList = _drawList[CurrentNode.ZIndex];
            _drawList[index].PushClipRect(previousList._ClipRectStack.Peek());

            CurrentNode.ZIndex = index;
        }

        public T GetStorage<T>(string key) where T : unmanaged => GetStorage<T>(CurrentNode, key);

        public void SetStorage<T>(string key, T value) where T : unmanaged => SetStorage(CurrentNode, key, value);

        public T GetStorage<T>(LayoutNode node, string key) where T : unmanaged
        {
            if (!_storage.TryGetValue(node.ID, out var storage))
                return default;

            if (storage.ContainsKey(key))
                return (T)storage[key];

            return default;
        }

        public void SetStorage<T>(LayoutNode node, string key, T value) where T : unmanaged
        {
            if (!_storage.TryGetValue(node.ID, out var storage))
                _storage[node.ID] = storage = [];

            storage[key] = value;
        }

        public void PushID(ulong id) => IDStack.Push(id);
        public void PopID() => IDStack.Pop();

    }
}