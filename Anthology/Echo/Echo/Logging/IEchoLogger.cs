namespace Echo.Logging
{
    public interface IEchoLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message, Exception? exception = null);
    }
}
