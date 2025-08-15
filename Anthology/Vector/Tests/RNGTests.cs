namespace Prowl.Vector.Methods;

public class RNGTests
{
    [Fact]
    public void NextGaussian_ReusesCachedValue()
    {
        var rng = new RNG(123);

        float u1 = rng.NextFloat();
        float u2 = rng.NextFloat();
        float expectedMag = MathF.Sqrt(-2f * MathF.Log(u1));
        float expected0 = expectedMag * MathF.Cos(2f * MathF.PI * u2);
        float expected1 = expectedMag * MathF.Sin(2f * MathF.PI * u2);
        float expectedNextFloat = rng.NextFloat();

        rng.SetSeed(123);

        float g0 = rng.NextGaussian();
        float g1 = rng.NextGaussian();
        float nextFloat = rng.NextFloat();

        Assert.Equal(expected0, g0, 6);
        Assert.Equal(expected1, g1, 6);
        Assert.Equal(expectedNextFloat, nextFloat, 6);
    }
}
