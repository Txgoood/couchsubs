// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace CouchSubs.App.Services;

internal sealed class SmoothFrameGenerator : IDisposable
{
    private readonly Process process;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor pixels;
    private readonly int frameSize;
    private readonly byte[] reply = new byte[1];
    private static readonly byte[] Next = [(byte)'N'];
    private static readonly byte[] Reset = [(byte)'C'];

    public SmoothFrameGenerator(int width, int height)
    {
        var couchSubs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchSubs");
        var runtime = Path.Combine(couchSubs, "frame-generation");
        var packages = Path.Combine(runtime, "python-packages");
        var python = Path.Combine(runtime, "venv", "Scripts", "python.exe");
        if (!File.Exists(python))
            python = Path.Combine(couchSubs, "priority1-venv", "Scripts", "python.exe");
        if (!File.Exists(python) || !Directory.Exists(Path.Combine(packages, "nvvfx")))
            throw new InvalidOperationException("NVIDIA kare üretme bileşeni bu bilgisayarda hazır değil.");

        var tag = @"Local\CouchSubs.FrameGeneration." + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(python)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["PYTHONPATH"] = packages;
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Services", "SmoothVideoWorker.py"));
        start.ArgumentList.Add(tag);
        start.ArgumentList.Add(width.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(height.ToString(CultureInfo.InvariantCulture));
        frameSize = checked(width * height * 4);
        mapping = MemoryMappedFile.CreateNew(tag, frameSize * 2L);
        try
        {
            pixels = mapping.CreateViewAccessor();
            try
            {
                process = Process.Start(start) ?? throw new InvalidOperationException("NVIDIA kare üretme başlatılamadı.");
                process.BeginErrorReadLine();
            }
            catch
            {
                pixels.Dispose();
                throw;
            }
        }
        catch
        {
            mapping.Dispose();
            throw;
        }
    }

    public async Task WarmUpAsync(CancellationToken cancellation)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        wait.CancelAfter(TimeSpan.FromSeconds(20));
        await process.StandardOutput.BaseStream.ReadExactlyAsync(reply, wait.Token).ConfigureAwait(false);
        if (reply[0] != 'R') throw new IOException("NVIDIA kare üretme başlatılamadı.");
    }

    public async Task<bool> GenerateAsync(byte[] original, byte[] generated, bool reset, CancellationToken cancellation)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        wait.CancelAfter(TimeSpan.FromMilliseconds(400));
        pixels.WriteArray(0, original, 0, frameSize);
        await process.StandardInput.BaseStream.WriteAsync(reset ? Reset : Next, wait.Token).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(wait.Token).ConfigureAwait(false);
        await process.StandardOutput.BaseStream.ReadExactlyAsync(reply, wait.Token).ConfigureAwait(false);
        if (reply[0] == 'C') return false;
        if (reply[0] != 'N') throw new IOException("NVIDIA kare üretme çalışmayı durdurdu.");
        pixels.ReadArray(frameSize, generated, 0, frameSize);
        return true;
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        finally
        {
            process.Dispose();
            pixels.Dispose();
            mapping.Dispose();
        }
    }
}
