#version 450

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 2) in vec2 v_TexCoord;
layout(location = 3) in vec4 v_Tangent;

layout(set = 0, binding = 0) uniform CameraBuffer
{
    mat4 ViewProjection;
};

layout(location = 0) out vec3 f_Normal;

void main()
{
    gl_Position = ViewProjection * vec4(v_Position, 1.0);
    f_Normal = normalize(v_Normal);
}
