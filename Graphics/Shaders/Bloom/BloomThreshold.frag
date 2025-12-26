#version 450

layout(location = 0) in vec2 v_ScreenUV;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform sampler MainSampler;
layout(set = 0, binding = 1) uniform texture2D InputTex;

layout(set = 1, binding = 0) uniform ThresholdParams {
    float Threshold;
    float SoftKnee;
};

void main() {
    vec2 uv = vec2(v_ScreenUV.x, 1.0 - v_ScreenUV.y);
    vec3 color = texture(sampler2D(InputTex, MainSampler), uv).rgb;
    
    // Soft knee thresholding
    float brightness = max(color.r, max(color.g, color.b));
    float knee = Threshold * SoftKnee;
    float soft = brightness - Threshold + knee;
    soft = clamp(soft, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee + 0.00001);
    
    float contribution = max(soft, brightness - Threshold);
    contribution /= max(brightness, 0.00001);
    
    out_Color = vec4(color * contribution, 1.0);
}
