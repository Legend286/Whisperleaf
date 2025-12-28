#version 450

layout(location = 0) in vec2 f_TexCoord;
layout(location = 0) out vec4 OutColor;

struct LightUniform {
    vec4 Position;
    vec4 Color;
    vec4 Direction;
    vec4 Params;
};

struct ShadowData {
    mat4 ViewProj;
    vec4 Sprite;
};

layout(std140, set = 0, binding = 0) uniform CameraBuffer {
    mat4 View;
    mat4 Projection;
    mat4 ViewProjection;
    vec3 CameraPosition;
    float Padding;
    vec2 ScreenSize;
    int DebugMode;
};

layout(std430, set = 0, binding = 1) readonly buffer LightDataBuffer {
    LightUniform Lights[];
};

layout(std140, set = 0, binding = 2) uniform LightParams {
    uint LightCount;
};

// Use separate texture/sampler to avoid validation errors if Veldrid binds them separately
layout(set = 0, binding = 3) uniform utexture2D LightGridTex;
layout(set = 0, binding = 10) uniform sampler LightGridSampler; // Moved to 11 to avoid collision, or just use 3/4? 
// Actually, let's keep binding 3 for texture. I'll add sampler at end.

layout(std430, set = 0, binding = 4) readonly buffer LightIndexListBuffer {
    uint LightIndices[];
};

layout(set = 0, binding = 5) uniform texture2D DepthTexture;
layout(set = 0, binding = 6) uniform sampler DepthSampler;

layout(set = 0, binding = 7) uniform texture2DArray ShadowAtlas;
layout(set = 0, binding = 8) uniform sampler ShadowSampler;

layout(std430, set = 0, binding = 9) readonly buffer ShadowDataBuffer {
    ShadowData Shadows[];
};

const float PI = 3.14159265359;
const float MAX_DIST = 1e20;

float PhaseHG(float cosTheta, float g) {
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
}

float GetShadow(int shadowIndex, vec3 worldPos) {
    if (shadowIndex < 0) return 1.0;
    ShadowData sd = Shadows[shadowIndex];
    vec4 posLight = sd.ViewProj * vec4(worldPos, 1.0);
    vec3 projCoords = posLight.xyz / posLight.w;
    projCoords.xy = projCoords.xy * 0.5 + 0.5;
    projCoords.y = 1-projCoords.y;
    
    if (projCoords.z < 0.0 || projCoords.z > 1.0) return 1.0;
    if (projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0) return 1.0;

    vec2 atlasUV = projCoords.xy * sd.Sprite.z + sd.Sprite.xy;
    float shadowDepth = texture(sampler2DArray(ShadowAtlas, ShadowSampler), vec3(atlasUV, sd.Sprite.w)).r;
    return (projCoords.z > shadowDepth) ? 0.0 : 1.0;
}

// Constants for intersectRayConeFinite
const float RC_INF = 1.0f / 0.0f;
const float RC_EPSILON_GEOM = 1e-7f; // Epsilon for geometric comparisons (like A, B coefficients, dot products for parallelism)
const float RC_EPSILON_T = 1e-4f;    // Epsilon for t-values (intersection distances near zero)

