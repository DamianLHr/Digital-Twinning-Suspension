using System;
using System.Runtime.InteropServices;

/// <summary>
/// Wire format for the Raspberry Pi Pico link. Structs are laid out to match the
/// firmware exactly ([StructLayout(Sequential, Pack=1)]); both ends are
/// little-endian so the raw bytes round-trip without conversion.
///
/// Inbound  (Pico → PC): header 0xAA 0xBB + TwinData
/// Outbound (PC → Pico): header 0xCC 0xDD + CommandData
///
/// ASSUMPTIONS (confirm with firmware):
///   • packet_id is a leading uint32 that increments every packet (loss detection).
///   • accel_* are in g (multiply by 9.81 for m/s²).
///   • belt_command units are device-specific (mapped on the actuator side).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TwinData
{
    public uint  packet_id;                 // ++ each packet
    public float accel_x, accel_y, accel_z; // g
    public int   distance_mm;               // ToF, millimetres
    public int   analog_1_raw, analog_2_raw;// pots, 0..4096
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CommandData
{
    public int target_steps;   // damping stepper position (PC maps c → steps)
    public int belt_command;   // belt speed command (units device-specific)
}

/// <summary>Channel ids shared by the transport and the Real* devices. Set the
/// matching channelId on each Real* sensor/actuator in the Inspector.</summary>
public static class PicoChannels
{
    // Inbound (sensors)
    public const byte Accel = 1;
    public const byte ToF   = 2;
    public const byte Pot1  = 3;
    public const byte Pot2  = 4;
    // Outbound (actuators)
    public const byte Damping = 10;
    public const byte Belt    = 11;
}

/// <summary>Framing constants + struct⇄bytes marshaling helpers.</summary>
public static class PicoProtocol
{
    public static readonly byte[] InHeader  = { 0xAA, 0xBB };
    public static readonly byte[] OutHeader = { 0xCC, 0xDD };

    public static byte[] StructToBytes<T>(T value) where T : struct
    {
        int len = Marshal.SizeOf<T>();
        var arr = new byte[len];
        IntPtr ptr = Marshal.AllocHGlobal(len);
        try { Marshal.StructureToPtr(value, ptr, false); Marshal.Copy(ptr, arr, 0, len); }
        finally { Marshal.FreeHGlobal(ptr); }
        return arr;
    }

    public static T BytesToStruct<T>(byte[] bytes) where T : struct
    {
        var h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { return Marshal.PtrToStructure<T>(h.AddrOfPinnedObject()); }
        finally { h.Free(); }
    }

    public static int SizeOf<T>() where T : struct => Marshal.SizeOf<T>();

    /// <summary>Frame an outbound command: OutHeader followed by the packed struct.</summary>
    public static byte[] FrameCommand(CommandData cmd)
    {
        byte[] payload = StructToBytes(cmd);
        var frame = new byte[OutHeader.Length + payload.Length];
        Buffer.BlockCopy(OutHeader, 0, frame, 0, OutHeader.Length);
        Buffer.BlockCopy(payload, 0, frame, OutHeader.Length, payload.Length);
        return frame;
    }
}
