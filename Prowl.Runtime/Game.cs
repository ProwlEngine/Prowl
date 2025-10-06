// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Echo.Logging;

using Prowl.Echo;
using Prowl.Runtime.Audio;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Runtime;

public class EchoLogger : IEchoLogger
{
    public void Debug(string message) => Prowl.Runtime.Debug.Log(message);

    public void Error(string message, Exception? exception = null) => Prowl.Runtime.Debug.LogError(message);

    public void Info(string message) => Prowl.Runtime.Debug.Log(message);

    public void Warning(string message) => Prowl.Runtime.Debug.LogWarning(message);
}

public abstract class Game
{
    public static IAssetProvider AssetProvider { get; private set; }

    private TimeData time = new TimeData();

    public void Run(string title, int width, int height, IAssetProvider assetProvider)
    {
        AssetProvider = assetProvider ?? throw new Exception("AssetProvider cannot be null");

        Window.InitWindow(title, width, height, Silk.NET.Windowing.WindowState.Normal, false);

        Window.Load += () => {
            Graphics.Initialize();

            //_paperRenderer = new PaperRenderer();
            //_paperRenderer.Initialize(width, height);
            //Paper.Initialize(_paperRenderer, width, height);

            Initialize();

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        };

        Window.Update += (delta) =>
        {
            //UpdatePaperInput();

            time.Update();
            Time.TimeStack.Clear();
            Time.TimeStack.Push(time);

            Update();

            SceneManager.Update();

            Console.Title = $"{title} - {Window.InternalWindow.FramebufferSize.X}x{Window.InternalWindow.FramebufferSize.Y} - FPS: {1.0 / Time.deltaTime}";
        };

        Window.Render += (delta) => {
            Debug.ClearGizmos();

            Graphics.StartFrame();

            Render();

            SceneManager.Draw();

            PostRender();

            Graphics.EndFrame();


            Graphics.Device.UnbindFramebuffer();
            Graphics.Device.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
            //Paper.BeginFrame((float)delta);

            GUI();

            //SceneManager.OnGUI();
            //GameStateManager.UI();

            //Paper.EndFrame();

            PostGUI();
        };

        Window.Resize += (size) => {
            //Paper.SetResolution(size.X, size.Y);
            //_paperRenderer.UpdateProjection(size.X, size.Y);
            Resize(size.X, size.Y);
        };

        Window.Closing += () => {
            Closing();

            Graphics.Dispose();

            Debug.Log("Is terminating...");
        };

        Debug.LogSuccess("Initialization complete");
        Window.Start();

    }

    public virtual void Initialize() { }
    public virtual void Update() { }
    public virtual void PostUpdate() { }
    public virtual void Render() { }
    public virtual void PostRender() { }
    public virtual void GUI() { }
    public virtual void PostGUI() { }
    public virtual void Resize(int width, int height) { }
    public virtual void Closing() { }

