using Prowl.Runtime.GUI.Graphics;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public class GuiState
        {
            public int ZIndex = 0;

            public GuiState Clone()
            {
                return new GuiState() {
                    ZIndex = ZIndex
                };
            }
        }

        private static Dictionary<ulong, Hashtable> _storage = [];

        public void SetZIndex(int index)
        {
            if (!_drawList.ContainsKey(index))
            {
                _drawList[index] = new UIDrawList();
                _drawList[index].PushTextureID(UIDrawList.DefaultFont.Texture.Handle);
            }

            // Copy over the clip rect from the previous list
            var previousList = _drawList[CurrentState.ZIndex];
            _drawList[index].PushClipRect(previousList._ClipRectStack.Peek());

            CurrentState.ZIndex = index;
        }

        public T GetStorage<T>(string key) where T : unmanaged
        {
            if (!_storage.TryGetValue(CurrentNode.ID, out var storage))
                return default;

            if (storage.ContainsKey(key))
                return (T)storage[key];

            return default;
        }

        public void SetStorage<T>(string key, T value) where T : unmanaged
        {
            if (!_storage.TryGetValue(CurrentNode.ID, out var storage))
                _storage[CurrentNode.ID] = storage = [];

            storage[key] = value;
        }

        public void PushID(ulong id) => IDStack.Push(id);
        public void PopID() => IDStack.Pop();

    }
}