using Raylib_cs;

namespace MobaGame;

public class TextInput
{
    public string Text = "";
    public bool Focused;
    public int MaxLength;
    public bool IsPassword;
    private float _cursorBlink;

    public TextInput(int maxLength = 32, bool isPassword = false)
    {
        MaxLength = maxLength;
        IsPassword = isPassword;
    }

    public void Update(float dt)
    {
        if (!Focused) return;
        _cursorBlink += dt;

        // Typed characters
        int key = Raylib.GetCharPressed();
        while (key > 0)
        {
            if (key >= 32 && key <= 126 && Text.Length < MaxLength)
                Text += (char)key;
            key = Raylib.GetCharPressed();
        }

        // Backspace
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace))
        {
            if (Text.Length > 0)
                Text = Text[..^1];
        }
    }

    public void Draw(int x, int y, int w, int h, string placeholder = "")
    {
        var borderColor = Focused ? Color.SkyBlue : Color.Gray;
        Raylib.DrawRectangle(x, y, w, h, Color.DarkGray);
        Raylib.DrawRectangleLines(x, y, w, h, borderColor);

        var displayText = Text.Length > 0
            ? (IsPassword ? new string('*', Text.Length) : Text)
            : placeholder;
        var textColor = Text.Length > 0 ? Color.White : Color.Gray;

        Raylib.DrawText(displayText, x + 5, y + (h - 16) / 2, 16, textColor);

        if (Focused && ((int)(_cursorBlink * 2) % 2 == 0))
        {
            int cursorX = x + 5 + Raylib.MeasureText(
                IsPassword ? new string('*', Text.Length) : Text, 16);
            Raylib.DrawLine(cursorX, y + 4, cursorX, y + h - 4, Color.White);
        }
    }

    public bool ClickCheck(int x, int y, int w, int h)
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var mx = Raylib.GetMouseX();
            var my = Raylib.GetMouseY();
            Focused = mx >= x && mx <= x + w && my >= y && my <= y + h;
            if (Focused) _cursorBlink = 0;
            return Focused;
        }
        return false;
    }
}

public static class UI
{
    public static bool Button(int x, int y, int w, int h, string text, Color? color = null)
    {
        var bgColor = color ?? Color.DarkBlue;
        var mx = Raylib.GetMouseX();
        var my = Raylib.GetMouseY();
        bool hover = mx >= x && mx <= x + w && my >= y && my <= y + h;

        if (hover)
            bgColor = new Color(
                (byte)Math.Min(255, bgColor.R + 40),
                (byte)Math.Min(255, bgColor.G + 40),
                (byte)Math.Min(255, bgColor.B + 40),
                bgColor.A);

        Raylib.DrawRectangle(x, y, w, h, bgColor);
        Raylib.DrawRectangleLines(x, y, w, h, Color.White);

        int tw = Raylib.MeasureText(text, 16);
        Raylib.DrawText(text, x + (w - tw) / 2, y + (h - 16) / 2, 16, Color.White);

        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    public static void Label(int x, int y, string text, int size = 16, Color? color = null)
    {
        Raylib.DrawText(text, x, y, size, color ?? Color.White);
    }

    public static void Panel(int x, int y, int w, int h, Color? color = null)
    {
        Raylib.DrawRectangle(x, y, w, h, color ?? new Color((byte)20, (byte)20, (byte)30, (byte)220));
        Raylib.DrawRectangleLines(x, y, w, h, Color.Gray);
    }

    public static void ProgressBar(int x, int y, int w, int h, float ratio, Color fill, Color bg)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        Raylib.DrawRectangle(x, y, w, h, bg);
        Raylib.DrawRectangle(x, y, (int)(w * ratio), h, fill);
        Raylib.DrawRectangleLines(x, y, w, h, Color.White);
    }
}
