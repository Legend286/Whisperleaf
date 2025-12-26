#version 450

layout(location = 0) in vec2 v_ScreenUV;

layout(location = 0) out vec4 OutputColor;

// --- Atmospheric Scattering Parameters ---
// Based on "Display of The Earth Taking into Account Atmospheric Scattering" (Nishita et al. 1993)
// and "Accurate Atmospheric Scattering" (Sean O'Neil, GPU Gems 2)

layout(set = 0, binding = 0) uniform CameraBuffer {
    mat4 View;
    mat4 Projection;
    mat4 ViewProjection;
    vec3 CameraPosition;
    float Padding;
    vec2 ScreenSize;
    int DebugMode;
};

// We will use the primary directional light from the LightBuffer if available,
// but for the SkyboxPass we might bind a specific SkyParam buffer.
// For simplicity, let's define a dedicated SkyUniform buffer.

layout(set = 1, binding = 0) uniform SkyParams {
    vec3 SunDirection;
    float SunIntensity;
    vec3 PlanetCenter; // Usually (0, -Radius, 0)
    float PlanetRadius;
    vec3 RayleighScattering; // (5.8e-6, 1.35e-5, 3.31e-5) typically
    float AtmosphereRadius;
    vec3 MieScattering;      // (2.1e-5, ...)
    float MieG;              // Phase function g factor (0.76)
};

// Constants
const int NUM_SAMPLES = 16;
const float PI = 3.14159265359;

// Ray-Sphere intersection
vec2 RaySphereIntersect(vec3 rayOrigin, vec3 rayDir, vec3 sphereCenter, float sphereRadius) {
    vec3 offset = rayOrigin - sphereCenter;
    float a = 1.0; // dot(rayDir, rayDir) is 1.0 if normalized
    float b = 2.0 * dot(offset, rayDir);
    float c = dot(offset, offset) - sphereRadius * sphereRadius;
    
    float d = b * b - 4.0 * a * c;
    
    if (d < 0.0) return vec2(-1.0);
    
    float sqrtD = sqrt(d);
    return vec2((-b - sqrtD) / (2.0 * a), (-b + sqrtD) / (2.0 * a));
}

