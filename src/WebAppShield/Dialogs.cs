using System.Diagnostics;

namespace WebAppShield;

internal static class Dialogs
{
    /// <summary>Modal error box with a single Close button. Blocks until the user closes it.</summary>
    public static void Error(string message, string? title = null)
    {
        MessageBox.Show(
            message,
            title ?? (Program.ExeName + " - Error"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public static void Warn(string message, string? title = null)
    {
        MessageBox.Show(
            message,
            title ?? (Program.ExeName + " - Warning"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    public static bool Confirm(string message, string? title = null)
        => MessageBox.Show(message, title ?? Program.ExeName,
               MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}

/// <summary>
/// Shown when the WebView2 runtime cannot be found. Offers the download link and lets
/// the user point at a folder that holds a fixed version runtime.
/// </summary>
internal sealed class WebView2MissingForm : Form
{
    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    public const string EvergreenBootstrapper = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>Set when the user picked a valid runtime folder.</summary>
    public string? ChosenPath { get; private set; }

    public WebView2MissingForm(string details)
    {
        Text = Program.ExeName + " - Microsoft Edge WebView2 not found";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(560, 300);
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        AutoScaleMode = AutoScaleMode.None;

        var icon = new PictureBox
        {
            Image = SystemIcons.Error.ToBitmap(),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Location = new Point(16, 16),
            Size = new Size(48, 48)
        };

        var message = new Label
        {
            Location = new Point(76, 16),
            Size = new Size(468, 132),
            Text =
                "This application needs the Microsoft Edge WebView2 Runtime, and it is not " +
                "installed on this computer." + Environment.NewLine + Environment.NewLine +
                "You have two ways to fix it:" + Environment.NewLine +
                "  1. Install the runtime from the link below, then start the app again." + Environment.NewLine +
                "  2. If you already have a fixed version runtime folder, choose it with Browse." +
                Environment.NewLine + Environment.NewLine + details
        };

        var link = new LinkLabel
        {
            Location = new Point(76, 156),
            Size = new Size(468, 22),
            Text = DownloadUrl
        };
        link.LinkClicked += (_, _) => OpenUrl(DownloadUrl);

        var pathBox = new TextBox
        {
            Location = new Point(76, 188),
            Size = new Size(360, 24),
            PlaceholderText = @"Folder that contains msedgewebview2.exe"
        };

        var browse = new Button
        {
            Location = new Point(444, 187),
            Size = new Size(100, 26),
            Text = "Browse..."
        };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Choose the folder that contains msedgewebview2.exe",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) pathBox.Text = dlg.SelectedPath;
        };

        var use = new Button
        {
            Location = new Point(300, 240),
            Size = new Size(120, 32),
            Text = "Use this folder"
        };
        use.Click += (_, _) =>
        {
            var folder = pathBox.Text.Trim();
            if (!IsRuntimeFolder(folder))
            {
                Dialogs.Warn("msedgewebview2.exe was not found in:" + Environment.NewLine +
                             Environment.NewLine + folder, Text);
                return;
            }
            ChosenPath = folder;
            DialogResult = DialogResult.OK;
            Close();
        };

        var close = new Button
        {
            Location = new Point(432, 240),
            Size = new Size(112, 32),
            Text = "Close",
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange([icon, message, link, pathBox, browse, use, close]);
        AcceptButton = use;
        CancelButton = close;
    }

    /// <summary>A fixed version runtime folder is one that holds msedgewebview2.exe.</summary>
    public static bool IsRuntimeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try { return File.Exists(Path.Combine(folder, "msedgewebview2.exe")); }
        catch { return false; }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Dialogs.Warn("Could not open the link:" + Environment.NewLine + ex.Message); }
    }
}
