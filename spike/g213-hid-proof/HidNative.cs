using System.Runtime.InteropServices;

/// <summary>
/// Win32 P/Invoke helpers for HID device enumeration and feature-report sending.
/// Covers setupapi.dll, hid.dll, and kernel32.dll.
/// This is throwaway spike code — not intended for reuse.
/// </summary>
internal static class HidNative
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const uint DIGCF_PRESENT         = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    private const uint GENERIC_READ    = 0x80000000;
    private const uint GENERIC_WRITE   = 0x40000000;
    private const uint FILE_SHARE_READ  = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING   = 3;

    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    // -------------------------------------------------------------------------
    // Structs
    // -------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize (4 bytes) + DevicePath (variable WCHAR[])
    // We allocate a byte buffer and marshal the path string manually.
    // cbSize must be set to 8 on 64-bit (size of cbSize(4) + pointer padding to align a WCHAR*).
    // Actually the documented value is 8 on 64-bit and 6 on 32-bit for the struct header only.
    // We use 8 (64-bit).

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    // -------------------------------------------------------------------------
    // P/Invoke — hid.dll
    // -------------------------------------------------------------------------

    [DllImport("hid.dll", SetLastError = false)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] lpReportBuffer, uint reportBufferLength);

    // -------------------------------------------------------------------------
    // P/Invoke — setupapi.dll
    // -------------------------------------------------------------------------

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // -------------------------------------------------------------------------
    // P/Invoke — kernel32.dll
    // -------------------------------------------------------------------------

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enumerates all HID interface paths whose VID and PID match the given values.
    /// Returns each matching path together with an open (non-exclusive) handle.
    /// Callers must close each handle by calling <see cref="CloseDeviceHandle"/> when done.
    /// </summary>
    public static IEnumerable<(string path, IntPtr handle)> OpenMatchingDevices(ushort vendorId, ushort productId)
    {
        HidD_GetHidGuid(out Guid hidGuid);

        IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devInfo == INVALID_HANDLE_VALUE)
            yield break;

        try
        {
            uint memberIndex = 0;
            while (true)
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };

                if (!SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIndex, ref interfaceData))
                    break; // ERROR_NO_MORE_ITEMS

                memberIndex++;

                string? devicePath = GetDevicePath(devInfo, ref interfaceData);
                if (devicePath == null)
                    continue;

                // Open non-exclusively so the keyboard keeps working for typing.
                IntPtr handle = CreateFile(
                    devicePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle == INVALID_HANDLE_VALUE)
                    continue; // Some collections reject any open; skip them.

                // Check VID/PID.
                var attrs = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attrs) ||
                    attrs.VendorID != vendorId ||
                    attrs.ProductID != productId)
                {
                    CloseHandle(handle);
                    continue;
                }

                yield return (devicePath, handle);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfo);
        }
    }

    /// <summary>
    /// Sends a HID feature report. Returns true on success.
    /// On failure, the Win32 error can be retrieved via <see cref="Marshal.GetLastWin32Error"/>.
    /// </summary>
    public static bool SendFeatureReport(IntPtr handle, byte[] report)
    {
        return HidD_SetFeature(handle, report, (uint)report.Length);
    }

    /// <summary>Closes a device handle returned by <see cref="OpenMatchingDevices"/>.</summary>
    public static void CloseDeviceHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != INVALID_HANDLE_VALUE)
            CloseHandle(handle);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string? GetDevicePath(IntPtr devInfo, ref SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        // First call: get required buffer size.
        SetupDiGetDeviceInterfaceDetail(devInfo, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

        if (requiredSize == 0)
            return null;

        // Allocate buffer. Layout: cbSize (DWORD, 4 bytes) + DevicePath (WCHAR[]).
        // On 64-bit the struct alignment is still 4 for cbSize, then the WCHAR array follows.
        // cbSize value documented as 8 on 64-bit (sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W)).
        IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // Write cbSize = 8 (64-bit) into the first 4 bytes of the buffer.
            Marshal.WriteInt32(detailBuffer, 8);

            bool ok = SetupDiGetDeviceInterfaceDetail(
                devInfo,
                ref interfaceData,
                detailBuffer,
                requiredSize,
                out _,
                IntPtr.Zero);

            if (!ok)
                return null;

            // DevicePath starts at offset 4 (after the cbSize DWORD).
            return Marshal.PtrToStringUni(detailBuffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }
}
