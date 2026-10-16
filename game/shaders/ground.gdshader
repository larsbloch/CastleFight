// Map ground: grass on top with dirt paths painted in from a mask, packed earth on the sides of the terraces.
// Textures are mapped in world space, so they line up across terraces and ramps and never stretch.
shader_type spatial;
render_mode cull_disabled;

uniform sampler2D grass_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D variation_texture : filter_linear_mipmap, repeat_enable;
uniform sampler2D path_mask : filter_linear, repeat_disable;
uniform vec2 map_size = vec2(240.0, 108.0);
uniform float grass_tile = 16.0;
uniform float dirt_tile = 12.0;
uniform float variation_tile = 90.0;
// Higher terraces are drawn a little lighter so the levels read apart.
uniform float level_height = 2.0;
uniform float level_brightening = 0.06;

varying vec3 world_position;
varying vec3 world_normal;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec2 ground = world_position.xz;
	vec3 grass = texture(grass_texture, ground / grass_tile).rgb;
	vec3 dirt = texture(dirt_texture, ground / dirt_tile).rgb;

	// The mask is 0.5 on a path's edge; noise at a few tiles' scale frays that edge.
	float fray = texture(variation_texture, ground / 7.0).r - 0.5;
	float path = smoothstep(0.4, 0.6, texture(path_mask, ground / map_size).r + fray * 0.5);
	vec3 top = mix(grass, dirt, path);

	vec3 side_x = texture(dirt_texture, world_position.zy * vec2(1.0, -1.0) / dirt_tile).rgb;
	vec3 side_z = texture(dirt_texture, world_position.xy * vec2(1.0, -1.0) / dirt_tile).rgb;
	vec3 side = mix(side_x, side_z, step(abs(world_normal.x), abs(world_normal.z))) * 0.55;

	vec3 color = mix(side, top, smoothstep(0.5, 0.8, abs(world_normal.y)));
	float variation = texture(variation_texture, ground / variation_tile).r;
	color *= (0.85 + 0.3 * variation) * (1.0 + level_brightening * max(world_position.y / level_height, 0.0));

	ALBEDO = color;
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
