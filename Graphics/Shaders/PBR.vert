#version 450

// Vertex inputs
layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 2) in vec4 v_Tangent;
layout(location = 3) in vec2 v_TexCoord;


// Camera buffer
layout(set = 0, binding = 0) uniform CameraBuffer {
    mat4 u_View;
    mat4 u_Proj;
    mat4 u_ViewProj;
    vec3 u_CameraPos;
};

struct InstanceData {
    mat4 WorldMatrix;
    uint MeshInfoIndex;
    uint padding1;
    uint padding2;
    uint padding3;
};

layout(std430, set = 1, binding = 0) readonly buffer InstanceDataStorage {
    InstanceData instances[];
};

struct MeshInfoGPU
{
    uint VertexOffset;
    uint IndexOffset;
    uint IndexCount;
    int MaterialIndex;
    vec3 AABBMin;
    uint _padding1; // Pad to 16 bytes
    vec3 AABBMax;
    uint _padding2; // Pad to 16 bytes
    uint _padding3; // For 64-byte alignment
    uint _padding4;
    uint _padding5;
    uint _padding6;
};

layout(std430, set = 1, binding = 1) readonly buffer MeshInfoStorage {
    MeshInfoGPU meshInfos[];
};


// Vertex outputs
layout(location = 0) out vec3 f_WorldPos;
layout(location = 1) out vec3 f_Normal;
layout(location = 2) out vec2 f_UV;
layout(location = 3) out mat3 f_TBN;
layout(location = 6) flat out int f_MaterialIndex;

void main()
{
    // World position
    InstanceData instance = instances[gl_InstanceIndex];
    vec4 worldPos = instance.WorldMatrix * vec4(v_Position, 1.0);
    f_WorldPos = worldPos.xyz;
    
    // Pass Material Index
    f_MaterialIndex = meshInfos[instance.MeshInfoIndex].MaterialIndex;

    // Normal (to world space)
    f_Normal = normalize(v_Normal);

    // Tangent space basis
    vec3 T = normalize(v_Tangent.xyz);
    vec3 N = normalize(f_Normal);
    vec3 B = cross(N, T) * v_Tangent.w; // reconstruct bitangent with sign
    f_TBN = mat3(T, B, N);
   
    f_UV = v_TexCoord * vec2(1,-1);

    // Clip space
    gl_Position = u_Proj * u_View * mat4(1) * worldPos;
}
