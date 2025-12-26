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

// Bicubic filter weights
vec4 cubic(float v) {
    vec4 n = vec4(1.0, 2.0, 3.0, 4.0) - v;
    vec4 s = n * n * n;
    float x = s.x;
    float y = s.y - 4.0 * s.x;
    float z = s.z - 4.0 * s.y + 6.0 * s.x;
    float w = 6.0 - x - y - z;
    return vec4(x, y, z, w) * (1.0/6.0);
}

vec3 textureBicubic(texture2D tex, sampler smp, vec2 uv) {
    vec2 texSize = textureSize(sampler2D(tex, smp), 0);
    vec2 invTexSize = 1.0 / texSize;

    uv = uv * texSize - 0.5;

    vec2 fxy = fract(uv);
    uv -= fxy;

    vec4 xweight = cubic(fxy.x);
    vec4 yweight = cubic(fxy.y);

    vec4 xpos = vec4(uv.x - 1.0, uv.x, uv.x + 1.0, uv.x + 2.0);
    vec4 ypos = vec4(uv.y - 1.0, uv.y, uv.y + 1.0, uv.y + 2.0);

    xpos *= invTexSize.x;
    ypos *= invTexSize.y;

    vec3 color = vec3(0.0);

    for (int y = 0; y < 4; y++) {
        for (int x = 0; x < 4; x++) {
            color += texture(sampler2D(tex, smp), vec2(xpos[x], ypos[y])).rgb * xweight[x] * yweight[y];
        }
    }

    return color;
}

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
    vec2 uv = vec2(v_ScreenUV.x, 1.0 - v_ScreenUV.y);
    vec3 scene = texture(sampler2D(SceneTex, MainSampler), uv).rgb;
    
    // Higher quality bicubic sampling for the bloom texture
    vec3 bloom = textureBicubic(BloomTex, MainSampler, uv);
    
    vec3 color = scene + bloom * BloomIntensity;
    
    // Tonemapping & Exposure
    color *= Exposure;
    color = ACESFilm(color);
    
    // Gamma correction
    color = pow(color, vec3(1.0/2.2));
    
    out_Color = vec4(color, 1.0);
}
