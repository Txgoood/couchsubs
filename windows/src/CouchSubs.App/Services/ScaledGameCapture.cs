// SPDX-License-Identifier: AGPL-3.0-only

using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace CouchSubs.App.Services;

/// <summary>Captures the selected monitor asynchronously and keeps only its newest original frame.</summary>
internal sealed class ScaledGameCapture : IDisposable
{
    private readonly IDirect3DDevice device;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private readonly byte[] source;
    private readonly int sourceWidth, sourceHeight, width, height;
    private readonly int[] columns;
    private readonly GraphicsCaptureItem item;
    private volatile bool closed;

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect
    {
        public int Left, Top, Right, Bottom;
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(in MonitorRect rect, uint flags);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
        nint levels, uint levelCount, uint sdkVersion, out nint device, nint level, nint context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);

    public ScaledGameCapture(Rectangle bounds, int width, int height)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new InvalidOperationException("Windows bu akıcı görüntü yakalama yöntemini desteklemiyor.");
        this.width = width;
        this.height = height;
        var rect = new MonitorRect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        var monitor = MonitorFromRect(rect, 0);
        if (monitor == 0) throw new InvalidOperationException("Seçilen ekran bulunamadı.");
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var pointer = interop.CreateForMonitor(monitor, new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"));
        try { item = MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
        var size = item.Size;
        sourceWidth = size.Width;
        sourceHeight = size.Height;
        source = new byte[checked(sourceWidth * sourceHeight * 4)];
        columns = Enumerable.Range(0, width).Select(x => x * sourceWidth / width).ToArray();
        device = MakeDevice();
        try
        {
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            try
            {
                session = pool.CreateCaptureSession(item);
                try
                {
                    session.IsCursorCaptureEnabled = false;
                    session.StartCapture();
                    item.Closed += OnClosed;
                }
                catch
                {
                    session.Dispose();
                    throw;
                }
            }
            catch
            {
                pool.Dispose();
                throw;
            }
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    private static IDirect3DDevice MakeDevice()
    {
        const uint hardware = 1, bgra = 0x20, sdkVersion = 7;
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, hardware, 0, bgra,
            0, 0, sdkVersion, out var native, 0, 0));
        try
        {
            var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in iid, out var dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                try { return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
                finally { Marshal.Release(inspectable); }
            }
            finally { Marshal.Release(dxgi); }
        }
        finally { Marshal.Release(native); }
    }

    public async Task<bool> CopyToAsync(byte[] destination, CancellationToken cancellation)
    {
        if (closed) throw new InvalidOperationException("Akıcı görüntü kapatıldı. Ekran bağlantısı kesildi.");
        using var first = pool.TryGetNextFrame();
        if (first is null) return false;
        using var newer = pool.TryGetNextFrame();
        var frame = newer ?? first;
        if (frame.ContentSize.Width != sourceWidth || frame.ContentSize.Height != sourceHeight)
            throw new InvalidOperationException("Akıcı görüntü kapatıldı. Ekran boyutu değişti.");
        using var image = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask(cancellation).ConfigureAwait(false);
        image.CopyToBuffer(source.AsBuffer());
        Resize(destination);
        return true;
    }

    private unsafe void Resize(byte[] destination)
    {
        if (width == sourceWidth && height == sourceHeight)
        {
            source.CopyTo(destination, 0);
            return;
        }
        fixed (byte* input = source, output = destination)
        {
            var target = (uint*)output;
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)input + y * sourceHeight / height * sourceWidth;
                for (var x = 0; x < width; x++) *target++ = row[columns[x]];
            }
        }
    }

    public void Dispose()
    {
        item.Closed -= OnClosed;
        session.Dispose();
        pool.Dispose();
        device.Dispose();
    }

    private void OnClosed(GraphicsCaptureItem sender, object args) => closed = true;
}
