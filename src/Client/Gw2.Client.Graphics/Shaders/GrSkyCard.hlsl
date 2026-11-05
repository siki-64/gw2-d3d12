#pragma program SkyCard vertex=MainVertex pixel=CardPixel
#pragma program AdditiveSkyCard vertex=MainVertex pixel=AdditiveCardPixel
#pragma program LightRaySourceCard vertex=MainVertex pixel=LightRaySourceCardPixel
#pragma program ClearLightRaySources vertex=FullScreenVertex pixel=NoLightRaySourcePixel

float4 World[4];
float4 WorldViewProjection[4];

float4 FogColorFar;
float4 TimeOfDay;
float4 shRed;
float4 shGreen;
float4 shBlue;
float4 skypara;
float4 skyparb;
float4 sunDir;
float4 sunClr;
float4 fxclr;
float4 StencilId;

Texture2D ss0;
Texture2D ss1;
SamplerState ss0Sampler;
SamplerState ss1Sampler;

struct VertexOutput
{
    float4 TextureCoordinates : TEXCOORD0;
    float3 ViewDirection : TEXCOORD1;
    float4 Position : SV_POSITION;
};

VertexOutput MainVertex(float4 position : POSITION0, float2 nightTextureCoordinate : TEXCOORD0, float2 dayTextureCoordinate : TEXCOORD1)
{
    VertexOutput output;
    output.TextureCoordinates = float4(nightTextureCoordinate, dayTextureCoordinate);
    output.ViewDirection = float3(dot(position.xyz, World[0].xyz), dot(position.xyz, World[1].xyz), dot(position.xyz, World[2].xyz));
    float4 clip = float4(dot(position, WorldViewProjection[0]), dot(position, WorldViewProjection[1]), dot(position, WorldViewProjection[2]), dot(position, WorldViewProjection[3]));
    output.Position = clip.xyww;
    return output;
}

float4 CardPixel(VertexOutput input) : SV_TARGET
{
    float3 direction = normalize(input.ViewDirection);
    float horizon = 1.0 - direction.z;
    horizon = sunDir.z * (1.0 - horizon) + horizon;
    float sunFacing = dot(sunDir.xyz, direction) * 0.25 + 0.75;
    float scatter = horizon * sunFacing * sunFacing;
    float4 shDirection = float4(-direction, 1.0);
    float3 ambient = float3(dot(shDirection, shRed), dot(shDirection, shGreen), dot(shDirection, shBlue));
    float3 lit = lerp(ambient, sunClr.rgb, scatter);

    float4 day = ss0.Sample(ss0Sampler, input.TextureCoordinates.zw);
    float4 night = ss1.Sample(ss1Sampler, input.TextureCoordinates.xy);
    float4 texel = lerp(night, day, TimeOfDay.x);
    float3 bright = texel.rgb;

    float3 multiplied = lit * bright;
    float3 screened = bright + lit - 2.0 * multiplied;
    float luminance = dot(bright, float3(0.3, 0.59, 0.11));
    float3 blended = luminance * luminance * screened + multiplied;
    float3 halfway = (blended - bright) * 0.5 + bright;
    float3 card = lerp(halfway, blended, scatter);
    card = lerp(bright, card, sunClr.w);

    float hazeHeight = saturate((saturate(-direction.z) - skypara.x) / skypara.y);
    float haze = saturate((1.0 - (3.0 - 2.0 * hazeHeight) * hazeHeight * hazeHeight) * skypara.z + skypara.w);
    card = lerp(card, FogColorFar.rgb, haze) * skyparb.y;
    return float4(card, texel.a * skyparb.x);
}

float4 AdditiveCardPixel(VertexOutput input) : SV_TARGET
{
    float4 card = CardPixel(input);
    return card.a * card * fxclr;
}

float4 LightRaySourceCardPixel(VertexOutput input) : SV_TARGET
{
    float4 card = CardPixel(input);
    if (StencilId.x <= 0.0 && card.a < 0.5) discard;
    if (StencilId.x > 0.0 && dot(card.a * card.rgb, float3(0.33334, 0.33334, 0.33334)) < 0.6) discard;
    return StencilId.xxxx;
}

float4 FullScreenVertex(float4 position : POSITION0) : SV_POSITION
{
    return float4(position.xy, 0.0, 1.0);
}

float4 NoLightRaySourcePixel() : SV_TARGET
{
    return float4(0.0, 0.0, 0.0, 0.0);
}
