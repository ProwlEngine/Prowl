using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Silk.NET.SDL;

namespace NeoVeldrid.Sdl2
{
    internal static class Sdl2WindowRegistry
    {
        public static readonly object Lock = new object();
        private static readonly Dictionary<uint, Sdl2Window> _eventsByWindowID
            = new Dictionary<uint, Sdl2Window>();
        private static bool _firstInit;

        public static void RegisterWindow(Sdl2Window window)
        {
            lock (Lock)
            {
                _eventsByWindowID.Add(window.WindowID, window);
                if (!_firstInit)
                {
                    _firstInit = true;
                    Sdl2Events.Subscribe(ProcessWindowEvent);
                }
            }
        }

        public static void RemoveWindow(Sdl2Window window)
        {
            lock (Lock)
            {
                _eventsByWindowID.Remove(window.WindowID);
            }
        }

        private static unsafe void ProcessWindowEvent(ref Event ev)
        {
            bool handled = false;
            uint windowID = 0;
            switch ((EventType)ev.Type)
            {
                case EventType.Quit:
                case EventType.AppTerminating:
                case EventType.Windowevent:
                case EventType.Keydown:
                case EventType.Keyup:
                case EventType.Textediting:
                case EventType.Textinput:
                case EventType.Keymapchanged:
                case EventType.Mousemotion:
                case EventType.Mousebuttondown:
                case EventType.Mousebuttonup:
                case EventType.Mousewheel:
                    // All of these event types have windowID at the same offset
                    // through the Window member of the union.
                    windowID = ev.Window.WindowID;
                    handled = true;
                    break;
                case EventType.Dropbegin:
                case EventType.Dropcomplete:
                case EventType.Dropfile:
                case EventType.Droptext:
                    DropEvent dropEvent = Unsafe.As<Event, DropEvent>(ref ev);
                    windowID = dropEvent.WindowID;
                    handled = true;
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled && _eventsByWindowID.TryGetValue(windowID, out Sdl2Window window))
            {
                window.AddEvent(ev);
            }
        }
    }
}
