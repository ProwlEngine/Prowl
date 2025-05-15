using System;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class Session
{
    private ISession _session;


    internal Session(ISession session)
    {
        _session = session;
    }
}