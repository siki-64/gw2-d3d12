#pragma program AccumLightSunSh vertex=MainVertex pixel=MainPixel
#pragma program AccumLightSunShOcclusion vertex=MainVertex pixel=OccludedPixel

#define MAXIMUM_CASCADES 4
#define SHADOW_NEAR_TAPS 18
#define CONTACT_SHADOW_STEPS 10

float4 LightBuffer;
float4 WorldToShadowA;
float4 WorldToShadowB;
float4 WorldToShadowC;
float4 ShadowBias;
float4 TexelOffset;
float4 ScreenDimsInv;
float4 ClipToWorld[4];
float4 WorldCameraPos;
float4 ShadowMapConfiguration;
float4 ShadowMapCascadeSplitRadii;
float4 ShadowMapCascadeWorldToShadow[MAXIMUM_CASCADES * 4];
float4 ShadowMapCascadeBoundSphereCenters[MAXIMUM_CASCADES];
float4 PostLightShRed;
float4 PostLightShGreen;
float4 PostLightShBlue;
float4 PostLightShSun;
float4 PostLightShSunColor;
float4 PostLightShSunDir;
float4 DepthToViewZ;
float4 ContactShadowsSettings;
float4 ContactShadowsDebugSettings;
float4 ContactShadowsSettings2;
float4 ContactShadowsWorldToViewA;
float4 ContactShadowsWorldToViewB;
float4 ContactShadowsWorldToViewC;
float4 ContactShadowsProjection[4];
float4 ContactShadowsProjectionInv[4];
float4 PostLightConstants;
Texture2D ssNormalSpec;
SamplerState ssNormalSpecSampler;
Texture2D ssDepth;
SamplerState ssDepthSampler;
Texture2D ssShadowNear;
SamplerState ssShadowNearSampler;
Texture2D ssShadow;
SamplerState ssShadowSampler;
Texture2D ssOcclusion : register(t4);
SamplerState ssOcclusionSampler : register(s4);

static const float3 ShadowNearTaps[SHADOW_NEAR_TAPS] =
{
    float3(-0.220147, 0.976896, 0.053378),
    float3(-0.735514, 0.693436, 0.053266),
    float3(-0.200476, 0.310353, 0.058720),
    float3(0.180822, 0.454146, 0.058061),
    float3(0.292754, 0.937414, 0.053604),
    float3(0.564255, 0.207879, 0.057283),
    float3(0.178031, 0.024583, 0.059398),
    float3(0.613912, -0.205936, 0.056920),
    float3(-0.385540, -0.070092, 0.058610),
    float3(0.962838, 0.378319, 0.052983),
    float3(-0.886362, 0.032122, 0.054663),
    float3(-0.466531, -0.741458, 0.054779),
    float3(0.006773, -0.574796, 0.057479),
    float3(-0.739828, -0.410584, 0.055091),
    float3(0.590785, -0.697557, 0.054370),
    float3(-0.081436, -0.963262, 0.053781),
    float3(1.000000, -0.100160, 0.053335),
    float3(0.622430, 0.680868, 0.054278),
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 ScreenPosition : TEXCOORD0;
};

VertexOutput MainVertex(float4 position : POSITION0)
{
    VertexOutput output;
    output.Position = position;
    output.ScreenPosition = position;
    return output;
}

float3 WorldPositionOf(float2 screenCoordinate, float depth)
{
    float4 clip = float4(screenCoordinate * 2.0 - 1.0, depth, 1.0);
    clip.y = -clip.y * TexelOffset.z;
    float4 world = float4(dot(ClipToWorld[0], clip), dot(ClipToWorld[1], clip), dot(ClipToWorld[2], clip), dot(ClipToWorld[3], clip));
    return world.xyz / world.w;
}

float DistanceFromSunRayToCascadeCenter(float3 worldPosition, float3 sunDirection, int cascade)
{
    float3 center = ShadowMapCascadeBoundSphereCenters[cascade].xyz;
    float3 alongSun = worldPosition - sunDirection * dot(sunDirection, worldPosition - center);
    return length(center - alongSun);
}

float CascadeRadius(int cascade)
{
    return ShadowMapCascadeSplitRadii[cascade];
}

float CascadeBias(int cascade)
{
    return cascade == 0 ? ShadowBias.x : cascade == 1 ? ShadowBias.y : cascade == 2 ? ShadowBias.z : ShadowBias.w;
}

