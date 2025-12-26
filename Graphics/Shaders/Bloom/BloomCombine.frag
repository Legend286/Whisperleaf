#version 450

layout(location = 0) in vec2 v_ScreenUV;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform sampler MainSampler;
layout(set = 0, binding = 1) uniform texture2D SceneTex;
layout(set = 0, binding = 2) uniform texture2D BloomTex;

layout(set = 1, binding = 0) uniform CombineParams {
    float BloomIntensity;
    float Exposure;
};

// ACES Tone Mapping
vec3 ACESFilm(vec3 x) {
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x*(a*x+b))/(x*(c*x+d)+e), 0.0, 1.0);
}

void main() {
    vec3 scene = texture(sampler2D(SceneTex, MainSampler), v_ScreenUV).rgb;
    vec3 bloom = texture(sampler2D(BloomTex, MainSampler), v_ScreenUV).rgb;
    
    vec3 color = scene + bloom * BloomIntensity;
    
    // Tonemapping & Exposure
    color *= Exposure;
    color = ACESFilm(color);
    
    // Gamma correction
    color = pow(color, vec3(1.0/2.2));
    
    out_Color = vec4(color, 1.0);
}
