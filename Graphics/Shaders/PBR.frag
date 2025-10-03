#version 450

layout(location = 0) in vec3 f_WorldPos;
layout(location = 1) in vec3 f_Normal;
layout(location = 2) in vec2 f_UV;
layout(location = 3) in mat3 f_TBN;

layout(location = 0) out vec4 out_Color;

// Camera (vertex+fragment)
layout(set = 0, binding = 0) uniform CameraBuffer {
    mat4 u_View;
    mat4 u_Proj;
    mat4 u_ViewProj;
    vec3 u_CameraPos;
    float padding0;
};

// Material resources: one sampler, multiple textures
layout(set = 1, binding = 0) uniform sampler MainSampler;
layout(set = 1, binding = 1) uniform texture2D BaseColorTex;
layout(set = 1, binding = 2) uniform texture2D NormalTex;
layout(set = 1, binding = 3) uniform texture2D MetallicTex;   // if you pack RMA, bind same tex to 3/4/5
layout(set = 1, binding = 4) uniform texture2D RoughnessTex;
layout(set = 1, binding = 5) uniform texture2D OcclusionTex;
layout(set = 1, binding = 6) uniform texture2D EmissiveTex;

// Optional material factors UBO (kept simple)
layout(set = 1, binding = 7) uniform MaterialParams {
    vec4  u_BaseColorFactor;  // rgba
    vec3  u_EmissiveFactor;   // rgb
    float u_MetallicFactor;   // scalar multiplier
    float u_RoughnessFactor;  // scalar multiplier
    int   u_UsePackedRMA;     // 0 = separate maps, 1 = packed in MetallicTex (R=AO,G=R,B=M)
};

// -------------------- PBR helpers --------------------
const float PI = 3.14159265359;

vec3 getCameraPos(mat4 view)
{
    // Invert the view matrix
    mat4 invView = inverse(view);
    return invView[3].xyz; // translation column
}

// GGX Normal Distribution Function (NDF)
float D_GGX(float NdotH, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float denom = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
    return a2 / (PI * denom * denom);
}

// Geometry function (Smith GGX)
float G_Smith(float NdotV, float NdotL, float roughness) {
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    float G_V = NdotV / (NdotV * (1.0 - k) + k);
    float G_L = NdotL / (NdotL * (1.0 - k) + k);

    return G_V * G_L;
}

// Fresnel (Schlick's approximation)
vec3 F_Schlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

// Main PBR lighting function
vec3 PBR(vec3 N, vec3 V, vec3 L, vec3 albedo, float metallic, float roughness, vec3 F0, vec3 lightColor, float lightIntensity) {
    
    vec3 H = normalize(V + L);
    
    vec3 baseF0 = vec3(0.04); 
    F0 = mix(baseF0, albedo, metallic); 
    
    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);

    float D = D_GGX(NdotH, roughness); 
    float G = G_Smith(NdotV, NdotL, roughness);
    vec3 F = F_Schlick(HdotV, F0);
    
    vec3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
    
    vec3 kD = (1.0 - F) * (1.0 - metallic);
    vec3 diffuse = (kD * albedo) / PI;
    
    vec3 lighting = (diffuse + specular) * lightColor * lightIntensity * NdotL;

    return lighting;
}

// -------------------- Main --------------------
void main()
{
    // Sample textures (combine sampler+texture objects)
    vec4 baseTex   = texture(sampler2D(BaseColorTex,   MainSampler), f_UV);
    vec3 normalTex = texture(sampler2D(NormalTex,      MainSampler), f_UV).rgb;
    vec3 emissiveT = texture(sampler2D(EmissiveTex,    MainSampler), f_UV).rgb;

    float metallic, roughness, ao;
    if (u_UsePackedRMA == 1)
    {
        // Packed RMA in MetallicTex: R=AO, G=R, B=M
        vec3 rma = texture(sampler2D(MetallicTex, MainSampler), f_UV).rgb;
        ao        = rma.r;
        roughness = clamp(rma.g, 0.04, 1.0);
        metallic  = clamp(rma.b,  0.0,  1.0);
    }
    else
    {
        float m = texture(sampler2D(MetallicTex,  MainSampler), f_UV).r;
        float r = texture(sampler2D(RoughnessTex, MainSampler), f_UV).g;
        float a = texture(sampler2D(OcclusionTex, MainSampler), f_UV).r;
        metallic  = clamp(m,  0.0, 1.0);
        roughness = clamp(r, 0.04, 1.0);
        ao        = a;
    }

    vec4 baseColor = baseTex;
    vec3 emissive  = emissiveT;

    // Normal mapping (tangent → world)
    vec3 N_ts = normalize(normalTex * 2 - 1);
    vec3 N = normalize(f_TBN * N_ts);

    // Lighting
    vec3 V = normalize(u_CameraPos - f_WorldPos);
    vec3 L = normalize(vec3(1,8,-2) - f_WorldPos);
    vec3 H = normalize(V + L);
    vec3 lightColor = vec3(1.0);
    
    vec3 final = PBR(N, V, L, baseTex.rgb, metallic, 0.2, vec3(0.04), lightColor, 1.0f);

    
    out_Color = vec4(final, baseColor.a);
}
