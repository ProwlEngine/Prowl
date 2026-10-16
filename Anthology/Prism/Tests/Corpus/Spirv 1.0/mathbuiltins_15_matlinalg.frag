#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    mat2 m2;
    mat3 m3;
    mat4 m4;
};

void main() {
    mat2 t2 = transpose(m2);
    mat3 t3 = transpose(m3);
    mat4 t4 = transpose(m4);
    float dets = determinant(m2) + determinant(m3) + determinant(m4);
    mat2 i2 = inverse(m2);
    mat3 i3 = inverse(m3);
    mat4 i4 = inverse(m4);
    vec4 acc = i4[0] + i4[1] + i4[2] + i4[3];
    acc.xyz += i3[0] + i3[1] + i3[2] + t3[0] + t3[1] + t3[2];
    acc.xy += i2[0] + i2[1] + t2[0] + t2[1] + t4[0].xy;
    fragColor = acc + dets;
}
