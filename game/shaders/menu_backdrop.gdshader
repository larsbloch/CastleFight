// Menu background: a smoky dusk sky glowing over a battlefield, two castle silhouettes facing each
// other, rising embers, vignette and film grain. Everything is procedural.
shader_type canvas_item;

uniform float aspect = 1.7778;

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

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 5; i++) {
		v += a * noise(p);
		p = p * 2.03 + vec2(1.7, 9.2);
		a *= 0.5;
	}
	return v;
}

// Top edge (in UV y, 0 = top) of a castle silhouette centred at cx; 2.0 means "no castle here".
float castle_top(float x, float cx, float scale) {
	float u = (x - cx) / scale;
	float top = 2.0;
	// Curtain wall with crenellations.
	if (abs(u) < 1.0) {
		top = 0.70 - step(0.5, fract(u * 9.0)) * 0.012;
	}
	// Two side towers and a tall keep.
	if (abs(abs(u) - 0.78) < 0.14) {
		top = min(top, 0.6 - step(0.5, fract(u * 22.0)) * 0.012);
	}
	if (abs(u) < 0.26) {
		top = min(top, 0.53 - step(0.5, fract(u * 20.0)) * 0.012);
	}
	// Spire on the keep.
	if (abs(u) < 0.07) {
		top = min(top, 0.53 - (0.07 - abs(u)) * 1.2);
	}
	return top;
}

void fragment() {
	vec2 uv = UV;
	vec2 p = vec2(uv.x * aspect, uv.y);
	float t = TIME;

	// Sky: near black at the top, ember red along the horizon, brightest in the middle where the armies meet.
	vec3 col = mix(vec3(0.025, 0.018, 0.016), vec3(0.32, 0.1, 0.035), smoothstep(0.1, 0.8, uv.y));
	float glow = exp(-pow((uv.x - 0.5) * 2.4, 2.0) - pow((uv.y - 0.78) * 4.0, 2.0));
	col += vec3(0.55, 0.22, 0.06) * glow * 0.8;

	// Drifting smoke.
	float smoke = fbm(p * 2.2 + vec2(t * 0.015, -t * 0.02));
	float smoke2 = fbm(p * 5.0 - vec2(t * 0.03, t * 0.01));
	col = mix(col, vec3(0.09, 0.07, 0.065), smoothstep(0.35, 0.85, smoke) * 0.75);
	col *= 0.8 + 0.35 * smoke2;

	// Ground and castles in silhouette.
	float ground = 0.8 + 0.025 * sin(uv.x * 7.0) + 0.03 * (fbm(vec2(uv.x * 6.0, 1.0)) - 0.5);
	float scale = 0.13;
	float top = min(castle_top(uv.x, 0.12, scale), castle_top(uv.x, 0.88, scale));
	float edge = min(ground, top);
	vec3 silhouette = vec3(0.018, 0.013, 0.011);
	// Rim light on the silhouettes from the fire behind.
	float rim = smoothstep(0.012, 0.0, uv.y - edge) * glow;
	col = mix(col, silhouette + vec3(0.25, 0.08, 0.02) * rim, step(edge, uv.y));

	// A few lit windows in the keeps.
	for (int side = 0; side < 2; side++) {
		float cx = side == 0 ? 0.12 : 0.88;
		vec2 w = vec2((uv.x - cx) / scale, uv.y);
		vec2 cell = vec2(floor(w.x * 12.0), floor(w.y * 70.0));
		float lit = step(0.8, hash(cell + float(side) * 13.0)) * step(abs(w.x), 0.85) * step(0.58, uv.y) * step(uv.y, 0.75);
		vec2 f = fract(vec2(w.x * 12.0, w.y * 70.0));
		float window = step(0.35, f.x) * step(f.x, 0.65) * step(0.3, f.y) * step(f.y, 0.75);
		float flicker = 0.75 + 0.25 * sin(t * 3.0 + hash(cell) * 30.0);
		col += vec3(1.0, 0.55, 0.15) * lit * window * flicker * 0.7 * step(edge, uv.y) * step(uv.y, 0.79);
	}

	// Embers rising from the battlefield.
	float embers = 0.0;
	for (int i = 0; i < 3; i++) {
		float fi = float(i);
		float s = 7.0 + fi * 6.0;
		vec2 q = vec2(p.x * s, p.y * s + t * (0.9 + fi * 0.5));
		q.x += sin(q.y * 0.6 + fi * 2.0 + t * 0.3) * 0.35;
		vec2 cell = floor(q);
		vec2 f = fract(q) - 0.5;
		float h = hash(cell + fi * 17.31);
		vec2 offset = vec2(hash(cell + 3.1), hash(cell + 7.7)) - 0.5;
		float d = length(f - offset * 0.6);
		float flicker = 0.6 + 0.4 * sin(t * 6.0 + h * 40.0);
		embers += smoothstep(0.07 - fi * 0.015, 0.0, d) * step(0.88, h) * flicker;
	}
	col += vec3(1.0, 0.45, 0.12) * embers * smoothstep(0.05, 0.75, uv.y) * 0.9;

	// Vignette and grain.
	vec2 v = (uv - 0.5) * vec2(1.0, 1.25);
	col *= 1.0 - smoothstep(0.35, 0.95, length(v)) * 0.75;
	col += (hash(uv * 1013.0 + fract(t) * 37.0) - 0.5) * 0.025;

	COLOR = vec4(col, 1.0);
}