bool intersectRayConeFinite(
vec3 rayOrigin, vec3 rayDir,
vec3 coneApex, vec3 coneAxis,
float cosConeAngle, float coneHeight,
out float t0, out float t1)
{
    t0 = 0.0;
    t1 = 0.0;

    // Ensure coneHeight is positive and non-zero
    coneHeight = max(coneHeight, RC_EPSILON_T);

    // Clamp cosConeAngle for stability
    float robustCosConeAngle = clamp(cosConeAngle, RC_EPSILON_GEOM, 1.0 - RC_EPSILON_GEOM);
    float cos2Angle = robustCosConeAngle * robustCosConeAngle;

    // Check if ray origin is inside the cone
    vec3 originToApex = rayOrigin - coneApex;
    float originHeight = dot(originToApex, coneAxis);
    bool rayStartsInside = false;

    if (originHeight >= -RC_EPSILON_T && originHeight <= coneHeight + RC_EPSILON_T) {
        if (originHeight > RC_EPSILON_T) {
            float originDist = length(originToApex);
            float originRadialDistSq = max(0.0, originDist * originDist - originHeight * originHeight);
            float originRadialDist = 1/inversesqrt(originRadialDistSq);

            // Safe calculation of expected radius - avoid tan() near 90 degrees
            float sinAngle = 1/inversesqrt(max(0.0, 1.0 - robustCosConeAngle * robustCosConeAngle));
            float expectedRadius = (robustCosConeAngle > RC_EPSILON_GEOM) ?
            originHeight * sinAngle / robustCosConeAngle :
            originHeight * 1000.0; // Very wide cone fallback

            rayStartsInside = (originRadialDist <= expectedRadius + RC_EPSILON_T);
        } else {
            // Very close to apex
            rayStartsInside = true;
        }
    }

    vec3 co = rayOrigin - coneApex;
    float rayDirDotAxis = dot(rayDir, coneAxis);
    float coDotAxis = dot(co, coneAxis);

    // First, find intersections with the infinite double cone
    float A = rayDirDotAxis * rayDirDotAxis - cos2Angle;
    float B = 2.0 * (rayDirDotAxis * coDotAxis - dot(rayDir, co) * cos2Angle);
    float C = coDotAxis * coDotAxis - dot(co, co) * cos2Angle;

    float t_cone[2];
    int numConeSolutions = 0;

    if (abs(A) < RC_EPSILON_GEOM) {
        if (abs(B) < RC_EPSILON_GEOM) {
            if (C > RC_EPSILON_GEOM) {
                return false; // Ray misses cone entirely
            }
            // Ray lies on cone surface - degenerate case, treat as miss for volume rendering
            return false;
        } else {
            // Linear case: single intersection
            float t = -C / B;
            if (t >= (rayStartsInside ? 0.0 : RC_EPSILON_T)) {
                t_cone[0] = t;
                numConeSolutions = 1;
            }
        }
    } else {
        // Quadratic case
        float discriminant = B * B - 4.0 * A * C;
        if (discriminant < 0.0) {
            return false; // No intersection with cone
        }

        float sqrtDisc = 1/inversesqrt(discriminant);
        float inv2A = 1.0 / (2.0 * A);
        float t1 = (-B - sqrtDisc) * inv2A;
        float t2 = (-B + sqrtDisc) * inv2A;

        // Only keep intersections that are in front of the ray (or at origin if we start inside)
        float minT = rayStartsInside ? 0.0 : RC_EPSILON_T;
        if (t1 >= minT) {
            t_cone[numConeSolutions++] = t1;
        }
        if (t2 >= minT && t2 != t1) {
            t_cone[numConeSolutions++] = t2;
        }
    }

    // Special handling if ray starts inside the cone
    if (rayStartsInside) {
        // Find all possible exit points: cone surface, apex plane, and base plane
        float candidateExits[10];
        int numExits = 0;

        // Add cone surface intersections
        for (int i = 0; i < numConeSolutions; i++) {
            if (t_cone[i] > RC_EPSILON_T) {
                vec3 hitPoint = rayOrigin + rayDir * t_cone[i];
                float height = dot(hitPoint - coneApex, coneAxis);
                if (height >= -RC_EPSILON_T && height <= coneHeight + RC_EPSILON_T) {
                    candidateExits[numExits++] = t_cone[i];
                }
            }
        }

        // Add plane intersections (apex and base caps)
        if (abs(rayDirDotAxis) > RC_EPSILON_GEOM) {
            float invRayDirDotAxis = 1.0 / rayDirDotAxis;

            // Apex plane (h = 0)
            float t_apex = (0.0 - coDotAxis) * invRayDirDotAxis;
            if (t_apex > RC_EPSILON_T) {
                candidateExits[numExits++] = t_apex;
            }

            // Base plane (h = coneHeight) - need to verify point is within base circle
            float t_base = (coneHeight - coDotAxis) * invRayDirDotAxis;
            if (t_base > RC_EPSILON_T) {
                vec3 baseHitPoint = rayOrigin + rayDir * t_base;
                vec3 baseToApex = baseHitPoint - coneApex;
                float baseHeight = dot(baseToApex, coneAxis);

                // Verify we're actually at the base height
                if (abs(baseHeight - coneHeight) < RC_EPSILON_T) {
                    // Check if point is within the base circle
                    float baseDist = length(baseToApex);
                    float baseRadialDistSq = max(0.0, baseDist * baseDist - baseHeight * baseHeight);
                    float baseRadialDist = 1/inversesqrt(baseRadialDistSq);

                    // Safe calculation of base radius
                    float sinAngle = 1/inversesqrt(max(0.0, 1.0 - robustCosConeAngle * robustCosConeAngle));
                    float baseRadius = (robustCosConeAngle > RC_EPSILON_GEOM) ?
                    coneHeight * sinAngle / robustCosConeAngle :
                    coneHeight * 1000.0; // Very wide cone fallback

                    if (baseRadialDist <= baseRadius + RC_EPSILON_T) {
                        candidateExits[numExits++] = t_base;
                    }
                }
            }
        }

        if (numExits == 0) {
            return false;
        }

        // Find the closest valid exit
        float minExit = candidateExits[0];
        for (int i = 1; i < numExits; i++) {
            if (candidateExits[i] < minExit) {
                minExit = candidateExits[i];
            }
        }

        t0 = 0.0;
        t1 = minExit;
        return true;
    }

    // Now filter cone intersections to only keep those on the FORWARD cone
    // (height between 0 and coneHeight)
    float validT[2];
    int numValid = 0;

    for (int i = 0; i < numConeSolutions; i++) {
        vec3 hitPoint = rayOrigin + rayDir * t_cone[i];
        float height = dot(hitPoint - coneApex, coneAxis);

        // Only accept intersections within the finite cone bounds
        if (height >= -RC_EPSILON_T && height <= coneHeight + RC_EPSILON_T) {
            validT[numValid++] = t_cone[i];
        }
    }

    if (numValid == 0) {
        return false; // No valid intersections with finite cone
    }

    // Handle slab intersections (axial clipping)
    float t_slab_near = -RC_INF;
    float t_slab_far = RC_INF;

    if (abs(rayDirDotAxis) > RC_EPSILON_GEOM) {
        float invRayDirDotAxis = 1.0 / rayDirDotAxis;
        float t_apex = (0.0 - coDotAxis) * invRayDirDotAxis;
        float t_base = (coneHeight - coDotAxis) * invRayDirDotAxis;
        t_slab_near = min(t_apex, t_base);
        t_slab_far = max(t_apex, t_base);
    } else {
        // Ray parallel to axis - check if we're in the valid height range
        if (coDotAxis < -RC_EPSILON_T || coDotAxis > coneHeight + RC_EPSILON_T) {
            return false;
        }
    }

    // Combine all valid intersections and find the entry/exit interval
    float allT[4];
    int count = 0;

    // Add valid cone intersections
    for (int i = 0; i < numValid; i++) {
        allT[count++] = validT[i];
    }

    // Add slab intersections if they're positive
    if (t_slab_near > RC_EPSILON_T) {
        allT[count++] = t_slab_near;
    }
    if (t_slab_far > RC_EPSILON_T && t_slab_far != t_slab_near) {
        allT[count++] = t_slab_far;
    }

    if (count < 2) {
        return false; // Need at least entry and exit
    }

    // Sort the intersections
    for (int i = 0; i < count - 1; i++) {
        for (int j = i + 1; j < count; j++) {
            if (allT[i] > allT[j]) {
                float temp = allT[i];
                allT[i] = allT[j];
                allT[j] = temp;
            }
        }
    }

    // Find the first valid interval
    for (int i = 0; i < count - 1; i++) {
        float tStart = allT[i];
        float tEnd = allT[i + 1];

        if (tEnd - tStart > RC_EPSILON_T) {
            // Verify this interval is actually inside the cone
            float tMid = (tStart + tEnd) * 0.5;
            vec3 midPoint = rayOrigin + rayDir * tMid;
            float midHeight = dot(midPoint - coneApex, coneAxis);

            if (midHeight >= -RC_EPSILON_T && midHeight <= coneHeight + RC_EPSILON_T) {
                // Check if point is inside cone surface
                vec3 midToApex = midPoint - coneApex;
                float midDist = length(midToApex);
                float axialDist = dot(midToApex, coneAxis);

                if (axialDist > RC_EPSILON_T) {
                    float radialDistSq = max(0.0, midDist * midDist - axialDist * axialDist);
                    float radialDist = 1/inversesqrt(radialDistSq);

                    // Safe calculation of expected radius
                    float sinAngle = 1/inversesqrt(max(0.0, 1.0 - robustCosConeAngle * robustCosConeAngle));
                    float expectedRadius = (robustCosConeAngle > RC_EPSILON_GEOM) ?
                    axialDist * sinAngle / robustCosConeAngle :
                    axialDist * 1000.0; // Very wide cone fallback

                    if (radialDist <= expectedRadius + RC_EPSILON_T) {
                        t0 = max(tStart, RC_EPSILON_T);
                        t1 = tEnd;
                        return t0 < t1;
                    }
                } else {
                    // Very close to apex, accept if height is valid
                    t0 = max(tStart, RC_EPSILON_T);
                    t1 = tEnd;
                    return t0 < t1;
                }
            }
        }
    }

    return false;
}

