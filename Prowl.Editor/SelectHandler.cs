using HexaEngine.ImGuiNET;
using Prowl.Runtime;

namespace Prowl.Editor;


public static class GlobalSelectHandler
{
    public static event Action<object>? OnGlobalSelectObject;
    public static event Action<object>? OnGlobalDeselectObject;
    public static void Select(object obj) => OnGlobalSelectObject?.Invoke(obj);
    public static void Deselect(object obj) =>  OnGlobalDeselectObject?.Invoke(obj);
}


/// <summary>
/// A General Purpose Selection Handler for the Editor
/// </summary>
/// <typeparam name="T">The Type you want to select, Must be of type 'Class'</typeparam>
public class SelectHandler<T> where T : class
{
    bool selectedThisFrame = false;
    List<T> selected = new();
    SortedList<int, T> previousFrameSorted;
    SortedList<int, T> sorted = new();
    int lastSelectedIndex = -1;

    public bool SelectedThisFrame => selectedThisFrame;
    public List<T> Selected => selected;
    public int Count => selected.Count;

    public event Action<T>? OnSelectObject;
    public event Action<T>? OnDeselectObject;

    private Func<T, bool> CheckIsDestroyed;
    private Func<T, T, bool> Equals;

    public SelectHandler(Func<T, bool> checkIsDestroyed, Func<T, T, bool> equals)
    {
        CheckIsDestroyed = checkIsDestroyed;
        Equals = equals;
    }

    public void StartFrame()
    {
        // Clear dead references
        for (int i = 0; i < selected.Count; i++) {
            if (CheckIsDestroyed.Invoke(selected[i])) {
                selected.RemoveAt(i);
                i--;
            }
        }

        selectedThisFrame = false;
        previousFrameSorted = sorted;
        sorted = new();

        if (lastSelectedIndex == -1 && selected.Count > 0) {
            SetSelectedIndex((T)selected[0]);
        }
    }

    public void SetSelection(T obj)
    {
        Clear();
        selected.Add(obj);
        selectedThisFrame = true;
        OnSelectObject?.Invoke(obj);
        GlobalSelectHandler.Select(obj);

        SetSelectedIndex(obj);
    }

    public void SetSelection(T[] objs)
    {
        Clear();
        selectedThisFrame = true;
        for (int i = 0; i < objs.Length; i++) {
            selected.Add(objs[i]);
            OnSelectObject?.Invoke(objs[i]);
            GlobalSelectHandler.Select(objs[i]);
        }
        if(objs.Length > 0)
            SetSelectedIndex(objs[0]);
    }

    public bool IsSelected(T obj)
    {
        for (int i = 0; i < selected.Count; i++) {
            if (Equals.Invoke(selected[i], obj))
                return true;
        }
        return false;
    }

    public void Foreach(Action<T> value)
    {
        var copy = new List<T>(selected);
        copy.ForEach((weak) => {
            value?.Invoke((T)weak);
        });
    }

    public void HandleSelectable(int index, T obj)
    {
        // This is a list of all the objects that are selectable sorted in order that their drawn in
        sorted.Add(index, obj);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
            int prevLastIndex = lastSelectedIndex;
            Select(obj);
            if (prevLastIndex != index) {
                if (prevLastIndex != -1 && Input.GetKey(Silk.NET.Input.Key.ShiftLeft)) {
                    // Bulk Select
                    for (int i = Math.Min(prevLastIndex, index); i <= Math.Max(prevLastIndex, index); i++) {
                        if (previousFrameSorted.TryGetValue(i, out var o)) {
                            if (CheckIsDestroyed.Invoke(o)) {
                                // Always additive so we cant call Select(o) here as that checks if ctrl is down
                                selected.Add(o);
                                selectedThisFrame = true;
                                OnSelectObject?.Invoke(o);
                                GlobalSelectHandler.Select(o);
                            }
                        }
                    }
                }
            }
            lastSelectedIndex = index;
        }
    }

    public void SelectIfNot(T obj)
    {
        if (!IsSelected(obj))
            Select(obj);
    }

    public void Select(T obj)
    {
        selectedThisFrame = true;
        if (Input.GetKey(Silk.NET.Input.Key.ControlLeft)) {
            // Additive
            if (IsSelected(obj)) {
                for (int i = 0; i < selected.Count; i++) {
                    if (Equals.Invoke(selected[i], obj)) {
                        selected.RemoveAt(i);
                        break;
                    }
                }
                OnDeselectObject?.Invoke(obj);
                GlobalSelectHandler.Deselect(obj);
            } else {
                selected.Add(obj);
                OnSelectObject?.Invoke(obj);
                GlobalSelectHandler.Select(obj);
            }
        } else SetSelection(obj);

        SetSelectedIndex(obj);
    }

    private void SetSelectedIndex(T entity)
    {
        if (previousFrameSorted == null) return;
        // if sorted has this value using reference equals, set lastSelectedIndex to the index of it
        for (int i = 0; i < previousFrameSorted.Count; i++) {
            if (Equals.Invoke(previousFrameSorted.Values[i], entity)) {
                lastSelectedIndex = previousFrameSorted.Keys[i];
                break;
            }
        }
    }

    public void Clear()
    {
        foreach (var item in selected) {
            OnDeselectObject?.Invoke(item);
            GlobalSelectHandler.Deselect(item);
        }
        selected.Clear();
        lastSelectedIndex = -1;
    }
}