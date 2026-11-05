#pragma program AboveWater vertex=AboveWaterVertex pixel=AboveWaterPixel
#pragma program BelowWater vertex=BelowWaterVertex pixel=BelowWaterPixel

float4 CameraPosition;
float4 FogDepthCue;
float4 FogParam0;
float4 World[4];
float4 WorldViewProjection[4];
float4 wtrprmv;
float4 wtrprmd;
float4 wtrprme;
float4 FogColorNearMinusFar;
float4 FogColorFar;
float4 FogColorHeight;
float4 ScreenDims;
float4 BlurryWater;
float4 wtrprmp;
float4 wtrclrs;
float4 wtrclrd;
float4 wtrclrp;
float4 FresnelTerminator;

Texture2D ssReflect : register(t0);
Texture2D ssBump : register(t1);
Texture2D ssPattern : register(t2);
Texture2D ssDepth : register(t12);
TextureCube ssEnvMap : register(t13);
Texture2D ssScreen : register(t14);
SamplerState ssReflectSampler : register(s0);
SamplerState ssBumpSampler : register(s1);
SamplerState ssPatternSampler : register(s2);
SamplerState ssDepthSampler : register(s12);
SamplerState ssEnvMapSampler : register(s13);
SamplerState ssScreenSampler : register(s14);

float AwayFromZero(float value)
{
    return abs(value) > 1e-37 ? value : (sign(value) != 0.0 ? sign(value) * 1e-37 : 1e-37);
}
struct AboveWaterVertexInput
{
    float3 POSITION0 : POSITION0;
    float3 NORMAL0 : NORMAL0;
    float4 COLOR0 : COLOR0;
};

struct AboveWaterVertexOutput
{
    float3 COLOR0 : COLOR0;
    float4 TEXCOORD0 : TEXCOORD0;
    float3 TEXCOORD1 : TEXCOORD1;
    float4 TEXCOORD2 : TEXCOORD2;
    float3 TEXCOORD3 : TEXCOORD3;
    float3 TEXCOORD4 : TEXCOORD4;
    float3 TEXCOORD5 : TEXCOORD5;
    float4 TEXCOORD6 : TEXCOORD6;
    float2 TEXCOORD7 : TEXCOORD7;
    float4 POSITION0 : SV_POSITION;
};

AboveWaterVertexOutput AboveWaterVertex(AboveWaterVertexInput input)
{
    AboveWaterVertexOutput output = (AboveWaterVertexOutput)0;
    float4 value0 = 0;
    float3 value1 = 0;
    float3 value2 = 0;
    float3 value3 = 0;
    float3 value4 = 0;
    float4 value5 = 0;
    float2 value6 = 0;
    float4 v0 = 0;
    float4 v1 = 0;
    float4 v2 = 0;
    float4 o0 = 0;
    float4 o1 = 0;
    float4 o2 = 0;
    float4 o3 = 0;
    float4 o4 = 0;
    float4 o5 = 0;
    float4 o6 = 0;
    float4 o7 = 0;
    float4 o8 = 0;
    float4 o9 = 0;
    v0.xyz = input.POSITION0;
    v1.xyz = input.NORMAL0;
    v2.xyzw = input.COLOR0;
    value0.yzw = float3(dot(v0.xyz, World[0].xyz), dot(v0.xyz, World[1].xyz), dot(v0.xyz, World[2].xyz));
    value1 = value0.yzw + -CameraPosition.xyz;
    o3.xyz = -value0.yzw + CameraPosition.xyz;
    value0.x = sqrt(dot(value1, value1));
    o0.xy = value0.xw * FogParam0.xz + FogParam0.yw;
    o0.z = min((value0.x * FogDepthCue.x), 1.0);
    o1.xyzw = v0.xyxy * wtrprmv.xxyy + wtrprmd.xyzw;
    o2.xy = v0.xy * wtrprme.xx + wtrprmv.zw;
    o2.z = 0.0;
    o3.w = v2.w * v2.w;
    value2 = normalize(v1.xyz);
    value3 = value2.zxy * float3(0.0, 0.0, 1.0) + (-(value2.yzx * float3(0.0, 1.0, 0.0)));
    value4 = rsqrt(dot(value3.yz, value3.yz)).xxx * value3;
    o4.xyz = value2.yzx * value4.zxy + (-(value2.zxy * value4.yzx));
    o5.xyz = value4;
    o6.xyz = value2;
    o7.xyzw = float4(0.0, 0.0, 0.0, 0.0);
    value5.xyz = v0.xyz;
    value5.w = 1.0;
    value6.y = dot(value5, WorldViewProjection[3]);
    o8.y = 1.0 / value6.y;
    value6.x = dot(value5, WorldViewProjection[2]);
    o8.x = value6.x;
    o9.zw = value6;
    o9.xy = float2(dot(value5, WorldViewProjection[0]), dot(value5, WorldViewProjection[1]));
    output.COLOR0 = o0.xyz;
    output.TEXCOORD0 = o1.xyzw;
    output.TEXCOORD1 = o2.xyz;
    output.TEXCOORD2 = o3.xyzw;
    output.TEXCOORD3 = o4.xyz;
    output.TEXCOORD4 = o5.xyz;
    output.TEXCOORD5 = o6.xyz;
    output.TEXCOORD6 = o7.xyzw;
    output.TEXCOORD7 = o8.xy;
    output.POSITION0 = o9.xyzw;
    return output;
}

