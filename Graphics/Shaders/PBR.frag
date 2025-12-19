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
    int   u_Padding1;
};

struct Light {
    vec4 position; // w = range
    vec4 color;    // w = intensity
    vec4 direction;// w = type
    vec4 params;   // x = innerCone, y = outerCone
};

layout(set = 4, binding = 0) readonly buffer LightData {
    Light lights[];
};

layout(set = 5, binding = 0) uniform LightParams {
    uint u_LightCount;
    vec3 _padding;
};

struct ShadowInfo {
    mat4 viewProj;
    vec4 atlasRect; // x,y, scale, layer
};

layout(set = 6, binding = 0) readonly buffer ShadowBuffer {
    ShadowInfo shadows[];
};

layout(set = 7, binding = 0) uniform texture2DArray ShadowMap;
layout(set = 7, binding = 1) uniform sampler ShadowSampler;

// Forward+
layout(set = 8, binding = 0) uniform usampler2D u_LightGrid; // Texture + Sampler
// Actually Veldrid separates Texture and Sampler in set 8.
// Binding 0: LightGrid Texture
// Binding 1: LightGrid Sampler (Added in GltfPass)
// Binding 2: LightIndices
// But usampler2D combines them.
// Let's explicitly define them if using separate bindings?
// No, in GLSL `uniform usampler2D` expects a combined image sampler.
// Veldrid handles `Texture` + `Sampler` bindings mapping to `combined image sampler` if we use `usampler2D`?
// Or we can use `uniform utexture2D` and `uniform sampler` and `texture(sampler2D(t, s), ...)`?
// texelFetch works on `usampler2D` too.
// Let's assume Veldrid Set 8 Binding 0 is Texture, Binding 1 is Sampler.
// So:
layout(set = 8, binding = 0) uniform utexture2D u_LightGridTex;
layout(set = 8, binding = 1) uniform sampler u_LightGridSampler;
// Combine them for texelFetch? texelFetch takes sampler2D.
// texelFetch(sampler2D(tex, samp), ...)

layout(set = 8, binding = 2) readonly buffer LightIndexList {
    uint u_LightIndices[];
};

float CalcShadow(int index, vec3 worldPos, vec3 N, vec3 L, vec3 lightPos, float lightRadius) {
    ShadowInfo info = shadows[index];
    float NdotL = max(dot(N, L), 0.0);
    float dist = distance(lightPos, worldPos);
    float proximity = clamp(1.0 - (dist / lightRadius), 0.0, 1.0);

    float normalOffset = (0.002 * (1.0 - NdotL) + 0.02 * proximity);
    vec3 biasedWorldPos = worldPos + normalize(N) * normalOffset;
    vec4 posLight = info.viewProj * vec4(biasedWorldPos, 1.0);
    
    vec3 projCoords = posLight.xyz / posLight.w;
    
    if (projCoords.z > 1.0 || projCoords.x < -1.0 || projCoords.x > 1.0 || projCoords.y < -1.0 || projCoords.y > 1.0)
        return 1.0;
    projCoords.y *= -1;
    projCoords.xy = projCoords.xy * 0.5 + 0.5;
    
    vec2 uv = info.atlasRect.xy + projCoords.xy * info.atlasRect.z;
    float layer = info.atlasRect.w;
    float currentDepth = projCoords.z;
    float bias = 0.0002;
    vec2 minUV = info.atlasRect.xy;
    vec2 maxUV = info.atlasRect.xy + vec2(info.atlasRect.z);
    
    vec2 texelSize = (1.0 / (2048.0 * vec2(info.atlasRect.z)));
    const float PCF_RADIUS = 0.125f;
    float tileRes = 2048.0 * info.atlasRect.z;
    float pcfScale = clamp(tileRes / 512.0, 0.25, 1.0);
    vec2 pcfStep = texelSize * PCF_RADIUS * pcfScale;
    
                float visibility = 0.0;
    
    
    
        for (int x = -1; x <= 1; x++) {
    
            for (int y = -1; y <= 1; y++) {
    
                vec2 offset = vec2(x, y) * pcfStep;
    
                vec2 sampleUV = clamp(uv + offset, minUV + texelSize, maxUV - texelSize);
    
                
    
                // Use sampler2DArrayShadow for hardware comparison
    
                // texture() returns the comparison result (0.0 or 1.0) directly (filtered)
    
                float shadowTest = texture(
    
                    sampler2DArrayShadow(ShadowMap, ShadowSampler),
    
                    vec4(sampleUV, layer, currentDepth - bias)
    
                );
    
    
    
                visibility += shadowTest;
    
            }
    
        }
    
        return visibility / 9.0;
    
    }

const float PI = 3.14159265359;

float D_GGX(float NdotH, float roughness)
{
    float a     = roughness * roughness;
    float a2    = a * a;
    float denom = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
    return a2 / (PI * denom * denom);
}

float G_Smith(float NdotV, float NdotL, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    float G_V = NdotV / (NdotV * (1.0 - k) + k);
    float G_L = NdotL / (NdotL * (1.0 - k) + k);
    return G_V * G_L;
}

vec3 F_Schlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

