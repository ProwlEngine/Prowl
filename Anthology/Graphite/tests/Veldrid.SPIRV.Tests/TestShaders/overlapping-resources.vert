#version 450

layout(set = 0, binding = 0) uniform Buffer0
{
    vec4 Data0;
};

layout(set = 0, binding = 0) uniform Buffer1
{
    vec4 Data1;
};

void main()
{
    gl_Position = vec4(Data0.x, Data0.y, Data1.x, Data1.y);
}