struct AboveWaterPixelInput
{
    float3 COLOR0 : COLOR0;
    float4 TEXCOORD0 : TEXCOORD0;
    float3 TEXCOORD1 : TEXCOORD1;
    float4 TEXCOORD2 : TEXCOORD2;
    float3 TEXCOORD3 : TEXCOORD3;
    float3 TEXCOORD4 : TEXCOORD4;
    float3 TEXCOORD5 : TEXCOORD5;
    float4 TEXCOORD6 : TEXCOORD6;
    float2 TEXCOORD7 : TEXCOORD7;
    float4 POSITION0 : SV_POSITION;
};

struct AboveWaterPixelOutput
{
    float4 TARGET0 : SV_TARGET;
};

AboveWaterPixelOutput AboveWaterPixel(AboveWaterPixelInput input)
{
    AboveWaterPixelOutput output = (AboveWaterPixelOutput)0;
    float3 value0 = 0;
    float3 value1 = 0;
    float4 value2 = 0;
    float4 value3 = 0;
    float3 value4 = 0;
    float3 value5 = 0;
    float value6 = 0;
    float4 ssEnvMapSample0 = 0;
    float3 value7 = 0;
    float2 value8 = 0;
    float3 value9 = 0;
    float2 value10 = 0;
    float2 value11 = 0;
    float3 ssReflectSample0 = 0;
    float3 value12 = 0;
    float4 value13 = 0;
    float3 value14 = 0;
    float ssDepthSample0 = 0;
    float3 ssScreenSample0 = 0;
    float3 value15 = 0;
    float value16 = 0;
    float3 value17 = 0;
    float value18 = 0;
    float3 value19 = 0;
    float4 value20 = 0;
    float4 ssPatternSample0 = 0;
    float4 value21 = 0;
    float2 value22 = 0;
    float value23 = 0;
    float4 value24 = 0;
    float4 value25 = 0;
    float4 value26 = 0;
    float4 value27 = 0;
    float4 v0 = 0;
    float4 v1 = 0;
    float4 v2 = 0;
    float4 v3 = 0;
    float4 v4 = 0;
    float4 v5 = 0;
    float4 v6 = 0;
    float4 v7 = 0;
    float4 v8 = 0;
    float4 v9 = 0;
    float4 o0 = 0;
    v0.xyz = input.COLOR0;
    v1.xyzw = input.TEXCOORD0;
    v2.xyz = input.TEXCOORD1;
    v3.xyzw = input.TEXCOORD2;
    v4.xyz = input.TEXCOORD3;
    v5.xyz = input.TEXCOORD4;
    v6.xyz = input.TEXCOORD5;
    v7.xyzw = input.TEXCOORD6;
    v8.xy = input.TEXCOORD7;
    v9.xyzw = input.POSITION0;
    value0 = ssBump.Sample(ssBumpSampler, v1.xy).xyz * float3(2.0, 2.0, 2.0) + float3(-1.0, -1.0, -1.0);
    value0.xy = value0.xy * wtrprmp.xx;
    value1 = ssBump.Sample(ssBumpSampler, v1.zw).xyz * float3(2.0, 2.0, 2.0) + float3(-1.0, -1.0, -1.0);
    value1.xy = value1.xy * wtrprmp.yy;
    value2.xyz = value0 + value1;
    value3.xyz = normalize((value2.zzz * v6.xyz + (value2.xxx * v4.xyz + (value2.yyy * v5.xyz))));
    value4 = normalize(v3.xyz);
    value5 = reflect(-value4, value3.xyz);
    value6 = dot(value3.xyz, value4);
    ssEnvMapSample0 = ssEnvMap.SampleLevel(ssEnvMapSampler, value5, BlurryWater.z).xyzw;
    value7 = ssEnvMapSample0.xyz / ssEnvMapSample0.www;
    value8 = v9.xy + float2(0.5, 0.5);
    value9.z = -value8.y * ScreenDims.y + 1.0;
    value9.xy = value8 * ScreenDims.xy;
    value10 = -value3.xy * wtrprmp.zz + value9.xz;
    value11 = (value3.xy * wtrprmp.zz) * v3.ww + value9.xy;
    ssReflectSample0 = ssReflect.Sample(ssReflectSampler, value10).xyz;
    value12 = value7 * value7 + -ssReflectSample0;
    value13.w = min(pow(AwayFromZero(((-min(abs(value6), 1.0)) + 1.0)), wtrprmp.w), 1.0);
    value14 = lerp((((-value13.w + 1.0) * BlurryWater.x).xxx * value12 + ssReflectSample0), value7 * value7, (value13.w * BlurryWater.y).xxx);
    ssDepthSample0 = ssDepth.Sample(ssDepthSampler, value11).x;
    ssScreenSample0 = ssScreen.Sample(ssScreenSampler, select(((ScreenDims.z / ((-(v8.y * v8.x)) * (-ScreenDims.z + ScreenDims.w) + ScreenDims.w)) < ssDepthSample0).xx, value11, value9.xy)).xyz;
    value15 = lerp(wtrclrs.xyz, wtrclrd.xyz, value6.xxx);
    value16 = -v3.w + 1.0;
    value17 = v3.www * value15 + value16.xxx;
    value18 = wtrclrs.w * value16 + v3.w;
    value19 = -ssScreenSample0 * value17 + value14;
    value20.xyz = value13.www * value19 + (value17 * ssScreenSample0);
    ssPatternSample0 = ssPattern.Sample(ssPatternSampler, v2.xy).xyzw;
    value21.xyz = lerp(value20.xyz, ssPatternSample0.xyz * wtrclrp.xyz, (value18 * (ssPatternSample0.w * wtrclrp.w)).xxx);
    value22 = (-min(exp2(-v0.xy), float2(1.0, 1.0))) + float2(1.0, 1.0);
    value23 = -value22.x + 1.0;
    value24 = value23.xxxx * FogColorNearMinusFar.xyzw + FogColorFar.xyzw;
    value25.w = min(value22.x, value24.w);
    value26.w = min((value22.y * v0.z), FogColorHeight.w);
    value25.xyz = value24.xyz * value25.www;
    value26.xyz = value26.www * FogColorHeight.xyz;
    value27 = value26 * value23.xxxx + value25;
    o0.xyz = value21.xyz * (-value27.w + 1.0).xxx + value27.xyz;
    o0.w = 1.0;
    output.TARGET0 = o0.xyzw;
    return output;
}

