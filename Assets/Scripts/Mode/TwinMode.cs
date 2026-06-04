/// <summary>
/// Global operating mode of the rig. Simulating = the Unity physics model is the
/// source of truth and the digital devices are live. Twinning = real hardware is
/// the source of truth and the real devices are live; the model mirrors it.
///
/// Components never read this themselves. The single authority is
/// <see cref="ModeManager"/>, which enables/disables the relevant devices and
/// pushes the mode to any <see cref="IModeReceiver"/>.
/// </summary>
public enum TwinMode { Simulating, Twinning }

/// <summary>Marker for devices that should be live only in Simulating mode.</summary>
public interface IDigitalDevice { }

/// <summary>Marker for devices that should be live only in Twinning mode.</summary>
public interface IRealDevice { }

/// <summary>
/// Implemented by components whose behaviour depends on the mode but which are
/// NOT simply enabled/disabled (e.g. a visualizer that displays in both modes
/// but, in Twinning, also drives the model). ModeManager calls OnModeChanged on
/// these so the decision still lives with the manager, not the component.
/// </summary>
public interface IModeReceiver { void OnModeChanged(TwinMode mode); }
