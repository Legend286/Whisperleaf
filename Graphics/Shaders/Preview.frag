#version 460

layout(location = 0) in vec3 f_WorldPos;
layout(location = 1) in vec3 f_Normal;
layout(location = 2) in vec2 f_UV;
layout(location = 3) in mat3 f_TBN;

layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform CameraBuffer {
    mat4 u_View;
    mat4 u_Proj;
    mat4 u_ViewProj;
    vec3 u_CameraPos;
    float u_Padding0;
    vec2 u_ScreenSize;
    int u_DebugMode;
};

layout(set = 2, binding = 0) uniform sampler MainSampler;
layout(set = 2, binding = 1) uniform texture2D BaseColorTex;
layout(set = 2, binding = 2) uniform texture2D NormalTex;
layout(set = 2, binding = 3) uniform texture2D MetallicTex;
layout(set = 2, binding = 4) uniform texture2D RoughnessTex;
layout(set = 2, binding = 5) uniform texture2D OcclusionTex;
layout(set = 2, binding = 6) uniform texture2D EmissiveTex;

layout(set = 3, binding = 0) uniform MaterialParams {
    vec4  u_BaseColorFactor;
    vec4  u_EmissiveFactor;
    float u_MetallicFactor;
    float u_RoughnessFactor;
    int   u_UsePackedRMA;
    float u_AlphaCutoff;
    int   u_AlphaMode;
    float _mpad0, _mpad1, _mpad2;
};

// Simplified Ortho Light
layout(set = 4, binding = 0) uniform OrthoLight {
    mat4 u_LightViewProj;
    mat4 u_ShadowViewProj;
    vec4 u_LightDir; // xyz = dir, w = intensity
    vec4 u_LightColor; // xyz = color
};

layout(set = 5, binding = 0) uniform texture2D ShadowMap;
layout(set = 5, binding = 1) uniform samplerShadow ShadowSampler;

const float PI = 3.14159265359;

float D_GGX(float NdotH, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float denom = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
    return a2 / (PI * denom * denom);
}

float G_Smith(float NdotV, float NdotL, float roughness) {
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    float G_V = NdotV / (NdotV * (1.0 - k) + k);
    float G_L = NdotL / (NdotL * (1.0 - k) + k);
    return G_V * G_L;
}

vec3 F_Schlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 EvaluatePBR(vec3 N, vec3 V, vec3 L, vec3 albedo, float metallic, float roughness, vec3 lightColor, float lightIntensity) {
    vec3 H = normalize(V + L);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);

    float D = D_GGX(NdotH, roughness);
    float G = G_Smith(NdotV, NdotL, roughness);
    vec3 F = F_Schlick(HdotV, F0);

    vec3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
    vec3 diffuse = (kD * albedo) / PI;

    return (diffuse + specular) * lightColor * lightIntensity * NdotL;
}

void main() {
    vec4 baseTex = texture(sampler2D(BaseColorTex, MainSampler), f_UV);
    vec3 normalTex = texture(sampler2D(NormalTex, MainSampler), f_UV).rgb;
    vec3 emissiveT = texture(sampler2D(EmissiveTex, MainSampler), f_UV).rgb;

    float metallic, roughness, ao;
    if (u_UsePackedRMA != 0) {
        vec3 rma = texture(sampler2D(MetallicTex, MainSampler), f_UV).rgb;
        ao = rma.r;
        roughness = rma.g * u_RoughnessFactor;
        metallic = rma.b * u_MetallicFactor;
    } else {
        ao = texture(sampler2D(OcclusionTex, MainSampler), f_UV).r;
        roughness = texture(sampler2D(RoughnessTex, MainSampler), f_UV).g * u_RoughnessFactor;
        metallic = texture(sampler2D(MetallicTex, MainSampler), f_UV).b * u_MetallicFactor;
    }
    
    roughness = clamp(roughness, 0.04, 1.0);
    metallic = clamp(metallic, 0.0, 1.0);

    vec4 baseColor = baseTex * u_BaseColorFactor;
    vec3 emissive = emissiveT * u_EmissiveFactor.rgb;

    vec3 N_ts = normalize(normalTex * 2.0 - 1.0);
    vec3 N = normalize(f_TBN * N_ts);
    vec3 V = normalize(u_CameraPos - f_WorldPos);
    vec3 L = normalize(-u_LightDir.xyz);

    // Shadow calculation using tight shadow matrix
    vec4 posShadow = u_ShadowViewProj * vec4(f_WorldPos, 1.0);
    vec3 shadowCoords = posShadow.xyz / posShadow.w;
    shadowCoords.xy = shadowCoords.xy * 0.5 + 0.5;
    shadowCoords.y = 1.0 - shadowCoords.y; // Correct for Vulkan Y

    float shadow = 1.0;
    if (shadowCoords.z >= 0.0 && shadowCoords.z <= 1.0 && 
        shadowCoords.x >= 0.0 && shadowCoords.x <= 1.0 && 
        shadowCoords.y >= 0.0 && shadowCoords.y <= 1.0) {
        shadow = texture(sampler2DShadow(ShadowMap, ShadowSampler), vec3(shadowCoords.xy, shadowCoords.z - 0.001));
    }

    // Smooth edge falloff for light intensity using wider scene matrix
    vec4 posLight = u_LightViewProj * vec4(f_WorldPos, 1.0);
    vec3 lightCoords = posLight.xyz / posLight.w;
    lightCoords.xy = lightCoords.xy * 0.5 + 0.5;
    
    vec2 edge = smoothstep(0.0, 0.05, lightCoords.xy) * smoothstep(1.0, 0.95, lightCoords.xy);
    float intensity = u_LightDir.w * edge.x * edge.y;

    vec3 lighting = EvaluatePBR(N, V, L, baseColor.rgb, metallic, roughness, u_LightColor.rgb, intensity);
    vec3 ambient = baseColor.rgb * ao * 0.1;
    vec3 finalColor = lighting * shadow + ambient + emissive;

    float alpha = baseTex.a * u_BaseColorFactor.a;
    if (u_AlphaMode == 1 && alpha < u_AlphaCutoff) discard;

    out_Color = vec4(finalColor, 1.0);
}
