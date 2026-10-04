namespace Prowl.Graphite;

internal interface IGraphStateSource
{
    int StateVersion { get; }

    TextureState StateOf(Texture texture);
}
