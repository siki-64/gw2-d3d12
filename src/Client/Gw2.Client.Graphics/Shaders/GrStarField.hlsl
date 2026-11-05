#pragma program StarField vertex=MainVertex pixel=StarPixel

float4 Time;
float4 World[4];
float4 WorldViewProjection[4];

float4 skypara;

Texture2D ss00;
SamplerState ss00Sampler;

struct VertexOutput
{
    float2 TextureCoordinate : TEXCOORD0;
    float4 DirectionAndTwinkle : TEXCOORD1;
    float4 Position : SV_POSITION;
};

float2 TriangleWave(float2 value)
{
    return frac(value) * 2.0 - 1.0;
}

VertexOutput MainVertex(float4 position : POSITION0, float4 seed : COLOR0, float2 textureCoordinate : TEXCOORD0)
{
    VertexOutput output;
    output.TextureCoordinate = textureCoordinate;
    float2 phase = Time.xz * seed.w + dot(seed.rgb, float3(1.0, 1.0, 1.0));
    float2 wave = TriangleWave(TriangleWave(phase) * 2.223);
    float2 eased = wave * wave * (3.0 - 2.0 * abs(wave));
    float3 direction = float3(dot(position.xyz, World[0].xyz), dot(position.xyz, World[1].xyz), dot(position.xyz, World[2].xyz));
    output.DirectionAndTwinkle = float4(normalize(direction), eased.x * eased.y);
    float4 clip = float4(dot(position, WorldViewProjection[0]), dot(position, WorldViewProjection[1]), dot(position, WorldViewProjection[2]), dot(position, WorldViewProjection[3]));
    output.Position = clip.xyww;
    return output;
}

float4 StarPixel(VertexOutput input) : SV_TARGET
{
    float hazeHeight = saturate((abs(input.DirectionAndTwinkle.z) - skypara.x) / skypara.y);
    float haze = 1.0 - (1.0 - hazeHeight * hazeHeight * (3.0 - 2.0 * hazeHeight)) * skypara.z;
    float4 texel = ss00.Sample(ss00Sampler, input.TextureCoordinate);
    float3 color = 2.0 * texel.rgb * texel.rgb;
    float sparkle = texel.a * dot(color, float3(1.0, 1.0, 1.0)) * input.DirectionAndTwinkle.w;
    float4 star = float4(color + sparkle * 0.666667, texel.a);
    return star * haze * skypara.w;
}
