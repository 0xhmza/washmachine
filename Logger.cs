using System;
using System.Drawing;
using System.Windows.Forms;

public static class Logger
{
    private static RichTextBox? _target;

    /// <summary>
    /// Hook up the logger to a RichTextBox on your form.
    /// Call this once (e.g., in Form_Load).
    /// </summary>
    public static void Initialize(RichTextBox targetBox)
    {
        _target = targetBox;
        _target.ReadOnly = true;
        _target.WordWrap = false;
        _target.ScrollBars = RichTextBoxScrollBars.Both;
    }

    // ---- Public logging methods ------------------------------------------

    public static void Info(string msg) => Write(msg, _target?.ForeColor ?? Color.Black);
    public static void Warn(string msg) => Write(msg, Color.Goldenrod);
    public static void Error(string msg) => Write(msg, Color.OrangeRed);
    public static void Ok(string msg) => Write(msg, Color.ForestGreen);

    // ---- Core write method -----------------------------------------------
    private static void Write(string msg, Color color)
    {
        if (_target == null || _target.IsDisposed) return;

        void Append()
        {
            _target.SelectionStart = _target.TextLength;
            _target.SelectionLength = 0;
            _target.SelectionColor = color;

            _target.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");

            _target.SelectionColor = _target.ForeColor;
            _target.ScrollToCaret();
        }

        // Thread-safe
        if (_target.InvokeRequired)
            _target.Invoke((Action)Append);
        else
            Append();
    }
}
