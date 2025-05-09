using System;
using System.Runtime.InteropServices;

using SlangUInt32 = uint;
using SlangInt32 = int;

using SlangInt = nint;
using SlangUInt = nuint;

using SlangBool = bool;

using static Prowl.Slang.Native.SlangNative_Dep;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
public unsafe struct CompilerOptionValue()
{
    public CompilerOptionValueKind kind = CompilerOptionValueKind.Int;
    public int intValue0 = 0;
    public int intValue1 = 0;
    public ConstU8Str stringValue0;
    public ConstU8Str stringValue1;
}


[StructLayout(LayoutKind.Sequential)]
public struct CompilerOptionEntry
{
    CompilerOptionName name;
    CompilerOptionValue value;
}


/* An interface to provide a mechanism to cast, that doesn't require ref counting
and doesn't have to return a pointer to a IUnknown derived class */
[UUID(0x87ede0e1, 0x4852, 0x44b0, 0x8b, 0xf2, 0xcb, 0x31, 0x87, 0x4d, 0xe2, 0x39)]
public unsafe interface ISlangCastable : IUnknown
{
    /// Can be used to cast to interfaces without reference counting.
    /// Also provides access to internal implementations, when they provide a guid
    /// Can simulate a 'generated' interface as long as kept in scope by cast from.
    void* CastAs(ref Guid guid);
}


[UUID(0x1ec36168, 0xe9f4, 0x430d, 0xbb, 0x17, 0x4, 0x8a, 0x80, 0x46, 0xb3, 0x1f)]
public unsafe interface ISlangClonable : ISlangCastable
{
    /// Note the use of guid is for the desired interface/object.
    /// The object is returned *not* ref counted. Any type that can implements the interface,
    /// derives from ICastable, and so (not withstanding some other issue) will always return
    /// an ICastable interface which other interfaces/types are accessible from via castAs
    void* Clone(ref Guid guid);
}


/** A "blob" of binary data.

This interface definition is compatible with the `ID3DBlob` and `ID3D10Blob` interfaces.
*/
[UUID(0x8BA5FB08, 0x5195, 0x40e2, 0xAC, 0x58, 0x0D, 0x98, 0x9C, 0x3A, 0x01, 0x02)]
public unsafe interface ISlangBlob : IUnknown
{
    void* GetBufferPointer();
    nuint GetBufferSize();
}


/* Can be requested from ISlangCastable cast to indicate the contained chars are null
 * terminated.
 */
[UUID(0xbe0db1a8, 0x3594, 0x4603, 0xa7, 0x8b, 0xc4, 0x86, 0x84, 0x30, 0xdf, 0xbb)]
interface SlangTerminatedChars
{
    //      operator byte*() const { return chars; }
    //      char chars[1];
};

/** A (real or virtual) file system.

Slang can make use of this interface whenever it would otherwise try to load files
from disk, allowing applications to hook and/or override filesystem access from
the compiler.

It is the responsibility of
the caller of any method that returns a ISlangBlob to release the blob when it is no
longer used (using 'release').
*/

[UUID(0x003A09FC, 0x3A4D, 0x4BA0, 0xAD, 0x60, 0x1F, 0xD8, 0x63, 0xA9, 0x15, 0xAB)]
public unsafe interface ISlangFileSystem : ISlangCastable
{
    /** Load a file from `path` and return a blob of its contents
    @param path The path to load from, as a null-terminated UTF-8 string.
    @param outBlob A destination pointer to receive the blob of the file contents.
    @returns A `SlangResult` to indicate success or failure in loading the file.

    NOTE! This is a *binary* load - the blob should contain the exact same bytes
    as are found in the backing file.

    If load is successful, the implementation should create a blob to hold
    the file's content, store it to `outBlob`, and return 0.
    If the load fails, the implementation should return a failure status
    (any negative value will do).
    */
    SlangResult LoadFile(ConstU8Str path, out ISlangBlob* outBlob);
}


// typedef void (* SlangFuncPtr) (void);

/**
(DEPRECATED) ISlangSharedLibrary
*/
[UUID(0x9c9d5bc5, 0xeb61, 0x496f, 0x80, 0xd7, 0xd1, 0x47, 0xc4, 0xa2, 0x37, 0x30)]
public unsafe interface ISlangSharedLibrary_Dep1 : IUnknown
{
    void* FindSymbolAddressByName(ConstU8Str name);
}

/** An interface that can be used to encapsulate access to a shared library. An implementation
does not have to implement the library as a shared library
*/
[UUID(0x70dbc7c4, 0xdc3b, 0x4a07, 0xae, 0x7e, 0x75, 0x2a, 0xf6, 0xa8, 0x15, 0x55)]
public unsafe interface ISlangSharedLibrary : ISlangCastable
{
    /** Get a symbol by name. If the library is unloaded will only return null.
    @param name The name of the symbol
    @return The pointer related to the name or null if not found
    */
    void* FindSymbolAddressByName(ConstU8Str name);
}


[UUID(0x6264ab2b, 0xa3e8, 0x4a06, 0x97, 0xf1, 0x49, 0xbc, 0x2d, 0x2a, 0xb1, 0x4d)]
public unsafe interface ISlangSharedLibraryLoader : IUnknown
{
    /** Load a shared library. In typical usage the library name should *not* contain any
    platform specific elements. For example on windows a dll name should *not* be passed with a
    '.dll' extension, and similarly on linux a shared library should *not* be passed with the
    'lib' prefix and '.so' extension
    @path path The unadorned filename and/or path for the shared library
    @ param sharedLibraryOut Holds the shared library if successfully loaded */
    SlangResult LoadSharedLibrary(ConstU8Str path, out ISlangSharedLibrary* sharedLibraryOut);
}


/** An extended file system abstraction.

Implementing and using this interface over ISlangFileSystem gives much more control over how
paths are managed, as well as how it is determined if two files 'are the same'.

All paths as input byte*, or output as ISlangBlobs are always encoded as UTF-8 strings.
Blobs that contain strings are always zero terminated.
*/
[UUID(0x5fb632d2, 0x979d, 0x4481, 0x9f, 0xee, 0x66, 0x3c, 0x3f, 0x14, 0x49, 0xe1)]
public unsafe interface ISlangFileSystemExt : ISlangFileSystem
{
    /** Get a uniqueIdentity which uniquely identifies an object of the file system.

    Given a path, returns a 'uniqueIdentity' which ideally is the same value for the same object
    on the file system.

    The uniqueIdentity is used to compare if two paths are the same - which amongst other things
    allows Slang to cache source contents internally. It is also used for #pragma once
    functionality.

    A *requirement* is for any implementation is that two paths can only return the same
    uniqueIdentity if the contents of the two files are *identical*. If an implementation breaks
    this constraint it can produce incorrect compilation. If an implementation cannot *strictly*
    identify *the same* files, this will only have an effect on #pragma once behavior.

    The string for the uniqueIdentity is held zero terminated in the ISlangBlob of
    outUniqueIdentity.

    Note that there are many ways a uniqueIdentity may be generated for a file. For example it
    could be the 'canonical path' - assuming it is available and unambiguous for a file system.
    Another possible mechanism could be to store the filename combined with the file date time
    to uniquely identify it.

    The client must ensure the blob be released when no longer used, otherwise memory will leak.

    NOTE! Ideally this method would be called 'getPathUniqueIdentity' but for historical reasons
    and backward compatibility it's name remains with 'File' even though an implementation
    should be made to work with directories too.

    @param path
    @param outUniqueIdentity
    @returns A `SlangResult` to indicate success or failure getting the uniqueIdentity.
    */
    SlangResult GetFileUniqueIdentity(ConstU8Str path, out ISlangBlob* outUniqueIdentity);

    /** Calculate a path combining the 'fromPath' with 'path'

    The client must ensure the blob be released when no longer used, otherwise memory will leak.

    @param fromPathType How to interpret the from path - as a file or a directory.
    @param fromPath The from path.
    @param path Path to be determined relative to the fromPath
    @param pathOut Holds the string which is the relative path. The string is held in the blob
    zero terminated.
    @returns A `SlangResult` to indicate success or failure in loading the file.
    */
    SlangResult CalcCombinedPath(
        SlangPathType fromPathType,
        ConstU8Str fromPath,
        ConstU8Str path,
        out ISlangBlob* pathOut);

    /** Gets the type of path that path is on the file system.
    @param path
    @param pathTypeOut
    @returns SLANG_OK if located and type is known, else an error. SLANG_E_NOT_FOUND if not
    found.
    */
    SlangResult GetPathType(ConstU8Str path, out SlangPathType pathTypeOut);

    /** Get a path based on the kind.

    @param kind The kind of path wanted
    @param path The input path
    @param outPath The output path held in a blob
    @returns SLANG_OK if successfully simplified the path (SLANG_E_NOT_IMPLEMENTED if not
    implemented, or some other error code)
    */
    SlangResult GetPath(PathKind kind, ConstU8Str path, out ISlangBlob* outPath);

    /** Clears any cached information */
    void ClearCache();

    /** Enumerate the contents of the path

    Note that for normal Slang operation it isn't necessary to enumerate contents this can
    return SLANG_E_NOT_IMPLEMENTED.

    @param The path to enumerate
    @param callback This callback is called for each entry in the path.
    @param userData This is passed to the callback
    @returns SLANG_OK if successful
    */
    SlangResult EnumeratePathContents(ConstU8Str path, void* callback, void* userData);

