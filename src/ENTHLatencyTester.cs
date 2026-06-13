using System;
// ENTH Latency Tester
// Copyright C F.M. Mariani - ENTHCREATIONS.COM
// SPDX-License-Identifier: GPL-3.0-only
// Optional attribution guidance is available in ATTRIBUTION.md.

using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("ENTH Latency Tester")]
[assembly: AssemblyProduct("ENTH Latency Tester")]
[assembly: AssemblyCompany("ENTHCREATIONS.COM")]
[assembly: AssemblyCopyright("Copyright C F.M. Mariani - ENTHCREATIONS.COM")]
[assembly: AssemblyVersion("0.4.18.0")]
[assembly: AssemblyFileVersion("0.4.18.0")]


public static class RawInputNative {
    public const int WM_INPUT = 0x00FF;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000B;
    public const uint RIM_TYPEHID = 2;
    public const uint RIDEV_INPUTSINK = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST {
        public IntPtr hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_HID {
        public uint dwVendorId;
        public uint dwProductId;
        public uint dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO {
        public uint cbSize;
        public uint dwType;
        public RID_DEVICE_INFO_HID hid;
    }

    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Auto)]
    public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, StringBuilder pData, ref uint pcbSize);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    public static string ActiveHidVidPids() {
        uint count = 0;
        uint listSize = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
        GetRawInputDeviceList(null, ref count, listSize);
        if (count == 0) return "";

        RAWINPUTDEVICELIST[] devices = new RAWINPUTDEVICELIST[count];
        if (GetRawInputDeviceList(devices, ref count, listSize) == UInt32.MaxValue) return "";

        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++) {
            if (devices[i].dwType != RIM_TYPEHID) continue;

            uint infoSize = (uint)Marshal.SizeOf(typeof(RID_DEVICE_INFO));
            IntPtr infoPtr = Marshal.AllocHGlobal((int)infoSize);
            bool isGameInput = false;
            try {
                Marshal.WriteInt32(infoPtr, (int)infoSize);
                if (GetRawInputDeviceInfo(devices[i].hDevice, RIDI_DEVICEINFO, infoPtr, ref infoSize) != UInt32.MaxValue) {
                    RID_DEVICE_INFO info = (RID_DEVICE_INFO)Marshal.PtrToStructure(infoPtr, typeof(RID_DEVICE_INFO));
                    isGameInput = info.hid.usUsagePage == 0x01 && (info.hid.usUsage == 0x04 || info.hid.usUsage == 0x05);
                }
            } finally {
                Marshal.FreeHGlobal(infoPtr);
            }
            if (!isGameInput) continue;

            uint nameChars = 0;
            GetRawInputDeviceInfo(devices[i].hDevice, RIDI_DEVICENAME, null, ref nameChars);
            if (nameChars == 0) continue;

            StringBuilder name = new StringBuilder((int)nameChars + 1);
            if (GetRawInputDeviceInfo(devices[i].hDevice, RIDI_DEVICENAME, name, ref nameChars) == UInt32.MaxValue) continue;

            string text = name.ToString().ToUpperInvariant();
            int vidIndex = text.IndexOf("VID_");
            int pidIndex = text.IndexOf("PID_");
            if (vidIndex < 0 || pidIndex < 0 || vidIndex + 8 > text.Length || pidIndex + 8 > text.Length) continue;
            string vid = text.Substring(vidIndex, 8);
            string pid = text.Substring(pidIndex, 8);
            ids.Add(vid + " " + pid);
        }

        return String.Join("|", ids);
    }

    public static string VidPidFromRawDevice(IntPtr hDevice) {
        uint nameChars = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, null, ref nameChars);
        if (nameChars == 0) return "";

        StringBuilder name = new StringBuilder((int)nameChars + 1);
        if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, name, ref nameChars) == UInt32.MaxValue) return "";

        string text = name.ToString().ToUpperInvariant();
        int vidIndex = text.IndexOf("VID_");
        int pidIndex = text.IndexOf("PID_");
        if (vidIndex < 0 || pidIndex < 0 || vidIndex + 8 > text.Length || pidIndex + 8 > text.Length) return "";
        return text.Substring(vidIndex, 8) + " " + text.Substring(pidIndex, 8);
    }
}

public class HidReportSampler {
    private readonly object gate = new object();
    private readonly Stopwatch runWatch = new Stopwatch();
    private readonly List<double> reportIntervals = new List<double>();
    private readonly List<string> rows = new List<string>();
    private readonly HashSet<IntPtr> devices = new HashSet<IntPtr>();
    private readonly Dictionary<IntPtr, double> lastReportByDevice = new Dictionary<IntPtr, double>();
    private readonly Dictionary<IntPtr, int> lastReportHashByDevice = new Dictionary<IntPtr, int>();
    private readonly Dictionary<IntPtr, byte[]> lastReportBytesByDevice = new Dictionary<IntPtr, byte[]>();
    private readonly Dictionary<IntPtr, IdleProfile> idleProfileByDevice = new Dictionary<IntPtr, IdleProfile>();
    private readonly Dictionary<IntPtr, double> lastAcceptedInputChangeByDevice = new Dictionary<IntPtr, double>();
    private bool running;
    private double sampleStartMs;
    private double firstReportMs;
    private double lastReportMs;
    private long rawReportCount;
    private long changedReportCount;
    private string lastVidPid = "";

    public bool Running { get { return running; } }
    public double MinPlausibleReportIntervalMs { get; set; }
    public double MaxPlausibleReportIntervalMs { get; set; }
    public double InputChangeCoalesceMs { get; set; }

    public HidReportSampler() {
        MinPlausibleReportIntervalMs = 0.05;
        MaxPlausibleReportIntervalMs = Double.PositiveInfinity;
        InputChangeCoalesceMs = 12.0;
    }

    public void TouchDevice(string vidPid) {
        lock (gate) {
            if (!String.IsNullOrWhiteSpace(vidPid)) lastVidPid = vidPid;
        }
    }

    public void Start() {
        lock (gate) {
            reportIntervals.Clear();
            rows.Clear();
            devices.Clear();
            lastReportByDevice.Clear();
            lastReportHashByDevice.Clear();
            lastReportBytesByDevice.Clear();
            idleProfileByDevice.Clear();
            lastAcceptedInputChangeByDevice.Clear();
            rows.Add("time_ms,device_handle,report_interval_ms,report_size_bytes,content_changed,stats_accepted,idle_calibrating,idle_masked_bytes");
            sampleStartMs = Double.NaN;
            firstReportMs = Double.NaN;
            lastReportMs = Double.NaN;
            rawReportCount = 0;
            changedReportCount = 0;
            lastVidPid = "";
            runWatch.Restart();
            running = true;
        }
    }

    public void Stop() {
        lock (gate) {
            running = false;
            runWatch.Stop();
        }
    }

