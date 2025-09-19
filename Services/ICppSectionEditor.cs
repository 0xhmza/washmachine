namespace Washmachine.Services;

public interface ICppSectionEditor
{
    bool UncommentMethodInSection(string cppPath, string sectionName, string methodName);
    void ReplaceInCppFile(string filePath, string oldValue, string newValue, bool backup = false);
}
