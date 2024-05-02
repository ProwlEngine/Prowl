using Hexa.NET.ImGui;
using Prowl.Runtime;
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

        public override void OnInspectorGUI()
        {
            var importer = (FontImporter)(target as MetaFile).importer;
            ImGui.InputFloat("Font Size", ref importer.fontSize);
            ImGui.InputInt("Width", ref importer.width);
            ImGui.InputInt("Height", ref importer.height);

            ImGui.Text("Character Ranges");
            ImGui.Indent();
            foreach (var range in importer.characterRanges)
            {
                if (ImGui.Button("X"))
                {
                    importer.characterRanges.Remove(range);
                    break;
                }
                ImGui.SameLine();
                ImGui.Text($"{range.Start:X} - {range.End:X}");
            }
            ImGui.Unindent();
            // Add new range
            if (ImGui.Button("Add Range"))
            {
                ImGui.OpenPopup("AddRange");
            }
            if (ImGui.BeginPopup("AddRange"))
            {
                ImGui.Text("Start");
                ImGui.InputInt("##Start", ref start);
                ImGui.Text("End");
                ImGui.InputInt("##End", ref end);
                if (ImGui.Button("Add Custom"))
                {
                    importer.characterRanges.Add(new Font.CharacterRange(start, end));
                    ImGui.CloseCurrentPopup();
                }
                // add predefined ranges
                if (ImGui.Button("Add Basic Latin"))
                    importer.characterRanges.Add(Font.CharacterRange.BasicLatin);
                if (ImGui.Button("Add Latin1 Supplement"))
                    importer.characterRanges.Add(Font.CharacterRange.Latin1Supplement);
                if (ImGui.Button("Add Latin Extended A"))
                    importer.characterRanges.Add(Font.CharacterRange.LatinExtendedA);
                if (ImGui.Button("Add Latin Extended B"))
                    importer.characterRanges.Add(Font.CharacterRange.LatinExtendedB);
                if (ImGui.Button("Add Cyrillic"))
                    importer.characterRanges.Add(Font.CharacterRange.Cyrillic);
                if (ImGui.Button("Add Cyrillic Supplement"))
                    importer.characterRanges.Add(Font.CharacterRange.CyrillicSupplement);
                if (ImGui.Button("Add Hiragana"))
                    importer.characterRanges.Add(Font.CharacterRange.Hiragana);
                if (ImGui.Button("Add Katakana"))
                    importer.characterRanges.Add(Font.CharacterRange.Katakana);
                if (ImGui.Button("Add Greek"))
                    importer.characterRanges.Add(Font.CharacterRange.Greek);
                if (ImGui.Button("Add Cjk Symbols And Punctuation"))
                    importer.characterRanges.Add(Font.CharacterRange.CjkSymbolsAndPunctuation);
                if (ImGui.Button("Add Cjk Unified Ideographs"))
                    importer.characterRanges.Add(Font.CharacterRange.CjkUnifiedIdeographs);
                if (ImGui.Button("Add Hangul Compatibility Jamo"))
                    importer.characterRanges.Add(Font.CharacterRange.HangulCompatibilityJamo);
                if (ImGui.Button("Add Hangul Syllables"))
                    importer.characterRanges.Add(Font.CharacterRange.HangulSyllables);
                ImGui.EndPopup();
            }

            if (ImGui.Button("Save"))
            {
                (target as MetaFile).Save();
                AssetDatabase.Reimport((target as MetaFile).AssetPath);
            }
        }

    }
}
