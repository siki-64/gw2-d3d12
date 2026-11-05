using System;
using Gw2.Dat.Content;

namespace Gw2.Client.World
{
    public sealed class Gw2TimeOfDayClock
    {
        public const uint ClockCycleMilliseconds = 0x6DDD00;
        private const float ClockMillisecondsPerHour = 300000f;
        private const float ClockMillisecondsPerMinute = 5000f;
        private const float ClockMillisecondsPerSecond = 83f;
        private const float SecondsPerHour = 3600f;
        private const float SecondsPerMinute = 60f;

        private float _fractionalMilliseconds;

        public uint ClockMilliseconds { get; private set; }

        public float Rate { get; private set; } = 1f;

        public bool IsFrozen { get; private set; }

        public Gw2TimeOfDay TimeOfDay
        {
            get
            {
                uint withinCycle = ClockMilliseconds % ClockCycleMilliseconds;
                return new Gw2TimeOfDay(
                    (int)(withinCycle / ClockMillisecondsPerHour),
                    (int)(withinCycle % (uint)ClockMillisecondsPerHour / ClockMillisecondsPerMinute),
                    (int)(withinCycle % (uint)ClockMillisecondsPerMinute / ClockMillisecondsPerSecond));
            }
        }

        public static uint ClockOf(Gw2TimeOfDay time) =>
            (uint)(time.Hour * ClockMillisecondsPerHour + time.Minute * ClockMillisecondsPerMinute + time.Second * ClockMillisecondsPerSecond);

        public static float DayWeight(Gw2DayNightCycle cycle, Gw2TimeOfDay time)
        {
            float now = time.Hour * SecondsPerHour + time.Minute * SecondsPerMinute + time.Second;
            float nightEnd = cycle.NightEndHour * SecondsPerHour;
            float dayStart = cycle.DayStartHour * SecondsPerHour;
            float dayEnd = cycle.DayEndHour * SecondsPerHour;
            float nightStart = cycle.NightStartHour * SecondsPerHour;
            if (Within(now, dayStart, dayEnd)) return 1f;
            if (Within(now, nightStart, nightEnd)) return 0f;
            float weight = Within(now, nightEnd, dayStart)
                ? 1f - (dayStart - now) / (dayStart - nightEnd)
                : (nightStart - now) / (nightStart - dayEnd);
            return Math.Clamp(weight, 0f, 1f);
        }

        private static bool Within(float now, float start, float end) =>
            start < end ? now >= start && now <= end : now >= start || now <= end;

        public void Set(uint clockMilliseconds, float rate, bool isFrozen)
        {
            ClockMilliseconds = clockMilliseconds;
            _fractionalMilliseconds = 0f;
            Rate = rate;
            IsFrozen = isFrozen;
        }

        public void EnterMap(bool mapFreezesTimeOfDay)
        {
            if (mapFreezesTimeOfDay) IsFrozen = true;
        }

        public void Advance(int elapsedMilliseconds)
        {
            if (IsFrozen) return;
            var scaled = elapsedMilliseconds * Rate;
            var whole = (int)MathF.Truncate(scaled);
            _fractionalMilliseconds += scaled - whole;
            if (_fractionalMilliseconds >= 1f)
            {
                var carried = (int)MathF.Truncate(_fractionalMilliseconds);
                whole += carried;
                _fractionalMilliseconds -= carried;
            }
            ClockMilliseconds = unchecked(ClockMilliseconds + (uint)whole);
        }
    }
}
