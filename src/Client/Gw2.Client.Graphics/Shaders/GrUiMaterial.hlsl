#pragma program UiMaterial vertex=UiMaterialVS pixel=UiMaterialPS

static const float ModulateTwiceNeutral = 0.498039;
static const float3 Luminance = float3(0.3, 0.59, 0.11);
static const uint CompassCellShader = 256;
static const float CompassExplorationTexel = 0.00048828125;

cbuffer UiMaterialConstants
{
    float4 ModelColor;
    float4 Control;
    uint UiShader;
    float AlphaReference;
    float Time;
    float UiMaterialPadding;
    float4 SceneDepthTest;
    float4 SceneDepthProjection;
};

Texture2D Texture0;
Texture2D Texture1;
Texture2D Texture2;
Texture2D Texture3;
SamplerState TextureSampler;
Texture2D<float> SceneDepth;

struct UiMaterialVertex
{
    float4 Position : SV_Position;
    float4 Uv01 : TEXCOORD0;
    float4 Uv23 : TEXCOORD1;
};

UiMaterialVertex UiMaterialVS(float3 position : POSITION, float2 uv0 : TEXCOORD0, float2 uv1 : TEXCOORD1, float2 uv2 : TEXCOORD2, float2 uv3 : TEXCOORD3)
{
    UiMaterialVertex output;
    output.Position = float4(position, 1.0);
    output.Uv01 = float4(uv0, uv1);
    output.Uv23 = float4(uv2, uv3);
    return output;
}

float FillWindow(float3 ramp, float2 window)
{
    float softness = saturate(1.0 - ramp.b) + 0.0001;
    float opened = saturate((window.x * (2.0 - ramp.b) - ramp.r) / softness);
    float closed = saturate((window.y * (2.0 - ramp.b) - ramp.g) / softness);
    return opened * (1.0 - closed);
}

float RadialWindow(float3 ramp, float start, float end)
{
    float3 inverted = 1.0 - ramp.gbr;
    float span = 2.0 - ramp.b;
    float3 clamped = saturate(inverted);
    float softness = clamped.y + 0.0001;
    float2 edges = float2(
        saturate(1.0 - start) * -span + span - (ramp.g + clamped.z) * 0.5,
        end * -span + span - (ramp.r + clamped.x) * 0.5);
    float2 open = 1.0 - saturate(edges / softness);
    return open.x * open.y;
}

float4 Clipped(float4 color)
{
    clip(color.a - AlphaReference);
    return color;
}

float4 Masked(float4 base, float mask)
{
    base.a *= mask;
    return Clipped(base * ModelColor);
}

float4 Desaturated(float4 tinted, float amount)
{
    return float4(lerp(tinted.rgb, dot(tinted.rgb, Luminance).xxx, amount), tinted.a);
}

float SceneViewDepth(float2 pixel)
{
    float depth = SceneDepth.Load(int3(pixel * SceneDepthProjection.xy, 0));
    return SceneDepthProjection.z * SceneDepthProjection.w / (SceneDepthProjection.w - depth * (SceneDepthProjection.w - SceneDepthProjection.z));
}

