// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// A drop-down selector. Extends <see cref="Selectable"/> for the hover/press tint and shows the current
/// choice in <see cref="CaptionText"/>. Clicking opens a list, built under <see cref="OptionsRoot"/> from
/// the <see cref="Options"/> strings; picking an item sets <see cref="Value"/> and closes the list. The
/// item GameObjects are created on demand at play time and reused between opens.
/// </summary>
[AddComponentMenu("UI/Dropdown")]
[ComponentIcon("")] // ChevronDown
public class UIDropdown : Selectable, IPointerClickHandler, ISubmitHandler, ICancelHandler
{
    [SerializeField] private List<string> _options = new();
    /// <summary>The selectable choices. Edit directly or via the Add/Clear/Set helpers.</summary>
    public List<string> Options => _options;

    [SerializeField] private int _value = 0;
    /// <summary>The selected index, clamped to the options range.</summary>
    public int Value { get => _value; set => SetValue(value, notify: true); }

    [SerializeField] private TextComponent? _captionText;
    /// <summary>The label that shows the current selection.</summary>
    public TextComponent? CaptionText { get => _captionText; set { _captionText = value; UpdateCaption(); } }

    [SerializeField] private RectTransform? _optionsRoot;
    /// <summary>The panel the item list is built inside; kept disabled while the dropdown is closed.</summary>
    public RectTransform? OptionsRoot { get => _optionsRoot; set => _optionsRoot = value; }

    [SerializeField] private float _itemHeight = 24f;
    public float ItemHeight { get => _itemHeight; set => _itemHeight = MathF.Max(1f, value); }

    [SerializeField] private int _itemTextSize = 16;
    public int ItemTextSize { get => _itemTextSize; set => _itemTextSize = Math.Max(1, value); }

    [SerializeField] private Color _itemColor = new(0.16f, 0.16f, 0.20f, 1f);
    [SerializeField] private Color _itemSelectedColor = new(0.24f, 0.40f, 0.75f, 1f);
    [SerializeField] private Color _itemTextColor = new(0.88f, 0.88f, 0.90f, 1f);

    /// <summary>Fires when the selected index changes.</summary>
    public event Action<int>? OnValueChanged;

    [SerializeField] private ProwlAction _onValueChanged = new();
    public ProwlAction ValueChangedAction => _onValueChanged;

    [SerializeIgnore] private bool _open;
    [SerializeIgnore] private readonly List<Item> _items = new();

    private sealed class Item
    {
        public required GameObject Go;
        public required UIImage Background;
        public required TextComponent Label;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        ClampValue();
        UpdateCaption();
    }

    public override void OnDisable()
    {
        base.OnDisable();
        Close();
    }

    // ============================================================
    // Options / value
    // ============================================================

    public void ClearOptions()
    {
        _options.Clear();
        ClampValue();
        UpdateCaption();
        if (_open) RebuildItems();
    }

    public void AddOption(string option)
    {
        _options.Add(option ?? string.Empty);
        UpdateCaption();
        if (_open) RebuildItems();
    }

    public void SetOptions(IEnumerable<string> options)
    {
        _options.Clear();
        if (options != null) _options.AddRange(options);
        ClampValue();
        UpdateCaption();
        if (_open) RebuildItems();
    }

    private void SetValue(int v, bool notify)
    {
        if (_options.Count == 0) { _value = 0; UpdateCaption(); return; }
        v = Math.Clamp(v, 0, _options.Count - 1);
        bool changed = v != _value;
        _value = v;
        UpdateCaption();

        if (changed && notify)
        {
            try { OnValueChanged?.Invoke(_value); }
            catch (Exception ex) { Debug.LogError($"[UIDropdown] OnValueChanged on '{Name}' threw: {ex.Message}\n{ex.StackTrace}"); }
            _onValueChanged.Invoke();
        }
    }

    private void ClampValue() => _value = _options.Count == 0 ? 0 : Math.Clamp(_value, 0, _options.Count - 1);

    private void UpdateCaption()
    {
        if (_captionText is null) return;
        _captionText.Text = (_value >= 0 && _value < _options.Count) ? _options[_value] : string.Empty;
    }

