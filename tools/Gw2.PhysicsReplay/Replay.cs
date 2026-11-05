using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Scene;
using Gw2.Client.Scene.Havok;
using System.Runtime.Intrinsics;
using Gw2.Dat.Content;
using Gw2.Formats;
using Gw2.Protocol.MsgPack;

internal static class Replay
{
    private const float StepSeconds = 0.04f;
    private const float GameUnitsPerCollisionUnit = 32f;

    public static void OneStepPredictions(uint mapId, IReadOnlyList<DecodedMessage> outbound, float radius, float height)
    {
        using var reader = Gw2ContentReader.Open(Gw2Installation.DatPath()!, verifyChecksums: false);
        var maps = new Gw2MapDefinitions(reader.ContentIndex, MapContentLayout.Current);
        if (maps.Find(mapId) is not { MapFileBaseId: not 0 } definition || !reader.Ids.TryGetFileId(definition.MapFileBaseId, out var fileId) || reader.ReadMap(fileId) is not { } map)
        {
            Console.WriteLine($"map {mapId} has no map file");
            return;
        }
        var states = new Dictionary<long, DecodedMessage>();
        var commands = new SortedDictionary<long, Vector2?>();
        foreach (var message in outbound)
        {
            if (message.MessageId == 0x0b && message.Fields.Count > 7 && message.Fields[7].Value is byte[] commandBytes)
                commands[(long)(ulong)message.Fields[2].Value!] = new Vector2(BitConverter.ToSingle(commandBytes, 0), BitConverter.ToSingle(commandBytes, 4));
            if (message.MessageId == 0x04) commands[(long)(ulong)message.Fields[2].Value!] = null;
            if (message.MessageId == 0x12) states[(long)(ulong)message.Fields[1].Value!] = message;
        }

        var jumps = outbound.Where(message => message.MessageId == 0x09).Select(message => (long)(ulong)message.Fields[2].Value!).ToHashSet();
        var dodges = new Dictionary<long, Vector2>();
        foreach (var message in outbound)
            if (message.MessageId == 0x0b && message.Fields.Count > 9 && message.Fields[5].Value is byte[] events && events.Length >= 3 && events[0] == 0x6c && message.Fields[7].Value is byte[] dodgeBytes)
                dodges[(long)(ulong)message.Fields[2].Value!] = new Vector2(BitConverter.ToSingle(dodgeBytes, 0), BitConverter.ToSingle(dodgeBytes, 4));
        Console.WriteLine($"dodges {dodges.Count}");
        DodgePredictions(map, states, dodges, radius, height);
        HavokPredictions(map, states, CommandDirections(commands, states.Keys), radius, height, false, false, jumps);
        HavokPredictions(map, states, CommandDirections(commands, states.Keys), radius, height, false, true, jumps);
    }

    private static Dictionary<long, Vector2?> CommandDirections(SortedDictionary<long, Vector2?> commands, IEnumerable<long> steps)
    {
        var result = new Dictionary<long, Vector2?>();
        foreach (var step in steps)
        {
            Vector2? direction = null;
            foreach (var (commandStep, commandDirection) in commands)
            {
                if (commandStep > step) break;
                direction = commandDirection;
            }
            result[step - 1] = direction;
        }
        return result;
    }

    private static void HavokPredictions(Gw2MapBundle map, Dictionary<long, DecodedMessage> states, Dictionary<long, Vector2?> directionAtStep, float radius, float height, bool countStanding, bool includeAir, HashSet<long> jumps)
    {
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        Gw2CharacterStep? character = null;
        var compared = 0;
        var exact = 0;
        var errors = new List<float>();
        var misses = new Dictionary<string, int>();
        var lastDirection = Vector2.UnitX;
        var airCompared = 0;
        var airExact = 0;
        var airShown = 0;
        foreach (var (step, state) in states.OrderBy(pair => pair.Key))
        {
            if (!states.TryGetValue(step - 1, out var previous) || (!includeAir && (!IsGround(state) || !IsGround(previous))))
            {
                character = null;
                continue;
            }
            var from = PositionOf(previous);
            var fresh = character is null;
            character ??= new Gw2CharacterStep(world, radius / GameUnitsPerCollisionUnit, height / GameUnitsPerCollisionUnit, Vector128.Create(from.X, from.Y, from.Z, 0f));
            character.Proxy.Position = Vector128.Create(from.X, from.Y, from.Z, 0f);
            var walks = directionAtStep[step - 1] is { } walk;
            var direction = walks ? Vector2.Normalize(directionAtStep[step - 1]!.Value) : lastDirection;
            var move = walks ? Vector128.Create(direction.X, direction.Y, 0f, 0f) : Vector128<float>.Zero;
            var lastForward = Vector128.Create(lastDirection.X, lastDirection.Y, 0f, 0f);
            character.Step(step * StepSeconds, move, lastForward, 294f / GameUnitsPerCollisionUnit, 0f, walks, jumps.Contains(step));
            if (includeAir && !IsGround(state)) airCompared++;
            lastDirection = direction;
            if (!walks && !countStanding) continue;
            var predicted = new Vector3(character.Position.GetElement(0), character.Position.GetElement(1), character.Position.GetElement(2));
            var recorded = PositionOf(state);
            compared++;
            if (BitsOf(predicted).SequenceEqual(BitsOf(recorded)))
            {
                exact++;
                if (includeAir && !IsGround(state)) airExact++;
            }
            else if (includeAir && !IsGround(state) && airShown++ < 12) Console.WriteLine($"air {step} jump {jumps.Contains(step)} state {character.Context.Current} error {predicted - recorded} recordedStep {recorded - from} velocity {character.Velocity} z {predicted.Z} rec {recorded.Z}");
            else if (directionAtStep.GetValueOrDefault(step) is { } next && next != directionAtStep[step - 1] && ExactWith(world, radius, height, from, next, lastForward, step, recorded))
            {
                misses["next command"] = misses.GetValueOrDefault("next command") + 1;
            }
            else
            {
                var kind = fresh ? "start" : predicted.X == recorded.X && predicted.Y == recorded.Y ? "z"
                    : MathF.Abs((predicted - from).Length() - (recorded - from).Length()) < 2e-5f ? "turn" : "xy";
                misses[kind] = misses.GetValueOrDefault(kind) + 1;
                if (kind == "xy" && misses[kind] <= 8 && !includeAir) Console.WriteLine($"miss {step} {predicted - recorded} state {character.Context.Current} surface {character.LastOutput.Velocity}");
            }
            errors.Add((predicted - recorded).Length());
        }
        errors.Sort();
        Console.WriteLine(string.Join(" ", misses.Select(pair => $"{pair.Key}={pair.Value}")));
        Console.WriteLine(compared == 0 ? "havok: nothing compared" : $"havok air {includeAir} ({airExact}/{airCompared} air): compared {compared} exact {exact} median error {errors[errors.Count / 2]:G4} cu p90 {errors[errors.Count * 9 / 10]:G4} max {errors[^1]:G4}");
    }