vec3 EvaluatePBR(vec3 N, vec3 V, vec3 L, vec3 albedo, float metallic, float roughness, vec3 F0, vec3 lightColor, float lightIntensity)
{
    vec3 H = normalize(V + L);
    vec3 baseF0 = vec3(0.04);
    F0 = mix(baseF0, albedo, metallic);

    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);

    float D = D_GGX(NdotH, roughness);
    float G = G_Smith(NdotV, NdotL, roughness);
    vec3  F = F_Schlick(HdotV, F0);

    vec3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
    vec3 kD = (1.0 - F) * (1.0 - metallic);
    vec3 diffuse = (kD * albedo) / PI;

    return (diffuse + specular) * lightColor * lightIntensity * NdotL;
}

void main()
{
    vec4 baseTex   = texture(sampler2D(BaseColorTex, MainSampler), f_UV);
    vec3 normalTex = texture(sampler2D(NormalTex, MainSampler), f_UV).rgb;
    vec3 emissiveT = texture(sampler2D(EmissiveTex, MainSampler), f_UV).rgb;

    float metallic;
    float roughness;
    float ao;

    vec3 rma = texture(sampler2D(MetallicTex, MainSampler), f_UV).rgb;
    ao        = rma.r;
    roughness = rma.g * u_RoughnessFactor;
    metallic  = rma.b * u_MetallicFactor;
    
    roughness = clamp(roughness, 0.04, 1.0);
    metallic  = clamp(metallic, 0.0, 1.0);

    vec4 baseColor = baseTex * u_BaseColorFactor;
    vec3 emissive  = emissiveT * u_EmissiveFactor.rgb;

    vec3 N_ts = normalize(normalTex * 2.0 - 1.0);
    vec3 N = normalize(f_TBN * N_ts);
    vec3 V = normalize(u_CameraPos - f_WorldPos);

    vec3 lighting = vec3(0.0);
    vec3 F0 = vec3(0.04);
    F0 = mix(F0, baseColor.rgb, metallic);

    // Forward+ Tiled Lighting
    ivec2 tileCoord = ivec2(vec2(gl_FragCoord.x, gl_FragCoord.y) / 16.0);
    
    // Using samplerless fetch via sampler object adapter
    uvec2 gridData = texelFetch(usampler2D(u_LightGridTex, u_LightGridSampler), tileCoord, 0).rg;
    
    uint offset = gridData.r;
    uint count = gridData.g;

    for (uint i = 0; i < count; ++i)
    {
        uint lightIdx = u_LightIndices[offset + i];
        Light light = lights[lightIdx];
        
        vec3 L;
        float attenuation = 1.0;
        int type = int(light.direction.w);

        if (type == 1)// Directional
        {
            L = normalize(-light.direction.xyz);
        }
        else // Point (0) or Spot (2)
        {
            vec3 lightVec = light.position.xyz - f_WorldPos;
            float dist = length(lightVec);
            L = normalize(lightVec);
            float range = light.position.w;

            float spotAttenuation = 1.0f;
            if (type == 2)
            {
                L = normalize(lightVec);
                vec3 D = normalize(light.direction.xyz);
                float cosInner = cos(light.params.x);
                float cosOuter = cos(light.params.y);
                float cosTheta = dot(-L, D);
                spotAttenuation = clamp((cosTheta - cosOuter) / (cosInner - cosOuter), 0.0, 1.0);
            }

            float distSq = dist * dist;
            attenuation = 1.0 / (1.0 + 0.1 * dist + 0.01 * distSq);
            float window = max(0.0, 1.0 - pow(dist/range, 4.0));
            attenuation *= spotAttenuation * window;
        }

        float shadow = 1.0;
        int shadowIdx = int(light.params.z);
        if (shadowIdx >= 0) {
            if (type == 0) { // Point
                 vec3 dir = -L;
                 vec3 absDir = abs(dir);
                 int face = 0;
                 if (absDir.x > absDir.y && absDir.x > absDir.z) {
                    face = dir.x > 0 ? 0 : 1;
                 } else if (absDir.y > absDir.z) {
                    face = dir.y > 0 ? 2 : 3;
                 } else {
                    face = dir.z > 0 ? 4 : 5;
                 }
                 shadow = CalcShadow(shadowIdx + face, f_WorldPos, f_Normal, L, light.position.xyz, light.position.w);
            } else {
                 shadow = CalcShadow(shadowIdx, f_WorldPos, f_Normal, L, light.position.xyz, light.position.w);
            }
        }

        vec3 contribution = EvaluatePBR(N, V, L, baseColor.rgb, metallic, roughness, F0, light.color.rgb, light.color.w * attenuation);
        lighting += contribution * shadow;
    }

    vec3 ambient = baseColor.rgb * ao * 0.03;
    vec3 finalColor = lighting + ambient + emissive;

    if (u_DebugMode == 1) {
        float t = float(count) / 10.0;
        vec3 heatmap = mix(vec3(0, 0, 1), vec3(1, 0, 0), clamp(t, 0.0, 1.0));
        finalColor = mix(finalColor, heatmap, 0.5);
        vec2 grid = fract(gl_FragCoord.xy / 16.0);
        if (grid.x < 0.05 || grid.y < 0.05) finalColor = vec3(1.0);
    }
        
    out_Color = vec4(finalColor, 1.0f);
}
