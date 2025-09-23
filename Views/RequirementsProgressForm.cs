using System;
using System.Drawing;
using System.Windows.Forms;

namespace Washmachine.Views;

public sealed class RequirementsProgressForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    public RequirementsProgressForm()
    {
        Text = "Downloading Requirements";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 140);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        TopMost = true;
        Font = new Font("Segoe UI", 9f);

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(16, 16, 16, 8),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Preparing downloads..."
        };

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            MarqueeAnimationSpeed = 0
        };

        Controls.Add(_progressBar);
        Controls.Add(_statusLabel);
    }

    public void UpdateStatus(string message, int progressPercent)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateStatus(message, progressPercent)));
            return;
        }

        _statusLabel.Text = message;
        _progressBar.Value = Math.Clamp(progressPercent, _progressBar.Minimum, _progressBar.Maximum);
    }
}
