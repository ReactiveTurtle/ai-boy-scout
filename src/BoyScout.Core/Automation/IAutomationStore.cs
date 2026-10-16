namespace BoyScout.Core.Automation;

public interface IAutomationStore
{
    bool GetAiProcessingEnabled();
    void SetAiProcessingEnabled(bool enabled);
}
