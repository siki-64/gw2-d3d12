#pragma program Cloud vertex=MainVertex pixel=CloudPixel

float4 CameraPosition;
float4 FogColorNearMinusFar;
float4 FogColorFar;
float4 FogParam0;
float4 World[4];
float4 WorldView[4];
float4 WorldViewProjection[4];
float4 uvTrans;
float4 cldPara;
float4 cldParb;

float4 ScreenDims;
float4 TexelOffset;
float4 shRed;
float4 shGreen;
float4 shBlue;
float4 sunDir;
float4 sunClr;

Texture2D ss0;
Texture2D ssNoiseDepth;
SamplerState ss0Sampler;
SamplerState ssNoiseDepthSampler;

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 TextureCoordinateDepthHeightFade : TEXCOORD0;
    float3 ViewDirection : TEXCOORD1;
    float2 EdgeFadeFog : TEXCOORD2;
};

VertexOutput MainVertex(float4 position : POSITION0, float4 color : COLOR0, float2 textureCoordinate : TEXCOORD0)
{
    VertexOutput output;
    float3 world = float3(dot(position, World[0]), dot(position, World[1]), dot(position, World[2]));
    float3 toCamera = CameraPosition.xyz - world;
    float heightFade = saturate((abs(toCamera.z) - cldParb.x) / cldParb.y);
    float4 clip = float4(dot(position, WorldViewProjection[0]), dot(position, WorldViewProjection[1]), dot(position, WorldViewProjection[2]), dot(position, WorldViewProjection[3]));
    float fog = saturate(dot(position, WorldView[2]) * FogParam0.x + FogParam0.y);
    fog = min((1.0 - fog) * FogColorNearMinusFar.w + FogColorFar.w, fog) * cldPara.z;
    output.Position = clip;
    output.TextureCoordinateDepthHeightFade = float4((textureCoordinate + uvTrans.xy) * uvTrans.z, clip.w, heightFade * heightFade);
    output.ViewDirection = toCamera;
    output.EdgeFadeFog = float2(color.x, fog);
    return output;
}

float LinearSceneDepth(float2 pixel)
{
    float2 screen = (pixel + TexelOffset.x) * ScreenDims.xy;
    float depth = ssNoiseDepth.Sample(ssNoiseDepthSampler, screen).r;
    return ScreenDims.z * ScreenDims.w / (ScreenDims.w - depth * (ScreenDims.w - ScreenDims.z));
}

float4 CloudPixel(VertexOutput input) : SV_TARGET
{
    float3 direction = normalize(input.ViewDirection);
    float horizon = 1.0 - direction.z;
    horizon = sunDir.z * (1.0 - horizon) + horizon;
    float sunFacing = dot(sunDir.xyz, direction) * 0.25 + 0.75;
    float scatter = horizon * sunFacing * sunFacing;
    float4 shDirection = float4(-direction, 1.0);
    float3 ambient = float3(dot(shDirection, shRed), dot(shDirection, shGreen), dot(shDirection, shBlue));
    float3 lit = lerp(ambient, sunClr.rgb, scatter);

    float2 textureCoordinate = input.TextureCoordinateDepthHeightFade.xy;
    float height = ss0.Sample(ss0Sampler, textureCoordinate).a - 0.5;
    float4 texel = ss0.Sample(ss0Sampler, textureCoordinate + height * cldPara.x * direction.xy);
    float3 bright = texel.rgb;

    float3 multiplied = lit * bright;
    float3 screened = bright + lit - 2.0 * multiplied;
    float luminance = dot(bright, float3(0.3, 0.59, 0.11));
    float3 blended = luminance * luminance * screened + multiplied;
    float3 halfway = (blended - bright) * 0.5 + bright;
    float3 cloud = lerp(halfway, blended, scatter);
    cloud = lerp(bright, cloud, sunClr.w);
    cloud = lerp(cloud, FogColorFar.rgb, input.EdgeFadeFog.y) * uvTrans.w;

    float softDepth = saturate((LinearSceneDepth(input.Position.xy) - input.TextureCoordinateDepthHeightFade.z) / cldPara.w);
    softDepth = (3.0 - 2.0 * softDepth) * softDepth * softDepth;
    float alpha = input.EdgeFadeFog.x * cldPara.y * texel.a * softDepth * input.TextureCoordinateDepthHeightFade.w;
    return float4(cloud, alpha);
}
