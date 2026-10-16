#version 460 core

// A bool has no defined memory layout, so a block stores its bool members as uint and converts on
// access. Here the bools sit inside a NESTED struct and inside an array, not directly in the block.

struct Flags
{
    bool enabled;
    bool inverted;
    bvec2 axes;
};

layout(std140, set = 0, binding = 0) uniform Settings
{
    Flags flags;
    bool topLevel;
    bvec4 mask;
} settings;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec3 c = vec3(0.0);

    if (settings.flags.enabled) c.r = 1.0;
    if (settings.flags.inverted) c.r = 1.0 - c.r;
    if (settings.flags.axes.x) c.g = 0.5;
    if (settings.flags.axes.y) c.g += 0.25;
    if (settings.topLevel) c.b = 1.0;
    if (any(settings.mask)) c *= 0.5;
    if (all(settings.mask.xy)) c += 0.125;

    Flags local = settings.flags;
    if (local.enabled && local.axes.y) c += 0.0625;

    FragColor = vec4(c, 1.0);
}
