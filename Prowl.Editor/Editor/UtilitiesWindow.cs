using Hexa.NET.ImGui;
using Prowl.Icons;
using Prowl.Runtime;

namespace Prowl.Editor.EditorWindows
{
    public class UtilitiesWindow : EditorWindow
    {
        public enum Utilities
        {
            Icons
        }
        public Utilities currentType = Utilities.Icons;
        protected override int Width { get; } = 256;
        protected override int Height { get; } = 512;

        public UtilitiesWindow() : base() { Title = FontAwesome6.ScrewdriverWrench + " Utilities"; }
        protected override void Draw()
        {
            const ImGuiTableFlags tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.ContextMenuInBody | ImGuiTableFlags.Resizable;
            System.Numerics.Vector2 availableRegion = ImGui.GetContentRegionAvail();
            if (ImGui.BeginTable("MainViewTable", 2, tableFlags, availableRegion))
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 200);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                ImGui.BeginChild("SettingTypes");
                {
                    if (ImGui.Selectable(FontAwesome6.Icons + " Icons"))
                        currentType = Utilities.Icons;
                }
                ImGui.EndChild();
                ImGui.TableSetColumnIndex(1);
                ImGui.BeginChild("Settings");
                {
                    switch (currentType)
                    {
                        case Utilities.Icons:
                            DrawIcons();
                            break;
                    }
                }
                ImGui.EndChild();

                ImGui.EndTable();
            }
        }

        public string[] IconStrings;
        private string _searchText = "";

        public void DrawIcons()
        {
            if(IconStrings == null)
            {
                // Cache all Icons
                // (icon) Name
                var icons = new List<string>();
                foreach (var field in typeof(FontAwesome6).GetFields())
                {
                    if (field.FieldType == typeof(string))
                    {
                        var iconValue = field.GetValue(null);
                        var iconName = field.Name;
                        icons.Add($"{iconValue} {iconName}");
                    }
                }
                IconStrings = icons.ToArray();
            }

            GUIHelper.Search("##searchBox", ref _searchText, ImGui.GetContentRegionAvail().X);
            ImGui.BeginChild("listoficons");
            // Draw a list of all FontAwesome6 Icons
            if (string.IsNullOrEmpty(_searchText))
            {
                for (int i = 0; i < IconStrings.Length; i++)
                    ImGui.Selectable(IconStrings[i]);
            }
            else
            {

                for (int i = 0; i < IconStrings.Length; i++)
                    if (IconStrings[i].Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                        ImGui.Selectable(IconStrings[i]);
            }
            ImGui.EndChild();
        }

    }
}
