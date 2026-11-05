using System.Numerics;

namespace Gw2.Client;

public readonly record struct Gw2SceneCharacter(uint AgentId, Vector3 PositionInGameUnits, float HeightInGameUnits, Vector3 Facing, float RadiusInGameUnits, Gw2VisibleAttitude VisibleAttitude, bool IsControlled, string? LiteralName, string? CodedName);
