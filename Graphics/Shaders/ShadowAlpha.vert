#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 3) in vec2 v_TexCoord;

layout(location = 0) out vec2 f_UV;

layout(set = 0, binding = 0) uniform ViewProjBuffer {
    mat4 u_ViewProj;
};

layout(set = 1, binding = 0) readonly buffer ModelBuffer {
    mat4 u_Models[];
};

void main()
{
    mat4 model = u_Models[gl_InstanceIndex];
    f_UV = v_TexCoord * vec2(1,-1); // V-flip matches PBR
    gl_Position = u_ViewProj * model * vec4(v_Position, 1.0);
}
