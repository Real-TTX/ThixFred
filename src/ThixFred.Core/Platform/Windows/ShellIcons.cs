using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace ThixFred.Core.Platform.Windows;

/// <summary>
/// The icon of a file, a shortcut, a folder or a Store app ("shell:AppsFolder\…") as Explorer shows it, in BGRA pixels
/// (premultiplied alpha, top-down): IShellItemImageFactory through source-generated COM (works in the trimmed build).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class ShellIcons
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    public static unsafe (byte[] Pixels, int Width, int Height)? Load(string parsingName, int size)
    {
        var interfaceId = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, interfaceId, out var instance) < 0 || instance == IntPtr.Zero)
        {
            return null;
        }

        var bitmap = IntPtr.Zero;
        try
        {
            var factory = (IShellItemImageFactory)Wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
            // SIIGBF_ICONONLY (4): the icon, never a thumbnail of the content; SIIGBF_BIGGERSIZEOK (1)
            if (factory.GetImage(new NativeSize { Width = size, Height = size }, 0x4 | 0x1, out bitmap) < 0 || bitmap == IntPtr.Zero)
            {
                return null;
            }

            return ReadPixels(bitmap);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            Marshal.Release(instance);
        }
    }

    private static unsafe (byte[] Pixels, int Width, int Height)? ReadPixels(IntPtr bitmap)
    {
        BitmapInfo info;
        if (GetObjectW(bitmap, sizeof(BitmapInfo), &info) == 0 || info.Width <= 0 || info.Height == 0)
        {
            return null;
        }

        var width = info.Width;
        var height = Math.Abs(info.Height);
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height, // top-down
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var screen = GetDC(IntPtr.Zero);
        try
        {
            fixed (byte* target = pixels)
            {
                if (GetDIBits(screen, bitmap, 0, (uint)height, target, &header, 0) == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        return (pixels, width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public uint Colors; // room for one colour entry of BITMAPINFO
    }

    [GeneratedComInterface]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    internal partial interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid interfaceId, out IntPtr item);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetObjectW(IntPtr handle, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(IntPtr deviceContext, IntPtr bitmap, uint start, uint lines, void* bits, void* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr window, IntPtr deviceContext);
}