struct BelowWaterVertexInput
{
    float4 POSITION0 : POSITION0;
    float3 NORMAL0 : NORMAL0;
    float4 COLOR0 : COLOR0;
};

struct BelowWaterVertexOutput
{
    float4 TEXCOORD0 : TEXCOORD0;
    float2 TEXCOORD1 : TEXCOORD1;
    float2 TEXCOORD2 : TEXCOORD2;
    float3 TEXCOORD3 : TEXCOORD3;
    float3 TEXCOORD4 : TEXCOORD4;
    float4 POSITION0 : SV_POSITION;
};

BelowWaterVertexOutput BelowWaterVertex(BelowWaterVertexInput input)
{
    BelowWaterVertexOutput output = (BelowWaterVertexOutput)0;
    float2 value0 = 0;
    float4 v0 = 0;
    float4 v1 = 0;
    float4 v2 = 0;
    float4 o0 = 0;
    float4 o1 = 0;
    float4 o2 = 0;
    float4 o3 = 0;
    float4 o4 = 0;
    v0.xyzw = input.POSITION0;
    v1.xyz = input.NORMAL0;
    v2.xyzw = input.COLOR0;
    o0.xyzw = v0.xyxy * wtrprmv.xxyy + wtrprmd.xyzw;
    o1.xy = v0.xy * wtrprme.xx + wtrprmv.zw;
    value0 = float2(dot(v0.xyzw, WorldViewProjection[2]), dot(v0.xyzw, WorldViewProjection[3]));
    o1.zw = value0;
    o4.zw = value0;
    o2.xyz = float3(dot(v0.xyzw, World[0]), dot(v0.xyzw, World[1]), dot(v0.xyzw, World[2]));
    o3.xyz = v1.xyz;
    o4.xy = float2(dot(v0.xyzw, WorldViewProjection[0]), dot(v0.xyzw, WorldViewProjection[1]));
    output.TEXCOORD0 = o0.xyzw;
    output.TEXCOORD1 = o1.xy;
    output.TEXCOORD2 = o1.zw;
    output.TEXCOORD3 = o2.xyz;
    output.TEXCOORD4 = o3.xyz;
    output.POSITION0 = o4.xyzw;
    return output;
}