    /** Returns how paths map to the OS file system

    @returns OSPathKind that describes how paths map to the Operating System file system
    */
    OSPathKind GetOSPathKind();
}


[UUID(0xa058675c, 0x1d65, 0x452a, 0x84, 0x58, 0xcc, 0xde, 0xd1, 0x42, 0x71, 0x5)]
public unsafe interface ISlangMutableFileSystem : ISlangFileSystemExt
{
    /** Write data to the specified path.

    @param path The path for data to be saved to
    @param data The data to be saved
    @param size The size of the data in bytes
    @returns SLANG_OK if successful (SLANG_E_NOT_IMPLEMENTED if not implemented, or some other
    error code)
    */
    SlangResult SaveFile(ConstU8Str path, void* data, nuint size);

    /** Write data in the form of a blob to the specified path.

    Depending on the implementation writing a blob might be faster/use less memory. It is
    assumed the blob is *immutable* and that an implementation can reference count it.

    It is not guaranteed loading the same file will return the *same* blob - just a blob with
    same contents.

    @param path The path for data to be saved to
    @param dataBlob The data to be saved
    @returns SLANG_OK if successful (SLANG_E_NOT_IMPLEMENTED if not implemented, or some other
    error code)
    */
    SlangResult SaveFileBlob(ConstU8Str path, ISlangBlob* dataBlob);

    /** Remove the entry in the path (directory of file). Will only delete an empty directory,
    if not empty will return an error.

    @param path The path to remove
    @returns SLANG_OK if successful
    */
    SlangResult Remove(ConstU8Str path);

    /** Create a directory.

    The path to the directory must exist

    @param path To the directory to create. The parent path *must* exist otherwise will return
    an error.
    @returns SLANG_OK if successful
    */
    SlangResult CreateDirectory(ConstU8Str path);
}


/** A stream typically of text, used for outputting diagnostic as well as other information.
 */
[UUID(0xec457f0e, 0x9add, 0x4e6b, 0x85, 0x1c, 0xd7, 0xfa, 0x71, 0x6d, 0x15, 0xfd)]
public unsafe interface ISlangWriter : IUnknown
{
    /** Begin an append buffer.
    NOTE! Only one append buffer can be active at any time.
    @param maxNumChars The maximum of chars that will be appended
    @returns The start of the buffer for appending to. */
    byte* BeginAppendBuffer(nuint maxNumChars);

    /** Ends the append buffer, and is equivalent to a write of the append buffer.
    NOTE! That an endAppendBuffer is not necessary if there are no characters to write.
    @param buffer is the start of the data to append and must be identical to last value
    returned from beginAppendBuffer
    @param numChars must be a value less than or equal to what was returned from last call to
    beginAppendBuffer
    @returns Result, will be SLANG_OK on success */
    SlangResult EndAppendBuffer(byte* buffer, nuint numChars);

    /** Write text to the writer
    @param chars The characters to write out
    @param numChars The amount of characters
    @returns SLANG_OK on success */
    SlangResult Write(ConstU8Str chars, nuint numChars);

    /** Flushes any content to the output */
    void Flush();

    /** Determines if the writer stream is to the console, and can be used to alter the output
    @returns Returns true if is a console writer */
    SlangBool IsConsole();

    /** Set the mode for the writer to use
    @param mode The mode to use
    @returns SLANG_OK on success */
    SlangResult SetMode(SlangWriterMode mode);
}


[UUID(0x197772c7, 0x0155, 0x4b91, 0x84, 0xe8, 0x66, 0x68, 0xba, 0xff, 0x06, 0x19)]
public unsafe interface ISlangProfiler : IUnknown
{
    nuint GetEntryCount();
    ConstU8Str GetEntryName(uint index);
    long GetEntryTimeMS(uint index);
    uint GetEntryInvocationTimes(uint index);
}


[StructLayout(LayoutKind.Explicit)]
public unsafe struct GenericArgReflection
{
    [FieldOffset(0)]
    public TypeReflection* typeVal;

    [FieldOffset(0)]
    public long intVal;

    [FieldOffset(0)]
    public bool boolVal;
}


[StructLayout(LayoutKind.Explicit)]
public unsafe struct Attribute
{
    Attribute* ptr => (Attribute*)Unsafe.AsPointer<Attribute>(ref this);

    public ConstU8Str getName()
    {
        return spReflectionUserAttribute_GetName(ptr);
    }

    public uint getArgumentCount()
    {
        return spReflectionUserAttribute_GetArgumentCount(
            ptr);
    }

    public TypeReflection* getArgumentType(uint index)
    {
        return spReflectionUserAttribute_GetArgumentType(
            ptr,
            index);
    }

    public SlangResult getArgumentValueInt(uint index, int* value)
    {
        return spReflectionUserAttribute_GetArgumentValueInt(
            ptr,
            index,
            value);
    }

    public SlangResult getArgumentValueFloat(uint index, float* value)
    {
        return spReflectionUserAttribute_GetArgumentValueFloat(
            ptr,
            index,
            value);
    }

    public ConstU8Str getArgumentValueString(uint index, out nuint outSize)
    {
        return spReflectionUserAttribute_GetArgumentValueString(
            ptr,
            index,
            out outSize);
    }
}


[StructLayout(LayoutKind.Sequential)]
public unsafe struct TypeReflection
{
    TypeReflection* ptr => (TypeReflection*)Unsafe.AsPointer<TypeReflection>(ref this);

    public SlangTypeKind getKind() { return spReflectionType_GetKind(ptr); }

    // only useful if `getKind() == Kind::Struct`
    public uint getFieldCount()
    {
        return spReflectionType_GetFieldCount(ptr);
    }

    public VariableReflection* getFieldByIndex(uint index)
    {
        return spReflectionType_GetFieldByIndex(ptr, index);
    }

    public bool isArray() { return getKind() == SlangTypeKind.ARRAY; }

    public TypeReflection* unwrapArray()
    {
        TypeReflection* type = ptr;
        while (type->isArray())
        {
            type = type->getElementType();
        }
        return type;
    }

    // only useful if `getKind() == Kind::Array`
    public nuint getElementCount()
    {
        return spReflectionType_GetElementCount(ptr);
    }

    public nuint getTotalArrayElementCount()
    {
        if (!isArray())
            return 0;

        nuint result = 1;
        TypeReflection* type = ptr;
        for (; ; )
        {
            if (!type->isArray())
                return result;

            result *= type->getElementCount();
            type = type->getElementType();
        }
    }

    public TypeReflection* getElementType()
    {
        return spReflectionType_GetElementType(ptr);
    }

    public uint getRowCount() { return spReflectionType_GetRowCount(ptr); }

    public uint getColumnCount()
    {
        return spReflectionType_GetColumnCount(ptr);
    }

    public SlangScalarType getScalarType()
    {
        return spReflectionType_GetScalarType(ptr);
    }

    public TypeReflection* getResourceResultType()
    {
        return spReflectionType_GetResourceResultType(ptr);
    }

    public SlangResourceShape getResourceShape()
    {
        return spReflectionType_GetResourceShape(ptr);
    }

    public SlangResourceAccess getResourceAccess()
    {
        return spReflectionType_GetResourceAccess(ptr);
    }

    public ConstU8Str getName()
    {
        return spReflectionType_GetName(ptr);
    }

    public SlangResult getFullName(ISlangBlob** outNameBlob)
    {
        return spReflectionType_GetFullName(ptr, outNameBlob);
    }

    public uint getUserAttributeCount()
    {
        return spReflectionType_GetUserAttributeCount(ptr);
    }

    public Attribute* getUserAttributeByIndex(uint index)
    {
        return spReflectionType_GetUserAttribute(ptr, index);
    }

    public Attribute* findAttributeByName(ConstU8Str name)
    {
        return spReflectionType_FindUserAttributeByName(
            ptr,
            name);
    }

    public Attribute* findUserAttributeByName(ConstU8Str name) { return findAttributeByName(name); }

    public TypeReflection* applySpecializations(GenericReflection* generic)
    {
        return spReflectionType_applySpecializations(
            ptr,
            generic);
    }

    public GenericReflection* getGenericContainer()
    {
        return spReflectionType_GetGenericContainer(ptr);
    }
};


public unsafe struct TypeLayoutReflection
{
    TypeLayoutReflection* ptr => (TypeLayoutReflection*)Unsafe.AsPointer<TypeLayoutReflection>(ref this);

    public TypeReflection* getType()
    {
        return spReflectionTypeLayout_GetType(ptr);
    }

    public SlangTypeKind getKind()
    {
        return spReflectionTypeLayout_getKind(ptr);
    }

    public nuint getSize(SlangParameterCategory category)
    {
        return spReflectionTypeLayout_GetSize(ptr, category);
    }

    public nuint getStride(SlangParameterCategory category)
    {
        return spReflectionTypeLayout_GetStride(ptr, category);
    }

    public int getAlignment(SlangParameterCategory category)
    {
        return spReflectionTypeLayout_getAlignment(ptr, category);
    }

