#pragma program SceneDepth vertex=MainVertex pixel=MainPixel
#pragma program InstancedSceneDepth vertex=InstancedVertex pixel=MainPixel
#pragma program AlphaTestedSceneDepth vertex=TexturedVertex pixel=AlphaTestedPixel
#pragma program InstancedAlphaTestedSceneDepth vertex=InstancedTexturedVertex pixel=AlphaTestedPixel

#define REGISTER_WORLD 16
#define ALPHA_TEST_REFERENCE 0.5
#define ALPHA_TEST_SCALE 2.0

float4 Registers[95];
float4x4 ViewProjection;
float SlopeScaledDepthBias;
Texture2D AlphaMask : register(t0);
SamplerState AlphaMaskSampler : register(s0);

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 ClipPosition : TEXCOORD0;
    float2 TextureCoordinate : TEXCOORD1;
};

VertexOutput Project(float3 world, float2 textureCoordinate)
{
    VertexOutput output;
    output.Position = mul(float4(world, 1.0), ViewProjection);
    output.ClipPosition = output.Position;
    output.TextureCoordinate = textureCoordinate;
    return output;
}

float3 RegisterWorld(float4 local)
{
    return float3(dot(Registers[REGISTER_WORLD + 0], local), dot(Registers[REGISTER_WORLD + 1], local), dot(Registers[REGISTER_WORLD + 2], local));
}

VertexOutput MainVertex(float3 position : POSITION0)
{
    return Project(RegisterWorld(float4(position, 1.0)), 0.0);
}

VertexOutput TexturedVertex(float3 position : POSITION0, float2 textureCoordinate : TEXCOORD0)
{
    return Project(RegisterWorld(float4(position, 1.0)), textureCoordinate);
}

VertexOutput InstancedVertex(float3 position : POSITION0, float4 rowX : TEXCOORD7, float4 rowY : TEXCOORD6, float4 rowZ : TEXCOORD5)
{
    float4 local = float4(position, 1.0);
    return Project(float3(dot(rowX, local), dot(rowY, local), dot(rowZ, local)), 0.0);
}

VertexOutput InstancedTexturedVertex(float3 position : POSITION0, float2 textureCoordinate : TEXCOORD0, float4 rowX : TEXCOORD7, float4 rowY : TEXCOORD6, float4 rowZ : TEXCOORD5)
{
    float4 local = float4(position, 1.0);
    return Project(float3(dot(rowX, local), dot(rowY, local), dot(rowZ, local)), textureCoordinate);
}

float4 MainPixel(VertexOutput input) : SV_TARGET
{
    float depth = input.ClipPosition.z / input.ClipPosition.w;
    return depth + SlopeScaledDepthBias * max(abs(ddx(depth)), abs(ddy(depth)));
}

float4 AlphaTestedPixel(VertexOutput input) : SV_TARGET
{
    clip(saturate(AlphaMask.Sample(AlphaMaskSampler, input.TextureCoordinate).a * ALPHA_TEST_SCALE) - ALPHA_TEST_REFERENCE);
    return MainPixel(input);
}
