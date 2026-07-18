using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Editor.Projects;


namespace Prowl.Editor;


/// <summary>
/// Compiles parsed <see cref="ShaderDefinition"/>s against the running editor's device (Vulkan only -
/// that is the only backend Graphite exposes). Owns a single shared Slang compiler session, opened
/// lazily on first use and torn down after <see cref="IdleWindDownMs"/> of inactivity, so a burst of
/// shader work (e.g. importing many shaders, or a scene load compiling many on-demand variants) reuses
/// one session instead of paying session-open cost per shader, while an idle editor holds nothing open.
/// Synchronous: the Slang compiler is not reentrant across concurrent sessions, so every entry point
/// serializes on <see cref="s_lock"/>.
/// </summary>
public static class CompilationWorker
{
    private const int IdleWindDownMs = 10_000;

    private static readonly object s_lock = new();
    private static SlangShaderCompiler? s_compiler;
    private static Timer? s_windDownTimer;
    private static Variant? s_fallbackVariant;


    /// <summary>
    /// A shared <see cref="IShaderCompiler"/> that routes through this worker's pooled session. Safe to
    /// hold onto indefinitely (e.g. attached to a <see cref="ShaderPass"/>) - the underlying session is
    /// opened and closed transparently around each call.
    /// </summary>
    public static IShaderCompiler Compiler => Shim.Instance;


    private sealed class Shim : IShaderCompiler
    {
        public static readonly Shim Instance = new();

        public IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass) => CompilationWorker.GetAxes(pass);

        public ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend) => CompilationWorker.Compile(pass, combo, backend);
    }


    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute is only intended to be used in application code or advanced source generator scenarios",
        Justification = "Registers the pooled compiler shim on Shader before any shader might need it.")]
    [ModuleInitializer]
    internal static void RegisterEditorCompiler()
    {
        Shader.EditorCompiler = Compiler;
        Shader.EditorFallbackProvider = GetFallbackVariant;
    }


    /// <summary>
    /// Binds <paramref name="definition"/> for import. With <paramref name="onDemand"/> false, every
    /// variant combination compiles immediately, matching the pre-on-demand behavior. With true, no
    /// variant is baked at import at all - <see cref="Shader.EditorCompiler"/> stays attached on the
    /// resulting <see cref="Shader"/> for the rest of the editor session (and again on every future
    /// cache-loaded relaunch, see <see cref="Shader.EnsureCreated"/>), so the first thing that actually
    /// asks for a variant - a draw call, the shader inspector, anything - compiles it right then through
    /// this same pooled worker. There's no separate "warm the default variant" step to skip: whichever
    /// variant is requested first just becomes the first one compiled.
    /// </summary>
    public static ShaderSnapshot CompileAll(ShaderDefinition definition, string moduleName, string modulePath, bool onDemand = false)
    {
        lock (s_lock)
        {
            try
            {
                Variant fallback = GetFallbackVariant();

                CompileMode mode = onDemand ? CompileMode.OnDemand : CompileMode.All;
                definition.Create(Graphics.Device, Shim.Instance, fallback, mode);

                return definition.Snapshot();
            }
            catch (Exception ex)
            {
                Debug.LogException(new Exception($"Failed to compile shader '{moduleName}' ({modulePath}): {ex.Message}", ex));
                throw;
            }
            finally
            {
                ScheduleWindDown();
            }
        }
    }


    /// <summary>
    /// The variant every pass falls back to when it can't resolve its own (see
    /// <see cref="ShaderDefinition.Create(GraphicsDevice, IShaderCompiler, Variant, CompileMode)"/>):
    /// the compiled built-in Invalid shader's only pass. Compiled once and cached for the process's
    /// lifetime. Locks independently rather than assuming a caller's lock, since it's also reachable
    /// standalone through <see cref="Shader.EditorFallbackProvider"/> (e.g. from
    /// <see cref="Shader.EnsureCreated"/> on a cache-loaded shader, off any <see cref="CompileAll"/>
    /// call) - safe to call from within an existing <see cref="s_lock"/> too, since <c>lock</c> is
    /// reentrant on the same thread. Deliberately bypasses <see cref="CompileAll"/> (which would recurse
    /// back into this method) and supplies an empty <see cref="Variant"/> of its own - if the Invalid
    /// shader itself fails to compile, that's an unrecoverable engine bug and should throw, not silently
    /// degrade further.
    /// </summary>
    private static Variant GetFallbackVariant()
    {
        lock (s_lock)
        {
            if (s_fallbackVariant != null)
                return s_fallbackVariant;

            try
            {
                EnsureSession();

                string source = Runtime.Resources.EmbeddedResources.ReadAllText("Assets/Defaults/Invalid.shader");
                ShaderDefinition definition = ShaderParser.Parse(source);
                definition.Create(Graphics.Device, Shim.Instance, new Variant(), CompileMode.All);

                s_fallbackVariant = definition.Passes![0].ActiveVariant;
                return s_fallbackVariant;
            }
            finally
            {
                ScheduleWindDown();
            }
        }
    }


    private static IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass)
    {
        lock (s_lock)
        {
            try
            {
                EnsureSession();
                return s_compiler!.GetAxes(pass);
            }
            finally
            {
                ScheduleWindDown();
            }
        }
    }


    private static ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)
    {
        lock (s_lock)
        {
            try
            {
                EnsureSession();
                return s_compiler!.Compile(pass, combo, backend);
            }
            finally
            {
                ScheduleWindDown();
            }
        }
    }


    /// <summary>Must be called with <see cref="s_lock"/> held.</summary>
    private static void EnsureSession()
    {
        s_windDownTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        if (s_compiler != null)
            return;

        s_compiler = new SlangShaderCompiler();
        s_compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
        s_compiler.BeginSession([new DirectoryInfo("/")], IncludeResolver);
    }


    /// <summary>Must be called with <see cref="s_lock"/> held. Restarts the idle countdown.</summary>
    private static void ScheduleWindDown()
    {
        s_windDownTimer ??= new Timer(_ => EndIdleSession(), null, Timeout.Infinite, Timeout.Infinite);
        s_windDownTimer.Change(IdleWindDownMs, Timeout.Infinite);
    }


    private static void EndIdleSession()
    {
        lock (s_lock)
        {
            s_compiler?.EndSession();
            s_compiler = null;
        }
    }


    private static Memory<byte>? IncludeResolver(string includePath)
    {
        // Do this for
        // a. safety reasons so a shader can't include some random system file
        // b. no crazy person can load a shader outside their project.
        if (Project.Current != null)
        {
            string assetsRoot = Path.GetFullPath(Project.Current.AssetsPath);
            string candidate = Path.GetFullPath(
                Path.Combine(assetsRoot, includePath));

            if (candidate.StartsWith(
                    assetsRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    candidate,
                    assetsRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(candidate))
                    return File.ReadAllBytes(candidate);
            }
        }

        string fileName = Path.GetFileName(includePath);
        try
        {
            return Runtime.Resources.EmbeddedResources.ReadAllBytes($"Assets/Defaults/{fileName}");
        }
        catch
        {
            return null;
        }
    }
}
