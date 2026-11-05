using System.Collections;
using System.Collections.Generic;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrTextureSlot
{
    public Texture2D? Texture { get; set; }
}

public sealed class GrTextureSlotList(IReadOnlyList<GrTextureSlot?> slots) : IReadOnlyList<Texture?>
{
    public Texture? this[int index] => slots[index]?.Texture;

    public int Count => slots.Count;

    public IEnumerator<Texture?> GetEnumerator()
    {
        foreach (var slot in slots) yield return slot?.Texture;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
