using Gw2.Formats;
using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public enum Gw2AnimActionKind
{
    PlayAnimation = 0,
    PlayBlendTree = 1,
    PlayBlendTreeAlias = 2,
    SetAnimationOptions = 3,
    SetRotation = 4,
}

public sealed record Gw2AnimAction(Gw2AnimActionKind Kind, ulong Token)
{
    public float BlendInTime { get; init; }
    public float BlendOutTime { get; init; }
    public uint Flags { get; init; }
    public ReadOnlyMemory<byte> BlendTree { get; init; }
    public IReadOnlyList<float> Rotation { get; init; } = [];
}

public sealed record Gw2AnimActionVariant(ulong Token, IReadOnlyList<Gw2AnimAction> Actions);

public sealed record Gw2AnimMachineTransition(string Name, string TargetStateName)
{
    public IReadOnlyList<Gw2AnimAction> Actions { get; init; } = [];
    public IReadOnlyList<Gw2AnimActionVariant> Variants { get; init; } = [];
}

public sealed record Gw2AnimMachineStateVariant(
    ulong Token,
    IReadOnlyList<Gw2AnimAction> Actions,
    IReadOnlyList<Gw2AnimActionVariant> ActionVariants,
    IReadOnlyList<Gw2AnimMachineTransition> Transitions);

public sealed record Gw2AnimMachineState(
    string Name,
    IReadOnlyList<Gw2AnimAction> Actions,
    IReadOnlyList<Gw2AnimMachineTransition> Transitions)
{
    public IReadOnlyList<Gw2AnimActionVariant> ActionVariants { get; init; } = [];
    public IReadOnlyList<Gw2AnimMachineStateVariant> Variants { get; init; } = [];
}

public sealed record Gw2AnimMachine(IReadOnlyList<Gw2AnimMachineState> States);

public sealed record Gw2AnimMachineModel(uint ModelFileId, string ModelFileRaw, uint MachineIndex)
{
    public Guid Listeners { get; init; }
}

public sealed class Gw2AnimMachines
{
    public Gw2AnimMachines(IReadOnlyList<Gw2AnimMachine> machines, IReadOnlyList<Gw2AnimMachineModel> models)
    {
        Machines = machines;
        Models = models;
    }

    public IReadOnlyList<Gw2AnimMachine> Machines { get; }
    public IReadOnlyList<Gw2AnimMachineModel> Models { get; }

    public static Gw2AnimMachines? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Chunks.FirstOrDefault(parsed => parsed.Chunk.Type == "mach")?.Root;
        if (root is null) return null;
        var machines = Objects(root, "machines").Select(machine => new Gw2AnimMachine(Objects(machine, "states").Select(State).ToArray())).ToArray();
        var models = Objects(root, "models").Select(Model).ToArray();
        return new Gw2AnimMachines(machines, models);
    }

    private static Gw2AnimMachineState State(PackObject state) =>
        new(Text(state, "name"), ActionBlock(state), Objects(state, "transitions").Select(Transition).ToArray())
        {
            ActionVariants = ActionVariantBlock(state),
            Variants = Objects(state, "variants").Select(variant => new Gw2AnimMachineStateVariant(
                PackValue.AsU64(variant.Fields["token"]), ActionBlock(variant), ActionVariantBlock(variant),
                Objects(variant, "transitions").Select(Transition).ToArray())).ToArray(),
        };

    private static Gw2AnimMachineTransition Transition(PackObject transition) =>
        new(Text(transition, "name"), Text(transition, "targetStateName"))
        {
            Actions = ActionBlock(transition),
            Variants = Objects(transition, "variants").Select(TokenActions).ToArray(),
        };

    private static Gw2AnimMachineModel Model(PackObject model) => new(
        model.Fields["modelFileId"] is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0,
        Text(model, "modelFileRaw"),
        PackValue.AsU32(model.Fields["machineIndex"]))
    {
        Listeners = model.Optional("listeners") is ReadOnlyMemory<byte> { Length: 16 } listeners ? new Guid(listeners.Span) : Guid.Empty,
    };

    private static IReadOnlyList<Gw2AnimActionVariant> ActionVariantBlock(PackObject owner) =>
        owner.Fields["actionVariantBlock"] is PackObject block
            ? Objects(block, "actionVariants").Select(TokenActions).ToArray()
            : [];

    private static Gw2AnimActionVariant TokenActions(PackObject variant) =>
        new(PackValue.AsU64(variant.Fields["token"]), ActionBlock(variant));

    private static IReadOnlyList<Gw2AnimAction> ActionBlock(PackObject owner) =>
        owner.Fields["actionBlock"] is PackObject block
            ? Objects(block, "actions").Select(action => Action((PackVariant)action.Fields["actionData"]!)).ToArray()
            : [];

    private static Gw2AnimAction Action(PackVariant variant)
    {
        var kind = (Gw2AnimActionKind)variant.Case;
        var payload = variant.Payload;
        if (payload is null) return new Gw2AnimAction(kind, 0);
        float Single(string name) => payload.Fields.TryGetValue(name, out var value) ? PackValue.AsF32(value) : 0;
        return kind switch
        {
            Gw2AnimActionKind.PlayAnimation => new(kind, PackValue.AsU64(payload.Fields["sequence"]))
            {
                BlendInTime = Single("blendInTime"), BlendOutTime = Single("blendOutTime"), Flags = PackValue.AsU32(payload.Fields["flags"]),
            },
            Gw2AnimActionKind.PlayBlendTree => new(kind, 0)
            {
                BlendTree = payload.Fields["blendTree"] is ReadOnlyMemory<byte> blendTree ? blendTree : default, BlendInTime = Single("blendInTime"),
            },
            Gw2AnimActionKind.PlayBlendTreeAlias => new(kind, PackValue.AsU64(payload.Fields["blendTreeAlias"])) { BlendInTime = Single("blendInTime") },
            Gw2AnimActionKind.SetAnimationOptions => new(kind, 0)
            {
                BlendInTime = Single("blendInTime"), BlendOutTime = Single("blendOutTime"), Flags = PackValue.AsU32(payload.Fields["flags"]),
            },
            Gw2AnimActionKind.SetRotation => new(kind, 0) { Rotation = payload.Fields["rotation"] as float[] ?? [] },
            _ => new(kind, 0),
        };
    }

    private static IEnumerable<PackObject> Objects(PackObject owner, string field) =>
        owner.Fields[field] is object?[] items ? items.OfType<PackObject>() : [];

    private static string Text(PackObject owner, string field) => owner.Fields[field] as string ?? string.Empty;
}
