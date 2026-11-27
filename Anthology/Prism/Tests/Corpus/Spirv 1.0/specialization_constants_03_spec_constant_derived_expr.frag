#version 460 core

// A plain const expression built from a specialization constant must fold into
// an OpSpecConstantOp rather than a plain literal, keeping its specialization-ness.

layout(constant_id = 0) const int BASE = 4;
const int DERIVED = BASE * 2 + 1;

layout(location = 0) in vec3 inColor;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec3 result = vec3(0.0);
    for (int i = 0; i < DERIVED; i++)
    {
        result += inColor * (1.0 / float(DERIVED));
    }

    FragColor = vec4(result, 1.0);
}
