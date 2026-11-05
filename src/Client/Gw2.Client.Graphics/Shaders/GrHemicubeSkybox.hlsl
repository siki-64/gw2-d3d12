#pragma program Sky vertex=MainVertex pixel=SkyPixel

float4 CameraPosition;
float4 ViewProjection[4];
float4 World[4];
float4 WorldViewProjection[4];

float4 FogColorFar;
float4 shRed;
float4 shGreen;
float4 shBlue;
float4 skypara;
float4 sunDir;
float4 sunClr;

Texture2D ss00;
Texture2D ss10;
SamplerState ss00Sampler;
SamplerState ss10Sampler;

static const float DomeScale = 100.0;

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 TextureCoordinate : TEXCOORD0;
    float3 CameraToDome : TEXCOORD1;
};

VertexOutput MainVertex(float4 position : POSITION0, float2 textureCoordinate : TEXCOORD0)
{
    VertexOutput output;
    output.TextureCoordinate = textureCoordinate;
    output.CameraToDome = CameraPosition.xyz - position.xyz * DomeScale;
    float3 farPlane = float3(dot(position, WorldViewProjection[0]), dot(position, WorldViewProjection[1]), dot(position, WorldViewProjection[3]));
    float4 belowHorizon = float4(dot(position, World[0]), dot(position, World[1]), max(dot(position, World[2]), 0.0), 1.0);
    float3 horizonStrip = float3(dot(belowHorizon, ViewProjection[0]), dot(belowHorizon, ViewProjection[1]), dot(belowHorizon, ViewProjection[3]));
    output.Position = 0.0 < position.z ? horizonStrip.xyzz : farPlane.xyzz;
    return output;
}

float4 SkyPixel(VertexOutput input) : SV_TARGET
{
    float3 direction = normalize(input.CameraToDome);
    float horizon = 1.0 - direction.z;
    horizon = sunDir.z * (1.0 - horizon) + horizon;
    float sunFacing = dot(sunDir.xyz, direction) * 0.25 + 0.75;
    float scatter = horizon * sunFacing * sunFacing;
    float4 shDirection = float4(-direction, 1.0);
    float3 ambient = float3(dot(shDirection, shRed), dot(shDirection, shGreen), dot(shDirection, shBlue));
    float3 lit = lerp(ambient, sunClr.rgb, scatter);
    float hazeHeight = max(direction.z, 0.0) - skypara.x;

    float4 current = ss00.Sample(ss00Sampler, input.TextureCoordinate);
    float4 previous = ss10.Sample(ss10Sampler, input.TextureCoordinate);
    float3 texel = lerp(previous.rgb * previous.a, current.rgb * current.a, skypara.w);
    float3 bright = texel * sunDir.w;

    float3 multiplied = lit * bright;
    float3 screened = bright + lit - 2.0 * multiplied;
    float luminance = dot(bright, float3(0.3, 0.59, 0.11));
    float3 blended = luminance * luminance * screened + multiplied;
    float3 halfway = (blended - bright) * 0.5 + bright;
    float3 sky = lerp(halfway, blended, scatter);
    sky = lerp(bright, sky, sunClr.w);

    float haze = saturate(hazeHeight / skypara.y);
    haze = (1.0 - (3.0 - 2.0 * haze) * haze * haze) * skypara.z;
    return float4(lerp(sky, FogColorFar.rgb, haze), 0.0);
}
