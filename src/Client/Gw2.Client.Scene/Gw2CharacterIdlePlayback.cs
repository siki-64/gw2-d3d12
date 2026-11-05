using System.Collections.Generic;
using System.Linq;
using Gw2.Client.Model;

namespace Gw2.Client.Scene;

public sealed class Gw2CharacterIdlePlayback
{
    private readonly Gw2BlendTreePlayer? _tree;
    private readonly IReadOnlyDictionary<ulong, Gw2AnimationClip> _clipsByToken = new Dictionary<ulong, Gw2AnimationClip>();

    public Gw2CharacterIdlePlayback(Gw2AnimationClip? clip)
    {
        Clip = clip;
    }

    internal Gw2CharacterIdlePlayback(Gw2BlendTreePlayer tree, IReadOnlyDictionary<ulong, Gw2AnimationClip> clipsByToken)
    {
        _tree = tree;
        _clipsByToken = clipsByToken;
        SelectTreeClip();
    }

    public Gw2AnimationClip? Clip { get; private set; }

    public float Seconds { get; private set; }

    public void Advance(float elapsedSeconds)
    {
        if (_tree is null)
        {
            if (Clip is { Duration: > 0f } clip) Seconds = (Seconds + elapsedSeconds) % clip.Duration;
            return;
        }
        _tree.Advance(elapsedSeconds);
        SelectTreeClip();
    }

    private void SelectTreeClip()
    {
        var playing = _tree!.PlayingClips.FirstOrDefault(candidate => _clipsByToken.ContainsKey(candidate.Token));
        if (playing.Token == 0) return;
        Clip = _clipsByToken[playing.Token];
        Seconds = playing.Seconds;
    }
}