float CompareShadowNearLinear(float2 shadowCoordinate, float referenceDepth)
{
    float2 texelPosition = shadowCoordinate / ShadowMapConfiguration.zw - 0.5;
    float2 weight = frac(texelPosition);
    float2 gatherCoordinate = (floor(texelPosition) + 1.0) * ShadowMapConfiguration.zw;
    float4 stored = ssShadowNear.GatherRed(ssShadowNearSampler, gatherCoordinate);
    float4 lit = step(referenceDepth, stored);
    return lerp(lerp(lit.w, lit.z, weight.x), lerp(lit.x, lit.y, weight.x), weight.y);
}

float ShadowNear(float3 worldPosition, int cascade)
{
    float4 position = float4(worldPosition, 1.0);
    float2 shadowCoordinate = float2(dot(position, ShadowMapCascadeWorldToShadow[cascade * 4 + 0]), dot(position, ShadowMapCascadeWorldToShadow[cascade * 4 + 1]));
    float shadowDepth = dot(position, ShadowMapCascadeWorldToShadow[cascade * 4 + 2]) - CascadeBias(cascade);
    float angle = frac(sin(dot(shadowCoordinate, float2(25.9796, 156.466))) * 43758.546875) * 12.566;
    float sine;
    float cosine;
    sincos(angle, sine, cosine);
    float visibility = 0.0;
    for (int tap = 0; tap < SHADOW_NEAR_TAPS; tap++)
    {
        float3 weightedTap = ShadowNearTaps[tap];
        float2 rotated = float2(weightedTap.x * cosine - sine * weightedTap.y, weightedTap.y * cosine + sine * weightedTap.x);
        float2 offset = rotated * ShadowMapConfiguration.zw * 1.723;
        visibility += weightedTap.z * CompareShadowNearLinear(shadowCoordinate + offset, shadowDepth);
    }
    return visibility;
}

float ShadowFar(float3 worldPosition)
{
    float4 position = float4(worldPosition, 1.0);
    float2 shadowCoordinate = float2(dot(position, WorldToShadowA), dot(position, WorldToShadowB));
    float shadowDepth = dot(position, WorldToShadowC);
    float2 moments = ssShadow.SampleLevel(ssShadowSampler, shadowCoordinate, 0).xy;
    float variance = min(max(moments.y - moments.x * moments.x, 0.0) + 0.000005, 1.0);
    float distanceToMean = shadowDepth - moments.x;
    float upperBound = saturate((variance / (distanceToMean * distanceToMean + variance) - 0.4) * 1.666667);
    return max(upperBound, shadowDepth < moments.x ? 1.0 : 0.0);
}

float SunShadow(float3 worldPosition)
{
    int cascadeCount = (int)ShadowMapConfiguration.y;
    float3 sunDirection = normalize(PostLightShSunDir.xyz);
    int cascade = 0;
    [loop]
    while (cascade < cascadeCount && CascadeRadius(cascade) < DistanceFromSunRayToCascadeCenter(worldPosition, sunDirection, cascade))
        cascade++;

    float cascadeWeight = 0.0;
    if (cascade < cascadeCount)
    {
        float3 center = ShadowMapCascadeBoundSphereCenters[cascade].xyz;
        cascadeWeight = 1.0;
        if (dot(WorldCameraPos.xyz - center, worldPosition - center) < 0.0)
        {
            float halfRadius = CascadeRadius(cascade) * 0.5;
            float fade = saturate((DistanceFromSunRayToCascadeCenter(worldPosition, sunDirection, cascade) - halfRadius) / halfRadius);
            cascadeWeight = 1.0 - fade * fade * (3.0 - 2.0 * fade);
        }
    }

    int lastCascade = cascadeCount - 1;
    float farShadow = cascade >= lastCascade && cascadeWeight < 1.0 ? ShadowFar(worldPosition) : 0.0;
    float nearShadow = 0.0;
    float nextNearShadow = 0.0;
    if (cascade < cascadeCount && cascadeWeight > 0.0)
    {
        nearShadow = ShadowNear(worldPosition, cascade);
        if (cascade < lastCascade && cascadeWeight < 1.0) nextNearShadow = ShadowNear(worldPosition, cascade + 1);
    }

    if (cascadeWeight == 1.0) return nearShadow;
    return cascade < lastCascade ? lerp(nextNearShadow, nearShadow, cascadeWeight) : lerp(farShadow, nearShadow, cascadeWeight);
}

