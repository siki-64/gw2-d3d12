#pragma program Streak vertex=StreakVertex pixel=FieldPixel
#pragma program Bubble vertex=BubbleVertex pixel=FieldPixel
#pragma program Sparkle vertex=SparkleVertex pixel=FieldPixel
#pragma program Ray vertex=RayVertex pixel=FieldPixel
#pragma program StreakSlab vertex=StreakSlabVertex pixel=FieldPixel
#pragma program BubbleSlab vertex=BubbleSlabVertex pixel=FieldPixel
#pragma program SparkleSlab vertex=SparkleSlabVertex pixel=FieldPixel
#pragma program RaySlab vertex=RaySlabVertex pixel=FieldPixel

float4 CameraPosition;
float4 ViewProjection[4];
float4 View[4];
float4 fldprta;
float4 fldprtb;
float4 fldprtc;
float4 fldprtd;
float4 fldprte;
float4 fldprtf;
float4 fldprtg;
float4 transformCount;
float4 CutoutRows[18];
float4 ScreenDims;

Texture2D ss00;
SamplerState ss00Sampler;
Texture2D ss12;
SamplerState ss12Sampler;

static const int MaximumCutouts = 6;

struct VertexInput
{
    float3 Position : POSITION0;
    float4 CornerAndPhase : COLOR0;
    float2 Scale : TEXCOORD0;
    float2 AngleAndRotation : TEXCOORD1;
    float2 FrameAndCorner : TEXCOORD2;
};

struct VertexOutput
{
    float4 FrameCoordinates : TEXCOORD0;
    float2 AlphaAndDepth : TEXCOORD1;
    float4 Position : SV_POSITION;
};

struct FieldMotion
{
    float3 Position;
    float Alpha;
};

float SmoothWave(float value)
{
    float wave = frac(value) * 2.0 - 1.0;
    return wave * wave * (3.0 - 2.0 * abs(wave));
}

float SignedFrac(float value)
{
    return value >= -value ? frac(abs(value)) : -frac(abs(value));
}

float Lerp(float2 range, float amount)
{
    return amount * (range.y - range.x) + range.x;
}

FieldMotion SparkleMotion(VertexInput input)
{
    float time = fldprta.w;
    float phase = input.CornerAndPhase.z;
    float spread = dot(input.Position, input.Position) * phase;
    float3 travelled = input.Position + fldprtc.xyz * (Lerp(fldprte.xy, phase) * time * 0.025);
    float deviationPhase = Lerp(fldprte.zw, frac(spread)) * time * (100.0 / fldprtb.x);
    float wanderPhase = spread + deviationPhase;
    float3 wander = float3(SmoothWave(wanderPhase + 0.5), SmoothWave(wanderPhase * 0.5), SmoothWave(wanderPhase + wanderPhase + 0.5));
    float twinkle = SmoothWave(spread * 100.0 + deviationPhase * 4.0);
    twinkle *= twinkle;
    twinkle *= twinkle;
    FieldMotion motion;
    motion.Position = travelled + wander * fldprtb.z;
    motion.Alpha = twinkle * input.CornerAndPhase.w * fldprtb.y;
    return motion;
}

FieldMotion BubbleMotion(VertexInput input)
{
    float time = fldprta.w;
    float phase = input.CornerAndPhase.z;
    float spread = dot(input.Position, input.Position) * phase;
    float distanceScale = 100.0 / fldprtb.x;
    float3 travelled = input.Position + fldprtc.xyz * (Lerp(fldprte.xy, frac(spread)) * time * distanceScale);
    float wobblePhase = spread + Lerp(fldprte.zw, phase) * time * distanceScale;
    float sway = SmoothWave(wobblePhase - SmoothWave(wobblePhase + 0.5));
    float2 wobble = float2(
        SmoothWave(wobblePhase * 0.5) * sway,
        SmoothWave((wobblePhase + 0.5) * 2.223) + SmoothWave((wobblePhase + 0.5) * 3.223 + 0.1234));
    FieldMotion motion;
    motion.Position = float3(travelled.xy + wobble * fldprtb.z, travelled.z);
    motion.Alpha = min(SmoothWave(spread) * fldprtb.y * 4.0, 1.0) * input.CornerAndPhase.w;
    return motion;
}

