using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2EmoteTiming(
    uint AnimationFileId,
    float BlendIn,
    float BlendOut,
    uint IntroDuration,
    uint LoopDuration,
    uint OutroDuration,
    uint StartOffset);

public sealed record Gw2EmoteAnimation(ulong Token, uint AnimationFileId)
{
    public IReadOnlyList<Gw2EmoteTiming> Timings { get; init; } = [];
}

public sealed class Gw2EmoteAnimations
{
    private Gw2EmoteAnimations(IReadOnlyList<Gw2EmoteAnimation> animations) => Animations = animations;

    public IReadOnlyList<Gw2EmoteAnimation> Animations { get; }

    public static Gw2EmoteAnimations? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.FindRoot("PackEmoteAnimations");
        if (root is null) return null;

        var animations = new List<Gw2EmoteAnimation>();
        if ((root.Fields.GetValueOrDefault("Animation") ?? root.Fields.GetValueOrDefault("animations")) is not object?[] entries) return new(animations);

        foreach (var item in entries)
        {
            if (item is not PackObject entry) continue;
            var token = PackValue.AsU64(entry.Fields.GetValueOrDefault("Token") ?? entry.Fields.GetValueOrDefault("token"));
            var timing = entry.Fields.GetValueOrDefault("Timing") ?? entry.Fields.GetValueOrDefault("timings");
            var animationFile = TimingFile(timing);
            if (animationFile != 0) animations.Add(new(token, animationFile) { Timings = ReadTimings(timing) });
        }

        return new(animations);
    }

    private static IReadOnlyList<Gw2EmoteTiming> ReadTimings(object? timing) =>
        PackValue.AsObjects(timing)
            .Select(record => new Gw2EmoteTiming(
                FileId(record),
                PackValue.AsF32(record.Fields.GetValueOrDefault("BlendIn") ?? record.Fields.GetValueOrDefault("blendInTime")),
                PackValue.AsF32(record.Fields.GetValueOrDefault("BlendOut") ?? record.Fields.GetValueOrDefault("blendOutTime")),
                PackValue.AsU32(record.Fields.GetValueOrDefault("IntroDuration") ?? record.Fields.GetValueOrDefault("duration")),
                PackValue.AsU32(record.Fields.GetValueOrDefault("LoopDuration") ?? record.Fields.GetValueOrDefault("loopDuration")),
                PackValue.AsU32(record.Fields.GetValueOrDefault("OutroDuration")),
                PackValue.AsU32(record.Fields.GetValueOrDefault("StartOffset"))))
            .ToArray();

    private static uint FileId(PackObject record)
    {
        foreach (var name in new[] { "ModelFile", "modelFileId" })
            if (record.Fields.GetValueOrDefault(name) is PackFileReference reference && reference.TryGetFileId(out var fileId))
                return fileId;
        return 0;
    }

    private static uint TimingFile(object? timing)
    {
        if (timing is not object?[] slots || slots.Length == 0 || slots[0] is not PackObject record) return 0;
        foreach (var name in new[] { "ModelFile", "modelFileId" })
            if (record.Fields.GetValueOrDefault(name) is PackFileReference reference && reference.TryGetFileId(out var fileId))
                return fileId;
        return 0;
    }
}
