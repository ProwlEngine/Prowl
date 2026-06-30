using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

namespace Prowl.Slang.NativeAPI;


public static unsafe class Program
{
    class Blob() : ManagedComProxy<ISlangBlob>, ISlangBlob
    {
        public byte* Bytes;
        public nuint Length;
        public bool NeedsFree = false;

        public void* GetBufferPointer()
        {
            return Bytes;
        }

        public nuint GetBufferSize()
        {
            return Length;
        }

        ~Blob()
        {
            if (NeedsFree)
                NativeMemory.Free(Bytes);
        }
    }


    class Filesystem : ManagedComProxy<ISlangFileSystem>, ISlangFileSystem
    {
        public void* CastAs(ref Guid guid)
        {
            return null;
        }


        public SlangResult LoadFile(ConstU8Str path, out ISlangBlob* outBlob)
        {
            if (!File.Exists(path.String))
            {
                // Console.WriteLine($"File {path.String} does not exist");

                outBlob = null;
                return SlangResult.Fail;
            }

            try
            {
                // Console.WriteLine($"Loading file: {path.String}");

                using (FileStream fs = new(path.String, FileMode.Open, FileAccess.Read))
                {
                    Blob blob = new Blob()
                    {
                        Bytes = (byte*)NativeMemory.Alloc((nuint)fs.Length),
                        Length = (nuint)fs.Length,
                        NeedsFree = true,
                    };

                    int bytesRead = 0;
                    while (bytesRead < fs.Length)
                    {
                        int read = fs.Read(new Span<byte>(blob.Bytes + bytesRead, (int)(fs.Length - bytesRead)));

                        if (read == 0)
                            break;

                        bytesRead += read;
                    }

                    outBlob = blob;

                    return SlangResult.Ok;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load file: {ex}");
                outBlob = null;

                return SlangResult.Fail;
            }
        }
    }


    public static void Main()
    {
        Process currentProcess = Process.GetCurrentProcess();
        Stopwatch stopwatch = Stopwatch.StartNew();

        using (var globalSession = SlangGlobalSession.Create())
        {
            long c = 0;
            while (true)
            {
                c++;
                CompileCode(globalSession);

                if (stopwatch.ElapsedMilliseconds / 1000 > .5)
                {
                    stopwatch.Restart();

                    // WorkingSet64 includes both managed and native allocations

                    currentProcess.Refresh();
                    long memoryUsed = currentProcess.PrivateMemorySize64;

                    Console.WriteLine($"Memory used: {memoryUsed / (1024.0 * 1024.0):F2} MB. Iterations: {c}");
                }
            }
        }
    }


    private static void CompileCode(SlangGlobalSession globalSession)
    {
        SlangTargetDesc targetDesc = new();
        targetDesc.Format = SlangCompileTarget.SPIRV;
        targetDesc.Profile = globalSession.FindProfile("glsl_450");

        SlangSessionDesc sessionDesc = new();
        sessionDesc.Targets = [ targetDesc ];
        sessionDesc.SearchPaths = ["./Shaders/"];
        sessionDesc.FileSystem = new Filesystem();

        SlangSession session = globalSession.CreateSession(sessionDesc);

        SlangModule module = session.LoadModule("MyShaders", out string? diagnostics);

        if (diagnostics != null)
        {
            Console.WriteLine(diagnostics);
            return;
        }

        SlangEntryPoint? entryPoint = module.FindEntryPointByName("computeMain");
        if (entryPoint == null)
            throw new Exception("Entry point not found");

        List<SlangComponentType> componentTypes = [
            module,
            entryPoint
            ];

        var program = session.CreateCompositeComponentType(componentTypes, out diagnostics);

        if (diagnostics != null)
        {
            Console.WriteLine(diagnostics);
            return;
        }

        var outCode = program.GetEntryPointCode(0, 0, out diagnostics);
        outCode.Dispose();

        if (diagnostics != null)
        {
            Console.WriteLine(diagnostics);
            return;
        }

        SlangReflectionLayout layout = program.GetLayout(0, out diagnostics);

        if (diagnostics != null)
        {
            Console.WriteLine(diagnostics);
            return;
        }

        var outReflection = layout.ToJson(out diagnostics);

        if (diagnostics != null)
        {
            Console.WriteLine(diagnostics);
            return;
        }

        program.Dispose();
        entryPoint.Dispose();
        session.Dispose();
    }
}
