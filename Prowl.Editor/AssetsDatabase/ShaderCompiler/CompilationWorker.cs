using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

using Prowl.Graphite.Compiler;
using Prowl.Slang;

using Prowl.Runtime;
using Prowl.Editor.Projects;


namespace Prowl.Editor;


public readonly record struct CompilationRequest(string ModuleName, string ModulePath, Memory<byte> SourceUtf8, ShaderType Type);


public static class CompilationWorker
{
    private static readonly Channel<CompileJob> _channel;
    private static readonly CancellationTokenSource _cts = new();
    private static readonly Task _worker;
    private static CompilationSession _session;
    private static CompilationRequest _currentProcessingRequest;


    static CompilationWorker()
    {
        _channel = Channel.CreateUnbounded<CompileJob>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        _session = new CompilationSession();
        _session.RegisterDiagnosticHandler(WriteCompilerException);

        _session.RegisterModule(new GLCompiler("glsl_450"));
        _session.RegisterModule(new VulkanCompiler("spirv_1_5"));
        Debug.LogWarning($"HLSL compilation currently disabled due to errors with HLSL slang backend and combined image samplers.");
        // _session.RegisterModule(new DXCompiler("sm_5_0"));
        // _session.RegisterModule(new MetalCompiler());
        // _session.RegisterModule(new WebGPUCompiler());

        _worker = Task.Factory.StartNew(
            RunCompilerLoop,
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }


    private static void WriteCompilerException(DiagnosticInfo info)
    {
        foreach (Diagnostic diag in info.GetDiagnostics())
        {
            LogSeverity logSeverity = diag.Severity switch
            {
                Severity.Note => LogSeverity.Normal,
                Severity.Warning => LogSeverity.Warning,
                Severity.Error => LogSeverity.Error,
                Severity.Fatal => LogSeverity.Exception,
                Severity.Internal => LogSeverity.Exception,
                _ => LogSeverity.Error
            };

            DebugStackFrame source = new(_currentProcessingRequest.ModulePath);
            DebugStackFrame errorFile = new(diag.FilePath, diag.LineNumber);

            DebugStackTrace trace = new(source.FileName == errorFile.FileName ? [errorFile] : [source, errorFile]);

            Debug.Log($"{diag.Severity} ({diag.ErrorCode}) compiling {_currentProcessingRequest.ModuleName}: {diag.Message}", logSeverity, trace);
        }
    }


    public static Task<CompilationResult> CompileAsync(
        CompilationRequest request,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<CompilationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var job = new CompileJob(
            request,
            tcs,
            cancellationToken);

        if (!_channel.Writer.TryWrite(job))
        {
            throw new InvalidOperationException("Compiler queue closed.");
        }

        return tcs.Task;
    }


    private static async Task RunCompilerLoop()
    {
        while (await _channel.Reader.WaitToReadAsync(_cts.Token))
        {
            while (_channel.Reader.TryRead(out CompileJob? job))
            {
                try
                {
                    var result = CompileShader(job.Request);

                    job.Completion.SetResult(result);
                }
                catch (Exception ex)
                {
                    job.Completion.SetException(ex);
                }
            }
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


    private static CompilationResult CompileShader(CompilationRequest request)
    {
        try
        {
            _session.BeginSession([new DirectoryInfo("/")], IncludeResolver);
            _currentProcessingRequest = request;

            CompilationResult result = _session.CompileShader(request.ModuleName, request.ModulePath, request.SourceUtf8, request.Type);

            return result;
        }
        catch (Exception ex)
        {
            _session.EndSession();
            Debug.LogException(ex);
        }

        return default;
    }


    public static void DisposeWorker()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            _worker.Wait();
        }
        catch
        {
        }

        _cts.Dispose();
    }


    private sealed record CompileJob(
        CompilationRequest Request,
        TaskCompletionSource<CompilationResult> Completion,
        CancellationToken CancellationToken);
}