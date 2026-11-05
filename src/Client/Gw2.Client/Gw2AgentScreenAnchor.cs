using System.Numerics;

namespace Gw2.Client;

public readonly record struct Gw2AgentScreenAnchor(uint AgentId, string Name, Vector2 Position, float Depth, Gw2VisibleAttitude VisibleAttitude, bool IsControlled, float Opacity)
{
    public bool WithinNameplateRange => Opacity > 0f;
}
