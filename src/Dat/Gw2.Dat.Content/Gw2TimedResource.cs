namespace Gw2.Dat.Content;

public readonly record struct Gw2TimedResource(float Value, float Maximum, float RatePerSecond, double TimestampSeconds)
{
    public float ValueAt(double timeSeconds) =>
        Math.Clamp(Value + (float)(timeSeconds - TimestampSeconds) * RatePerSecond, 0f, Maximum);

    public float RatioAt(double timeSeconds) => Maximum <= 0f ? 0f : ValueAt(timeSeconds) / Maximum;

    public bool CanSpend(float amount, double timeSeconds) => ValueAt(timeSeconds) >= amount;

    public Gw2TimedResource Spend(float amount, double timeSeconds) =>
        this with { Value = Math.Clamp(ValueAt(timeSeconds) - amount, 0f, Maximum), TimestampSeconds = timeSeconds };

    public Gw2TimedResource Gain(float amount, double timeSeconds) => Spend(-amount, timeSeconds);

    public Gw2TimedResource WithRate(float ratePerSecond, double timeSeconds) =>
        this with { Value = ValueAt(timeSeconds), RatePerSecond = ratePerSecond, TimestampSeconds = timeSeconds };
}
