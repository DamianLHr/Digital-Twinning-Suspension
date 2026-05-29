using UnityEngine;

namespace Suspension.Sensors
{
    /// <summary>
    /// Wheel-speed from encoder ticks over USB. Payload: int32 cumulative tick
    /// count. Speed is derived from tick delta over packet-timestamp delta.
    /// </summary>
    public class RealWheelSpeedSensor : RealSensorBase
    {
        [Header("Wheel speed (real) — encoder")]
        [SerializeField] private int   ticksPerRev        = 2048;
        [SerializeField] private float wheelCircumference = 0.4f;  // m
        [SerializeField] private WheelSpeedOutput wheelOutput;

        private int   _lastTicks;
        private float _lastTime;
        private bool  _primed;

        protected override void Decode(SensorPacket packet)
        {
            if (wheelOutput == null || packet.Payload.Length < 4) return;

            int   ticks = packet.ReadInt(0);
            float now   = packet.Timestamp;

            if (_primed)
            {
                float dt = now - _lastTime;
                if (dt > 0f)
                {
                    float revs = (ticks - _lastTicks) / (float)ticksPerRev;
                    wheelOutput.Publish(revs * wheelCircumference / dt);
                }
            }

            _lastTicks = ticks;
            _lastTime  = now;
            _primed    = true;
        }
    }
}
