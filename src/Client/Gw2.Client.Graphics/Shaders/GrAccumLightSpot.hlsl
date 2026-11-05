#pragma program AccumLightSpot vertex=MainVertex pixel=MainPixel

float4 LightBuffer;
float4 TexelOffset;
float4 ScreenDimsInv;
float4 ClipToWorld[4];
float4 WorldCameraPos;
float4 SpotLightPosition;
float4 SpotLightDirection;
float4 SpotLightColor;
float4 SpotLightAttenuation;
Texture2D ssNormalSpec;
SamplerState ssNormalSpecSampler;
Texture2D ssDepth;
SamplerState ssDepthSampler;

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
    clip.y = -clip.y * TexelOffset.z;
    float4 world = float4(dot(ClipToWorld[0], clip), dot(ClipToWorld[1], clip), dot(ClipToWorld[2], clip), dot(ClipToWorld[3], clip));
    return world.xyz / world.w;
}

float4 MainPixel(VertexOutput input) : SV_TARGET
{
    float2 screenCoordinate = (input.Position.xy + TexelOffset.x) * ScreenDimsInv.xy;
    float4 normalSpec = ssNormalSpec.SampleLevel(ssNormalSpecSampler, screenCoordinate, 0);
    float depth = ssDepth.SampleLevel(ssDepthSampler, screenCoordinate, 0).x;
    float3 worldPosition = WorldPositionOf(screenCoordinate, depth);
    float3 normal = normalize(normalSpec.xyz * 2.0 - 1.0);
    float3 toCamera = normalize(WorldCameraPos.xyz - worldPosition);
    float3 toLight = SpotLightPosition.xyz - worldPosition;
    float distanceToLight = length(toLight);
    float3 lightDirection = toLight / distanceToLight;
    float cone = saturate(dot(-lightDirection, SpotLightDirection.xyz) * SpotLightAttenuation.x + SpotLightAttenuation.y);
    float attenuation = cone * saturate(distanceToLight * SpotLightAttenuation.z + SpotLightAttenuation.w);
    float diffuse = attenuation * saturate(dot(lightDirection, normal));
    float3 halfVector = normalize(lightDirection + toCamera);
    float specular = diffuse * pow(max(dot(normal, halfVector), 0.0001), normalSpec.w * LightBuffer.w);
    return float4(SpotLightColor.xyz * diffuse, specular * 0.5) * LightBuffer.x;
}
