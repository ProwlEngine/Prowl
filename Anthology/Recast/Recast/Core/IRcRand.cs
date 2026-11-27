namespace Prowl.Recast.Core
{
    public interface IRcRand
    {
        float Next();
        double NextDouble();
        int NextInt32();
    }
}