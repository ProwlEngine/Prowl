// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Slang.NativeAPI;

namespace Prowl.Slang;

public class SlangBlob : IDisposable
{
    private ISlangBlob? _nativeBlob;

    internal unsafe SlangBlob(ISlangBlob* blobPtr)
    {
        if (blobPtr == null)
            throw new ArgumentNullException(nameof(blobPtr));
        _nativeBlob = NativeComProxy.Create(blobPtr);
    }

    public unsafe IntPtr GetBufferPointer()
    {
        if (_nativeBlob == null)
            throw new ObjectDisposedException(nameof(SlangBlob));
        return (IntPtr)_nativeBlob.GetBufferPointer();
    }

    public nuint GetBufferSize()
    {
        if (_nativeBlob == null)
            throw new ObjectDisposedException(nameof(SlangBlob));
        return _nativeBlob.GetBufferSize();
    }

    public unsafe byte[] ToByteArray()
    {
        if (_nativeBlob == null)
            throw new ObjectDisposedException(nameof(SlangBlob));

        void* bufferPtr = _nativeBlob.GetBufferPointer();
        nuint bufferSize = _nativeBlob.GetBufferSize();

        if (bufferPtr == null || bufferSize == 0)
            return Array.Empty<byte>();

        byte[] bytes = new byte[bufferSize];
        Marshal.Copy((IntPtr)bufferPtr, bytes, 0, (int)bufferSize);
        return bytes;
    }

    public unsafe string? ConvertToString()
    {
        if (_nativeBlob == null)
            throw new ObjectDisposedException(nameof(SlangBlob));

        void* bufferPtr = _nativeBlob.GetBufferPointer();
        nuint bufferSize = _nativeBlob.GetBufferSize();

        if (bufferPtr == null || bufferSize == 0)
            return null;

        return Marshal.PtrToStringUTF8((IntPtr)bufferPtr, (int)bufferSize);
    }

    public void Dispose()
    {
        _nativeBlob?.Release();
        _nativeBlob = null;
        GC.SuppressFinalize(this);
    }

    ~SlangBlob()
    {
        Dispose();
    }
}

public class SlangGlobalSession : IDisposable
{
    private IGlobalSession? _nativeGlobalSession;

    private unsafe SlangGlobalSession(IGlobalSession* globalSessionPtr)
    {
        if (globalSessionPtr == null)
            throw new ArgumentNullException(nameof(globalSessionPtr), "Native global session pointer cannot be null.");

        _nativeGlobalSession = NativeComProxy.Create(globalSessionPtr);
    }

    internal IGlobalSession Native => _nativeGlobalSession ?? throw new InvalidOperationException("Global session has been disposed.");

    public static unsafe SlangGlobalSession Create(int apiVersion = 0)
    {
        SlangResult result = SlangNative.slang_createGlobalSession(apiVersion, out IGlobalSession* globalSessionPtr);
        result.Throw();

        if (globalSessionPtr == null)
            throw new InvalidOperationException("Failed to create Slang global session (pointer is null).");

        return new SlangGlobalSession(globalSessionPtr);
    }

