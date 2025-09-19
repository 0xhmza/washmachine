using System.Windows.Forms;

namespace Washmachine.Services;

public interface IUserInteractionService
{
    DialogResult ShowMessage(
        IWin32Window owner,
        string message,
        string title,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1);

    string? SelectFile(
        IWin32Window owner,
        string title,
        string filter,
        string initialDirectory);

    void ShowShellcodeTip(IWin32Window owner);
    void ShowGuardRailInfo(IWin32Window owner);
}
