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

On Windows, create a clean build in `dist` with:

```powershell
.\scripts\build.ps1
```

### Signed Windows releases

The executable must be Authenticode-signed for reliable use with Windows 11 Smart App Control. Use an RSA code-signing certificate issued by a provider trusted by Microsoft; a self-signed certificate is not sufficient for public distribution.

Sign from a PFX file (prefer passing the password through a protected CI secret rather than command history):

```powershell
.\scripts\build.ps1 `
  -CertificatePath C:\secure\enth-code-signing.pfx `
  -CertificatePassword $env:ENTH_CERTIFICATE_PASSWORD `
  -TimestampUrl https://your-ca.example/rfc3161
```

Or sign with a certificate already installed in the current user's certificate store:

```powershell
.\scripts\build.ps1 `
  -CertificateThumbprint $env:ENTH_CERTIFICATE_THUMBPRINT `
  -TimestampUrl https://your-ca.example/rfc3161
```

The script uses SHA-256 for the file digest and RFC 3161 timestamp, then verifies the signature with the Windows Authenticode policy. Never commit a PFX file or its password to this repository.

Microsoft references: [Smart App Control overview](https://learn.microsoft.com/windows/apps/develop/smart-app-control/overview), [code signing for Smart App Control](https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control), and [SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool).

### Support reply

> Thanks for reporting this. The current preview executable is unsigned, so Windows 11 Smart App Control may block it and does not offer per-app exceptions. We are preparing future Windows releases with a trusted Authenticode signature. Please do not disable Smart App Control just to run the tester. We will publish an updated signed build when it is available.

## License

This project is licensed under the **GNU General Public License v3.0 only**.

You may use, study, modify, and redistribute the tester under those terms. If you reuse the application, keeping the following credit visible would be greatly appreciated, but it is not mandatory:

```text
Copyright C F.M. Mariani - ENTHCREATIONS.COM
```

See [LICENSE](LICENSE) for the license and [ATTRIBUTION.md](ATTRIBUTION.md) for the optional attribution request.

## Copyright

Copyright C F.M. Mariani - [ENTH Creations](https://www.enthcreations.com/)
