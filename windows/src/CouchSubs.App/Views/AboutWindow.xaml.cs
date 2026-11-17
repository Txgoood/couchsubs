// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace CouchSubs.App.Views;

public partial class AboutWindow : Window
{
    internal AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        Version.Text = version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build} · Build #{version.Revision}";
    }

    public static void Open(Window? owner) => new AboutWindow { Owner = owner }.ShowDialog();

    private void OnLink(object sender, RequestNavigateEventArgs e) =>
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
