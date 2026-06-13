ENTH Latency TESTER
Version: 0.4.17 preview
Copyright C F.M. Mariani - ENTHCREATIONS.COM

This is a Windows EXE preview.
It does not launch PowerShell and integrates Raw Input sampling plus USB descriptor scanning in the executable.
Keep the logo PNG next to the EXE for the ENTH sidebar branding.
Some PCs may still require Run as administrator for complete USB hub descriptor access.

0.4.17
- Removes the unnecessary product-name specification from the executable and public documentation.

0.4.16
- Rounds the ENTH logo corners in the sidebar.

0.4.15
- Restyles the interface to match the current ENTH Signal Tester visual system.
- Adds rounded telemetry panels, holographic dark surfaces, cyan/magenta accents, and clearer metric hierarchy.
- Leaves Raw Input sampling, USB descriptor scanning, calibration, calculations, and CSV export unchanged.

0.4.14
- Resets live metrics immediately on Start and Stop.
- Keeps USB POLLING as controller identity while clearing AVG/MIN/MAX/report activity between runs.

0.4.13
- Fixes the Start button label so it switches from Calibrating to Live as soon as calibration ends, even with no idle reports.

0.4.12
- Calibration now ends on real elapsed time, even if a static fightstick sends no idle reports.
- Sample time uses the test timer instead of waiting for the next Raw Input report.

0.4.11
- Counts input activation only; release-to-idle transitions no longer increment INPUT CHANGES.
- Keeps the 12 ms coalescing window for duplicate press reports.

0.4.10
- Coalesces repeated input-change reports within 12 ms so one button action is counted once.
- Raw report stream and WIN REPORT RATE remain untouched.

0.4.9
- Prevents stable button bytes from being swallowed by idle range margins.
- Only bytes that actually move during calibration are treated as idle range/noise.

0.4.8
- Replaces byte masking with idle range learning so analog axes still register when moved outside their calibrated idle range.

0.4.7
- Makes idle calibration the general test flow.
- Shows CALIBRATION / do not touch messaging before LIVE.
- Adds idle_calibrating and idle_masked_bytes columns to CSV export.

0.4.6
- Adds automatic idle-byte learning during the first 1.2 seconds of each test.
- Filters self-changing PS5-style report bytes such as timestamps/IMU/status from INPUT CHANGES.

0.4.5
- Makes the controller identity sidebar explicit: device type, USB ID, USB polling, firmware/profile.

0.4.4
- Narrows the analog drift filter so button bitfield changes are counted again.
- Only small centered analog-axis jitter is ignored as idle noise.

0.4.3
- Adds inferred firmware/profile information from VID/PID.
- Adds USB version and device release from the USB device descriptor where available.
- Saves the extra identity fields in usb-polling-descriptors.csv.

0.4.2
- Shows filtered input changes instead of raw report count in the main grid.
- Widens analog idle filtering for LS/RS drift around center and rest positions.

0.4.1
- Adds a small analog drift filter for LS/RS idle noise.
- Raw reports are still counted, but tiny centered analog changes no longer trigger input activity.
