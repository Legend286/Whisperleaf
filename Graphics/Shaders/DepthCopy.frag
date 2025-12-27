#version 450

layout(set = 0, binding = 0) uniform texture2D InputTexture;
layout(set = 0, binding = 1) uniform sampler InputSampler;

layout(location = 0) in vec2 f_TexCoord;
layout(location = 0) out float f_Depth;

void main() {
    f_Depth = texture(sampler2D(InputTexture, InputSampler), f_TexCoord).r;
}