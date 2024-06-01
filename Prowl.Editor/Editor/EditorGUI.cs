using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using System.ComponentModel;
using System.Reflection;
using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor
{
    public static class EditorGUI
    {
        public static bool QuickButton(string label)
        {
            var g = ActiveGUI;
            using (g.ButtonNode(label, out var p, out var h).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                g.DrawRect(g.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 1, 10);

                g.DrawText(label, g.CurrentNode.LayoutData.Rect, GuiStyle.Base8);

                var interact = g.GetInteractable();
                if (interact.TakeFocus())
                {
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 10);
                    return true;
                }

                if (interact.IsHovered())
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5 * 0.5f, 10);


                return false;
            }
        }

        public static bool InputDouble(string ID, ref double value, Offset x, Offset y, Size width, GuiStyle? style = null)
        {
            string textValue = value.ToString();
            var changed = ActiveGUI.InputField(ID, ref textValue, 16, InputFieldFlags.NumbersOnly, x, y, width, null, style);
            if (changed && Double.TryParse(textValue, out value)) return true;
            return false;
        }
        public static bool InputFloat(string ID, ref float value, Offset x, Offset y, Size width, GuiStyle? style = null)
        {
            string textValue = value.ToString();
            var changed = ActiveGUI.InputField(ID, ref textValue, 16, InputFieldFlags.NumbersOnly, x, y, width, null, style);
            if (changed && float.TryParse(textValue, out value)) return true;
            return false;
        }

        public static bool InputLong(string ID, ref long value, Offset x, Offset y, Size width, GuiStyle? style = null)
        {
            string textValue = value.ToString();
            var changed = ActiveGUI.InputField(ID, ref textValue, 16, InputFieldFlags.NumbersOnly, x, y, width, null, style);
            if (changed && long.TryParse(textValue, out value)) return true;
            return false;
        }


        static GuiStyle VectorXStyle = new GuiStyle() { TextColor = GuiStyle.Red, WidgetColor = GuiStyle.WindowBackground, Border = GuiStyle.Borders, BorderThickness = 1 };
        static GuiStyle VectorYStyle = new GuiStyle() { TextColor = GuiStyle.Green, WidgetColor = GuiStyle.WindowBackground, Border = GuiStyle.Borders, BorderThickness = 1 };
        static GuiStyle VectorZStyle = new GuiStyle() { TextColor = GuiStyle.Blue, WidgetColor = GuiStyle.WindowBackground, Border = GuiStyle.Borders, BorderThickness = 1 };

        static GuiStyle InputFieldStyle = new GuiStyle() { TextColor = GuiStyle.Emerald, WidgetColor = GuiStyle.WindowBackground, Border = GuiStyle.Borders, BorderThickness = 1 };

        static LayoutNode Property(string ID)
        {
            var g = ActiveGUI;
            return g.Node(ID + "_Content").ExpandWidth().Height(GuiStyle.ItemHeight);
        }

        public static bool Property_Vector2(string ID, ref Vector2 value)
        {
            using (Property(ID).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                bool changed = InputDouble(ID + "X", ref value.x, 0, 0, 0, VectorXStyle);
                changed |= InputDouble(ID + "Y", ref value.y, 0, 0, 0, VectorYStyle);
                return changed;
            }
        }

        public static bool Property_Vector3(string ID, ref Vector3 value)
        {
            using (Property(ID).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                bool changed = InputDouble(ID + "X", ref value.x, 0, 0, 0, VectorXStyle);
                changed |= InputDouble(ID + "Y", ref value.y, 0, 0, 0, VectorYStyle);
                changed |= InputDouble(ID + "Z", ref value.z, 0, 0, 0, VectorZStyle);
                return changed;
            }
        }

        public static bool Property_Vector4(string ID, ref Vector4 value)
        {
            using (Property(ID).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                bool changed = InputDouble(ID + "X", ref value.x, 0, 0, 0, VectorXStyle);
                changed |= InputDouble(ID + "Y", ref value.y, 0, 0, 0, VectorYStyle);
                changed |= InputDouble(ID + "Z", ref value.z, 0, 0, 0, VectorZStyle);
                changed |= InputDouble(ID + "W", ref value.w, 0, 0, 0, InputFieldStyle);
                return changed;
            }
        }

        public static bool Property_Double(string ID, ref double value)
        {
            using (Property(ID).Enter())
                return InputDouble(ID + "Val", ref value, 0, 0, Size.Percentage(1f), InputFieldStyle);
        }

        public static bool Property_Float(string ID, ref float value)
        {
            using (Property(ID).Enter())
                return InputFloat(ID + "Val", ref value, 0, 0, Size.Percentage(1f), InputFieldStyle);
        }

        public static bool PropertyIntegar<T>(string ID, ref T value) where T : struct
        {
            using (Property(ID).Enter())
            {
                long val = Convert.ToInt64(value);
                bool changed = InputLong(ID + "Val", ref val, 0, 0, Size.Percentage(1f), InputFieldStyle);
                if(changed)
                    value = (T)Convert.ChangeType(val, typeof(T));
                return changed;
            }
        }

        public static bool Property_Bool(string ID, ref bool value)
        {
            using (Property(ID).Enter())
                return Gui.ActiveGUI.Checkbox(ID + "Val", ref value, -5, 0, out _);
        }
        public static bool Property_Color(string ID, ref Color value)
        {
            using (Property(ID).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                double r = value.r;
                bool changed = InputDouble(ID + "R", ref r, 0, 0, 0, VectorXStyle);
                value.r = (float)r;
                double g = value.g;
                changed |= InputDouble(ID + "G", ref g, 0, 0, 0, VectorYStyle);
                value.g = (float)g;
                double b = value.b;
                changed |= InputDouble(ID + "B", ref b, 0, 0, 0, VectorZStyle);
                value.b = (float)b;

                using (ActiveGUI.Node(ID + "ColIcon").MaxWidth(22.5f).Height(22.5f).Enter())
                {
                    ActiveGUI.DrawRectFilled(ActiveGUI.CurrentNode.LayoutData.Rect, value);
                }

                return changed;
            }
        }

        #region Asset Property

        public static ulong Selected;
        public static Guid assignedGUID;
        public static ushort assignedFileID;
        public static ulong guidAssignedToID = 0;

        public static bool Property_Asset(string ID, ref IAssetRef value)
        {
            using (Property(ID).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
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

                ActiveGUI.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 1, 2);

                bool p = false;
                bool h = false;
                using (ActiveGUI.ButtonNode(ID + "Selector", out p, out h).MaxWidth(GuiStyle.ItemHeight).ExpandHeight().Enter())
                {
                    var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
                    pos += new Vector2(8, 8);
                    ActiveGUI.DrawText(FontAwesome6.MagnifyingGlass, pos, GuiStyle.Base11 * (h ? 1f : 0.8f));
                    if (p)
                    {
                        Selected = assetDrawerID;
                        new AssetSelectorWindow(value.InstanceType, (guid, fileid) => { assignedGUID = guid; guidAssignedToID = assetDrawerID; assignedFileID = fileid; });
                    }
                }

                using (ActiveGUI.ButtonNode(ID + "Asset", out p, out h).ExpandHeight().Clip().Enter())
                {
                    var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
                    pos += new Vector2(0, 8);
                    if (value.IsExplicitNull || value.IsRuntimeResource)
                    {
                        string text = value.IsExplicitNull ? "(Null)" : "(Runtime)" + value.Name;
                        var col = GuiStyle.Base11 * (h ? 1f : 0.8f);
                        if(value.IsExplicitNull)
                            col = GuiStyle.Red * (h ? 1f : 0.8f);
                        ActiveGUI.DrawText(text, pos, col);
                        if (p)
                            Selected = assetDrawerID;
                    }
                    else if (AssetDatabase.TryGetFile(value.AssetID, out var assetPath))
                    {
                        ActiveGUI.DrawText(AssetDatabase.ToRelativePath(assetPath), pos, GuiStyle.Base11 * (h ? 1f : 0.8f));
                        if (p)
                        {
                            Selected = assetDrawerID;
                            AssetDatabase.Ping(value.AssetID);
                        }
                    }

                    if(h && ActiveGUI.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
                        GlobalSelectHandler.Select(value);


                    // Drag and drop support
                    if (DragnDrop.Drop(out var instance, value.InstanceType))
                    {
                        // SetInstance() will also set the AssetID if the instance is an asset
                        value.SetInstance(instance);
                        changed = true;
                    }

                    if(Selected == assetDrawerID && ActiveGUI.IsKeyDown(Silk.NET.Input.Key.Delete))
                    {
                        value.AssetID = Guid.Empty;
                        value.FileID = 0;
                        changed = true;
                    }
                }

                return changed;
            }
        }

        #endregion

        // TODO, Widgets for:
        // Lists/Arrays
        // LayerMask


        #region PropertyGrid
        public enum TargetFields
        {
            Serializable,
            Public,
            Private,
            All
        }

        [Flags]
        public enum PropertyGridConfig
        {
            None = 0,
            NoHeader = 1,
            NoLabel = 2,
            NoTooltip = 4,
            NoBorder = 8,
            NoBackground = 16,
            NoCollapse = 64,

            Debug = 128
        }

        public static bool PropertyGrid(string name, ref object target, TargetFields targetFields, PropertyGridConfig config = PropertyGridConfig.None)
        {
            bool changed = false;
            if (target == null) return changed;

            LayoutNode node;
            using ((node = ActiveGUI.Node(name)).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                // Draw the Background & Borders
                if (!config.HasFlag(PropertyGridConfig.NoBackground))
                    ActiveGUI.DrawRectFilled(node.LayoutData.Rect, GuiStyle.FrameBGColor);

                if (!config.HasFlag(PropertyGridConfig.NoBorder))
                    ActiveGUI.DrawRect(node.LayoutData.Rect, GuiStyle.Borders);

                if (!config.HasFlag(PropertyGridConfig.NoHeader))
                {
                    node.PaddingTop(GuiStyle.ItemHeight);

                    // Draw the header
                    var headerRect = new Rect(node.LayoutData.GlobalPosition, new Vector2(node.LayoutData.Rect.width, GuiStyle.ItemHeight));
                    if (!config.HasFlag(PropertyGridConfig.NoBackground))
                        ActiveGUI.DrawRectFilled(headerRect, GuiStyle.Indigo);

                    ActiveGUI.DrawText(UIDrawList.DefaultFont, name, 20, headerRect, GuiStyle.Base11, false);
                }

                DrawProperties(ref target, targetFields, config);

            }


            return changed;
        }

        private static void DrawProperties(ref object target, TargetFields targetFields, PropertyGridConfig config = PropertyGridConfig.None)
        {
            // Get the target fields
            var fields = targetFields switch {
                TargetFields.Serializable => RuntimeUtils.GetSerializableFields(target),
                TargetFields.Public => target.GetType().GetFields(),
                TargetFields.Private => target.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
                _ => target.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            };

            // Draw the properties
            int i = 0;
            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                var fieldValue = field.GetValue(target);

                // if has HideInInspector ignore
                if (field.GetCustomAttributes(typeof(HideInInspectorAttribute), true).Length > 0)
                    continue;

                // Draw the property
                DrawProperty(i++, field.Name, fieldType, ref fieldValue, config);

                // Update the value
                field.SetValue(target, fieldValue);
            }

        }

        public static bool DrawProperty<T>(int index, string name, ref T? value, PropertyGridConfig config = PropertyGridConfig.None)
        {
            object? obj = value;
            bool changed = DrawProperty(index, name, typeof(T), ref obj, config);
            value = (T?)obj;
            return changed;
        }

        public static bool DrawProperty(int index, string name, Type fieldType, ref object? fieldValue, PropertyGridConfig config = PropertyGridConfig.None)
        {
            // Create the root Node for this property
            using (ActiveGUI.Node(name, index).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                // Draw line down the middle
                //var start = new Vector2(ActiveGUI.CurrentNode.LayoutData.Rect.x + ActiveGUI.CurrentNode.LayoutData.Rect.width / 2, ActiveGUI.CurrentNode.LayoutData.Rect.y);
                //var end = new Vector2(start.x, ActiveGUI.CurrentNode.LayoutData.Rect.y + ActiveGUI.CurrentNode.LayoutData.Rect.height);
                //ActiveGUI.DrawLine(start, end, GuiStyle.Borders);

                // Label
                if (!config.HasFlag(PropertyGridConfig.NoLabel))
                {
                    using (ActiveGUI.Node("#_Label").ExpandHeight().Clip().Enter())
                    {
                        var pos = ActiveGUI.CurrentNode.LayoutData.Rect.Min;
                        //bool hasHeader = !config.HasFlag(PropertyGridConfig.NoHeader);
                        //pos.x += hasHeader ? 28 : 5;
                        pos.x += 28;
                        pos.y += 5;
                        ActiveGUI.DrawText(name, pos, GuiStyle.Base8);
                    }
                }

                // Value
                using (ActiveGUI.Node("#_Value").ExpandHeight().Enter())
                {
                    bool changed = false;
                    if (fieldValue == null)
                    {
                        ActiveGUI.DrawText(UIDrawList.DefaultFont, "null", 20, Gui.ActiveGUI.CurrentNode.LayoutData.Rect, GuiStyle.Base11, false);
                    }
                    else
                    {
                        if (fieldValue is IAssetRef assetVal)
                        {
                            changed |= Property_Asset("Prop" + index, ref assetVal);
                            fieldValue = assetVal;
                        }
                        else if (fieldValue is Color cVal)
                        {
                            changed |= Property_Color("Prop" + index, ref cVal);
                            fieldValue = cVal;
                        }
                        else if (fieldValue is bool bVal)
                        {
                            changed |= Property_Bool("Prop" + index, ref bVal);
                            fieldValue = bVal;
                        }
                        else if (fieldValue is float fVal)
                        {
                            changed |= Property_Float("Prop" + index, ref fVal);
                            fieldValue = fVal;
                        }
                        else if (fieldValue is double dVal)
                        {
                            changed |= Property_Double("Prop" + index, ref dVal);
                            fieldValue = dVal;
                        }
                        else if (fieldValue is byte byteVal)
                        {
                            changed |= PropertyIntegar("Prop" + index, ref byteVal);
                            fieldValue = byteVal;
                        }
                        else if (fieldValue is short shortVal)
                        {
                            changed |= PropertyIntegar("Prop" + index, ref shortVal);
                            fieldValue = shortVal;
                        }
                        else if (fieldValue is int intVal)
                        {
                            changed |= PropertyIntegar("Prop" + index, ref intVal);
                            fieldValue = intVal;
                        }
                        else if (fieldValue is long longVal)
                        {
                            changed |= PropertyIntegar("Prop" + index, ref longVal);
                            fieldValue = longVal;
                        }
                        else if (fieldValue is Vector3 v3Val)
                        {
                            changed |= Property_Vector3("Prop" + index, ref v3Val);
                            fieldValue = v3Val;
                        }
                        else if (fieldValue is Enum)
                        {
                            Enum enumValue = (Enum)fieldValue;
                            Array values = Enum.GetValues(fieldType);
                            int selectedIndex = Array.IndexOf(values, enumValue);

                            string[] names = new string[values.Length];
                            for (int i = 0; i < values.Length; i++)
                            {
                                FieldInfo fieldInfo = fieldType.GetField(values.GetValue(i).ToString());
                                TextAttribute attribute = fieldInfo.GetCustomAttribute<TextAttribute>();
                                names[i] = attribute != null ? attribute.text : fieldInfo.Name;
                            }

                            changed |= ActiveGUI.Combo("#_PropID", "#_PropPopupID", ref selectedIndex, names, 0, 0, Size.Percentage(1f), GuiStyle.ItemHeight);

                            if (selectedIndex >= 0 && selectedIndex < values.Length)
                            {
                                fieldValue = values.GetValue(selectedIndex);
                            }
                        }
                        else
                        {
                            var pos = ActiveGUI.CurrentNode.LayoutData.Rect.Min;
                            pos.x += 1;
                            pos.y += 5;
                            ActiveGUI.DrawText("Unsupported Type", pos, GuiStyle.Base11);
                        }
                    }

                    return changed;
                }
            }
        }

        #endregion

    }
}