    public void Record(IntPtr device, double timeMs, int reportSizeBytes, string vidPid, int reportHash, byte[] reportBytesData) {
        lock (gate) {
            if (!running) return;

            devices.Add(device);
            if (!String.IsNullOrWhiteSpace(vidPid)) lastVidPid = vidPid;
            if (Double.IsNaN(sampleStartMs)) sampleStartMs = timeMs;
            if (Double.IsNaN(firstReportMs)) firstReportMs = timeMs;
            lastReportMs = timeMs;
            rawReportCount++;
            bool contentChanged = true;
            byte[] previousBytes;
            if (lastReportBytesByDevice.TryGetValue(device, out previousBytes)) {
                bool learningIdle = IsCalibrating();
                IdleProfile idleProfile = GetIdleProfile(device, reportBytesData);
                if (learningIdle) {
                    idleProfile.Learn(previousBytes);
                    idleProfile.Learn(reportBytesData);
                    contentChanged = false;
                } else {
                    contentChanged = IsMeaningfulChange(previousBytes, reportBytesData, idleProfile);
                }
            }
            lastReportHashByDevice[device] = reportHash;
            lastReportBytesByDevice[device] = reportBytesData;
            if (contentChanged) {
                double previousAccepted;
                if (lastAcceptedInputChangeByDevice.TryGetValue(device, out previousAccepted) && timeMs - previousAccepted < InputChangeCoalesceMs) {
                    contentChanged = false;
                } else {
                    lastAcceptedInputChangeByDevice[device] = timeMs;
                    changedReportCount++;
                }
            }

            double interval = Double.NaN;
            double previous;
            if (lastReportByDevice.TryGetValue(device, out previous)) {
                interval = timeMs - previous;
                if (interval > 0) {
                    if (interval >= MinPlausibleReportIntervalMs && interval <= MaxPlausibleReportIntervalMs) {
                        reportIntervals.Add(interval);
                        if (reportIntervals.Count > 50000) reportIntervals.RemoveRange(0, 5000);
                    }
                }
            }
            lastReportByDevice[device] = timeMs;

            rows.Add(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F3},{1},{2:F3},{3},{4},{5},{6},{7}", timeMs, device.ToInt64(), interval, reportSizeBytes, contentChanged ? 1 : 0, (!Double.IsNaN(interval) && interval >= MinPlausibleReportIntervalMs && interval <= MaxPlausibleReportIntervalMs) ? 1 : 0, IsCalibrating() ? 1 : 0, IdleMaskedByteCount()));
        }
    }

    private bool IsCalibrating() {
        return running && runWatch.IsRunning && runWatch.Elapsed.TotalMilliseconds < 1200.0;
    }

    public int IdleMaskedByteCount() {
        int total = 0;
        foreach (IdleProfile profile in idleProfileByDevice.Values) {
            if (profile != null) total += profile.ActiveByteCount;
        }
        return total;
    }

    private IdleProfile GetIdleProfile(IntPtr device, byte[] report) {
        IdleProfile profile;
        if (!idleProfileByDevice.TryGetValue(device, out profile) || profile.Length != report.Length) {
            profile = new IdleProfile(report.Length);
            profile.Learn(report);
            idleProfileByDevice[device] = profile;
        }
        return profile;
    }

    private static bool IsMeaningfulChange(byte[] previous, byte[] current, IdleProfile idleProfile) {
        if (previous == null || current == null) return true;
        int length = Math.Min(previous.Length, current.Length);
        if (previous.Length != current.Length) return true;

        bool anyDifference = false;
        bool activeCurrentState = false;
        for (int i = 0; i < length; i++) {
            int oldValue = previous[i];
            int newValue = current[i];
            int delta = Math.Abs(newValue - oldValue);
            if (delta == 0) continue;

            anyDifference = true;
            if (idleProfile != null && idleProfile.Contains(i, newValue)) continue;
            bool centeredAnalogNoise = oldValue >= 96 && oldValue <= 160 && newValue >= 96 && newValue <= 160 && delta <= 10;
            if (!centeredAnalogNoise) activeCurrentState = true;
        }
        return anyDifference && activeCurrentState;
    }

    private sealed class IdleProfile {
        private readonly int[] min;
        private readonly int[] max;
        private readonly bool[] learned;
        private const int Margin = 6;

        public int Length { get { return min.Length; } }

        public int ActiveByteCount {
            get {
                int total = 0;
                for (int i = 0; i < learned.Length; i++) if (learned[i] && min[i] != max[i]) total++;
                return total;
            }
        }

        public IdleProfile(int length) {
            min = new int[length];
            max = new int[length];
            learned = new bool[length];
        }

        public void Learn(byte[] data) {
            if (data == null) return;
            int length = Math.Min(data.Length, min.Length);
            for (int i = 0; i < length; i++) {
                int value = data[i];
                if (!learned[i]) {
                    min[i] = value;
                    max[i] = value;
                    learned[i] = true;
                } else {
                    if (value < min[i]) min[i] = value;
                    if (value > max[i]) max[i] = value;
                }
            }
        }

        public bool Contains(int index, int value) {
            if (index < 0 || index >= min.Length || !learned[index]) return false;
            int width = max[index] - min[index];
            if (width == 0) return value == min[index];
            if (width > 20) return true;
            return value >= min[index] - Margin && value <= max[index] + Margin;
        }
    }

    private static double Average(List<double> values) {
        if (values.Count == 0) return Double.NaN;
        double sum = 0;
        for (int i = 0; i < values.Count; i++) sum += values[i];
        return sum / values.Count;
    }

    private static double Percentile(List<double> source, double percentile) {
        if (source.Count == 0) return Double.NaN;
        double[] values = source.ToArray();
        Array.Sort(values);
        int index = (int)Math.Floor((values.Length - 1) * percentile);
        if (index < 0) index = 0;
        if (index >= values.Length) index = values.Length - 1;
        return values[index];
    }

    private static double Min(List<double> values) {
        if (values.Count == 0) return Double.NaN;
        double result = values[0];
        for (int i = 1; i < values.Count; i++) if (values[i] < result) result = values[i];
        return result;
    }

    private static double Max(List<double> values) {
        if (values.Count == 0) return Double.NaN;
        double result = values[0];
        for (int i = 1; i < values.Count; i++) if (values[i] > result) result = values[i];
        return result;
    }

    public string Snapshot() {
        lock (gate) {
            double avg = Average(reportIntervals);
            double min = Min(reportIntervals);
            double max = Max(reportIntervals);
            double p95 = Percentile(reportIntervals, 0.95);
            double elapsed = runWatch.IsRunning ? runWatch.Elapsed.TotalMilliseconds : (Double.IsNaN(sampleStartMs) || Double.IsNaN(lastReportMs) ? Double.NaN : Math.Max(0, lastReportMs - sampleStartMs));
            double activeSpan = Double.IsNaN(firstReportMs) || Double.IsNaN(lastReportMs) ? Double.NaN : Math.Max(0, lastReportMs - firstReportMs);
            double hz = Double.IsNaN(elapsed) || elapsed <= 0 ? Double.NaN : rawReportCount * 1000.0 / elapsed;
            double minHz = Double.IsNaN(max) || max <= 0 ? Double.NaN : 1000.0 / max;
            double maxHz = Double.IsNaN(min) || min <= 0 ? Double.NaN : 1000.0 / min;
            double stability = Double.IsNaN(avg) || avg <= 0 || Double.IsNaN(p95) ? Double.NaN : p95 / avg;
            bool reliable = rawReportCount >= 100 && elapsed >= 3000 && !Double.IsNaN(stability) && stability <= 1.35;
            bool calibrating = IsCalibrating();
            return String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3:F3}|{4:F3}|{5:F3}|{6:F3}|{7:F1}|{8:F1}|{9:F1}|{10:F3}|{11:F3}|{12}|{13}|{14}|{15}|{16}",
                running ? 1 : 0, devices.Count, rawReportCount, avg, min, max, p95, hz, minHz, maxHz, elapsed, stability, reliable ? 1 : 0, lastVidPid, changedReportCount, calibrating ? 1 : 0, IdleMaskedByteCount());
        }
    }

    public string Csv() {
        lock (gate) {
            return String.Join(Environment.NewLine, rows.ToArray());
        }
    }
}

