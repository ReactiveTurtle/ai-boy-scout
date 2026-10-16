using BoyScout.Core.Reports;
using BoyScout.Core.Storage;
using BoyScout.Core.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class TaskPaginationTests
{
    [Fact]
    public void BrowseTasks_pages_after_filtering_project_status_and_case_insensitive_search()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"task-page-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = new BoyScoutDatabase(Path.Combine(directory, "reports.db"));
            database.Migrate();
            var reports = new ReportStore(database);
            reports.Add(new CreateReport(Path.Combine(directory, "one"), "First", null, "Observation", null, null, null));
            reports.Add(new CreateReport(Path.Combine(directory, "two"), "Second", null, "Observation", null, null, null));
            var projectIds = reports.ListProjects().ToDictionary(project => Path.GetFileName(project.Name), project => project.Id);
            using (var connection = database.OpenConnection())
            {
                foreach (var (project, title, description, criteria, status) in new[]
                {
                    ("one", "Verify", "x", "x", "todo"),
                    ("one", "x", "VERIFICATION", "x", "todo"),
                    ("one", "x", "x", "verification pending", "todo"),
                    ("one", "Verify", "x", "x", "done"),
                    ("two", "Verify", "x", "x", "todo"),
                })
                {
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "INSERT INTO tasks (project_id, title, description, acceptance_criteria, status, created_at, updated_at) "
                        + "VALUES ($projectId, $title, $description, $criteria, $status, '2026-01-01', '2026-01-01');";
                    command.Parameters.AddWithValue("$projectId", projectIds[project]);
                    command.Parameters.AddWithValue("$title", title);
                    command.Parameters.AddWithValue("$description", description);
                    command.Parameters.AddWithValue("$criteria", criteria);
                    command.Parameters.AddWithValue("$status", status);
                    command.ExecuteNonQuery();
                }
            }

            var store = new TaskStore(database);
            var first = store.BrowseTasks(projectIds["one"], "todo", "  VERIF  ", null, 2);
            var second = store.BrowseTasks(projectIds["one"], "todo", "verif", first.NextBefore, 2);
            Assert.Equal(3, first.Total);
            Assert.Equal(3, second.Total);
            Assert.Equal(["BST-000003", "BST-000002"], first.Items.Select(task => task.Id));
            Assert.Equal(2, first.NextBefore);
            Assert.Equal("BST-000001", Assert.Single(second.Items).Id);
            Assert.Null(second.NextBefore);
            Assert.Equal(5, store.BrowseTasks(null, null, null, null, 100).Total);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BrowseTasks_rejects_invalid_paging_and_filters()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"task-page-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = new BoyScoutDatabase(Path.Combine(directory, "reports.db"));
            database.Migrate();
            var store = new TaskStore(database);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.BrowseTasks(null, null, null, null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.BrowseTasks(null, null, null, null, 101));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.BrowseTasks(null, null, null, 0, 40));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.BrowseTasks(0, null, null, null, 40));
            Assert.Throws<ArgumentException>(() => store.BrowseTasks(null, "invalid", null, null, 40));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
