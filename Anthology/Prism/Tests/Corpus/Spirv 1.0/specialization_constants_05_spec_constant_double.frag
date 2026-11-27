#version 450
layout(constant_id = 0) const double scale = 2.5;
layout(location = 0) out vec4 color;
void main()
{
    color = vec4(float(scale));
}
