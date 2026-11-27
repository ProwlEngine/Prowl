#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // vector constant folding for vec3 and ivec3
    const vec3 va = vec3(1.0, 2.0, 3.0);
    const vec3 vb = vec3(4.0, 5.0, 6.0);
    const vec3 vres = (va + vb) * 2.0 - vb / 2.0;

    const ivec3 ia = ivec3(2, 4, 6);
    const ivec3 ib = ivec3(1, 2, 3);
    const ivec3 ires = (ia - ib) * ib + ia / ib;

    const float total = vres.x + vres.y + vres.z + float(ires.x + ires.y + ires.z);

    FragColor = vec4(total, vres.z, 0.0, 1.0);
}