    //private void UpdatePaperInput()
    //{
    //
    //    // Handle mouse position and movement
    //    Int2 mousePos = Input.MousePosition;
    //    Paper.SetPointerState(PaperMouseBtn.Unknown, mousePos.X, mousePos.Y, false, true);
    //
    //    // Handle mouse buttons
    //    if (Input.GetMouseButtonDown(0))
    //        Paper.SetPointerState(PaperMouseBtn.Left, mousePos.X, mousePos.Y, true, false);
    //    if (Input.GetMouseButtonUp(0))
    //        Paper.SetPointerState(PaperMouseBtn.Left, mousePos.X, mousePos.Y, false, false);
    //
    //    if (Input.GetMouseButtonDown(1))
    //        Paper.SetPointerState(PaperMouseBtn.Right, mousePos.X, mousePos.Y, true, false);
    //    if (Input.GetMouseButtonUp(1))
    //        Paper.SetPointerState(PaperMouseBtn.Right, mousePos.X, mousePos.Y, false, false);
    //
    //    if (Input.GetMouseButtonDown(2))
    //        Paper.SetPointerState(PaperMouseBtn.Middle, mousePos.X, mousePos.Y, true, false);
    //    if (Input.GetMouseButtonUp(2))
    //        Paper.SetPointerState(PaperMouseBtn.Middle, mousePos.X, mousePos.Y, false, false);
    //
    //    // Handle mouse wheel
    //    float wheelDelta = Input.MouseWheelDelta;
    //    if (wheelDelta != 0)
    //        Paper.SetPointerWheel(wheelDelta);
    //
    //    // Handle keyboard input
    //    char? c = Input.GetPressedChar();
    //    while (c != null)
    //    {
    //        Paper.AddInputCharacter((c.Value).ToString());
    //        c = Input.GetPressedChar();
    //    }
    //
    //    // Handle key states for keys
    //    // Fortunately Papers key enums have almost all the same names
    //    // So we only need to map a few keys manually, the rest we can use reflection
    //    foreach (Silk.NET.Input.Key k in Enum.GetValues(typeof(Silk.NET.Input.Key)))
    //        if (k != Silk.NET.Input.Key.Unknown)
    //            if (Enum.TryParse(k.ToString(), out PaperKey paperKey))
    //                HandleKey(k, paperKey);
    //
    //    // Handle the few keys that are not the same
    //    HandleKey(Silk.NET.Input.Key.Equal, PaperKey.Equals);
    //    HandleKey(Silk.NET.Input.Key.BackSlash, PaperKey.Backslash);
    //    HandleKey(Silk.NET.Input.Key.GraveAccent, PaperKey.Grave);
    //    HandleKey(Silk.NET.Input.Key.KeypadEqual, PaperKey.KeypadEquals);
    //
    //    HandleKey(Silk.NET.Input.Key.Number0, PaperKey.Num0);
    //    HandleKey(Silk.NET.Input.Key.Number1, PaperKey.Num1);
    //    HandleKey(Silk.NET.Input.Key.Number2, PaperKey.Num2);
    //    HandleKey(Silk.NET.Input.Key.Number3, PaperKey.Num3);
    //    HandleKey(Silk.NET.Input.Key.Number4, PaperKey.Num4);
    //    HandleKey(Silk.NET.Input.Key.Number5, PaperKey.Num5);
    //    HandleKey(Silk.NET.Input.Key.Number6, PaperKey.Num6);
    //    HandleKey(Silk.NET.Input.Key.Number7, PaperKey.Num7);
    //    HandleKey(Silk.NET.Input.Key.Number8, PaperKey.Num8);
    //    HandleKey(Silk.NET.Input.Key.Number9, PaperKey.Num9);
    //
    //    HandleKey(Silk.NET.Input.Key.KeypadSubtract, PaperKey.KeypadMinus);
    //    HandleKey(Silk.NET.Input.Key.KeypadAdd, PaperKey.KeypadPlus);
    //
    //    HandleKey(Silk.NET.Input.Key.LeftBracket, PaperKey.LeftBracket);
    //    HandleKey(Silk.NET.Input.Key.RightBracket, PaperKey.RightBracket);
    //    HandleKey(Silk.NET.Input.Key.ShiftLeft, PaperKey.LeftShift);
    //    HandleKey(Silk.NET.Input.Key.ShiftRight, PaperKey.RightShift);
    //    HandleKey(Silk.NET.Input.Key.AltLeft, PaperKey.LeftAlt);
    //    HandleKey(Silk.NET.Input.Key.AltRight, PaperKey.RightAlt);
    //    HandleKey(Silk.NET.Input.Key.ControlLeft, PaperKey.LeftControl);
    //    HandleKey(Silk.NET.Input.Key.ControlRight, PaperKey.RightControl);
    //    HandleKey(Silk.NET.Input.Key.SuperLeft, PaperKey.LeftSuper);
    //    HandleKey(Silk.NET.Input.Key.SuperRight, PaperKey.RightSuper);
    //
    //    // These keys don't have direct equivalents in Silk.NET.Input.Key
    //    // HandleKey(someKey, PaperKey.AudioNext);
    //    // HandleKey(someKey, PaperKey.AudioPrevious);
    //    // HandleKey(someKey, PaperKey.AudioStop);
    //    // HandleKey(someKey, PaperKey.AudioPlay);
    //    // HandleKey(someKey, PaperKey.AudioMute);
    //    // HandleKey(someKey, PaperKey.Application);
    //    // HandleKey(someKey, PaperKey.Select);
    //    // HandleKey(someKey, PaperKey.Help);
    //}
    //
    //static void HandleKey(Silk.NET.Input.Key silkKey, PaperKey paperKey)
    //{
    //    if (Input.GetKeyDown(silkKey))
    //        Paper.SetKeyState(paperKey, true);
    //    else if (Input.GetKeyUp(silkKey))
    //        Paper.SetKeyState(paperKey, false);
    //}

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Log the exception, display it, etc
        Console.WriteLine((e.ExceptionObject as Exception).Message);
    }

    public static void Quit()
    {
        Window.Stop();
        Debug.Log("Is terminating...");
    }
}