    // ============================================================
    // Open / close
    // ============================================================

    public override void OnPointerDown(PointerEventData e)
    {
        base.OnPointerDown(e);
        // Consume the press so it doesn't fall through; the toggle happens on click.
        if (e.Button == MouseButton.Left && IsInteractable()) e.Use();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.Button != MouseButton.Left || !IsInteractable()) return;
        Toggle();
    }

    public void OnSubmit()
    {
        if (IsInteractable()) Toggle();
    }

    public void OnCancel() => Close();

    private void Toggle()
    {
        if (_open) Close();
        else Open();
    }

    private void Open()
    {
        if (_open || !Application.IsPlaying || _optionsRoot is null) return;
        _open = true;
        RebuildItems();
        _optionsRoot.GameObject.Enabled = true;
    }

    private void Close()
    {
        if (!_open) return;
        _open = false;
        if (_optionsRoot is { IsDisposed: false })
            _optionsRoot.GameObject.Enabled = false;
    }

    // A press anywhere outside the dropdown (and its item list) closes it.
    public override void Update()
    {
        base.Update();
        if (!Application.IsPlaying || !_open) return;

        if (Input.GetMouseButtonDown(0) && !IsWithinSubtree(EventSystem.Current?.Hovered))
            Close();
    }

    private bool IsWithinSubtree(GameObject? go)
    {
        for (GameObject? n = go; n != null; n = n.Parent)
            if (ReferenceEquals(n, GameObject)) return true;
        return false;
    }

    // ============================================================
    // Item list construction
    // ============================================================

    private void RebuildItems()
    {
        if (_optionsRoot is null) return;

        while (_items.Count < _options.Count)
            _items.Add(CreateItem(_items.Count));

        for (int i = 0; i < _items.Count; i++)
        {
            Item it = _items[i];
            bool used = i < _options.Count;
            it.Go.Enabled = used;
            if (!used) continue;

            it.Label.Text = _options[i];
            it.Label.Size = _itemTextSize;
            it.Label.TextColor = _itemTextColor;
            it.Background.Color = (i == _value) ? _itemSelectedColor : _itemColor;

            RectTransform rt = it.Go.RectTransform!;
            rt.AnchorMin = new Float2(0f, 1f);
            rt.AnchorMax = new Float2(1f, 1f);
            rt.Pivot = new Float2(0.5f, 1f);
            rt.SizeDelta = new Float2(0f, _itemHeight);
            rt.AnchoredPosition = new Float2(0f, -i * _itemHeight);
        }

        _optionsRoot.SizeDelta = new Float2(_optionsRoot.SizeDelta.X, _options.Count * _itemHeight);
    }

    private Item CreateItem(int index)
    {
        GameObject go = new GameObject($"Item {index}");
        go.EnsureRectTransform();
        UIImage bg = go.AddComponent<UIImage>();
        bg.Color = _itemColor;
        UIButton btn = go.AddComponent<UIButton>();
        btn.TargetGraphic = bg;
        go.SetParent(_optionsRoot!.GameObject, worldPositionStays: false);

        GameObject labelGo = new GameObject("Label");
        labelGo.EnsureRectTransform();
        TextComponent label = labelGo.AddComponent<TextComponent>();
        label.Alignment = TextAlignment.CenterLeft;
        label.Size = _itemTextSize;
        label.TextColor = _itemTextColor;
        labelGo.SetParent(go, worldPositionStays: false);

        // Stretch the label to fill the item with a small left inset.
        RectTransform lrt = labelGo.RectTransform!;
        lrt.AnchorMin = Float2.Zero;
        lrt.AnchorMax = Float2.One;
        lrt.SizeDelta = new Float2(-8f, 0f);
        lrt.AnchoredPosition = new Float2(4f, 0f);

        int captured = index;
        btn.OnClick += () => OnItemClicked(captured);

        return new Item { Go = go, Background = bg, Label = label };
    }

    private void OnItemClicked(int index)
    {
        SetValue(index, notify: true);
        Close();
    }
}
