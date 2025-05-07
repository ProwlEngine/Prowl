using System;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;

public static class Program
{
    public static unsafe void Main(string[] args)
    {
        unsafe
        {
            IGlobalSession* globalSession;
            SlangNative.slang_createGlobalSession(0, &globalSession).Throw();

            nint queryInterfacePtr = globalSession->lpVtbl->QueryInterface;
            QueryInterfaceDelegate queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryInterfacePtr);

            SlangUUID uuid = new(0xc140b5fd, 0xc78, 0x452e, 0xba, 0x7c, 0x1a, 0x1e, 0x70, 0xc7, 0xf7, 0x1c);

            queryInterface(globalSession, ref uuid, out IntPtr globalSesh).Throw();

            Console.WriteLine($"Global session ptr: {(IntPtr)globalSession}. Query result: {globalSesh}");

            // Get delegate from vtable
            nint createSessionPtr = globalSession->lpVtbl->CreateSession;
            CreateSessionDelegate createSession = Marshal.GetDelegateForFunctionPointer<CreateSessionDelegate>(createSessionPtr);

            SessionDesc desc = new();

            createSession(globalSession, &desc, out IntPtr outSession).Throw();

            Console.WriteLine($"Session created successfully. Session ptr: {outSession}");
        }
    }
}
