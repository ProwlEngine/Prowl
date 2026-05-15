using System;
using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor;

/// <summary>
/// Auth dropdown panel attached to the profile button in the menu bar.
/// Call <see cref="Draw"/> inside the trigger element's Enter() scope so
/// HookToParent positions the panel relative to the button.
/// </summary>
internal static class ProfilePopup
{
    private const float ProfileWidth  = 200f;
    private const float FormWidth     = 250f;
    private const float ItemHeight    = 24f;

    private static string _handleInput = "";
    private static string _nameInput   = "";
    private static bool   _isCreating;
    private static string? _createError;
    private static string? _handleWarning;

    private enum AvailStatus { None, Checking, Available, Taken }
    private static AvailStatus _availStatus = AvailStatus.None;
    private static string _lastChecked = "";

    public static bool IsOpen { get; private set; }

    public static void Toggle() { if (IsOpen) Close(); else IsOpen = true; }

    public static void Close()
    {
        IsOpen = false;
        _createError = null;
    }

    public static void ResetForm()
    {
        _handleInput = "";
        _nameInput   = "";
        _availStatus = AvailStatus.None;
        _lastChecked = "";
        _createError = null;
        _handleWarning = null;
        _isCreating  = false;
        IsOpen = false;
    }

    /// <summary>
    /// Render the backdrop and dropdown panel. Must be called inside the trigger
    /// button element's <c>using (Enter())</c> scope.
    /// </summary>
    public static void Draw(Paper paper, FontFile font, float triggerHeight)
    {
        if (!IsOpen) return;

        bool hasProfile  = ProwlService.IsProfileLoaded && ProwlService.CachedProfile != null;
        bool needsProfile = ProwlService.IsProfileLoaded && ProwlService.CachedProfile == null;
        float popupWidth = needsProfile ? FormWidth : ProfileWidth;

        // Full-screen backdrop on Overlay (below the Topmost popup) — click-outside to close.
        // StopEventPropagation is required: without it the click bubbles up to the trigger
        // button's OnClick → Toggle() → immediately re-opens the popup.
        paper.Box("mb_pp_bd")
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999)
            .Size(99999, 99999)
            .Layer(Layer.Overlay)
            .OnClick(_ => Close())
            .StopEventPropagation();

