// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// A single-line editable text field. Extends <see cref="Selectable"/> so it gets the hover/focus tint,
/// and drives a child <see cref="TextComponent"/> for the visible text plus optional caret and selection
/// images that it positions each frame using <see cref="TextComponent.MeasureWidth"/>. Supports caret
/// movement (arrows, Home/End), Backspace/Delete, drag- and shift-selection, select-all, and clipboard
/// cut/copy/paste. The text scrolls horizontally within the field so the caret stays visible; pair the
/// field with a <see cref="RectMask"/> to clip the overflow.
/// </summary>
[AddComponentMenu("UI/Input Field")]
[ComponentIcon("")] // Keyboard
public class UIInputField : Selectable,
    IPointerDownHandler, IBeginDragHandler, IDragHandler,
    ISubmitHandler, ICancelHandler
{
    private const float BlinkRate = 0.53f;
    private const float InitialRepeatDelay = 0.4f;
    private const float RepeatInterval = 0.035f;

    private static readonly Color s_transparent = new(0f, 0f, 0f, 0f);

    // ---- Wired children ----
    [SerializeField] private TextComponent? _textComponent;
    /// <summary>The label that shows the current text. Required for the field to render anything.</summary>
    public TextComponent? TextComponent { get => _textComponent; set { _textComponent = value; UpdateDisplay(); } }

    [SerializeField] private TextComponent? _placeholder;
    /// <summary>Optional label shown only while the field is empty.</summary>
    public TextComponent? Placeholder { get => _placeholder; set { _placeholder = value; UpdatePlaceholder(); } }

    [SerializeField] private RectTransform? _caret;
    /// <summary>Optional caret image; its left edge is moved to the caret position each frame.</summary>
    public RectTransform? Caret { get => _caret; set => _caret = value; }

    [SerializeField] private RectTransform? _selection;
    /// <summary>Optional selection highlight image; sized to cover the selected range.</summary>
    public RectTransform? Selection { get => _selection; set => _selection = value; }

    [SerializeField] private RectTransform? _textArea;
    /// <summary>The area the text is measured/scrolled within; defaults to the text's own rect.</summary>
    public RectTransform? TextArea { get => _textArea; set => _textArea = value; }

    // ---- Value ----
    [SerializeField] private string _text = string.Empty;
    /// <summary>The current text. Setting this from code fires <see cref="OnValueChanged"/>; use
    /// <see cref="SetTextWithoutNotify"/> to suppress that.</summary>
    public string Text
    {
        get => _text;
        set
        {
            string v = Sanitize(value);
            if (v == _text) return;
            _text = v;
            ClampCaret();
            OnChanged();
        }
    }

    [SerializeField] private int _characterLimit = 0;
    /// <summary>Maximum number of characters, or 0 for no limit.</summary>
    public int CharacterLimit { get => _characterLimit; set => _characterLimit = Math.Max(0, value); }

    [SerializeField] private bool _isPassword = false;
    /// <summary>When true, the visible text is masked with <see cref="MaskChar"/>.</summary>
    public bool IsPassword { get => _isPassword; set { _isPassword = value; UpdateDisplay(); } }

    [SerializeField] private char _maskChar = '*';
    public char MaskChar { get => _maskChar; set { _maskChar = value; UpdateDisplay(); } }

    [SerializeField] private bool _readOnly = false;
    public bool ReadOnly { get => _readOnly; set => _readOnly = value; }

    [SerializeField] private bool _selectAllOnFocus = true;
    /// <summary>Select the whole text when the field gains focus.</summary>
    public bool SelectAllOnFocus { get => _selectAllOnFocus; set => _selectAllOnFocus = value; }

    // ---- Caret / selection appearance ----
    [SerializeField] private Color _caretColor = new(0.90f, 0.90f, 0.92f, 1f);
    public Color CaretColor { get => _caretColor; set => _caretColor = value; }

    [SerializeField] private Color _selectionColor = new(0.25f, 0.47f, 0.85f, 0.5f);
    public Color SelectionColor { get => _selectionColor; set => _selectionColor = value; }

    // ---- Callbacks ----
    /// <summary>Fires whenever the text changes (typing, deleting, paste, or code assignment).</summary>
    public event Action<string>? OnValueChanged;
    /// <summary>Fires when editing ends - focus lost or Enter pressed.</summary>
    public event Action<string>? OnEndEdit;
    /// <summary>Fires when Enter/Submit is pressed while focused.</summary>
    public event Action<string>? OnSubmitted;

    [SerializeField] private ProwlAction _onValueChanged = new();
    public ProwlAction ValueChangedAction => _onValueChanged;

    [SerializeField] private ProwlAction _onEndEdit = new();
    public ProwlAction EndEditAction => _onEndEdit;

    // ---- Runtime state ----
    [SerializeIgnore] private int _caretPos;
    [SerializeIgnore] private int _selectionAnchor;
    [SerializeIgnore] private float _scrollX;
    [SerializeIgnore] private float _blinkTimer;
    [SerializeIgnore] private bool _blinkOn = true;
    [SerializeIgnore] private KeyCode _repeatKey = KeyCode.Unknown;
    [SerializeIgnore] private float _repeatDelay;
    [SerializeIgnore] private bool _placeholderShown = true;
    [SerializeIgnore] private UIImage? _caretImage;
    [SerializeIgnore] private UIImage? _selectionImage;

    private string DisplayText => _isPassword ? new string(_maskChar, _text.Length) : _text;
    private RectTransform Area => _textArea ?? _textComponent?.GameObject.RectTransform ?? GameObject.RectTransform!;
    private int SelMin => Math.Min(_selectionAnchor, _caretPos);
    private int SelMax => Math.Max(_selectionAnchor, _caretPos);
    private bool HasSelection => _selectionAnchor != _caretPos;
    private bool IsFocused => ReferenceEquals(EventSystem.Current?.Selected, GameObject) && IsInteractable();

    private UIImage? CaretImage => _caretImage ??= _caret?.GameObject.GetComponent<UIImage>();
    private UIImage? SelectionImage => _selectionImage ??= _selection?.GameObject.GetComponent<UIImage>();

    public override void OnEnable()
    {
        base.OnEnable();
        ClampCaret();
        UpdateDisplay();
    }

    // Keep the visible text in sync when the field is edited in the inspector (edit mode, where Update
    // does not run). Runs on any inspector field change.
    public override void OnValidate()
    {
        base.OnValidate();
        ClampCaret();
        UpdateDisplay();
    }

    // ============================================================
    // Focus
    // ============================================================

    public override void OnSelect()
    {
        base.OnSelect();
        if (_selectAllOnFocus) SelectAll();
        ResetBlink();
    }

    public override void OnDeselect()
    {
        base.OnDeselect();
        _selectionAnchor = _caretPos; // clear selection
        EndEdit();
    }

    public void OnSubmit()
    {
        if (!IsInteractable()) return;
        try { OnSubmitted?.Invoke(_text); }
        catch (Exception ex) { Debug.LogError($"[UIInputField] OnSubmitted on '{Name}' threw: {ex.Message}\n{ex.StackTrace}"); }
        EventSystem.Current?.SetSelected(null); // ends editing (fires OnDeselect -> EndEdit)
    }

    public void OnCancel()
    {
        // The event system clears focus after this; nothing extra to revert for now.
    }

    // ============================================================
    // Pointer
    // ============================================================

    public override void OnPointerDown(PointerEventData e)
    {
        base.OnPointerDown(e);
        if (e.Button != MouseButton.Left || !IsInteractable()) return;
        SetCaret(IndexFromPointer(e), Input.IsShiftPressed);
        e.Use();
    }

    public void OnBeginDrag(PointerEventData e) { /* selection extends in OnDrag */ }

    public void OnDrag(PointerEventData e)
    {
        if (e.Button != MouseButton.Left || !IsInteractable()) return;
        SetCaret(IndexFromPointer(e), select: true);
        e.Use();
    }

    private int IndexFromPointer(PointerEventData e)
    {
        Rect area = Area.ComputedRect;
        float localX = e.DesignPosition.X - area.Min.X + _scrollX;
        return IndexFromLocalX(localX);
    }

    private int IndexFromLocalX(float localX)
    {
        string d = DisplayText;
        if (_textComponent is null || d.Length == 0 || localX <= 0f) return 0;

        float prev = 0f;
        for (int i = 1; i <= d.Length; i++)
        {
            float w = _textComponent.MeasureWidth(d.Substring(0, i));
            if (localX < (prev + w) * 0.5f) return i - 1;
            prev = w;
        }
        return d.Length;
    }

    // ============================================================
    // Per-frame
    // ============================================================

    public override void Update()
    {
        base.Update(); // Selectable tint lerp
        if (!Application.IsPlaying) return;

        // Keep the label in sync every frame so the text shows regardless of how it was set.
        UpdateDisplay();

        bool focused = IsFocused;
        if (focused)
        {
            ProcessKeyboard();

            _blinkTimer += Time.DeltaTime;
            if (_blinkTimer >= BlinkRate) { _blinkTimer -= BlinkRate; _blinkOn = !_blinkOn; }

            UpdateCaretVisual();
        }
        else if (_scrollX != 0f)
        {
            _scrollX = 0f;
            ShiftText(0f);
        }

        ApplyCaretColors(focused);
    }

    private void ProcessKeyboard()
    {
        bool shift = Input.IsShiftPressed;
        bool ctrl = Input.IsCtrlPressed;

        if (_repeatKey != KeyCode.Unknown && !Input.GetKey(_repeatKey))
            _repeatKey = KeyCode.Unknown;

        if (ctrl)
        {
            if (Input.GetKeyDown(KeyCode.A)) SelectAll();
            if (Input.GetKeyDown(KeyCode.C)) Copy();
            if (Input.GetKeyDown(KeyCode.X)) Cut();
            if (Input.GetKeyDown(KeyCode.V)) Paste();
        }

        if (Repeat(KeyCode.Left))      MoveLeft(shift);
        if (Repeat(KeyCode.Right))     MoveRight(shift);
        if (Repeat(KeyCode.Home))      SetCaret(0, shift);
        if (Repeat(KeyCode.End))       SetCaret(_text.Length, shift);
        if (Repeat(KeyCode.Backspace)) Backspace();
        if (Repeat(KeyCode.Delete))    DeleteForward();

        // Typed characters come from Input.InputString, which is read non-destructively so Paper and the
        // UI don't fight over the input queue. Control combos and non-printables are ignored.
        if (!ctrl)
        {
            foreach (char ch in Input.InputString)
            {
                if (ch < ' ' || ch == '\x7f') continue;
                Insert(ch.ToString());
            }
        }
    }

    // Fires true on the initial press and again on a repeat cadence while the key is held.
    private bool Repeat(KeyCode key)
    {
        if (Input.GetKeyDown(key)) { _repeatKey = key; _repeatDelay = InitialRepeatDelay; return true; }
        if (_repeatKey == key && Input.GetKey(key))
        {
            _repeatDelay -= Time.DeltaTime;
            if (_repeatDelay <= 0f) { _repeatDelay = RepeatInterval; return true; }
        }
        return false;
    }

    // ============================================================
    // Editing operations
    // ============================================================

    private void MoveLeft(bool select)
    {
        if (!select && HasSelection) SetCaret(SelMin, false);
        else SetCaret(_caretPos - 1, select);
    }

    private void MoveRight(bool select)
    {
        if (!select && HasSelection) SetCaret(SelMax, false);
        else SetCaret(_caretPos + 1, select);
    }

    private void SetCaret(int pos, bool select)
    {
        pos = Math.Clamp(pos, 0, _text.Length);
        if (!select) _selectionAnchor = pos;
        _caretPos = pos;
        ResetBlink();
    }

    /// <summary>Selects the whole text and puts the caret at the end.</summary>
    public void SelectAll()
    {
        _selectionAnchor = 0;
        _caretPos = _text.Length;
        ResetBlink();
    }

    private void Insert(string s)
    {
        if (_readOnly) return;
        DeleteSelection();

        if (_characterLimit > 0 && _text.Length + s.Length > _characterLimit)
            s = s.Substring(0, Math.Max(0, _characterLimit - _text.Length));
        if (s.Length == 0) return;

        _text = _text.Insert(_caretPos, s);
        _caretPos += s.Length;
        _selectionAnchor = _caretPos;
        OnChanged();
    }

    private bool DeleteSelection()
    {
        if (_readOnly || !HasSelection) return false;
        int a = SelMin, b = SelMax;
        _text = _text.Remove(a, b - a);
        _caretPos = a;
        _selectionAnchor = a;
        OnChanged();
        return true;
    }

    private void Backspace()
    {
        if (_readOnly) return;
        if (DeleteSelection()) return;
        if (_caretPos <= 0) return;
        _text = _text.Remove(_caretPos - 1, 1);
        _caretPos--;
        _selectionAnchor = _caretPos;
        OnChanged();
    }

    private void DeleteForward()
    {
        if (_readOnly) return;
        if (DeleteSelection()) return;
        if (_caretPos >= _text.Length) return;
        _text = _text.Remove(_caretPos, 1);
        _selectionAnchor = _caretPos;
        OnChanged();
    }

    private void Copy()
    {
        if (_isPassword || !HasSelection) return;
        Input.Clipboard = _text.Substring(SelMin, SelMax - SelMin);
    }

    private void Cut()
    {
        if (_isPassword || _readOnly || !HasSelection) return;
        Input.Clipboard = _text.Substring(SelMin, SelMax - SelMin);
        DeleteSelection();
    }

    private void Paste()
    {
        if (_readOnly) return;
        string clip = Input.Clipboard;
        if (string.IsNullOrEmpty(clip)) return;
        Insert(Sanitize(clip));
    }

    /// <summary>Assigns the text without firing <see cref="OnValueChanged"/>.</summary>
    public void SetTextWithoutNotify(string value)
    {
        _text = Sanitize(value);
        ClampCaret();
        UpdateDisplay();
    }

    // Single-line: strip newlines and enforce the character limit.
    private string Sanitize(string? value)
    {
        string v = value ?? string.Empty;
        if (v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
            v = v.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        if (_characterLimit > 0 && v.Length > _characterLimit)
            v = v.Substring(0, _characterLimit);
        return v;
    }

    private void ClampCaret()
    {
        _caretPos = Math.Clamp(_caretPos, 0, _text.Length);
        _selectionAnchor = Math.Clamp(_selectionAnchor, 0, _text.Length);
    }

    private void OnChanged()
    {
        UpdateDisplay();
        ResetBlink();
        try { OnValueChanged?.Invoke(_text); }
        catch (Exception ex) { Debug.LogError($"[UIInputField] OnValueChanged on '{Name}' threw: {ex.Message}\n{ex.StackTrace}"); }
        _onValueChanged.Invoke();
    }

    private void EndEdit()
    {
        try { OnEndEdit?.Invoke(_text); }
        catch (Exception ex) { Debug.LogError($"[UIInputField] OnEndEdit on '{Name}' threw: {ex.Message}\n{ex.StackTrace}"); }
        _onEndEdit.Invoke();
    }

    // ============================================================
    // Visuals
    // ============================================================

    private void UpdateDisplay()
    {
        if (_textComponent != null) _textComponent.Text = DisplayText;
        UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
    {
        if (_placeholder is null) return;
        bool show = _text.Length == 0;
        if (show == _placeholderShown) return;
        _placeholderShown = show;
        _placeholder.GameObject.Enabled = show;
    }

    private void ResetBlink()
    {
        _blinkOn = true;
        _blinkTimer = 0f;
    }

    private void UpdateCaretVisual()
    {
        if (_textComponent is null) return;

        string d = DisplayText;
        int caret = Math.Clamp(_caretPos, 0, d.Length);
        float caretX = _textComponent.MeasureWidth(d.Substring(0, caret));
        float viewW = Area.ComputedRect.Size.X;

        // Scroll so the caret stays inside the viewport, then clamp so we never scroll past the text.
        if (caretX - _scrollX > viewW) _scrollX = caretX - viewW;
        if (caretX - _scrollX < 0f) _scrollX = caretX;
        float totalW = _textComponent.MeasureWidth(d);
        _scrollX = Maths.Clamp(_scrollX, 0f, MathF.Max(0f, totalW - viewW));

        ShiftText(-_scrollX);

        if (_caret != null)
            _caret.AnchoredPosition = new Float2(caretX - _scrollX, _caret.AnchoredPosition.Y);

        if (_selection != null && HasSelection)
        {
            float aX = _textComponent.MeasureWidth(d.Substring(0, SelMin));
            float bX = _textComponent.MeasureWidth(d.Substring(0, SelMax));
            _selection.AnchoredPosition = new Float2(aX - _scrollX, _selection.AnchoredPosition.Y);
            _selection.SizeDelta = new Float2(bX - aX, _selection.SizeDelta.Y);
        }
    }

    private void ShiftText(float x)
    {
        RectTransform? trt = _textComponent?.GameObject.RectTransform;
        if (trt is null) return;
        if (trt.AnchoredPosition.X != x)
            trt.AnchoredPosition = new Float2(x, trt.AnchoredPosition.Y);
    }

    private void ApplyCaretColors(bool focused)
    {
        UIImage? ci = CaretImage;
        if (ci != null) ci.Color = (focused && _blinkOn) ? _caretColor : s_transparent;

        UIImage? si = SelectionImage;
        if (si != null) si.Color = (focused && HasSelection) ? _selectionColor : s_transparent;
    }
}
