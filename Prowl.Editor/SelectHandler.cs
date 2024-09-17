// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

namespace Prowl.Editor;


public static class GlobalSelectHandler
{
    public static event Action<object>? OnGlobalSelectObject;
    public static event Action<object>? OnGlobalDeselectObject;
    public static void Select(object obj) => OnGlobalSelectObject?.Invoke(obj);
    public static void Deselect(object obj) => OnGlobalDeselectObject?.Invoke(obj);
}


/// <summary>
/// A General Purpose Selection Handler for the Editor
/// </summary>
/// <typeparam name="T">The Type you want to select, Must be of type 'Class'</typeparam>
public class SelectHandler<T> where T : class
{
    bool selectedThisFrame;
    readonly List<T> selected = new();
    SortedList<int, T> previousFrameSelectables = [];
    SortedList<int, T> selectables = new();
    int lastSelectedIndex = -1;

    public bool SelectedThisFrame => selectedThisFrame;
    public List<T> Selected => selected;
    public int Count => selected.Count;

    public event Action<T>? OnSelectObject;
    public event Action<T>? OnDeselectObject;

    private readonly Func<T, bool> CheckIsDestroyed;
    private readonly Func<T, T, bool> EqualsFunc;

    public SelectHandler(Func<T, bool> checkIsDestroyed, Func<T, T, bool> equals)
    {
        CheckIsDestroyed = checkIsDestroyed;
        EqualsFunc = equals;
    }

    public void StartFrame()
    {
        // Clear dead references
        for (int i = 0; i < selected.Count; i++)
        {
            if (CheckIsDestroyed.Invoke(selected[i]))
            {
                selected.RemoveAt(i);
                i--;
            }
        }

        selectedThisFrame = false;
        previousFrameSelectables = selectables;
        selectables = new();

        if (lastSelectedIndex == -1 && selected.Count > 0)
        {
            SetSelectedIndex(Selected[0]);
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
        for (int i = 0; i < objs.Length; i++)
        {
            selected.Add(objs[i]);
            OnSelectObject?.Invoke(objs[i]);
            GlobalSelectHandler.Select(objs[i]);
        }
        if (objs.Length > 0)
            SetSelectedIndex(objs[0]);
    }

    public bool IsSelected(T obj)
    {
        for (int i = 0; i < selected.Count; i++)
        {
            if (EqualsFunc.Invoke(selected[i], obj))
                return true;
        }
        return false;
    }

    public void Foreach(Action<T> value)
    {
        var copy = new List<T>(selected);
        copy.ForEach((weak) =>
        {
            value.Invoke((T)weak);
        });
    }

    public void AddSelectableAtIndex(int index, T obj) => selectables.Add(index, obj);

    public void Select(int index, T obj)
    {
        int prevLastIndex = lastSelectedIndex;
        Select(obj);
        if (prevLastIndex != index && index != -1 && lastSelectedIndex != -1)
        {
            if (prevLastIndex != -1 && Input.GetKey(Key.LeftShift))
            {
                // Bulk Select
                for (int i = Math.Min(prevLastIndex, index); i <= Math.Max(prevLastIndex, index); i++)
                {
                    if (previousFrameSelectables.TryGetValue(i, out var o))
                    {
                        if (!CheckIsDestroyed.Invoke(o))
                        {
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

    public void SelectIfNot(T obj, bool additively = false)
    {
        if (!IsSelected(obj))
            Select(obj, additively);
    }

    public void Select(T obj, bool additively = false)
    {
        selectedThisFrame = true;
        if (additively || Input.GetKey(Key.LeftControl))
        {
            // Additive
            if (IsSelected(obj))
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    if (EqualsFunc.Invoke(selected[i], obj))
                    {
                        selected.RemoveAt(i);
                        break;
                    }
                }
                OnDeselectObject?.Invoke(obj);
                GlobalSelectHandler.Deselect(obj);
            }
            else
            {
                selected.Add(obj);
                OnSelectObject?.Invoke(obj);
                GlobalSelectHandler.Select(obj);
            }
        }
        else SetSelection(obj);

        SetSelectedIndex(obj);
    }

    private void SetSelectedIndex(T entity)
    {
        // if sorted has this value using reference equals, set lastSelectedIndex to the index of it
        for (int i = 0; i < previousFrameSelectables.Count; i++)
        {
            if (EqualsFunc.Invoke(previousFrameSelectables.Values[i], entity))
            {
                lastSelectedIndex = previousFrameSelectables.Keys[i];
                break;
            }
        }
    }

    public void Clear()
    {
        foreach (var item in selected)
        {
            OnDeselectObject?.Invoke(item);
            GlobalSelectHandler.Deselect(item);
        }
        selected.Clear();
        lastSelectedIndex = -1;
    }
}
