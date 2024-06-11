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
            using (gui.Node("f" + name, numberOfProperties).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                // Label
                using (gui.Node("#_Label").ExpandHeight().Clip().Enter())
                {
                    var pos = gui.CurrentNode.LayoutData.Rect.Min;
                    pos.x += 28;
                    pos.y += 5;
                    gui.Draw2D.DrawText(name, pos, GuiStyle.Base8);
                }

                // Value
                using (gui.Node("#_Value").ExpandHeight().Enter())
                    EditorGUI.Property_Float(name, ref val);
            }
        }

        public void InputInt<T>(string name, ref T val) where T : struct
        {
            using (gui.Node("f" + name, numberOfProperties).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                // Label
                using (gui.Node("#_Label").ExpandHeight().Clip().Enter())
                {
                    var pos = gui.CurrentNode.LayoutData.Rect.Min;
                    pos.x += 28;
                    pos.y += 5;
                    gui.Draw2D.DrawText(name, pos, GuiStyle.Base8);
                }

                // Value
                using (gui.Node("#_Value").ExpandHeight().Enter())
                    EditorGUI.PropertyIntegar(name, ref val);
            }
        }

        public bool QuickButton(string label, LayoutNode popupHolder)
        {
            using (gui.Node(label).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f, 4);

                gui.Draw2D.DrawText(label, gui.CurrentNode.LayoutData.Rect, GuiStyle.Base8);

                if (gui.IsNodePressed())
                {
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                    if(popupHolder != null)
                        gui.ClosePopup(popupHolder);
                    return true;
                }

                if (gui.IsNodeHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);


                return false;
            }
        }

        public override void OnInspectorGUI()
        {
            var importer = (FontImporter)(target as MetaFile).importer;

            gui.CurrentNode.Layout(LayoutType.Column);

            InputFloat("Font Size", ref importer.fontSize);
            InputInt("Width", ref importer.width);
            InputInt("Height", ref importer.height);

            using (gui.Node("Ranges").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 10);

                int rangeIndex = 0;
                foreach (var range in importer.characterRanges)
                {
                    using (gui.Node("DelRange" + rangeIndex++).Scale(GuiStyle.ItemHeight).Enter())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f, 4);

                        gui.Draw2D.DrawText("X", gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, 8), GuiStyle.Base8);

                        if (gui.IsNodeHovered())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                        if (gui.IsNodePressed())
                        {
                            importer.characterRanges.Remove(range);
                        }
                        var pos = new Vector2(gui.CurrentNode.LayoutData.Rect.Right, ((gui.CurrentNode.LayoutData.Rect.Top + gui.CurrentNode.LayoutData.Rect.Bottom) / 2) - 15);
                        gui.Draw2D.DrawText($"{range.Start:X} - {range.End:X}", pos + new Vector2(8, 8), GuiStyle.Base8);
                    }
                }
            }


            using (gui.Node("AddRange").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f, 4);

                gui.Draw2D.DrawText("Add Range", gui.CurrentNode.LayoutData.Rect, GuiStyle.Base8);

                if (gui.IsNodeHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                if(gui.IsNodePressed())
                    gui.OpenPopup("AddRangePopup");

                if (gui.BeginPopup("AddRangePopup", out var popupNode))
                {
                    using (popupNode.Width(250).FitContentHeight().Layout(LayoutType.Column).Padding(5).Enter())
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