struct BelowWaterPixelInput
{
    float4 TEXCOORD0 : TEXCOORD0;
    float2 TEXCOORD1 : TEXCOORD1;
    float2 TEXCOORD2 : TEXCOORD2;
    float3 TEXCOORD3 : TEXCOORD3;
    float3 TEXCOORD4 : TEXCOORD4;
    float4 POSITION0 : SV_POSITION;
};

struct BelowWaterPixelOutput
{
    float4 TARGET0 : SV_TARGET;
};

BelowWaterPixelOutput BelowWaterPixel(BelowWaterPixelInput input)
{
    BelowWaterPixelOutput output = (BelowWaterPixelOutput)0;
    float value0 = 0;
    float3 value1 = 0;
    float3 value2 = 0;
    float3 value3 = 0;
    float2 value4 = 0;
    float4 value5 = 0;
    float4 value6 = 0;
    float4 value7 = 0;
    float4 value8 = 0;
    float4 value9 = 0;
    float4 ssDepthSample0 = 0;
    bool4 condition0 = 0;
    uint2 value10 = 0;
    uint value11 = 0;
    float4 value12 = 0;
    float3 ssScreenSample0 = 0;
    float3 value13 = 0;
    float3 value14 = 0;
    float3 value15 = 0;
    float3 value16 = 0;
    float3 value17 = 0;
    float3 value18 = 0;
    float4 value19 = 0;
    float3 value20 = 0;
    float value21 = 0;
    float3 value22 = 0;
    float4 value23 = 0;
    float4 value24 = 0;
    float4 ssPatternSample0 = 0;
    float4 value25 = 0;
    float3 value26 = 0;
    float2 value27 = 0;
    float value28 = 0;
    float2 value29 = 0;
    float4 value30 = 0;
    float value31 = 0;
    float4 value32 = 0;
    float4 value33 = 0;
    float4 value34 = 0;
    float4 v0 = 0;
    float4 v1 = 0;
    float4 v2 = 0;
    float4 v3 = 0;
    float4 v4 = 0;
    float4 o0 = 0;
    v0.xyzw = input.TEXCOORD0;
    v1.xy = input.TEXCOORD1;
    v1.zw = input.TEXCOORD2;
    v2.xyz = input.TEXCOORD3;
    v3.xyz = input.TEXCOORD4;
    v4.xyzw = input.POSITION0;
    value0 = ScreenDims.z / ((-(v1.z / v1.w)) * (-ScreenDims.z + ScreenDims.w) + ScreenDims.w);
    value1 = ssBump.Sample(ssBumpSampler, v0.xy).xyz * float3(2.0, 2.0, 2.0) + float3(-1.0, -1.0, -1.0);
    value1.xy = value1.xy * wtrprmp.xx;
    value2 = ssBump.Sample(ssBumpSampler, v0.zw).xyz * float3(2.0, 2.0, 2.0) + float3(-1.0, -1.0, -1.0);
    value2.xy = value2.xy * wtrprmp.yy;
    value3 = normalize((-value1 + -value2));
    value4 = ((value3.xy * wtrprmp.zz) / ScreenDims.xy) + v4.xy;
    value5 = floor(value4.xyxy);
    value6.xy = value4 + float2(0.5, 0.5);
    value7 = value5.zwzw + float4(0.5, 0.5, 1.5, 0.5);
    value8 = (value5 + float4(0.5, 1.5, 1.5, 1.5)) * ScreenDims.xyxy;
    value9 = value7 * ScreenDims.xyxy;
    ssDepthSample0.x = ssDepth.Sample(ssDepthSampler, value9.xy).x;
    ssDepthSample0.y = ssDepth.Sample(ssDepthSampler, value9.zw).x;
    ssDepthSample0.z = ssDepth.Sample(ssDepthSampler, value8.xy).x;
    ssDepthSample0.w = ssDepth.Sample(ssDepthSampler, value8.zw).x;
    condition0 = value0.xxxx < ssDepthSample0;
    value10 = (select(condition0.zw, 0xffffffffu, 0u)) & (select(condition0.xy, 0xffffffffu, 0u));
    value11 = value10.y & value10.x;
    value6.zw = v4.xy + float2(0.5, 0.5);
    value12 = value6 * ScreenDims.xyxy;
    ssScreenSample0 = ssScreen.Sample(ssScreenSampler, select((value11.xx != 0u), value12.xy, value12.zw)).xyz;
    value13 = normalize(v3.xyz);
    value14 = value13 * float3(0.0, 1.0, 0.0) + (-(value13.zxy * float3(1.0, 0.0, 0.0)));
    value15 = rsqrt(dot(value14.xy, value14.xy)).xxx * value14;
    value16 = value13.yzx * value15.yzx + (-(value13.zxy * value15));
    value17 = normalize((-v2.xyz + CameraPosition.xyz));
    value18.x = dot(value17, value16);
    value18.y = dot(value17.yz, value15.xy);
    value18.z = dot(value17, value13);
    value19.x = dot(value3, value18);
    value20 = lerp(wtrclrs.xyz, wtrclrd.xyz, value19.xxx);
    value21 = saturate(value19.x + -FresnelTerminator.x);
    value22 = value20 * ssScreenSample0;
    value23.yzw = -ssScreenSample0 * value20 + value20;
    value24.xyz = ((-min(pow(AwayFromZero(value21), (1.0 / wtrprmp.w)), 1.0)) + 1.0).xxx * value23.yzw + value22;
    ssPatternSample0 = ssPattern.Sample(ssPatternSampler, v1.xy).xyzw;
    value25.xyz = lerp(value24.xyz, ssPatternSample0.xyz * wtrclrp.xyz, (ssPatternSample0.w * wtrclrp.w).xxx);
    value26 = v2.xyz + -CameraPosition.xyz;
    value27.x = sqrt(dot(value26, value26));
    value28 = min((value27.x * FogDepthCue.x), 1.0);
    value27.y = v2.z;
    value29 = (-min(exp2((-(value27 * FogParam0.xz + FogParam0.yw))), float2(1.0, 1.0))) + float2(1.0, 1.0);
    value30.w = min((value28 * value29.y), FogColorHeight.w);
    value30.xyz = value30.www * FogColorHeight.xyz;
    value31 = -value29.x + 1.0;
    value32 = value31.xxxx * FogColorNearMinusFar.xyzw + FogColorFar.xyzw;
    value33.w = min(value29.x, value32.w);
    value33.xyz = value33.www * value32.xyz;
    value34 = value30 * value31.xxxx + value33;
    o0.xyz = value25.xyz * (-value34.w + 1.0).xxx + value34.xyz;
    o0.w = 1.0;
    output.TARGET0 = o0.xyzw;
    return output;
}
