#pragma program ShadowedFog vertex=MainVertex pixel=ShadowedFogPixel

float4 ScreenDimsInv;
float4 FogLightTexelSize;
float4 ClipToWorld[4];
float4 WorldCameraPos;
float4 FogColorNearMinusFar;
float4 FogColorFar;
float4 FogColorHeight;
float4 FogDepthCue;
float4 FogParam0;
float4 PostLightShSunDir;
float4 VolumetricFogSettings;
Texture2D ssDepth;
SamplerState ssDepthSampler;
Texture2D ssFogLight;
SamplerState ssFogLightSampler;

struct VertexOutput
{
    float4 Position : SV_POSITION;
};

VertexOutput MainVertex(float4 position : POSITION0)
{
    VertexOutput output;
    output.Position = position;
    return output;
}

float3 WorldPositionOf(float2 screenCoordinate, float depth)
{
    float4 clip = float4(screenCoordinate * 2.0 - 1.0, depth, 1.0);
    clip.y = -clip.y;
    float4 world = float4(dot(ClipToWorld[0], clip), dot(ClipToWorld[1], clip), dot(ClipToWorld[2], clip), dot(ClipToWorld[3], clip));
    return world.xyz / world.w;
}

float4 ExeFog(float3 worldPosition)
{
    float distanceToCamera = distance(worldPosition, WorldCameraPos.xyz);
    float2 fogs = float2(distanceToCamera, worldPosition.z) * FogParam0.xz + FogParam0.yw;
    fogs = 1.0 - saturate(exp2(-fogs));
    float4 fogColorDistance = FogColorFar + FogColorNearMinusFar * (1.0 - fogs.x);
    fogs.x = min(fogs.x, fogColorDistance.w);
    fogs.y = min(fogs.y, FogColorHeight.w) * min(distanceToCamera * FogDepthCue.x, 1.0);
    float4 distanceFog = float4(fogColorDistance.xyz * fogs.x, fogs.x);
    float4 heightFog = float4(FogColorHeight.xyz * fogs.y, fogs.y);
    return heightFog * (1.0 - distanceFog.w) + distanceFog;
}

float HenyeyGreenstein(float cosine, float anisotropy)
{
    float squared = anisotropy * anisotropy;
    return (1.0 - squared) / pow(abs(1.0 + squared - 2.0 * anisotropy * cosine), 1.5);
}

float LitRatio(float2 screenCoordinate)
{
    float2 offset = FogLightTexelSize.xy * 0.5;
    float sum = ssFogLight.SampleLevel(ssFogLightSampler, screenCoordinate + float2(-offset.x, -offset.y), 0).x;
    sum += ssFogLight.SampleLevel(ssFogLightSampler, screenCoordinate + float2(offset.x, -offset.y), 0).x;
    sum += ssFogLight.SampleLevel(ssFogLightSampler, screenCoordinate + float2(-offset.x, offset.y), 0).x;
    sum += ssFogLight.SampleLevel(ssFogLightSampler, screenCoordinate + float2(offset.x, offset.y), 0).x;
    return sum * 0.25;
}

float3 WorldPositionOfPixel(float4 position)
{
    float2 screenCoordinate = position.xy * ScreenDimsInv.xy;
    float depth = ssDepth.SampleLevel(ssDepthSampler, screenCoordinate, 0).x;
    return WorldPositionOf(screenCoordinate, depth);
}

float ShadowStrength()
{
    return 1.0 - abs(PostLightShSunDir.w);
}

float4 ShadowedFogPixel(VertexOutput input) : SV_TARGET0
{
    float3 worldPosition = WorldPositionOfPixel(input.Position);
    float shadowedShare = (1.0 - saturate(LitRatio(input.Position.xy * ScreenDimsInv.xy))) * ShadowStrength();
    float anisotropy = VolumetricFogSettings.y;
    float cosine = dot(normalize(worldPosition - WorldCameraPos.xyz), normalize(PostLightShSunDir.xyz));
    float towardSun = HenyeyGreenstein(cosine, anisotropy) / HenyeyGreenstein(1.0, anisotropy);
    float sunShare = lerp(VolumetricFogSettings.x, VolumetricFogSettings.z, towardSun);
    return float4(ExeFog(worldPosition).xyz * (sunShare * shadowedShare), 0.0);
}
