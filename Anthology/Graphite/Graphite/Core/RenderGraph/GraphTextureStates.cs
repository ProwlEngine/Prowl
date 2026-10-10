using System.Collections.Generic;

namespace Prowl.Graphite;

internal sealed class GraphTextureStates
{
    private static readonly Dictionary<Texture, TextureState> s_none = new();

    private readonly Dictionary<Texture, TextureState> _states;
    private readonly Dictionary<Texture, TextureState> _allowed;

    internal GraphTextureStates(Dictionary<Texture, TextureState> states, Dictionary<Texture, TextureState>? allowed = null)
    {
        _states = new Dictionary<Texture, TextureState>(states);
        _allowed = allowed ?? s_none;
    }

    internal TextureState? StateOf(Texture texture)
        => _states.TryGetValue(texture, out TextureState state) ? state : null;

    internal bool Allows(Texture texture, TextureState state)
        => _allowed.TryGetValue(texture, out TextureState allowed) && (allowed & state) != 0;

    internal GraphTextureStates With(Texture texture, TextureState state)
    {
        GraphTextureStates next = new(_states, _allowed);
        next._states[texture] = state;
        return next;
    }

    internal void CopyTo(Dictionary<Texture, TextureState> states)
    {
        foreach ((Texture texture, TextureState state) in _states)
            states[texture] = state;
    }
}
