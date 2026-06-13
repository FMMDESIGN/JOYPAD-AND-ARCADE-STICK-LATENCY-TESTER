# ENTH Latency Tester Native

ENTH Latency Tester Native is a Windows tool for analyzing controller USB polling rates, Raw Input report intervals, input activity, stability, and latency-related telemetry.

Designed for arcade sticks, fightsticks, gamepads, and fighting-game controllers.

## Features

- Reads the USB polling rate declared by the controller endpoint.
- Samples HID reports through Windows Raw Input.
- Displays average, minimum, and maximum report intervals.
- Tracks filtered input changes and report-rate stability.
- Automatically learns idle noise, timestamps, status bytes, and analog drift during calibration.
- Exports captured telemetry to CSV.

## Usage

1. Keep `ENTH Latency TESTER Native.exe` and `ENTH LOGO 2025 WHITE.png` in the same folder.
2. Connect the controller and press a button or move a stick.
3. Select **Refresh USB** if the controller is not identified automatically.
4. Select **Start test** and do not touch the controller during calibration.
5. Test the inputs and export the results when needed.

Some systems may require **Run as administrator** for complete access to USB hub descriptors.

## Measurement Note

USB polling is read from the endpoint `bInterval`. Raw Input telemetry represents reports delivered by Windows and does not by itself measure the controller's complete end-to-end input latency.

## Build

The application is implemented as a standalone C# Windows Forms source file:

```text
src/ENTHLatencyTesterNative.cs
```

Current release: **0.4.16 native preview**

## Copyright

Copyright C F.M. Mariani - [ENTH Creations](https://www.enthcreations.com/)
