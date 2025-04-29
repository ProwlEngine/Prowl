// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using System.Numerics;

using Prowl.PaperUI;
using Raylib_cs;
using Shared;
using static Raylib_cs.Raylib;

namespace RaylibSample;

internal class Program
{
    static RaylibCanvasRenderer _renderer;

    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        int width = 1080;
        int height = 850;

        // Initialize the window
        SetConfigFlags(ConfigFlags.ResizableWindow);
        InitWindow(width, height, "Raylib Sample");
        SetTargetFPS(60);

        _renderer = new RaylibCanvasRenderer();
        Paper.Initialize(_renderer, width, height);
        Paper.SetClipboardHandler(new RaylibClipboardHandler());

        // Initialize the Demo, this loads the Demo fonts and other resources
        PaperDemo.Initialize();

        // Main game loop
        while (!WindowShouldClose())
        {
            if (width != GetScreenWidth() || height != GetScreenHeight())
            {
                width = GetScreenWidth();
                height = GetScreenHeight();
                Paper.SetResolution(width, height);
            }

            UpdateInput();

            BeginDrawing();
            ClearBackground(Color.RayWhite);

            Paper.BeginFrame(GetFrameTime());

            PaperDemo.RenderUI();

            Paper.EndFrame();

            EndDrawing();
        }

        _renderer.Dispose();

        // Close the window
        CloseWindow();
    }

    static void UpdateInput()
    {
        // Handle mouse position and movement
        Vector2 mousePos = GetMousePosition();
        Paper.SetPointerState(PaperMouseBtn.Unknown, (int)mousePos.X, (int)mousePos.Y, false, true);

        // Handle mouse buttons
        if (IsMouseButtonPressed(MouseButton.Left))
            Paper.SetPointerState(PaperMouseBtn.Left, (int)mousePos.X, (int)mousePos.Y, true, false);
        if (IsMouseButtonReleased(MouseButton.Left))
            Paper.SetPointerState(PaperMouseBtn.Left, (int)mousePos.X, (int)mousePos.Y, false, false);

        if (IsMouseButtonPressed(MouseButton.Right))
            Paper.SetPointerState(PaperMouseBtn.Right, (int)mousePos.X, (int)mousePos.Y, true, false);
        if (IsMouseButtonReleased(MouseButton.Right))
            Paper.SetPointerState(PaperMouseBtn.Right, (int)mousePos.X, (int)mousePos.Y, false, false);

        if (IsMouseButtonPressed(MouseButton.Middle))
            Paper.SetPointerState(PaperMouseBtn.Middle, (int)mousePos.X, (int)mousePos.Y, true, false);
        if (IsMouseButtonReleased(MouseButton.Middle))
            Paper.SetPointerState(PaperMouseBtn.Middle, (int)mousePos.X, (int)mousePos.Y, false, false);

        // Handle mouse wheel
        float wheelDelta = GetMouseWheelMove();
        if (wheelDelta != 0)
            Paper.SetPointerWheel(wheelDelta);

        // Handle keyboard input
        int key = GetCharPressed();
        while (key > 0)
        {
            Paper.AddInputCharacter(((char)key).ToString());
            key = GetCharPressed();
        }

        // Handle key states for keys
        // Fortunately Papers key enums have almost all the same names
        // So we only need to map a few keys manually, the rest we can use reflection
        foreach (KeyboardKey k in Enum.GetValues(typeof(KeyboardKey)))
            if (Enum.TryParse(k.ToString(), out PaperKey paperKey))
                HandleKey(k, paperKey);

        // Handle the few keys that are not the same
        HandleKey(KeyboardKey.Zero, PaperKey.Num0);
        HandleKey(KeyboardKey.One, PaperKey.Num1);
        HandleKey(KeyboardKey.Two, PaperKey.Num2);
        HandleKey(KeyboardKey.Three, PaperKey.Num3);
        HandleKey(KeyboardKey.Four, PaperKey.Num4);
        HandleKey(KeyboardKey.Five, PaperKey.Num5);
        HandleKey(KeyboardKey.Six, PaperKey.Num6);
        HandleKey(KeyboardKey.Seven, PaperKey.Num7);
        HandleKey(KeyboardKey.Eight, PaperKey.Num8);
        HandleKey(KeyboardKey.Nine, PaperKey.Num9);

        HandleKey(KeyboardKey.Kp0, PaperKey.Keypad0);
        HandleKey(KeyboardKey.Kp1, PaperKey.Keypad1);
        HandleKey(KeyboardKey.Kp2, PaperKey.Keypad2);
        HandleKey(KeyboardKey.Kp3, PaperKey.Keypad3);
        HandleKey(KeyboardKey.Kp4, PaperKey.Keypad4);
        HandleKey(KeyboardKey.Kp5, PaperKey.Keypad5);
        HandleKey(KeyboardKey.Kp6, PaperKey.Keypad6);
        HandleKey(KeyboardKey.Kp7, PaperKey.Keypad7);
        HandleKey(KeyboardKey.Kp8, PaperKey.Keypad8);
        HandleKey(KeyboardKey.Kp9, PaperKey.Keypad9);
        HandleKey(KeyboardKey.KpDecimal, PaperKey.KeypadDecimal);
        HandleKey(KeyboardKey.KpDivide, PaperKey.KeypadDivide);
        HandleKey(KeyboardKey.KpMultiply, PaperKey.KeypadMultiply);
        HandleKey(KeyboardKey.KpSubtract, PaperKey.KeypadMinus);
        HandleKey(KeyboardKey.KpAdd, PaperKey.KeypadPlus);
        HandleKey(KeyboardKey.KpEnter, PaperKey.KeypadEnter);
        HandleKey(KeyboardKey.KpEqual, PaperKey.KeypadEquals);
    }

    class RaylibClipboardHandler : IClipboardHandler
    {
        public string GetClipboardText() => Raylib.GetClipboardText_();
        public void SetClipboardText(string text) => Raylib.SetClipboardText(text);
    }

    static void HandleKey(KeyboardKey rayKey, PaperKey paperKey)
    {
        if (IsKeyPressed(rayKey))
            Paper.SetKeyState(paperKey, true);
        else if (IsKeyReleased(rayKey))
            Paper.SetKeyState(paperKey, false);
    }
}
