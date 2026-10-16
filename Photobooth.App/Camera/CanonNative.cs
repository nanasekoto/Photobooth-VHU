using System.Runtime.InteropServices;

namespace Photobooth.App.Camera;

// Windows x64 EDSDK ABI. Canon's proprietary DLLs must be supplied separately by the operator.
internal static class CanonNative
{
    private const string Dll = "EDSDK.dll";
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate uint ObjectHandler(uint eventId, IntPtr item, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate uint StateHandler(uint eventId, uint parameter, IntPtr context);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct DirectoryItemInfo
    {
        public ulong Size;
        public int IsFolder;
        public uint GroupId;
        public uint Option;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string FileName;
        public uint Format;
        public uint DateTime;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Capacity { public int FreeClusters; public int BytesPerSector; public int Reset; }
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsInitializeSDK();
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsTerminateSDK();
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsGetCameraList(out IntPtr list);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsGetChildCount(IntPtr list, out uint count);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsGetChildAtIndex(IntPtr list, int index, out IntPtr camera);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsOpenSession(IntPtr camera);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsCloseSession(IntPtr camera);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsRelease(IntPtr item);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsSetObjectEventHandler(IntPtr camera, uint eventId, ObjectHandler callback, IntPtr context);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsSetCameraStateEventHandler(IntPtr camera, uint eventId, StateHandler callback, IntPtr context);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsSetPropertyData(IntPtr camera, uint property, int parameter, uint size, ref uint data);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsSetCapacity(IntPtr camera, Capacity capacity);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsSendCommand(IntPtr camera, uint command, int parameter);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsGetDirectoryItemInfo(IntPtr item, out DirectoryItemInfo info);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsCreateMemoryStream(ulong size, out IntPtr stream);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsDownload(IntPtr item, ulong size, IntPtr stream);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsDownloadComplete(IntPtr item);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsDownloadCancel(IntPtr item);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsGetPointer(IntPtr stream, out IntPtr pointer);
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] internal static extern uint EdsGetEvent();

    internal static void Check(uint error, string operation)
    {
        if (error != 0) throw new InvalidOperationException($"Canon: {operation} thất bại (EDSDK 0x{error:X8}). Kiểm tra lấy nét, chế độ máy, cáp và EOS Utility.");
    }
}
