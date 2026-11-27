#version 450
#extension GL_ARB_gpu_shader_int64 : require
layout(constant_id = 0) const int64_t count = 10L;
layout(location = 0) out vec4 color;
void main()
{
    color = vec4(float(count));
}
