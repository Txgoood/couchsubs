// SPDX-License-Identifier: AGPL-3.0-only
using System.Net.Sockets;
using System.Text;

namespace CouchSubs.RemotePlay;

/// <summary>
/// PS5's LAN discovery protocol can optionally report running-app-name.
/// Unlike OCR, this may identify any installed game without preconfigured names.
/// No game control, credentials, cloud requests or HTTP calls are involved.
/// </summary>
public static class Ps5RunningGameDiscovery
{
    private const int Ps5DiscoveryPort = 9302;
    private static readonly byte[] Search = Encoding.ASCII.GetBytes(
        "SRCH * HTTP/1.1\n" +
        "device-discovery-protocol-version:00030010\n\0");

    public static async Task<string?> ProbeAsync(string pairedHost, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pairedHost)) return null;
        try
        {
            using var udp = new UdpClient(0); // Own ephemeral port; never hijacks Remote Play.
            await udp.SendAsync(Search, Search.Length, pairedHost, Ps5DiscoveryPort)
                .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            var response = await udp.ReceiveAsync(cancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromMilliseconds(1200), cancellationToken).ConfigureAwait(false);
            return ParseGameName(Encoding.UTF8.GetString(response.Buffer));
        }
        catch (Exception error) when (error is SocketException or TimeoutException
            or OperationCanceledException or ObjectDisposedException or ArgumentException)
        {
            return null; // No discovery support must never affect active subtitles.
        }
    }

    /// <summary>Extract a safe running application name from a successful PS5 DDP response.</summary>
    public static string? ParseGameName(string? response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length > 8192) return null;
        var lines = response.Split('\n', StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase))
            return null;
        foreach (var line in lines.Skip(1))
        {
            if (!line.StartsWith("running-app-name:", StringComparison.OrdinalIgnoreCase)) continue;
            var title = line[(line.IndexOf(':') + 1)..].Trim().TrimEnd('\0');
            if (title.Length is < 2 or > 80 || title.Any(char.IsControl)) return null;
            title = string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (!title.Any(char.IsLetter) ||
                title is "Home Screen" or "PS5" or "Remote Play" or "Media Gallery")
                return null;
            return title;
        }
        return null;
    }
}
