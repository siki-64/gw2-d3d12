using System.Numerics;

namespace Gw2.Client.Scene;

public readonly record struct Gw2CharacterMotion(float GroundSpeed, Vector2 Facing, Vector2 MoveDirection);