    public uint getFieldCount()
    {
        return spReflectionTypeLayout_GetFieldCount(ptr);
    }

    public VariableLayoutReflection* getFieldByIndex(uint index)
    {
        return spReflectionTypeLayout_GetFieldByIndex(ptr, index);
    }

    public SlangInt findFieldIndexByName(ConstU8Str nameBegin, ConstU8Str nameEnd)
    {
        return spReflectionTypeLayout_findFieldIndexByName(
            ptr,
            nameBegin,
            nameEnd);
    }

    public VariableLayoutReflection* getExplicitCounter()
    {
        return spReflectionTypeLayout_GetExplicitCounter(ptr);
    }

    public bool isArray() { return getType()->isArray(); }

    public TypeLayoutReflection* unwrapArray()
    {
        TypeLayoutReflection* typeLayout = ptr;

        while (typeLayout->isArray())
            typeLayout = typeLayout->getElementTypeLayout();

        return typeLayout;
    }

    // only useful if `getKind() == Kind::Array`
    public nuint getElementCount() { return getType()->getElementCount(); }

    public nuint getTotalArrayElementCount() { return getType()->getTotalArrayElementCount(); }

    public nuint getElementStride(SlangParameterCategory category)
    {
        return spReflectionTypeLayout_GetElementStride(ptr, category);
    }

    public TypeLayoutReflection* getElementTypeLayout()
    {
        return spReflectionTypeLayout_GetElementTypeLayout(
            ptr);
    }

    public VariableLayoutReflection* getElementVarLayout()
    {
        return spReflectionTypeLayout_GetElementVarLayout(
            ptr);
    }

    public VariableLayoutReflection* getContainerVarLayout()
    {
        return spReflectionTypeLayout_getContainerVarLayout(
            ptr);
    }

    // How is this type supposed to be bound?
    public SlangParameterCategory getParameterCategory()
    {
        return spReflectionTypeLayout_GetParameterCategory(
            ptr);
    }

    public uint getCategoryCount()
    {
        return spReflectionTypeLayout_GetCategoryCount(ptr);
    }

    public SlangParameterCategory getCategoryByIndex(uint index)
    {
        return spReflectionTypeLayout_GetCategoryByIndex(
            ptr,
            index);
    }

    public uint getRowCount() { return getType()->getRowCount(); }

    public uint getColumnCount() { return getType()->getColumnCount(); }

    public SlangScalarType getScalarType() { return getType()->getScalarType(); }

    public TypeReflection* getResourceResultType() { return getType()->getResourceResultType(); }

    public SlangResourceShape getResourceShape() { return getType()->getResourceShape(); }

    public SlangResourceAccess getResourceAccess() { return getType()->getResourceAccess(); }

    public ConstU8Str getName() { return getType()->getName(); }

    public SlangMatrixLayoutMode getMatrixLayoutMode()
    {
        return spReflectionTypeLayout_GetMatrixLayoutMode(ptr);
    }

    public int getGenericParamIndex()
    {
        return spReflectionTypeLayout_getGenericParamIndex(ptr);
    }

    public TypeLayoutReflection* getPendingDataTypeLayout()
    {
        return spReflectionTypeLayout_getPendingDataTypeLayout(ptr);
    }

    public VariableLayoutReflection* getSpecializedTypePendingDataVarLayout()
    {
        return spReflectionTypeLayout_getSpecializedTypePendingDataVarLayout(ptr);
    }

    public SlangInt getBindingRangeCount()
    {
        return spReflectionTypeLayout_getBindingRangeCount(ptr);
    }

    public SlangBindingType getBindingRangeType(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeType(ptr, index);
    }

    public bool isBindingRangeSpecializable(SlangInt index)
    {
        return spReflectionTypeLayout_isBindingRangeSpecializable(ptr, index) == 1;
    }

    public SlangInt getBindingRangeBindingCount(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeBindingCount(ptr, index);
    }

    public SlangInt getFieldBindingRangeOffset(SlangInt fieldIndex)
    {
        return spReflectionTypeLayout_getFieldBindingRangeOffset(ptr, fieldIndex);
    }

    public SlangInt getExplicitCounterBindingRangeOffset()
    {
        return spReflectionTypeLayout_getExplicitCounterBindingRangeOffset(ptr);
    }

    public TypeLayoutReflection* getBindingRangeLeafTypeLayout(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeLeafTypeLayout(
            ptr,
            index);
    }

    public VariableReflection* getBindingRangeLeafVariable(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeLeafVariable(ptr, index);
    }

    public SlangImageFormat getBindingRangeImageFormat(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeImageFormat(ptr, index);
    }

    public SlangInt getBindingRangeDescriptorSetIndex(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(ptr, index);
    }

    public SlangInt getBindingRangeFirstDescriptorRangeIndex(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(ptr, index);
    }

    public SlangInt getBindingRangeDescriptorRangeCount(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(
            ptr,
            index);
    }

    public SlangInt getDescriptorSetCount()
    {
        return spReflectionTypeLayout_getDescriptorSetCount(ptr);
    }

    public SlangInt getDescriptorSetSpaceOffset(SlangInt setIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetSpaceOffset(
            ptr,
            setIndex);
    }

    public SlangInt getDescriptorSetDescriptorRangeCount(SlangInt setIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(
            ptr,
            setIndex);
    }

    public SlangInt getDescriptorSetDescriptorRangeIndexOffset(SlangInt setIndex, SlangInt rangeIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(
            ptr,
            setIndex,
            rangeIndex);
    }

    public SlangInt getDescriptorSetDescriptorRangeDescriptorCount(SlangInt setIndex, SlangInt rangeIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(
            ptr,
            setIndex,
            rangeIndex);
    }

    public SlangBindingType getDescriptorSetDescriptorRangeType(SlangInt setIndex, SlangInt rangeIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(
            ptr,
            setIndex,
            rangeIndex);
    }

    public SlangParameterCategory getDescriptorSetDescriptorRangeCategory(
        SlangInt setIndex,
        SlangInt rangeIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(
            ptr,
            setIndex,
            rangeIndex);
    }

    public SlangInt getSubObjectRangeCount()
    {
        return spReflectionTypeLayout_getSubObjectRangeCount(ptr);
    }

    public SlangInt getSubObjectRangeBindingRangeIndex(SlangInt subObjectRangeIndex)
    {
        return spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(
            ptr,
            subObjectRangeIndex);
    }

    public SlangInt getSubObjectRangeSpaceOffset(SlangInt subObjectRangeIndex)
    {
        return spReflectionTypeLayout_getSubObjectRangeSpaceOffset(
            ptr,
            subObjectRangeIndex);
    }

    public VariableLayoutReflection* getSubObjectRangeOffset(SlangInt subObjectRangeIndex)
    {
        return spReflectionTypeLayout_getSubObjectRangeOffset(
            ptr,
            subObjectRangeIndex);
    }
};

public unsafe struct Modifier
{
}

public unsafe struct VariableReflection
{
    VariableReflection* ptr => (VariableReflection*)Unsafe.AsPointer<VariableReflection>(ref this);

    public ConstU8Str getName()
    {
        return spReflectionVariable_GetName(ptr);
    }

    public TypeReflection* getType()
    {
        return spReflectionVariable_GetType(ptr);
    }

    public Modifier* findModifier(SlangModifierID id)
    {
        return spReflectionVariable_FindModifier(ptr, id);
    }

    public uint getUserAttributeCount()
    {
        return spReflectionVariable_GetUserAttributeCount(ptr);
    }

    public Attribute* getUserAttributeByIndex(uint index)
    {
        return spReflectionVariable_GetUserAttribute(
            ptr,
            index);
    }

    public Attribute* findAttributeByName(IGlobalSession* globalSession, ConstU8Str name)
    {
        return spReflectionVariable_FindUserAttributeByName(
            ptr,
            globalSession,
            name);
    }

    public Attribute* findUserAttributeByName(IGlobalSession* globalSession, ConstU8Str name)
    {
        return findAttributeByName(globalSession, name);
    }

    public bool hasDefaultValue()
    {
        return spReflectionVariable_HasDefaultValue(ptr);
    }

    public SlangResult getDefaultValueInt(long* value)
    {
        return spReflectionVariable_GetDefaultValueInt(ptr, value);
    }

    public GenericReflection* getGenericContainer()
    {
        return spReflectionVariable_GetGenericContainer(ptr);
    }

    public VariableReflection* applySpecializations(GenericReflection* generic)
    {
        return spReflectionVariable_applySpecializations(ptr, generic);
    }
};

public unsafe struct VariableLayoutReflection
{
    VariableLayoutReflection* ptr => (VariableLayoutReflection*)Unsafe.AsPointer<VariableLayoutReflection>(ref this);

    public VariableReflection* getVariable()
    {
        return spReflectionVariableLayout_GetVariable(ptr);
    }

    public ConstU8Str getName()
    {
        return getVariable()->getName();
    }

    public Modifier* findModifier(SlangModifierID id) { return getVariable()->findModifier(id); }

    public TypeLayoutReflection* getTypeLayout()
    {
        return spReflectionVariableLayout_GetTypeLayout(
            ptr);
    }

    public SlangParameterCategory getCategory() { return getTypeLayout()->getParameterCategory(); }

