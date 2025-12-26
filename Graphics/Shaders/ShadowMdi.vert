#version 460

layout(location = 0) in vec3 v_Position;

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

layout(set = 2, binding = 0) readonly buffer VisibleInstances {
    uint u_VisibleIndices[];
};

void main()
{
    // gl_InstanceIndex includes the 'firstInstance' offset from the indirect command
    uint modelIndex = u_VisibleIndices[gl_InstanceIndex];
    mat4 model = u_Models[modelIndex];
    gl_Position = u_ViewProj * model * vec4(v_Position, 1.0);
}
