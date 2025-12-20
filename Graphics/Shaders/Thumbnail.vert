#version 450
layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 2) in vec4 v_Tangent;
layout(location = 3) in vec2 v_TexCoord;

layout(set = 0, binding = 0) uniform Params {
    mat4 ViewProjection;
    vec3 Color;
    float Roughness;
    vec3 CameraPos;
    float Metallic;
};

layout(location = 0) out vec3 f_Normal;
layout(location = 1) out vec3 f_WorldPos;
layout(location = 2) out vec2 f_TexCoord;
layout(location = 3) out vec4 f_Tangent;

void main() {
    gl_Position = ViewProjection * vec4(v_Position, 1.0);
    f_Normal = v_Normal;
    f_WorldPos = v_Position;
    f_TexCoord = v_TexCoord;
    f_Tangent = v_Tangent;
}