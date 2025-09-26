#version 450

layout(set = 0, binding = 0) uniform CameraBuffer
{
    mat4 ViewProjection;
};

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Color;

layout(location = 0) out vec3 f_Color;

void main()
{
    gl_Position = ViewProjection * vec4(v_Position, 1.0f);
    f_Color = v_Color;
}