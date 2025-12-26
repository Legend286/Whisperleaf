#version 450

// Vertex inputs
layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 2) in vec4 v_Tangent;
layout(location = 3) in vec2 v_TexCoord;

layout(set = 0, binding = 0) uniform CameraBuffer {
    mat4 u_View;
    mat4 u_Proj;
    mat4 u_ViewProj;
    vec3 u_CameraPos;
    float u_Padding0;
    vec2 u_ScreenSize;
    int u_DebugMode;
    int u_Padding1;
};

layout(std140, set = 1, binding = 0) buffer ModelTransforms {
    mat4 u_Model[];
};

layout(set = 4, binding = 0) uniform OrthoLight {
    mat4 u_LightViewProj;
    vec4 u_LightDir;
    vec4 u_LightColor;
};

// Vertex outputs
layout(location = 0) out vec3 f_WorldPos;
layout(location = 1) out vec3 f_Normal;
layout(location = 2) out vec2 f_UV;
layout(location = 3) out mat3 f_TBN;

void main()
{
    vec4 worldPos = u_Model[gl_InstanceIndex] * vec4(v_Position, 1.0);
    f_WorldPos = worldPos.xyz;

    mat3 nMat = mat3(transpose(inverse(u_Model[gl_InstanceIndex])));
    f_Normal = normalize(nMat * v_Normal);

    vec3 T = normalize(nMat * v_Tangent.xyz);
    vec3 N = f_Normal;
    vec3 B = cross(N, T) * v_Tangent.w;
    f_TBN = mat3(T, B, N);
   
    f_UV = v_TexCoord * vec2(1,-1);
    gl_Position = u_ViewProj * worldPos;
}
