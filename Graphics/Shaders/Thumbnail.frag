#version 450
layout(location = 0) in vec3 f_Normal;
layout(location = 1) in vec3 f_WorldPos;
layout(location = 2) in vec2 f_TexCoord;
layout(location = 3) in vec4 f_Tangent;

layout(location = 0) out vec4 o_Color;

layout(set = 0, binding = 0) uniform Params {
    mat4 ViewProjection;
    vec3 Color;
    float Roughness;
    vec3 CameraPos;
    float Metallic;
};

layout(set = 1, binding = 0) uniform texture2D BaseColorMap;
layout(set = 1, binding = 1) uniform texture2D NormalMap;
layout(set = 1, binding = 2) uniform texture2D RMAMap;
layout(set = 1, binding = 3) uniform sampler MainSampler;

const float PI = 3.14159265359;

float DistributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float num = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;
    return num / max(denom, 0.00001);
}

float GeometrySchlickGGX(float NdotV, float roughness) {
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);
    return ggx1 * ggx2;
}

vec3 FresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 CalculateLight(vec3 lightDir, vec3 lightColor, vec3 N, vec3 V, vec3 F0, vec3 albedo, float metal, float rough) {
    vec3 L = normalize(lightDir);
    vec3 H = normalize(V + L);
    
    float NdotL = max(dot(N, L), 0.0);
    
    // Specular
    float NDF = DistributionGGX(N, H, rough);
    float G = GeometrySmith(N, V, L, rough);
    vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    
    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * max(dot(N, V), 0.0) * NdotL + 0.0001;
    vec3 specular = numerator / denominator;
    
    vec3 kS = F;
    vec3 kD = vec3(1.0) - kS;
    kD *= (1.0 - metal);
    
    return (kD * albedo / PI + specular) * lightColor * NdotL;
}

void main() {
    // Sample Textures
    vec4 baseColorSample = texture(sampler2D(BaseColorMap, MainSampler), f_TexCoord);
    vec3 normalSample = texture(sampler2D(NormalMap, MainSampler), f_TexCoord).rgb;
    vec3 rmaSample = texture(sampler2D(RMAMap, MainSampler), f_TexCoord).rgb;
    
    // Mix with uniforms (use uniforms as factors)
    // If textures are missing (white), uniforms control result.
    vec3 albedo = Color * baseColorSample.rgb;
    float rough = Roughness * rmaSample.g;
    float metal = Metallic * rmaSample.b;
    float ao = rmaSample.r;
    
    // Normal Mapping
    vec3 N = normalize(f_Normal);
    vec3 T = normalize(f_Tangent.xyz);
    vec3 B = cross(N, T) * f_Tangent.w;
    mat3 TBN = mat3(T, B, N);
    
    vec3 mapN = normalSample * 2.0 - 1.0;
    N = normalize(TBN * mapN);
    
    vec3 V = normalize(CameraPos - f_WorldPos);
    
    vec3 F0 = vec3(0.04);
    F0 = mix(F0, albedo, metal);
    
    vec3 Lo = vec3(0.0);
    
    // Key Light
    vec3 keyPos = CameraPos + vec3(10, 10, 10);
    vec3 keyDir = normalize(keyPos - f_WorldPos);
    Lo += CalculateLight(keyDir, vec3(1.0, 0.9, 0.8) * 3.0, N, V, F0, albedo, metal, rough);
    
    // Fill Light
    vec3 fillPos = CameraPos + vec3(-10, 0, 5);
    vec3 fillDir = normalize(fillPos - f_WorldPos);
    Lo += CalculateLight(fillDir, vec3(0.6, 0.7, 0.9) * 1.5, N, V, F0, albedo, metal, rough);
    
    // Rim Light
    vec3 rimPos = CameraPos + vec3(0, 10, -10); 
    vec3 rimDir = normalize(rimPos - f_WorldPos);
    Lo += CalculateLight(rimDir, vec3(1.0, 1.0, 1.0) * 3.0, N, V, F0, albedo, metal, rough);
    
    vec3 ambient = vec3(0.03) * albedo * ao;
    vec3 color = ambient + Lo;
    
    // Tone mapping
    color = color / (color + vec3(1.0));
    color = pow(color, vec3(1.0/2.2));
    
    o_Color = vec4(color, 1.0);
}