float ContactShadowJitter(float2 pixel)
{
    float amount = frac(ContactShadowsDebugSettings.y);
    float type = trunc(ContactShadowsDebugSettings.y);
    float noise;
    if (type == 0.0)
    {
        noise = frac(52.982918 * frac(dot(pixel, float2(0.067111, 0.005837))));
    }
    else if (type == 1.0)
    {
        noise = 2.0 * frac(sin(dot(pixel, float2(25.9796, 156.466))) * 43758.546875);
    }
    else if (type == 2.0)
    {
        float2 cell = floor(pixel);
        float2 local = frac(pixel);
        float2 gradient00 = frac(sin(float2(dot(cell, float2(25.9796, 156.466)), dot(cell * 2.0, float2(25.9796, 156.466)))) * 43758.546875) * 2.0 - 1.0;
        float2 cell10 = cell + float2(1.0, 0.0);
        float2 cell01 = cell + float2(0.0, 1.0);
        float2 cell11 = cell + float2(1.0, 1.0);
        float2 gradient10 = frac(sin(float2(dot(cell10, float2(25.9796, 156.466)), dot(cell10 * 2.0, float2(25.9796, 156.466)))) * 43758.546875) * 2.0 - 1.0;
        float2 gradient01 = frac(sin(float2(dot(cell01, float2(25.9796, 156.466)), dot(cell01 * 2.0, float2(25.9796, 156.466)))) * 43758.546875) * 2.0 - 1.0;
        float2 gradient11 = frac(sin(float2(dot(cell11, float2(25.9796, 156.466)), dot(cell11 * 2.0, float2(25.9796, 156.466)))) * 43758.546875) * 2.0 - 1.0;
        float2 fade = local * local * (3.0 - 2.0 * local);
        float bottom = lerp(dot(gradient00, local), dot(gradient10, local - float2(1.0, 0.0)), fade.x);
        float top = lerp(dot(gradient01, local - float2(0.0, 1.0)), dot(gradient11, local - float2(1.0, 1.0)), fade.x);
        noise = abs(lerp(bottom, top, fade.y));
    }
    else
    {
        noise = 0.0;
    }
    float steady = 1.0 - amount;
    return steady + noise * amount;
}

float ClipDepthTimesW(float ndcDepth, float projectionDeterminant)
{
    return ndcDepth * projectionDeterminant / (ContactShadowsProjection[2].z - ContactShadowsProjection[2].w * ndcDepth);
}

