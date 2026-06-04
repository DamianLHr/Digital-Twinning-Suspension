using System;
using UnityEngine;

/// <summary>
/// Base for sensors fed by real hardware over USB. Subscribes to an
/// <see cref="ISensorPacketSource"/> by channelId and decodes each packet
/// into the shared output.
/// </summary>
public abstract class RealSensorBase : SensorBase
{
    [Header("Real")]
    [Tooltip("Protocol channel this sensor listens on.")]
    [SerializeField] protected byte channelId;
    [Tooltip("MonoBehaviour implementing ISensorPacketSource (your serial layer). " +
             "If left empty, call OnPacketReceived() manually.")]
    [SerializeField] protected MonoBehaviour packetSource;

    private ISensorPacketSource _source;

    public override void Initialize() { /* references resolved in OnEnable */ }

    protected virtual void OnEnable()
    {
        _source = packetSource as ISensorPacketSource;
        _source?.Subscribe(channelId, OnPacketReceived);
    }

    protected virtual void OnDisable()
    {
        _source?.Unsubscribe(channelId, OnPacketReceived);
        _source = null;
    }

    /// <summary>Entry point for the serial layer. Filters by channel, then decodes.</summary>
    public void OnPacketReceived(SensorPacket packet)
    {
        if (packet.ChannelId != channelId) return;
        Decode(packet);
    }

    /// <summary>Parse a packet payload and publish to the output.</summary>
    protected abstract void Decode(SensorPacket packet);
}
