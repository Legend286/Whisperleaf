#version 450
layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec4 v_Color;

layout(set = 0, binding = 0) uniform CameraBuffer {
    mat4 u_View;
    mat4 u_Proj;
    mat4 u_ViewProj;
    vec3 u_CameraPos;
};

layout(location = 0) out vec4 f_Color;

void main() {
    gl_Position = u_Proj * u_View * vec4(v_Position, 1.0);
    f_Color = v_Color;
}
