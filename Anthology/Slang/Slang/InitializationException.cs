using System;


namespace Prowl.Slang;


public class InitializationException : Exception
{
    public InitializationException() { }
    public InitializationException(string message) : base(message) { }
    public InitializationException(string message, Exception inner) : base(message, inner) { }
}
