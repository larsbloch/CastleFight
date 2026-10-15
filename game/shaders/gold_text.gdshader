// Gold lettering for the title: a vertical metal gradient with a bright bevel line and gritty wear.
// Only the fill (drawn with a white font colour) is changed; outline and shadow draw as usual.
shader_type canvas_item;

uniform float text_top = 0.0;
uniform float text_bottom = 100.0;

varying vec2 local_pos;

// Sine-free hash (Dave Hoskins): sin() of large arguments is imprecise on some GPUs.
float hash(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	vec4 glyph = texture(TEXTURE, UV);
	if (COLOR.r > 0.9 && COLOR.g > 0.9 && COLOR.b > 0.9) {
		float t = clamp((local_pos.y - text_top) / (text_bottom - text_top), 0.0, 1.0);
		vec3 gold = mix(vec3(1.0, 0.94, 0.7), vec3(0.93, 0.68, 0.28), smoothstep(0.0, 0.48, t));
		gold = mix(gold, vec3(0.5, 0.28, 0.08), smoothstep(0.52, 1.0, t));
		gold += vec3(0.35, 0.3, 0.18) * smoothstep(0.05, 0.0, abs(t - 0.5));
		float wear = noise(local_pos * 0.18) * 0.6 + noise(local_pos * 0.7) * 0.4;
		gold *= 0.72 + 0.4 * wear;
		gold *= 1.0 - 0.35 * step(0.985, hash(floor(local_pos * vec2(0.5, 2.0))));
		COLOR = vec4(gold, glyph.a * COLOR.a);
	} else {
		COLOR = COLOR * glyph;
	}
}
