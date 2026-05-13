using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor;

public static class MainMenuBar
{
    private const float DropdownWidth = 200f;
    private const float ItemHeight = 24f;

    private static int _openMenuIndex = -1;
    private static float _xPos = -1;
    private static string? s_cachedVersion;

    public static void Draw(Paper paper)
    {
        var font = EditorTheme.DefaultFont;
        var items = MenuRegistry.RootMenus;

        using (paper.Row("menubar")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Size(paper.Percent(100), EditorTheme.MenuBarHeight)
            .BackgroundColor(EditorTheme.Neutral200)
            .RowBetween(6)
            .Enter())
        {
            paper.Box("mb_pad_l").Width(4);

            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var item = items[i];

                using (paper.Box($"menu_{index}")
                    .Height(EditorTheme.MenuBarHeight)
                    .Width(UnitValue.Auto)
                    .BackgroundColor(_openMenuIndex == index ? EditorTheme.Ink200 : Color.Transparent)
                    .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(item.Label, font)
                        .TextColor(EditorTheme.Ink500)
                        .Alignment(TextAlignment.MiddleCenter)
                        .FontSize(EditorTheme.FontSize)
                    .OnClick(index, (idx, e) =>
                    {
                        _openMenuIndex = _openMenuIndex == idx ? -1 : idx;
                        _xPos = e.ElementRect.Min.X;
                    })
                    .Enter())
                {
                    if (_openMenuIndex >= 0 && _openMenuIndex != index && paper.IsParentHovered)
                        _openMenuIndex = index;
                }
            }

            paper.Box("mb_stretch").Width(UnitValue.Stretch(1));

            DrawAuthWidget(paper, font);

            paper.Box("mb_ver_sep").Width(1).Height(EditorTheme.MenuBarHeight - 10f).BackgroundColor(EditorTheme.Ink200);

            paper.Box("mb_version")
                .Height(EditorTheme.MenuBarHeight)
                .Width(UnitValue.Auto)
                .IsNotInteractable()
                .Text(GetVersion(), font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleCenter);

            paper.Box("mb_pad_r").Width(10);
        }

        if (_openMenuIndex >= 0 && _openMenuIndex < items.Count)
        {
            var openItem = items[_openMenuIndex];
            if (openItem.HasSubItems)
            {
                paper.Box("menubar_backdrop")
                    .PositionType(PositionType.SelfDirected)
                    .Position(0, 0)
                    .Size(99999, 99999)
                    .BackgroundColor(Color.FromArgb(85, 0, 0, 0))
                    .Layer(Layer.Topmost)
                    .OnClick(0, (_, _) => _openMenuIndex = -1);

                RenderDropdown(paper, $"dd_{_openMenuIndex}", openItem.SubItems, _xPos, EditorTheme.MenuBarHeight - 2);
            }
        }
    }

    private static void DrawAuthWidget(Paper paper, FontFile? font)
    {
        if (font == null) return;

        if (ProwlService.IsSignedIn)
        {
            string email = ProwlService.GetCurrentUser()?.Email ?? "Signed in";
            int atIdx = email.IndexOf('@');
            string display = atIdx > 0 ? email[..atIdx] : email;

            paper.Box("mb_user")
                .Height(EditorTheme.MenuBarHeight)
                .Width(UnitValue.Auto)
                .IsNotInteractable()
                .Text($"{EditorIcons.User}  {display}", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter)
                .Margin(0, 0, 4, 0);

            paper.Box("mb_signout")
                .Height(EditorTheme.MenuBarHeight)
                .Width(UnitValue.Auto)
                .BackgroundColor(Color.Transparent)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text("Sign Out", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter)
                .Margin(0, 0, 4, 0)
                .OnClick(0, (_, _) => { _ = ProwlService.SignOutAsync(); });
        }
        else
        {
            string label = ProwlService.IsSigningIn
                ? "Signing in..."
                : $"{EditorIcons.ArrowRightToBracket}  Sign In";

            paper.Box("mb_signin")
                .Height(EditorTheme.MenuBarHeight)
                .Width(UnitValue.Auto)
                .BackgroundColor(Color.Transparent)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text(label, font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter)
                .Margin(0, 0, 4, 0)
                .OnClick(0, (_, _) =>
                {
                    if (!ProwlService.IsSigningIn)
                        _ = ProwlService.SignInWithGitHubAsync();
                });
        }
    }

    private static string GetVersion()
    {
        if (s_cachedVersion != null) return s_cachedVersion;
        string v = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.1";
        int plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        s_cachedVersion = $"v{v}";
        return s_cachedVersion;
    }

    private static void RenderDropdown(Paper paper, string id, List<MenuItem> items, float x, float y)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Column(id)
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(DropdownWidth)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200)
            .BorderWidth(1)
            .Rounded(4)
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .Enter())
        {
            paper.Box($"{id}_pad_t").Height(3);

            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var item = items[i];

                if (item.IsSeparator)
                {
                    paper.Box($"{id}_sep_{index}")
                        .Height(1)
                        .Margin(8, 3, 8, 3)
                        .BackgroundColor(EditorTheme.Ink200);
                    continue;
                }

                bool itemEnabled = item.IsEnabled;
                Color textColor = itemEnabled ? EditorTheme.Ink500 : EditorTheme.Ink300;

                using (paper.Row($"{id}_i_{index}")
                    .Height(ItemHeight)
                    .Margin(3, 0, 3, 0)
                    .BackgroundColor(Color.Transparent)
                    .Rounded(3)
                    .Hovered.BackgroundColor(itemEnabled ? EditorTheme.Purple400 : Color.Transparent).End()
                    .OnClick(item, (captured, e) =>
                    {
                        if (captured.IsEnabled && captured.OnClick != null)
                        {
                            captured.OnClick();
                            _openMenuIndex = -1;
                        }
                    })
                    .Enter())
                {
                    // Left indent
                    paper.Box($"{id}_il_{index}").Width(8);

                    // Checkmark — always reserves space so labels stay aligned
                    paper.Box($"{id}_chk_{index}")
                        .Width(14).Height(ItemHeight)
                        .Alignment(TextAlignment.MiddleCenter)
                        .IsNotInteractable()
                        .Text(item.IsChecked ? EditorIcons.Check : "", font)
                        .TextColor(textColor)
                        .FontSize(EditorTheme.FontSize - 3);

                    paper.Box($"{id}_gap_{index}").Width(4);

                    // Label
                    paper.Box($"{id}_lbl_{index}")
                        .Height(ItemHeight)
                        .Width(UnitValue.Stretch())
                        .Alignment(TextAlignment.MiddleLeft)
                        .IsNotInteractable()
                        .Text(item.DisplayLabel, font)
                        .TextColor(textColor)
                        .FontSize(EditorTheme.FontSize);

                    // Submenu arrow
                    if (item.HasSubItems)
                    {
                        paper.Box($"{id}_arr_{index}")
                            .Width(16).Height(ItemHeight)
                            .Alignment(TextAlignment.MiddleCenter)
                            .IsNotInteractable()
                            .Text(EditorIcons.AngleRight, font)
                            .TextColor(textColor)
                            .FontSize(EditorTheme.FontSize - 2);

                        if (paper.IsParentHovered)
                            RenderDropdown(paper, $"{id}_s_{index}", item.SubItems, DropdownWidth - 5, 0);
                    }
                    else
                    {
                        paper.Box($"{id}_arr_pad_{index}").Width(16);
                    }

                    paper.Box($"{id}_ir_{index}").Width(4);
                }
            }

            paper.Box($"{id}_pad_b").Height(3);
        }
    }
}
