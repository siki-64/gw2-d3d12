#pragma program Albedo vertex=MeshVertex pixel=AlbedoPixel
#pragma program InstancedAlbedo vertex=InstancedVertex pixel=AlbedoPixel
#pragma program Wire vertex=MeshVertex pixel=WirePixel
#pragma program InstancedWire vertex=InstancedVertex pixel=WirePixel
#pragma program Overdraw vertex=MeshVertex pixel=OverdrawPixel
#pragma program InstancedOverdraw vertex=InstancedVertex pixel=OverdrawPixel
#pragma program Clay vertex=MeshVertex pixel=ClayPixel
#pragma program InstancedClay vertex=InstancedVertex pixel=ClayPixel
#pragma program TexelDensity vertex=MeshVertex pixel=TexelDensityPixel
#pragma program InstancedTexelDensity vertex=InstancedVertex pixel=TexelDensityPixel
#pragma program Lighting vertex=MeshVertex pixel=LightingPixel
#pragma program InstancedLighting vertex=InstancedVertex pixel=LightingPixel

#define REGISTER_WORLD 16
#define OVERDRAW_LAYER 0.05
#define CLAY_CHECKER_SCALE 0.025
#define CLAY_CHECKER_DEPTH_STEP 0.0002
#define TEXEL_DENSITY_GRID_TEXELS 8.0
#define TEXEL_DENSITY_GRID_GAP 0.1

float4 Registers[95];
float4x4 ViewProjection;
float4x4 View;
float4 ModelColor;
float2 TextureSize;
float4 SunDirection;
float4 WireColor;
float4 LightBuffer;
float4 ScreenDims;
float4 TexelOffset;
Texture2D Albedo : register(t0);
SamplerState AlbedoSampler : register(s0);
Texture2D LightBufferTexture : register(t1);
SamplerState LightBufferSampler : register(s1);

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 TextureCoordinate : TEXCOORD0;
    float3 WorldPosition : TEXCOORD1;
    float3 WorldNormal : TEXCOORD2;
    float ViewDepth : TEXCOORD3;
};

VertexOutput Project(float4 local, float3 normal, float4 rowX, float4 rowY, float4 rowZ, float2 textureCoordinate)
{
    VertexOutput output;
    float3 world = float3(dot(rowX, local), dot(rowY, local), dot(rowZ, local));
    output.Position = mul(float4(world, 1.0), ViewProjection);
    output.TextureCoordinate = textureCoordinate;
    output.WorldPosition = world;
    output.WorldNormal = normalize(float3(dot(rowX.xyz, normal), dot(rowY.xyz, normal), dot(rowZ.xyz, normal)));
    output.ViewDepth = abs(mul(float4(world, 1.0), View).z);
    return output;
}

VertexOutput MeshVertex(float3 position : POSITION0, float3 normal : NORMAL0, float2 textureCoordinate : TEXCOORD0)
{
    return Project(float4(position, 1.0), normal, Registers[REGISTER_WORLD + 0], Registers[REGISTER_WORLD + 1], Registers[REGISTER_WORLD + 2], textureCoordinate);
}

VertexOutput InstancedVertex(float3 position : POSITION0, float3 normal : NORMAL0, float2 textureCoordinate : TEXCOORD0, float4 rowX : TEXCOORD7, float4 rowY : TEXCOORD6, float4 rowZ : TEXCOORD5)
{
    return Project(float4(position, 1.0), normal, rowX, rowY, rowZ, textureCoordinate);
}

float4 AlbedoPixel(VertexOutput input) : SV_TARGET
{
    return float4(Albedo.Sample(AlbedoSampler, input.TextureCoordinate).rgb, 1.0);
}

float4 WirePixel(VertexOutput input) : SV_TARGET
{
    return WireColor;
}

float4 OverdrawPixel(VertexOutput input) : SV_TARGET
{
    return OVERDRAW_LAYER;
}

float ClayChecker(VertexOutput input)
{
    float2 cell = input.WorldPosition.xy * (CLAY_CHECKER_SCALE / exp2(floor(input.ViewDepth * CLAY_CHECKER_DEPTH_STEP)));
    float2 parity = floor(cell - floor(cell * 0.5) * 2.0);
    return max(1.0 - abs(parity.x + parity.y - 1.0), 0.0) * 0.02 + 0.48;
}

float4 ClayPixel(VertexOutput input) : SV_TARGET
{
    float halfLambert = (dot(normalize(SunDirection.xyz), normalize(input.WorldNormal)) + 1.0) * 0.5;
    return float4(ModelColor.rgb * ClayChecker(input) * halfLambert, 1.0);
}

float4 LightingPixel(VertexOutput input) : SV_TARGET
{
    float2 screenUv = (input.Position.xy + TexelOffset.x) * ScreenDims.xy;
    float3 light = LightBufferTexture.Sample(LightBufferSampler, screenUv).rgb;
    return float4(light * LightBuffer.y * ClayChecker(input), 1.0);
}

float4 TexelDensityPixel(VertexOutput input) : SV_TARGET
{
    float2 fine = input.TextureCoordinate * TextureSize;
    float2 grid = frac(fine / TEXEL_DENSITY_GRID_TEXELS);
    float inCell = (grid.x >= TEXEL_DENSITY_GRID_GAP ? 1.0 : 0.0) * (grid.y >= TEXEL_DENSITY_GRID_GAP ? 1.0 : 0.0);
    float texelsPerPixel = max(max(abs(ddx_coarse(fine).x), abs(ddx_coarse(fine).y)), max(abs(ddy_coarse(fine).x), abs(ddy_coarse(fine).y)));
    float2 ramp = saturate((texelsPerPixel - float2(1.0, 0.5)) * 2.0);
    float tooDense = ramp.x * ramp.x * (3.0 - 2.0 * ramp.x);
    float sparse = 1.0 - ramp.y * ramp.y * (3.0 - 2.0 * ramp.y);
    float4 color = tooDense * float4(0.0, -1.0, 1.0, 0.0) + float4(0.0, 1.0, 0.0, 1.0);
    color += sparse * float4(1.0, -1.0, 0.0, 0.0);
    return inCell * color;
}
