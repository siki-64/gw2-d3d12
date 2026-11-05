using System.Numerics;

namespace Gw2.Client;

public readonly record struct Gw2SceneNamedObject(uint AgentId, Vector3 PositionInGameUnits, string Name, float NameplateRangeInGameUnits);