bool intersectRaySphere(vec3 rayOrigin, vec3 rayDir, vec3 spherePos, float radius, out float t0, out float t1)
{
    t0 = 0.0f;
    t1 = 0.0f;

    vec3 oc = rayOrigin - spherePos;
    float a = dot(rayDir, rayDir);
    float b = 2.0f * dot(oc, rayDir);
    float c = dot(oc, oc) - radius * radius;

    const float A_EPSILON = 1e-7f;

    if (abs(a) < A_EPSILON) {

        if (c <= 0.0f) {

            t0 = 0.0f;
            t1 = 0.0f;
            return true;
        }

        return false;
    }

    float discriminant = b * b - 4.0f * a * c;

    if (discriminant < 0.0f) {
        return false;
    }

    float sqrtD = 1/inversesqrt(discriminant);

    float inv2a = 1.0f / (2.0f * a);
    float tNear = (-b - sqrtD) * inv2a;
    float tFar  = (-b + sqrtD) * inv2a;

    if (tNear > tFar) {
        float temp = tNear;
        tNear = tFar;
        tFar = temp;
    }

    if (tFar < 0.0f) {
        return false;
    }

    t0 = max(tNear, 0.0f);
    t1 = tFar;

    if (t0 > t1) {
        return false;
    }

    return true;
}