float ContactShadow(float2 pixel, float3 ndcOrigin, float3 towardSun)
{
    float projectionDeterminant = ContactShadowsProjection[2].z * ContactShadowsProjection[3].w - ContactShadowsProjection[2].w * ContactShadowsProjection[3].z;
    float originW = projectionDeterminant / (ContactShadowsProjection[2].z - ContactShadowsProjection[2].w * ndcOrigin.z);
    float4 clipOrigin = float4(ndcOrigin * originW, originW);
    float3 viewOrigin = (clipOrigin.x * ContactShadowsProjectionInv[0] + clipOrigin.y * ContactShadowsProjectionInv[1] + clipOrigin.z * ContactShadowsProjectionInv[2] + clipOrigin.w * ContactShadowsProjectionInv[3]).xyz;
    float3 viewTowardSun = float3(dot(ContactShadowsWorldToViewA.xyz, towardSun), dot(ContactShadowsWorldToViewB.xyz, towardSun), dot(ContactShadowsWorldToViewC.xyz, towardSun));
    float3 viewEnd = viewOrigin + viewTowardSun * 10000.0;
    float4 clipEnd = viewEnd.x * ContactShadowsProjection[0] + viewEnd.y * ContactShadowsProjection[1] + viewEnd.z * ContactShadowsProjection[2] + ContactShadowsProjection[3];
    float direction = clipEnd.w >= 0.0 ? 1.0 : -1.0;
    float3 delta = clipEnd.xyz / clipEnd.w - ndcOrigin;
    float2 screenSize = 1.0 / ScreenDimsInv.yx;
    float stepScale = direction * sqrt(1.0 / dot(screenSize * screenSize, delta.yx * delta.yx)) * ContactShadowsSettings.x;
    float3 step = stepScale * delta;
    float marchLength = length(step.xy * CONTACT_SHADOW_STEPS);
    float deltaLength = length(delta.xy);
    if (deltaLength < marchLength) step *= deltaLength / marchLength;

    float viewDistance = DepthToViewZ.x / (ndcOrigin.z + DepthToViewZ.y);
    float nearFar = saturate((viewDistance - ContactShadowsSettings2.w * 0.7) / (ContactShadowsSettings2.w * 0.3));
    nearFar = nearFar * nearFar * (3.0 - 2.0 * nearFar);
    float2 thickness = lerp(ContactShadowsSettings.yz, ContactShadowsSettings2.yz, nearFar);
    float3 jitteredStep = step * ContactShadowJitter(pixel);
    float falloffStart = ContactShadowsDebugSettings.z;

    [unroll]
    for (int index = 1; index <= CONTACT_SHADOW_STEPS; index++)
    {
        float3 position = ndcOrigin + jitteredStep * index;
        float2 coordinate = float2(position.x * 0.5 + 0.5, -position.y * 0.5 + 0.5);
        float sceneDepth = ssDepth.SampleLevel(ssDepthSampler, coordinate, 0).x;
        float penetration = ClipDepthTimesW(position.z, projectionDeterminant) - ClipDepthTimesW(sceneDepth, projectionDeterminant);
        if (penetration >= thickness.x && penetration <= thickness.y)
            return index >= falloffStart ? saturate((index - falloffStart) / (CONTACT_SHADOW_STEPS - falloffStart)) : 0.0;
    }
    return 1.0;
}

float4 AccumulateSun(VertexOutput input, bool occluded)
{
    float2 screenCoordinate = (input.Position.xy + TexelOffset.x) * ScreenDimsInv.xy;
    float4 normalSpec = ssNormalSpec.SampleLevel(ssNormalSpecSampler, screenCoordinate, 0);
    float depth = ssDepth.SampleLevel(ssDepthSampler, screenCoordinate, 0).x;
    float3 worldPosition = WorldPositionOf(screenCoordinate, depth);
    float4 normal = float4(normalize(normalSpec.xyz * 2.0 - 1.0), 1.0);
    float3 toCamera = normalize(WorldCameraPos.xyz - worldPosition);
    float3 halfVector = normalize(toCamera + PostLightShSunDir.xyz);
    float specular = 0.5 * pow(max(dot(normal.xyz, halfVector), 0.0001), normalSpec.w * LightBuffer.w);
    float shadow = SunShadow(worldPosition);
    if (ContactShadowsSettings.x > 0.0 && dot(normal.xyz, toCamera) >= 0.0)
        shadow = min(ContactShadow(input.Position.xy, float3(input.ScreenPosition.xy, depth), normalize(PostLightShSunDir.xyz)), shadow);
    float sunVisibility = PostLightShSunDir.w >= 0.0 ? (1.0 - PostLightShSunDir.w) * shadow : 1.0 - (1.0 - shadow) * (1.0 + PostLightShSunDir.w);
    float3 ambient = float3(dot(normal, PostLightShRed), dot(normal, PostLightShGreen), dot(normal, PostLightShBlue));
    float3 sun = PostLightShSunColor.xyz * sunVisibility * saturate(dot(normal, PostLightShSun));
    if (occluded)
    {
        float occlusion = ssOcclusion.SampleLevel(ssOcclusionSampler, screenCoordinate, 0).x - 1.0;
        ambient = lerp(ambient, sqrt(saturate(ambient)), PostLightConstants.z);
        ambient = max(ambient * (1.0 + occlusion * PostLightConstants.x), 0.0);
        sun = max(sun * (1.0 + occlusion * PostLightConstants.y), 0.0);
        specular = max(specular * (1.0 + occlusion * PostLightConstants.y), 0.0);
    }
    return float4(ambient + sun, max(sunVisibility, 0.5) * specular) * LightBuffer.x;
}

float4 MainPixel(VertexOutput input) : SV_TARGET
{
    return AccumulateSun(input, false);
}

float4 OccludedPixel(VertexOutput input) : SV_TARGET
{
    return AccumulateSun(input, true);
}
