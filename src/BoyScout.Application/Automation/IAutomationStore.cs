namespace BoyScout.Application.Automation;

public interface IAutomationStore
{
    bool GetAiProcessingEnabled();
    void SetAiProcessingEnabled(bool enabled);
}
