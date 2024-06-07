using Hexa.NET.ImGui;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(Font), ".ttf")]
    public class FontImporter : ScriptedImporter
    {
        public List<Font.CharacterRange> characterRanges = new() { Font.CharacterRange.BasicLatin };
        public float fontSize = 20;
        public int width = 1024;
        public int height = 1024;

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            var ttf = File.ReadAllBytes(assetPath.FullName);
            ctx.SetMainObject(Font.CreateFromTTFMemory(ttf, fontSize, width, height, characterRanges.ToArray()));
        }
    }

    [CustomEditor(typeof(FontImporter))]
    public class FontEditor : ScriptedEditor
    {
        static int start, end;
        private int numberOfProperties = 0;
        public void InputFloat(string name, ref float val)
        {
            using (g.Node("f" + name, numberOfProperties).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                // Label
                using (g.Node("#_Label").ExpandHeight().Clip().Enter())
                {
                    var pos = g.CurrentNode.LayoutData.Rect.Min;
                    pos.x += 28;
                    pos.y += 5;
                    g.Draw2D.DrawText(name, pos, GuiStyle.Base8);
                }

                // Value
                using (g.Node("#_Value").ExpandHeight().Enter())
                    EditorGUI.Property_Float(name, ref val);
            }
        }

        public void InputInt<T>(string name, ref T val) where T : struct
        {
            using (g.Node("f" + name, numberOfProperties).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                // Label
                using (g.Node("#_Label").ExpandHeight().Clip().Enter())
                {
                    var pos = g.CurrentNode.LayoutData.Rect.Min;
                    pos.x += 28;
                    pos.y += 5;
                    g.Draw2D.DrawText(name, pos, GuiStyle.Base8);
                }

                // Value
                using (g.Node("#_Value").ExpandHeight().Enter())
                    EditorGUI.PropertyIntegar(name, ref val);
            }
        }

        public bool QuickButton(string label, LayoutNode popupHolder)
        {
            using (g.ButtonNode(label, out var p, out var h).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f, 4);

                g.Draw2D.DrawText(label, g.CurrentNode.LayoutData.Rect, GuiStyle.Base8);

                var interact = g.GetInteractable();
                if (interact.TakeFocus())
                {
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                    if(popupHolder != null)
                        g.ClosePopup(popupHolder);
                    return true;
                }

                if (interact.IsHovered())
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);


                return false;
            }
        }

        public override void OnInspectorGUI()
        {
            var importer = (FontImporter)(target as MetaFile).importer;

            g.CurrentNode.Layout(LayoutType.Column);

            InputFloat("Font Size", ref importer.fontSize);
            InputInt("Width", ref importer.width);
            InputInt("Height", ref importer.height);

            using (g.Node("Ranges").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 10);

                int rangeIndex = 0;
                foreach (var range in importer.characterRanges)
                {
                    using (g.ButtonNode("DelRange" + rangeIndex++, out var pressed, out var hovered).Scale(GuiStyle.ItemHeight).Enter())
                    {
                        g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f, 4);

                        g.Draw2D.DrawText("X", g.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, 8), GuiStyle.Base8);

                        if (hovered)
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                        if (pressed)
                        {
                            importer.characterRanges.Remove(range);
                        }
                        var pos = new Vector2(g.CurrentNode.LayoutData.Rect.Right, ((g.CurrentNode.LayoutData.Rect.Top + g.CurrentNode.LayoutData.Rect.Bottom) / 2) - 15);
                        g.Draw2D.DrawText($"{range.Start:X} - {range.End:X}", pos + new Vector2(8, 8), GuiStyle.Base8);
                    }
                }
            }


            using (g.ButtonNode("AddRange", out var p, out var h).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f, 4);

                g.Draw2D.DrawText("Add Range", g.CurrentNode.LayoutData.Rect, GuiStyle.Base8);

                if (h)
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                if(p)
                    g.OpenPopup("AddRangePopup");

                if (g.BeginPopup("AddRangePopup", out var popupNode))
                {
                    using (popupNode.Width(250).FitContentHeight().Layout(LayoutType.Column).Padding(10).Enter())
                    {
                        if (QuickButton("Add Basic Latin", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.BasicLatin);
                        if (QuickButton("Add Latin1 Supplement", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.Latin1Supplement);
                        if (QuickButton("Add Latin Extended A", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.LatinExtendedA);
                        if (QuickButton("Add Latin Extended B", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.LatinExtendedB);
                        if (QuickButton("Add Cyrillic", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.Cyrillic);
                        if (QuickButton("Add Cyrillic Supplement", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.CyrillicSupplement);
                        if (QuickButton("Add Hiragana", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.Hiragana);
                        if (QuickButton("Add Katakana", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.Katakana);
                        if (QuickButton("Add Greek", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.Greek);
                        if (QuickButton("Add Cjk Symbols And Punctuation", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.CjkSymbolsAndPunctuation);
                        if (QuickButton("Add Cjk Unified Ideographs", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.CjkUnifiedIdeographs);
                        if (QuickButton("Add Hangul Compatibility Jamo", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.HangulCompatibilityJamo);
                        if (QuickButton("Add Hangul Syllables", popupNode.Parent))
                            importer.characterRanges.Add(Font.CharacterRange.HangulSyllables);
                    }
                }
            }

            if (QuickButton("Save", null))
            {
                (target as MetaFile).Save();
                AssetDatabase.Reimport((target as MetaFile).AssetPath);
            }
        }

    }
}
