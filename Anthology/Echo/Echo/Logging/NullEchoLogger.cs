namespace Echo.Logging
{
    public class NullEchoLogger : IEchoLogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
