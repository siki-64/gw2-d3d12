#pragma program Character vertex=MainVertex pixel=MainPixel
#pragma program SkinnedCharacter vertex=SkinnedVertex pixel=MainPixel

#define REGISTER_COUNT 95
#define REGISTER_LIGHT_DIRECTION 5
#define REGISTER_LIGHT_COLOR 6
#define REGISTER_WORLD 16

float4 Registers[REGISTER_COUNT];
float4x4 ViewProjection;
float4 TintMultiply;
float4 TintOffset;
float4 TintContrast;
float TintOpacity;
Texture2D Atlas : register(t0);
SamplerState AtlasSampler;
Texture2D NormalAtlas : register(t1);
SamplerState NormalAtlasSampler;
StructuredBuffer<float4> PaletteColumns : register(t2);
uint PaletteOffset;
uint PaletteBoneCount;

struct VertexInput
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float3 Tangent : TANGENT0;
    float3 Bitangent : BITANGENT0;
    float2 TextureCoordinate : TEXCOORD0;
};

struct SkinnedVertexInput
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float3 Tangent : TANGENT0;
    float3 Bitangent : BITANGENT0;
    float2 TextureCoordinate : TEXCOORD0;
    int4 Bones : BLENDINDICES0;
    float4 Weights : BLENDWEIGHT0;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float3 Normal : TEXCOORD0;
    float2 TextureCoordinate : TEXCOORD1;
    float3 Tangent : TEXCOORD2;
    float3 Bitangent : TEXCOORD3;
};

float3 TransformByWorld(float4 value)
{
    return float3(
        dot(Registers[REGISTER_WORLD + 0], value),
        dot(Registers[REGISTER_WORLD + 1], value),
        dot(Registers[REGISTER_WORLD + 2], value));
}

VertexOutput MainVertex(VertexInput input)
{
    VertexOutput output;
    float3 worldPosition = TransformByWorld(float4(input.Position, 1.0));
    output.Position = mul(float4(worldPosition, 1.0), ViewProjection);
    output.Normal = TransformByWorld(float4(input.Normal, 0.0));
    output.TextureCoordinate = input.TextureCoordinate;
    output.Tangent = TransformByWorld(float4(input.Tangent, 0.0));
    output.Bitangent = TransformByWorld(float4(input.Bitangent, 0.0));
    return output;
}

float3 TransformByBone(uint first, float4 value)
{
    return float3(dot(PaletteColumns[first], value), dot(PaletteColumns[first + 1], value), dot(PaletteColumns[first + 2], value));
}

VertexOutput SkinnedVertex(SkinnedVertexInput input)
{
    float3 position = 0;
    float3 normal = 0;
    float3 tangent = 0;
    float3 bitangent = 0;
    [unroll]
    for (int slot = 0; slot < 4; slot++)
    {
        int bone = input.Bones[slot];
        float weight = input.Weights[slot];
        if (weight <= 0.0 || bone < 0 || (uint)bone >= PaletteBoneCount) continue;
        uint first = (PaletteOffset + (uint)bone) * 3;
        position += weight * TransformByBone(first, float4(input.Position, 1.0));
        normal += weight * TransformByBone(first, float4(input.Normal, 0.0));
        tangent += weight * TransformByBone(first, float4(input.Tangent, 0.0));
        bitangent += weight * TransformByBone(first, float4(input.Bitangent, 0.0));
    }
    VertexOutput output;
    float3 worldPosition = TransformByWorld(float4(position, 1.0));
    output.Position = mul(float4(worldPosition, 1.0), ViewProjection);
    output.Normal = TransformByWorld(float4(normal, 0.0));
    output.TextureCoordinate = input.TextureCoordinate;
    output.Tangent = TransformByWorld(float4(tangent, 0.0));
    output.Bitangent = TransformByWorld(float4(bitangent, 0.0));
    return output;
}

float4 MainPixel(VertexOutput input) : SV_TARGET
{
    float3 lightDirection = normalize(Registers[REGISTER_LIGHT_DIRECTION].xyz);
    float3 normal = normalize(input.Normal);
    float3 tangent = normalize(input.Tangent);
    float3 bitangent = normalize(input.Bitangent);
    float2 tangentSpaceXY = NormalAtlas.Sample(NormalAtlasSampler, input.TextureCoordinate).rg * 2.0 - 1.0;
    float tangentSpaceZ = sqrt(saturate(1.0 - dot(tangentSpaceXY, tangentSpaceXY)));
    float3 shadingNormal = normalize(tangent * tangentSpaceXY.x + bitangent * tangentSpaceXY.y + normal * tangentSpaceZ);
    float diffuse = saturate(dot(shadingNormal, -lightDirection));
    float3 albedo = Atlas.Sample(AtlasSampler, input.TextureCoordinate).rgb;
    float3 color = albedo * Registers[REGISTER_LIGHT_COLOR].rgb * (0.25 + 0.75 * diffuse);
    static const float4x4 OrderedDitherThresholds =
    {
        0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
        12.0 / 16.0, 4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
        3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
        15.0 / 16.0, 7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
    };
    uint2 ditherCell = uint2(input.Position.xy) % 4;
    clip(TintOpacity - OrderedDitherThresholds[ditherCell.y][ditherCell.x] - 1.0 / 32.0);
    color = saturate(color * TintMultiply.rgb + TintOffset.rgb);
    color = saturate((color - 0.5) * TintContrast.rgb * 2.0 + 0.5);
    return float4(color, 1.0);
}
