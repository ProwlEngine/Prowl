#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    int int_a;
    int int_b;
    float float_a;
    float float_b;
    uint uint_a;
    uint uint_b;
};

void main()
{
    // relational and equality operators across int, float, uint
    bool lt = int_a < int_b;
    bool le = int_a <= int_b;
    bool gt = float_a > float_b;
    bool ge = float_a >= float_b;
    bool eq = uint_a == uint_b;
    bool ne = uint_a != uint_b;

    float red = mix(0.0, 1.0, lt ? 1.0 : 0.0);
    float green = step(0.5, ge ? 1.0 : 0.0);
    float blue = mix(step(0.5, le ? 1.0 : 0.0), step(0.5, gt ? 1.0 : 0.0), 0.5);
    float alpha = (eq || ne) ? 1.0 : 0.0;

    FragColor = vec4(red, green, blue, alpha);
}