    public unsafe SlangSession CreateSession(SlangSessionDesc description)
    {
        if (description == null)
            throw new ArgumentNullException(nameof(description));

        NativeAPI.SessionDesc nativeDesc = default;
        var handles = new List<GCHandle>();
        var allocatedMemory = new List<IntPtr>();
        ManagedComProxy<ISlangFileSystem> fsProxyToDispose = null;

        try
        {
            nativeDesc.structureSize = (nuint)sizeof(NativeAPI.SessionDesc);
            nativeDesc.flags = description.Flags;
            nativeDesc.defaultMatrixLayoutMode = description.DefaultMatrixLayoutMode;
            nativeDesc.enableEffectAnnotations = description.EnableEffectAnnotations;
            nativeDesc.allowGLSLSyntax = description.AllowGLSLSyntax;

            MarshalTargets(description, ref nativeDesc, handles, allocatedMemory);
            MarshalSearchPaths(description, ref nativeDesc, handles, allocatedMemory);
            MarshalPreprocessorMacros(description, ref nativeDesc, handles, allocatedMemory);
            MarshalFileSystem(description, ref nativeDesc);
            MarshalCompilerOptionEntries(description, ref nativeDesc, handles, allocatedMemory);

            SlangResult result = Native.CreateSession(&nativeDesc, out ISession* sessionPtr);
            result.Throw();

            if (sessionPtr == null)
                throw new InvalidOperationException("Failed to create Slang session (pointer is null).");

            return new SlangSession(sessionPtr, fsProxyToDispose);
        }
        finally
        {
            foreach (GCHandle handle in handles)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
            foreach (IntPtr ptr in allocatedMemory)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    private unsafe void MarshalTargets(SlangSessionDesc description, ref NativeAPI.SessionDesc nativeDesc,
        List<GCHandle> handles, List<IntPtr> allocatedMemory)
    {
        if (description.Targets != null && description.Targets.Count > 0)
        {
            nativeDesc.targetCount = description.Targets.Count;
            var nativeTargets = (NativeAPI.TargetDesc*)Marshal.AllocHGlobal(sizeof(NativeAPI.TargetDesc) * description.Targets.Count);
            allocatedMemory.Add((IntPtr)nativeTargets);

            for (int i = 0; i < description.Targets.Count; i++)
            {
                if (description.Targets[i] == null)
                    throw new ArgumentNullException(nameof(description.Targets), "Target description cannot be null.");
                nativeTargets[i] = description.Targets[i].ToNative(handles, allocatedMemory);
            }
            nativeDesc.targets = nativeTargets;
        }
    }

    private unsafe void MarshalSearchPaths(SlangSessionDesc description, ref NativeAPI.SessionDesc nativeDesc,
        List<GCHandle> handles, List<IntPtr> allocatedMemory)
    {
        if (description.SearchPaths != null && description.SearchPaths.Count > 0)
        {
            nativeDesc.searchPathCount = description.SearchPaths.Count;
            var nativeSearchPaths = (NativeAPI.ConstU8Str*)Marshal.AllocHGlobal(sizeof(NativeAPI.ConstU8Str) * description.SearchPaths.Count);
            allocatedMemory.Add((IntPtr)nativeSearchPaths);

            for (int i = 0; i < description.SearchPaths.Count; i++)
            {
                if (string.IsNullOrEmpty(description.SearchPaths[i]))
                    throw new ArgumentNullException(nameof(description.SearchPaths), "Search path cannot be null or empty.");

                byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(description.SearchPaths[i] + "\0");
                GCHandle handle = GCHandle.Alloc(utf8Bytes, GCHandleType.Pinned);
                handles.Add(handle);
                nativeSearchPaths[i].Data = (byte*)handle.AddrOfPinnedObject();
            }
            nativeDesc.searchPaths = nativeSearchPaths;
        }
    }

    private unsafe void MarshalPreprocessorMacros(SlangSessionDesc description, ref NativeAPI.SessionDesc nativeDesc,
        List<GCHandle> handles, List<IntPtr> allocatedMemory)
    {
        if (description.PreprocessorMacros != null && description.PreprocessorMacros.Count > 0)
        {
            nativeDesc.preprocessorMacroCount = description.PreprocessorMacros.Count;
            var nativeMacros = (NativeAPI.PreprocessorMacroDesc*)Marshal.AllocHGlobal(sizeof(NativeAPI.PreprocessorMacroDesc) * description.PreprocessorMacros.Count);
            allocatedMemory.Add((IntPtr)nativeMacros);

            for (int i = 0; i < description.PreprocessorMacros.Count; i++)
            {
                var macro = description.PreprocessorMacros[i];
                if (macro == null)
                    throw new ArgumentNullException(nameof(description.PreprocessorMacros), "Preprocessor macro cannot be null.");

                byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(macro.Name + "\0");
                GCHandle nameHandle = GCHandle.Alloc(nameBytes, GCHandleType.Pinned);
                handles.Add(nameHandle);
                nativeMacros[i].name.Data = (byte*)nameHandle.AddrOfPinnedObject();

                byte[] valueBytes = System.Text.Encoding.UTF8.GetBytes(macro.Value + "\0");
                GCHandle valueHandle = GCHandle.Alloc(valueBytes, GCHandleType.Pinned);
                handles.Add(valueHandle);
                nativeMacros[i].value.Data = (byte*)valueHandle.AddrOfPinnedObject();
            }
            nativeDesc.preprocessorMacros = nativeMacros;
        }
    }

    private unsafe void MarshalFileSystem(SlangSessionDesc description, ref NativeAPI.SessionDesc nativeDesc)
    {
        if (description.FileSystem != null)
        {
            if (description.FileSystem is ManagedComProxy<ISlangFileSystem> managedProxy)
            {
                nativeDesc.fileSystem = managedProxy.NativeRef;
            }
            else
            {
                throw new ArgumentException("FileSystem must be a ManagedComProxy<ISlangFileSystem>.", nameof(description.FileSystem));
            }
        }
    }

    private unsafe void MarshalCompilerOptionEntries(SlangSessionDesc description, ref NativeAPI.SessionDesc nativeDesc,
        List<GCHandle> handles, List<IntPtr> allocatedMemory)
    {
        if (description.CompilerOptionEntries != null && description.CompilerOptionEntries.Count > 0)
        {
            nativeDesc.compilerOptionEntryCount = (uint)description.CompilerOptionEntries.Count;
            var nativeOptions = (NativeAPI.CompilerOptionEntry*)Marshal.AllocHGlobal(sizeof(NativeAPI.CompilerOptionEntry) * description.CompilerOptionEntries.Count);
            allocatedMemory.Add((IntPtr)nativeOptions);

            for (int i = 0; i < description.CompilerOptionEntries.Count; i++)
            {
                if (description.CompilerOptionEntries[i] == null)
                    throw new ArgumentNullException(nameof(description.CompilerOptionEntries), "Compiler option entry cannot be null.");
                nativeOptions[i] = description.CompilerOptionEntries[i].ToNative(handles, allocatedMemory);
            }
            nativeDesc.compilerOptionEntries = nativeOptions;
        }
    }

    public unsafe SlangProfileID FindProfile(string profileName)
    {
        U8Str u8ProfileName = U8Str.Alloc(profileName);
        try
        {
            return Native.FindProfile(u8ProfileName);
        }
        finally
        {
            U8Str.Free(u8ProfileName);
        }
    }

    public static unsafe void Shutdown()
    {
        SlangNative.slang_shutdown();
    }

    public void Dispose()
    {
        _nativeGlobalSession?.Release();
        _nativeGlobalSession = null;
        GC.SuppressFinalize(this);
    }

    ~SlangGlobalSession()
    {
        Dispose();
    }
}

public class SlangCompilerOptionEntry
{
    public CompilerOptionName Name { get; set; }
    public CompilerOptionValueKind ValueKind { get; private set; }
    public int IntValue0 { get; private set; }
    public int IntValue1 { get; private set; }
    public string StringValue0 { get; private set; }
    public string StringValue1 { get; private set; }

    private SlangCompilerOptionEntry(CompilerOptionName name)
    {
        Name = name;
    }

    public static SlangCompilerOptionEntry CreateIntEntry(CompilerOptionName name, int val0, int val1 = 0)
    {
        return new SlangCompilerOptionEntry(name)
        {
            ValueKind = CompilerOptionValueKind.Int,
            IntValue0 = val0,
            IntValue1 = val1
        };
    }

    public static SlangCompilerOptionEntry CreateStringEntry(CompilerOptionName name, string val0, string val1 = null)
    {
        return new SlangCompilerOptionEntry(name)
        {
            ValueKind = CompilerOptionValueKind.String,
            StringValue0 = val0,
            StringValue1 = val1
        };
    }

    internal unsafe CompilerOptionEntry ToNative(List<GCHandle> handles, List<IntPtr> allocatedMemory)
    {
        CompilerOptionEntry nativeEntry = default;
        nativeEntry.name = Name;
        nativeEntry.value.kind = ValueKind;

        if (ValueKind == CompilerOptionValueKind.Int)
        {
            nativeEntry.value.intValue0 = IntValue0;
            nativeEntry.value.intValue1 = IntValue1;
        }
        else
        {
            if (StringValue0 != null)
            {
                byte[] s0Bytes = System.Text.Encoding.UTF8.GetBytes(StringValue0 + "\0");
                GCHandle s0Handle = GCHandle.Alloc(s0Bytes, GCHandleType.Pinned);
                handles.Add(s0Handle);
                nativeEntry.value.stringValue0.Data = (byte*)s0Handle.AddrOfPinnedObject();
            }

            if (StringValue1 != null)
            {
                byte[] s1Bytes = System.Text.Encoding.UTF8.GetBytes(StringValue1 + "\0");
                GCHandle s1Handle = GCHandle.Alloc(s1Bytes, GCHandleType.Pinned);
                handles.Add(s1Handle);
                nativeEntry.value.stringValue1.Data = (byte*)s1Handle.AddrOfPinnedObject();
            }
        }
        return nativeEntry;
    }
}

public class SlangPreprocessorMacro
{
    public string Name { get; set; }
    public string Value { get; set; }

    public SlangPreprocessorMacro(string name, string value)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}

public class SlangTargetDesc
{
    public SlangCompileTarget Format { get; set; } = SlangCompileTarget.TARGET_UNKNOWN;
    public SlangProfileID Profile { get; set; } = SlangProfileID.UNKNOWN;
    public SlangTargetFlags Flags { get; set; } = SlangTargetFlags.Default;
    public SlangFloatingPointMode FloatingPointMode { get; set; } = SlangFloatingPointMode.DEFAULT;
    public SlangLineDirectiveMode LineDirectiveMode { get; set; } = SlangLineDirectiveMode.DEFAULT;
    public bool ForceGLSLScalarBufferLayout { get; set; } = false;
    public List<SlangCompilerOptionEntry> CompilerOptionEntries { get; set; } = new List<SlangCompilerOptionEntry>();

    public SlangTargetDesc() { }

    internal unsafe TargetDesc ToNative(List<GCHandle> handles, List<IntPtr> allocatedMemory)
    {
        TargetDesc nativeDesc = default;
        nativeDesc.structureSize = (nuint)sizeof(TargetDesc);
        nativeDesc.format = Format;
        nativeDesc.profile = Profile;
        nativeDesc.flags = Flags;
        nativeDesc.floatingPointMode = FloatingPointMode;
        nativeDesc.lineDirectiveMode = LineDirectiveMode;
        nativeDesc.forceGLSLScalarBufferLayout = ForceGLSLScalarBufferLayout;

        if (CompilerOptionEntries != null && CompilerOptionEntries.Count > 0)
        {
            nativeDesc.compilerOptionEntryCount = (uint)CompilerOptionEntries.Count;
            CompilerOptionEntry* nativeOptions = (CompilerOptionEntry*)Marshal.AllocHGlobal(sizeof(CompilerOptionEntry) * CompilerOptionEntries.Count);
            allocatedMemory.Add((IntPtr)nativeOptions);

            for (int i = 0; i < CompilerOptionEntries.Count; i++)
            {
                nativeOptions[i] = CompilerOptionEntries[i].ToNative(handles, allocatedMemory);
            }
            nativeDesc.compilerOptionEntries = nativeOptions;
        }
        else
        {
            nativeDesc.compilerOptionEntries = null;
            nativeDesc.compilerOptionEntryCount = 0;
        }
        return nativeDesc;
    }
}

public class SlangSessionDesc
{
    public List<SlangTargetDesc> Targets { get; set; } = new List<SlangTargetDesc>();
    public SessionFlags Flags { get; set; } = SessionFlags.None;
    public SlangMatrixLayoutMode DefaultMatrixLayoutMode { get; set; } = SlangMatrixLayoutMode.ROW_MAJOR;
    public List<string> SearchPaths { get; set; } = new List<string>();
    public List<SlangPreprocessorMacro> PreprocessorMacros { get; set; } = new List<SlangPreprocessorMacro>();
    public ISlangFileSystem FileSystem { get; set; }
    public bool EnableEffectAnnotations { get; set; } = false;
    public bool AllowGLSLSyntax { get; set; } = false;
    public List<SlangCompilerOptionEntry> CompilerOptionEntries { get; set; } = new List<SlangCompilerOptionEntry>();

    public SlangSessionDesc() { }
}

public class SlangSession : IDisposable
{
    private ISession? _nativeSession;
    private readonly ManagedComProxy<ISlangFileSystem>? _ownedFileSystemProxy;

    internal unsafe SlangSession(ISession* sessionPtr, ManagedComProxy<ISlangFileSystem>? fileSystemProxy = null)
    {
        if (sessionPtr == null)
            throw new ArgumentNullException(nameof(sessionPtr), "Native session pointer cannot be null.");
        _nativeSession = NativeComProxy.Create(sessionPtr);
        _ownedFileSystemProxy = fileSystemProxy;
    }

    public ISession Native => _nativeSession ?? throw new InvalidOperationException("Session has been disposed.");

    public unsafe SlangModule LoadModule(string moduleName, out string? diagnostics)
    {
        diagnostics = null;
        U8Str u8ModuleName = U8Str.Alloc(moduleName);
        ISlangBlob* diagnosticsBlob = null;
        IModule* modulePtr = null;

        try
        {
            modulePtr = Native.LoadModule(u8ModuleName, out diagnosticsBlob);

            if (diagnosticsBlob != null)
            {
                using var slangDiagBlob = new SlangBlob(diagnosticsBlob);
                diagnostics = slangDiagBlob.ConvertToString();
            }

            if (modulePtr == null)
                return null;

            return new SlangModule(modulePtr);
        }
        finally
        {
            U8Str.Free(u8ModuleName);
        }
    }

    public unsafe SlangCompositeComponentType? CreateCompositeComponentType(
        IEnumerable<SlangComponentType> componentTypes,
        out string? diagnostics)
    {
        diagnostics = null;
        if (componentTypes == null)
            throw new ArgumentNullException(nameof(componentTypes));

        var componentTypeList = componentTypes.ToList();
        if (!componentTypeList.Any())
        {
            diagnostics = "Cannot create a composite component type from an empty list of component types.";
            return null;
        }

        int count = componentTypeList.Count;
        IComponentType** nativeComponentTypePtrsArray = (IComponentType**)Marshal.AllocHGlobal(IntPtr.Size * count);
        if (nativeComponentTypePtrsArray == null)
            throw new OutOfMemoryException("Failed to allocate memory for component type pointers.");

        ISlangBlob* diagnosticsBlob = null;
        IComponentType* outCompositeComponentTypePtr = null;

        try
        {
            for (int i = 0; i < count; i++)
            {
                SlangComponentType managedComponent = componentTypeList[i];
                if (managedComponent == null)
                    throw new ArgumentException($"Component type at index {i} is null.", nameof(componentTypes));

                nativeComponentTypePtrsArray[i] = managedComponent.NativePointer;
            }

            SlangResult result = Native.CreateCompositeComponentType(
                nativeComponentTypePtrsArray,
                count,
                out outCompositeComponentTypePtr,
                out diagnosticsBlob);

            if (diagnosticsBlob != null)
            {
                using var slangDiagBlob = new SlangBlob(diagnosticsBlob);
                diagnostics = slangDiagBlob.ConvertToString();
            }

            if (!result.IsOk() || outCompositeComponentTypePtr == null)
                return null;

            return new SlangCompositeComponentType(outCompositeComponentTypePtr);
        }
        finally
        {
            if (nativeComponentTypePtrsArray != null)
                Marshal.FreeHGlobal((IntPtr)nativeComponentTypePtrsArray);
        }
    }

    public void Dispose()
    {
        _nativeSession?.Release();
        _nativeSession = null;
        _ownedFileSystemProxy?.Release();
        GC.SuppressFinalize(this);
    }

    ~SlangSession()
    {
        Dispose();
    }
}

public class SlangCompositeComponentType : SlangComponentType
{
    internal unsafe SlangCompositeComponentType(IComponentType* componentTypePtr)
        : base(componentTypePtr, NativeComProxy.Create(componentTypePtr))
    {
    }
}

public class SlangReflectionLayout
{
    private readonly unsafe ShaderReflection* _nativeReflectionPtr;
    private readonly SlangComponentType _owner;

    internal unsafe SlangReflectionLayout(ShaderReflection* reflectionPtr, SlangComponentType owner)
    {
        if (reflectionPtr == null)
            throw new ArgumentNullException(nameof(reflectionPtr));
        _nativeReflectionPtr = reflectionPtr;
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public unsafe ShaderReflection* NativePointer => _nativeReflectionPtr;

    public uint GetParameterCount()
    {
        unsafe { return _nativeReflectionPtr->getParameterCount(); }
    }

    public unsafe string? ToJson(out string? diagnostics)
    {
        diagnostics = null;

        if (_owner == null || _nativeReflectionPtr == null)
            throw new ObjectDisposedException(nameof(SlangReflectionLayout), "The parent component type or reflection data may have been disposed.");

        ISlangBlob* jsonBlobPtr = null;
        try
        {
            SlangResult result = _nativeReflectionPtr->toJson(out jsonBlobPtr);

            if (!result.IsOk())
            {
                diagnostics = $"Failed to serialize reflection to JSON. SlangResult: {result}";
                result.Throw();
                return null;
            }

            if (jsonBlobPtr == null)
            {
                diagnostics = "toJson call succeeded but returned a null blob for JSON data.";
                return null;
            }

            using var slangJsonBlob = new SlangBlob(jsonBlobPtr);
            return slangJsonBlob.ConvertToString();
        }
        catch (Exception ex)
        {
            diagnostics = $"Exception during ToJson: {ex.Message}";
            return null;
        }
    }
}

public abstract class SlangComponentType : IDisposable
{
    private unsafe IComponentType* _nativePointer;
    private readonly IComponentType _csInterfaceProxy;

    internal unsafe SlangComponentType(IComponentType* nativePtr, IComponentType csProxy)
    {
        if (nativePtr == null)
            throw new ArgumentNullException(nameof(nativePtr));
        _nativePointer = nativePtr;
        _csInterfaceProxy = csProxy ?? throw new ArgumentNullException(nameof(csProxy));
    }

    internal IComponentType CsInterface => _csInterfaceProxy ?? throw new ObjectDisposedException(GetType().Name);
    internal unsafe IComponentType* NativePointer => _nativePointer == null ? throw new ObjectDisposedException(GetType().Name) : _nativePointer;

    public unsafe SlangReflectionLayout? GetLayout(int targetIndex, out string? diagnostics)
    {
        diagnostics = null;
        ShaderReflection* layoutPtr = CsInterface.GetLayout(targetIndex, out ISlangBlob* diagnosticsBlob);

        if (diagnosticsBlob != null)
        {
            using var slangDiagBlob = new SlangBlob(diagnosticsBlob);
            diagnostics = slangDiagBlob.ConvertToString();
        }

        if (layoutPtr == null)
            return null;

        return new SlangReflectionLayout(layoutPtr, this);
    }

    public unsafe SlangBlob GetEntryPointCode(int entryPointIndex, int targetIndex, out string? diagnostics)
    {
        diagnostics = null;
        ISlangBlob* codeBlobPtr = null;
        ISlangBlob* diagnosticsBlobPtr = null;

        try
        {
            SlangResult result = CsInterface.GetEntryPointCode(
                entryPointIndex,
                targetIndex,
                out codeBlobPtr,
                out diagnosticsBlobPtr);

            if (diagnosticsBlobPtr != null)
            {
                using var slangDiagBlob = new SlangBlob(diagnosticsBlobPtr);
                diagnostics = slangDiagBlob.ConvertToString();
            }

            if (!result.IsOk() || codeBlobPtr == null)
                return null;

            return new SlangBlob(codeBlobPtr);
        }
        catch (Exception ex)
        {
            diagnostics = $"{diagnostics}\nException during GetEntryPointCode: {ex.Message}";
            return null;
        }
    }

    public virtual unsafe void Dispose()
    {
        // Dont release SlangModule
        if (this is SlangModule)
            return;

        if (_csInterfaceProxy != null)
        {
            _csInterfaceProxy.Release();
            _nativePointer = null;
        }
        GC.SuppressFinalize(this);
    }

    ~SlangComponentType()
    {
        Dispose();
    }
}

public class SlangEntryPoint : SlangComponentType
{
    private IEntryPoint NativeEntryPointInterface => (IEntryPoint)CsInterface;

    internal unsafe SlangEntryPoint(IEntryPoint* entryPointPtr)
        : base((IComponentType*)entryPointPtr, NativeComProxy.Create(entryPointPtr))
    {
    }
}

public class SlangModule : SlangComponentType
{
    private IModule NativeModuleInterface => (IModule)CsInterface;

    internal unsafe SlangModule(IModule* modulePtr)
        : base((IComponentType*)modulePtr, NativeComProxy.Create(modulePtr))
    {
    }

    public string Name => NativeModuleInterface.GetName().String;
    public string FilePath => NativeModuleInterface.GetFilePath().String;
    public string UniqueIdentity => NativeModuleInterface.GetUniqueIdentity().String;

    public unsafe SlangEntryPoint? FindEntryPointByName(string entryPointName)
    {
        if (string.IsNullOrEmpty(entryPointName))
            throw new ArgumentException("Entry point name cannot be null or empty.", nameof(entryPointName));

        U8Str u8Name = U8Str.Alloc(entryPointName);
        try
        {
            SlangResult result = NativeModuleInterface.FindEntryPointByName(u8Name, out IEntryPoint* entryPointPtr);
            if (!result.IsOk() || entryPointPtr == null)
                return null;

            return new SlangEntryPoint(entryPointPtr);
        }
        finally
        {
            U8Str.Free(u8Name);
        }
    }

    public int GetDefinedEntryPointCount()
    {
        return NativeModuleInterface.GetDefinedEntryPointCount();
    }

    public unsafe SlangEntryPoint? GetDefinedEntryPoint(int index)
    {
        SlangResult result = NativeModuleInterface.GetDefinedEntryPoint(index, out IEntryPoint* entryPointPtr);
        if (!result.IsOk() || entryPointPtr == null)
            return null;

        return new SlangEntryPoint(entryPointPtr);
    }

    public unsafe SlangEntryPoint? FindAndCheckEntryPoint(string entryPointName, SlangStage stage, out string? diagnostics)
    {
        diagnostics = null;
        if (string.IsNullOrEmpty(entryPointName))
            throw new ArgumentException("Entry point name cannot be null or empty.", nameof(entryPointName));

        U8Str u8Name = U8Str.Alloc(entryPointName);
        try
        {
            SlangResult result = NativeModuleInterface.FindAndCheckEntryPoint(u8Name, stage, out IEntryPoint* entryPointPtr, out var diagnosticsBlob);

            if (diagnosticsBlob != null)
            {
                using var slangDiagBlob = new SlangBlob(diagnosticsBlob);
                diagnostics = slangDiagBlob.ConvertToString();
            }

            if (!result.IsOk() || entryPointPtr == null)
                return null;

            return new SlangEntryPoint(entryPointPtr);
        }
        finally
        {
            U8Str.Free(u8Name);
        }
    }

    public unsafe void WriteToFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException("File name cannot be null or empty.", nameof(fileName));

        U8Str u8FileName = U8Str.Alloc(fileName);
        try
        {
            SlangResult result = NativeModuleInterface.WriteToFile(u8FileName);
            result.Throw();
        }
        finally
        {
            U8Str.Free(u8FileName);
        }
    }
}
