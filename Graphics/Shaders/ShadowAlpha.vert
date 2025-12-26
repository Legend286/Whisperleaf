#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 3) in vec2 v_TexCoord;

layout(location = 0) out vec2 f_UV;

layout(set = 0, binding = 0) uniform CameraBuffer {
    mat4 u_View;
    mat4 u_Proj;
    mat4 u_ViewProj;
    vec3 u_CameraPos;
    float _padding;
};

layout(set = 1, binding = 0) readonly buffer ModelBuffer {
    mat4 u_Models[];
};

void main()
{
    mat4 model = u_Models[gl_InstanceIndex];
    f_UV = v_TexCoord * vec2(1, -1);
    gl_Position = u_ViewProj * (model * vec4(v_Position, 1.0));
}