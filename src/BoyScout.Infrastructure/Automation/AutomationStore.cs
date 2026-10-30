using System.Globalization;
using BoyScout.Infrastructure.Persistence;
using BoyScout.Application.Automation;

namespace BoyScout.Infrastructure.Automation;

public sealed class AutomationStore(BoyScoutDatabase database) : IAutomationStore
{
    private const string AiProcessingEnabledKey = "ai_processing_enabled";

    public bool GetAiProcessingEnabled()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM automation_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", AiProcessingEnabledKey);
        return command.ExecuteScalar() switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidOperationException("The AI processing setting is missing or invalid."),
        };
    }

    public void SetAiProcessingEnabled(bool enabled)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE automation_settings
            SET value = $value, updated_at = $updatedAt
            WHERE key = $key;
            """;
        command.Parameters.AddWithValue("$value", enabled ? "true" : "false");
        command.Parameters.AddWithValue(
            "$updatedAt",
            DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$key", AiProcessingEnabledKey);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The AI processing setting could not be updated.");
        }
    }
}