    public uint getCategoryCount() { return getTypeLayout()->getCategoryCount(); }

    public SlangParameterCategory getCategoryByIndex(uint index)
    {
        return getTypeLayout()->getCategoryByIndex(index);
    }


    public nuint getOffset(SlangParameterCategory category)
    {
        return spReflectionVariableLayout_GetOffset(ptr, category);
    }

    public TypeReflection* getType() { return getVariable()->getType(); }

    public uint getBindingIndex()
    {
        return spReflectionParameter_GetBindingIndex(ptr);
    }

    public uint getBindingSpace()
    {
        return spReflectionParameter_GetBindingSpace(ptr);
    }

    public nuint getBindingSpace(SlangParameterCategory category)
    {
        return spReflectionVariableLayout_GetSpace(ptr, category);
    }

    public SlangImageFormat getImageFormat()
    {
        return spReflectionVariableLayout_GetImageFormat(ptr);
    }

    public ConstU8Str getSemanticName()
    {
        return spReflectionVariableLayout_GetSemanticName(ptr);
    }

    public nuint getSemanticIndex()
    {
        return spReflectionVariableLayout_GetSemanticIndex(ptr);
    }

    public SlangStage getStage()
    {
        return spReflectionVariableLayout_getStage(ptr);
    }

    public VariableLayoutReflection* getPendingDataLayout()
    {
        return spReflectionVariableLayout_getPendingDataLayout(
            ptr);
    }
};

public unsafe struct FunctionReflection
{
    FunctionReflection* ptr => (FunctionReflection*)Unsafe.AsPointer<FunctionReflection>(ref this);

    public ConstU8Str getName()
    {
        return spReflectionFunction_GetName(ptr);
    }

    public TypeReflection* getReturnType()
    {
        return spReflectionFunction_GetResultType(ptr);
    }

    public uint getParameterCount()
    {
        return spReflectionFunction_GetParameterCount(ptr);
    }

    public VariableReflection* getParameterByIndex(uint index)
    {
        return spReflectionFunction_GetParameter(
            ptr,
            index);
    }

    public uint getUserAttributeCount()
    {
        return spReflectionFunction_GetUserAttributeCount(ptr);
    }

    public Attribute* getUserAttributeByIndex(uint index)
    {
        return spReflectionFunction_GetUserAttribute(ptr, index);
    }

    public Attribute* findAttributeByName(IGlobalSession* globalSession, ConstU8Str name)
    {
        return spReflectionFunction_FindUserAttributeByName(
            ptr,
            globalSession,
            name);
    }

    public Attribute* findUserAttributeByName(IGlobalSession* globalSession, ConstU8Str name)
    {
        return findAttributeByName(globalSession, name);
    }

    public Modifier* findModifier(SlangModifierID id)
    {
        return spReflectionFunction_FindModifier(
            ptr,
            id);
    }

    public GenericReflection* getGenericContainer()
    {
        return spReflectionFunction_GetGenericContainer(ptr);
    }

    public FunctionReflection* applySpecializations(GenericReflection* generic)
    {
        return spReflectionFunction_applySpecializations(ptr, generic);
    }

    public FunctionReflection* specializeWithArgTypes(uint argCount, TypeReflection** types)
    {
        return spReflectionFunction_specializeWithArgTypes(
            ptr,
            (nint)argCount,
            types);
    }

    public bool isOverloaded()
    {
        return spReflectionFunction_isOverloaded(ptr);
    }

    public uint getOverloadCount()
    {
        return spReflectionFunction_getOverloadCount(ptr);
    }

    public FunctionReflection* getOverload(uint index)
    {
        return spReflectionFunction_getOverload(
            ptr,
            index);
    }
};

public unsafe struct GenericReflection
{
    GenericReflection* ptr => (GenericReflection*)Unsafe.AsPointer<GenericReflection>(ref this);

    public DeclReflection* asDecl()
    {
        return spReflectionGeneric_asDecl(ptr);
    }

    public ConstU8Str getName()
    {
        return spReflectionGeneric_GetName(ptr);
    }

    public uint getTypeParameterCount()
    {
        return spReflectionGeneric_GetTypeParameterCount(ptr);
    }

    public VariableReflection* getTypeParameter(uint index)
    {
        return spReflectionGeneric_GetTypeParameter(
            ptr,
            index);
    }

    public uint getValueParameterCount()
    {
        return spReflectionGeneric_GetValueParameterCount(ptr);
    }

    public VariableReflection* getValueParameter(uint index)
    {
        return spReflectionGeneric_GetValueParameter(
            ptr,
            index);
    }

    public uint getTypeParameterConstraintCount(VariableReflection* typeParam)
    {
        return spReflectionGeneric_GetTypeParameterConstraintCount(
            ptr,
            typeParam);
    }

    public TypeReflection* getTypeParameterConstraintType(VariableReflection* typeParam, uint index)
    {
        return spReflectionGeneric_GetTypeParameterConstraintType(
            ptr,
            typeParam,
            index);
    }

    public DeclReflection* getInnerDecl()
    {
        return spReflectionGeneric_GetInnerDecl(ptr);
    }

    public SlangDeclKind getInnerKind()
    {
        return spReflectionGeneric_GetInnerKind(ptr);
    }

    public GenericReflection* getOuterGenericContainer()
    {
        return spReflectionGeneric_GetOuterGenericContainer(
            ptr);
    }

    public TypeReflection* getConcreteType(VariableReflection* typeParam)
    {
        return spReflectionGeneric_GetConcreteType(
            ptr,
            typeParam);
    }

    public long getConcreteIntVal(VariableReflection* valueParam)
    {
        return spReflectionGeneric_GetConcreteIntVal(
            ptr,
            valueParam);
    }

    public GenericReflection* applySpecializations(GenericReflection* generic)
    {
        return spReflectionGeneric_applySpecializations(
            ptr,
            generic);
    }
};

public unsafe struct EntryPointReflection
{
    EntryPointReflection* ptr => (EntryPointReflection*)Unsafe.AsPointer<EntryPointReflection>(ref this);

    public ConstU8Str getName()
    {
        return spReflectionEntryPoint_getName(ptr);
    }

    public ConstU8Str getNameOverride()
    {
        return spReflectionEntryPoint_getNameOverride(ptr);
    }

    public uint getParameterCount()
    {
        return spReflectionEntryPoint_getParameterCount(ptr);
    }

    public FunctionReflection* getFunction()
    {
        return spReflectionEntryPoint_getFunction(
            ptr);
    }

    public VariableLayoutReflection* getParameterByIndex(uint index)
    {
        return spReflectionEntryPoint_getParameterByIndex(
            ptr,
            index);
    }

    public SlangStage getStage()
    {
        return spReflectionEntryPoint_getStage(ptr);
    }

    public void getComputeThreadGroupSize(SlangUInt axisCount, SlangUInt* outSizeAlongAxis)
    {
        spReflectionEntryPoint_getComputeThreadGroupSize(
            ptr,
            axisCount,
            outSizeAlongAxis);
    }

    public void getComputeWaveSize(SlangUInt* outWaveSize)
    {
        spReflectionEntryPoint_getComputeWaveSize(
            ptr,
            outWaveSize);
    }

    public bool usesAnySampleRateInput()
    {
        return spReflectionEntryPoint_usesAnySampleRateInput(ptr) != 0;
    }

    public VariableLayoutReflection* getVarLayout()
    {
        return spReflectionEntryPoint_getVarLayout(
            ptr);
    }

    public TypeLayoutReflection* getTypeLayout() { return getVarLayout()->getTypeLayout(); }

    public VariableLayoutReflection* getResultVarLayout()
    {
        return spReflectionEntryPoint_getResultVarLayout(
            ptr);
    }

    public bool hasDefaultConstantBuffer()
    {
        return spReflectionEntryPoint_hasDefaultConstantBuffer(ptr) != 0;
    }
};

public unsafe struct TypeParameterReflection
{
    TypeParameterReflection* ptr => (TypeParameterReflection*)Unsafe.AsPointer<TypeParameterReflection>(ref this);

    public ConstU8Str getName()
    {
        return spReflectionTypeParameter_GetName(ptr);
    }
    public uint getIndex()
    {
        return spReflectionTypeParameter_GetIndex(ptr);
    }

    public uint getConstraintCount()
    {
        return spReflectionTypeParameter_GetConstraintCount(ptr);
    }

    public TypeReflection* getConstraintByIndex(uint index)
    {
        return spReflectionTypeParameter_GetConstraintByIndex(
            ptr,
            index);
    }
};


public unsafe struct ShaderReflection
{
    ShaderReflection* ptr => (ShaderReflection*)Unsafe.AsPointer<ShaderReflection>(ref this);

    public uint getParameterCount() { return spReflection_GetParameterCount(ptr); }

    public uint getTypeParameterCount()
    {
        return spReflection_GetTypeParameterCount(ptr);
    }

    public ISession* getSession() { return spReflection_GetSession(ptr); }

    public TypeParameterReflection* getTypeParameterByIndex(uint index)
    {
        return spReflection_GetTypeParameterByIndex(
            ptr,
            index);
    }

    public TypeParameterReflection* findTypeParameter(ConstU8Str name)
    {
        return
            spReflection_FindTypeParameter(ptr, name);
    }