FieldMotion StreakMotion(VertexInput input)
{
    float time = fldprta.w;
    float phase = input.CornerAndPhase.z;
    float spread = dot(input.Position, input.Position) * phase;
    FieldMotion motion;
    motion.Position = input.Position + fldprtc.xyz * (Lerp(fldprte.xy, phase) * time * (2000.0 / fldprtb.x));
    motion.Alpha = min(SmoothWave(spread) * fldprtb.y * 4.0, 1.0) * input.CornerAndPhase.w;
    return motion;
}

FieldMotion RayMotion(VertexInput input)
{
    float time = fldprta.w;
    float phase = input.CornerAndPhase.z;
    float spread = dot(input.Position, input.Position) * phase;
    float deviationPhase = Lerp(fldprte.zw, frac(spread)) * time * (100.0 / fldprtb.x);
    float swayPhase = spread + deviationPhase;
    float horizontalLength = length(fldprtc.xy);
    float3 horizontalDirection = horizontalLength > 0.0 ? float3(fldprtc.xy, 0.0) / horizontalLength : float3(0.0, 0.0, 0.0);
    float twinkle = SmoothWave(spread * 100.0 + deviationPhase * 4.0);
    twinkle *= twinkle;
    twinkle *= twinkle;
    FieldMotion motion;
    motion.Position = input.Position + SmoothWave(swayPhase + 0.5) * horizontalDirection * fldprtb.z;
    motion.Alpha = twinkle * input.CornerAndPhase.w * fldprtb.y;
    return motion;
}

float CutoutWeight(float3 worldPosition)
{
    float weight = 1.0;
    int count = (int)transformCount.x;
    float4 position = float4(worldPosition, 1.0);
    [loop]
    for (int cutout = 0; cutout < MaximumCutouts && cutout < count; cutout++)
    {
        float3 boxPosition = float3(dot(CutoutRows[cutout * 3], position), dot(CutoutRows[cutout * 3 + 1], position), dot(CutoutRows[cutout * 3 + 2], position));
        float3 extent = min(abs(boxPosition * 0.85), 1.0);
        float outside = min(dot(floor(extent), float3(1.0, 1.0, 1.0)), 1.0);
        float edge = max(max(extent.x, extent.y), extent.z);
        weight *= outside * (outside - edge) + edge;
    }
    return weight > 0.0 ? pow(abs(weight), 16.0) : 0.0;
}

float4 FrameCoordinates(float2 frameAndCorner)
{
    float columns = fldprtd.y;
    float rows = fldprtd.z;
    float2 frames = float2(frameAndCorner.x + fldprtd.x, frameAndCorner.x + fldprtd.x + 1.0) / columns;
    float2 column = float2(SignedFrac(frames.x), SignedFrac(frames.y));
    float2 rowFrames = floor(frames) / rows;
    float2 row = float2(SignedFrac(rowFrames.x), SignedFrac(rowFrames.y));
    float cornerU = floor(abs(frameAndCorner.y) + 0.5);
    float cornerV = floor(frameAndCorner.y + 1.1);
    float2 u = (column * columns + cornerU) / columns;
    float2 v = (row * rows + cornerV) / rows;
    return float4(u.x, v.x, u.y, v.y);
}

float LifetimeGate(float phase)
{
    float lifetime = Lerp(fldprtf.zw, phase);
    float cycle = (Lerp(fldprtf.xy, phase) + lifetime) * 0.999995;
    float cyclePosition = SignedFrac((phase * cycle + fldprta.w) / cycle);
    return 1.0 - ceil(saturate(cyclePosition * cycle - lifetime));
}

