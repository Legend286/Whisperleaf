#version 460

layout(location = 0) in vec3 v_Position;

layout(set = 0, binding = 0) uniform OrthoLight {
    mat4 u_LightViewProj;
    mat4 u_ShadowViewProj;
};

layout(set = 1, binding = 0) readonly buffer ModelBuffer {
    mat4 u_Models[];
};

void main()
{
    mat4 model = u_Models[gl_InstanceIndex];
    gl_Position = u_ShadowViewProj * model * vec4(v_Position, 1.0);
}
