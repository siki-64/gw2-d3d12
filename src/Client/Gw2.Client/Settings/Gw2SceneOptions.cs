using System.Numerics;
using Gw2.Client.World;

namespace Gw2.Client.Settings;

public sealed record Gw2SceneOptions(Gw2TimeOfDay TimeOfDay, float TimeOfDayRate, Vector3 Position, Vector3? CameraEye, Vector3? CameraFront, float? FieldOfView);
