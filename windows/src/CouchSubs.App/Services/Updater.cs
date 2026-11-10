// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Shell;

namespace CouchSubs.App.Services;

/// <summary>
/// TR fork: the original application's auto-updater must never replace this
/// second-monitor build with the upstream TV-only binary. Updates are delivered
/// manually from the fork's versioned GitHub releases until fork-safe upgrade
/// support is implemented and tested.
/// </summary>
internal sealed class Updater : IUpdater
{
    public bool CanUpdateItself => false;
    public Uri DownloadPage { get; } = new("https://github.com/Txgoood/couchsubs/releases");

    public Task<string?> CheckAsync() => Task.FromResult<string?>(null);

    public Task InstallAsync(IProgress<double> progress) => Task.CompletedTask;
}
