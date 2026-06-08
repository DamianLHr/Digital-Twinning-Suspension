using System;
using UnityEngine;

/// <summary>
/// Single source of truth for each Pico channel's PAYLOAD byte layout. Shared by
/// <see cref="PicoSerialTransport"/> (which encodes when demuxing a TwinData into
/// per-channel SensorPackets) and the Real* sensors (which decode). Keeping both
/// sides here means the wire-payload contract can't silently drift between them.
///
/// Physical interpretation (units, ranges, calibration) stays in the sensors;
/// this class only deals with the raw byte layout per channel.
/// </summary>
public static class PicoChannelCodec
{
    // --- Accelerometer (PicoChannels.Accel): three little-endian floats, in g ---
    public static byte[] EncodeAccel(float gx, float gy, float gz)
    {
        var b = new byte[12];
        Buffer.BlockCopy(BitConverter.GetBytes(gx), 0, b, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(gy), 0, b, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(gz), 0, b, 8, 4);
        return b;
    }

    /// <summary>Acceleration in g (multiply by 9.81 for m/s²).</summary>
    public static Vector3 DecodeAccelG(SensorPacket p) =>
        new Vector3(p.ReadFloat(0), p.ReadFloat(4), p.ReadFloat(8));

    // --- ToF (PicoChannels.ToF): one little-endian int32, in millimetres ---
    public static byte[] EncodeToFMm(int mm) => BitConverter.GetBytes(mm);
    public static int DecodeToFMm(SensorPacket p) => p.ReadInt(0);

    // --- Potentiometer (PicoChannels.Pot1/Pot2): one little-endian int32 raw ADC ---
    public static byte[] EncodePotRaw(int raw) => BitConverter.GetBytes(raw);
    public static int DecodePotRaw(SensorPacket p) => p.ReadInt(0);
}
