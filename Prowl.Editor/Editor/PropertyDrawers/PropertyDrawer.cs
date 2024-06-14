using Prowl.Editor.Assets;
using Prowl.Editor.Utilities;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;
using System.Reflection;
using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor.PropertyDrawers
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class DrawerAttribute(Type type) : Attribute
    {
        public Type TargetType { get; private set; } = type;

        private static Dictionary<Type, PropertyDrawer> drawers = [];

        public static bool DrawProperty(Gui gui, string label, int index, Type propertyType, ref object? propertyValue, EditorGUI.PropertyGridConfig config)
        {
            if (drawers.TryGetValue(propertyType, out var drawer))
                return drawer.PropertyLayout(gui, label, index, propertyType, ref propertyValue, config);

            // No direct drawer found, try to find a drawer for the base type
            foreach (var kvp in drawers)
                if (propertyType.IsAssignableTo(kvp.Key))
                    return kvp.Value.PropertyLayout(gui, label, index, propertyType, ref propertyValue, config);

            // No drawer found
            return false;
        }

        [OnAssemblyUnload]
        public static void ClearDrawers()
        {
            drawers.Clear();
        }

        [OnAssemblyLoad]
        public static void FindAllDrawers()
        {
            foreach (Assembly editorAssembly in AssemblyManager.ExternalAssemblies.Append(typeof(Program).Assembly))
            {
                List<Type> derivedTypes = EditorUtils.GetDerivedTypes(typeof(PropertyDrawer), editorAssembly);
                foreach (var type in derivedTypes)
                {
                    var attribute = type.GetCustomAttribute<DrawerAttribute>();
                    if (attribute != null)
                    {
                        PropertyDrawer drawer = (PropertyDrawer)Activator.CreateInstance(type);
                        if (drawer == null)
                        {
                            Debug.LogError($"Failed to create instance of {type.Name}");
                            continue;
                        }
                        if (!drawers.TryAdd(attribute.TargetType, drawer))
                        {
                            Debug.LogError($"Failed to add PropertyDrawer for {attribute.TargetType.Name}, already exists?");
                            continue;
                        }
                    }
                }
            }
        }
    }

    public abstract class PropertyDrawer
    {
        public virtual bool PropertyLayout(Gui gui, string label, int index, Type propertyType, ref object? propertyValue, EditorGUI.PropertyGridConfig config)
        {
            using (gui.Node(label, index).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                // Draw line down the middle
                //var start = new Vector2(ActiveGUI.CurrentNode.LayoutData.Rect.x + ActiveGUI.CurrentNode.LayoutData.Rect.width / 2, ActiveGUI.CurrentNode.LayoutData.Rect.y);
                //var end = new Vector2(start.x, ActiveGUI.CurrentNode.LayoutData.Rect.y + ActiveGUI.CurrentNode.LayoutData.Rect.height);
                //ActiveGUI.DrawLine(start, end, GuiStyle.Borders);

                // Label
                if (!config.HasFlag(EditorGUI.PropertyGridConfig.NoLabel))
                    OnLabelGUI(gui, label);

                // Value
                using (gui.Node("#_Value").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                    return OnValueGUI(gui, $"#{label}_{index}", propertyType, ref propertyValue);
            }
        }

        public virtual void OnLabelGUI(Gui gui, string label)
        {
            using (gui.Node("#_Label").ExpandHeight().Clip().Enter())
            {
                var pos = gui.CurrentNode.LayoutData.Rect.Min;
                pos.x += 28;
                pos.y += 5;
                string pretty = RuntimeUtils.Prettify(label);
                gui.Draw2D.DrawText(pretty, pos, GuiStyle.Base8);
            }
        }

        public virtual bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
        {
            var col = GuiStyle.Red * (gui.IsNodeHovered() ? 1f : 0.8f);
            var pos = gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(0, 8);
            gui.Draw2D.DrawText(targetValue.ToString(), pos, col);
            return false;
        }
    }

    [Drawer(typeof(float))]
    public class Float_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            float val = (float)value;
            bool changed = EditorGUI.InputFloat(ID + "Val", ref val, 0, 0, Size.Percentage(1f), EditorGUI.InputFieldStyle);
            value = val;
            return changed;
        }
    }

    [Drawer(typeof(double))]
    public class Double_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            double val = (double)value;
            bool changed = EditorGUI.InputDouble(ID + "Val", ref val, 0, 0, Size.Percentage(1f), EditorGUI.InputFieldStyle);
            value = val;
            return changed;
        }
    }

    public class Integar_PropertyDrawer<T> : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            long val = Convert.ToInt64(value);
            bool changed = EditorGUI.InputLong(ID + "Val", ref val, 0, 0, Size.Percentage(1f), EditorGUI.InputFieldStyle);
            if (changed)
                value = (T)Convert.ChangeType(val, typeof(T));
            return changed;
        }
    }

    [Drawer(typeof(int))] public class Int_PropertyDrawer : Integar_PropertyDrawer<int> { }
    [Drawer(typeof(uint))] public class UInt_PropertyDrawer : Integar_PropertyDrawer<uint> { }
    [Drawer(typeof(long))] public class Long_PropertyDrawer : Integar_PropertyDrawer<long> { }
    [Drawer(typeof(ulong))] public class ULong_PropertyDrawer : Integar_PropertyDrawer<ulong> { }
    [Drawer(typeof(short))] public class Short_PropertyDrawer : Integar_PropertyDrawer<short> { }
    [Drawer(typeof(ushort))] public class UShort_PropertyDrawer : Integar_PropertyDrawer<ushort> { }
    [Drawer(typeof(byte))] public class Byte_PropertyDrawer : Integar_PropertyDrawer<byte> { }
    [Drawer(typeof(sbyte))] public class SByte_PropertyDrawer : Integar_PropertyDrawer<sbyte> { }

    [Drawer(typeof(bool))]
    public class Bool_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            bool val = (bool)value;
            bool changed = Gui.ActiveGUI.Checkbox(ID + "Val", ref val, -5, 0, out _);
            value = val;
            return changed;
        }
    }

    [Drawer(typeof(string))]
    public class String_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            string val = value as string ?? "";
            bool changed = ActiveGUI.InputField(ID, ref val, 255, InputFieldFlags.None, 0, 0, Size.Percentage(1f), null, null);
            value = val;
            return changed;
        }
    }

    [Drawer(typeof(Vector2))]
    public class Vector2_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            Vector2 val = (Vector2)value;
            bool changed = EditorGUI.InputDouble(ID + "X", ref val.x, 0, 0, 0, EditorGUI.VectorXStyle);
            changed |= EditorGUI.InputDouble(ID + "Y", ref val.y, 0, 0, 0, EditorGUI.VectorYStyle);
            value = val;
            return changed;
        }
    }

    [Drawer(typeof(Vector3))]
    public class Vector3_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            Vector3 val = (Vector3)value;
            bool changed = EditorGUI.InputDouble(ID + "X", ref val.x, 0, 0, 0, EditorGUI.VectorXStyle);
            changed |= EditorGUI.InputDouble(ID + "Y", ref val.y, 0, 0, 0, EditorGUI.VectorYStyle);
            changed |= EditorGUI.InputDouble(ID + "Z", ref val.z, 0, 0, 0, EditorGUI.VectorZStyle);
            value = val;
            return changed;
        }
    }

    [Drawer(typeof(Vector4))]
    public class Vector4_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            Vector4 val = (Vector4)value;
            bool changed = EditorGUI.InputDouble(ID + "X", ref val.x, 0, 0, 0, EditorGUI.VectorXStyle);
            changed |= EditorGUI.InputDouble(ID + "Y", ref val.y, 0, 0, 0, EditorGUI.VectorYStyle);
            changed |= EditorGUI.InputDouble(ID + "Z", ref val.z, 0, 0, 0, EditorGUI.VectorZStyle);
            changed |= EditorGUI.InputDouble(ID + "W", ref val.w, 0, 0, 0, EditorGUI.InputFieldStyle);
            value = val;
            return changed;
        }
    }

    [Drawer(typeof(Color))]
    public class Color_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            Color val = (Color)value;
            var style = new GuiStyle(EditorGUI.InputFieldStyle);
            style.TextColor = val with { a = 1 };

            double r = val.r;
            bool changed = EditorGUI.InputDouble(ID + "R", ref r, 0, 0, 0, style);
            val.r = (float)r;
            double g = val.g;
            changed |= EditorGUI.InputDouble(ID + "G", ref g, 0, 0, 0, style);
            val.g = (float)g;
            double b = val.b;
            changed |= EditorGUI.InputDouble(ID + "B", ref b, 0, 0, 0, style);
            val.b = (float)b;
            double a = val.a;
            changed |= EditorGUI.InputDouble(ID + "A", ref a, 0, 0, 0, style);
            val.a = (float)a;

            value = val;
            return changed;
        }
    }

    [Drawer(typeof(IAssetRef))]
    public class IAssetRef_PropertyDrawer : PropertyDrawer
    {
        public static ulong Selected;
        public static Guid assignedGUID;
        public static ushort assignedFileID;
        public static ulong guidAssignedToID = 0;

        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            var value = (IAssetRef)targetValue;

            bool changed = false;
            ulong assetDrawerID = ActiveGUI.CurrentNode.ID;
            if (guidAssignedToID != 0 && guidAssignedToID == assetDrawerID)
            {
                value.AssetID = assignedGUID;
                value.FileID = assignedFileID;
                assignedGUID = Guid.Empty;
                assignedFileID = 0;
                guidAssignedToID = 0;
                changed = true;
            }

            ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 1, 2);

            bool p = false;
            bool h = false;
            using (ActiveGUI.Node(ID + "Selector").MaxWidth(GuiStyle.ItemHeight).ExpandHeight().Enter())
            {
                var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
                pos += new Vector2(8, 8);
                ActiveGUI.Draw2D.DrawText(FontAwesome6.MagnifyingGlass, pos, GuiStyle.Base11 * (h ? 1f : 0.8f));
                if (ActiveGUI.IsNodePressed())
                {
                    Selected = assetDrawerID;
                    new AssetSelectorWindow(value.InstanceType, (guid, fileid) => { assignedGUID = guid; guidAssignedToID = assetDrawerID; assignedFileID = fileid; });
                }
            }

            // Thumbnail for Textures
            if (value.InstanceType == typeof(Texture2D) && value.IsAvailable)
            {
                using (ActiveGUI.Node(ID + "Thumbnail").MaxWidth(GuiStyle.ItemHeight + 5).ExpandHeight().Enter())
                {
                    var tex = value.GetInstance();
                    if (tex != null)
                    {
                        var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
                        ActiveGUI.Draw2D.DrawImage((Texture2D)tex, pos, new Vector2(GuiStyle.ItemHeight), true);
                    }
                }
            }

            using (ActiveGUI.Node(ID + "Asset").ExpandHeight().Clip().Enter())
            {
                var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
                pos += new Vector2(0, 8);
                if (value.IsExplicitNull || value.IsRuntimeResource)
                {
                    string text = value.IsExplicitNull ? "(Null)" : "(Runtime)" + value.Name;
                    var col = GuiStyle.Base11 * (h ? 1f : 0.8f);
                    if (value.IsExplicitNull)
                        col = GuiStyle.Red * (h ? 1f : 0.8f);
                    ActiveGUI.Draw2D.DrawText(text, pos, col);
                    if (ActiveGUI.IsNodePressed())
                        Selected = assetDrawerID;
                }
                else if (AssetDatabase.TryGetFile(value.AssetID, out var assetPath))
                {
                    string name = value.Name;
                    if(string.IsNullOrWhiteSpace(name))
                        name = AssetDatabase.ToRelativePath(assetPath);
                    ActiveGUI.Draw2D.DrawText(name, pos, GuiStyle.Base11 * (h ? 1f : 0.8f));
                    if (ActiveGUI.IsNodePressed())
                    {
                        Selected = assetDrawerID;
                        AssetDatabase.Ping(value.AssetID);
                    }
                }

                if (h && ActiveGUI.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
                    GlobalSelectHandler.Select(value);


                // Drag and drop support
                if (DragnDrop.Drop(out var instance, value.InstanceType))
                {
                    // SetInstance() will also set the AssetID if the instance is an asset
                    value.SetInstance(instance);
                    changed = true;
                }

                if (Selected == assetDrawerID && ActiveGUI.IsKeyDown(Silk.NET.Input.Key.Delete))
                {
                    value.AssetID = Guid.Empty;
                    value.FileID = 0;
                    changed = true;
                }
            }

            targetValue = value;
            return changed;
        }
    }

    [Drawer(typeof(EngineObject))]
    public class EngineObject_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            var value = (EngineObject)targetValue;

            bool changed = false;

            ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 1, 2);

            var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
            pos += new Vector2(0, 8);
            if (value == null)
            {
                string text = "(Null) " + targetType.Name;
                var col = GuiStyle.Red * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f);
                ActiveGUI.Draw2D.DrawText(text, pos, col);
            }
            else
            {
                ActiveGUI.Draw2D.DrawText(value.Name, pos, GuiStyle.Base11 * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f));
                if (ActiveGUI.IsNodeHovered() && ActiveGUI.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
                    GlobalSelectHandler.Select(value);
            }

            // Drag and drop support
            if (DragnDrop.Drop(out var instance, targetType))
            {
                targetValue = instance as EngineObject;
                changed = true;
            }

            // support looking for components on dropped GameObjects
            if (targetType.IsAssignableTo(typeof(MonoBehaviour)) && DragnDrop.Drop(out GameObject go))
            {
                var component = go.GetComponent(targetType);
                if (component != null)
                {
                    targetValue = component;
                    changed = true;
                }
            }

            return changed;
        }
    }

    [Drawer(typeof(Transform))]
    public class Transform_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            var value = (Transform)targetValue;

            bool changed = false;

            ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 1, 2);

            var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
            pos += new Vector2(0, 8);
            if (value == null)
            {
                string text = "(Null)" + targetType.Name;
                var col = GuiStyle.Red * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f);
                ActiveGUI.Draw2D.DrawText(text, pos, col);
            }
            else
            {
                ActiveGUI.Draw2D.DrawText(value.gameObject.Name + "(Transform)", pos, GuiStyle.Base11 * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f));
                if (ActiveGUI.IsNodeHovered() && ActiveGUI.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
                    GlobalSelectHandler.Select(value);
            }

            // Drag and drop support
            if (DragnDrop.Drop(out Transform instance))
            {
                targetValue = instance;
                changed = true;
            }

            // support looking for components on dropped GameObjects
            if (DragnDrop.Drop(out GameObject go))
            {
                targetValue = go.Transform;
                changed = true;
            }

            return changed;
        }
    }


}
