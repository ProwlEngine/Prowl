// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Provides reflection information for shader stage input/output variables defined in a shader source module.
/// </summary>
public unsafe struct VariableLayoutReflection
{
    internal ComponentType _component;
    internal Native.VariableLayoutReflection* _ptr;


    internal VariableLayoutReflection(Native.VariableLayoutReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }

    /// <summary>
    /// Gets the variable reflection information.
    /// </summary>
    public readonly VariableReflection Variable =>
        new(spReflectionVariableLayout_GetVariable(_ptr), _component);

    /// <summary>
    /// Gets the name of the variable.
    /// </summary>
    public readonly string Name =>
        Variable.Name;

    /// <summary>
    /// Gets the type layout reflection information.
    /// </summary>
    public readonly TypeLayoutReflection TypeLayout =>
        new(spReflectionVariableLayout_GetTypeLayout(_ptr), _component);

    /// <summary>
    /// Gets the parameter category of the variable.
    /// </summary>
    public readonly ParameterCategory Category =>
        TypeLayout.ParameterCategory;

    /// <summary>
    /// Gets the number of parameter categories.
    /// </summary>
    public readonly uint CategoryCount =>
        TypeLayout.CategoryCount;

    /// <summary>
    /// Gets a parameter category by its index.
    /// </summary>
    /// <param name="index">The index of the category to retrieve.</param>
    /// <returns>The parameter category at the specified index.</returns>
    public readonly ParameterCategory GetCategoryByIndex(uint index) =>
        TypeLayout.GetCategoryByIndex(index);

    /// <summary>
    /// Gets all parameter categories.
    /// </summary>
    public readonly IEnumerable<ParameterCategory> Categories =>
        Utility.For(CategoryCount, GetCategoryByIndex);

    /// <summary>
    /// Gets the byte offset for the variable in the specified category.
    /// </summary>
    /// <param name="category">The parameter category.</param>
    /// <returns>The byte offset for the variable.</returns>
    public readonly uint GetOffset(ParameterCategory category) =>
        (uint)spReflectionVariableLayout_GetOffset(_ptr, category);

    /// <summary>
    /// Gets the type reflection information.
    /// </summary>
    public readonly TypeReflection Type =>
        Variable.Type;

    /// <summary>
    /// Gets the binding index for the variable.
    /// </summary>
    public readonly uint BindingIndex =>
        spReflectionParameter_GetBindingIndex(_ptr);

    /// <summary>
    /// Gets the binding space for the variable.
    /// </summary>
    public readonly uint BindingSpace =>
        spReflectionParameter_GetBindingSpace(_ptr);

    /// <summary>
    /// Gets the binding space for the variable in the specified category.
    /// </summary>
    /// <param name="category">The parameter category.</param>
    /// <returns>The binding space for the variable in the specified category.</returns>
    public readonly uint GetBindingSpace(ParameterCategory category) =>
        (uint)spReflectionVariableLayout_GetSpace(_ptr, category);

    /// <summary>
    /// Gets the image format for the variable.
    /// </summary>
    public readonly ImageFormat ImageFormat =>
        spReflectionVariableLayout_GetImageFormat(_ptr);

    /// <summary>
    /// Gets the semantic name for the variable.
    /// </summary>
    public readonly string SemanticName =>
        spReflectionVariableLayout_GetSemanticName(_ptr).String;

    /// <summary>
    /// Gets the semantic index for the variable.
    /// </summary>
    public readonly uint SemanticIndex =>
        (uint)spReflectionVariableLayout_GetSemanticIndex(_ptr);

    /// <summary>
    /// Gets the shader stage associated with the variable.
    /// </summary>
    public readonly ShaderStage Stage =>
        spReflectionVariableLayout_getStage(_ptr);

    /// <summary>
    /// Gets the pending data layout for the variable.
    /// </summary>
    public readonly VariableLayoutReflection PendingDataLayout =>
        new(spReflectionVariableLayout_getPendingDataLayout(_ptr), _component);
}
