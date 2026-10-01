namespace BoyScout.Core.Tasks;

public static class TaskStatuses
{
    public const string Todo = "todo";
    public const string Done = "done";

    public static bool IsValid(string status) => status is Todo or Done;
}
