#pragma program NameLabel vertex=SpriteVS pixel=NameLabelPS

cbuffer SpriteTransform
{
    float4x4 MatrixTransform;
};

cbuffer NameLabelDepth : register(b1)
{
    float4 SceneDepthScale;
};

Texture2D Texture;
SamplerState TextureSampler;
Texture2D<float> SceneDepth;

struct SpriteVertex
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TextureCoordinate : TEXCOORD0;
};

SpriteVertex SpriteVS(float3 position : POSITION, float4 color : COLOR0, float2 textureCoordinate : TEXCOORD0)
{
    SpriteVertex output;
    output.Position = mul(float4(position, 1.0), MatrixTransform);
    output.Color = color;
    output.TextureCoordinate = textureCoordinate;
    return output;
}

float4 NameLabelPS(SpriteVertex input) : SV_Target
{
    float sceneDepth = SceneDepth.Load(int3(input.Position.xy * SceneDepthScale.xy, 0));
    if (input.Position.z > sceneDepth) discard;
    return Texture.Sample(TextureSampler, input.TextureCoordinate) * input.Color;
}
