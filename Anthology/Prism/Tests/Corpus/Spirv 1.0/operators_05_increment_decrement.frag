#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    int start_value;
};

void main()
{
    // increment and decrement operators as statements and inside expressions
    int counter = start_value;
    counter++;
    ++counter;
    counter--;
    --counter;

    int base = start_value;
    int post_result = base++ + 10;
    int pre_result = ++base + 20;
    int post_dec_result = base-- - 5;
    int pre_dec_result = --base - 5;

    int loop_sum = 0;
    for (int idx = 0; idx < 5; idx++)
    {
        loop_sum += idx;
    }

    float total = float(counter + post_result + pre_result + post_dec_result + pre_dec_result + loop_sum);

    FragColor = vec4(total, 0.0, 0.0, 1.0);
}
