#version 460

layout(location = 0) in vec2 f_UV;

layout(set = 2, binding = 0) uniform sampler MainSampler;
layout(set = 2, binding = 1) uniform texture2D BaseColorTex;

layout(set = 3, binding = 0) uniform MaterialParams {
    vec4  u_BaseColorFactor;
    vec4  u_EmissiveFactor;
    float u_MetallicFactor;
    float u_RoughnessFactor;
    int   u_UsePackedRMA;
    float u_AlphaCutoff;
    int   u_AlphaMode;
};

void main()
{
    float alpha = texture(sampler2D(BaseColorTex, MainSampler), f_UV).a * u_BaseColorFactor.a;

    // Alpha Mode: Mask = 1
    if (u_AlphaMode == 1 && alpha < u_AlphaCutoff) {
        discard;
    }
}