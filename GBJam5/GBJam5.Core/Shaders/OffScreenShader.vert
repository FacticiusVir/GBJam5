#version 450
#extension GL_ARB_separate_shader_objects : enable

layout(binding = 0) uniform UniformBufferObject {
    mat4 world;
    mat4 view;
    mat4 projection;
} ubo;

layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inUv;
layout(location = 2) in vec2 inInstancePosition;

layout(location = 0) out vec2 fragUv;

out gl_PerVertex {
    vec4 gl_Position;
};

void main() {
	vec4 worldPosition = ubo.world * vec4(inPosition, 0.0, 1.0);
    gl_Position = ubo.projection * ubo.view * (worldPosition + vec4(inInstancePosition, 0.0, 0.0));
	fragUv = inUv;
}