#version 450

layout(location = 0) out vec2 f_TexCoord;

void main() {
    vec2 pos = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    gl_Position = vec4(pos * 2.0 - 1.0, 0.0, 1.0);
    f_TexCoord = pos;
}
