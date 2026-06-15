// G213 HID proof-of-concept spike.
// Sets the entire Logitech G213 keyboard to uniform blue via raw HID feature reports.
// No Logitech vendor software required. Throwaway — not intended for reuse by later slices.

using System.Runtime.InteropServices;

try
{
    return Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    return 3;
}

static int Run()
{
    const ushort vid = 0x046D;
    const ushort pid = 0xC336;

    // The G213 exposes several HID collections (keyboard input, consumer controls,
    // vendor lighting, ...). Lighting commands are only accepted by the vendor
    // collection on interface 1, identified by usage page 0xFF43 / usage 0x0602.
    // (Source: OpenRGB's REGISTER_HID_DETECTOR_IPU for the G213.)
    const ushort lightingUsagePage = 0xFF43;
    const ushort lightingUsage = 0x0602;

    const byte r = 0x00;
    const byte g = 0x00;
    const byte b = 0xFF;

    bool foundDevice = false;

    foreach (var (_, handle) in HidNative.OpenMatchingDevices(vid, pid))
    {
        foundDevice = true;

        if (!HidNative.TryGetCaps(handle, out ushort usagePage, out ushort usage, out int outputReportLen) ||
            usagePage != lightingUsagePage || usage != lightingUsage)
        {
            HidNative.CloseDeviceHandle(handle);
            continue; // Not the lighting collection — try the next one.
        }

        // WriteFile must send exactly the declared output-report length. The G213
        // command is 20 bytes (report ID + 19); pad up if the descriptor is larger.
        int reportLen = Math.Max(outputReportLen, 20);

        // The G213 has five lighting zones (0x01–0x05) and needs no commit packet.
        // The zones must be paced: fired back-to-back the keyboard drops reports and
        // only some zones change colour. OpenRGB reads a response after each write to
        // synchronise; a short sleep between writes achieves the same here.
        bool allOk = true;
        for (byte zone = 0x01; zone <= 0x05; zone++)
        {
            if (!HidNative.SendOutputReport(handle, BuildReport(zone, r, g, b, reportLen)))
            {
                int err = Marshal.GetLastWin32Error();
                Console.Error.WriteLine(
                    $"Failed to write lighting report for zone 0x{zone:X2}. " +
                    $"Win32 error: {err} (0x{err:X8}).");
                allOk = false;
                break;
            }
            Thread.Sleep(20);
        }

        HidNative.CloseDeviceHandle(handle);

        if (allOk)
        {
            Console.WriteLine($"G213 set to blue (R={r} G={g} B={b}).");
            return 0;
        }
        return 2;
    }

    if (!foundDevice)
    {
        Console.Error.WriteLine(
            $"G213 not found (looked for VID=0x{vid:X4} PID=0x{pid:X4}). " +
            "Is the keyboard connected?");
        return 1;
    }

    // Device found, but none of its collections matched the lighting usage.
    Console.Error.WriteLine(
        $"G213 found (VID=0x{vid:X4} PID=0x{pid:X4}) but its lighting collection " +
        $"(usage page 0x{lightingUsagePage:X4}, usage 0x{lightingUsage:X4}) was not found.");
    return 2;
}

static byte[] BuildReport(byte zone, byte red, byte green, byte blue, int reportLen)
{
    // G213 lighting output report (sent via WriteFile, i.e. a HID output report):
    // [0]  = 0x11  (report ID)
    // [1]  = 0xFF
    // [2]  = 0x0C
    // [3]  = 0x3A
    // [4]  = zone (0x01–0x05)
    // [5]  = 0x01
    // [6]  = R
    // [7]  = G
    // [8]  = B
    // [9]  = 0x02
    // [10..] = 0x00 (padding to the descriptor's output-report length)
    var buf = new byte[reportLen];
    buf[0] = 0x11;
    buf[1] = 0xFF;
    buf[2] = 0x0C;
    buf[3] = 0x3A;
    buf[4] = zone;
    buf[5] = 0x01;
    buf[6] = red;
    buf[7] = green;
    buf[8] = blue;
    buf[9] = 0x02;
    // Remaining bytes stay 0x00.
    return buf;
}
