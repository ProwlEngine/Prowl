# Modern.WindowKit

Modern.WindowKit is a .NET abstraction over the windowing systems of Windows/Mac/Linux.

It allows you to create native windows, draw on them, and handle input.

This is a nearly direct port from [Avalonia](https://github.com/AvaloniaUI/Avalonia), with patches to make it independent from the rest of Avalonia.

# Sample Application

There is a sample application in `samples/Demo`.

This sample shows how to perform various operations such as:

- Create a window
- Handle painting
- Handle mouse input (Click and drag to paint)
- Handle touch input (Touch to paint, Windows/Linux only)
- Handle keyboard input (F1 to toggle diagnostic information)
- Show native file open dialog (F9)
- Show native folder open dialog (F10)
- Show native file save dialog (F12)

![demo-screenshot](https://user-images.githubusercontent.com/179295/147585748-0a37d7f9-973d-407f-9120-95df44864c04.png)
