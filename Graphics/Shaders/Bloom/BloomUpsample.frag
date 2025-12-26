#version 450

layout(location = 0) in vec2 v_ScreenUV;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform sampler MainSampler;
layout(set = 0, binding = 1) uniform texture2D InputTex;

layout(set = 0, binding = 2) uniform UpsampleParams {
    float FilterRadius;
};

void main() {
    vec2 uv = vec2(v_ScreenUV.x, 1.0 - v_ScreenUV.y);
    float x = FilterRadius;
    float y = FilterRadius;

    // 3x3 tent filter
    vec3 a = texture(sampler2D(InputTex, MainSampler), uv + vec2(-x, y)).rgb;
    vec3 b = texture(sampler2D(InputTex, MainSampler), uv + vec2( 0, y)).rgb;
    vec3 c = texture(sampler2D(InputTex, MainSampler), uv + vec2( x, y)).rgb;

    vec3 d = texture(sampler2D(InputTex, MainSampler), uv + vec2(-x, 0)).rgb;
    vec3 e = texture(sampler2D(InputTex, MainSampler), uv + vec2( 0, 0)).rgb;
    vec3 f = texture(sampler2D(InputTex, MainSampler), uv + vec2( x, 0)).rgb;

    vec3 g = texture(sampler2D(InputTex, MainSampler), uv + vec2(-x,-y)).rgb;
    vec3 h = texture(sampler2D(InputTex, MainSampler), uv + vec2( 0,-y)).rgb;
    vec3 i = texture(sampler2D(InputTex, MainSampler), uv + vec2( x,-y)).rgb;

    vec3 color = e*4.0;
    color += (b+d+f+h)*2.0;
    color += (a+c+g+i);
    color *= 1.0 / 16.0;

    out_Color = vec4(color, 1.0);
}