    public VariableLayoutReflection* getParameterByIndex(uint index)
    {
        return spReflection_GetParameterByIndex(
            ptr,
            index);
    }

    public static ShaderReflection* get(ICompileRequest* request)
    {
        return spGetReflection(request);
    }

    public SlangUInt getEntryPointCount()
    {
        return spReflection_getEntryPointCount(ptr);
    }

    public EntryPointReflection* getEntryPointByIndex(SlangUInt index)
    {
        return
            spReflection_getEntryPointByIndex(ptr, index);
    }

    public SlangUInt getGlobalConstantBufferBinding()
    {
        return spReflection_getGlobalConstantBufferBinding(ptr);
    }

    public nuint getGlobalConstantBufferSize()
    {
        return spReflection_getGlobalConstantBufferSize(ptr);
    }

    public TypeReflection* findTypeByName(ConstU8Str name)
    {
        return spReflection_FindTypeByName(ptr, name);
    }

    public FunctionReflection* findFunctionByName(ConstU8Str name)
    {
        return spReflection_FindFunctionByName(ptr, name);
    }

    public FunctionReflection* findFunctionByNameInType(TypeReflection* type, ConstU8Str name)
    {
        return spReflection_FindFunctionByNameInType(
            ptr,
            type,
            name);
    }

    public VariableReflection* findVarByNameInType(TypeReflection* type, ConstU8Str name)
    {
        return spReflection_FindVarByNameInType(
            ptr,
            type,
            name);
    }

    public TypeLayoutReflection* getTypeLayout(
        TypeReflection* type,
        SlangLayoutRules rules)
    {
        return spReflection_GetTypeLayout(
            ptr,
            type,
            rules);
    }

    public EntryPointReflection* findEntryPointByName(ConstU8Str name)
    {
        return
            spReflection_findEntryPointByName(ptr, name);
    }

    public TypeReflection* specializeType(
        TypeReflection* type,
        SlangInt specializationArgCount,
        TypeReflection** specializationArgs,
        out ISlangBlob* outDiagnostics)
    {
        return spReflection_specializeType(
            ptr,
            type,
            specializationArgCount,
            specializationArgs,
            out outDiagnostics);
    }

    public GenericReflection* specializeGeneric(
        GenericReflection* generic,
        SlangInt specializationArgCount,
        SlangReflectionGenericArgType* specializationArgTypes,
        GenericArgReflection* specializationArgVals,
        out ISlangBlob* outDiagnostics)
    {
        return spReflection_specializeGeneric(
            ptr,
            generic,
            specializationArgCount,
            specializationArgTypes,
            specializationArgVals,
            out outDiagnostics);
    }

    public bool isSubType(TypeReflection* subType, TypeReflection* superType)
    {
        return spReflection_isSubType(
            ptr,
            subType,
            superType);
    }

    public SlangUInt getHashedStringCount()
    {
        return spReflection_getHashedStringCount(ptr);
    }

    public ConstU8Str getHashedString(SlangUInt index, nuint* outCount)
    {
        return spReflection_getHashedString(ptr, index, outCount);
    }

    public TypeLayoutReflection* getGlobalParamsTypeLayout()
    {
        return spReflection_getGlobalParamsTypeLayout(
            ptr);
    }

    public VariableLayoutReflection* getGlobalParamsVarLayout()
    {
        return spReflection_getGlobalParamsVarLayout(
            ptr);
    }

    public SlangResult toJson(out ISlangBlob* outBlob)
    {
        return spReflection_ToJson(ptr, null, out outBlob);
    }
};


public unsafe struct DeclReflection
{
    DeclReflection* ptr => (DeclReflection*)Unsafe.AsPointer(ref this);

    public ConstU8Str getName()
    {
        return spReflectionDecl_getName(ptr);
    }

    public SlangDeclKind getKind() { return spReflectionDecl_getKind(ptr); }

    public uint getChildrenCount()
    {
        return spReflectionDecl_getChildrenCount(ptr);
    }

    public DeclReflection* getChild(uint index)
    {
        return spReflectionDecl_getChild(ptr, index);
    }

    public TypeReflection* getType()
    {
        return spReflection_getTypeFromDecl(ptr);
    }

    public VariableReflection* asVariable()
    {
        return spReflectionDecl_castToVariable(ptr);
    }

    public FunctionReflection* asFunction()
    {
        return spReflectionDecl_castToFunction(ptr);
    }

    public GenericReflection* asGeneric()
    {
        return spReflectionDecl_castToGeneric(ptr);
    }

    public DeclReflection* getParent()
    {
        return spReflectionDecl_getParent(ptr);
    }


    public List<nint> GetChildrenOfKind(SlangDeclKind kind)
    {
        List<nint> children = new();

        for (uint i = 0; i < getChildrenCount(); i++)
        {
            DeclReflection* child = getChild(i);

            if (child->getKind() != kind)
                continue;

            children.Add((nint)getChild(i));
        }

        return children;
    }


    public List<nint> GetChildren()
    {
        List<nint> children = new();

        for (uint i = 0; i < getChildrenCount(); i++)
            children.Add((nint)getChild(i));

        return children;
    }
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SpecializationArg
{
    public enum Kind : int
    {
        Unknown, /**< An invalid specialization argument. */
        Type,    /**< Specialize to a type. */
    };

    /** The kind of specialization argument. */
    public Kind kind;

    /** A type specialization argument, used for `Kind::Type`. */
    public TypeReflection* type;

    public static SpecializationArg fromType(TypeReflection* inType)
    {
        SpecializationArg rs;
        rs.kind = Kind.Type;
        rs.type = inType;
        return rs;
    }
};


public struct CompileCoreModuleFlag
{
}


/** A global session for interaction with the Slang library.

An application may create and re-use a single global session across
multiple sessions, in order to amortize startups costs (in current
Slang this is mostly the cost of loading the Slang standard library).

The global session is currently *not* thread-safe and objects created from
a single global session should only be used from a single thread at
a time.
*/
[UUID(0xc140b5fd, 0xc78, 0x452e, 0xba, 0x7c, 0x1a, 0x1e, 0x70, 0xc7, 0xf7, 0x1c)]
public unsafe interface IGlobalSession : IUnknown
{
    /** Create a new session for loading and compiling code.
     */
    SlangResult CreateSession(SessionDesc* desc, out ISession* outSession);

    /** Look up the internal ID of a profile by its `name`.

    Profile IDs are *not* guaranteed to be stable across versions
    of the Slang library, so clients are expected to look up
    profiles by name at runtime.
    */
    SlangProfileID FindProfile(ConstU8Str name);

    /** Set the path that downstream compilers (aka back end compilers) will
    be looked from.
    @param passThrough Identifies the downstream compiler
    @param path The path to find the downstream compiler (shared library/dll/executable)

    For back ends that are dlls/shared libraries, it will mean the path will
    be prefixed with the path when calls are made out to ISlangSharedLibraryLoader.
    For executables - it will look for executables along the path */
    void SetDownstreamCompilerPath(SlangPassThrough passThrough, ConstU8Str path);

    /** DEPRECATED: Use setLanguagePrelude

    Set the 'prelude' for generated code for a 'downstream compiler'.
    @param passThrough The downstream compiler for generated code that will have the prelude applied
    to it.
    @param preludeText The text added pre-pended verbatim before the generated source

    That for pass-through usage, prelude is not pre-pended, preludes are for code generation only.
    */
    void SetDownstreamCompilerPrelude(SlangPassThrough passThrough, ConstU8Str preludeText);

    /** DEPRECATED: Use getLanguagePrelude

    Get the 'prelude' for generated code for a 'downstream compiler'.
    @param passThrough The downstream compiler for generated code that will have the prelude applied
    to it.
    @param outPrelude  On exit holds a blob that holds the string of the prelude.
    */
    void GetDownstreamCompilerPrelude(SlangPassThrough passThrough, out ISlangBlob* outPrelude);

    /** Get the build version 'tag' string. The string is the same as produced via `git describe
    --tags` for the project. If Slang is built separately from the automated build scripts the
    contents will by default be 'unknown'. Any string can be set by changing the contents of
    'slang-tag-version.h' file and recompiling the project.

    This method will return exactly the same result as the free function spGetBuildTagString.

    @return The build tag string
    */
    ConstU8Str GetBuildTagString();

    /* For a given source language set the default compiler.
    If a default cannot be chosen (for example the target cannot be achieved by the default),
    the default will not be used.

    @param sourceLanguage the source language
    @param defaultCompiler the default compiler for that language
    @return
    */
    SlangResult SetDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage, SlangPassThrough defaultCompiler);

