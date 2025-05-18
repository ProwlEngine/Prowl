using System;


namespace Prowl.Slang;


public class InvalidComponentException : Exception
{
    public InvalidComponentException() { }
    public InvalidComponentException(string message) : base(message) { }
    public InvalidComponentException(string message, Exception inner) : base(message, inner) { }
}
