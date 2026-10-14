#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float u_x;
    float u_y;
    float u_a;
    float u_zero;
};

void main() {
    float x = u_x;
    float y = u_y;

    float r_abs = abs(x);
    float r_sign = sign(x);
    float r_floor = floor(x);
    float r_ceil = ceil(x);
    float r_fract = fract(x);
    float r_mod = mod(x, y);
    float r_min = min(x, y);
    float r_max = max(x, y);
    float r_clamp = clamp(x, -1.0, 1.0);

    // mix with float interpolant
    float r_mix = mix(x, y, u_a);

    // mix with bvec selector overload
    vec3 mix_a = vec3(x, y, u_a);
    vec3 mix_b = vec3(y, x, 1.0);
    bvec3 mix_sel = bvec3(true, false, true);
    vec3 r_mix_bvec = mix(mix_a, mix_b, mix_sel);

    float r_step = step(0.0, x);
    float r_smoothstep = smoothstep(-1.0, 1.0, x);

    float r_round = round(x);
    float r_roundeven = roundEven(x);
    float r_trunc = trunc(x);

    float nan_val = u_zero / u_zero;
    float inf_val = 1.0 / u_zero;
    float r_isnan = isnan(nan_val) ? 1.0 : 0.0;
    float r_isinf = isinf(inf_val) ? 1.0 : 0.0;

    float total = r_abs + r_sign + r_floor + r_ceil + r_fract + r_mod + r_min +
                  r_max + r_clamp + r_mix + r_mix_bvec.x + r_mix_bvec.y + r_mix_bvec.z +
                  r_step + r_smoothstep + r_round + r_roundeven + r_trunc +
                  r_isnan + r_isinf;

    FragColor = vec4(total, r_mix, r_mix_bvec.x, 1.0);
}