public class RawInputForm : Form {
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public HidReportSampler Sampler { get; set; }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        RegisterHid(0x04);
        RegisterHid(0x05);
        RegisterHid(0x08);
    }

    private void RegisterHid(ushort usage) {
        RawInputNative.RAWINPUTDEVICE[] devices = new RawInputNative.RAWINPUTDEVICE[1];
        devices[0].usUsagePage = 0x01;
        devices[0].usUsage = usage;
        devices[0].dwFlags = RawInputNative.RIDEV_INPUTSINK;
        devices[0].hwndTarget = this.Handle;
        RawInputNative.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(RawInputNative.RAWINPUTDEVICE)));
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == RawInputNative.WM_INPUT && Sampler != null) {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RawInputNative.RAWINPUTHEADER));
            RawInputNative.GetRawInputData(m.LParam, RawInputNative.RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size >= headerSize) {
                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try {
                    uint read = RawInputNative.GetRawInputData(m.LParam, RawInputNative.RID_INPUT, buffer, ref size, headerSize);
                    if (read == size) {
                        RawInputNative.RAWINPUTHEADER header = (RawInputNative.RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RawInputNative.RAWINPUTHEADER));
                        if (header.dwType == RawInputNative.RIM_TYPEHID) {
                            int offset = (int)headerSize;
                            uint sizeHid = (uint)Marshal.ReadInt32(buffer, offset);
                            uint count = (uint)Marshal.ReadInt32(buffer, offset + 4);
                            int reportBytes = (int)(sizeHid * count);
                            int reportOffset = offset + 8;
                            int reportHash = 17;
                            byte[] reportBytesData = new byte[Math.Max(0, reportBytes)];
                            for (int i = 0; i < reportBytes && reportOffset + i < (int)size; i++) {
                                byte value = Marshal.ReadByte(buffer, reportOffset + i);
                                reportBytesData[i] = value;
                                reportHash = unchecked(reportHash * 31 + value);
                            }
                            string vidPid = RawInputNative.VidPidFromRawDevice(header.hDevice);
                            Sampler.TouchDevice(vidPid);
                            if (Sampler.Running) {
                                Sampler.Record(header.hDevice, watch.Elapsed.TotalMilliseconds, reportBytes, vidPid, reportHash, reportBytesData);
                            }
                        }
                    }
                } finally {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        base.WndProc(ref m);
    }
}

public class PollingDonut : Control {
    public double DeclaredHz { get; set; }
    public double ObservedHz { get; set; }
    private readonly double[] bars = new double[64];
    private readonly double[] peaks = new double[64];
    private readonly Random random = new Random();
    private double phase;

    public PollingDonut() {
        this.DoubleBuffered = true;
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        DeclaredHz = Double.NaN;
        ObservedHz = Double.NaN;
    }

    public void Pulse(double value) {
        if (Double.IsNaN(value) || value <= 0) {
            for (int i = 0; i < bars.Length; i++) {
                bars[i] *= 0.72;
                peaks[i] = Math.Max(0, peaks[i] - 0.035);
            }
            Invalidate();
            return;
        }

        double reference = (!Double.IsNaN(DeclaredHz) && DeclaredHz > 0) ? DeclaredHz : 1000.0;
        double intensity = Math.Max(0.18, Math.Min(1.0, value / reference));
        phase += 0.22 + intensity * 0.42;
        for (int i = 0; i < bars.Length; i++) {
            double bass = Math.Abs(Math.Sin(phase + i * 0.20));
            double treble = Math.Abs(Math.Sin(phase * 1.8 + i * 0.57));
            double wave = 0.22 + 0.55 * bass + 0.23 * treble;
            double jitter = 0.72 + random.NextDouble() * 0.38;
            double target = intensity * wave * jitter;
            bars[i] = Math.Max(bars[i] * 0.74, target);
            peaks[i] = Math.Max(peaks[i] - 0.018, bars[i]);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle r = ClientRectangle;
        using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Math.Max(1, r.Width - 1), Math.Max(1, r.Height - 1)), 16))
        using (LinearGradientBrush bg = new LinearGradientBrush(r, Color.FromArgb(28, 20, 46), Color.FromArgb(8, 9, 24), 120f))
        using (Pen border = new Pen(Color.FromArgb(90, 72, 200, 255))) {
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }

        using (Font small = new Font("Consolas", 8))
        using (SolidBrush text = new SolidBrush(Color.FromArgb(247, 244, 238)))
        using (SolidBrush muted = new SolidBrush(Color.FromArgb(169, 179, 199))) {
            g.DrawString("RAW INPUT SPECTRUM", small, muted, r.Left + 12, r.Top + 10);
            string usb = Double.IsNaN(DeclaredHz) ? "USB --" : "USB " + DeclaredHz.ToString("0.#") + " Hz";
            string raw = Double.IsNaN(ObservedHz) ? "ACTIVITY --" : "ACTIVITY " + ObservedHz.ToString("0.#") + " Hz";
            g.DrawString(usb + " / " + raw, small, text, r.Right - 190, r.Top + 10);
        }

        using (Pen grid = new Pen(Color.FromArgb(35, 72, 200, 255))) {
            for (int gy = r.Top + 38; gy < r.Bottom - 18; gy += 18) g.DrawLine(grid, r.Left + 14, gy, r.Right - 14, gy);
        }

        int baseY = r.Bottom - 18;
        int topY = r.Top + 34;
        int availableH = Math.Max(10, baseY - topY);
        float slot = (r.Width - 28) / (float)bars.Length;
        for (int i = 0; i < bars.Length; i++) {
            int h = Math.Max(3, (int)(bars[i] * availableH));
            int x = r.Left + 14 + (int)(i * slot);
            int w = Math.Max(3, (int)(slot * 0.62));
            int y = baseY - h;
            int hot = (int)Math.Min(255, bars[i] * 255);
            Color c = bars[i] > 0.72 ? Color.FromArgb(238, 255, 79, 216) : Color.FromArgb(228, 72, 200, 255);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(34, c))) g.FillRectangle(glow, x - 1, y - 2, w + 2, h + 4);
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
            int peakY = baseY - Math.Max(3, (int)(peaks[i] * availableH));
            using (Pen p = new Pen(Color.FromArgb(230, 247, 241, 223))) g.DrawLine(p, x, peakY, x + w, peakY);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius) {
        GraphicsPath path = new GraphicsPath();
        int diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}



public static class UsbDescriptorScanner {
    private static readonly Guid GUID_DEVINTERFACE_USB_HUB = new Guid("f18a0e88-c30c-11d0-8815-00a0c906bed8");

    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_USB_GET_NODE_INFORMATION = 0x220408;
    private const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448;
    private const uint IOCTL_USB_GET_NODE_CONNECTION_NAME = 0x220414;
    private const uint IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = 0x220410;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError=true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError=true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError=true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    public static string Scan() {
        StringBuilder output = new StringBuilder();
        StringBuilder diagnostics = new StringBuilder();
        output.AppendLine("VID,PID,Port,Speed,Endpoint,bInterval,Declared interval,Declared Hz,Note,Firmware profile,USB version,Device release");

        HashSet<string> visitedHubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> hubs = new List<string>(EnumerateHubPaths());
        diagnostics.AppendLine("Diagnostics");
        diagnostics.AppendLine("Hubs found: " + hubs.Count);
        foreach (string hubPath in hubs) {
            diagnostics.AppendLine("Hub: " + hubPath);
            ScanHub(hubPath, visitedHubs, output, diagnostics, 0);
        }

        if (output.ToString().Trim().Split('\n').Length <= 1) {
            output.AppendLine(",,,,,,,,No interrupt endpoint found or USB hub access unavailable,,,");
        }

        return output.ToString() + Environment.NewLine + diagnostics.ToString();
    }