float4 UiMaterialPS(UiMaterialVertex input) : SV_Target
{
    if (SceneDepthTest.w != 0.0 && SceneDepthTest.z > SceneDepth.Load(int3(input.Position.xy * SceneDepthTest.xy, 0))) discard;
    float2 uv0 = input.Uv01.xy;
    float2 uv1 = input.Uv01.zw;
    float2 uv2 = input.Uv23.xy;
    float2 uv3 = input.Uv23.zw;
    float4 base = Texture0.Sample(TextureSampler, uv0);

    switch (UiShader)
    {
    case 0:
        return float4(base.rgb * ModelColor.rgb * ModelColor.a, 0.0);
    case 4:
    {
        float4 tinted = Clipped(Desaturated(base * ModelColor, Control.z));
        float3 scaled = tinted.rgb * Control.x;
        return float4(Control.y * (1.0 - scaled) + scaled, tinted.a);
    }
    case 5:
    {
        float luminance = dot(base.rgb, Luminance);
        float3 toned = lerp(ModelColor.rgb, luminance.xxx, abs(luminance * 2.0 - 1.0));
        return Clipped(float4(toned * smoothstep(0.0, 1.0, saturate(luminance * 2.0)), base.a * ModelColor.a));
    }
    case 6:
    {
        float inFrontOfGeometry = smoothstep(0.0, 1.0, (SceneViewDepth(input.Position.xy) - Control.x + Control.y) / Control.y);
        float4 tinted = base * ModelColor;
        tinted.a *= lerp(Control.z, 1.0, inFrontOfGeometry);
        return Clipped(tinted);
    }
    case 7:
    {
        float4 front = Texture1.Sample(TextureSampler, uv0);
        float wipe = saturate((uv0.y - (1.0 - Control.x)) * 30.0);
        float4 mixed = float4(front.rgb * front.a * (1.0 - wipe) + wipe * base.a * base.rgb, front.a);
        clip(front.a * ModelColor.a - AlphaReference);
        return mixed * ModelColor;
    }
    case 10:
        return Masked(base, Control.x);
    case 12:
    case 13:
    case 14:
        return Masked(base, FillWindow(Texture1.Sample(TextureSampler, uv1).rgb, Control.xy));
    case 17:
    {
        float window = FillWindow(Texture2.Sample(TextureSampler, uv2).rgb, Control.xy) * Texture0.Sample(TextureSampler, uv1).a;
        float2 centred = uv0 - 0.5;
        float2 pushed = normalize(centred) * Control.z + uv0;
        float rim = saturate(1.0 - Texture0.Sample(TextureSampler, pushed).a * Texture1.Sample(TextureSampler, pushed).b);
        return Clipped(float4(1.0, 1.0, 1.0, rim * window) * ModelColor);
    }
    case 19:
        return Clipped(Desaturated(base * ModelColor, Control.x));
    case 20:
        return Masked(base, FillWindow(Texture3.Sample(TextureSampler, uv3).rgb, Control.xy)
            * Texture1.Sample(TextureSampler, uv1).g * Texture2.Sample(TextureSampler, uv2).g);
    case 21:
    {
        float coverage = FillWindow(Texture3.Sample(TextureSampler, uv3).rgb, Control.xy)
            * Texture1.Sample(TextureSampler, uv1).g * Texture2.Sample(TextureSampler, uv2).g * base.a;
        clip(coverage * ModelColor.a - AlphaReference);
        float band = smoothstep(0.0, 1.0, saturate((uv0.x * 0.25 + uv0.y) / Control.z));
        float3 tinted = base.rgb * lerp(ModelColor.rgb, ModulateTwiceNeutral.xxx, band) * 2.0;
        return float4(tinted * ModelColor.a, coverage * ModelColor.a);
    }
    case 28:
        return Masked(base, RadialWindow(Texture1.Sample(TextureSampler, uv1).rgb, Control.x, Control.y));
    case 29:
        return Masked(base, RadialWindow(Texture1.Sample(TextureSampler, uv1).rgb, Control.w, Control.x));
    case 32:
    {
        float4 tinted = base * ModelColor;
        tinted.a *= Texture1.Sample(TextureSampler, uv1).g;
        return float4(tinted.rgb * tinted.a, tinted.a);
    }
    case 34:
    case 35:
        return Masked(base, Texture1.Sample(TextureSampler, uv1).g);
    case 38:
        return Masked(base, Texture1.Sample(TextureSampler, uv1).g * Control.x);
    case 39:
        return Masked(base, FillWindow(Texture2.Sample(TextureSampler, uv2).rgb, Control.xy) * Texture1.Sample(TextureSampler, uv1).g);
    case 41:
    {
        float4 tinted = base * ModelColor;
        tinted.a *= Texture1.Sample(TextureSampler, uv1).g;
        return Clipped(Desaturated(tinted, Control.x));
    }
    case 42:
    {
        float2 flicker = (Texture2.Sample(TextureSampler, uv2).xy * 2.0 - 1.0) * Control.x * saturate(1.0 - uv0.y);
        float flame = Texture1.Sample(TextureSampler, uv1 + flicker).g * ModelColor.a;
        return float4(Texture0.Sample(TextureSampler, uv0 + flicker).rgb * ModelColor.rgb * flame, 0.0);
    }
    case 43:
    {
        float coverage = FillWindow(Texture2.Sample(TextureSampler, uv2).rgb, Control.xy) * Texture1.Sample(TextureSampler, uv1).g * ModelColor.a;
        return coverage * float4(base.rgb - 0.5, -0.5) + 0.5;
    }
    case 44:
        return Masked(base, Texture1.Sample(TextureSampler, uv1).g * Texture2.Sample(TextureSampler, uv2 + 1.0 - saturate(Control.xy)).g);
    case 46:
        return Masked(base, Texture1.SampleBias(TextureSampler, uv1, Control.x).g * Texture2.SampleBias(TextureSampler, uv2, Control.x).g);
    case 47:
    {
        float coverage = Texture1.SampleBias(TextureSampler, uv1, Control.x).g * Texture2.SampleBias(TextureSampler, uv2, Control.x).g * base.a * ModelColor.a;
        return coverage * float4(base.rgb * ModelColor.rgb - 0.5, -0.5) + 0.5;
    }
    case 48:
    {
        float coverage = base.a * ModelColor.a;
        return coverage * (base * ModelColor - float4(ModulateTwiceNeutral.xxx, 1.0)) + float4(ModulateTwiceNeutral.xxx, 1.0);
    }
    case 57:
    {
        float3 gradient = Texture1.Sample(TextureSampler, uv1).rgb;
        float level = (gradient.r + gradient.g + gradient.b) / 3.0;
        return Masked(base, 1.0 - saturate((1.0 - Control.x - level) * 10000.0));
    }
    case 50:
        return lerp(0.5, base, ModelColor.a);
    case 52:
    {
        float2 scrolled = Control.x * Time + uv0.yx;
        float clouds = Texture0.Sample(TextureSampler, float2(uv0.x, scrolled.x)).r * Texture0.Sample(TextureSampler, float2(scrolled.y, uv0.y)).r;
        float density = clouds * Texture2.Sample(TextureSampler, uv0).r * Control.y + Texture1.Sample(TextureSampler, uv0).r;
        float alpha = FillWindow(Texture3.Sample(TextureSampler, uv2).rgb, Control.zw) * density * ModelColor.a;
        return Clipped(float4(ModelColor.rgb, alpha));
    }
    case CompassCellShader:
    {
        float4 painted = Texture1.Sample(TextureSampler, uv1);
        float paintedCoverage = saturate(max(painted.r, max(painted.g, painted.b)) * 64.0);
        float water = ceil(painted.a) * saturate(painted.a * 3.3333333);
        float3 explored = lerp(painted.rgb, painted.a * float3(-0.3450982, -0.313725, -0.21176499) + float3(0.392157, 0.560784, 0.419608), water);
        float discovered = (Texture3.Sample(TextureSampler, uv3 + float2(CompassExplorationTexel, -CompassExplorationTexel)).r
            + Texture3.Sample(TextureSampler, uv3 + CompassExplorationTexel).r
            + Texture3.Sample(TextureSampler, uv3 - CompassExplorationTexel).r
            + Texture3.Sample(TextureSampler, uv3 + float2(-CompassExplorationTexel, CompassExplorationTexel)).r) * 0.25;
        float hidden = saturate(1.0 - discovered);
        float hiddenEdge = min(hidden * 1.25, 1.0);
        float noise = lerp(Texture2.Sample(TextureSampler, uv1).r, 1.0, smoothstep(0.0, 1.0, hidden));
        float fog = hiddenEdge * hiddenEdge * (3.0 - 2.0 * hiddenEdge) * noise;
        return float4(lerp(base.rgb, explored, paintedCoverage * (1.0 - fog)), 1.0);
    }
    default:
        return Clipped(base * ModelColor);
    }
}
