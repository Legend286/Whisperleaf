#version 450

layout(set = 0, binding = 0) uniform texture2D InputTexture;
layout(set = 0, binding = 1) uniform sampler InputSampler;

layout(location = 0) out float f_Depth;

void main() {
    // gl_FragCoord.xy corresponds to the center of the current output pixel.
    // Truncating to ivec2 gives the output pixel index.
    ivec2 pos = ivec2(gl_FragCoord.xy);
    
    // The 2x2 block in the input texture starts at pos * 2.
    ivec2 srcPos = pos * 2;
    
    // Get input dimensions to handle boundary cases for odd sizes.
    ivec2 inputSize = textureSize(sampler2D(InputTexture, InputSampler), 0);
    ivec2 maxPos = inputSize - 1;

    // Fetch the 4 texels using integer coordinates.
    // Construct the combined sampler inline for each call to satisfy SPIR-V constraints.
    float d0 = texelFetch(sampler2D(InputTexture, InputSampler), min(srcPos + ivec2(0, 0), maxPos), 0).r;
    float d1 = texelFetch(sampler2D(InputTexture, InputSampler), min(srcPos + ivec2(1, 0), maxPos), 0).r;
    float d2 = texelFetch(sampler2D(InputTexture, InputSampler), min(srcPos + ivec2(0, 1), maxPos), 0).r;
    float d3 = texelFetch(sampler2D(InputTexture, InputSampler), min(srcPos + ivec2(1, 1), maxPos), 0).r;

    f_Depth = max(max(d0, d1), max(d2, d3));
}