namespace KeyboardOfClaude.Tray;

internal static class G213Keyboard
{
    private const ushort VendorId = 0x046D;
    private const ushort ProductId = 0xC336;
    private const ushort LightingUsagePage = 0xFF43;
    private const ushort LightingUsage = 0x0602;
    private const int ZoneCount = 5;            // zones 0x01..0x05
    private const int ZonePaceMs = 20;

    /// <summary>
    /// Paints the whole keyboard one colour. Returns true if the device was
    /// found and all zone writes succeeded; false otherwise. Never throws for
    /// device-absent / write-failure cases.
    /// </summary>
    public static bool TryPaint(byte r, byte g, byte b)
    {
        try
        {
            foreach (var (_, handle) in HidNative.OpenMatchingDevices(VendorId, ProductId))
            {
                if (!HidNative.TryGetCaps(handle, out var usagePage, out var usage, out var outLen)
                    || usagePage != LightingUsagePage || usage != LightingUsage)
                {
                    HidNative.CloseDeviceHandle(handle);
                    continue;
                }

                int reportLen = Math.Max(outLen, 20);
                bool allOk = true;
                try
                {
                    for (byte zone = 0x01; zone <= ZoneCount; zone++)
                    {
                        if (!HidNative.SendOutputReport(handle, BuildReport(zone, r, g, b, reportLen)))
                        {
                            allOk = false;
                            break;
                        }
                        // Pace between zones only; no need to sleep after the final write.
                        if (zone < ZoneCount)
                            Thread.Sleep(ZonePaceMs);
                    }
                }
                finally
                {
                    HidNative.CloseDeviceHandle(handle);
                }
                return allOk;
            }
        }
        catch
        {
            return false; // any unexpected native failure is a "keyboard problem", not a crash
        }
        return false; // device / lighting collection not found
    }

    private static byte[] BuildReport(byte zone, byte r, byte g, byte b, int reportLen)
    {
        var buf = new byte[reportLen];
        buf[0] = 0x11; buf[1] = 0xFF; buf[2] = 0x0C; buf[3] = 0x3A;
        buf[4] = zone; buf[5] = 0x01;
        buf[6] = r; buf[7] = g; buf[8] = b;
        buf[9] = 0x02;
        return buf;
    }
}
