using System;
using UnityEngine;
using UnityEngine.Events;

// If your model classes (SprungMass, TerrainWheel, WheelDriveMotor) live in a
// namespace, add the appropriate `using` here.
namespace Suspension.Sensors
{
    /// <summary>Which implementation feeds a given output (used by the binding/UI layer).</summary>
    public enum SensorSource { Digital, Real }

    // ---- Concrete UnityEvent subclasses ------------------------------------
    // Generic UnityEvent<T> only serializes (and shows in the Inspector) when
    // wrapped in a concrete, [Serializable] subclass. These are the typed
    // channels every *Output broadcasts on.
    [Serializable] public class FloatEvent      : UnityEvent<float>   {}
    [Serializable] public class FloatArrayEvent : UnityEvent<float[]> {}
    [Serializable] public class Vector3Event    : UnityEvent<Vector3> {}

    /// <summary>
    /// Gaussian noise + bias + quantization applied to digital samples so that
    /// a Digital* sensor produces a realistic, imperfect signal like its Real* sibling.
    /// </summary>
    [Serializable]
    public class NoiseProfile
    {
        [Tooltip("Std-dev of additive Gaussian noise, in sensor units.")]
        public float gaussianStdDev = 0f;
        [Tooltip("Constant offset added to every sample.")]
        public float bias = 0f;
        [Tooltip("Quantization step (0 = none).")]
        public float quantization = 0f;

        public float Apply(float v)
        {
            v += bias;
            if (gaussianStdDev > 0f) v += NextGaussian() * gaussianStdDev;
            if (quantization > 0f)   v = Mathf.Round(v / quantization) * quantization;
            return v;
        }

        public Vector3 Apply(Vector3 v) => new Vector3(Apply(v.x), Apply(v.y), Apply(v.z));

        // Box–Muller transform.
        private static float NextGaussian()
        {
            float u1 = 1f - UnityEngine.Random.value;
            float u2 = 1f - UnityEngine.Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        }
    }

    /// <summary>
    /// One framed message from the microcontroller, already de-framed by the
    /// serial layer. Payload is little-endian; use the Read* helpers.
    /// </summary>
    public readonly struct SensorPacket
    {
        public readonly byte    ChannelId;
        public readonly float   Timestamp;   // device timestamp (s)
        public readonly byte[]  Payload;

        public SensorPacket(byte channelId, float timestamp, byte[] payload)
        {
            ChannelId = channelId;
            Timestamp = timestamp;
            Payload   = payload ?? Array.Empty<byte>();
        }

        public float ReadFloat(int offset) => BitConverter.ToSingle(Payload, offset);
        public int   ReadInt(int offset)   => BitConverter.ToInt32(Payload, offset);
        public short ReadShort(int offset) => BitConverter.ToInt16(Payload, offset);
        public int   FloatCount            => Payload.Length / 4;
    }

    /// <summary>
    /// Implemented by the serial layer (e.g. SerialPortManager). Real* sensors
    /// subscribe to it by channelId in OnEnable. This is the only coupling
    /// point between the sensor family and the comms subsystem — wire your
    /// existing decoder to this interface.
    /// </summary>
    public interface ISensorPacketSource
    {
        void Subscribe(byte channelId, Action<SensorPacket> handler);
        void Unsubscribe(byte channelId, Action<SensorPacket> handler);
    }
}
