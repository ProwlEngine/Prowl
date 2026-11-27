namespace Prowl.Graphite.Vk;

internal sealed class VkGpuSubmission : GpuSubmission
{
    private readonly VkGraphicsDevice _gd;

    internal ulong Serial;

    internal VkGpuSubmission(VkGraphicsDevice gd)
    {
        _gd = gd;
    }

    public override bool IsComplete
    {
        get
        {
            bool done = _gd.GetCompletedSerial() >= Serial;
            if (done)
                _gd.PollSubmissions();
            return done;
        }
    }

    public override bool Wait(ulong nanosecondTimeout = ulong.MaxValue)
    {
        bool done = _gd.WaitForSerial(Serial, nanosecondTimeout);
        if (done)
            _gd.PollSubmissions();
        return done;
    }
}
