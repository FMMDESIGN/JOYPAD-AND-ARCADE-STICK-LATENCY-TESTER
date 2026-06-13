# ENTH Latency Tester

ENTH Latency Tester is a Windows tool for analyzing controller USB polling rates, Raw Input report intervals, input activity, stability, and latency-related telemetry.

Designed for arcade sticks, fightsticks, gamepads, and fighting-game controllers.

## Features

- Reads the USB polling rate declared by the controller endpoint.
- Samples HID reports through Windows Raw Input.
- Displays average, minimum, and maximum report intervals.
- Tracks filtered input changes and report-rate stability.
- Automatically learns idle noise, timestamps, status bytes, and analog drift during calibration.
- Exports captured telemetry to CSV.

## Usage

1. Keep `ENTH Latency TESTER.exe` and `ENTH LOGO 2025 WHITE.png` in the same folder.
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
src/ENTHLatencyTester.cs
```

Current release: **0.4.18 preview**

## License

This project is licensed under the **GNU General Public License v3.0 only**, with an additional attribution requirement permitted by GPLv3 section 7(b).

You may use, study, modify, and redistribute the tester under those terms. Modified or redistributed versions with an interactive interface must keep the following notice clearly visible:

```text
Copyright C F.M. Mariani - ENTHCREATIONS.COM
```

See [LICENSE](LICENSE) and [ADDITIONAL-TERMS.md](ADDITIONAL-TERMS.md) for the complete terms.

## Copyright

Copyright C F.M. Mariani - [ENTH Creations](https://www.enthcreations.com/)
