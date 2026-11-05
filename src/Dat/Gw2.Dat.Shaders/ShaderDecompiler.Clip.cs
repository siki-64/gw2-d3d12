namespace Gw2.Dat.Shaders;

public sealed partial class ShaderDecompiler
{
    private readonly Dictionary<int, (int Compare, int Component)> _clips = [];

    private void RecogniseClips()
    {
        var removedComponents = new Dictionary<int, int>();
        for (var index = 0; index < Instructions.Count; index++)
        {
            var discard = Instructions[index];
            if (discard.Name != "discard" || !discard.TestNonZero || !IsPlainTemp(discard.Operands[0])) continue;
            if (ReachingDefinition(index, discard.Operands[0], 0) is not { } compareIndex) continue;
            var compare = Instructions[compareIndex];
            var component = discard.Operands[0].Swizzle[0];
            if (compare.Name != "lt" || _idiomExpressions.ContainsKey(compareIndex) || _skippedInstructions.Contains(compareIndex)) continue;
            if (!IsImmediate(compare.Operands[2], component, 0f) || OtherComponentReaders(compareIndex, compare.Operands[0].FirstIndex, component, index)) continue;
            _clips[index] = (compareIndex, component);
            removedComponents[compareIndex] = removedComponents.GetValueOrDefault(compareIndex) | (1 << component);
        }

        foreach (var (compareIndex, removed) in removedComponents)
        {
            var destination = Instructions[compareIndex].Operands[0];
            var mask = destination.Mask & ~removed;
            if (mask == 0) _skippedInstructions.Add(compareIndex);
            else _idiomDestinations[compareIndex] = destination with { Mask = mask };
        }
    }

    private bool OtherComponentReaders(int definition, int temp, int component, int allowedReader) =>
        _readDefinitions.Any(pair => pair.Key != allowedReader && pair.Value.TryGetValue((temp, component), out var definitions) && definitions.Contains(definition));

    private bool TranslateClip(int index)
    {
        if (!_clips.TryGetValue(index, out var clip)) return false;
        Line($"clip({SourceAt(clip.Compare, Instructions[clip.Compare].Operands[1], [clip.Component], ValueType.Float)});");
        return true;
    }
}
