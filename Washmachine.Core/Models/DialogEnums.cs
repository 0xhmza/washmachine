namespace Washmachine;

public enum MsgBoxButton
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

public enum MsgBoxIcon
{
    None,
    Error,
    Warning,
    Information,
    Question
}

// Numeric values match Win32 IDOK/IDCANCEL/IDYES/IDNO return values from MessageBoxW
public enum MsgBoxResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Yes = 6,
    No = 7
}
