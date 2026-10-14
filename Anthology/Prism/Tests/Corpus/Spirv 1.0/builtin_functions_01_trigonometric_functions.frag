#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float u_x;
};

void main() {
    float x = u_x;
    float x_unit = clamp(x, -0.99, 0.99);
    float x_ge1 = abs(x) + 1.0;

    // sin, cos, tan
    float r_sin = sin(x);
    float r_cos = cos(x);
    float r_tan = tan(x);

    // asin, acos
    float r_asin = asin(x_unit);
    float r_acos = acos(x_unit);

    // atan 1-arg and 2-arg
    float r_atan1 = atan(x);
    float r_atan2 = atan(x, 1.0);

    // sinh, cosh, tanh
    float r_sinh = sinh(x);
    float r_cosh = cosh(x);
    float r_tanh = tanh(x);

    // asinh, acosh, atanh
    float r_asinh = asinh(x);
    float r_acosh = acosh(x_ge1);
    float r_atanh = atanh(x_unit);

    // radians, degrees
    float r_radians = radians(x);
    float r_degrees = degrees(x);

    float total = r_sin + r_cos + r_tan + r_asin + r_acos + r_atan1 + r_atan2 +
                  r_sinh + r_cosh + r_tanh + r_asinh + r_acosh + r_atanh +
                  r_radians + r_degrees;

    FragColor = vec4(total, r_sin, r_cos, 1.0);
}
