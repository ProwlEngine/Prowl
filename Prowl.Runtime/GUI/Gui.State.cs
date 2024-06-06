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

        public void SetZIndex(int index, bool keepClipSpace = true)
        {
            if (!_drawList.ContainsKey(index))
            {
                _drawList[index] = new UIDrawList();
                _drawList[index].PushTextureID(UIDrawList.DefaultFont.Texture.Handle);
            }

            // Copy over the clip rect from the previous list
            if (keepClipSpace)
            {
                var previousList = _drawList[CurrentNode.ZIndex];
                _drawList[index].PushClipRect(previousList._ClipRectStack.Peek());
            }

            CurrentNode.ZIndex = index;
        }

        public T GetGlobalStorage<T>(string key) where T : unmanaged => GetStorage<T>(rootNode, key, default);
        public void SetGlobalStorage<T>(string key, T value) where T : unmanaged => SetStorage(rootNode, key, value);

        public T GetStorage<T>(string key) where T : unmanaged => GetStorage<T>(CurrentNode, key, default);
        public T GetStorage<T>(string key, T defaultValue) where T : unmanaged => GetStorage<T>(CurrentNode, key, defaultValue);

        public void SetStorage<T>(string key, T value) where T : unmanaged => SetStorage(CurrentNode, key, value);

        public T GetStorage<T>(LayoutNode node, string key, T defaultValue) where T : unmanaged
        {
            if (!_storage.TryGetValue(node.ID, out var storage))
                return defaultValue;

            if (storage.ContainsKey(key))
                return (T)storage[key];

            return defaultValue;
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