    private static void DodgePredictions(Gw2MapBundle map, Dictionary<long, DecodedMessage> states, Dictionary<long, Vector2> dodges, float radius, float height)
    {
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var compared = 0;
        var exact = 0;
        var shown = 0;
        foreach (var (dodgeStep, dodgeDirection) in dodges)
        {
            if (!states.TryGetValue(dodgeStep - 1, out var start)) continue;
            var from = PositionOf(start);
            var character = new Gw2CharacterStep(world, radius / GameUnitsPerCollisionUnit, height / GameUnitsPerCollisionUnit, Vector128.Create(from.X, from.Y, from.Z, 0f));
            var direction = Vector2.Normalize(dodgeDirection);
            var move = Vector128.Create(direction.X, direction.Y, 0f, 0f);
            for (var step = dodgeStep; step < dodgeStep + 20; step++)
            {
                if (!states.TryGetValue(step - 1, out var previous) || !states.TryGetValue(step, out var state)) break;
                var before = PositionOf(previous);
                character.Proxy.Position = Vector128.Create(before.X, before.Y, before.Z, 0f);
                character.Step(step * StepSeconds, move, move, 367f / GameUnitsPerCollisionUnit, 0f, true);
                var predicted = new Vector3(character.Position.GetElement(0), character.Position.GetElement(1), character.Position.GetElement(2));
                var recorded = PositionOf(state);
                compared++;
                if (BitsOf(predicted).SequenceEqual(BitsOf(recorded))) exact++;
                else if (shown++ < 10) Console.WriteLine($"dodge {dodgeStep}+{step - dodgeStep} error {predicted - recorded} recordedStep {recorded - before} state {character.Context.Current} ground {IsGround(state)}");
            }
        }
        Console.WriteLine($"dodge steps compared {compared} exact {exact}");
    }

    private static bool ExactWith(Gw2HavokMapWorld world, float radius, float height, Vector3 from, Vector2 walk, Vector128<float> lastForward, long step, Vector3 recorded)
    {
        var start = Vector128.Create(from.X, from.Y, from.Z, 0f);
        var candidate = new Gw2CharacterStep(world, radius / GameUnitsPerCollisionUnit, height / GameUnitsPerCollisionUnit, start);
        var direction = Vector2.Normalize(walk);
        var move = Vector128.Create(direction.X, direction.Y, 0f, 0f);
        candidate.Step(step * StepSeconds, move, lastForward, 294f / GameUnitsPerCollisionUnit, 0f, true);
        var predicted = new Vector3(candidate.Position.GetElement(0), candidate.Position.GetElement(1), candidate.Position.GetElement(2));
        return BitsOf(predicted).SequenceEqual(BitsOf(recorded));
    }

    private static Vector3 GameOf(Vector3 collision) => new(collision.X * GameUnitsPerCollisionUnit, collision.Y * GameUnitsPerCollisionUnit, -collision.Z * GameUnitsPerCollisionUnit);

    private static Vector3 CollisionOf(Vector3 game) => new(game.X / GameUnitsPerCollisionUnit, game.Y / GameUnitsPerCollisionUnit, -game.Z / GameUnitsPerCollisionUnit);

    private static byte[] BitsOf(Vector3 position) => [.. BitConverter.GetBytes(position.X), .. BitConverter.GetBytes(position.Y), .. BitConverter.GetBytes(position.Z)];

    private static bool IsGround(DecodedMessage message) => (ulong)message.Fields[5].Value! == 0 && (ulong)message.Fields[6].Value! == 0;

    private static Vector3 PositionOf(DecodedMessage message)
    {
        var bytes = (byte[])message.Fields[3].Value!;
        return new Vector3(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8));
    }
}
