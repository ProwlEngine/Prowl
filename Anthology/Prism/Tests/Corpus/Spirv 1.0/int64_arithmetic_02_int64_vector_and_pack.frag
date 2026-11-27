#version 460 core
#extension GL_ARB_gpu_shader_int64 : require

layout(location = 0) out vec4 FragColor;

void main() {
    i64vec2 a = i64vec2(1000000000000l, -2000000000000l);
    i64vec2 b = i64vec2(3l, 4l);
    i64vec2 sum = a + b;
    i64vec2 prod = a * b;

    u64vec3 ua = u64vec3(1000000000000ul, 2000000000000ul, 3000000000000ul);
    u64vec3 ub = u64vec3(1ul, 2ul, 3ul);
    u64vec3 usum = ua + ub;
    u64vec3 uprod = ua * ub;

    int64_t packed = packInt2x32(ivec2(1, 2));
    ivec2 unpacked = unpackInt2x32(packed);

    uint64_t upacked = packUint2x32(uvec2(10u, 20u));
    uvec2 uunpacked = unpackUint2x32(upacked);

    float r = float(sum.x % int64_t(255)) / 255.0;
    float g = float(usum.y % uint64_t(255)) / 255.0;
    float b2 = float(unpacked.x + unpacked.y) / 255.0;
    float a2 = float(uunpacked.x + uunpacked.y) / 255.0
        + float(prod.x % int64_t(100)) / 10000.0
        + float(uprod.z % uint64_t(100)) / 10000.0;

    FragColor = vec4(r, g, b2, a2);
}
