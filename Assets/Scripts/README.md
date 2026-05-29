# Suspension Sensor Subsystem (Unity / C#)

All sensor classes implemented as MonoBehaviours, following the three-script
pattern from the class diagram: a Digital* sensor (samples Unity physics), a
Real* sensor (decodes USB packets), and a *Output (broadcasts via UnityEvent).
Digital and Real implementations write to the SAME *Output instance — that
shared output is the swap point that lets you mix real and simulated sensors.

## Layout
- Core/      SensorBase, DigitalSensorBase, RealSensorBase, SensorSupport
- Outputs/   SensorOutputBase + ToF/Accelerometer/Gyro/Position/WheelSpeed outputs
- Digital/   DigitalToFSensor, DigitalIMU, DigitalPotentiometer, DigitalWheelSpeedSensor
- Real/      RealToFSensor (VL53L0X), RealIMU (MPU-6050), RealPotentiometer (10k), RealWheelSpeedSensor

## Wiring (per output)
1. Add the *Output component to a GameObject.
2. Add EITHER the Digital* OR the Real* sensor and point its output field at
   that same *Output.
3. Consumers (e.g. the damping searcher) subscribe to the output's UnityEvent
   (OnBumpProfile / OnAcceleration / OnAngularVelocity / OnPosition / OnWheelSpeed).
   They never poll.

## Integration notes
- Sensors reference your existing model classes (SprungMass, TerrainWheel,
  WheelDriveMotor). If those live in a namespace, add a `using` in the relevant
  files. DigitalIMU expects SprungMass.GetAngularVelocity() (rad/s) — add it as a
  one-liner returning Rigidbody.angularVelocity if absent.
- Real* sensors implement no transport themselves. Make your SerialPortManager
  implement ISensorPacketSource (in SensorSupport.cs) and assign it to each
  Real* sensor's `packetSource`; sensors subscribe by channelId. Or call
  OnPacketReceived(packet) directly.
- RealPotentiometer is real-only by design; use two instances (L/R) on separate channels.

## Model classes (Model/)
- SprungMass         — Rigidbody-backed (composition). Add a Rigidbody; mass set from field.
- TerrainWheel       — kinematic road belt + profile lookahead (no Rigidbody).
- WheelDriveMotor    — belt-speed controller + encoder (no Rigidbody); references a TerrainWheel.
Namespace Suspension.Model; the digital sensors `using Suspension.Model;`.
