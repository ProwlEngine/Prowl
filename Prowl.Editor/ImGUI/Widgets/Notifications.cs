using HexaEngine.ImGuiNET;
using Prowl.Icons;
using Prowl.Runtime;
using System.Numerics;

// Ported and modified from: https://github.com/patrickcjk/imgui-notify - MIT License

namespace Prowl.Editor
{
    public enum ImGuiToastType
    {
        None,
        Success,
        Warning,
        Error,
        Info,
        COUNT
    }

    public enum ImGuiToastPhase
    {
        FadeIn,
        Wait,
        FadeOut,
        Expired,
        COUNT
    }

    public enum ImGuiToastPos
    {
        TopLeft,
        TopCenter,
        TopRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
        Center,
        COUNT
    }

    public class ImGuiToast
    {
        public ImGuiToastType Type { get; set; } = ImGuiToastType.None;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public Color Color { get; set; } = Color.white;
        public float DismissTime { get; set; } = 5f;
        public DateTime CreationTime { get; } = DateTime.Now;

        private float NOTIFY_FADE_IN_OUT_TIME => 0.25f;
        private const float NOTIFY_OPACITY = 1.0f;

        public ImGuiToastPhase GetPhase()
        {
            var elapsed = DateTime.Now - CreationTime;

            if (elapsed.TotalSeconds > NOTIFY_FADE_IN_OUT_TIME + DismissTime + NOTIFY_FADE_IN_OUT_TIME)
            {
                return ImGuiToastPhase.Expired;
            }
            else if (elapsed.TotalSeconds > NOTIFY_FADE_IN_OUT_TIME + DismissTime)
            {
                return ImGuiToastPhase.FadeOut;
            }
            else if (elapsed.TotalSeconds > NOTIFY_FADE_IN_OUT_TIME)
            {
                return ImGuiToastPhase.Wait;
            }
            else
            {
                return ImGuiToastPhase.FadeIn;
            }
        }

        public float GetFadePercent()
        {
            var phase = GetPhase();
            var elapsed = DateTime.Now - CreationTime;

            if (phase == ImGuiToastPhase.FadeIn)
            {
                return ((float)elapsed.TotalSeconds / NOTIFY_FADE_IN_OUT_TIME) * NOTIFY_OPACITY;
            }
            else if (phase == ImGuiToastPhase.FadeOut)
            {
                return (float)(1f - (((float)elapsed.TotalSeconds - NOTIFY_FADE_IN_OUT_TIME - DismissTime) / NOTIFY_FADE_IN_OUT_TIME)) * NOTIFY_OPACITY;
            }

            return 1f * NOTIFY_OPACITY;
        }
    }

    public static class ImGuiNotify
    {
        private const int NOTIFY_MAX_MSG_LENGTH = 4096;
        private const float NOTIFY_PADDING_X = 20f;
        private const float NOTIFY_PADDING_Y = 20f;
        private const float NOTIFY_PADDING_MESSAGE_Y = 10f;

        private static List<ImGuiToast> notifications = new List<ImGuiToast>();

        public static void InsertNotification(ImGuiToast toast)
        {
            notifications.Add(toast);
        }

        public static void InsertNotification(string Title, Color color, string Content="")
        {
            Console.WriteLine($"Inserting notification: {Title} - {Content}");
            InsertNotification(new ImGuiToast()
            {
                Title = Title,
                Content = Content,
                Color = color,
            });
        }

        public static void RemoveNotification(int index)
        {
            notifications.RemoveAt(index);
        }

        /// <summary>
        /// Render toasts, call at the end of your rendering!
        /// </summary>
        public static void RenderNotifications()
        {
            var vp_size = ImGui.GetMainViewport().Size;

            float height = 0f;

            for (int i = 0; i < notifications.Count; i++)
            {
                var currentToast = notifications[i];

                // Remove toast if expired
                if (currentToast.GetPhase() == ImGuiToastPhase.Expired)
                {
                    RemoveNotification(i);
                    continue;
                }

                // Get icon, title and other data
                var icon = FontAwesome6.Exclamation;
                var title = currentToast.Title;
                var content = currentToast.Content;
                var defaultTitle = "Notification";
                var opacity = currentToast.GetFadePercent(); // Get opacity based on the current phase

                // Window rendering
                var textColor = currentToast.Color;
                textColor.a = opacity;

                // Generate new unique name for this toast
                var windowName = $"##TOAST{i}";

                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.SetNextWindowBgAlpha(opacity);
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(vp_size.X - NOTIFY_PADDING_X, vp_size.Y - NOTIFY_PADDING_Y - height), ImGuiCond.Always, new System.Numerics.Vector2(1.0f, 1.0f));
                ImGui.Begin(windowName, 
                    ImGuiWindowFlags.AlwaysAutoResize | 
                    ImGuiWindowFlags.NoDecoration | 
                    ImGuiWindowFlags.NoInputs | 
                    ImGuiWindowFlags.NoNav | 
                    //ImGuiWindowFlags.NoBringToFrontOnFocus | 
                    ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.Tooltip
                    );

                // Here we render the toast content
                {
                    ImGui.PushTextWrapPos(vp_size.X / 3f); // We want to support multi-line text, this will wrap the text after 1/3 of the screen width

                    bool wasTitleRendered = false;

                    // If an icon is set
                    if (!string.IsNullOrEmpty(icon))
                    {
                        //Text(icon); // Render icon text
                        ImGui.TextColored(textColor, icon);
                        wasTitleRendered = true;
                    }

                    // If a title is set
                    if (!string.IsNullOrEmpty(title))
                    {
                        // If a title and an icon is set, we want to render on the same line
                        if (!string.IsNullOrEmpty(icon))
                            ImGui.SameLine();

                        ImGui.Text(title); // Render title text
                        wasTitleRendered = true;
                    }
                    else if (!string.IsNullOrEmpty(defaultTitle))
                    {
                        if (!string.IsNullOrEmpty(icon))
                            ImGui.SameLine();

                        ImGui.Text(defaultTitle); // Render default title text (ImGuiToastType_Success -> "Success", etc...)
                        wasTitleRendered = true;
                    }

                    // In case ANYTHING was rendered at the top, we want to add a small padding so the text (or icon) looks centered vertically
                    if (wasTitleRendered && !string.IsNullOrEmpty(content))
                    {
                        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5f); // Must be a better way to do this!!!!
                    }

                    // If content is set
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (wasTitleRendered)
                            ImGui.Separator();
                        ImGui.Text(content); // Render content text
                    }

                    ImGui.PopTextWrapPos();
                }

                // Save height for next toasts
                height += ImGui.GetWindowHeight() + NOTIFY_PADDING_MESSAGE_Y;

                ImGui.PopStyleColor();
                // End
                ImGui.End();
            }
        }

    }
}