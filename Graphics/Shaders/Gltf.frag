#version 450

layout(location = 0) in vec3 f_Normal;
layout(location = 0) out vec4 out_Color;

void main()
{
    vec3 N = normalize(f_Normal);
    out_Color = vec4(N * 0.5 + 0.5, 1.0); // just visualize normals
}
