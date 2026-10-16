// Copies the push constants into a buffer.
Compute
{
    layout(local_size_x = 1) in;

    layout(push_constant) uniform Push { uvec4 values; } push;

    layout(set = 0, binding = 0, std430) buffer Output { uvec4 result; } output_;

    void main()
    {
        output_.result = push.values;
    }
}