    /* For a source type get the default compiler

    @param sourceLanguage the source language
    @return The downstream compiler for that source language */
    SlangPassThrough GetDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage);

    /* Set the 'prelude' placed before generated code for a specific language type.

    @param sourceLanguage The language the prelude should be inserted on.
    @param preludeText The text added pre-pended verbatim before the generated source

    Note! That for pass-through usage, prelude is not pre-pended, preludes are for code generation
    only.
    */
    void SetLanguagePrelude(SlangSourceLanguage sourceLanguage, ConstU8Str preludeText);

    /** Get the 'prelude' associated with a specific source language.
    @param sourceLanguage The language the prelude should be inserted on.
    @param outPrelude  On exit holds a blob that holds the string of the prelude.
    */
    void GetLanguagePrelude(SlangSourceLanguage sourceLanguage, out ISlangBlob* outPrelude);

    /** Create a compile request.
     */
    [Obsolete("Method is deprecated")]
    SlangResult CreateCompileRequest(out /* ICompileRequest */ void* outCompileRequest);

    /** Add new builtin declarations to be used in subsequent compiles.
     */
    void AddBuiltins(ConstU8Str sourcePath, ConstU8Str sourceString);

    /** Set the session shared library loader. If this changes the loader, it may cause shared
    libraries to be unloaded
    @param loader The loader to set. Setting null sets the default loader.
    */
    void SetSharedLibraryLoader(ISlangSharedLibraryLoader* loader);

    /** Gets the currently set shared library loader
    @return Gets the currently set loader. If returns null, it's the default loader
    */
    ISlangSharedLibraryLoader* GetSharedLibraryLoader();

    /** Returns SLANG_OK if the compilation target is supported for this session

    @param target The compilation target to test
    @return SLANG_OK if the target is available
    SLANG_E_NOT_IMPLEMENTED if not implemented in this build
    SLANG_E_NOT_FOUND if other resources (such as shared libraries) required to make target work
    could not be found SLANG_FAIL other kinds of failures */
    SlangResult CheckCompileTargetSupport(SlangCompileTarget target);

    /** Returns SLANG_OK if the pass through support is supported for this session
    @param session Session
    @param target The compilation target to test
    @return SLANG_OK if the target is available
    SLANG_E_NOT_IMPLEMENTED if not implemented in this build
    SLANG_E_NOT_FOUND if other resources (such as shared libraries) required to make target work
    could not be found SLANG_FAIL other kinds of failures */
    SlangResult CheckPassThroughSupport(SlangPassThrough passThrough);

    /** Compile from (embedded source) the core module on the session.
    Will return a failure if there is already a core module available
    NOTE! API is experimental and not ready for production code
    @param flags to control compilation
    */
    SlangResult CompileCoreModule(CompileCoreModuleFlags flags);

    /** Load the core module. Currently loads modules from the file system.
    @param coreModule Start address of the serialized core module
    @param coreModuleSizeInBytes The size in bytes of the serialized core module

    NOTE! API is experimental and not ready for production code
    */
    SlangResult LoadCoreModule(void* coreModule, nuint coreModuleSizeInBytes);

    /** Save the core module to the file system
    @param archiveType The type of archive used to hold the core module
    @param outBlob The serialized blob containing the core module

    NOTE! API is experimental and not ready for production code  */
    SlangResult SaveCoreModule(SlangArchiveType archiveType, out ISlangBlob* outBlob);

    /** Look up the internal ID of a capability by its `name`.

    Capability IDs are *not* guaranteed to be stable across versions
    of the Slang library, so clients are expected to look up
    capabilities by name at runtime.
    */
    SlangCapabilityID FindCapability(ConstU8Str name);

    /** Set the downstream/pass through compiler to be used for a transition from the source type to
    the target type
    @param source The source 'code gen target'
    @param target The target 'code gen target'
    @param compiler The compiler/pass through to use for the transition from source to target
    */
    void SetDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target, SlangPassThrough compiler);

    /** Get the downstream/pass through compiler for a transition specified by source and target
    @param source The source 'code gen target'
    @param target The target 'code gen target'
    @return The compiler that is used for the transition. Returns NONE it is not
    defined
    */
    SlangPassThrough GetDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target);

    /** Get the time in seconds spent in the slang and downstream compiler.
     */
    void GetCompilerElapsedTime(out double outTotalTime, out double outDownstreamTime);

    /** Specify a spirv.core.grammar.json file to load and use when
     * parsing and checking any SPIR-V code
     */
    SlangResult SetSPIRVCoreGrammar(ConstU8Str jsonPath);

    /** Parse slangc command line options into a SessionDesc that can be used to create a session
     *   with all the compiler options specified in the command line.
     *   @param argc The number of command line arguments.
     *   @param argv An input array of command line arguments to parse.
     *   @param outSessionDesc A pointer to a SessionDesc struct to receive parsed session desc.
     *   @param outAuxAllocation Auxiliary memory allocated to hold data used in the session desc.
     */
    SlangResult ParseCommandLineArguments(int argc, ConstU8Str* argv, SessionDesc* outSessionDesc, out IUnknown* outAuxAllocation);

    /** Computes a digest that uniquely identifies the session description.
     */
    SlangResult GetSessionDescDigest(SessionDesc* sessionDesc, out ISlangBlob* outBlob);

    /** Compile from (embedded source) the builtin module on the session.
    Will return a failure if there is already a builtin module available.
    NOTE! API is experimental and not ready for production code.
    @param module The builtin module name.
    @param flags to control compilation
    */
    SlangResult CompileBuiltinModule(BuiltinModuleName module, CompileCoreModuleFlags flags);

    /** Load a builtin module. Currently loads modules from the file system.
    @param module The builtin module name
    @param moduleData Start address of the serialized core module
    @param sizeInBytes The size in bytes of the serialized builtin module

    NOTE! API is experimental and not ready for production code
    */
    SlangResult LoadBuiltinModule(BuiltinModuleName module, void* moduleData, nuint sizeInBytes);

    /** Save the builtin module to the file system
    @param module The builtin module name
    @param archiveType The type of archive used to hold the builtin module
    @param outBlob The serialized blob containing the builtin module

    NOTE! API is experimental and not ready for production code  */
    SlangResult SaveBuiltinModule(BuiltinModuleName module, SlangArchiveType archiveType, out ISlangBlob* outBlob);
}