void main() {
    // 1. Setup Geometry per Pixel to avoid interpolation artifacts
    vec4 clipPos = vec4(v_ScreenUV * 2.0 - 1.0, 1.0, 1.0);
    mat4 invProj = inverse(Projection);
    mat4 invView = inverse(View);
    
    vec4 viewPos = invProj * clipPos;
    viewPos /= viewPos.w; // Perspective divide to get View Space position
    
    // View Space -> World Space Direction (ignoring translation)
    vec3 worldDir = (invView * vec4(viewPos.xyz, 0.0)).xyz;
    vec3 rayDir = normalize(worldDir);
    vec3 rayStart = CameraPosition;
    
    // Check intersection with Atmosphere (Outer Sphere)
    vec2 hitAtmosphere = RaySphereIntersect(rayStart, rayDir, PlanetCenter, AtmosphereRadius);
    float distToAtmosphereFar = hitAtmosphere.y;
    float distToAtmosphereNear = hitAtmosphere.x;
    
    // We must be inside the atmosphere for this shader to work as expected typically, 
    // or we handle the "from space" case. Assuming camera is near ground/inside.
    // If we are inside, near hit is < 0.
    
    float rayLength = distToAtmosphereFar;
    if (distToAtmosphereNear > 0.0) {
        // We are outside atmosphere looking in?
        // For a skybox usually we render from ground. 
        // Let's assume ground view.
        // If distToAtmosphereFar < 0, we are looking away from it? No, sphere encloses us.
    }
    
    // Check intersection with Planet (Inner Sphere) to block view below horizon
    vec2 hitPlanet = RaySphereIntersect(rayStart, rayDir, PlanetCenter, PlanetRadius);
    if (hitPlanet.x > 0.0) {
        rayLength = min(rayLength, hitPlanet.x);
    }
    
    // 2. Numerical Integration (Raymarching)
    float stepSize = rayLength / float(NUM_SAMPLES);
    vec3 currentPos = rayStart;
    
    vec3 totalRayleigh = vec3(0.0);
    vec3 totalMie = vec3(0.0);
    
    float opticalDepthRayleigh = 0.0;
    float opticalDepthMie = 0.0;
    
    // Scale Height (Height where density drops by factor e)
    // Earth: Rayleigh ~8km, Mie ~1.2km
    // We derive scale height from radii ratios roughly or use constants.
    // Hardcoded for earth-like look:
    float scaleHeightRayleigh = (AtmosphereRadius - PlanetRadius) * 0.15; 
    float scaleHeightMie = (AtmosphereRadius - PlanetRadius) * 0.02; // Mie concentrates low
    
    // Phase Functions
    float mu = dot(rayDir, SunDirection);
    float phaseR = 3.0 / (16.0 * PI) * (1.0 + mu * mu);
    float g = MieG;
    float g2 = g * g;
    float phaseM = 3.0 / (8.0 * PI) * ((1.0 - g2) * (1.0 + mu * mu)) / ((2.0 + g2) * pow(1.0 + g2 - 2.0 * g * mu, 1.5));
    
    for (int i = 0; i < NUM_SAMPLES; i++) {
        vec3 samplePos = currentPos + rayDir * (stepSize * 0.5);
        float height = length(samplePos - PlanetCenter) - PlanetRadius;
        
        if (height < 0.0) height = 0.0; // clamp
        
        float hr = exp(-height / scaleHeightRayleigh) * stepSize;
        float hm = exp(-height / scaleHeightMie) * stepSize;
        
        opticalDepthRayleigh += hr;
        opticalDepthMie += hm;
        
        // Light Ray (Sun to Sample)
        vec2 hitSun = RaySphereIntersect(samplePos, SunDirection, PlanetCenter, AtmosphereRadius);
        float distToSun = hitSun.y;
        
        // This secondary loop is expensive. 
        // Optimization: Use look-up table or reduced samples.
        // For CLI shader, we'll do 4 samples for light ray.
        float stepSizeSun = distToSun / 4.0;
        float opticalDepthSunR = 0.0;
        float opticalDepthSunM = 0.0;
        vec3 sunSamplePos = samplePos;
        
        // Check if blocked by earth
        // Simple horizon check
        
        for (int j = 0; j < 4; j++) {
            vec3 sunPos = sunSamplePos + SunDirection * (stepSizeSun * 0.5);
            float hSun = length(sunPos - PlanetCenter) - PlanetRadius;
            opticalDepthSunR += exp(-hSun / scaleHeightRayleigh) * stepSizeSun;
            opticalDepthSunM += exp(-hSun / scaleHeightMie) * stepSizeSun;
            sunSamplePos += SunDirection * stepSizeSun;
        }
        
        vec3 attenuation = exp(-(RayleighScattering * (opticalDepthRayleigh + opticalDepthSunR) + MieScattering * (opticalDepthMie + opticalDepthSunM)));
        
        totalRayleigh += attenuation * hr;
        totalMie += attenuation * hm;
        
        currentPos += rayDir * stepSize;
    }
    
    vec3 color = SunIntensity * (totalRayleigh * RayleighScattering * phaseR + totalMie * MieScattering * phaseM);
    
    // 3. Physically Correct Sun Disk with Atmospheric Lensing
    // Increased base size for visual impact (approx 1.7 degrees diameter)
    float sunAltitude = SunDirection.y;
    float lensing = 1.0 + 0.5 * clamp(1.0 - abs(sunAltitude) * 2.0, 0.0, 1.0);
    float sunRadiusRad = 0.02 * lensing; 
    float sunCosThreshold = cos(sunRadiusRad);

    if (mu > sunCosThreshold && hitPlanet.x < 0.0) {
        vec3 sunExtinction = exp(-(RayleighScattering * opticalDepthRayleigh + MieScattering * opticalDepthMie));
        // Smoother edge for a natural look
        float edge = smoothstep(sunCosThreshold, sunCosThreshold + 0.0002, mu);
        color += sunExtinction * 1000.0 * edge; 
    }
    
    OutputColor = vec4(color, 1.0);
}