    private static IEnumerable<string> EnumerateHubPaths() {
        List<string> paths = new List<string>();
        Guid hubGuid = GUID_DEVINTERFACE_USB_HUB;
        IntPtr info = SetupDiGetClassDevs(ref hubGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (info == IntPtr.Zero || info.ToInt64() == -1) return paths;

        try {
            for (int index = 0; ; index++) {
                SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hubGuid, index, ref data)) break;

                int required;
                SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr detailBuffer = Marshal.AllocHGlobal(required);
                try {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(info, ref data, detailBuffer, required, out required, IntPtr.Zero)) {
                        string path = Marshal.PtrToStringAuto(IntPtr.Add(detailBuffer, 4));
                        if (String.IsNullOrWhiteSpace(path)) {
                            path = Marshal.PtrToStringAuto(IntPtr.Add(detailBuffer, IntPtr.Size == 8 ? 8 : 4));
                        }
                        if (!String.IsNullOrWhiteSpace(path)) paths.Add(path);
                    }
                } finally {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(info);
        }

        return paths;
    }

    private static void ScanHub(string hubPath, HashSet<string> visitedHubs, StringBuilder output, StringBuilder diagnostics, int depth) {
        if (String.IsNullOrWhiteSpace(hubPath) || visitedHubs.Contains(hubPath)) return;
        visitedHubs.Add(hubPath);

        using (SafeFileHandle hub = CreateFile(hubPath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero)) {
            if (hub == null || hub.IsInvalid) {
                diagnostics.AppendLine("  Apertura hub fallita");
                return;
            }

            int ports = GetPortCount(hub);
            diagnostics.AppendLine("  Porte viste: " + ports);
            for (int port = 1; port <= ports; port++) {
                byte[] deviceDescriptor = GetDescriptor(hub, port, 0x01, 0, 18);
                byte[] configDescriptor = GetDescriptor(hub, port, 0x02, 0, 4096);
                if (deviceDescriptor != null && configDescriptor != null && configDescriptor.Length >= 9) {
                    ushort vidDirect = BitConverter.ToUInt16(deviceDescriptor, 8);
                    ushort pidDirect = BitConverter.ToUInt16(deviceDescriptor, 10);
                    ushort bcdUsbDirect = BitConverter.ToUInt16(deviceDescriptor, 2);
                    ushort bcdDeviceDirect = BitConverter.ToUInt16(deviceDescriptor, 12);
                    int endpointCount = ParseConfigEndpoints(configDescriptor, output, diagnostics, port, vidDirect, pidDirect, bcdUsbDirect, bcdDeviceDirect);
                    diagnostics.AppendLine(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "  Port {0}: descriptor VID_{1:X4} PID_{2:X4}, USB {3}, device {4}, interrupt endpoint {5}", port, vidDirect, pidDirect, BcdText(bcdUsbDirect), BcdText(bcdDeviceDirect), endpointCount));
                    continue;
                }

                byte[] connection = GetConnectionInfo(hub, port);
                if (connection == null) {
                    diagnostics.AppendLine("  Port " + port + ": info unavailable");
                    continue;
                }

                ushort vid = BitConverter.ToUInt16(connection, 12);
                ushort pid = BitConverter.ToUInt16(connection, 14);
                ushort bcdDevice = BitConverter.ToUInt16(connection, 16);
                ushort bcdUsb = BitConverter.ToUInt16(connection, 6);
                byte speed = connection[23];
                int pipeCount = BitConverter.ToInt32(connection, 28);
                string speedName = SpeedName(speed);
                int status = BitConverter.ToInt32(connection, 32);
                if ((vid == 0 && pid == 0) || pipeCount < 0 || pipeCount > 64) {
                    diagnostics.AppendLine("  Port " + port + ": status " + status + ", no plausible device endpoint");
                    continue;
                }
                diagnostics.AppendLine(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  Port {0}: VID_{1:X4} PID_{2:X4}, speed {3}, pipe {4}, status {5}", port, vid, pid, speedName, pipeCount, status));

                for (int i = 0; i < pipeCount; i++) {
                    int offset = 36 + (i * 12);
                    if (offset + 7 > connection.Length) break;
                    byte endpoint = connection[offset + 2];
                    byte attributes = connection[offset + 3];
                    byte interval = connection[offset + 6];
                    if ((attributes & 0x03) != 0x03) continue;

                    double intervalMs = EndpointIntervalMs(interval, speed);
                    double hz = intervalMs > 0 ? 1000.0 / intervalMs : Double.NaN;
                    string note = bcdUsb >= 0x0200 ? "interrupt endpoint" : "interrupt endpoint";

                    output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "VID_{0:X4},PID_{1:X4},{2},{3},0x{4:X2},{5},{6:0.###} ms,{7:0.#} Hz,{8},{9},{10},{11}",
                        vid, pid, port, speedName, endpoint, interval, intervalMs, hz, note, FirmwareProfile(vid, pid), BcdText(bcdUsb), BcdText(bcdDevice));
                    output.AppendLine();
                }

                string childHub = GetChildHubPath(hub, port);
                if (!String.IsNullOrWhiteSpace(childHub)) ScanHub(childHub, visitedHubs, output, diagnostics, depth + 1);
            }
        }
    }

    private static int GetPortCount(SafeFileHandle hub) {
        IntPtr native = Marshal.AllocHGlobal(256);
        int returned;
        try {
            ZeroMemory(native, 256);
            Marshal.WriteInt32(native, 0);
            if (!DeviceIoControl(hub, IOCTL_USB_GET_NODE_INFORMATION, native, 256, native, 256, out returned, IntPtr.Zero)) return 0;
            if (returned < 7) return 0;
            return Marshal.ReadByte(native, 6);
        } finally {
            Marshal.FreeHGlobal(native);
        }
    }

    private static byte[] GetConnectionInfo(SafeFileHandle hub, int port) {
        int size = 4096;
        IntPtr native = Marshal.AllocHGlobal(size);
        int returned;
        try {
            ZeroMemory(native, size);
            Marshal.WriteInt32(native, port);
            if (!DeviceIoControl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, native, size, native, size, out returned, IntPtr.Zero)) return null;
            if (returned < 36) return null;
            byte[] buffer = new byte[returned];
            Marshal.Copy(native, buffer, 0, returned);
            return buffer;
        } finally {
            Marshal.FreeHGlobal(native);
        }
    }

    private static byte[] GetDescriptor(SafeFileHandle hub, int port, byte descriptorType, byte descriptorIndex, ushort requestedLength) {
        int size = 12 + Math.Max(256, (int)requestedLength);
        IntPtr native = Marshal.AllocHGlobal(size);
        int returned;
        try {
            ZeroMemory(native, size);
            Marshal.WriteInt32(native, port);
            Marshal.WriteByte(native, 4, 0x80);
            Marshal.WriteByte(native, 5, 0x06);
            Marshal.WriteInt16(native, 6, (short)((descriptorType << 8) | descriptorIndex));
            Marshal.WriteInt16(native, 8, 0);
            Marshal.WriteInt16(native, 10, (short)requestedLength);

            if (!DeviceIoControl(hub, IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, native, size, native, size, out returned, IntPtr.Zero)) return null;
            if (returned <= 12) return null;
            int dataLength = returned - 12;
            byte[] data = new byte[dataLength];
            Marshal.Copy(IntPtr.Add(native, 12), data, 0, dataLength);
            return data;
        } finally {
            Marshal.FreeHGlobal(native);
        }
    }

    private static int ParseConfigEndpoints(byte[] config, StringBuilder output, StringBuilder diagnostics, int port, ushort vid, ushort pid, ushort bcdUsb, ushort bcdDevice) {
        int found = 0;
        int totalLength = config.Length >= 4 ? BitConverter.ToUInt16(config, 2) : config.Length;
        if (totalLength <= 0 || totalLength > config.Length) totalLength = config.Length;

        int offset = 0;
        while (offset + 2 <= totalLength) {
            int length = config[offset];
            int type = config[offset + 1];
            if (length <= 0 || offset + length > totalLength) break;

            if (type == 0x05 && length >= 7) {
                byte endpoint = config[offset + 2];
                byte attributes = config[offset + 3];
                byte interval = config[offset + 6];
                if ((attributes & 0x03) == 0x03) {
                    double fullSpeedMs = interval == 0 ? Double.NaN : interval;
                    double fullSpeedHz = fullSpeedMs > 0 ? 1000.0 / fullSpeedMs : Double.NaN;
                    double highSpeedMs = interval == 0 ? Double.NaN : Math.Pow(2, Math.Max(0, interval - 1)) * 0.125;
                    double highSpeedHz = highSpeedMs > 0 ? 1000.0 / highSpeedMs : Double.NaN;
                    string note = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "interrupt endpoint; Full/Low speed {0:0.###} ms {1:0.#} Hz; High speed {2:0.###} ms {3:0.#} Hz",
                        fullSpeedMs, fullSpeedHz, highSpeedMs, highSpeedHz);
                    output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "VID_{0:X4},PID_{1:X4},{2},{3},0x{4:X2},{5},{6:0.###} ms,{7:0.#} Hz,{8},{9},{10},{11}",
                        vid, pid, port, "Descriptor", endpoint, interval, fullSpeedMs, fullSpeedHz, note, FirmwareProfile(vid, pid), BcdText(bcdUsb), BcdText(bcdDevice));
                    output.AppendLine();
                    found++;
                }
            }

            offset += length;
        }
        return found;
    }

    private static string GetChildHubPath(SafeFileHandle hub, int port) {
        int size = 1024;
        IntPtr native = Marshal.AllocHGlobal(size);
        int returned;
        try {
            ZeroMemory(native, size);
            Marshal.WriteInt32(native, port);
            if (!DeviceIoControl(hub, IOCTL_USB_GET_NODE_CONNECTION_NAME, native, size, native, size, out returned, IntPtr.Zero)) return null;
            if (returned < 10) return null;

            byte[] buffer = new byte[returned];
            Marshal.Copy(native, buffer, 0, returned);
            int actualLength = BitConverter.ToInt32(buffer, 4);
            if (actualLength <= 8 || actualLength > buffer.Length) return null;
            string name = Encoding.Unicode.GetString(buffer, 8, actualLength - 8).TrimEnd('\0');
            if (String.IsNullOrWhiteSpace(name)) return null;
            if (name.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase)) {
                return "\\\\.\\" + name.Substring(8);
            }
            return name;
        } finally {
            Marshal.FreeHGlobal(native);
        }
    }

    private static void ZeroMemory(IntPtr pointer, int length) {
        byte[] zero = new byte[length];
        Marshal.Copy(zero, 0, pointer, length);
    }

    private static double EndpointIntervalMs(byte bInterval, byte speed) {
        if (bInterval == 0) return Double.NaN;
        if (speed >= 2) {
            int exponent = Math.Max(0, bInterval - 1);
            return Math.Pow(2, exponent) * 0.125;
        }
        return bInterval;
    }

    private static string BcdText(ushort value) {
        int major = (value >> 8) & 0xFF;
        int minor = (value >> 4) & 0x0F;
        int patch = value & 0x0F;
        return String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:X}.{1:X}{2:X}", major, minor, patch);
    }

    private static string FirmwareProfile(ushort vid, ushort pid) {
        string key = String.Format(System.Globalization.CultureInfo.InvariantCulture, "VID_{0:X4} PID_{1:X4}", vid, pid);
        switch (key) {
            case "VID_320F PID_5012": return "GP2040-CE / Pico firmware";
            case "VID_045E PID_028E": return "Xbox 360 compatible / XInput profile";
            case "VID_054C PID_05C4": return "Sony DualShock 4 profile";
            case "VID_054C PID_09CC": return "Sony DualShock 4 profile";
            case "VID_054C PID_0CE6": return "Sony DualSense profile";
            case "VID_0F0D PID_": return "HORI profile";
            default:
                if (vid == 0x0F0D) return "HORI profile";
                if (vid == 0x2E24 || vid == 0x0C12) return "Brook / arcade board profile";
                return "Generic HID / unknown firmware";
        }
    }

    private static string SpeedName(byte speed) {
        switch (speed) {
            case 0: return "Low";
            case 1: return "Full";
            case 2: return "High";
            case 3: return "Super";
            default: return "Unknown";
        }
    }
}

