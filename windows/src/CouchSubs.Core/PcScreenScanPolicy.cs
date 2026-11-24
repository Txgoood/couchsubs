// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core;

/// <summary>Low-cost screen sampling for PC/Remote Play. Dialogue OCR remains independent.</summary>
public sealed class PcScreenScanPolicy
{
    private byte[]? previous;
    private byte[]? scanned;
    private long lastScan = -1;
    private int movingFrames;
    private bool settled;
    public bool Moving => movingFrames >= 2;
    public int FullCaptureInterval => Moving ? 1000 : 500;

    public void Observe(Frame frame)
    {
        var current = Signature(frame);
        var wasMoving = Moving;
        movingFrames = previous is not null && Difference(previous, current, 30) >= .14
            ? Math.Min(2, movingFrames + 1) : 0;
        settled |= wasMoving && !Moving;
        previous = current;
    }

    public bool ShouldScan(long now, bool changedRegions)
    {
        if (previous is null) return false;
        if (lastScan < 0) return true;
        var elapsed = now - lastScan;
        // Keep a periodic discovery/refresh even if sparse sampling missed tiny text.
        if (elapsed >= 6500) return true;
        if (Moving) return elapsed >= 2600;
        if (settled || changedRegions) return true;
        return elapsed >= 250 && (scanned is null || Difference(scanned, previous, 14) >= .006);
    }

    public void MarkScan(long now)
    {
        scanned = previous;
        lastScan = now;
        settled = false;
    }

    public void RetryAfterCancellation() => settled = true;

    public void Reset()
    {
        previous = scanned = null;
        lastScan = -1;
        movingFrames = 0;
        settled = false;
    }

    private static byte[] Signature(Frame frame)
    {
        const int columns = 96, rows = 54;
        var result = new byte[columns * rows];
        if (frame.Width <= 0 || frame.Height <= 0) return result;
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
        {
            var i = (y * frame.Height / rows) * frame.Stride + (x * frame.Width / columns) * 4;
            result[y * columns + x] = (byte)((frame.Pixels[i] + 2 * frame.Pixels[i + 1] + frame.Pixels[i + 2]) / 4);
        }
        return result;
    }

    private static double Difference(byte[] first, byte[] second, int threshold)
    {
        var changed = 0;
        for (var i = 0; i < first.Length; i++)
            if (Math.Abs(first[i] - second[i]) >= threshold) changed++;
        return (double)changed / first.Length;
    }
}
