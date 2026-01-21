namespace Washmachine.Logging;

public sealed class RichTextBoxLogger : IAppLogger
{
    private readonly RichTextBox _target;

    public RichTextBoxLogger(RichTextBox target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _target.ReadOnly = true;
        _target.WordWrap = false;
        _target.ScrollBars = RichTextBoxScrollBars.Both;
    }

    public void Info(string message) => Write(message, _target.ForeColor);
    public void Warn(string message) => Write(message, Color.Goldenrod);
    public void Error(string message) => Write(message, Color.OrangeRed);
    public void Ok(string message) => Write(message, Color.ForestGreen);

    private void Write(string message, Color color)
    {
        if (_target.IsDisposed) return;

        void Append()
        {
            _target.SelectionStart = _target.TextLength;
            _target.SelectionLength = 0;
            _target.SelectionColor = color;

            _target.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

            _target.SelectionColor = _target.ForeColor;
            _target.ScrollToCaret();
        }

        if (_target.InvokeRequired)
            _target.Invoke((Action)Append);
        else
            Append();
    }
}
