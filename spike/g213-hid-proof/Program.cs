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
    const byte r = 0x00;
    const byte g = 0x00;
    const byte b = 0xFF;

    bool foundDevice = false;
    int lastRejectErr = 0;

    foreach (var (_, handle) in HidNative.OpenMatchingDevices(vid, pid))
    {
        foundDevice = true;

        // Try sending region 0x01 first. If it succeeds, this is the lighting collection —
        // send the remaining four regions. If it fails, try the next matching collection.
        if (!HidNative.SendFeatureReport(handle, BuildReport(0x01, r, g, b)))
        {
            // Capture the error now, before CloseDeviceHandle (a SetLastError P/Invoke)
            // clobbers it, so the "all collections rejected" branch can report a real code.
            lastRejectErr = Marshal.GetLastWin32Error();
            HidNative.CloseDeviceHandle(handle);
            continue; // Try the next HID collection with the same VID/PID.
        }

        // First region succeeded — send the remaining four.
        bool allOk = true;
        for (byte region = 0x02; region <= 0x05; region++)
        {
            if (!HidNative.SendFeatureReport(handle, BuildReport(region, r, g, b)))
            {
                int err = Marshal.GetLastWin32Error();
                Console.Error.WriteLine(
                    $"Failed to send feature report for region 0x{region:X2}. " +
                    $"Win32 error: {err} (0x{err:X8}).");
                allOk = false;
                break;
            }
        }

        HidNative.CloseDeviceHandle(handle);

        if (allOk)
        {
            Console.WriteLine($"G213 set to blue (R={r} G={g} B={b}).");
            return 0;
        }
        else
        {
            return 2;
        }
    }

    if (!foundDevice)
    {
        Console.Error.WriteLine(
            $"G213 not found (looked for VID=0x{vid:X4} PID=0x{pid:X4}). " +
            "Is the keyboard connected?");
        return 1;
    }

    // Device(s) found but every collection rejected region 0x01.
    Console.Error.WriteLine(
        $"G213 found (VID=0x{vid:X4} PID=0x{pid:X4}) but all HID collections rejected the feature report. " +
        $"Win32 error: {lastRejectErr} (0x{lastRejectErr:X8}).");
    return 2;
}

static byte[] BuildReport(byte region, byte red, byte green, byte blue)
{
    // 20-byte feature report for the G213 lighting protocol:
    // [0]  = 0x11  (report ID)
    // [1]  = 0xFF
    // [2]  = 0x0C
    // [3]  = 0x3A
    // [4]  = region (0x01–0x05)
    // [5]  = 0x01
    // [6]  = R
    // [7]  = G
    // [8]  = B
    // [9–19] = 0x00
    var buf = new byte[20];
    buf[0] = 0x11;
    buf[1] = 0xFF;
    buf[2] = 0x0C;
    buf[3] = 0x3A;
    buf[4] = region;
    buf[5] = 0x01;
    buf[6] = red;
    buf[7] = green;
    buf[8] = blue;
    // Remaining bytes stay 0x00.
    return buf;
}
