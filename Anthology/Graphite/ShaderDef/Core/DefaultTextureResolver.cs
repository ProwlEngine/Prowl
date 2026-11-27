namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Maps a default texture name from a Properties block to a view, or null to skip that property.
/// </summary>
public delegate TextureView? DefaultTextureResolver(string textureName, ShaderPropertyType type);
