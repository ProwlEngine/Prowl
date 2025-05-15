using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;

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

        SlangNative.slang_createGlobalSession(0, out IGlobalSession* globalSessionPtr).Throw();

        if ((IntPtr)globalSessionPtr == IntPtr.Zero || globalSessionPtr == null)
        {
            // This is a failure to create the global session, which is a fatal error.
            // The Slang library will not work without this.
            Console.WriteLine("Failed to create global session");
            Console.ReadLine();
            return;
        }

        IGlobalSession globalSession = NativeComProxy.Create(globalSessionPtr);

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

                Console.WriteLine($"Memory used: {memoryUsed / (1024.0):F2} KB. Iterations: {c}");
            }
        }

        SlangNative.slang_shutdown();
    }


    static U8Str glsl_450 = new U8Str("glsl_450"u8);
    static U8Str _shaders = new U8Str("./Shaders/"u8);
    static U8Str _myShaders = new U8Str("MyShaders"u8);
    static U8Str _computeMain = new U8Str("computeMain"u8);


    private static void CompileCode(IGlobalSession globalSession)
    {
        Filesystem filesystem = new();

        SessionDesc sessionDesc = new();
        TargetDesc targetDesc = new();

        targetDesc.format = SlangCompileTarget.GLSL;
        targetDesc.profile = globalSession.FindProfile(glsl_450);

        sessionDesc.targets = &targetDesc;
        sessionDesc.targetCount = 1;

        ConstU8Str* paths = stackalloc ConstU8Str[1];

        // CWD must have a Shaders/ folder in it.
        paths[0] = _shaders;

        sessionDesc.searchPaths = paths;
        sessionDesc.searchPathCount = 1;

        sessionDesc.fileSystem = filesystem;

        globalSession.CreateSession(&sessionDesc, out ISession* sessionPtr).Throw();
        ISession session = NativeComProxy.Create(sessionPtr);

        IModule* modulePtr = session.LoadModule(_myShaders, out ISlangBlob* diagnostics);

        if ((IntPtr)modulePtr == IntPtr.Zero || modulePtr == null)
        {
            Console.WriteLine("Failed to load module");
            Console.ReadLine();
            return;
        }

        IModule module = NativeComProxy.Create(modulePtr, false);

        PrintBlob(diagnostics);

        module.FindEntryPointByName(_computeMain, out IEntryPoint* entryPointPtr).Throw();
        IEntryPoint entryPoint = NativeComProxy.Create(entryPointPtr);

        IComponentType** componentTypes = stackalloc IComponentType*[2];
        componentTypes[0] = (IComponentType*)modulePtr;
        componentTypes[1] = (IComponentType*)entryPointPtr;

        session.CreateCompositeComponentType(componentTypes, 2, out IComponentType* programPtr, out _).Throw();
        IComponentType program = NativeComProxy.Create(programPtr);

        program.GetEntryPointCode(0, 0, out ISlangBlob* outCodePtr, out diagnostics).Throw();

        PrintBlob(diagnostics);

        ShaderReflection* layout = program.GetLayout(0, out diagnostics);

        PrintBlob(diagnostics);

        layout->toJson(out ISlangBlob* reflectionBlob).Throw();

        ISlangBlob outCode = NativeComProxy.Create(outCodePtr);
        ISlangBlob outReflection = NativeComProxy.Create(reflectionBlob);

        string code = System.Text.Encoding.UTF8.GetString((byte*)outCode.GetBufferPointer(), (int)outCode.GetBufferSize());
        // Console.WriteLine("Got " + code.Length + " chars of code");

        string json = System.Text.Encoding.UTF8.GetString((byte*)outReflection.GetBufferPointer(), (int)outReflection.GetBufferSize());
        // Console.WriteLine("Got " + json.Length + " chars of json");
    }


    private static void PrintBlob(ISlangBlob* blobPtr)
    {
        if (blobPtr == null)
            return;

        ISlangBlob blob = NativeComProxy.Create(blobPtr);
        Console.WriteLine(Marshal.PtrToStringUTF8((nint)blob.GetBufferPointer()));
    }
}
