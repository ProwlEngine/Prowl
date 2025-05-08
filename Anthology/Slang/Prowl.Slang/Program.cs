using System;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


public static class Program
{



    public static unsafe void Main(string[] args)
    {
        SlangNative.slang_createGlobalSession(0, out IGlobalSession* globalSessionPtr).Throw();
        IGlobalSession globalSession = ProxyEmitter.CreateVtableProxy(globalSessionPtr);

        SessionDesc sessionDesc = new();
        TargetDesc targetDesc = new();

        targetDesc.format = SlangCompileTarget.METAL;
        targetDesc.profile = globalSession.FindProfile(new U8Str("glsl_450"u8));

        sessionDesc.targets = &targetDesc;
        sessionDesc.targetCount = 1;

        byte** paths = stackalloc byte*[1];

        // CWD must have a Shaders/ folder in it.
        paths[0] = new U8Str("./Shaders/"u8);

        sessionDesc.searchPaths = paths;
        sessionDesc.searchPathCount = 1;

        globalSession.CreateSession(&sessionDesc, out ISession* sessionPtr).Throw();
        ISession session = ProxyEmitter.CreateVtableProxy(sessionPtr);

        IModule* modulePtr = session.LoadModule(new U8Str("MyShaders"u8), out ISlangBlob* diagnosticsPtr);

        if (diagnosticsPtr != null)
        {
            ISlangBlob diagnostics = ProxyEmitter.CreateVtableProxy(diagnosticsPtr);
            Console.WriteLine(Marshal.PtrToStringUTF8((nint)diagnostics.GetBufferPointer()));

            return;
        }

        IModule module = ProxyEmitter.CreateVtableProxy(modulePtr);

        module.FindEntryPointByName(new U8Str("computeMain"u8), out IEntryPoint* entryPointPtr).Throw();
        IEntryPoint entryPoint = ProxyEmitter.CreateVtableProxy(entryPointPtr);


        IComponentType** componentTypes = stackalloc IComponentType*[2];
        componentTypes[0] = (IComponentType*)modulePtr;
        componentTypes[1] = (IComponentType*)entryPointPtr;

        session.CreateCompositeComponentType(componentTypes, 2, out IComponentType* programPtr, out _).Throw();
        IComponentType program = ProxyEmitter.CreateVtableProxy(programPtr);

        program.getEntryPointCode(0, 0, out ISlangBlob* outCodePtr, out _).Throw();
        ISlangBlob outCode = ProxyEmitter.CreateVtableProxy(outCodePtr);

        string code = System.Text.Encoding.ASCII.GetString((byte*)outCode.GetBufferPointer(), (int)outCode.GetBufferSize());

        Console.WriteLine(code);
    }
}