        // Popup panel anchored below the trigger via HookToParent.
        using (paper.Column("mb_pp_panel")
            .PositionType(PositionType.SelfDirected)
            .Position(0, triggerHeight)
            .Width(popupWidth)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1)
            .Rounded(4)
            .HookToParent()
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            if (!ProwlService.IsProfileLoaded)
                DrawLoading(paper, font);
            else if (hasProfile)
                DrawProfile(paper, font, ProwlService.CachedProfile!);
            else
                DrawCreateForm(paper, font, popupWidth);
        }
    }

    // ── Loading ──────────────────────────────────────────────────────────

    private static void DrawLoading(Paper paper, FontFile font)
    {
        paper.Box("mb_pp_loading")
            .Height(ItemHeight).Width(UnitValue.Stretch())
            .Margin(0, 0, 6, 6)
            .IsNotInteractable()
            .Text("Loading...", font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);
    }

    // ── Signed-in profile view ───────────────────────────────────────────

    private static void DrawProfile(Paper paper, FontFile font, UserProfile profile)
    {
        paper.Box("mb_pp_name_lbl")
            .Height(20).Width(UnitValue.Stretch())
            .Margin(12, 6, 8, 0)
            .IsNotInteractable()
            .Text(profile.Name, font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleLeft);

        paper.Box("mb_pp_handle_lbl")
            .Height(16).Width(UnitValue.Stretch())
            .Margin(12, 6, 0, 6)
            .IsNotInteractable()
            .Text($"@{profile.Handle}", font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSize - 2)
            .Alignment(TextAlignment.MiddleLeft);

        paper.Box("mb_pp_sep")
            .Height(1).Margin(4, 4, 0, 0)
            .BackgroundColor(EditorTheme.Ink200)
            .IsNotInteractable();

        using (paper.Row("mb_pp_signout")
            .Height(ItemHeight)
            .Margin(2, 2, 2, 2)
            .BackgroundColor(Color.Transparent)
            .Rounded(3)
            .Hovered.BackgroundColor(EditorTheme.Purple400).End()
            .OnClick(e => { Close(); ProwlService.SignOutAsync().ContinueWith(_ => { }); })
            .Enter())
        {
            paper.Box("mb_pp_so_icon")
                .Width(32).Height(ItemHeight)
                .IsNotInteractable()
                .Text(EditorIcons.ArrowRightFromBracket, font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 1)
                .Alignment(TextAlignment.MiddleCenter);

            paper.Box("mb_pp_so_lbl")
                .Height(ItemHeight).Width(UnitValue.Stretch())
                .IsNotInteractable()
                .Text("Sign Out", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleLeft);
        }
    }

    // ── Create-profile form ──────────────────────────────────────────────

    private static void DrawCreateForm(Paper paper, FontFile font, float popupWidth)
    {
        // Title
        paper.Box("mb_pp_title")
            .Height(20).Width(UnitValue.Stretch())
            .Margin(8, 8, 8, 0)
            .IsNotInteractable()
            .Text("Choose a username", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);

        paper.Box("mb_pp_sub")
            .Height(14).Width(UnitValue.Stretch())
            .Margin(8, 8, 2, 6)
            .IsNotInteractable()
            .Text("Your public handle on Prowl", font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSize - 3)
            .Alignment(TextAlignment.MiddleCenter);

        paper.Box("mb_pp_sep1")
            .Height(1).Margin(4, 4, 0, 0)
            .BackgroundColor(EditorTheme.Ink200)
            .IsNotInteractable();

        // Username label
        paper.Box("mb_pp_hlbl")
            .Height(14).Width(UnitValue.Stretch())
            .Margin(10, 10, 8, 2)
            .IsNotInteractable()
            .Text("Username", font)
            .TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize - 2)
            .Alignment(TextAlignment.MiddleLeft);

        // Handle row: @-prefix joined to TextField
        using (paper.Row("mb_pp_handle_row")
            .Height(26f)
            .Margin(10, 10, 0, 0)
            .Enter())
        {
            paper.Box("mb_pp_at")
                .Width(26).Height(26f)
                .BackgroundColor(EditorTheme.Neutral200)
                .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                .RoundedLeft(3)
                .IsNotInteractable()
                .Text("@", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);

            Origami.TextField(paper, "mb_pp_handle_inp", _handleInput, v =>
            {
                _handleInput = v;
                _availStatus = AvailStatus.None;
                _lastChecked = "";

                // Validate — show red warning, do not silently filter input
                bool hasInvalid = false;
                foreach (char c in v)
                {
                    if (!char.IsLetterOrDigit(c)) { hasInvalid = true; break; }
                }

                if (hasInvalid)
                    _handleWarning = "Only letters and numbers are allowed";
                else if (v.Length > 0 && v.Length < 3)
                    _handleWarning = "Must be at least 3 characters";
                else if (v.Length > 30)
                    _handleWarning = "30 character maximum";
                else
                    _handleWarning = null;
            })
            .Placeholder("handle")
            .Width(UnitValue.Stretch())
            .Show();
        }

        // Validation warning or availability status (always same height for stable layout)
        DrawHandleStatus(paper, font);

        // Display Name label
        paper.Box("mb_pp_nlbl")
            .Height(14).Width(UnitValue.Stretch())
            .Margin(10, 10, 6, 2)
            .IsNotInteractable()
            .Text("Display Name", font)
            .TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize - 2)
            .Alignment(TextAlignment.MiddleLeft);

        // Display Name input — wrapped in a row so margins control width
        using (paper.Row("mb_pp_name_row")
            .Height(26f)
            .Margin(10, 10, 0, 0)
            .Enter())
        {
            Origami.TextField(paper, "mb_pp_name_inp", _nameInput, v => _nameInput = v)
                .Placeholder("Your display name")
                .Width(UnitValue.Stretch())
                .Show();
        }

        // Create-profile error (or placeholder gap to prevent layout shift)
        if (_createError != null)
        {
            paper.Box("mb_pp_err")
                .Height(14).Width(UnitValue.Stretch())
                .Margin(10, 10, 4, 0)
                .IsNotInteractable()
                .Text(_createError, font)
                .TextColor(Color.FromArgb(255, 220, 80, 80))
                .FontSize(EditorTheme.FontSize - 3)
                .Alignment(TextAlignment.MiddleCenter);
        }
        else
        {
            paper.Box("mb_pp_err_pad").Height(8);
        }

        // Create button
        using (paper.Row("mb_pp_btn_row")
            .Height(26f)
            .Margin(10, 10, 0, 8)
            .Enter())
        {
            Origami.Button(paper, "mb_pp_create",
                _isCreating ? "Creating..." : "Create Profile",
                OnCreateClicked)
                .FullWidth()
                .Show();
        }
    }

    private static void DrawHandleStatus(Paper paper, FontFile font)
    {
        string text;
        Color color;

        if (_handleWarning != null)
        {
            text  = _handleWarning;
            color = Color.FromArgb(255, 220, 80, 80);
        }
        else if (_availStatus == AvailStatus.Checking)
        {
            text  = "Checking availability...";
            color = EditorTheme.Ink300;
        }
        else if (_availStatus == AvailStatus.Available)
        {
            text  = $"{EditorIcons.Check}  Available";
            color = Color.FromArgb(255, 80, 200, 120);
        }
        else if (_availStatus == AvailStatus.Taken)
        {
            text  = "Username already taken";
            color = Color.FromArgb(255, 220, 80, 80);
        }
        else
        {
            // Empty placeholder — keeps layout stable
            paper.Box("mb_pp_hpad").Height(16).Margin(10, 10, 2, 4);
            return;
        }

        paper.Box("mb_pp_hstatus")
            .Height(16).Width(UnitValue.Stretch())
            .Margin(10, 10, 2, 4)
            .IsNotInteractable()
            .Text(text, font)
            .TextColor(color)
            .FontSize(EditorTheme.FontSize - 3)
            .Alignment(TextAlignment.MiddleLeft);
    }

    // ── Create logic ─────────────────────────────────────────────────────

    private static void OnCreateClicked()
    {
        if (_isCreating) return;

        string h = _handleInput.Trim();
        string n = _nameInput.Trim();

        // Client-side guard — mirrors real-time validation
        bool hasInvalid = false;
        foreach (char c in h)
        {
            if (!char.IsLetterOrDigit(c)) { hasInvalid = true; break; }
        }

        if (hasInvalid)
        {
            _handleWarning = "Only letters and numbers are allowed";
            return;
        }

        if (h.Length < 3)  { _handleWarning = "Must be at least 3 characters"; return; }
        if (h.Length > 30) { _handleWarning = "30 character maximum"; return; }
        if (string.IsNullOrWhiteSpace(n)) { _createError = "Display name is required."; return; }

        // Check availability if not yet confirmed for this exact handle
        if (_availStatus != AvailStatus.Available || _lastChecked != h)
        {
            _availStatus = AvailStatus.Checking;
            _lastChecked = h;
            _ = ProwlService.CheckHandleAvailableAsync(h).ContinueWith(t =>
            {
                _availStatus = t.Result ? AvailStatus.Available : AvailStatus.Taken;
            });
            return; // User sees status; clicking again will proceed if Available
        }

        _isCreating  = true;
        _createError = null;
        _ = ProwlService.CreateProfileAsync(h, n).ContinueWith(t =>
        {
            _isCreating = false;
            if (t.Result == null)
            {
                _availStatus = AvailStatus.Taken;
                _createError = "That username was just taken — try another.";
            }
            else
            {
                ResetForm();
            }
        });
    }
}