internal sealed class UsbPollingRow {
    public string Name;
    public string Vid;
    public string Pid;
    public string Endpoint;
    public string Interval;
    public string Hz;
    public string Firmware;
    public string UsbVersion;
    public string DeviceRelease;
    public double HzValue;
}

internal class ThemedPanel : Panel {
    public Color BorderColor { get; set; }
    public Color AccentColor { get; set; }
    public int CornerRadius { get; set; }

    public ThemedPanel() {
        DoubleBuffered = true;
        BorderColor = Color.FromArgb(92, 72, 200, 255);
        AccentColor = Color.Transparent;
        CornerRadius = 16;
        BackColor = Color.FromArgb(220, 12, 11, 28);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnSizeChanged(EventArgs e) {
        base.OnSizeChanged(e);
        if (Width < 2 || Height < 2) return;
        using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius)) {
            Region = new Region(path);
        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using (GraphicsPath path = RoundedRect(rect, CornerRadius))
        using (LinearGradientBrush fill = new LinearGradientBrush(rect, Color.FromArgb(35, 255, 255, 255), BackColor, 132f))
        using (Pen border = new Pen(BorderColor)) {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
        if (AccentColor.A > 0) {
            using (LinearGradientBrush accent = new LinearGradientBrush(new Rectangle(14, 0, Math.Max(1, Width - 28), 2), AccentColor, Color.Transparent, 0f)) {
                e.Graphics.FillRectangle(accent, 14, 0, Math.Max(1, Width - 28), 2);
            }
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius) {
        GraphicsPath path = new GraphicsPath();
        int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class MetricBox : ThemedPanel {
    private readonly Label value;
    public MetricBox(string title) {
        BackColor = Color.FromArgb(225, 12, 11, 28);
        BorderColor = Color.FromArgb(86, 72, 200, 255);
        AccentColor = Color.FromArgb(150, 255, 79, 216);
        CornerRadius = 13;
        Size = new Size(202, 112);
        Label name = new Label { Text = title, Location = new Point(12, 11), Size = new Size(176, 22), ForeColor = Color.FromArgb(169, 179, 199), Font = new Font("Consolas", 8, FontStyle.Bold), BackColor = Color.Transparent };
        value = new Label { Text = "--", Location = new Point(12, 43), Size = new Size(176, 48), ForeColor = Color.FromArgb(247, 244, 238), Font = new Font("Segoe UI Black", 19, FontStyle.Bold), BackColor = Color.Transparent };
        Controls.Add(name);
        Controls.Add(value);
    }
    public string Value {
        get { return value.Text; }
        set {
            this.value.Text = value;
            float size = value != null && value.Length > 12 ? 11f : value != null && value.Length > 8 ? 15f : 19f;
            Font oldFont = this.value.Font;
            this.value.Font = new Font("Segoe UI Black", size, FontStyle.Bold);
            oldFont.Dispose();
        }
    }
    public Color ValueColor { get { return value.ForeColor; } set { this.value.ForeColor = value; } }
}

internal sealed class MainForm : RawInputForm {
    const string AppName = "ENTH Latency Tester";
    const string AppVersion = "0.4.18 preview";
    const string Copyright = "Copyright C F.M. Mariani - ENTHCREATIONS.COM";

    readonly HidReportSampler sampler = new HidReportSampler();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Dictionary<string, MetricBox> cards = new Dictionary<string, MetricBox>();
    readonly Label status, linkValue, setupState, deviceNameLabel, deviceIdLabel, declaredSummaryLabel, sideController;
    readonly TextBox diagnosis;
    readonly Button startButton, stopButton, exportButton, scanUsbButton;
    readonly PollingDonut spectrum;
    UsbPollingRow identified;
    string lastAutoScanVidPid = "";
    long lastVisualizerReports = 0;

    readonly Color bg = Color.FromArgb(8, 7, 18);
    readonly Color panel = Color.FromArgb(225, 10, 10, 26);
    readonly Color panel2 = Color.FromArgb(232, 19, 19, 38);
    readonly Color line = Color.FromArgb(92, 72, 200, 255);
    readonly Color text = Color.FromArgb(247, 244, 238);
    readonly Color muted = Color.FromArgb(169, 179, 199);
    readonly Color cyan = Color.FromArgb(72, 200, 255);
    readonly Color green = Color.FromArgb(103, 232, 255);
    readonly Color magenta = Color.FromArgb(255, 79, 216);

    public MainForm() {
        Sampler = sampler;
        Text = AppName + " v" + AppVersion;
        Size = new Size(1260, 990);
        MinimumSize = new Size(1120, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = bg;
        ForeColor = text;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9);

        ThemedPanel header = NewPanel(260, 10, 944, 100, false); header.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        Label liveDot = NewLabel("*", 14, 35, 18, 20, 18, true); liveDot.ForeColor = magenta;
        Label modeLabel = NewLabel("HID-LINK / POLLING CORE", 40, 13, 260, 18, 8, true); modeLabel.Font = new Font("Consolas", 8, FontStyle.Bold); modeLabel.ForeColor = cyan;
        Label title = NewLabel("ENTH LATENCY TESTER", 40, 31, 520, 38, 22, true); title.Font = new Font("Segoe UI Black", 20, FontStyle.Bold);
        Label subtitle = NewLabel("USB DESCRIPTOR / RAW INPUT LIVE TELEMETRY / v" + AppVersion, 40, 68, 650, 22, 9, false); subtitle.Font = new Font("Consolas", 9); subtitle.ForeColor = muted;

        status = NewLabel("NO CONTROLLER", 774, 8, 154, 84, 11, true); status.TextAlign = ContentAlignment.MiddleCenter; status.BorderStyle = BorderStyle.FixedSingle; status.BackColor = Color.FromArgb(22, 15, 34); status.ForeColor = cyan; AnchorRight(status);

        ThemedPanel linkBox = NewPanel(638, 8, 124, 84, false); AnchorRight(linkBox);
        Label linkTitle = NewLabel("LINK", 10, 10, 90, 18, 8, false); linkTitle.Font = new Font("Consolas", 8); linkTitle.ForeColor = muted;
        linkValue = NewLabel("ARMED", 10, 36, 104, 28, 12, true); linkValue.Font = new Font("Consolas", 12, FontStyle.Bold); linkValue.ForeColor = cyan;
        linkBox.Controls.AddRange(new Control[]{linkTitle, linkValue});
        header.Controls.AddRange(new Control[]{liveDot, modeLabel, title, subtitle, linkBox, status});

        ThemedPanel sidebar = NewPanel(8, 10, 238, 930, false); sidebar.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
        Label brandTop = NewLabel("POWERED BY", 12, 12, 100, 18, 7, false); brandTop.Font = new Font("Consolas", 7); brandTop.ForeColor = muted;
        PictureBox logo = new PictureBox { Location = new Point(12,32), Size = new Size(188,58), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        ApplyRoundedRegion(logo, 10);
        logo.Resize += (s,e) => ApplyRoundedRegion(logo, 10);
        string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ENTH LOGO 2025 WHITE.png"); if(File.Exists(logoPath)) logo.Image = Image.FromFile(logoPath);
        Label brandSub = NewLabel("USB/HID TELEMETRY / v0.4.18", 12, 96, 190, 18, 7, false); brandSub.Font = new Font("Consolas", 7); brandSub.ForeColor = cyan;
        Label sideTitle = NewLabel("CONTROLLER", 12, 140, 110, 18, 8, false); sideTitle.Font = new Font("Consolas", 8); sideTitle.ForeColor = muted;
        sideController = NewLabel("DEVICE TYPE: waiting for input\r\nUSB ID: --\r\nUSB POLLING: --\r\nFIRMWARE/PROFILE: --", 12, 164, 210, 126, 8, true); sideController.Font = new Font("Consolas", 8, FontStyle.Bold); sideController.BorderStyle = BorderStyle.FixedSingle; sideController.BackColor = Color.FromArgb(20, 13, 34);
        Label sideHint = NewLabel("Move the controller to lock VID/PID and read bInterval from the USB descriptor.", 12, 306, 210, 72, 8, false); sideHint.Font = new Font("Consolas", 8); sideHint.ForeColor = muted;
        ThemedPanel method = NewPanel(12, 386, 210, 260, false);
        Label methodTitle = NewLabel("READ DEPTH", 10, 10, 170, 18, 8, true); methodTitle.Font = new Font("Consolas", 8, FontStyle.Bold); methodTitle.ForeColor = cyan;
        Label methodText = NewLabel("The controller declares polling in bInterval: the lowest communication layer Windows can read on the interrupt endpoint talking to USB bus.\r\n\r\nInputs are captured above that: Windows receives HID reports and delivers them through Raw Input.", 10, 34, 188, 210, 7, false); methodText.Font = new Font("Consolas", 7); methodText.ForeColor = muted;
        method.Controls.AddRange(new Control[]{methodTitle, methodText});
        Label foot = NewLabel("v0.4.18 / GPLv3 / NO WARRANTY\r\n" + Copyright, 12, 842, 210, 64, 7, false); foot.Font = new Font("Consolas", 7); foot.ForeColor = muted;
        sidebar.Controls.AddRange(new Control[]{brandTop, logo, brandSub, sideTitle, sideController, sideHint, method, foot});

        ThemedPanel setup = NewPanel(260,128,944,132,true); setup.Anchor = AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;
        Label setupTitle = NewLabel("PRIMARY LINK", 14,12,180,18,8,true); setupTitle.Font = new Font("Consolas",8,FontStyle.Bold); setupTitle.ForeColor = cyan;
        Label setupText = NewLabel("Connect and move controller. VID/PID lock triggers descriptor scan and unlocks test.",14,38,520,34,9,false); setupText.Font = new Font("Consolas",9); setupText.ForeColor = muted;
        setupState = NewLabel("WAITING",728,18,190,28,11,true); setupState.TextAlign = ContentAlignment.MiddleCenter; setupState.BorderStyle = BorderStyle.FixedSingle; setupState.BackColor = Color.FromArgb(22,15,34); setupState.ForeColor = cyan;
        deviceNameLabel = NewLabel("--",14,84,310,28,12,true);
        deviceIdLabel = NewLabel("--",340,84,210,28,10,false); deviceIdLabel.ForeColor = muted;
        declaredSummaryLabel = NewLabel("--",622,72,296,44,22,true); declaredSummaryLabel.TextAlign = ContentAlignment.MiddleRight; declaredSummaryLabel.ForeColor = green;
        setup.Controls.AddRange(new Control[]{setupTitle, setupText, setupState, deviceNameLabel, deviceIdLabel, declaredSummaryLabel});

        scanUsbButton = NewButton("Refresh USB",260,282,150,38,false); scanUsbButton.Click += (s,e)=>RefreshUsb();
        exportButton = NewButton("Export CSV",826,282,148,38,false); exportButton.Enabled=false; exportButton.Click += ExportCsv;
        startButton = NewButton("Start test",990,282,128,38,true); startButton.Enabled=false; startButton.Click += StartTest;
        stopButton = NewButton("Stop",1128,282,76,38,false); stopButton.Enabled=false; stopButton.Click += StopTest;

        spectrum = new PollingDonut { Location = new Point(260,336), Size = new Size(944,134), Anchor = AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right };
        AddMetric("declaredHz", "USB POLLING", 260, 500);
        AddMetric("packetAvg", "WIN AVG", 488, 500);
        AddMetric("packetMax", "WIN MAX", 716, 500);
        AddMetric("events", "INPUT CHANGES", 944, 500);
        AddMetric("packetMin", "WIN MIN", 260, 644);
        AddMetric("lagMin", "STABILITY", 488, 644);
        AddMetric("lagMax", "SAMPLE TIME", 716, 644);
        AddMetric("detectedHz", "WIN REPORT RATE", 944, 644);

        Label diagTitle = NewLabel("DIAGNOSTICS",260,790,160,26,13,true); diagTitle.Font = new Font("Consolas",11,FontStyle.Bold);
        diagnosis = new TextBox { Location = new Point(260,820), Size = new Size(944,72), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(18, 12, 32), ForeColor = text, Font = new Font("Consolas",9), Text = "Start sampling and leave the controller untouched for 5 seconds. If the value stays measurable, the device is sending continuous reports; if not, Windows is only delivering state changes.", Anchor = AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right };
        Label note = NewLabel("USB polling = endpoint bInterval. Spectrum = visual Raw Input activity, not a percentage and not a replacement for numeric values.",260,900,900,24,9,false); note.Font = new Font("Consolas",8); note.ForeColor = muted; note.Anchor = AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;

        Controls.AddRange(new Control[]{sidebar,header,setup,scanUsbButton,exportButton,startButton,stopButton,spectrum,diagTitle,diagnosis,note});

        timer.Interval = 100;
        timer.Tick += Tick;
        timer.Start();
        FormClosing += (s,e)=>{ timer.Stop(); sampler.Stop(); };
    }

    Label NewLabel(string t,int x,int y,int w,int h,float size,bool bold){ return new Label{Text=t,Location=new Point(x,y),Size=new Size(w,h),ForeColor=text,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),BackColor=Color.Transparent}; }
    ThemedPanel NewPanel(int x,int y,int w,int h,bool accent){ return new ThemedPanel{Location=new Point(x,y),Size=new Size(w,h),BackColor=accent?panel2:panel,BorderColor=accent?Color.FromArgb(130,255,79,216):line,AccentColor=accent?cyan:Color.FromArgb(80,72,200,255),CornerRadius=16}; }
    Button NewButton(string t,int x,int y,int w,int h,bool primary){ var b=new Button{Text=t,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,ForeColor=text,BackColor=primary?Color.FromArgb(139,69,217):Color.FromArgb(18,12,32),Font=new Font("Segoe UI Semibold",9),Anchor=AnchorStyles.Top|AnchorStyles.Right,Cursor=Cursors.Hand}; b.FlatAppearance.BorderSize=1; b.FlatAppearance.BorderColor=primary?magenta:line; ApplyRoundedRegion(b,8); b.Resize+=(s,e)=>ApplyRoundedRegion(b,8); return b; }
    void ApplyRoundedRegion(Control control,int radius){ if(control.Width<2||control.Height<2)return; using(GraphicsPath path=new GraphicsPath()){ int d=radius*2; Rectangle r=new Rectangle(0,0,control.Width,control.Height); path.AddArc(r.Left,r.Top,d,d,180,90); path.AddArc(r.Right-d,r.Top,d,d,270,90); path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.Left,r.Bottom-d,d,d,90,90); path.CloseFigure(); control.Region=new Region(path); } }
    void AnchorRight(Control c){ c.Anchor = AnchorStyles.Top | AnchorStyles.Right; }
    void AddMetric(string key,string title,int x,int y){ var box=new MetricBox(title){Location=new Point(x,y),Anchor=AnchorStyles.Left|AnchorStyles.Top}; cards[key]=box; Controls.Add(box); }
    string FormatMs(double v){ if(double.IsNaN(v)) return "--"; return v < 10 ? string.Format("{0:N2} ms", v) : string.Format("{0:N1} ms", v); }
    void ResetLiveMetrics(){
        cards["packetAvg"].Value="--";
        cards["packetMin"].Value="--";
        cards["packetMax"].Value="--";
        cards["events"].Value="0";
        cards["lagMin"].Value="--";
        cards["lagMax"].Value="0.0 s";
        cards["detectedHz"].Value="not measurable";
        lastVisualizerReports=0;
        spectrum.ObservedHz=double.NaN;
        spectrum.Pulse(double.NaN);
    }

    void StartTest(object sender, EventArgs e){ ResetLiveMetrics(); sampler.Start(); startButton.Enabled=false; startButton.Text="Calibrating"; stopButton.Enabled=true; exportButton.Enabled=true; linkValue.Text="CAL"; linkValue.ForeColor=Color.FromArgb(255,210,90); diagnosis.Text="CALIBRATION: do not touch the controller. The app is learning idle noise, timestamps, IMU/status bytes and analog drift."; }
    void StopTest(object sender, EventArgs e){ sampler.Stop(); ResetLiveMetrics(); startButton.Text="Start test"; startButton.Enabled=true; stopButton.Enabled=false; exportButton.Enabled=true; diagnosis.Text="Sampling stopped. Live metrics reset; you can export CSV or start again."; }
    void ExportCsv(object sender, EventArgs e){ using(var d=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName="controller-hid-polling.csv"}) if(d.ShowDialog()==DialogResult.OK) File.WriteAllText(d.FileName, sampler.Csv()); }

    void RefreshUsb(){
        try{
            scanUsbButton.Enabled=false; scanUsbButton.Text="Reading..."; Application.DoEvents();
            string preferred=""; try{ var p=sampler.Snapshot().Split('|'); if(p.Length>13) preferred=p[13]; }catch{}
            List<UsbPollingRow> rows=GetUsbRows(preferred);
            if(rows.Count==0){ diagnosis.Text="USB read completed, but no useful interrupt endpoint was found. Try running as administrator or changing USB port/controller mode."; return; }
            UsbPollingRow best=rows.OrderByDescending(r=>r.HzValue).First();
            identified=best;
            setupState.Text="LOCKED"; setupState.BackColor=Color.FromArgb(18,73,47); setupState.ForeColor=green;
            deviceNameLabel.Text=best.Name; deviceIdLabel.Text=best.Vid+" "+best.Pid; declaredSummaryLabel.Text=best.Hz;
            sideController.Text="DEVICE TYPE: "+best.Name+"\r\nUSB ID: "+best.Vid+" "+best.Pid+"\r\nUSB POLLING: "+best.Hz+"\r\nFIRMWARE/PROFILE: "+best.Firmware; sideController.ForeColor=green;
            linkValue.Text="LOCKED"; cards["declaredHz"].Value=best.Hz; cards["declaredHz"].ValueColor=green;
            if(best.HzValue>0){ spectrum.DeclaredHz=best.HzValue; double declaredMs=1000.0/best.HzValue; sampler.MinPlausibleReportIntervalMs=Math.Max(0.02,declaredMs*0.70); sampler.MaxPlausibleReportIntervalMs=declaredMs*2.50; spectrum.Invalidate(); }
            startButton.Enabled=true;
            diagnosis.Text="Controller locked: "+best.Name+" "+best.Vid+" "+best.Pid+". Firmware/profile: "+best.Firmware+". USB "+best.UsbVersion+", device release "+best.DeviceRelease+". Declared USB polling: "+best.Hz+", endpoint "+best.Endpoint+", interval "+best.Interval+".";
        } catch(Exception ex){ diagnosis.Text="USB read failed: "+ex.Message; }
        finally{ scanUsbButton.Text="Refresh USB"; scanUsbButton.Enabled=true; }
    }

    List<UsbPollingRow> GetUsbRows(string preferred){
        string scan=UsbDescriptorScanner.Scan();
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"usb-polling-descriptors.csv"), scan);
        var result=new List<UsbPollingRow>();
        string[] lines=scan.Split(new[]{"\r\n","\n"}, StringSplitOptions.None);
        for(int i=1;i<lines.Length;i++){
            string line=lines[i]; if(string.IsNullOrWhiteSpace(line) || line=="Diagnostics" || line.StartsWith("Hubs found:")) break;
            string[] f=line.Split(','); if(f.Length<8) continue;
            string vid=f[0], pid=f[1], hz=f[7]; if(string.IsNullOrWhiteSpace(vid)||string.IsNullOrWhiteSpace(pid)||string.IsNullOrWhiteSpace(hz)) continue;
            if(vid=="VID_05E3"||vid=="VID_8087") continue;
            string vidPid=vid+" "+pid; if(!string.IsNullOrWhiteSpace(preferred) && preferred!=vidPid) continue;
            double hzValue=ParseNumber(hz);
            string firmware=f.Length>9?f[9]:FirmwareName(vidPid);
            string usbVersion=f.Length>10?f[10]:"--";
            string deviceRelease=f.Length>11?f[11]:"--";
            string name=vidPid=="VID_045E PID_028E"?"Xbox 360 compatible":vidPid=="VID_320F PID_5012"?"GP2040 / Pico board":"HID device";
            result.Add(new UsbPollingRow{Name=name,Vid=vid,Pid=pid,Endpoint=f[4],Interval=f[6],Hz=hz,Firmware=firmware,UsbVersion=usbVersion,DeviceRelease=deviceRelease,HzValue=hzValue});
        }
        if(result.Count==0 && !string.IsNullOrWhiteSpace(preferred)) return GetUsbRows("");
        return result.GroupBy(r=>r.Vid+" "+r.Pid).Select(g=>g.OrderByDescending(r=>r.HzValue).First()).ToList();
    }
    double ParseNumber(string s){ string cleaned=new string(s.Select(ch=>char.IsDigit(ch)||ch=='.'||ch==','?ch:' ').ToArray()).Trim().Split(' ')[0].Replace(',','.'); double v; return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out v)?v:double.NaN; }
    string FirmwareName(string vidPid){ if(vidPid=="VID_320F PID_5012") return "GP2040-CE / Pico firmware"; if(vidPid=="VID_045E PID_028E") return "Xbox 360 compatible / XInput profile"; return "Generic HID / unknown firmware"; }

    void Tick(object sender, EventArgs e){
        string[] parts=sampler.Snapshot().Split('|');
        bool running=parts[0]=="1"; long devices=long.Parse(parts[1]); long reports=long.Parse(parts[2]);
        double avg=double.Parse(parts[3],CultureInfo.InvariantCulture), min=double.Parse(parts[4],CultureInfo.InvariantCulture), max=double.Parse(parts[5],CultureInfo.InvariantCulture), p95=double.Parse(parts[6],CultureInfo.InvariantCulture), hz=double.Parse(parts[7],CultureInfo.InvariantCulture), elapsed=double.Parse(parts[10],CultureInfo.InvariantCulture), stability=double.Parse(parts[11],CultureInfo.InvariantCulture);
        bool reliable=parts[12]=="1"; string lastVidPid=parts.Length>13?parts[13]:""; long changed=parts.Length>14?long.Parse(parts[14]):0; bool calibrating=parts.Length>15 && parts[15]=="1"; int idleMasked=parts.Length>16?int.Parse(parts[16]):0;
        bool connected=devices>0;
        status.Text=connected?devices+" HID ACTIVE":running?"LISTENING":"READY"; status.BackColor=connected?Color.FromArgb(18,73,47):running?Color.FromArgb(19,46,73):panel; status.ForeColor=connected?green:running?cyan:muted;
        linkValue.Text=calibrating?"CAL":running?"LIVE":identified!=null?"LOCKED":"ARMED"; linkValue.ForeColor=calibrating?Color.FromArgb(255,210,90):running?cyan:identified!=null?Color.FromArgb(145,255,0):muted;
        if (running) startButton.Text = calibrating ? "Calibrating" : "Live";
        cards["detectedHz"].Value=reliable&&!double.IsNaN(hz)?string.Format("{0:N0} Hz",hz):"not measurable";
        cards["packetAvg"].Value=FormatMs(avg); cards["packetMin"].Value=FormatMs(min); cards["packetMax"].Value=FormatMs(max); cards["events"].Value=changed.ToString(); cards["lagMin"].Value=double.IsNaN(stability)?"--":string.Format("{0:N2}x",stability); cards["lagMax"].Value=double.IsNaN(elapsed)?"--":string.Format("{0:N1} s",elapsed/1000.0);
        long delta=Math.Max(0,changed-lastVisualizerReports); lastVisualizerReports=changed; double visual=running&&delta>0?delta*(1000.0/timer.Interval):double.NaN; spectrum.ObservedHz=visual; spectrum.Pulse(visual);
        if(!string.IsNullOrWhiteSpace(lastVidPid) && identified==null){ setupState.Text=lastVidPid; setupState.BackColor=Color.FromArgb(19,46,73); setupState.ForeColor=cyan; if(lastAutoScanVidPid!=lastVidPid){ lastAutoScanVidPid=lastVidPid; RefreshUsb(); } }
        if(!running){ if(identified!=null && startButton.Enabled) diagnosis.Text="Controller locked: "+identified.Name+". Declared USB polling: "+identified.Hz+". Start the test when ready."; else if(!string.IsNullOrWhiteSpace(lastVidPid)) diagnosis.Text="Controller detected: "+lastVidPid+". Reading the USB descriptor to complete identification."; else diagnosis.Text="Connect the controller and press a button or move a stick to identify it."; }
        else if(!connected) diagnosis.Text="Listening, but no joystick/gamepad HID report has been received. If the controller is pure XInput, try DInput/HID, PC, PS3 or an equivalent mode.";
        else if(calibrating){ diagnosis.Text="CALIBRATION: do not touch the controller. Idle noise learned: "+idleMasked+" bytes masked."; }
        else if(elapsed<3000) { diagnosis.Text="Calibration complete. Idle noise learned: "+idleMasked+" bytes masked. Keep sampling for at least 5 seconds."; }
        else if(!reliable) diagnosis.Text="Raw Input is seeing events/state changes, not a constant polling stream."+(string.IsNullOrWhiteSpace(lastVidPid)?"":" Last controller input: "+lastVidPid+".");
        else diagnosis.Text="Single observed value measurable: "+Math.Round(hz)+" Hz. Report intervals: avg "+FormatMs(avg)+", P95 "+FormatMs(p95)+", min "+FormatMs(min)+", max "+FormatMs(max)+".";
    }
}

internal static class Program {
    [STAThread]
    static void Main(){ Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); }
}
