using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed record GrArchiveSampler(string ParameterName, int Slot, bool IsEngineTexture, int ResourceIndex, Gw2AmatSamplerState State, bool IsVertexStage = false, bool IsCube = false);