VertexOutput FieldVertex(VertexInput input, int fieldType, bool inSlab)
{
    FieldMotion motion;
    if (fieldType == 0) motion = StreakMotion(input);
    else if (fieldType == 1) motion = BubbleMotion(input);
    else if (fieldType == 2) motion = SparkleMotion(input);
    else motion = RayMotion(input);

    float extent = fldprtb.x;
    float3 wrapped = frac(motion.Position - fldprta.xyz);
    float3 worldPosition = (wrapped + fldprta.xyz) * extent;
    float3 fieldPosition = worldPosition;
    float slabFade = 1.0;
    if (inSlab)
    {
        float depth = fldprtc.w;
        float signedDepth = SignedFrac(worldPosition.z / depth) * depth;
        float slabDepth = signedDepth >= 0.0 ? signedDepth : signedDepth + depth;
        fieldPosition.z = slabDepth + fldprtb.w;
        slabFade = saturate((depth * 0.5 - abs(slabDepth - depth * 0.5)) / fldprtd.w);
    }

    float angle = fldprta.w * input.AngleAndRotation.y + input.AngleAndRotation.x;
    float sine;
    float cosine;
    sincos(angle, sine, cosine);
    float3 right;
    float3 up;
    if (fieldType == 0 || fieldType == 3)
    {
        float3 toEye = normalize(worldPosition - CameraPosition.xyz);
        float3 side = cross(toEye, fldprtc.xyz);
        right = (cosine * side - sine * fldprtc.xyz) * input.Scale.x;
        up = (-cosine * fldprtc.xyz - sine * side) * input.Scale.y;
    }
    else
    {
        right = (sine * View[1].xyz + cosine * View[0].xyz) * input.Scale.x;
        up = (cosine * View[1].xyz - sine * View[0].xyz) * input.Scale.y;
    }

    float cutoutWeight = CutoutWeight(fieldPosition);
    float cornerU = input.CornerAndPhase.x * 2.0 - 1.0;
    float cornerV = fieldType == 3 ? input.CornerAndPhase.y * -2.0 : input.CornerAndPhase.y * 2.0 - 1.0;
    float keep = ceil((cutoutWeight - 0.05) * 1.05);
    float3 cornerPosition = (fieldPosition + right * cornerU + up * cornerV) * keep;
    float4 clipPosition = float4(
        dot(float4(cornerPosition, 1.0), ViewProjection[0]),
        dot(float4(cornerPosition, 1.0), ViewProjection[1]),
        dot(float4(cornerPosition, 1.0), ViewProjection[2]),
        dot(float4(cornerPosition, 1.0), ViewProjection[3]));

    float3 edge = wrapped * 2.0 - 1.0;
    edge = 1.0 - edge * edge * edge * edge;
    float alpha = motion.Alpha * min(min(edge.x, edge.y), edge.z);
    alpha *= LifetimeGate(input.CornerAndPhase.z);
    alpha = min(abs(worldPosition.z) * 0.01, alpha);
    alpha *= slabFade;
    alpha *= cutoutWeight;
    if (fieldType == 3)
    {
        float eyeDistance = saturate(length(CameraPosition.xyz - cornerPosition) / extent);
        eyeDistance *= eyeDistance;
        alpha *= 1.0 - eyeDistance * eyeDistance;
    }

    VertexOutput output;
    output.FrameCoordinates = FrameCoordinates(input.FrameAndCorner);
    output.AlphaAndDepth = float2(alpha, clipPosition.z);
    output.Position = clipPosition * saturate(ceil(alpha));
    return output;
}

VertexOutput StreakVertex(VertexInput input) { return FieldVertex(input, 0, false); }
VertexOutput BubbleVertex(VertexInput input) { return FieldVertex(input, 1, false); }
VertexOutput SparkleVertex(VertexInput input) { return FieldVertex(input, 2, false); }
VertexOutput RayVertex(VertexInput input) { return FieldVertex(input, 3, false); }
VertexOutput StreakSlabVertex(VertexInput input) { return FieldVertex(input, 0, true); }
VertexOutput BubbleSlabVertex(VertexInput input) { return FieldVertex(input, 1, true); }
VertexOutput SparkleSlabVertex(VertexInput input) { return FieldVertex(input, 2, true); }
VertexOutput RaySlabVertex(VertexInput input) { return FieldVertex(input, 3, true); }

float4 FieldPixel(VertexOutput input) : SV_TARGET
{
    float2 screenCoordinate = (input.Position.xy + 0.5) * ScreenDims.xy;
    float sceneDepth = ss12.Sample(ss12Sampler, screenCoordinate).x * ScreenDims.w - ScreenDims.z;
    float softness = saturate((sceneDepth - input.AlphaAndDepth.y) * 0.008333);
    softness = softness * softness * (3.0 - 2.0 * softness);
    softness *= softness;
    float4 current = ss00.Sample(ss00Sampler, input.FrameCoordinates.xy);
    float4 next = ss00.Sample(ss00Sampler, input.FrameCoordinates.zw);
    float4 color = lerp(current, next, fldprtg.w);
    return float4(color.rgb + color.rgb, color.a * input.AlphaAndDepth.x * softness);
}
