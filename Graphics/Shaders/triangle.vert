#version 450

layout(location = 0) in vec2 v_Position;
layout(location = 1) in vec3 v_Color;

layout(location = 0) out vec3 f_Color;

void main()
{
    gl_Position = vec4(v_Position, 0.0f, 1.0f);
    f_Color = v_Color;
}