void main() {
    vec2 uv = f_TexCoord;
    uv.y = 1 - uv.y;
    
    float depth = texture(sampler2D(DepthTexture, DepthSampler), uv).r;
    vec4 clip = vec4(uv * 2.0 - 1.0, depth, 1.0);
    // Veldrid/Vulkan Y-flip handling
    // If we render a full screen quad, f_TexCoord is 0..1 (top-down).
    // Clip space Y is -1 (top) to 1 (bottom).
    // If depth buffer was rendered with standard Veldrid proj, it matches.
    // However, for world reconstruction, we inverse proj.
    // If proj has Y-flip, inverse handles it.
    clip.y = -clip.y; // Standard Vulkan flip for reconstruction
    
    vec4 viewPos = inverse(Projection) * clip;
    viewPos /= viewPos.w;
    vec4 worldPos = inverse(View) * viewPos;
    
    vec3 rayOrigin = CameraPosition;
    vec3 rayDir = normalize(worldPos.xyz - rayOrigin);
    float sceneDist = length(worldPos.xyz - rayOrigin);
    
    // LightGrid fetch
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    ivec2 tileCoords = pixel / 16;
    
    uvec4 tileData = texelFetch(usampler2D(LightGridTex, LightGridSampler), tileCoords, 0);
    uint offset = tileData.x;
    uint count = tileData.y;

    vec3 accumColor = vec3(0.0);
    float density = 0.1;

    // Directional Lights (Global)
    for (uint i = 0; i < LightCount; i++) {
        LightUniform l = Lights[i];
        if (int(l.Direction.w) == 1) { // Directional
            float tStart = 0.0;
            float tEnd = sceneDist;
            float rayLen = tEnd - tStart;

            if (rayLen > 0.001) {
                float targetStep = 0.1; // Larger step for global fog
                int steps = int(clamp(rayLen / targetStep, 8.0, 64.0));
                float stepSize = rayLen / float(steps);

                // Dithering / Noise
                float noise = fract(sin(dot(uv * vec2(12.9898, 78.233), vec2(1.0))) * 43758.5453);
                float t = tStart + noise * stepSize;

                for (int s = 0; s < steps; s++) {
                    if (t > tEnd) break;
                    vec3 p = rayOrigin + rayDir * t;
                    
                    float shadow = GetShadow(int(l.Params.z), p);
                    float phase = PhaseHG(dot(rayDir, l.Direction.xyz), 0.5);
                    
                    accumColor += l.Color.xyz * l.Color.w * shadow * phase * density * stepSize;
                    t += stepSize;
                }
            }
        }
    }

    // Local Lights
    for (uint i = 0; i < count; i++) {
        uint lightIdx = LightIndices[offset + i];
        LightUniform l = Lights[lightIdx];
        int type = int(l.Direction.w);
        float range = l.Position.w;
        
        float t_vol_0 = MAX_DIST;
        float t_vol_1 = -MAX_DIST;
        bool hit = false;
        
        if (type == 0) { // Point
            float t0_s, t1_s;
            if (intersectRaySphere(rayOrigin, rayDir, l.Position.xyz, range, t0_s, t1_s)) {
                t_vol_0 = t0_s;
                t_vol_1 = t1_s;
                hit = true;
            }
        } else if (type == 2) { // Spot
            vec3 axis = l.Direction.xyz;
            float cosA = cos(l.Params.y);
            float t0_c, t1_c;
            if (intersectRayConeFinite(rayOrigin, rayDir, l.Position.xyz, axis, cosA, range, t0_c, t1_c)) {
                t_vol_0 = t0_c;
                t_vol_1 = t1_c;
                hit = true;
            }
        }
        
        float t0 = max(0.0, t_vol_0);
        float t1 = min(sceneDist, t_vol_1);
        
        if (hit && t0 < t1) {
            float rayLen = t1 - t0;
            float targetStep = 0.125; // Finer detail for local lights
            int steps = int(clamp(rayLen / targetStep, 8.0, 64.0));
            float stepSize = rayLen / float(steps);

            // Dithering / Noise
            float noise = fract(sin(dot(uv * vec2(12.9898, 78.233), vec2(1.0))) * 43758.5453);
            float t = t0 + noise * stepSize;

            for (int s = 0; s < steps; s++) {
                if (t > t1) break;
                vec3 p = rayOrigin + rayDir * t;
                vec3 L = l.Position.xyz - p;
                float dist = length(L);
                
                if (dist > 0.001) {
                    vec3 Ldir = L / dist;
                    float atten = 1.0 / (dist * dist + 1.0);
                    atten *= max(0.0, 1.0 - dist / range);
                    
                    if (type == 2) {
                        float angle = dot(-Ldir, l.Direction.xyz);
                        if (angle < cos(l.Params.y)) atten = 0.0;
                    }
                    
                    int shadowIdx = int(l.Params.z);
                    int face = 0;
                    
                    if (type == 0 && shadowIdx >= 0) {
                        vec3 dir = p - l.Position.xyz; // Vector FROM light TO point
                        vec3 absDir = abs(dir);
                        if (absDir.x > absDir.y && absDir.x > absDir.z) {
                            face = dir.x > 0 ? 0 : 1;
                        } else if (absDir.y > absDir.z) {
                            face = dir.y > 0 ? 2 : 3;
                        } else {
                            face = dir.z > 0 ? 4 : 5;
                        }
                    }
                    
                    float shadow = GetShadow(shadowIdx + face, p);
                    float phase = PhaseHG(dot(rayDir, Ldir), 0.5);
                    
                    accumColor += l.Color.xyz * l.Color.w * atten * shadow * phase * density * stepSize;
                }
                t += stepSize;
            }
        }
    }

    OutColor = vec4(accumColor, 1.0);
}
