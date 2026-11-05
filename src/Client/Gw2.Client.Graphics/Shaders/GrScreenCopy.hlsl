#pragma program Copy vertex=MainVertex pixel=CopyPixel
#pragma program GammaPresent vertex=MainVertex pixel=GammaPresentPixel
#pragma program LinearDepth vertex=MainVertex pixel=LinearDepthPixel

Texture2D Source;
float4 DepthToLinearDepth;
float4 Gamma;
SamplerState SourceSampler;

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 TextureCoordinate : TEXCOORD0;
};

VertexOutput MainVertex(float3 position : POSITION0)
{
    VertexOutput output;
    output.Position = float4(position, 1.0);
    output.TextureCoordinate = position.xy * float2(0.5, -0.5) + 0.5;
    return output;
}

float4 CopyPixel(VertexOutput input) : SV_TARGET
{
    return Source.SampleLevel(SourceSampler, input.TextureCoordinate, 0);
}

float4 GammaPresentPixel(VertexOutput input) : SV_TARGET
{
    return Source.SampleLevel(SourceSampler, input.TextureCoordinate, 0) * Gamma.x;
}

float4 LinearDepthPixel(VertexOutput input) : SV_TARGET
{
    float ndcDepth = Source.SampleLevel(SourceSampler, input.TextureCoordinate, 0).x;
    return DepthToLinearDepth.x / (ndcDepth + DepthToLinearDepth.y) * DepthToLinearDepth.z;
}
