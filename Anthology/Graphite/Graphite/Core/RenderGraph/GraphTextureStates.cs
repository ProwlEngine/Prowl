using System.Collections.Generic;

namespace Prowl.Graphite;

internal sealed class GraphTextureStates
{
    private readonly Dictionary<Texture, TextureState> _states;

    internal GraphTextureStates(Dictionary<Texture, TextureState> states)
    {
        _states = new Dictionary<Texture, TextureState>(states);
    }

    internal TextureState StateOf(Texture texture)
        => _states.TryGetValue(texture, out TextureState state) ? state : TextureState.Resting;
}