/** Description of a code generation target.
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct TargetDesc()
{
    /** The size of this structure, in bytes.
     */
    public nuint structureSize = (nuint)sizeof(TargetDesc);

    /** The target format to generate code for (e.g., SPIR-V, DXIL, etc.)
     */
    public SlangCompileTarget format = SlangCompileTarget.TARGET_UNKNOWN;

    /** The compilation profile supported by the target (e.g., "Shader Model 5.1")
     */
    public SlangProfileID profile = SlangProfileID.UNKNOWN;

    /** Flags for the code generation target. Currently unused. */
    public SlangTargetFlags flags = SlangTargetFlags.Default;

    /** Default mode to use for floating-point operations on the target.
     */
    public SlangFloatingPointMode floatingPointMode = SlangFloatingPointMode.DEFAULT;

    /** The line directive mode for output source code.
     */
    public SlangLineDirectiveMode lineDirectiveMode = SlangLineDirectiveMode.DEFAULT;

    /** Whether to force `scalar` layout for glsl shader storage buffers.
     */
    public bool forceGLSLScalarBufferLayout = false;

    /** Pointer to an array of compiler option entries, whose size is compilerOptionEntryCount.
     */
    public CompilerOptionEntry* compilerOptionEntries = null;

    /** Number of additional compiler option entries.
     */
    public uint compilerOptionEntryCount = 0;
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct PreprocessorMacroDesc
{
    public ConstU8Str name;
    public ConstU8Str value;
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SessionDesc()
{
    /** The size of this structure, in bytes.
     */
    public nuint structureSize = (nuint)sizeof(SessionDesc);

    /** Code generation targets to include in the session.
     */
    public TargetDesc* targets = null;
    public SlangInt targetCount = 0;

    /** Flags to configure the session.
     */
    public SessionFlags flags = SessionFlags.None;

    /** Default layout to assume for variables with matrix types.
     */
    public SlangMatrixLayoutMode defaultMatrixLayoutMode = SlangMatrixLayoutMode.ROW_MAJOR;

    /** Paths to use when searching for `#include`d or `import`ed files.
     */
    public ConstU8Str* searchPaths = null;
    public SlangInt searchPathCount = 0;

    public PreprocessorMacroDesc* preprocessorMacros = null;
    public SlangInt preprocessorMacroCount = 0;

    public ISlangFileSystem* fileSystem = null;

    public bool enableEffectAnnotations = false;
    public bool allowGLSLSyntax = false;

    /** Pointer to an array of compiler option entries, whose size is compilerOptionEntryCount.
     */
    public CompilerOptionEntry* compilerOptionEntries = null;

    /** Number of additional compiler option entries.
     */
    public uint compilerOptionEntryCount = 0;
};


/** A session provides a scope for code that is loaded.

A session can be used to load modules of Slang source code,
and to request target-specific compiled binaries and layout
information.

In order to be able to load code, the session owns a set
of active "search paths" for resolving `#include` directives
and `import` declarations, as well as a set of global
preprocessor definitions that will be used for all code
that gets `import`ed in the session.

If multiple user shaders are loaded in the same session,
and import the same module (e.g., two source files do `import X`)
then there will only be one copy of `X` loaded within the session.

In order to be able to generate target code, the session
owns a list of available compilation targets, which specify
code generation options.

Code loaded and compiled within a session is owned by the session
and will remain resident in memory until the session is released.
Applications wishing to control the memory usage for compiled
and loaded code should use multiple sessions.
*/
[UUID(0x67618701, 0xd116, 0x468f, 0xab, 0x3b, 0x47, 0x4b, 0xed, 0xce, 0xe, 0x3d)]
public unsafe interface ISession : IUnknown
{
    /** Get the global session thas was used to create this session.
     */
    IGlobalSession* GetGlobalSession();

    /** Load a module as it would be by code using `import`.
     */
    IModule* LoadModule(ConstU8Str moduleName, out ISlangBlob* outDiagnostics);

    /** Load a module from Slang source code.
     */
    IModule* loadModuleFromSource(
        ConstU8Str moduleName,
        ConstU8Str path,
        ISlangBlob* source,
        out ISlangBlob* outDiagnostics);

    /** Combine multiple component types to create a composite component type.

    The `componentTypes` array must contain `componentTypeCount` pointers
    to component types that were loaded or created using the same session.

    The shader parameters and specialization parameters of the composite will
    be the union of those in `componentTypes`. The relative order of child
    component types is significant, and will affect the order in which
    parameters are reflected and laid out.

    The entry-point functions of the composite will be the union of those in
    `componentTypes`, and will follow the ordering of `componentTypes`.

    The requirements of the composite component type will be a subset of
    those in `componentTypes`. If an entry in `componentTypes` has a requirement
    that can be satisfied by another entry, then the composition will
    satisfy the requirement and it will not appear as a requirement of
    the composite. If multiple entries in `componentTypes` have a requirement
    for the same type, then only the first such requirement will be retained
    on the composite. The relative ordering of requirements on the composite
    will otherwise match that of `componentTypes`.

    If any diagnostics are generated during creation of the composite, they
    will be written to `outDiagnostics`. If an error is encountered, the
    function will return null.

    It is an error to create a composite component type that recursively
    aggregates a single module more than once.
    */
    SlangResult CreateCompositeComponentType(
        IComponentType** componentTypes,
        SlangInt componentTypeCount,
        out IComponentType* outCompositeComponentType,
        out ISlangBlob* outDiagnostics);

    /** Specialize a type based on type arguments.
     */
    TypeReflection* specializeType(
        TypeReflection* type,
        SpecializationArg* specializationArgs,
        SlangInt specializationArgCount,
        out ISlangBlob* outDiagnostics);


    /** Get the layout `type` on the chosen `target`.
     */
    TypeLayoutReflection* getTypeLayout(
        TypeReflection* type,
        SlangInt targetIndex,
        SlangLayoutRules rules,
        out ISlangBlob* outDiagnostics);

    /** Get a container type from `elementType`. For example, given type `T`, returns
        a type that represents `StructuredBuffer<T>`.

        @param `elementType`: the element type to wrap around.
        @param `containerType`: the type of the container to wrap `elementType` in.
        @param `outDiagnostics`: a blob to receive diagnostic messages.
    */
    TypeReflection* getContainerType(
        TypeReflection* elementType,
        ContainerType containerType,
        out ISlangBlob* outDiagnostics);

    /** Return a `TypeReflection` that represents the `__Dynamic` type.
        This type can be used as a specialization argument to indicate using
        dynamic dispatch.
    */
    TypeReflection* GetDynamicType();

    /** Get the mangled name for a type RTTI object.
     */
    SlangResult GetTypeRTTIMangledName(TypeReflection* type, out ISlangBlob* outNameBlob);

    /** Get the mangled name for a type witness.
     */
    SlangResult GetTypeConformanceWitnessMangledName(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out ISlangBlob* outNameBlob);

    /** Get the sequential ID used to identify a type witness in a dynamic object.
     */
    SlangResult GetTypeConformanceWitnessSequentialID(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out nuint outId);

    /** Create a request to load/compile front-end code.
     */
    SlangResult CreateCompileRequest(out ICompileRequest* outCompileRequest);


    /** Creates a `IComponentType` that represents a type's conformance to an interface.
        The retrieved `ITypeConformance` objects can be included in a composite `IComponentType`
        to explicitly specify which implementation types should be included in the final compiled
        code. For example, if an module defines `IMaterial` interface and `AMaterial`,
        `BMaterial`, `CMaterial` types that implements the interface, the user can exclude
        `CMaterial` implementation from the resulting shader code by explicitly adding
        `AMaterial:IMaterial` and `BMaterial:IMaterial` conformances to a composite
        `IComponentType` and get entry point code from it. The resulting code will not have
        anything related to `CMaterial` in the dynamic dispatch logic. If the user does not
        explicitly include any `TypeConformances` to an interface type, all implementations to
        that interface will be included by default. By linking a `ITypeConformance`, the user is
        also given the opportunity to specify the dispatch ID of the implementation type. If
        `conformanceIdOverride` is -1, there will be no override behavior and Slang will
        automatically assign IDs to implementation types. The automatically assigned IDs can be
        queried via `ISession::getTypeConformanceWitnessSequentialID`.

        Returns SLANG_OK if succeeds, or SLANG_FAIL if `type` does not conform to `interfaceType`.
    */
    SlangResult CreateTypeConformanceComponentType(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out ITypeConformance* outConformance,
        SlangInt conformanceIdOverride,
        out ISlangBlob* outDiagnostics);

    /** Load a module from a Slang module blob.
     */
    IModule* LoadModuleFromIRBlob(
        ConstU8Str moduleName,
        ConstU8Str path,
        ISlangBlob* source,
        out ISlangBlob* outDiagnostics);

    SlangInt GetLoadedModuleCount();
    IModule* GetLoadedModule(SlangInt index);

    /** Checks if a precompiled binary module is up-to-date with the current compiler
     *   option settings and the source file contents.
     */
    bool IsBinaryModuleUpToDate(ConstU8Str modulePath, ISlangBlob* binaryModuleBlob);

    /** Load a module from a string.
     */
    IModule* loadModuleFromSourceString(
        ConstU8Str moduleName,
        ConstU8Str path,
        ConstU8Str srcString,
        out ISlangBlob* outDiagnostics);
}


[UUID(0x8044a8a3, 0xddc0, 0x4b7f, 0xaf, 0x8e, 0x2, 0x6e, 0x90, 0x5d, 0x73, 0x32)]
public interface IMetadata : ISlangCastable
{
    /*
    Returns whether a resource parameter at the specified binding location is actually being used
    in the compiled shader.
    */
    SlangResult IsParameterLocationUsed(
        SlangParameterCategory category, // is this a `t` register? `s` register?
        SlangUInt spaceIndex,            // `space` for D3D12, `set` for Vulkan
        SlangUInt registerIndex,         // `register` for D3D12, `binding` for Vulkan
        out bool outUsed);
}

/** A component type is a unit of shader code layout, reflection, and linking.

A component type is a unit of shader code that can be included into
a linked and compiled shader program. Each component type may have:

* Zero or more uniform shader parameters, representing textures,
  buffers, etc. that the code in the component depends on.

* Zero or more *specialization* parameters, which are type or
  value parameters that can be used to synthesize specialized
  versions of the component type.

* Zero or more entry points, which are the individually invocable
  kernels that can have final code generated.

* Zero or more *requirements*, which are other component
  types on which the component type depends.

One example of a component type is a module of Slang code:

* The global-scope shader parameters declared in the module are
  the parameters when considered as a component type.

* Any global-scope generic or interface type parameters introduce
  specialization parameters for the module.

* A module does not by default include any entry points when
  considered as a component type (although the code of the
  module might *declare* some entry points).

* Any other modules that are `import`ed in the source code
  become requirements of the module, when considered as a
  component type.

An entry point is another example of a component type:

* The `uniform` parameters of the entry point function are
  its shader parameters when considered as a component type.

* Any generic or interface-type parameters of the entry point
  introduce specialization parameters.

* An entry point component type exposes a single entry point (itself).

* An entry point has one requirement for the module in which
  it was defined.

Component types can be manipulated in a few ways:

* Multiple component types can be combined into a composite, which
  combines all of their code, parameters, etc.

* A component type can be specialized, by "plugging in" types and
  values for its specialization parameters.

* A component type can be laid out for a particular target, giving
  offsets/bindings to the shader parameters it contains.

* Generated kernel code can be requested for entry points.

*/
[UUID(0x5bc42be8, 0x5c50, 0x4929, 0x9e, 0x5e, 0xd1, 0x5e, 0x7c, 0x24, 0x1, 0x5f)]
public unsafe interface IComponentType : IUnknown
{

    /** Get the runtime session that this component type belongs to.
     */
    ISession* GetSession();

    /** Get the layout for this program for the chosen `targetIndex`.

    The resulting layout will establish offsets/bindings for all
    of the global and entry-point shader parameters in the
    component type.

    If this component type has specialization parameters (that is,
    it is not fully specialized), then the resulting layout may
    be incomplete, and plugging in arguments for generic specialization
    parameters may result in a component type that doesn't have
    a compatible layout. If the component type only uses
    interface-type specialization parameters, then the layout
    for a specialization should be compatible with an unspecialized
    layout (all parameters in the unspecialized layout will have
    the same offset/binding in the specialized layout).

    If this component type is combined into a composite, then
    the absolute offsets/bindings of parameters may not stay the same.
    If the shader parameters in a component type don't make
    use of explicit binding annotations (e.g., `register(...)`),
    then the *relative* offset of shader parameters will stay
    the same when it is used in a composition.
    */
    ShaderReflection* GetLayout(SlangInt targetIndex, out ISlangBlob* outDiagnostics);

    /** Get the number of (unspecialized) specialization parameters for the component type.
     */
    SlangInt GetSpecializationParamCount();

    /** Get the compiled code for the entry point at `entryPointIndex` for the chosen `targetIndex`

    Entry point code can only be computed for a component type that
    has no specialization parameters (it must be fully specialized)
    and that has no requirements (it must be fully linked).

    If code has not already been generated for the given entry point and target,
    then a compilation error may be detected, in which case `outDiagnostics`
    (if non-null) will be filled in with a blob of messages diagnosing the error.
    */
    SlangResult GetEntryPointCode(
        SlangInt entryPointIndex,
        SlangInt targetIndex,
        out ISlangBlob* outCode,
        out ISlangBlob* outDiagnostics);

    /** Get the compilation result as a file system.

    Has the same requirements as getEntryPointCode.

    The result is not written to the actual OS file system, but is made available as an
    in memory representation.
    */
    SlangResult GetResultAsFileSystem(
        SlangInt entryPointIndex,
        SlangInt targetIndex,
        out ISlangMutableFileSystem* outFileSystem);

    /** Compute a hash for the entry point at `entryPointIndex` for the chosen `targetIndex`.

    This computes a hash based on all the dependencies for this component type as well as the
    target settings affecting the compiler backend. The computed hash is used as a key for caching
    the output of the compiler backend to implement shader caching.
    */
    void GetEntryPointHash(SlangInt entryPointIndex, SlangInt targetIndex, out ISlangBlob* outHash);

    /** Specialize the component by binding its specialization parameters to concrete arguments.

    The `specializationArgs` array must have `specializationArgCount` entries, and
    this must match the number of specialization parameters on this component type.

    If any diagnostics (error or warnings) are produced, they will be written to `outDiagnostics`.
    */
    SlangResult Specialize(SpecializationArg* specializationArgs, SlangInt specializationArgCount, out IComponentType* outSpecializedComponentType, out ISlangBlob* outDiagnostics);

    /** Link this component type against all of its unsatisfied dependencies.

    A component type may have unsatisfied dependencies. For example, a module
    depends on any other modules it `import`s, and an entry point depends
    on the module that defined it.

    A user can manually satisfy dependencies by creating a composite
    component type, and when doing so they retain full control over
    the relative ordering of shader parameters in the resulting layout.

    It is an error to try to generate/access compiled kernel code for
    a component type with unresolved dependencies, so if dependencies
    remain after whatever manual composition steps an application
    cares to perform, the `link()` function can be used to automatically
    compose in any remaining dependencies. The order of parameters
    (and hence the global layout) that results will be deterministic,
    but is not currently documented.
    */
    SlangResult Link(out IComponentType* outLinkedComponentType, out ISlangBlob* outDiagnostics);

    /** Get entry point 'callable' functions accessible through the ISlangSharedLibrary interface.

    The functions remain in scope as long as the ISlangSharedLibrary interface is in scope.

    NOTE! Requires a compilation target of SLANG_HOST_CALLABLE.

    @param entryPointIndex  The index of the entry point to get code for.
    @param targetIndex      The index of the target to get code for (default: zero).
    @param outSharedLibrary A pointer to a ISharedLibrary interface which functions can be queried
    on.
    @returns                A `SlangResult` to indicate success or failure.
    */
    SlangResult GetEntryPointHostCallable(int entryPointIndex, int targetIndex, out ISlangSharedLibrary* outSharedLibrary, out ISlangBlob* outDiagnostics);

    /** Get a new ComponentType object that represents a renamed entry point.

    The current object must be a single EntryPoint, or a CompositeComponentType or
    SpecializedComponentType that contains one EntryPoint component.
    */
    SlangResult RenameEntryPoint(ConstU8Str newName, out IComponentType* outEntryPoint);

    /** Link and specify additional compiler options when generating code
     *   from the linked program.
     */
    SlangResult LinkWithOptions(
        out IComponentType* outLinkedComponentType,
        uint compilerOptionEntryCount,
        CompilerOptionEntry* compilerOptionEntries,
        out ISlangBlob* outDiagnostics);

    SlangResult GetTargetCode(
        SlangInt targetIndex,
        out ISlangBlob* outCode,
        out ISlangBlob* outDiagnostics);

    SlangResult GetTargetMetadata(
        SlangInt targetIndex,
        out IMetadata* outMetadata,
        out ISlangBlob* outDiagnostics);

    SlangResult GetEntryPointMetadata(
        SlangInt entryPointIndex,
        SlangInt targetIndex,
        out IMetadata* outMetadata,
        out ISlangBlob* outDiagnostics);
}


[UUID(0x8f241361, 0xf5bd, 0x4ca0, 0xa3, 0xac, 0x2, 0xf7, 0xfa, 0x24, 0x2, 0xb8)]
public interface IEntryPoint : IComponentType
{
    /* FunctionReflection */
    void GetFunctionReflection();
}


[UUID(0x73eb3147, 0xe544, 0x41b5, 0xb8, 0xf0, 0xa2, 0x44, 0xdf, 0x21, 0x94, 0xb)]
public interface ITypeConformance : IComponentType
{
}


/** A module is the granularity of shader code compilation and loading.

In most cases a module corresponds to a single compile "translation unit."
This will often be a single `.slang` or `.hlsl` file and everything it
`#include`s.

Notably, a module `M` does *not* include the things it `import`s, as these
as distinct modules that `M` depends on. There is a directed graph of
module dependencies, and all modules in the graph must belong to the
same session (`ISession`).

A module establishes a namespace for looking up types, functions, etc.
*/
[UUID(0xc720e64, 0x8722, 0x4d31, 0x89, 0x90, 0x63, 0x8a, 0x98, 0xb1, 0xc2, 0x79)]
public unsafe interface IModule : IComponentType
{
    /// Find and an entry point by name.
    /// Note that this does not work in case the function is not explicitly designated as an entry
    /// point, e.g. using a `[shader("...")]` attribute. In such cases, consider using
    /// `IModule::findAndCheckEntryPoint` instead.
    SlangResult FindEntryPointByName(ConstU8Str name, out IEntryPoint* outEntryPoint);

    /// Get number of entry points defined in the module. An entry point defined in a module
    /// is by default not included in the linkage, so calls to `IComponentType::getEntryPointCount`
    /// on an `IModule` instance will always return 0. However `IModule::getDefinedEntryPointCount`
    /// will return the number of defined entry points.
    SlangInt32 GetDefinedEntryPointCount();

    /// Get the name of an entry point defined in the module.
    SlangResult GetDefinedEntryPoint(SlangInt32 index, out IEntryPoint* outEntryPoint);

    /// Get a serialized representation of the checked module.
    SlangResult Serialize(out ISlangBlob* outSerializedBlob);

    /// Write the serialized representation of this module to a file.
    SlangResult WriteToFile(ConstU8Str fileName);

    /// Get the name of the module.
    ConstU8Str GetName();

    /// Get the path of the module.
    ConstU8Str GetFilePath();

    /// Get the unique identity of the module.
    ConstU8Str GetUniqueIdentity();

    /// Find and validate an entry point by name, even if the function is
    /// not marked with the `[shader("...")]` attribute.
    SlangResult FindAndCheckEntryPoint(
        ConstU8Str name,
        SlangStage stage,
        out IEntryPoint* outEntryPoint,
        out ISlangBlob* outDiagnostics);

    /// Get the number of dependency files that this module depends on.
    /// This includes both the explicit source files, as well as any
    /// additional files that were transitively referenced (e.g., via
    /// a `#include` directive).
    SlangInt32 GetDependencyFileCount();

    /// Get the path to a file this module depends on.
    ConstU8Str GetDependencyFilePath(SlangInt32 index);

    DeclReflection* GetModuleReflection();

    /** Disassemble a module.
     */
    SlangResult Disassemble(out ISlangBlob* outDisassembledBlob);
}

/* Experimental interface for doing target precompilation of slang modules */
[UUID(0x8e12e8e3, 0x5fcd, 0x433e, 0xaf, 0xcb, 0x13, 0xa0, 0x88, 0xbc, 0x5e, 0xe5)]
public unsafe interface IModulePrecompileService_Experimental : IUnknown
{
    SlangResult PrecompileForTarget(SlangCompileTarget target, out ISlangBlob* outDiagnostics);

    SlangResult GetPrecompiledTargetCode(
        SlangCompileTarget target,
        out ISlangBlob* outCode,
        out ISlangBlob* outDiagnostics);

    SlangInt getModuleDependencyCount();

    SlangResult getModuleDependency(
        SlangInt dependencyIndex,
        out IModule* outModule,
        out ISlangBlob* outDiagnostics);
}


/* Description of a Slang global session.
 */
[StructLayout(LayoutKind.Sequential)]
unsafe struct SlangGlobalSessionDesc()
{
    /// Size of this struct.
    uint structureSize = (uint)sizeof(SlangGlobalSessionDesc);

    /// Slang API version.
    uint apiVersion = 0;

    /// Slang language version.
    uint languageVersion = 2025;

    /// Whether to enable GLSL support.
    bool enableGLSL = false;

    /// Reserved for future use.
    fixed uint reserved[16];
};
