using System;
using UnityEngine;

/// <summary>
/// One actuation command framed for the microcontroller. The outbound mirror of
/// <see cref="SensorPacket"/>: built by a Real* actuator and handed to the
/// serial layer for transmission. Payload is little-endian.
/// </summary>
public readonly struct ActuatorPacket
{
    public readonly byte   ChannelId;
    public readonly float  Timestamp;   // host timestamp (s)
    public readonly byte[] Payload;

    public ActuatorPacket(byte channelId, float timestamp, byte[] payload)
    {
        ChannelId = channelId;
        Timestamp = timestamp;
        Payload   = payload ?? Array.Empty<byte>();
    }

    /// <summary>Convenience: a single little-endian float payload.</summary>
    public static ActuatorPacket FromFloat(byte channelId, float value)
        => new ActuatorPacket(channelId, Time.time, BitConverter.GetBytes(value));

    /// <summary>Convenience: a single little-endian int payload.</summary>
    public static ActuatorPacket FromInt(byte channelId, int value)
        => new ActuatorPacket(channelId, Time.time, BitConverter.GetBytes(value));
}

/// <summary>
/// Implemented by the serial layer — the outbound counterpart to
/// <see cref="ISensorPacketSource"/>. Real* actuators call Send to transmit a
/// command to the microcontroller. Wire your serial writer to this interface;
/// it is the only coupling point between the actuator family and the comms
/// subsystem.
/// </summary>
public interface IActuatorPacketSink
{
    void Send(ActuatorPacket packet);
}
