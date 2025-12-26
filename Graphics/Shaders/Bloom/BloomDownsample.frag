#version 450

layout(location = 0) in vec2 v_ScreenUV;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform sampler MainSampler;
layout(set = 0, binding = 1) uniform texture2D InputTex;

layout(set = 0, binding = 2) uniform DownsampleParams {
    vec2 TexelSize;
};

void main() {
    vec2 texel = TexelSize;
    float x = texel.x;
    float y = texel.y;

    // 13-tap filter
    vec3 a = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2(-2*x, 2*y)).rgb;
    vec3 b = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2( 0,   2*y)).rgb;
    vec3 c = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2( 2*x, 2*y)).rgb;

    vec3 d = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2(-2*x, 0)).rgb;
    vec3 e = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2( 0,   0)).rgb;
    vec3 f = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2( 2*x, 0)).rgb;

    vec3 g = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2(-2*x, -2*y)).rgb;
    vec3 h = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2( 0,   -2*y)).rgb;
    vec3 i = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2( 2*x, -2*y)).rgb;

    vec3 j = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2(-x, y)).rgb;
    vec3 k = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2( x, y)).rgb;
    vec3 l = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2(-x, -y)).rgb;
    vec3 m = texture(sampler2D(InputTex, MainSampler), v_ScreenUV + vec2( x, -y)).rgb;

    // Karis Average to reduce fireflies/blockiness on high-intensity sources
    float w0 = 1.0 / (1.0 + max(e.r, max(e.g, e.b)));
    float w1 = 1.0 / (1.0 + max(a.r, max(a.g, a.b)));
    float w2 = 1.0 / (1.0 + max(b.r, max(b.g, b.b)));
    float w3 = 1.0 / (1.0 + max(c.r, max(c.g, c.b)));
    float w4 = 1.0 / (1.0 + max(d.r, max(d.g, d.b)));
    float w5 = 1.0 / (1.0 + max(f.r, max(f.g, f.b)));
    float w6 = 1.0 / (1.0 + max(g.r, max(g.g, g.b)));
    float w7 = 1.0 / (1.0 + max(h.r, max(h.g, h.b)));
    float w8 = 1.0 / (1.0 + max(i.r, max(i.g, i.b)));
    float w9 = 1.0 / (1.0 + max(j.r, max(j.g, j.b)));
    float w10 = 1.0 / (1.0 + max(k.r, max(k.g, k.b)));
    float w11 = 1.0 / (1.0 + max(l.r, max(l.g, l.b)));
    float w12 = 1.0 / (1.0 + max(m.r, max(m.g, m.b)));

    vec3 color = e*0.125*w0;
    color += (a*w1 + c*w3 + g*w6 + i*w8)*0.03125;
    color += (b*w2 + d*w4 + f*w5 + h*w7)*0.0625;
    color += (j*w9 + k*w10 + l*w11 + m*w12)*0.125;
    
    float totalWeight = 0.125*w0 + 0.03125*(w1+w3+w6+w8) + 0.0625*(w2+w4+w5+w7) + 0.125*(w9+w10+w11+w12);
    color /= totalWeight;

    out_Color = vec4(color, 1.0);
}
