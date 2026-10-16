// Spreads every resource kind across three sets. Output is valueA, valueB, Input[0], valueC, sampled red * 255.
Compute
{
    layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

    layout(set = 0, binding = 0, std140) uniform BlockA { uint valueA; };
    layout(set = 0, binding = 1, std430) buffer Output { uint values[]; } outputs;

    layout(set = 1, binding = 0, std140) uniform BlockB { uint valueB; };
    layout(set = 1, binding = 1, std430) readonly buffer Input { uint values[]; } inputs;

    layout(set = 2, binding = 0, std140) uniform BlockC { uint valueC; };
    layout(set = 2, binding = 1) uniform texture2D Tex;
    layout(set = 2, binding = 2) uniform sampler Samp;

    void main()
    {
        outputs.values[0] = valueA;
        outputs.values[1] = valueB;
        outputs.values[2] = inputs.values[0];
        outputs.values[3] = valueC;

        vec4 texel = textureLod(sampler2D(Tex, Samp), vec2(0.5, 0.5), 0.0);
        outputs.values[4] = uint(texel.r * 255.0 + 0.5);
    }
}
