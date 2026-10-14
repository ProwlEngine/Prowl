#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float u_a;
    float u_b;
};

void main() {
    float base = abs(u_a) + 0.1;
    float exponent = u_b;

    float r_pow = pow(base, exponent);
    float r_exp = exp(u_a);
    float r_log = log(base);
    float r_exp2 = exp2(u_a);
    float r_log2 = log2(base);
    float r_sqrt = sqrt(base);
    float r_inversesqrt = inversesqrt(base);

    float total = r_pow + r_exp + r_log + r_exp2 + r_log2 + r_sqrt + r_inversesqrt;

    FragColor = vec4(total, r_pow, r_sqrt, 1.0);
}
