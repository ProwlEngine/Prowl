// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Slang.Native;


namespace Prowl.Slang;


/// <summary>
/// <para>
/// A global session for interaction with the Slang library.
/// </para>
/// <para>
/// An application may create and re-use a single global session across
/// multiple sessions, in order to amortize startups costs (in current
/// Slang this is mostly the cost of loading the Slang standard library).
/// </para>
/// <para>
/// The global session is currently *not* thread-safe and objects created from
/// a single global session should only be used from a single thread at
/// a time.
/// </para>
/// </summary>
public static unsafe class GlobalSession
{
    internal static readonly IGlobalSession s_session = CreateSession();

    private static IGlobalSession CreateSession()
    {
        SlangResult result = SlangNative.slang_createGlobalSession(0, out IGlobalSession* globalSessionPtr);

        if (!result.IsOk())
            throw new InitializationException("Failed to initialize Global Session", result.GetException()!);

        return NativeComProxy.Create(globalSessionPtr);
    }


    /// <summary>
    /// Create a new session for loading and compiling code.
    /// </summary>
    public static Session CreateSession(in SessionDescription description)
    {
        Native.SessionDescription nativeDesc = new();

        nativeDesc.Allocate(description, out FileSystem? fsAllocation);

        s_session.CreateSession(&nativeDesc, out ISession* sessionPtr);

        nativeDesc.Free(fsAllocation);

        return new Session(NativeComProxy.Create(sessionPtr));
    }


    /// <summary>
    /// <para>
    /// Look up the internal ID of a profile by its `name`.
    /// </para>
    /// <para>
    /// Profile IDs are *not* guaranteed to be stable across versions
    /// of the Slang library, so clients are expected to look up
    /// profiles by name at runtime.
    /// </para>
    /// </summary>
    public static SlangProfileID FindProfile(string name)
    {
        using U8Str str = U8Str.Alloc(name);

        SlangProfileID ID = s_session.FindProfile(str);

        return ID;
    }


    /// <summary>
    /// Set the path that downstream compilers (aka back end compilers) will
    /// be looked from.
    /// </summary>
    /// <param name="passThrough">Identifies the downstream compiler.</param>
    /// <param name="path">The path to find the downstream compiler(shared library/dll/executable).</param>
    /// <remarks>
    /// For back ends that are dlls/shared libraries, it will mean the path will
    /// be prefixed with the path when calls are made out to ISlangSharedLibraryLoader.
    /// For executables - it will look for executables along the path
    /// </remarks>
    public static void SetDownstreamCompilerPath(SlangPassThrough passThrough, string path)
    {
        using U8Str str = U8Str.Alloc(path);

        s_session.SetDownstreamCompilerPath(passThrough, str);
    }

    /// <summary>
    /// Get the build version 'tag' string. The string is the same as produced via `git describe
    /// --tags` for the project. If Slang is built separately from the automated build scripts the
    /// contents will by default be 'unknown'.
    /// </summary>
    public static string GetBuildTagString()
    {
        return s_session.GetBuildTagString().String;
    }

