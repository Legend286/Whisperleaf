#version 450

layout(location = 0) in vec2 v_ScreenUV;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform sampler MainSampler;
layout(set = 0, binding = 1) uniform texture2D InputTex;

layout(set = 0, binding = 2) uniform DownsampleParams {
    vec2 TexelSize;
};

void main() {
    vec2 uv = vec2(v_ScreenUV.x, 1.0 - v_ScreenUV.y);
    vec2 texel = TexelSize;
    float x = texel.x;
    float y = texel.y;

    // 13-tap filter
    vec3 a = texture(sampler2D(InputTex, MainSampler), uv + vec2(-2*x, 2*y)).rgb;
    vec3 b = texture(sampler2D(InputTex, MainSampler), uv + vec2( 0,   2*y)).rgb;
    vec3 c = texture(sampler2D(InputTex, MainSampler), uv + vec2( 2*x, 2*y)).rgb;

    vec3 d = texture(sampler2D(InputTex, MainSampler), uv + vec2(-2*x, 0)).rgb;
    vec3 e = texture(sampler2D(InputTex, MainSampler), uv + vec2( 0,   0)).rgb;
    vec3 f = texture(sampler2D(InputTex, MainSampler), uv + vec2( 2*x, 0)).rgb;

    vec3 g = texture(sampler2D(InputTex, MainSampler), uv + vec2(-2*x, -2*y)).rgb;
    vec3 h = texture(sampler2D(InputTex, MainSampler), uv + vec2( 0,   -2*y)).rgb;
    vec3 i = texture(sampler2D(InputTex, MainSampler), uv + vec2( 2*x, -2*y)).rgb;

    vec3 j = texture(sampler2D(InputTex, MainSampler), uv + vec2(-x, y)).rgb;
    vec3 k = texture(sampler2D(InputTex, MainSampler), uv + vec2( x, y)).rgb;
    vec3 l = texture(sampler2D(InputTex, MainSampler), uv + vec2(-x, -y)).rgb;
    vec3 m = texture(sampler2D(InputTex, MainSampler), uv + vec2( x, -y)).rgb;

    // Jimenez's 13-tap filter weights
    // Use Karis Average only on the first downsample to reduce fireflies/blockiness
    // We can detect the first pass if the input is close to full resolution, 
    // or just pass a flag. For now, let's always use Karis but with a more stable formulation.
    
    // Divide into 4 groups of 4 taps to apply Karis Average
    vec3 group0 = (a + b + d + e) * 0.25;
    vec3 group1 = (b + c + e + f) * 0.25;
    vec3 group2 = (d + e + g + h) * 0.25;
    vec3 group3 = (e + f + h + i) * 0.25;
    vec3 group4 = (j + k + l + m) * 0.25;

    float w0 = 1.0 / (1.0 + max(group0.r, max(group0.g, group0.b)));
    float w1 = 1.0 / (1.0 + max(group1.r, max(group1.g, group1.b)));
    float w2 = 1.0 / (1.0 + max(group2.r, max(group2.g, group2.b)));
    float w3 = 1.0 / (1.0 + max(group3.r, max(group3.g, group3.b)));
    float w4 = 1.0 / (1.0 + max(group4.r, max(group4.g, group4.b)));

    vec3 color = group0 * w0 + group1 * w1 + group2 * w2 + group3 * w3 + group4 * w4;
    color /= (w0 + w1 + w2 + w3 + w4);

    out_Color = vec4(color, 1.0);
}
