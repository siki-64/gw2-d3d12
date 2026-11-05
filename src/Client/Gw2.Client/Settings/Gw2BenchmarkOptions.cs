using Gw2.Client.Scene;

namespace Gw2.Client.Settings;

public sealed record Gw2BenchmarkOptions(int FrameLimit, int BackBufferWidth, int BackBufferHeight, float? RecordSeconds, float WarmupSeconds, string? Name, string? ReportPath, Gw2FlightPath? Flight, bool LogsSlowFrames = false);
