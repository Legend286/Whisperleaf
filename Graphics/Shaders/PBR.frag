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

vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

float distributionGGX(vec3 N, vec3 H, float roughness)
{
    float a  = roughness * roughness;
    float a2 = a * a;
    float NdotH  = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    return a2 / (PI * denom * denom);
}

float geometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) * 0.125; // (r^2)/8
    return NdotV / (NdotV * (1.0 - k) + k);
}

float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx1 = geometrySchlickGGX(NdotV, roughness);
    float ggx2 = geometrySchlickGGX(NdotL, roughness);
    return ggx1 * ggx2;
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
        roughness = clamp(rma.g * u_RoughnessFactor, 0.04, 1.0);
        metallic  = clamp(rma.b * u_MetallicFactor,  0.0,  1.0);
    }
    else
    {
        float m = texture(sampler2D(MetallicTex,  MainSampler), f_UV).r;
        float r = texture(sampler2D(RoughnessTex, MainSampler), f_UV).g;
        float a = texture(sampler2D(OcclusionTex, MainSampler), f_UV).r;
        metallic  = clamp(m * u_MetallicFactor,  0.0, 1.0);
        roughness = clamp(r * u_RoughnessFactor, 0.04, 1.0);
        ao        = a;
    }

    vec4 baseColor = baseTex * u_BaseColorFactor;
    vec3 emissive  = emissiveT * u_EmissiveFactor;

    // Normal mapping (tangent → world)
    vec3 N_ts = normalize(normalTex * 2.0 - 1.0);
    vec3 N = normalize(f_TBN * N_ts);

    // Lighting
    vec3 V = normalize(u_CameraPos - f_WorldPos);
    vec3 L = normalize(vec3(0.5, 1.0, 0.3));
    vec3 H = normalize(V + L);
    vec3 lightColor = vec3(1.0);

    // Cook–Torrance GGX
    float NDF = distributionGGX(N, H, roughness);
    float G   = geometrySmith(N, V, L, roughness);
    vec3  F0  = mix(vec3(0.04), baseColor.rgb, metallic);
    vec3  F   = fresnelSchlick(max(dot(H, V), 0.0), F0);

    vec3 specNumerator   = NDF * G * F;
    float denom          = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.001;
    vec3 specular        = specNumerator / denom;

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);

    float NdotL = max(dot(N, L), 0.0);
    vec3 Lo = (kD * baseColor.rgb / PI + specular) * lightColor * NdotL;

    // Ambient (simple AO term; replace with IBL later)
    vec3 ambient = baseColor.rgb * ao * 0.03;

    vec3 color = ambient + Lo + emissive;

    // ACES-ish tone map + gamma
    color = color / (color + vec3(1.0));
    color = pow(color, vec3(1.0 / 2.2));

    out_Color = vec4(f_TBN * vec3(0,0,1),1.0f);
}
