// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Build;
using Prowl.Editor.Preferences;
using Prowl.Editor.ProjectSettings;
using Prowl.Editor.Utilities;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor
{
    public class BuildWindow : EditorWindow
    {
        protected override bool Center { get; } = true;
        protected override double Width { get; } = 512 + (512 / 2);
        protected override double Height { get; } = 512;
        protected override bool BackgroundFade { get; } = true;
        protected override bool LockSize => true;
        protected override double Padding => 0;

        private List<ProjectBuilder> builders = new List<ProjectBuilder>();
        private int selectedBuilder = 0;
        private string buildName = "";

        public BuildWindow() : base()
        {
            Title = FontAwesome6.FileExport + " Build Project";


            foreach (Assembly editorAssembly in AssemblyManager.ExternalAssemblies.Append(typeof(Program).Assembly))
            {
                List<Type> derivedTypes = EditorUtils.GetDerivedTypes(typeof(ProjectBuilder), editorAssembly);
                foreach (Type type in derivedTypes)
                {
                    if (type.IsAbstract)
                        continue;

                    builders.Add((ProjectBuilder)Activator.CreateInstance(type));
                }
            }
        }

        protected override void Draw()
        {
            if (!Project.HasProject)
                isOpened = false;

            gui.CurrentNode.Layout(LayoutType.Row);
            gui.CurrentNode.ScaleChildren();

            using (gui.Node("Players").ExpandHeight().MaxWidth(150).Padding(10).Enter())
                DrawPlayerList();

            using (gui.Node("Main").ExpandHeight().Enter())
                DrawSettings();
        }

        private void DrawPlayerList()
        {
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.WindowRoundness);

            selectedBuilder = Math.Clamp(selectedBuilder, 0, builders.Count - 1);

            for (int i = 0; i < builders.Count; i++)
            {
                ProjectBuilder? builder = builders[i];
                using (gui.Node("Player", i).ExpandWidth().Height(EditorStylePrefs.Instance.ItemSize * 2).Enter())
                {
                    if (gui.IsNodePressed())
                        selectedBuilder = builders.IndexOf(builder);
                    else if (gui.IsNodeHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

                    // Name types are formatted as "Desktop_Player" -> "Desktop"
                    string name = builder.GetType().Name;
                    name = name.Substring(0, name.IndexOf('_'));
                    gui.Draw2D.DrawText(name, gui.CurrentNode.LayoutData.InnerRect);
                }
            }
        }

        private void DrawSettings()
        {
            gui.CurrentNode.Layout(LayoutType.Column);


            using (gui.Node("Settings").ExpandWidth().Height(Size.Percentage(1f, -75)).Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.WindowRoundness);

                object builder = builders[selectedBuilder];
                EditorGUI.PropertyGrid("Builder Settings", ref builder, EditorGUI.TargetFields.Serializable | EditorGUI.TargetFields.Properties, EditorGUI.PropertyGridConfig.NoBackground);
            }

            using (gui.Node("Butt's").ExpandWidth().Height(75).Enter())
            {
                gui.InputField("CreateInput", ref buildName, 0x100, Gui.InputFieldFlags.None, 0, 15, 400, null, EditorGUI.GetInputStyle());
                string path = Path.Combine(Project.Active.ProjectPath, "Builds", buildName);
                string displayPath = path;
                if (displayPath.Length > 55)
                    displayPath = string.Concat("...", path.AsSpan(path.Length - 55));
                gui.Draw2D.DrawText(displayPath, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(0, 45), EditorStylePrefs.Instance.LesserText);

                using (gui.Node("BuildButt").ExpandHeight().TopLeft(Offset.Percentage(1f, -175), Offset.Percentage(1f, -75)).Scale(175, 75).Enter())
                {
                    if (!string.IsNullOrEmpty(buildName) && !Directory.Exists(path))
                    {
                        if (gui.IsNodePressed())
                        {
                            builders[selectedBuilder].StartBuild(BuildProjectSetting.Instance.Scenes, new(path));
                        }
                        var col = gui.IsNodeActive() ? EditorStylePrefs.Instance.Highlighted :
                                  gui.IsNodeHovered() ? EditorStylePrefs.Instance.Highlighted * 0.8f : EditorStylePrefs.Instance.Highlighted;

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, col, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
                        gui.Draw2D.DrawText("Build", gui.CurrentNode.LayoutData.Rect);
                    }
                    else
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, Color.white * 0.4f, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
                        gui.Draw2D.DrawText("Build", gui.CurrentNode.LayoutData.Rect);
                    }
                }
            }
        }

    }
}
