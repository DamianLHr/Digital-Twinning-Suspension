using UnityEngine;

/// <summary>
/// Base for actuators that forward commands to real hardware over USB. Live only
/// in Twinning mode (ModeManager toggles `enabled`). Subclasses hook the command
/// channel in Subscribe and call Send() to transmit an encoded packet — the
/// reverse of how Real* SENSORS receive packets via ISensorPacketSource.
/// </summary>
public abstract class RealActuatorBase : ActuatorBase, IRealDevice
{
    [Header("Real")]
    [Tooltip("Protocol channel this actuator transmits on.")]
    [SerializeField] protected byte channelId;
    [Tooltip("MonoBehaviour implementing IActuatorPacketSink (your serial layer).")]
    [SerializeField] protected MonoBehaviour packetSink;

    private IActuatorPacketSink _sink;

    public override void Initialize() { /* references resolved in OnEnable */ }

    protected virtual void OnEnable()
    {
        _sink = packetSink as IActuatorPacketSink;
        Subscribe();
    }

    protected virtual void OnDisable()
    {
        Unsubscribe();
        _sink = null;
    }

    /// <summary>Transmit a single-float command on this actuator's channel.</summary>
    protected void Send(float value)
    {
        _sink?.Send(ActuatorPacket.FromFloat(channelId, value));
    }

    protected abstract void Subscribe();
    protected abstract void Unsubscribe();
}
