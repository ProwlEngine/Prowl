using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;

namespace Prowl.Slang.NativeAPI;


public static unsafe class Program
{
    class Blob() : ManagedComProxy<ISlangBlob>, ISlangBlob
    {
        public byte* Bytes;
        public nuint Length;

        public void* GetBufferPointer()
        {
            return Bytes;
        }

        public nuint GetBufferSize()
        {
            return Length;
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
                Console.WriteLine($"File {path.String} does not exist");

                outBlob = null;
                return SlangResult.Fail;
            }

            try
            {
                Console.WriteLine($"Loading file: {path.String}");

                byte[] fileData = File.ReadAllBytes(path.String);
                nuint length = (nuint)fileData.Length;

                byte* unmanagedPtr = (byte*)Marshal.AllocHGlobal(fileData.Length);
                Marshal.Copy(fileData, 0, (IntPtr)unmanagedPtr, fileData.Length);

                Blob blob = new() { Bytes = unmanagedPtr, Length = length };
                outBlob = blob;

                return SlangResult.Ok;
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
        CompileCode();
    }


    private static void CompileCode()
    {
        SlangNative.slang_createGlobalSession(0, out IGlobalSession* globalSessionPtr).Throw();
        IGlobalSession globalSession = ProxyEmitter.CreateNativeProxy(globalSessionPtr);

        Filesystem filesystem = new();

        SessionDesc sessionDesc = new();
        TargetDesc targetDesc = new();

        targetDesc.format = SlangCompileTarget.GLSL;
        targetDesc.profile = globalSession.FindProfile(new U8Str("glsl_450"u8));

        sessionDesc.targets = &targetDesc;
        sessionDesc.targetCount = 1;

        ConstU8Str* paths = stackalloc ConstU8Str[1];

        // CWD must have a Shaders/ folder in it.
        paths[0] = new U8Str("./Shaders/"u8);

        sessionDesc.searchPaths = paths;
        sessionDesc.searchPathCount = 1;

        sessionDesc.fileSystem = filesystem;

        globalSession.CreateSession(&sessionDesc, out ISession* sessionPtr).Throw();
        ISession session = ProxyEmitter.CreateNativeProxy(sessionPtr);

        IModule* modulePtr = session.LoadModule(new U8Str("MyShaders"u8), out ISlangBlob* diagnosticsPtr);

        if (diagnosticsPtr != null)
        {
            PrintBlob(diagnosticsPtr);
            return;
        }

        IModule module = ProxyEmitter.CreateNativeProxy(modulePtr);

        module.FindEntryPointByName(new U8Str("computeMain"u8), out IEntryPoint* entryPointPtr).Throw();
        IEntryPoint entryPoint = ProxyEmitter.CreateNativeProxy(entryPointPtr);

        IComponentType** componentTypes = stackalloc IComponentType*[2];
        componentTypes[0] = (IComponentType*)modulePtr;
        componentTypes[1] = (IComponentType*)entryPointPtr;

        session.CreateCompositeComponentType(componentTypes, 2, out IComponentType* programPtr, out _).Throw();
        IComponentType program = ProxyEmitter.CreateNativeProxy(programPtr);

        SlangResult result = program.GetEntryPointCode(0, 0, out ISlangBlob* outCodePtr, out ISlangBlob* resultDiagnosticsPtr);

        if (result != SlangResult.Ok)
        {
            PrintBlob(resultDiagnosticsPtr);
            return;
        }

        ISlangBlob outCode = ProxyEmitter.CreateNativeProxy(outCodePtr);

        ShaderReflection* layout = program.GetLayout(0, out _);

        layout->toJson(out ISlangBlob* blob);
        ISlangBlob outReflection = ProxyEmitter.CreateNativeProxy(blob);

        string code = System.Text.Encoding.UTF8.GetString((byte*)outCode.GetBufferPointer(), (int)outCode.GetBufferSize());
        Console.WriteLine(code);

        string json = System.Text.Encoding.UTF8.GetString((byte*)outReflection.GetBufferPointer(), (int)outReflection.GetBufferSize());
        Console.WriteLine("Got " + json.Length + " chars of json");
    }


    private static void PrintBlob(ISlangBlob* blobPtr)
    {
        ISlangBlob blob = ProxyEmitter.CreateNativeProxy(blobPtr);
        Console.WriteLine(Marshal.PtrToStringUTF8((nint)blob.GetBufferPointer()));
    }
}
