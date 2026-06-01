using System.Runtime.InteropServices;

namespace ThumbnailPrimer;

/// <summary>
/// Minimal Shell COM interop surface for Windows thumbnail cache priming.
/// Only the declarations needed to drive IThumbnailCache are included.
/// </summary>
internal static class NativeMethods
{
    internal static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    internal static readonly Guid CLSID_LocalThumbnailCache = new("50EF4544-AC9F-4A8E-B21B-8A26180DB13F");

    /// <summary>WTS_FLAGS 0x0 — extract from file and write to cache if not already present.</summary>
    internal const uint WTS_EXTRACT = 0x00000000;

    /// <summary>WTS_FLAGS 0x1 — return only if already in the cache; never extract from file.</summary>
    internal const uint WTS_INCACHEONLY = 0x00000001;

    /// <summary>
    /// Creates an IShellItem from an absolute file path.
    /// Returns an HRESULT (0 == S_OK).
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

    // Empty COM interfaces: we only pass/receive pointers — no method calls needed.

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem { }

    [ComImport, Guid("091162A4-BC96-411F-AAE8-C5122CD03363"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISharedBitmap { }

    [ComImport, Guid("F676C15D-596A-4CE2-8234-33996F445DB1"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IThumbnailCache
    {
        /// <summary>
        /// Forces thumbnail extraction and writes it to the Windows thumbnail cache.
        /// Returns an HRESULT; 0 == S_OK.
        /// </summary>
        [PreserveSig]
        int GetThumbnail(
            IShellItem pShellItem,
            uint cxyRequestedThumbSize,
            uint flags,
            out ISharedBitmap? ppvThumb,
            out uint pOutFlags,
            out WTS_THUMBNAILID pThumbnailID);

        // Vtable slot 4 — declared to keep the vtable size correct; never called.
        [PreserveSig]
        int GetThumbnailByID(
            WTS_THUMBNAILID thumbnailID,
            uint cxyRequestedThumbSize,
            out ISharedBitmap? ppvThumb,
            out uint pOutFlags);
    }

    /// <summary>
    /// 16-byte opaque cache key returned by GetThumbnail; we discard the value.
    /// Two longs keeps the struct blittable with correct size and alignment.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WTS_THUMBNAILID
    {
#pragma warning disable CS0169 // Fields are populated by the native caller; not read in managed code
        private long _part1;
        private long _part2;
#pragma warning restore CS0169
    }
}
