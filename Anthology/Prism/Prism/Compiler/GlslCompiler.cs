using System.Collections.Concurrent;
using Prowl.Prism.Glsl;
using Prowl.Prism.SpirV;

namespace Prowl.Prism.Compiler;

/// <summary>The pipeline stage a shader is compiled for (its SPIR-V entry-point model).</summary>
public enum ShaderStage { Vertex, Fragment, Compute, Geometry, TessControl, TessEvaluation, Mesh, Task,
    RayGen, Intersection, AnyHit, ClosestHit, Miss, Callable }

/// <summary>
/// The public entry point: compiles GLSL source text into a SPIR-V module.
/// Pipeline is parse (with preprocessor) -> lower -> serialize. The optional defines dictionary
/// selects shader variants; the target selects the SPIR-V version and defaults to 1.0. The optional
/// includes callback resolves <c>#include</c> directives; without it, an include is a compile error.
/// </summary>
public static class GlslCompiler
{
    // Reused across compiles on the compile lane (thread-static), so the front-end reaches ~zero steady-
    // state allocation. Concurrent compiles on different threads each get their own lane and pools.
    [ThreadStatic] static Lowerer? _lowerer;
    [ThreadStatic] static CompileLane? _lane;

    public static Prowl.Prism.SpirVModule Compile(string source, ShaderStage stage,
        SpirVVersion target = SpirVVersion.V1_0, IReadOnlyDictionary<string, string>? defines = null, bool optimize = false,
        bool autoMapLocations = false, IncludeCallback? includes = null) =>
        (_lane ??= new CompileLane()).Run(() =>
        {
            var unit = Parser.Parse(source, defines, includes, stage);
            var module = (_lowerer ??= new Lowerer()).Reset(stage, target, autoMapLocations).Lower(unit);
            var words = module.Serialize();
            // glslang type-checks uncalled functions too: re-lower a fresh parse (the AST carries per-compile
            // memos) with every body reachable, only for its errors.
            if (_lowerer.SkippedUncalledFunctions)
                new Lowerer().Reset(stage, target, autoMapLocations).ValidateAllFunctions().Lower(Parser.Parse(source, defines, includes));
            if (optimize) _lowerer.ReleaseCompileState();   // the front-end's state is dead weight while optimizing
            if (optimize) words = Optimizer.SpirVOptimizer.Optimize(words);
            return new Prowl.Prism.SpirVModule(words);
        });

    public static byte[] CompileToBytes(string source, ShaderStage stage,
        SpirVVersion target = SpirVVersion.V1_0, IReadOnlyDictionary<string, string>? defines = null, bool optimize = false,
        bool autoMapLocations = false, IncludeCallback? includes = null) =>
        Compile(source, stage, target, defines, optimize, autoMapLocations, includes).ToBytes();

    /// <summary>
    /// Runs only the preprocessor. Returns the preprocessed GLSL text and the state it found (version, extensions,
    /// pragmas, defines, and included files), so a host can lay out a program before compiling it.
    /// </summary>
    public static PreprocessResult Preprocess(string source, IReadOnlyDictionary<string, string>? defines = null, IncludeCallback? includes = null) =>
        (_lane ??= new CompileLane()).Run(() => Preprocessor.Preprocess(source, defines, includes));
}

/// <summary>
/// One compile lane per calling thread: the parser and lowerer recurse once per nesting level, and
/// glslang's acceptance zone runs to ~5,000-deep paren nesting - far beyond what a default 1 MB
/// thread stack survives - so the pipeline executes on a dedicated 64 MB stack. The lane is reused
/// across compiles from the same thread, which keeps the ThreadStatic pools above warm.
/// </summary>
sealed class CompileLane
{
    readonly Thread _thread;
    readonly BlockingCollection<(Func<object> Work, TaskCompletionSource<object?> Done)> _queue = new();

    public CompileLane()
    {
        // The lane's thread captures only the queue, never this lane: the lane is then held only by its owner
        // thread's ThreadStatic slot, so once the owner has exited and the lane is collected, the finalizer below
        // ends the lane's thread too.
        var queue = _queue;
        _thread = new Thread(() =>
        {
            foreach (var (work, done) in queue.GetConsumingEnumerable())
                try { done.SetResult(work()); }
                catch (Exception e) { done.SetException(e); }
        }, maxStackSize: 64 * 1024 * 1024) { IsBackground = true, Name = "Prism compile" };
        _thread.Start();
    }

    ~CompileLane() => _queue.CompleteAdding();

    public T Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add((() => work()!, done));
        return (T)done.Task.GetAwaiter().GetResult()!;
    }
}