    /// <summary>
    /// For a given source language set the default compiler.
    /// If a default cannot be chosen (for example the target cannot be achieved by the default),
    /// the default will not be used.
    /// </summary>
    /// <param name="sourceLanguage">The source language.</param>
    /// <param name="defaultCompiler">The default compiler for that language.</param>
    public static void SetDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage, SlangPassThrough defaultCompiler)
    {
        s_session.SetDefaultDownstreamCompiler(sourceLanguage, defaultCompiler).Throw();
    }

    /// <summary>
    /// For a source type get the default compiler.
    /// </summary>
    /// <param name="sourceLanguage">The source language.</param>
    /// <returns>The downstream compiler for that source language.</returns>
    public static SlangPassThrough GetDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage)
    {
        return s_session.GetDefaultDownstreamCompiler(sourceLanguage);
    }

    /// <summary>
    /// /* Set the 'prelude' placed before generated code for a specific language type.
    /// </summary>
    /// <param name="sourceLanguage">The language the prelude should be inserted on.</param>
    /// <param name="preludeText">The text added pre-pended verbatim before the generated source.</param>
    /// <remarks>
    /// Note: For pass-through usage, prelude is not pre-pended, preludes are for code generation
    /// only.
    /// </remarks>
    public static void SetLanguagePrelude(SlangSourceLanguage sourceLanguage, string preludeText)
    {
        using U8Str str = U8Str.Alloc(preludeText);

        s_session.SetLanguagePrelude(sourceLanguage, str);
    }

    /// <summary>
    /// Get the 'prelude' associated with a specific source language.
    /// </summary>
    /// <param name="sourceLanguage">The language the prelude should be inserted on.</param>
    /// <returns>A string that holds the language prelude.</returns>
    public static string GetLanguagePrelude(SlangSourceLanguage sourceLanguage)
    {
        s_session.GetLanguagePrelude(sourceLanguage, out ISlangBlob* blobPtr);
        ISlangBlob blob = NativeComProxy.Create(blobPtr);

        return blob.GetString();
    }

    /// <summary>
    /// Add new builtin declarations to be used in subsequent compiles.
    /// </summary>
    /// <param name="sourcePath">The source path for the builtin declaration to use.</param>
    /// <param name="sourceString">The source string of the declaration.</param>
    public static void AddBuiltins(string sourcePath, string sourceString)
    {
        using U8Str strA = U8Str.Alloc(sourcePath);
        using U8Str strB = U8Str.Alloc(sourceString);

        s_session.AddBuiltins(strA, strB);
    }

    /// <summary>
    /// Set the session shared library loader. If this changes the loader, it may cause shared
    /// libraries to be unloaded
    /// </summary>
    /// <param name="loader">The loader to set. Setting null sets the default loader.</param>
    internal static void SetSharedLibraryLoader(SharedLibraryLoader loader)
    {
        s_session.SetSharedLibraryLoader(loader);
    }

    /// <summary>
    /// Gets the currently set shared library loader.
    /// </summary>
    /// <returns>Gets the currently set loader. If returns null, it's the default loader</returns>
    internal static ISlangSharedLibraryLoader? GetSharedLibraryLoader()
    {
        ISlangSharedLibraryLoader* loaderPtr = s_session.GetSharedLibraryLoader();

        if (loaderPtr != null)
            return NativeComProxy.Create(loaderPtr);

        return null;
    }


    /// <summary>
    /// Checks if the compilation target is supported for this session
    /// </summary>
    /// <param name="target">The compilation target to test.</param>
    /// <param name="notImplemented">True if not implemented in this build.</param>
    /// <param name="notFound">True if other resources (such as shared libraries) required to make target work could not be found.</param>
    /// <returns>True if the target is available, false otherwise.</returns>
    public static bool CheckCompileTargetSupport(SlangCompileTarget target, out bool notImplemented, out bool notFound)
    {
        SlangResult result = s_session.CheckCompileTargetSupport(target);

        notImplemented = result == SlangResult.NotImplemented;
        notFound = result == SlangResult.NotFound;

        return result.IsOk();
    }


    /// <summary>
    /// Checks if the pass through support is supported for this session
    /// </summary>
    /// <param name="passThrough">The pass through type to test.</param>
    /// <param name="notImplemented">True if not implemented in this build.</param>
    /// <param name="notFound">True if other resources(such as shared libraries) required to make target work could not be found.</param>
    /// <returns>Trie if passthroughs are supported, false otherwise.</returns>
    public static bool CheckPassThroughSupport(SlangPassThrough passThrough, out bool notImplemented, out bool notFound)
    {
        SlangResult result = s_session.CheckPassThroughSupport(passThrough);

        notImplemented = result == SlangResult.NotImplemented;
        notFound = result == SlangResult.NotFound;

        return result.IsOk();
    }


    /// <summary>
    /// Look up the internal ID of a capability by its `name`.
    /// Capability IDs are *not* guaranteed to be stable across versions
    /// of the Slang library, so clients are expected to look up
    /// capabilities by name at runtime.
    /// </summary>
    /// <param name="name">The capability name to search for.</param>
    /// <returns>The internal capability ID.</returns>
    public static SlangCapabilityID FindCapability(string name)
    {
        using U8Str str = U8Str.Alloc(name);

        SlangCapabilityID id = s_session.FindCapability(str);

        return id;
    }


    /// <summary>
    /// Set the downstream/pass through compiler to be used for a transition from the source type to
    /// the target type.
    /// </summary>
    /// <param name="source">The source 'code gen target'</param>
    /// <param name="target">The target 'code gen target'</param>
    /// <param name="compiler">The compiler/pass through to use for the transition from source to target.</param>
    public static void SetDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target, SlangPassThrough compiler)
    {
        s_session.SetDownstreamCompilerForTransition(source, target, compiler);
    }


    /// <summary>
    /// Get the downstream/pass through compiler for a transition specified by source and target.
    /// </summary>
    /// <param name="source">The source 'code gen target'</param>
    /// <param name="target">The target 'code gen target'</param>
    /// <returns>The compiler that is used for the transition. Returns 'None' if is not defined.</returns>
    public static SlangPassThrough GetDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target)
    {
        return s_session.GetDownstreamCompilerForTransition(source, target);
    }


    /// <summary>
    /// Get the time in seconds spent in the slang and downstream compiler.
    /// </summary>
    /// <param name="outTotalTime">Total time in seconds spent in the slang compiler.</param>
    /// <param name="outDownstreamTime">Total time in seconds spent by the downstream compiler.</param>
    public static void GetCompilerElapsedTime(out double outTotalTime, out double outDownstreamTime)
    {
        s_session.GetCompilerElapsedTime(out outTotalTime, out outDownstreamTime);
    }

    /// <summary>
    /// Specify a spirv.core.grammar.json file to load and use when
    /// parsing and checking any SPIR-V code
    /// </summary>
    /// <param name="jsonPath">JSON source grammar path.</param>
    public static void SetSPIRVCoreGrammar(string jsonPath)
    {
        using U8Str str = U8Str.Alloc(jsonPath);

        s_session.SetSPIRVCoreGrammar(str).Throw();
    }


    /// <summary>
    /// Parse slangc command line options into a SessionDesc that can be used to create a session
    /// with all the compiler options specified in the command line.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The resulting parsed session description.</returns>
    public static SessionDescription ParseCommandLineArguments(string[] args)
    {
        U8Str[] strs = [.. args.Select(U8Str.Alloc)];

        ConstU8Str* strsPtr = stackalloc ConstU8Str[strs.Length];

        for (int i = 0; i < strs.Length; i++)
            strsPtr[i] = strs[i];

        try
        {
            Native.SessionDescription* outSession = null;

            s_session.ParseCommandLineArguments(strs.Length, strsPtr, outSession, out IUnknown* allocationPtr).Throw();

            IUnknown allocation = NativeComProxy.Create(allocationPtr);

            return outSession->Read();
        }
        finally
        {
            Array.ForEach(strs, U8Str.Free);
        }
    }

    /// <summary>
    /// Computes a digest that uniquely identifies the session description.
    /// </summary>
    public static string GetSessionDescDigest(in SessionDescription sessionDesc)
    {
        Native.SessionDescription nativeDesc = new();

        nativeDesc.Allocate(sessionDesc, out FileSystem? fsAllocation);

        s_session.GetSessionDescDigest(&nativeDesc, out ISlangBlob* outBlobPtr);

        nativeDesc.Free(fsAllocation);

        return NativeComProxy.Create(outBlobPtr).GetString();
    }
}
