using System;
using System.Collections.Generic;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


public unsafe struct VariableLayoutReflection
{
    internal Session _session;
    internal Native.VariableLayoutReflection* _ptr;


    internal VariableLayoutReflection(Native.VariableLayoutReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }

    public VariableReflection Variable =>
        new(spReflectionVariableLayout_GetVariable(_ptr), _session);

    public string Name =>
        Variable.Name;

    public Modifier FindModifier(SlangModifierID id) =>
        Variable.FindModifier(id);

    public TypeLayoutReflection TypeLayout =>
        new(spReflectionVariableLayout_GetTypeLayout(_ptr), _session);

    public SlangParameterCategory Category =>
        TypeLayout.ParameterCategory;

    public uint CategoryCount =>
        TypeLayout.CategoryCount;

    public SlangParameterCategory GetCategoryByIndex(uint index) =>
        TypeLayout.GetCategoryByIndex(index);

    public IEnumerable<SlangParameterCategory> Categories =>
        Utility.For(CategoryCount, GetCategoryByIndex);

    public nuint GetOffset(SlangParameterCategory category) =>
        spReflectionVariableLayout_GetOffset(_ptr, category);

    public TypeReflection Type =>
        Variable.Type;

    public uint BindingIndex =>
        spReflectionParameter_GetBindingIndex(_ptr);

    public uint BindingSpace =>
        spReflectionParameter_GetBindingSpace(_ptr);

    public nuint GetBindingSpace(SlangParameterCategory category) =>
        spReflectionVariableLayout_GetSpace(_ptr, category);

    public SlangImageFormat ImageFormat =>
        spReflectionVariableLayout_GetImageFormat(_ptr);

    public string SemanticName =>
        spReflectionVariableLayout_GetSemanticName(_ptr).String;

    public nuint SemanticIndex =>
        spReflectionVariableLayout_GetSemanticIndex(_ptr);

    public SlangStage Stage =>
        spReflectionVariableLayout_getStage(_ptr);

    public VariableLayoutReflection PendingDataLayout =>
        new(spReflectionVariableLayout_getPendingDataLayout(_ptr), _session);
};
