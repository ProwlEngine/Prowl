// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.Events {

public interface IEventManagerHolder<T> where T : struct, Enum
{
    EventManager<T> EventManager { get; }
}

public static class EventManagerExtensions
{
    public static void InvokeEvents<T, TArgs>(this IEventManagerHolder<T>[] holders, T eventType, TArgs args) where T : struct, Enum
    {
        for (int i = 0; i < holders.Length; i++)
        {
            IEventManagerHolder<T>? holder = holders[i];
            holder?.EventManager.InvokeEvent(eventType, args);
        }
    }
    public static void InvokeEvents<T, TArgs>(this List<IEventManagerHolder<T>> holders, T eventType, TArgs args) where T : struct, Enum
    {
        for (int i = 0; i < holders.Count; i++)
        {
            IEventManagerHolder<T> holder = holders[i];
            holder?.EventManager.InvokeEvent(eventType, args);
        }
    }

    public static void InvokeEvents<T>(this IEventManagerHolder<T>[] holders, T eventType) where T : struct, Enum
    {
        for (int i = 0; i < holders.Length; i++)
        {
            IEventManagerHolder<T>? holder = holders[i];
            holder?.EventManager.InvokeEvent(eventType);
        }
    }
    public static void InvokeEvents<T>(this List<IEventManagerHolder<T>> holders, T eventType) where T : struct, Enum
    {
        for (int i = 0; i < holders.Count; i++)
        {
            IEventManagerHolder<T> holder = holders[i];
            holder?.EventManager.InvokeEvent(eventType);
        }
    }
}

}
