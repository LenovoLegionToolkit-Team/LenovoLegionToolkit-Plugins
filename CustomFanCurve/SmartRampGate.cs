using System;
using System.Collections.Concurrent;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    // Smart Auto Ramp Gate: Ignores short demand spikes, ramps up smoothly once demand is sustained,
    // and holds the current speed briefly before gliding down when demand drops.
    internal class SmartRampGate
    {
        private class FanState
        {
            public double OutputRpm;
            public long LastTick;
            public long RiseSince;
            public long FallSince;
            public bool IsRampingUp;
            public bool IsRampingDown;
            public long LastRampStart;
            public long PrevRampStart;
        }

        // Demand changes smaller than this are treated as noise and never start a timer.
        private const int DeadbandRpm = 100;
        private const double MaxStepSeconds = 2.0;

        private readonly ConcurrentDictionary<int, FanState> _states = new();

        public int Apply(int fanId, int demandRpm, int currentRpm, int minRpm, float temperature, bool isEmergency, long now, CustomFanCurveSettings settings)
        {
            // Thermal Urgency: The hotter it runs, the shorter the spike wait and the faster the ramp-up (0 = cool, 1 = hot).
            double urgencyRange = settings.SmartUrgencyFullTemp - settings.SmartUrgencyStartTemp;
            double urgency = urgencyRange > 0
                ? Math.Clamp((temperature - settings.SmartUrgencyStartTemp) / urgencyRange, 0, 1)
                : (temperature >= settings.SmartUrgencyFullTemp ? 1 : 0);
            double spikeIgnoreSeconds = settings.SmartSpikeIgnoreSeconds * (1 - urgency);
            double rampUpRpmPerSec = settings.SmartRampUpRpmPerSec + (settings.SmartUrgentRampUpRpmPerSec - settings.SmartRampUpRpmPerSec) * urgency;

            if (!_states.TryGetValue(fanId, out var s))
            {
                // Start from what the fan is actually doing so the first cycle never causes a jump.
                s = new FanState { OutputRpm = currentRpm > 0 ? currentRpm : demandRpm, LastTick = now };
                _states[fanId] = s;
            }

            double dtSeconds = Math.Clamp((now - s.LastTick) / (double)TimeSpan.TicksPerSecond, 0, MaxStepSeconds);
            s.LastTick = now;

            // Emergency: thermal safety must never wait for the spike filter.
            if (isEmergency && demandRpm > s.OutputRpm)
            {
                s.OutputRpm = demandRpm;
                s.RiseSince = 0;
                s.FallSince = 0;
                s.IsRampingUp = false;
                s.IsRampingDown = false;
                return demandRpm;
            }

            if (demandRpm > s.OutputRpm + DeadbandRpm)
            {
                s.FallSince = 0;
                s.IsRampingDown = false;
                if (!s.IsRampingUp)
                {
                    if (s.RiseSince == 0) s.RiseSince = now;
                    // A single hot reading is never enough: the rise must still be there on a later cycle.
                    if (now > s.RiseSince && now - s.RiseSince >= TimeSpan.FromSeconds(spikeIgnoreSeconds).Ticks)
                    {
                        s.IsRampingUp = true;
                        s.PrevRampStart = s.LastRampStart;
                        s.LastRampStart = now;
                        // A fan cannot spin below its minimum, so a ramp from standstill starts there.
                        if (s.OutputRpm < minRpm) s.OutputRpm = minRpm;
                    }
                }

                if (s.IsRampingUp)
                {
                    // Fast urgent ramps are for leaving a quiet state (e.g. a game starting); within a sustained load, rises stay gentle.
                    if (IsSustained(s, now, settings)) rampUpRpmPerSec = Math.Min(rampUpRpmPerSec, Math.Max(settings.SmartSustainedRampUpRpmPerSec, settings.SmartRampUpRpmPerSec));
                    s.OutputRpm = Math.Min(demandRpm, s.OutputRpm + rampUpRpmPerSec * dtSeconds);
                }
            }
            else
            {
                // Demand is no longer above the fan speed: stop any ramp-up right where it is.
                s.RiseSince = 0;
                s.IsRampingUp = false;

                // Once a glide has started it continues all the way down to demand, ignoring the deadband.
                if (demandRpm < s.OutputRpm - (s.IsRampingDown ? 0 : DeadbandRpm))
                {
                    if (s.FallSince == 0) s.FallSince = now;
                    // Sustained Load: Repeated ramp-ups (e.g. gaming) mean the load keeps returning, so hold longer and fall slowly between peaks.
                    bool isSustained = IsSustained(s, now, settings);
                    double holdSeconds = isSustained ? Math.Max(settings.SmartSustainedHoldSeconds, settings.SmartDecreaseHoldSeconds) : settings.SmartDecreaseHoldSeconds;
                    int rampDownRpmPerSec = isSustained ? Math.Min(settings.SmartSustainedRampDownRpmPerSec, settings.SmartRampDownRpmPerSec) : settings.SmartRampDownRpmPerSec;
                    if (now - s.FallSince >= TimeSpan.FromSeconds(holdSeconds).Ticks)
                    {
                        s.IsRampingDown = true;
                        s.OutputRpm = Math.Max(demandRpm, s.OutputRpm - rampDownRpmPerSec * dtSeconds);
                    }
                }
                else
                {
                    s.FallSince = 0;
                    s.IsRampingDown = false;
                }
            }

            return (int)Math.Round(s.OutputRpm);
        }

        private static bool IsSustained(FanState s, long now, CustomFanCurveSettings settings) =>
            s.PrevRampStart > 0 && now - s.PrevRampStart <= TimeSpan.FromSeconds(settings.SmartSustainedWindowSeconds).Ticks;

        public void Reset(int fanId) => _states.TryRemove(fanId, out _);

        public void Clear() => _states.Clear();
    }
}
