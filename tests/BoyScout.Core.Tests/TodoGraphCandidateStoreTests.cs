using TodoGraph;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class TodoGraphCandidateStoreTests
{
    [Fact]
    public void TaskLinksAllowCrossProjectEndpointsAreIdempotentAndDoNotChangeProjectTaskLists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-LINK-001", "Project one", "First project") with
        {
            SourceProjectId = 101,
            SourceTasks = [ImportedTask("BST-LINK-001", 101, "First task")],
        });
        store.Receive(Note("BSR-LINK-002", "Project two", "Second project") with
        {
            SourceProjectId = 102,
            SourceTasks = [ImportedTask("BST-LINK-002", 102, "Second task")],
        });
        var first = Assert.Single(store.ListTaskNodes(101));
        var second = Assert.Single(store.ListTaskNodes(102));
        var request = new TaskLinkRequest(second.Id, "depends_on");

        Assert.True(store.AddTaskLink(first.Id, request));
        Assert.True(store.AddTaskLink(first.Id, request));

        var edge = Assert.Single(store.GetGraphSnapshot().Edges, item => item.From == $"task:{first.Id}" && item.To == $"task:{second.Id}");
        Assert.Equal("depends_on", edge.Relation);
        Assert.Equal(first.Id, Assert.Single(store.ListTaskNodes(101)).Id);
        Assert.Equal(second.Id, Assert.Single(store.ListTaskNodes(102)).Id);
        Assert.Single(store.GetGraphSnapshot().Edges, item => item.From == $"task:{first.Id}" && item.To == $"task:{second.Id}");
    }

    [Fact]
    public void TaskLinksRejectMissingTasksSelfLinksAndUnsupportedRelations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-LINK-003", "Project", "Project") with
        {
            SourceProjectId = 103,
            SourceTasks = [ImportedTask("BST-LINK-003", 103, "Task")],
        });
        var task = Assert.Single(store.ListTaskNodes(103));

        Assert.False(store.AddTaskLink(task.Id, new TaskLinkRequest(long.MaxValue, "related")));
        Assert.False(store.AddTaskLink(long.MaxValue, new TaskLinkRequest(task.Id, "related")));
        Assert.Throws<ArgumentException>(() => store.AddTaskLink(task.Id, new TaskLinkRequest(task.Id, "related")));
        Assert.Throws<ArgumentException>(() => store.AddTaskLink(task.Id, new TaskLinkRequest(long.MaxValue, "unknown")));
        Assert.Empty(store.GetGraphSnapshot().Edges);
    }

    [Fact]
    public void ReceiveIsIdempotentAndSuggestsRelatedCandidates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));
        var first = store.Receive(Note("BSR-000001", "SQLite connection cleanup", "SQLite connections leak after failed cleanup."));
        var second = store.Receive(Note("BSR-000002", "SQLite cleanup failure", "Failed SQLite connection cleanup leaks handles."));

        var duplicate = store.Receive(Note("BSR-000001", "Changed title ignored", "The same approved source note."));

        Assert.False(first.AlreadyReceived);
        Assert.False(second.AlreadyReceived);
        Assert.True(duplicate.AlreadyReceived);
        Assert.Equal(first.Candidate.Id, duplicate.Candidate.Id);
        Assert.Contains(store.Get(first.Candidate.Id)!.SuggestedLinks, link => link.CandidateId == second.Candidate.Id);
    }

    [Fact]
    public void OnlyAcceptedNotesCanBeReceived()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));
        Assert.Throws<ArgumentException>(() => store.Receive(Note("BSR-000001", "Title", "Observation") with { Status = "open" }));
    }

    [Fact]
    public void PlannedNotesCanEnterTheCandidateQueueWithoutChangingTheirSourceStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));

        var result = store.Receive(Note("BSR-000012", "Title", "Observation") with { Status = "planned" });

        Assert.Equal("planned", result.Candidate.Status);
    }

    [Fact]
    public void NewCandidatesAreQueuedAndFailedJobsCanBeRetried()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));
        var candidate = store.Receive(Note("BSR-000013", "Title", "Observation")).Candidate;

        Assert.Equal("queued", store.GetAgentJob(candidate.Id)!.Status);
        Assert.Equal(candidate.Id, store.TryClaimAgentJob());
        Assert.Equal("running", store.GetAgentJob(candidate.Id)!.Status);
        store.SetAgentJobState(candidate.Id, "failed", "OpenCode unavailable");

        Assert.Equal("queued", store.QueueAgentJob(candidate.Id)!.Status);
        Assert.Equal(candidate.Id, store.TryClaimAgentJob());
        Assert.Equal("ready", store.SetAgentJobState(candidate.Id, "ready")!.Status);
    }

    [Fact]
    public void GraphSearchSupportsShortPackageNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));
        var target = store.Receive(Note("BSR-000014", "Review dependency graph", "A package audit is needed.")).Candidate;
        var npmCandidate = store.Receive(Note("BSR-000015", "npm vulnerabilities", "The npm audit found vulnerable transitive packages.")).Candidate;

        Assert.Contains(store.SearchGraph(target.Id, "npm"), node => node.Id == npmCandidate.Id);
    }

    [Fact]
    public void GraphContextReportsWhenCandidateHasNoOtherNodes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));
        var candidate = store.Receive(Note("BSR-000016", "Standalone candidate", "No related work exists yet.")).Candidate;

        var context = store.BuildGraphContext(candidate.Id)!;

        Assert.Equal(0, context.AvailableNodeCount);
        Assert.Empty(context.Nodes);
    }

    [Fact]
    public void AgentSuggestionsOnlyChangeGraphAfterHumanApproval()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));
        var incoming = store.Receive(Note("BSR-000010", "SQLite cleanup", "Failed SQLite cleanup leaks connection handles.")).Candidate;
        var existing = store.Receive(Note("BSR-000011", "SQLite connection cleanup", "Connection cleanup fails and leaks handles.")).Candidate;
        var context = store.BuildGraphContext(incoming.Id)!;
        var search = store.SearchGraph(incoming.Id, "connection cleanup");
        Assert.Contains(search, node => node.Id == existing.Id);
        var review = store.SaveAgentReview(context with
        {
            LocalizedCandidateTitle = "Очистка подключений SQLite",
            LocalizedCandidateDescription = "При сбое очистки утекут дескрипторы подключения.",
        }, "Похоже на ту же проблему.",
            [new AgentLinkProposal(existing.Id, "related", "Совпадают причина и последствия.")],
            new AgentTaskPlan("create_task", null, "Исправить очистку SQLite", "Корректно очищать соединения SQLite после ошибки.", "Добавить тест на очистку соединения после исключения.", "Подходящая задача в графе отсутствует."));

        Assert.Contains(store.Get(incoming.Id)!.Links, link => link.Relation == "source");
        store.DecideAgentSuggestion(incoming.Id, review.Suggestions[0].Id, accept: true);

        Assert.Contains(store.Get(incoming.Id)!.Links, link => link.CandidateId == existing.Id && link.Relation == "related");
        Assert.Equal("Исправить очистку SQLite", store.Get(incoming.Id)!.LocalizedTitle);
        var expandedContext = store.BuildGraphContext(incoming.Id)!;
        Assert.DoesNotContain(expandedContext.Nodes, node => node.Id == incoming.Id);
        Assert.Contains(expandedContext.Nodes, node => node.Id == existing.Id && node.Description.Contains("Connection cleanup", StringComparison.Ordinal));
        Assert.Contains(expandedContext.Edges, edge => edge.FromId == incoming.Id && edge.ToId == existing.Id);
        Assert.Contains(store.ExpandGraphNode(existing.Id)!.Edges, edge => edge.FromId == incoming.Id && edge.ToId == existing.Id);
    }

    [Fact]
    public void BoyScoutTasksAreImportedAndAgentUpdatesRequireHumanConfirmation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));
        var candidate = store.Receive(Note("BSR-000030", "Проверить npm audit", "Проверить уязвимости пакетов.") with
        {
            SourceProjectId = 7,
            SourceTasks = [new ImportedTask("BST-000001", 7, "C:\\repo", "Обновить npm-зависимости", "Обновить пакеты по результатам аудита.", "npm audit завершается без ошибок.", null, "todo", "2026-10-01T00:00:00Z", "2026-10-02T00:00:00Z", [])],
        }).Candidate;
        var task = Assert.Single(store.ListTaskNodes(7));
        var context = store.BuildGraphContext(candidate.Id)! with
        {
            LocalizedCandidateTitle = "Проверить уязвимости npm",
            LocalizedCandidateDescription = "Нужно проверить версии зависимостей и устранить уязвимости.",
        };
        var review = store.SaveAgentReview(context, "Кандидат относится к существующей задаче.", [],
            new AgentTaskPlan("update_existing", task.Id, "Обновить npm-зависимости", "Обновить уязвимые npm-зависимости и подтвердить исправления аудитом.", "npm audit не сообщает об уязвимостях; тесты проходят.", "Текущая задача не уточняет проверку устранения уязвимостей."));

        Assert.Equal("Обновить пакеты по результатам аудита.", store.GetTaskNode(task.Id)!.Description);
        store.DecideAgentTaskPlan(candidate.Id, review.TaskPlan!.Id!.Value, accept: true);

        Assert.Equal("Обновить уязвимые npm-зависимости и подтвердить исправления аудитом.", store.GetTaskNode(task.Id)!.Description);
        Assert.True(store.GetTaskNode(task.Id)!.UserModified);
        Assert.Contains(store.BuildGraphContext(candidate.Id)!.Edges, edge => edge.FromId == candidate.Id && edge.ToId == -task.Id);
    }

    [Fact]
    public void LinkingMergeRequestDoesNotChangeTaskStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-000032", "Review task", "Task for review.") with
        {
            SourceProjectId = 8,
            SourceTasks = [new ImportedTask("BST-000002", 8, "C:\\repo", "Task title", "Task description", "Pass tests", null, "todo", "2026-10-01T00:00:00Z", "2026-10-02T00:00:00Z", [])],
        });
        var task = Assert.Single(store.ListTaskNodes(8));

        var updated = store.UpdateTask(task.Id, new TaskUpdateRequest(" https://gitlab.com/group/project/-/merge_requests/12 "))!;

        Assert.Equal("todo", updated.Status);
        Assert.Equal("https://gitlab.com/group/project/-/merge_requests/12", updated.MergeRequestUrl);
        Assert.Equal(MergeRequestStatus.Open, updated.MergeRequestStatus);
        Assert.True(updated.UserModified);
        var graphTask = Assert.Single(store.GetGraphSnapshot().Nodes, node => node.Key == $"task:{task.Id}");
        Assert.Equal(updated.MergeRequestUrl, graphTask.MergeRequestUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@gitlab.com/group/project/-/merge_requests/12")]
    public void TaskUpdateRejectsUnsafeMergeRequestUrl(string mergeRequestUrl)
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-000033", "Review task", "Task for review.") with
        {
            SourceProjectId = 9,
            SourceTasks = [new ImportedTask("BST-000003", 9, "C:\\repo", "Task title", "Task description", "Pass tests", null, "todo", "2026-10-01T00:00:00Z", "2026-10-02T00:00:00Z", [])],
        });
        var task = Assert.Single(store.ListTaskNodes(9));

        Assert.Throws<ArgumentException>(() => store.UpdateTask(task.Id, new TaskUpdateRequest(mergeRequestUrl)));
        Assert.Equal("todo", store.GetTaskNode(task.Id)!.Status);
        Assert.Null(store.GetTaskNode(task.Id)!.MergeRequestUrl);
    }

    [Fact]
    public void OnlyCodeReviewTasksWithMergeRequestUrlsArePollable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-000034", "Review tasks", "Task status test.") with
        {
            SourceProjectId = 10,
            SourceTasks =
            [
                ImportedTask("BST-000004", 10, "Task without URL"),
                ImportedTask("BST-000005", 10, "Task with URL"),
            ],
        });
        var tasks = store.ListTaskNodes(10);

        store.UpdateTask(tasks[1].Id, new TaskUpdateRequest("https://gitlab.com/group/project/-/merge_requests/15"));

        var pollable = Assert.Single(store.ListPollableMergeRequests());
        Assert.Equal(tasks[1].Id, pollable.Id);
    }

    [Fact]
    public void ProjectRepositoryAndQueueActionsPersistWithoutDuplicatingWork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using (var git = Process.Start(new ProcessStartInfo("git")
        {
            ArgumentList = { "init", directory },
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }))
        {
            Assert.NotNull(git);
            git.WaitForExit();
            Assert.Equal(0, git.ExitCode);
        }
        var database = Path.Combine(directory, "tasks.db");
        var store = new CandidateStore(database);
        store.Receive(Note("BSR-QUEUE-001", "Queue a fix", "Review work.") with
        {
            SourceProjectId = 15,
            SourceTasks = [ImportedTask("BST-QUEUE-001", 15, "Fix broken feature")],
        });
        var task = Assert.Single(store.ListTaskNodes(15));

        Assert.Null(store.QueueTask(task.Id, out var missingPathError)?.RepositoryPath);
        Assert.Contains("Configure a local Git repository", missingPathError);
        Assert.Throws<ArgumentException>(() => store.SetProjectRepository(15, Path.Combine(directory, "missing")));
        var configured = store.SetProjectRepository(15, directory)!;
        Assert.Equal(Path.GetFullPath(directory), configured.RepositoryPath);
        Assert.Equal(TaskExecutionStatus.Queued, store.QueueTask(task.Id, out var firstError)!.ExecutionStatus);
        Assert.Null(firstError);
        Assert.Equal(TaskExecutionStatus.Queued, store.QueueTask(task.Id, out var repeatedError)!.ExecutionStatus);
        Assert.Null(repeatedError);
        Assert.Equal(TaskExecutionStatus.Queued, new CandidateStore(database).GetTaskNode(task.Id)!.ExecutionStatus);
        var node = Assert.Single(store.GetGraphSnapshot().Nodes, node => node.Key == $"task:{task.Id}");
        Assert.Equal(directory, node.RepositoryPath);

        Assert.Equal("closed", store.RejectTask(task.Id)!.Status);
        Assert.Equal(TaskExecutionStatus.Closed, store.GetTaskNode(task.Id)!.ExecutionStatus);
        Assert.Equal("closed", store.QueueTask(task.Id, out var closedError)!.Status);
        Assert.NotNull(closedError);
        Assert.Null(store.UpdateTask(task.Id, new TaskUpdateRequest(null)));
    }

    [Fact]
    public void LegacyTaskDatabaseGainsQueueColumnsWithoutChangingExistingTasks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "legacy.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE task_nodes (
                  id INTEGER PRIMARY KEY AUTOINCREMENT, source_project_id INTEGER, source_task_id TEXT,
                  source_candidate_id INTEGER UNIQUE, project TEXT NOT NULL, title TEXT NOT NULL,
                  description TEXT NOT NULL, acceptance_criteria TEXT NOT NULL, current_consequences TEXT,
                  status TEXT NOT NULL DEFAULT 'ready', source_report_ids_json TEXT NOT NULL DEFAULT '[]',
                  merge_request_url TEXT, user_modified INTEGER NOT NULL DEFAULT 0,
                  created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
                  UNIQUE(source_project_id, source_task_id)
                );
                INSERT INTO task_nodes (source_project_id, source_task_id, project, title, description,
                  acceptance_criteria, status, merge_request_url, created_at, updated_at)
                VALUES (17, 'BST-LEGACY', 'sample-project', 'Keep task', 'Keep description',
                  'Keep checks', 'code_review', 'https://gitlab.com/org/project/-/merge_requests/17', '2026-10-22T00:00:00Z', '2026-10-22T00:00:00Z');
                """;
            command.ExecuteNonQuery();
        }

        var restored = Assert.Single(new CandidateStore(path).ListTaskNodes(17));

        Assert.Equal("code_review", restored.Status);
        Assert.Equal("Keep task", restored.Title);
        Assert.Equal(TaskExecutionStatus.Idle, restored.ExecutionStatus);
        Assert.Equal(ProjectBuildStatus.NotStarted, restored.BuildStatus);
        Assert.Equal(MergeRequestStatus.Open, restored.MergeRequestStatus);
        Assert.Single(new CandidateStore(path).ListPollableMergeRequests());
    }

    [Fact]
    public void TaskHistoryMigratesLegacyDatabaseAndRecordsStatusAndContentChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "legacy-history.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE task_nodes (
                  id INTEGER PRIMARY KEY AUTOINCREMENT, source_project_id INTEGER, source_task_id TEXT,
                  source_candidate_id INTEGER UNIQUE, project TEXT NOT NULL, title TEXT NOT NULL,
                  description TEXT NOT NULL, acceptance_criteria TEXT NOT NULL, current_consequences TEXT,
                  status TEXT NOT NULL DEFAULT 'ready', source_report_ids_json TEXT NOT NULL DEFAULT '[]',
                  merge_request_url TEXT, user_modified INTEGER NOT NULL DEFAULT 0,
                  created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
                  UNIQUE(source_project_id, source_task_id)
                );
                INSERT INTO task_nodes (source_project_id, source_task_id, project, title, description, acceptance_criteria, status, created_at, updated_at)
                VALUES (55, 'legacy', 'legacy', 'Existing', 'Old text', 'Checks', 'todo', '2026-10-22T00:00:00Z', '2026-10-22T00:00:00Z');
                """;
            command.ExecuteNonQuery();
        }

        var store = new CandidateStore(path);
        var legacyTask = Assert.Single(store.ListTaskNodes(55));
        store.RejectTask(legacyTask.Id);
        var events = store.ListTaskHistory(legacyTask.Id);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, item => item.EventType == "status" && item.PreviousValue == "todo" && item.NewValue == "closed");
        Assert.Contains(events, item => item.EventType == "execution" && item.PreviousValue == "idle" && item.NewValue == "closed");
    }

    [Fact]
    public void FileListingReturnsSafeMetadataFromWorktreeAndNoFilesWithoutRepository()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var repository = Path.Combine(directory, "repository");
        var worktree = Path.Combine(directory, "worktree");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(worktree);
        RunGit(repository, "init", "-b", "main");
        File.WriteAllText(Path.Combine(repository, "repo-only.txt"), "repository content");
        File.WriteAllText(Path.Combine(worktree, "visible.txt"), "must never be returned");
        File.WriteAllText(Path.Combine(worktree, ".env.local"), "secret=hidden");
        File.WriteAllText(Path.Combine(worktree, "private.pem"), "private key");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        File.WriteAllText(Path.Combine(worktree, ".git", "config"), "internal");
        var path = Path.Combine(directory, "tasks.db");
        var store = new CandidateStore(path);
        var associatedCandidate = store.Receive(Note("BSR-FILES", "Files", "Files") with
        {
            SourceProjectId = 61,
            SourceTasks = [ImportedTask("BST-FILES", 61, "Task with repo")],
        });
        store.SetProjectRepository(61, repository);
        var task = Assert.Single(store.ListTaskNodes(61));
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE task_nodes SET worktree_path=$path WHERE id=$id;";
            command.Parameters.AddWithValue("$path", worktree);
            command.Parameters.AddWithValue("$id", task.Id);
            command.ExecuteNonQuery();
        }

        var files = store.ListTaskFiles(task.Id);

        var file = Assert.Single(files);
        Assert.Equal("visible.txt", file.Path);
        Assert.Equal(new FileInfo(Path.Combine(worktree, "visible.txt")).Length, file.Size);
        Assert.DoesNotContain(files, item => Path.IsPathRooted(item.Path));
        Assert.DoesNotContain(files, item => item.Path.Contains(".git", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, item => item.Path.Contains("secret", StringComparison.OrdinalIgnoreCase) || item.Path.Contains(".env", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("must never be returned", System.Text.Json.JsonSerializer.Serialize(files), StringComparison.Ordinal);
        Assert.Contains(store.ListCandidateFiles(associatedCandidate.Candidate.Id), item => item.Path == "repo-only.txt");

        var noRepo = store.Receive(Note("BSR-NO-REPO", "No repo", "No repo") with
        {
            SourceProjectId = 63, SourceTasks = [ImportedTask("BST-NO-REPO", 63, "No repository")],
        });
        var unassociatedTask = store.ListTaskNodes().Single(item => item.SourceTaskId == "BST-NO-REPO");
        Assert.Empty(store.ListTaskFiles(unassociatedTask.Id));
        Assert.Empty(store.ListCandidateFiles(noRepo.Candidate.Id));
    }

    [Fact]
    public void DescriptionEditsValidateAndUpdateOnlyLocalGraphCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        var candidate = store.Receive(Note("BSR-EDIT", "Editable", "Source description") with
        {
            SourceProjectId = 62, SourceTasks = [ImportedTask("BST-EDIT", 62, "Editable task")],
        }).Candidate;
        var task = Assert.Single(store.ListTaskNodes(62));
        Assert.Contains(store.ListTaskHistory(task.Id), item => item.EventType == "created" && item.NewValue == "Editable task");

        Assert.True(store.UpdateTaskDescription(task.Id, "  Updated task description  "));
        Assert.True(store.GetTaskNode(task.Id)!.UserModified);
        Assert.Equal("Updated task description", store.GetTaskNode(task.Id)!.Description);
        Assert.Throws<ArgumentException>(() => store.UpdateTaskDescription(task.Id, "  "));
        Assert.Throws<ArgumentException>(() => store.UpdateCandidateDescription(candidate.Id, new string('x', 10001)));
        Assert.True(store.UpdateCandidateDescription(candidate.Id, "Local candidate description"));
        Assert.Equal("Local candidate description", store.Get(candidate.Id)!.LocalizedDescription);
        Assert.Equal("Source description", store.Get(candidate.Id)!.Observation);
        Assert.Contains(store.ListTaskHistory(task.Id), item => item.EventType == "content" && item.PreviousValue!.Contains("Task description", StringComparison.Ordinal) && item.NewValue!.Contains("Updated task description", StringComparison.Ordinal));
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "tasks.db")}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE task_nodes SET build_status='passed' WHERE id=$id;";
            command.Parameters.AddWithValue("$id", task.Id);
            command.ExecuteNonQuery();
        }
        Assert.Contains(store.ListTaskHistory(task.Id), item => item.EventType == "build" && item.PreviousValue == "not_started" && item.NewValue == "passed");
    }

    [Fact]
    public void ManualTaskCanBeCreatedInCandidateProjectAndShowsItsAuthor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        var candidate = store.Receive(Note("BSR-MANUAL-TASK", "Manual task project", "A project with no imported tasks.") with
        {
            SourceProjectId = 34,
            CreatedBy = "Boy Scout user",
        }).Candidate;
        Assert.Empty(store.ListTaskNodes(34));
        Assert.Contains(store.ListProjectRepositories(), project => project.SourceProjectId == 34 && project.Project == candidate.Project);
        Assert.Equal(CodeHost.Github, store.SetProjectRepository(34, directory, CodeHost.Github)!.CodeHost);

        var created = store.CreateManualTask(new CreateManualTaskRequest(34, "Add a manual capability", "Describe the requested behavior.", "The user can verify the result.", null), "Denis")!;

        Assert.Null(created.SourceTaskId);
        Assert.Equal("todo", created.Status);
        Assert.Equal(TaskExecutionStatus.Idle, created.ExecutionStatus);
        Assert.Equal(ProjectBuildStatus.NotStarted, created.BuildStatus);
        Assert.Equal("Denis", created.CreatedBy);
        Assert.Equal(CodeHost.Github, created.CodeHost);
        Assert.Equal("The user can verify the result.", created.AcceptanceCriteria);
        var graphTask = Assert.Single(store.GetGraphSnapshot().Nodes, node => node.Key == $"task:{created.Id}");
        Assert.Equal("Denis", graphTask.CreatedBy);
        Assert.Equal(CodeHost.Github, graphTask.CodeHost);
        Assert.Equal("Boy Scout user", store.Get(candidate.Id)!.CreatedBy);
        Assert.Throws<ArgumentException>(() => store.CreateManualTask(new CreateManualTaskRequest(34, " ", "Missing title", null, null), "Denis"));
    }

    [Fact]
    public void NewProjectIsIdentifiedByRepositoryPathAndCanOwnManualTasksWithoutImportedProjectId()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));

        var project = store.CreateProject(directory, CodeHost.Github);
        var sameProject = store.CreateProject(directory, CodeHost.Gitlab);
        var created = store.CreateManualTask(new CreateManualTaskRequest(0, "A task in a new project", "Task description.", null, null, directory), "Denis")!;

        Assert.Equal(project.SourceProjectId, sameProject.SourceProjectId);
        Assert.Equal(Path.GetFullPath(directory), project.RepositoryPath);
        Assert.Equal(CodeHost.Github, project.CodeHost);
        Assert.Equal(project.SourceProjectId, created.SourceProjectId);
        Assert.Equal(project.Project, created.Project);
        Assert.Equal("Denis", created.CreatedBy);
        Assert.Equal(project, Assert.Single(store.ListProjectRepositories()));
    }

    [Fact]
    public void OpenMrContinuesPollingWhileTaskDevelopsAndCommits()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-MR-INDEPENDENT", "Review ongoing changes", "MR already exists.") with
        {
            SourceProjectId = 22, SourceTasks = [ImportedTask("BST-MR-INDEPENDENT", 22, "Review ongoing task")],
        });
        var task = Assert.Single(store.ListTaskNodes(22));
        store.SetProjectRepository(22, directory);
        const string url = "https://gitlab.com/org/project/-/merge_requests/22";
        var ongoing = store.UpdateTask(task.Id, new TaskUpdateRequest(url))!;

        Assert.Equal(MergeRequestStatus.Open, ongoing.MergeRequestStatus);
        Assert.Single(store.ListPollableMergeRequests());
        store.SaveMergeRequestComments(task.Id, url, [new MergeRequestCommentInput("note-1", "thread-1", "Check this change.", "Reviewer", "2026-10-22T12:00:00Z", null)]);
        Assert.NotNull(store.SaveMergeRequestReplyDraft(task.Id, "note-1", "We will fix it."));
        Assert.True(store.ConfirmMergeRequestReply(task.Id, "note-1"));

        Assert.Equal(MergeRequestStatus.Closed, store.UpdateMergeRequestStatus(task.Id, MergeRequestStatus.Closed)!.MergeRequestStatus);
        Assert.Empty(store.ListPollableMergeRequests());
        Assert.Null(store.TryClaimMergeRequestReply());
        Assert.Equal(MergeRequestStatus.Open, store.UpdateMergeRequestStatus(task.Id, MergeRequestStatus.Open)!.MergeRequestStatus);
        Assert.Single(store.ListPollableMergeRequests());
        Assert.Equal(MergeRequestStatus.Merged, store.UpdateMergeRequestStatus(task.Id, MergeRequestStatus.Merged)!.MergeRequestStatus);
        Assert.Empty(store.ListPollableMergeRequests());
    }

    [Fact]
    public void ExistingMrSourceBranchIsRequiredBeforeQueueAndPersistsIndependently()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-MR-BRANCH", "Continue existing MR", "The MR branch must be reused.") with
        {
            SourceProjectId = 30, SourceTasks = [ImportedTask("BST-MR-BRANCH", 30, "Continue existing MR")],
        });
        var task = Assert.Single(store.ListTaskNodes(30));
        const string url = "https://gitlab.com/group/project/-/merge_requests/30";
        store.UpdateTask(task.Id, new TaskUpdateRequest(url));
        store.SetProjectRepository(30, directory);
        Assert.NotNull(store.QueueTask(task.Id, out var missingBranch));
        Assert.Contains("source branch", missingBranch, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskExecutionStatus.Idle, store.GetTaskNode(task.Id)!.ExecutionStatus);

        Assert.True(store.SaveMergeRequestReview(task.Id, url, new MergeRequestPollResult(MergeRequestStatus.Open, "feature/reviewer-fixes", [], MergeRequestTargetBranch: "main")));

        var linked = store.GetTaskNode(task.Id)!;
        Assert.Equal("todo", linked.Status);
        Assert.Equal(MergeRequestStatus.Open, linked.MergeRequestStatus);
        Assert.Equal("feature/reviewer-fixes", linked.MergeRequestSourceBranch);
        Assert.Equal("main", linked.MergeRequestTargetBranch);
        Assert.Equal("main", new CandidateStore(Path.Combine(directory, "tasks.db")).GetTaskNode(task.Id)!.MergeRequestTargetBranch);
        Assert.Equal(TaskExecutionStatus.Queued, store.QueueTask(task.Id, out var queuedError)!.ExecutionStatus);
        Assert.Null(queuedError);
    }

    [Fact]
    public void MergeRequestPollResultUpdatesIndependentRemoteStateAndCommentEligibility()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-MR-STATE", "Monitor MR", "MR state must not change task phase.") with
        {
            SourceProjectId = 31, SourceTasks = [ImportedTask("BST-MR-STATE", 31, "Monitor MR")],
        });
        var task = Assert.Single(store.ListTaskNodes(31));
        const string url = "https://gitlab.com/group/project/-/merge_requests/31";
        store.UpdateTask(task.Id, new TaskUpdateRequest(url));

        Assert.True(store.SaveMergeRequestReview(task.Id, url,
            new MergeRequestPollResult(MergeRequestStatus.Closed, "feature/reviewer-fixes", [], MergeRequestTargetBranch: "main")));
        Assert.Equal("todo", store.GetTaskNode(task.Id)!.Status);
        Assert.Equal(MergeRequestStatus.Closed, store.GetTaskNode(task.Id)!.MergeRequestStatus);
        Assert.Empty(store.ListPollableMergeRequests());
        Assert.True(store.SaveMergeRequestReview(task.Id, url,
            new MergeRequestPollResult(MergeRequestStatus.Open, "feature/reviewer-fixes", [], MergeRequestTargetBranch: "main")));
        Assert.Equal("todo", store.GetTaskNode(task.Id)!.Status);
        Assert.Single(store.ListPollableMergeRequests());
    }

    [Fact]
    public async Task QueuedTaskIsExplainedOnceAndClarificationIsPersistedWithoutStartingDevelopment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using (var git = Process.Start(new ProcessStartInfo("git")
        {
            ArgumentList = { "init", directory }, RedirectStandardOutput = true, UseShellExecute = false,
        }))
        {
            Assert.NotNull(git);
            git.WaitForExit();
            Assert.Equal(0, git.ExitCode);
        }
        File.WriteAllText(Path.Combine(directory, "sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var path = Path.Combine(directory, "tasks.db");
        var store = new CandidateStore(path);
        store.Receive(Note("BSR-QUEUE-002", "Explain task", "Existing behavior is broken.") with
        {
            SourceProjectId = 18, SourceTasks = [ImportedTask("BST-QUEUE-002", 18, "Fix missing behavior")],
        });
        var task = Assert.Single(store.ListTaskNodes(18));
        store.SetProjectRepository(18, directory);
        store.QueueTask(task.Id, out _);
        var source = new FakeTaskPreflightSource(
            new TaskPreflightResult("Сейчас ошибка приводит к потере данных.", "Данные сохраняются после сбоя.",
                ["В коде найден риск при записи."], ["Старые данные должны читаться."],
                ["Пустой файл не должен ломать процесс."], ["Как обрабатывать повреждённые записи?"], null),
            new TaskPreflightResult("После уточнения причина ясна.", "Данные сохраняются после сбоя.",
                ["В коде найден риск при записи."], ["Старые данные должны читаться."],
                ["Пустой файл не должен ломать процесс."], [], "Можно выделить отдельную проверку."));
        using var worker = new TaskPreflightWorker(store, source, NullLogger<TaskPreflightWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(1, source.Attempts);
        Assert.Equal(TaskExecutionStatus.AwaitingClarification, store.GetTaskNode(task.Id)!.ExecutionStatus);
        Assert.Equal(ProjectBuildStatus.NotStarted, store.GetTaskNode(task.Id)!.BuildStatus);
        Assert.NotNull(store.ApproveTaskPreflight(task.Id, out var unanswered));
        Assert.NotNull(unanswered);
        Assert.Equal("Сохранять записи до подтверждения.", store.SaveTaskClarification(task.Id, "  Сохранять записи до подтверждения.  ")!.Answers);
        Assert.Equal(TaskExecutionStatus.Queued, store.GetTaskNode(task.Id)!.ExecutionStatus);
        await worker.ProcessOnceAsync(CancellationToken.None);
        Assert.Equal(2, source.Attempts);
        var review = new CandidateStore(path).GetTaskPreflight(task.Id)!;
        Assert.Equal("После уточнения причина ясна.", review.Problem);
        Assert.Empty(review.Questions);
        Assert.Equal("Сохранять записи до подтверждения.", review.Answers);
        Assert.Equal("Можно выделить отдельную проверку.", review.Decomposition);
        Assert.Equal(TaskExecutionStatus.ReadyForDevelopment, store.ApproveTaskPreflight(task.Id, out var approved)!.ExecutionStatus);
        Assert.Null(approved);
        Assert.Equal(ProjectBuildStatus.NotStarted, store.GetTaskNode(task.Id)!.BuildStatus);
    }

    [Fact]
    public void PreflightWithoutQuestionsCanBeApprovedButRejectedTaskCannot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using (var git = Process.Start(new ProcessStartInfo("git")
        {
            ArgumentList = { "init", directory }, RedirectStandardOutput = true, UseShellExecute = false,
        }))
        {
            Assert.NotNull(git);
            git.WaitForExit();
            Assert.Equal(0, git.ExitCode);
        }
        File.WriteAllText(Path.Combine(directory, "sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-QUEUE-005", "Approve task", "Simple requirement.") with
        {
            SourceProjectId = 20, SourceTasks = [ImportedTask("BST-QUEUE-005", 20, "Simple task")],
        });
        var task = Assert.Single(store.ListTaskNodes(20));
        store.SetProjectRepository(20, directory);
        store.QueueTask(task.Id, out _);
        Assert.Equal(task.Id, store.TryClaimTaskPreflight()!.Id);
        var result = new TaskPreflightResult("Проблема подтверждена.", "Изменение готово.", [], [], [], [], null);
        Assert.True(store.SaveTaskPreflight(task.Id, result));
        Assert.Equal(TaskExecutionStatus.ReadyForDevelopment, store.ApproveTaskPreflight(task.Id, out var approved)!.ExecutionStatus);
        Assert.Null(approved);
        store.RejectTask(task.Id);
        Assert.Equal("closed", store.ApproveTaskPreflight(task.Id, out var rejected)!.Status);
        Assert.NotNull(rejected);
    }

    [Fact]
    public void TaskWithPlaceholderAcceptanceCriteriaCannotPassPreflightApproval()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        var project = store.CreateProject(directory);
        var task = store.CreateManualTask(new CreateManualTaskRequest(0, "Raw task", "The task still needs acceptance criteria.", null, null, directory), "Denis")!;
        Assert.Equal(project.SourceProjectId, task.SourceProjectId);
        Assert.Equal("Критерии уточняются.", task.AcceptanceCriteria);

        store.QueueTask(task.Id, out _);
        Assert.Equal(task.Id, store.TryClaimTaskPreflight()!.Id);
        Assert.True(store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Проблема ясна.", "Результат ожидаем.", [], [], [], [], null)));

        var approval = store.ApproveTaskPreflight(task.Id, out var error)!;

        Assert.Equal(TaskExecutionStatus.AwaitingClarification, approval.ExecutionStatus);
        Assert.Contains("criteria", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(approval.WorktreePath);
        Assert.Equal(ProjectBuildStatus.NotStarted, approval.BuildStatus);
    }

    [Fact]
    public void EveryNewClarificationRoundMustBeAnsweredBeforeDevelopmentApproval()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using (var git = Process.Start(new ProcessStartInfo("git")
        {
            ArgumentList = { "init", "-b", "main", directory }, RedirectStandardOutput = true, UseShellExecute = false,
        }))
        {
            Assert.NotNull(git);
            git.WaitForExit();
            Assert.Equal(0, git.ExitCode);
        }
        File.WriteAllText(Path.Combine(directory, "app.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-CLARIFY-002", "Clarify requirements", "Two separate requirements need answers.") with
        {
            SourceProjectId = 33, SourceTasks = [ImportedTask("BST-CLARIFY-002", 33, "Clarify task")],
        });
        var task = Assert.Single(store.ListTaskNodes(33));
        store.SetProjectRepository(33, directory);
        store.QueueTask(task.Id, out _);
        store.TryClaimTaskPreflight();
        Assert.True(store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Проблема понятна.", "Готово.", [], [], [], ["Вопрос 1?"], null)));

        store.SaveTaskClarification(task.Id, "Ответ 1.");
        Assert.Equal(task.Id, store.TryClaimTaskPreflight()!.Id);
        Assert.True(store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Уточнение учтено.", "Готово.", [], [], [], ["Вопрос 2?"], null)));
        var secondRound = store.GetTaskPreflight(task.Id)!;
        Assert.True(secondRound.QuestionsPending);
        Assert.Equal("Ответ 1.", secondRound.Answers);
        Assert.NotNull(store.ApproveTaskPreflight(task.Id, out var stillPending));
        Assert.NotNull(stillPending);

        store.SaveTaskClarification(task.Id, "Ответ 2.");
        Assert.Equal(task.Id, store.TryClaimTaskPreflight()!.Id);
        Assert.True(store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Оба требования уточнены.", "Готово.", [], [], [], [], "Разбить на две проверки.")));
        var finalRound = store.GetTaskPreflight(task.Id)!;
        Assert.False(finalRound.QuestionsPending);
        Assert.Contains("Ответ 1.", finalRound.Answers);
        Assert.Contains("Ответ 2.", finalRound.Answers);
        Assert.Equal("Разбить на две проверки.", finalRound.Decomposition);
        Assert.Equal(TaskExecutionStatus.ReadyForDevelopment, store.ApproveTaskPreflight(task.Id, out var approved)!.ExecutionStatus);
        Assert.Null(approved);
    }

    [Fact]
    public void RejectedTaskCannotBeClaimedAndInterruptedPreflightIsRequeued()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using (var git = Process.Start(new ProcessStartInfo("git")
        {
            ArgumentList = { "init", directory }, RedirectStandardOutput = true, UseShellExecute = false,
        }))
        {
            Assert.NotNull(git);
            git.WaitForExit();
            Assert.Equal(0, git.ExitCode);
        }
        var path = Path.Combine(directory, "tasks.db");
        var store = new CandidateStore(path);
        store.Receive(Note("BSR-QUEUE-003", "Explain task", "Existing behavior is broken.") with
        {
            SourceProjectId = 19,
            SourceTasks = [ImportedTask("BST-QUEUE-003", 19, "First task"), ImportedTask("BST-QUEUE-004", 19, "Rejected task")],
        });
        store.SetProjectRepository(19, directory);
        var first = store.ListTaskNodes(19).Single(task => task.Title == "First task");
        var rejected = store.ListTaskNodes(19).Single(task => task.Title == "Rejected task");
        store.QueueTask(first.Id, out _);
        store.QueueTask(rejected.Id, out _);
        store.RejectTask(rejected.Id);
        Assert.Equal(first.Id, store.TryClaimTaskPreflight()!.Id);
        Assert.Null(store.TryClaimTaskPreflight());

        var restarted = new CandidateStore(path);
        Assert.Equal(TaskExecutionStatus.Queued, restarted.GetTaskNode(first.Id)!.ExecutionStatus);
        Assert.Equal(first.Id, restarted.TryClaimTaskPreflight()!.Id);
        Assert.Equal("closed", restarted.GetTaskNode(rejected.Id)!.Status);
    }

    [Fact]
    public void PreflightParserRequiresStructuredExplanation()
    {
        var result = TaskPreflightAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"problem\":\"Ошибка при сохранении.\",\"expectedResult\":\"Сохранение работает.\",\"evidence\":[\"Файл хранения.\"],\"risks\":[],\"cornerCases\":[],\"questions\":[],\"decomposition\":null}"}}
            """);
        Assert.Equal("Ошибка при сохранении.", result.Problem);
        var pathEvidence = TaskPreflightAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"problem\":\"Ошибка при сохранении комментария.\",\"expectedResult\":\"Комментарий сохраняется после перезапуска.\",\"evidence\":[\"src/BoyScout.Core/Tasks/TaskStore.cs:120\"],\"risks\":[],\"cornerCases\":[],\"questions\":[],\"decomposition\":null}"}}
            """);
        Assert.Contains("TaskStore.cs", Assert.Single(pathEvidence.Evidence));
        Assert.Throws<InvalidOperationException>(() => TaskPreflightAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"problem\":\"\",\"expectedResult\":\"Готово.\",\"evidence\":[],\"risks\":[],\"cornerCases\":[],\"questions\":[],\"decomposition\":null}"}}
            """));
    }

    [Fact]
    public async Task ApprovedTaskCreatesIsolatedWorktreeAndNeverPushes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checkout = Path.Combine(directory, "checkout");
        var remote = Path.Combine(directory, "remote.git");
        RunGit(directory, "init", "--bare", remote);
        RunGit(directory, "init", "-b", "main", checkout);
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "--allow-empty", "-m", "initial");
        RunGit(checkout, "remote", "add", "origin", remote);

        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-WORKTREE-001", "Create worktree", "Implement change.") with
        {
            SourceProjectId = 21, SourceTasks = [ImportedTask("BST-WORKTREE-001", 21, "Implement change")],
        });
        var task = Assert.Single(store.ListTaskNodes(21));
        store.SetProjectRepository(21, checkout);
        store.QueueTask(task.Id, out _);
        Assert.Equal(task.Id, store.TryClaimTaskPreflight()!.Id);
        Assert.True(store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Проблема ясна.", "Исправление готово.", [], [], [], [], null)));
        store.ApproveTaskPreflight(task.Id, out _);
        using var worker = new TaskWorktreeWorker(store, new TaskWorktreePreparer(), NullLogger<TaskWorktreeWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var prepared = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.Prepared, prepared.ExecutionStatus);
        Assert.Equal("main", prepared.BaseBranch);
        Assert.NotNull(prepared.WorktreePath);
        Assert.True(File.Exists(Path.Combine(prepared.WorktreePath, ".git")));
        Assert.StartsWith("todo-graph/task-", prepared.BranchName);
        Assert.Empty(RunGit(checkout, "ls-remote", "--heads", "origin").Trim());
        Assert.Null(store.TryClaimWorktreePreparation());
    }

    [Fact]
    public async Task WorktreeFailurePreservesReservationAndCanBeRetriedAfterOriginIsConfigured()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checkout = Path.Combine(directory, "checkout");
        RunGit(directory, "init", "-b", "main", checkout);
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "--allow-empty", "-m", "initial");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-WORKTREE-002", "Create worktree", "Implement change.") with
        {
            SourceProjectId = 23, SourceTasks = [ImportedTask("BST-WORKTREE-002", 23, "Implement change")],
        });
        var task = Assert.Single(store.ListTaskNodes(23));
        store.SetProjectRepository(23, checkout);
        store.QueueTask(task.Id, out _);
        store.TryClaimTaskPreflight();
        store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Проблема ясна.", "Исправление готово.", [], [], [], [], null));
        store.ApproveTaskPreflight(task.Id, out _);
        using var worker = new TaskWorktreeWorker(store, new TaskWorktreePreparer(), NullLogger<TaskWorktreeWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var blocked = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.Blocked, blocked.ExecutionStatus);
        Assert.Equal(ProjectBuildStatus.NotStarted, blocked.BuildStatus);
        Assert.NotNull(blocked.WorktreePath);
        Assert.False(Directory.Exists(blocked.WorktreePath));
        Assert.NotNull(store.QueueTask(task.Id, out var queueError));
        Assert.NotNull(queueError);
        var remote = Path.Combine(directory, "remote.git");
        RunGit(directory, "init", "--bare", remote);
        RunGit(checkout, "remote", "add", "origin", remote);
        Assert.Equal(TaskExecutionStatus.RetryWorktree, store.RetryWorktreePreparation(task.Id, out var retryError)!.ExecutionStatus);
        Assert.Null(retryError);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var prepared = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.Prepared, prepared.ExecutionStatus);
        Assert.Equal(blocked.WorktreePath, prepared.WorktreePath);
        Assert.Equal(blocked.BranchName, prepared.BranchName);
    }

    [Fact]
    public async Task ExistingOpenMrUsesItsSourceBranchForTheSingleTaskWorktree()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checkout = Path.Combine(directory, "checkout");
        var remote = Path.Combine(directory, "remote.git");
        RunGit(directory, "init", "--bare", remote);
        RunGit(directory, "init", "-b", "main", checkout);
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "--allow-empty", "-m", "initial");
        RunGit(checkout, "remote", "add", "origin", remote);
        RunGit(checkout, "push", "-u", "origin", "main");
        RunGit(checkout, "checkout", "-b", "feature/mr-fixes");
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "--allow-empty", "-m", "MR commit");
        RunGit(checkout, "push", "-u", "origin", "feature/mr-fixes");
        RunGit(checkout, "checkout", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-MR-WORKTREE", "Continue MR", "Fix comments on the existing MR.") with
        {
            SourceProjectId = 32, SourceTasks = [ImportedTask("BST-MR-WORKTREE", 32, "Continue existing MR")],
        });
        var task = Assert.Single(store.ListTaskNodes(32));
        const string mrUrl = "https://gitlab.com/group/project/-/merge_requests/32";
        store.UpdateTask(task.Id, new TaskUpdateRequest(mrUrl));
        store.SaveMergeRequestReview(task.Id, mrUrl, new MergeRequestPollResult(MergeRequestStatus.Open, "feature/mr-fixes", [], MergeRequestTargetBranch: "main"));
        store.SetProjectRepository(32, checkout);
        store.QueueTask(task.Id, out _);
        store.TryClaimTaskPreflight();
        store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Задача ясна.", "Комментарии обработаны.", [], [], [], [], null));
        store.ApproveTaskPreflight(task.Id, out _);
        using var worker = new TaskWorktreeWorker(store, new TaskWorktreePreparer(), NullLogger<TaskWorktreeWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var prepared = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.Prepared, prepared.ExecutionStatus);
        Assert.Equal("feature/mr-fixes", prepared.BranchName);
        Assert.Equal("feature/mr-fixes", prepared.BaseBranch);
        Assert.Equal("feature/mr-fixes", RunGit(prepared.WorktreePath!, "branch", "--show-current").Trim());
        Assert.Equal("main", RunGit(checkout, "branch", "--show-current").Trim());

        File.WriteAllText(Path.Combine(checkout, "target-only.txt"), "The saved MR target.\n");
        RunGit(checkout, "add", "target-only.txt");
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "-m", "advance MR target");
        RunGit(checkout, "push", "origin", "main");
        Assert.Equal("main", prepared.MergeRequestTargetBranch);
        Assert.Equal(MergeRequestStatus.Open, prepared.MergeRequestStatus);
        await new TaskGitOperations().RebaseAsync(prepared, CancellationToken.None);
        Assert.Equal(RunGit(checkout, "rev-parse", "main").Trim(), RunGit(prepared.WorktreePath!, "rev-parse", "HEAD^1").Trim());
        Assert.Equal("The saved MR target.", File.ReadAllText(Path.Combine(prepared.WorktreePath!, "target-only.txt")).Trim());

        store.Receive(Note("BSR-MR-WORKTREE-2", "Duplicate branch", "Do not share an active MR branch.") with
        {
            SourceProjectId = 32, SourceTasks = [ImportedTask("BST-MR-WORKTREE-2", 32, "Another task on same MR")],
        });
        var second = store.ListTaskNodes(32).Single(node => node.SourceTaskId == "BST-MR-WORKTREE-2");
        store.UpdateTask(second.Id, new TaskUpdateRequest(mrUrl));
        store.SaveMergeRequestReview(second.Id, mrUrl, new MergeRequestPollResult(MergeRequestStatus.Open, "feature/mr-fixes", [], MergeRequestTargetBranch: "main"));
        store.QueueTask(second.Id, out _);
        store.TryClaimTaskPreflight();
        store.SaveTaskPreflight(second.Id, new TaskPreflightResult("Вторая задача ясна.", "Нет дублирующего worktree.", [], [], [], [], null));
        store.ApproveTaskPreflight(second.Id, out _);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var conflict = store.GetTaskNode(second.Id)!;
        Assert.Equal(TaskExecutionStatus.Blocked, conflict.ExecutionStatus);
        Assert.Contains("уже открыта", conflict.ExecutionError, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(conflict.WorktreePath));
    }

    [Fact]
    public async Task DeveloperRepairsFailedBuildThenWaitsForLocalCommitWithoutPushing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-DEV-001", "Develop", "Fix a build problem.") with
        {
            SourceProjectId = 24, SourceTasks = [ImportedTask("BST-DEV-001", 24, "Fix build")],
        });
        var task = Assert.Single(store.ListTaskNodes(24));
        store.SetProjectRepository(24, directory);
        store.QueueTask(task.Id, out _);
        store.TryClaimTaskPreflight();
        store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Проблема ясна.", "Проект собирается.", [], [], [], [], null));
        store.ApproveTaskPreflight(task.Id, out _);
        var reserved = store.TryClaimWorktreePreparation()!;
        Directory.CreateDirectory(reserved.WorktreePath!);
        store.FinishWorktreePreparation(task.Id, "main");
        var developer = new FakeImplementationAgent();
        var builder = new FakeBuildRunner(
            [new TaskBuildResult("dotnet test", 1, "A compile error."), new TaskBuildResult("dotnet test", 0, "Build succeeded.")]);
        using var worker = new TaskImplementationWorker(store, developer, builder, NullLogger<TaskImplementationWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(2, developer.Attempts);
        Assert.Contains("A compile error", developer.BuildFailures[1]);
        Assert.Equal(2, builder.Attempts);
        Assert.Equal(TaskExecutionStatus.AwaitingCommit, store.GetTaskNode(task.Id)!.ExecutionStatus);
        Assert.Equal(ProjectBuildStatus.Passed, store.GetTaskNode(task.Id)!.BuildStatus);
    }

    [Fact]
    public void BuildCommandIsDetectedWithoutRunningUntrustedTaskText()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Assert.Throws<InvalidOperationException>(() => ProjectBuildRunner.DetectBuild(directory));
        File.WriteAllText(Path.Combine(directory, "app.sln"), "test");
        var command = ProjectBuildRunner.DetectBuild(directory);
        Assert.Equal("dotnet", command.Executable);
        Assert.Equal("test", command.Arguments[0]);
    }

    [Fact]
    public async Task SuccessfulBuildCommitsLocallyAndOnlyTheHumanPushChangesRemoteState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checkout = Path.Combine(directory, "checkout");
        var remote = Path.Combine(directory, "remote.git");
        RunGit(directory, "init", "--bare", remote);
        RunGit(directory, "init", "-b", "main", checkout);
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "--allow-empty", "-m", "initial");
        RunGit(checkout, "remote", "add", "origin", remote);
        RunGit(checkout, "push", "origin", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-COMMIT-001", "Implement", "Prepare local commit.") with
        {
            SourceProjectId = 25, SourceTasks = [ImportedTask("BST-COMMIT-001", 25, "Implement improvement")],
        });
        var task = Assert.Single(store.ListTaskNodes(25));
        store.SetProjectRepository(25, checkout);
        store.QueueTask(task.Id, out _);
        store.TryClaimTaskPreflight();
        store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Проблема ясна.", "Проект собирается.", [], [], [], [], null));
        store.ApproveTaskPreflight(task.Id, out _);
        using (var worktreeWorker = new TaskWorktreeWorker(store, new TaskWorktreePreparer(), NullLogger<TaskWorktreeWorker>.Instance))
            await worktreeWorker.ProcessOnceAsync(CancellationToken.None);
        var prepared = store.GetTaskNode(task.Id)!;
        store.TryClaimImplementation();
        store.SetTaskBuildState(task.Id, ProjectBuildStatus.Passed);
        Assert.True(store.CompleteImplementation(task.Id));
        File.WriteAllText(Path.Combine(checkout, "target.txt"), "Advanced target.\n");
        RunGit(checkout, "add", "target.txt");
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "-m", "advance target");
        RunGit(checkout, "push", "origin", "main");
        File.WriteAllText(Path.Combine(prepared.WorktreePath!, "feature.txt"), "Staged task change.\n");
        RunGit(prepared.WorktreePath!, "add", "feature.txt");
        File.WriteAllText(Path.Combine(prepared.WorktreePath!, "unstaged.txt"), "Unstaged task change.\n");
        File.WriteAllText(Path.Combine(prepared.WorktreePath!, "untracked.txt"), "Untracked task change.\n");
        var git = new TaskGitOperations(new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = "Test", ["GIT_AUTHOR_EMAIL"] = "test@example.org",
            ["GIT_COMMITTER_NAME"] = "Test", ["GIT_COMMITTER_EMAIL"] = "test@example.org",
        });
        var headAtBuild = string.Empty;
        var rebuild = new FakeBuildRunner([new TaskBuildResult("dotnet test", 0, "Build succeeded.")], _ =>
        {
            headAtBuild = RunGit(prepared.WorktreePath!, "rev-parse", "HEAD");
            Assert.Equal("Advanced target.", File.ReadAllText(Path.Combine(prepared.WorktreePath!, "target.txt")).Trim());
            Assert.True(File.Exists(Path.Combine(prepared.WorktreePath!, "feature.txt")));
            Assert.True(File.Exists(Path.Combine(prepared.WorktreePath!, "unstaged.txt")));
            Assert.True(File.Exists(Path.Combine(prepared.WorktreePath!, "untracked.txt")));
        });
        using var worker = new TaskCommitWorker(store, git, rebuild, NullLogger<TaskCommitWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var committed = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.AwaitingPush, committed.ExecutionStatus);
        Assert.Equal(1, rebuild.Attempts);
        Assert.Equal(RunGit(checkout, "rev-parse", "main"), headAtBuild);
        Assert.NotEqual(headAtBuild, committed.CommitSha);
        Assert.Equal("Staged task change.", File.ReadAllText(Path.Combine(prepared.WorktreePath!, "feature.txt")).Trim());
        Assert.Equal("Unstaged task change.", File.ReadAllText(Path.Combine(prepared.WorktreePath!, "unstaged.txt")).Trim());
        Assert.Equal("Untracked task change.", File.ReadAllText(Path.Combine(prepared.WorktreePath!, "untracked.txt")).Trim());
        Assert.Matches("^[a-f0-9]{40,64}$", committed.CommitSha!);
        Assert.Equal(string.Empty, RunGit(checkout, "ls-remote", "--heads", "origin", prepared.BranchName!));
        RunGit(remote, "fetch", prepared.WorktreePath!, $"{prepared.BranchName}:refs/heads/{prepared.BranchName}"); // Populate the test remote as if a human pushed; no push is run by the agent.

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(TaskExecutionStatus.AwaitingMrRepositoryUrl, store.GetTaskNode(task.Id)!.ExecutionStatus);
        var projectWithRemoteUrl = store.SetProjectRemoteRepositoryUrl(25, "https://gitlab.com/group/project")!;
        Assert.Equal("https://gitlab.com/group/project", projectWithRemoteUrl.RemoteRepositoryUrl);
        Assert.Equal(TaskExecutionStatus.Pushed, store.GetTaskNode(task.Id)!.ExecutionStatus);

        var creator = new FakeMergeRequestCreator("https://gitlab.com/group/project/-/merge_requests/25");
        using var mrWorker = new MergeRequestCreationWorker(store, creator, git, NullLogger<MergeRequestCreationWorker>.Instance);
        await mrWorker.ProcessOnceAsync(CancellationToken.None);
        var linked = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.CodeReview, linked.ExecutionStatus);
        Assert.Equal(MergeRequestStatus.Open, linked.MergeRequestStatus);
        Assert.Equal("todo", linked.Status);
        Assert.Single(store.ListPollableMergeRequests());
        Assert.Equal(1, creator.Calls);
        await mrWorker.ProcessOnceAsync(CancellationToken.None);
        Assert.Equal(1, creator.Calls);
    }

    [Fact]
    public async Task FetchFailureBlocksCommitAndPreservesWorktreeForHumanResolution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checkout = Path.Combine(directory, "checkout");
        var remote = Path.Combine(directory, "remote.git");
        RunGit(directory, "init", "--bare", remote);
        RunGit(directory, "init", "-b", "main", checkout);
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "--allow-empty", "-m", "initial");
        RunGit(checkout, "remote", "add", "origin", remote);
        RunGit(checkout, "push", "origin", "main");

        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-COMMIT-FETCH-001", "Block failed rebase", "Keep changes for manual recovery.") with
        {
            SourceProjectId = 251, SourceTasks = [ImportedTask("BST-COMMIT-FETCH-001", 251, "Keep worktree")],
        });
        var task = Assert.Single(store.ListTaskNodes(251));
        store.SetProjectRepository(251, checkout);
        store.QueueTask(task.Id, out _);
        store.TryClaimTaskPreflight();
        store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Clear.", "Complete.", [], [], [], [], null));
        store.ApproveTaskPreflight(task.Id, out _);
        using (var worktreeWorker = new TaskWorktreeWorker(store, new TaskWorktreePreparer(), NullLogger<TaskWorktreeWorker>.Instance))
            await worktreeWorker.ProcessOnceAsync(CancellationToken.None);
        var prepared = store.GetTaskNode(task.Id)!;
        store.TryClaimImplementation();
        store.SetTaskBuildState(task.Id, ProjectBuildStatus.Passed);
        Assert.True(store.CompleteImplementation(task.Id));
        var before = RunGit(prepared.WorktreePath!, "rev-parse", "HEAD").Trim();
        const string localChange = "Preserve during fetch failure.\n";
        File.WriteAllText(Path.Combine(prepared.WorktreePath!, "task-change.txt"), localChange);
        RunGit(remote, "update-ref", "-d", "refs/heads/main");

        var git = new TaskGitOperations(new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = "Test", ["GIT_AUTHOR_EMAIL"] = "test@example.org",
            ["GIT_COMMITTER_NAME"] = "Test", ["GIT_COMMITTER_EMAIL"] = "test@example.org",
        });
        var rebuild = new FakeBuildRunner([new TaskBuildResult("dotnet test", 0, "Should not run." )]);
        using var worker = new TaskCommitWorker(store, git, rebuild, NullLogger<TaskCommitWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var blocked = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.Blocked, blocked.ExecutionStatus);
        Assert.Equal(ProjectBuildStatus.NotStarted, blocked.BuildStatus);
        Assert.Null(blocked.CommitSha);
        Assert.Contains(prepared.WorktreePath!, blocked.ExecutionError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no local commit", blocked.ExecutionError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, rebuild.Attempts);
        Assert.True(Directory.Exists(prepared.WorktreePath));
        Assert.Equal(localChange, File.ReadAllText(Path.Combine(prepared.WorktreePath!, "task-change.txt")));
        Assert.Equal(before, RunGit(prepared.WorktreePath!, "rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task AutostashConflictBlocksCommitAndPreservesUnresolvedWorktreeForHumanResolution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checkout = Path.Combine(directory, "checkout");
        var remote = Path.Combine(directory, "remote.git");
        RunGit(directory, "init", "--bare", remote);
        RunGit(directory, "init", "-b", "main", checkout);
        File.WriteAllText(Path.Combine(checkout, "shared.txt"), "base value\n");
        RunGit(checkout, "add", "shared.txt");
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "-m", "initial");
        RunGit(checkout, "remote", "add", "origin", remote);
        RunGit(checkout, "push", "origin", "main");

        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-COMMIT-CONFLICT-001", "Block conflicting rebase", "Keep conflict state for manual recovery.") with
        {
            SourceProjectId = 252, SourceTasks = [ImportedTask("BST-COMMIT-CONFLICT-001", 252, "Resolve conflict")],
        });
        var task = Assert.Single(store.ListTaskNodes(252));
        store.SetProjectRepository(252, checkout);
        store.QueueTask(task.Id, out _);
        store.TryClaimTaskPreflight();
        store.SaveTaskPreflight(task.Id, new TaskPreflightResult("Clear.", "Complete.", [], [], [], [], null));
        store.ApproveTaskPreflight(task.Id, out _);
        using (var worktreeWorker = new TaskWorktreeWorker(store, new TaskWorktreePreparer(), NullLogger<TaskWorktreeWorker>.Instance))
            await worktreeWorker.ProcessOnceAsync(CancellationToken.None);
        var prepared = store.GetTaskNode(task.Id)!;
        store.TryClaimImplementation();
        store.SetTaskBuildState(task.Id, ProjectBuildStatus.Passed);
        Assert.True(store.CompleteImplementation(task.Id));

        const string taskVersion = "task version\n";
        File.WriteAllText(Path.Combine(prepared.WorktreePath!, "shared.txt"), taskVersion);
        File.WriteAllText(Path.Combine(checkout, "shared.txt"), "target version\n");
        RunGit(checkout, "add", "shared.txt");
        RunGit(checkout, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "-m", "conflicting target change");
        RunGit(checkout, "push", "origin", "main");
        var targetCommit = RunGit(checkout, "rev-parse", "HEAD").Trim();

        var git = new TaskGitOperations(new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = "Test", ["GIT_AUTHOR_EMAIL"] = "test@example.org",
            ["GIT_COMMITTER_NAME"] = "Test", ["GIT_COMMITTER_EMAIL"] = "test@example.org",
        });
        var rebuild = new FakeBuildRunner([new TaskBuildResult("dotnet test", 0, "Should not run.")]);
        using var worker = new TaskCommitWorker(store, git, rebuild, NullLogger<TaskCommitWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var blocked = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.Blocked, blocked.ExecutionStatus);
        Assert.Equal(ProjectBuildStatus.NotStarted, blocked.BuildStatus);
        Assert.Null(blocked.CommitSha);
        Assert.Equal(0, rebuild.Attempts);
        Assert.Contains(prepared.WorktreePath!, blocked.ExecutionError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resolve", blocked.ExecutionError, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(prepared.WorktreePath));
        Assert.Equal(targetCommit, RunGit(prepared.WorktreePath!, "rev-parse", "HEAD").Trim());
        Assert.Contains("<<<<<<<", File.ReadAllText(Path.Combine(prepared.WorktreePath!, "shared.txt")), StringComparison.Ordinal);
        Assert.Contains("UU shared.txt", RunGit(prepared.WorktreePath!, "status", "--short"), StringComparison.Ordinal);
    }

    [Fact]
    public void MrCreationResponseRequiresAConfirmedUrl()
    {
        var response = MergeRequestCreationAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"url\":\"https://gitlab.com/org/project/-/merge_requests/1\"}"}}
            """);
        Assert.EndsWith("/1", response);
        Assert.Throws<InvalidOperationException>(() => MergeRequestCreationAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"url\":\"not-a-url\"}"}}
            """));
    }

    [Fact]
    public void DeveloperCanChooseReplyOnlyForCommentRound()
    {
        var result = TaskImplementationAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"action\":\"reply_only\",\"summary\":\"Изменение кода не требуется.\",\"question\":\"\"}"}}
            """);
        Assert.Equal("reply_only", result.Action);
        Assert.Equal("", result.Question);
        Assert.Throws<InvalidOperationException>(() => TaskImplementationAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"action\":\"reply_only\",\"summary\":\"Изменение кода не требуется.\",\"question\":null}"}}
            """));
    }

    [Fact]
    public void GitLabMcpSettingsEncryptTokensAndGenerateExactToolAllowlist()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var keys = Path.Combine(directory, "keys");
        Directory.CreateDirectory(keys);
        using var services = new ServiceCollection()
            .AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys)).Services
            .BuildServiceProvider();
        var protector = services.GetRequiredService<IDataProtectionProvider>();
        var database = Path.Combine(directory, "settings.db");
        var settings = new GitLabMcpSettingsStore(database, protector);
        const string readToken = "read-secret-for-test-123456";
        const string writeToken = "write-secret-for-test-123456";
        const string githubToken = "github-read-secret-for-test-123456";
        var configured = settings.Save(new GitLabMcpSettingsUpdate("Denis",
            new LocalMcpServerUpdate(true, "npx", ["-y", "gitlab-mcp"], "GITLAB_TOKEN", readToken, false,
                ["get_merge_request", "list_merge_request_discussions"]),
            new LocalMcpServerUpdate(true, "npx", ["-y", "gitlab-mcp"], "GITLAB_TOKEN", writeToken, false,
                ["create_merge_request", "create_discussion_note", "list_merge_requests"]),
            new LocalMcpServerUpdate(true, "npx", ["-y", "github-mcp"], "GITHUB_TOKEN", githubToken, false,
                ["get_pull_request", "list_pull_request_comments"])));

        Assert.True(configured.Read.HasToken);
        Assert.True(configured.Write.HasToken);
        Assert.True(configured.GitHubRead.HasToken);
        using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT read_token_protected || write_token_protected || github_token_protected FROM gitlab_mcp_settings WHERE id=1;";
            var protectedValues = (string)command.ExecuteScalar()!;
            Assert.DoesNotContain(readToken, protectedValues, StringComparison.Ordinal);
            Assert.DoesNotContain(writeToken, protectedValues, StringComparison.Ordinal);
            Assert.DoesNotContain(githubToken, protectedValues, StringComparison.Ordinal);
        }
        Assert.Equal(readToken, settings.GetRuntimeSettings().Read.Token);
        Assert.Equal(writeToken, settings.GetRuntimeSettings().Write.Token);
        Assert.Equal(githubToken, settings.GetRuntimeSettings().GitHubRead!.Token);

        var profileDirectory = Path.Combine(directory, ".opencode");
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(Path.Combine(profileDirectory, "todo-graph.json"), """
            {"agent":{"todo-graph-mr-reader":{"permission":{"*":"deny","gitlab_*":"deny"}},"todo-graph-pr-reader":{"permission":{"*":"deny","github_*":"deny"}},"todo-graph-mr-replier":{"permission":{"*":"deny","gitlab-write_*":"deny"}},"todo-graph-mr-creator":{"permission":{"*":"deny","gitlab-write_*":"deny"}}}}
            """);
        using var launch = new OpenCodeMcpConfiguration(settings, new FakeWebHostEnvironment(directory)).Create("todo-graph-mr-creator");
        var process = new ProcessStartInfo("opencode");
        launch.Apply(process);
        var generated = File.ReadAllText(process.Environment["OPENCODE_CONFIG"]!);
        Assert.DoesNotContain(readToken, generated, StringComparison.Ordinal);
        Assert.DoesNotContain(writeToken, generated, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(generated);
        var permissions = document.RootElement.GetProperty("agent").GetProperty("todo-graph-mr-creator").GetProperty("permission");
        Assert.Equal("allow", permissions.GetProperty("gitlab-write_create_merge_request").GetString());
        Assert.Equal("deny", permissions.GetProperty("gitlab-write_*").GetString());
        Assert.False(permissions.TryGetProperty("gitlab-write_push_files", out _));
        Assert.Equal(writeToken, process.Environment["TODO_GRAPH_GITLAB_WRITE_TOKEN"]);
        using var githubLaunch = new OpenCodeMcpConfiguration(settings, new FakeWebHostEnvironment(directory)).Create("todo-graph-pr-reader");
        var githubProcess = new ProcessStartInfo("opencode");
        githubLaunch.Apply(githubProcess);
        var githubConfigPath = githubProcess.Environment["OPENCODE_CONFIG"]!;
        var githubGenerated = File.ReadAllText(githubConfigPath);
        Assert.DoesNotContain(githubToken, githubGenerated, StringComparison.Ordinal);
        using var githubConfig = System.Text.Json.JsonDocument.Parse(githubGenerated);
        Assert.True(githubConfig.RootElement.GetProperty("mcp").TryGetProperty("github", out _));
        Assert.False(githubConfig.RootElement.GetProperty("mcp").TryGetProperty("gitlab-write", out _));
        Assert.Equal("allow", githubConfig.RootElement.GetProperty("agent").GetProperty("todo-graph-pr-reader")
            .GetProperty("permission").GetProperty("github_get_pull_request").GetString());
        Assert.Equal(githubToken, githubProcess.Environment["TODO_GRAPH_GITHUB_READ_TOKEN"]);

        var readUpdate = new LocalMcpServerUpdate(configured.Read.Enabled, configured.Read.Executable,
            configured.Read.Arguments, configured.Read.TokenEnvironmentVariable, null, false, configured.Read.Tools);
        var invalidWrite = new LocalMcpServerUpdate(configured.Write.Enabled, configured.Write.Executable,
            configured.Write.Arguments, configured.Write.TokenEnvironmentVariable, null, false, ["push_files"]);
        var githubUpdate = new LocalMcpServerUpdate(false, "", [], "GITHUB_TOKEN", null, false, []);
        Assert.Throws<ArgumentException>(() => settings.Save(new GitLabMcpSettingsUpdate("Denis", readUpdate, invalidWrite, githubUpdate)));
    }

    [Fact]
    public async Task NewOpenMrCommentStartsIndependentReviewCycleAndReplyOnlyDoesNotCommit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "tasks.db");
        var store = new CandidateStore(databasePath);
        store.Receive(Note("BSR-MR-ROUND-001", "Inspect review", "Task already has an MR.") with
        {
            SourceProjectId = 26, SourceTasks = [ImportedTask("BST-MR-ROUND-001", 26, "Handle reviewer feedback")],
        });
        var task = Assert.Single(store.ListTaskNodes(26));
        const string mrUrl = "https://gitlab.com/group/project/-/merge_requests/26";
        store.UpdateTask(task.Id, new TaskUpdateRequest(mrUrl));
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE task_nodes SET execution_status='code_review', worktree_path=$path WHERE id=$id;";
            command.Parameters.AddWithValue("$path", directory);
            command.Parameters.AddWithValue("$id", task.Id);
            command.ExecuteNonQuery();
        }
        var poller = new FakeMergeRequestCommentSource(
            [new MergeRequestCommentInput("note-26", "thread-26", "Please clarify the edge case.", "Reviewer", "2026-10-22T12:00:00Z", null)]);
        using var reviewWorker = new MergeRequestReviewWorker(store, poller, NullLogger<MergeRequestReviewWorker>.Instance);

        await reviewWorker.PollOnceAsync(CancellationToken.None);

        Assert.Equal(TaskExecutionStatus.ReviewingComments, store.GetTaskNode(task.Id)!.ExecutionStatus);
        var developer = new FakeImplementationAgent("reply_only");
        var build = new FakeBuildRunner([]);
        using var implementationWorker = new TaskImplementationWorker(store, developer, build, NullLogger<TaskImplementationWorker>.Instance);

        await implementationWorker.ProcessOnceAsync(CancellationToken.None);

        var after = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.CommentsReviewed, after.ExecutionStatus);
        Assert.Equal("todo", after.Status);
        Assert.Equal(ProjectBuildStatus.NotStarted, after.BuildStatus);
        var comment = Assert.Single(store.ListMergeRequestComments(task.Id));
        Assert.NotNull(comment.AgentHandledAt);
        Assert.Null(comment.ReviewedAt);
        Assert.Equal(0, build.Attempts);

        Assert.True(store.SaveMergeRequestComments(task.Id, mrUrl,
            [new MergeRequestCommentInput("note-27b", "thread-27b", "One more detail.", "Reviewer", "2026-10-22T12:01:00Z", null)]));
        Assert.Equal(TaskExecutionStatus.ReviewingComments, store.GetTaskNode(task.Id)!.ExecutionStatus);
    }

    [Fact]
    public async Task CodeChangesFromMrReviewMarkCommentsHandledOnlyAfterBuildPasses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "tasks.db");
        var store = new CandidateStore(databasePath);
        store.Receive(Note("BSR-MR-ROUND-002", "Inspect review", "Task already has an MR.") with
        {
            SourceProjectId = 27, SourceTasks = [ImportedTask("BST-MR-ROUND-002", 27, "Apply reviewer feedback")],
        });
        var task = Assert.Single(store.ListTaskNodes(27));
        const string mrUrl = "https://gitlab.com/group/project/-/merge_requests/27";
        store.UpdateTask(task.Id, new TaskUpdateRequest(mrUrl));
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE task_nodes SET execution_status='code_review', worktree_path=$path WHERE id=$id;";
            command.Parameters.AddWithValue("$path", directory);
            command.Parameters.AddWithValue("$id", task.Id);
            command.ExecuteNonQuery();
        }
        var poller = new FakeMergeRequestCommentSource(
            [new MergeRequestCommentInput("note-27", "thread-27", "Please cover the empty case.", "Reviewer", "2026-10-22T12:00:00Z", null)]);
        using var reviewWorker = new MergeRequestReviewWorker(store, poller, NullLogger<MergeRequestReviewWorker>.Instance);
        await reviewWorker.PollOnceAsync(CancellationToken.None);
        var agent = new FakeImplementationAgent();
        var build = new FakeBuildRunner([new TaskBuildResult("dotnet test", 0, "Passed")]);
        using var worker = new TaskImplementationWorker(store, agent, build, NullLogger<TaskImplementationWorker>.Instance);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var after = store.GetTaskNode(task.Id)!;
        Assert.Equal(TaskExecutionStatus.CommentsReviewed, after.ExecutionStatus);
        Assert.Equal("todo", after.Status);
        Assert.NotNull(Assert.Single(store.ListMergeRequestComments(task.Id)).AgentHandledAt);
        Assert.Equal("note-27", Assert.Single(agent.ReceivedComments).Id);
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    [Fact]
    public void MergeRequestCommentsAreDeduplicatedAndStalePollCannotWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-000035", "Review task", "Task status test.") with
        {
            SourceProjectId = 11,
            SourceTasks = [ImportedTask("BST-000006", 11, "Task with URL")],
        });
        var task = Assert.Single(store.ListTaskNodes(11));
        const string mrUrl = "https://gitlab.com/group/project/-/merge_requests/16";
        store.UpdateTask(task.Id, new TaskUpdateRequest(mrUrl));
        var comment = new MergeRequestCommentInput("456", "discussion-1", "Please add a regression test.", "Reviewer", "2026-10-22T12:00:00Z", mrUrl + "#note_456");

        Assert.True(store.SaveMergeRequestComments(task.Id, mrUrl, [comment]));
        Assert.True(store.SaveMergeRequestComments(task.Id, mrUrl, [comment]));

        var stored = Assert.Single(store.ListMergeRequestComments(task.Id));
        Assert.Equal("456", stored.Id);
        Assert.Equal("Please add a regression test.", stored.Body);
        Assert.Null(stored.ReviewedAt);
        var reviewed = store.MarkMergeRequestCommentReviewed(task.Id, stored.Id)!;
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.NotNull(Assert.Single(store.ListMergeRequestComments(task.Id)).ReviewedAt);
        store.UpdateTask(task.Id, new TaskUpdateRequest("https://gitlab.com/group/project/-/merge_requests/other"));
        Assert.False(store.SaveMergeRequestComments(task.Id, mrUrl, [comment]));
    }

    [Fact]
    public void MergeRequestReviewAgentParsesNormalizedCommentsAndRejectsInvalidIdentity()
    {
        var comments = MergeRequestReviewAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"state\":\"open\",\"sourceBranch\":\"feature/check-error\",\"targetBranch\":\"main\",\"approvalState\":\"unknown\",\"mergeability\":\"unknown\",\"approvedCount\":null,\"requiredApprovals\":null,\"comments\":[{\"id\":\"1\",\"discussionId\":\"d-1\",\"body\":\"Проверить обработку ошибки.\",\"author\":\"Reviewer\",\"createdAt\":\"2026-10-22T12:00:00Z\",\"url\":null}]}"}}
            """);

        Assert.Equal("feature/check-error", comments.SourceBranch);
        Assert.Equal("1", Assert.Single(comments.Comments).Id);
        Assert.Throws<InvalidOperationException>(() => MergeRequestReviewAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"state\":\"open\",\"sourceBranch\":\"feature/test\",\"targetBranch\":\"main\",\"approvalState\":\"unknown\",\"mergeability\":\"unknown\",\"approvedCount\":null,\"requiredApprovals\":null,\"comments\":[{\"id\":\"1\",\"discussionId\":\"\",\"body\":\"body\",\"author\":\"Reviewer\",\"createdAt\":\"now\",\"url\":null}]}"}}
            """));
    }

    [Fact]
    public async Task BackgroundReviewWorkerPollsOnlyEligibleTasksAndStoresNewComments()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-000036", "Review tasks", "Task status test.") with
        {
            SourceProjectId = 12,
            SourceTasks =
            [
                ImportedTask("BST-000007", 12, "Task in review"),
                ImportedTask("BST-000008", 12, "Task not in review"),
            ],
        });
        var tasks = store.ListTaskNodes(12);
        const string mrUrl = "https://gitlab.com/group/project/-/merge_requests/17";
        store.UpdateTask(tasks[0].Id, new TaskUpdateRequest(mrUrl));
        store.UpdateTask(tasks[1].Id, new TaskUpdateRequest(mrUrl));
        store.UpdateMergeRequestStatus(tasks[1].Id, MergeRequestStatus.Closed);
        var source = new FakeMergeRequestCommentSource(
            [new MergeRequestCommentInput("789", "discussion-2", "Please fix the edge case.", "Reviewer", "2026-10-22T12:00:00Z", null)]);
        using var worker = new MergeRequestReviewWorker(store, source, NullLogger<MergeRequestReviewWorker>.Instance);

        await worker.PollOnceAsync(CancellationToken.None);

        Assert.Equal([tasks[0].Id], source.PolledTaskIds);
        Assert.Equal("Please fix the edge case.", Assert.Single(store.ListMergeRequestComments(tasks[0].Id)).Body);
        Assert.Empty(store.ListMergeRequestComments(tasks[1].Id));
    }

    [Fact]
    public async Task ReplyIsNotPublishedUntilConfirmedAndThenCompletesThroughBackgroundWorker()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        store.Receive(Note("BSR-000037", "Review task", "Task status test.") with
        {
            SourceProjectId = 13,
            SourceTasks = [ImportedTask("BST-000009", 13, "Task for reply")],
        });
        var task = Assert.Single(store.ListTaskNodes(13));
        store.SetProjectRepository(13, directory);
        const string mrUrl = "https://gitlab.com/group/project/-/merge_requests/18";
        store.UpdateTask(task.Id, new TaskUpdateRequest(mrUrl));
        store.SaveMergeRequestComments(task.Id, mrUrl,
            [new MergeRequestCommentInput("900", "discussion-9", "Please clarify this case.", "Reviewer", "2026-10-22T12:00:00Z", null)]);
        Assert.False(store.ConfirmMergeRequestReply(task.Id, "900"));

        var draft = store.SaveMergeRequestReplyDraft(task.Id, "900", "I added the missing explanation.")!;
        Assert.Equal("draft", draft.ReplyStatus);
        Assert.Null(store.TryClaimMergeRequestReply());
        Assert.True(store.ConfirmMergeRequestReply(task.Id, "900"));
        var publisher = new FakeMergeRequestReplyPublisher("reply-901");
        using var worker = new MergeRequestReplyWorker(store, publisher, NullLogger<MergeRequestReplyWorker>.Instance);

        await worker.ProcessQueueOnceAsync(CancellationToken.None);

        Assert.Equal("discussion-9", Assert.Single(publisher.Published).DiscussionId);
        var sent = Assert.Single(store.ListMergeRequestComments(task.Id));
        Assert.Equal("sent", sent.ReplyStatus);
        Assert.Equal("reply-901", sent.ReplyExternalId);
    }

    [Fact]
    public void InterruptedReplyIsNotAutomaticallyRetried()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RunGit(directory, "init", "-b", "main");
        var databasePath = Path.Combine(directory, "tasks.db");
        var store = new CandidateStore(databasePath);
        store.Receive(Note("BSR-000038", "Review task", "Task status test.") with
        {
            SourceProjectId = 14,
            SourceTasks = [ImportedTask("BST-000010", 14, "Task for reply")],
        });
        var task = Assert.Single(store.ListTaskNodes(14));
        store.SetProjectRepository(14, directory);
        const string mrUrl = "https://gitlab.com/group/project/-/merge_requests/19";
        store.UpdateTask(task.Id, new TaskUpdateRequest(mrUrl));
        store.SaveMergeRequestComments(task.Id, mrUrl,
            [new MergeRequestCommentInput("901", "discussion-10", "Please clarify.", "Reviewer", "2026-10-22T12:00:00Z", null)]);
        store.SaveMergeRequestReplyDraft(task.Id, "901", "Approved response.");
        store.ConfirmMergeRequestReply(task.Id, "901");
        Assert.NotNull(store.TryClaimMergeRequestReply());

        var restarted = new CandidateStore(databasePath);

        var recovered = Assert.Single(restarted.ListMergeRequestComments(task.Id));
        Assert.Equal("failed", recovered.ReplyStatus);
        Assert.Contains("outcome is unknown", recovered.ReplyError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(restarted.TryClaimMergeRequestReply());
    }

    [Fact]
    public void ReplyAgentRequiresProviderConfirmationForTheExactDiscussion()
    {
        Assert.Equal("reply-902", MergeRequestReplyAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"posted\":true,\"replyId\":\"reply-902\",\"discussionId\":\"discussion-11\"}"}}
            """, "discussion-11"));
        Assert.Throws<InvalidOperationException>(() => MergeRequestReplyAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"posted\":true,\"replyId\":\"reply-902\",\"discussionId\":\"another-discussion\"}"}}
            """, "discussion-11"));
    }

    [Fact]
    public void FinalBoyScoutCreateTaskPlanIsMaterializedAutomatically()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        var store = new CandidateStore(Path.Combine(directory, "candidates.db"));
        var candidate = store.Receive(Note("BSR-000031", "New finding", "A new verified finding.")).Candidate;
        var context = store.BuildGraphContext(candidate.Id)! with
        {
            LocalizedCandidateTitle = "Новая проверенная проблема",
            LocalizedCandidateDescription = "Проверенное наблюдение требует отдельного исправления.",
        };
        var review = store.SaveAgentReview(context, "Создать самостоятельную задачу.", [],
            new AgentTaskPlan("create_task", null, "Исправить новую проблему", "Устранить подтверждённую проблему.", "Добавить проверку, подтверждающую исправление.", "В графе нет подходящей существующей задачи."));

        var task = Assert.Single(store.ListTaskNodes());
        Assert.Equal("Исправить новую проблему", task.Title);
        Assert.Equal("triaged", store.Get(candidate.Id)!.TriageStatus);
        Assert.Equal("accepted", review.TaskPlan!.Status);
        Assert.Contains(store.BuildGraphContext(candidate.Id)!.Edges, edge => edge.FromId == candidate.Id && edge.ToId == -task.Id);
        var graph = store.GetGraphSnapshot();
        Assert.Contains(graph.Nodes, node => node.Key == $"candidate:{candidate.Id}");
        Assert.Contains(graph.Nodes, node => node.Key == $"task:{task.Id}");
        Assert.Contains(graph.Edges, edge => edge.From == $"candidate:{candidate.Id}" && edge.To == $"task:{task.Id}");
    }

    [Fact]
    public void AgentResponseParserReadsJsonEventAndValidatesItsShape()
    {
        const string output = """
            {"type":"step_start"}
            {"type":"text","part":{"text":"{\"action\":\"finish\",\"summary\":\"Похожая проблема\",\"localizedCandidateTitle\":\"Проблема зависимостей\",\"localizedCandidateDescription\":\"Следует проверить обновления библиотек.\",\"query\":null,\"candidateId\":null,\"links\":[{\"candidateId\":7,\"relation\":\"duplicate\",\"rationale\":\"Совпадает причина.\"}],\"taskPlan\":{\"action\":\"create_task\",\"targetTaskId\":null,\"title\":\"Исправить уязвимость\",\"description\":\"Обновить уязвимую зависимость.\",\"acceptanceCriteria\":\"Проверки проходят после обновления.\",\"rationale\":\"В графе нет подходящей задачи.\"},\"clarifyingQuestions\":[]}"}}
            """;

        var parsed = TodoGraphAgent.ParseResponse(output);

        Assert.Equal("finish", parsed.Action);
        Assert.Equal("Похожая проблема", parsed.Summary);
        Assert.Equal(7L, Assert.Single(parsed.Links).CandidateId);
        Assert.Throws<InvalidOperationException>(() => TodoGraphAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"action\":\"finish\",\"summary\":\"No related nodes found\",\"localizedCandidateTitle\":\"Title\",\"localizedCandidateDescription\":\"Описание\",\"query\":null,\"candidateId\":null,\"links\":[],\"taskPlan\":null,\"clarifyingQuestions\":[]}"}}
            """));
        Assert.Equal("search", TodoGraphAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"action\":\"search\",\"summary\":\"Нужно проверить ещё узлы.\",\"localizedCandidateTitle\":null,\"localizedCandidateDescription\":null,\"query\":\"connection pool\",\"candidateId\":null,\"links\":[],\"taskPlan\":null,\"clarifyingQuestions\":[]}"}}
            """).Action);
        Assert.Throws<InvalidOperationException>(() => TodoGraphAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"action\":\"finish\",\"summary\":\"Описание готово.\",\"localizedCandidateTitle\":\"Fix the issue\",\"localizedCandidateDescription\":\"Описание проблемы.\",\"query\":null,\"candidateId\":null,\"links\":[],\"taskPlan\":{\"action\":\"create_task\",\"targetTaskId\":null,\"title\":\"Новая задача\",\"description\":\"Описание задачи.\",\"acceptanceCriteria\":\"Критерий проверки.\",\"rationale\":\"Обоснование решения.\"},\"clarifyingQuestions\":[]}"}}
            """));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => TodoGraphAgent.ParseResponse("{not-json}"));
    }

    [Fact]
    public void RawItemQuestionsPersistAcrossRoundsAndOnlyFinalCriteriaCreateLinkedTask()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var repository = Path.Combine(directory, "repo");
        Directory.CreateDirectory(repository);
        RunGit(repository, "init", "-b", "main");
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        var project = store.CreateProject(repository);
        Assert.True(project.SourceProjectId < 0);
        var projectCandidate = store.Receive(Note("BSR-RAW-PROJECT", "Project", "Existing project.") with { SourceProjectId = project.SourceProjectId }).Candidate;

        var raw = store.SubmitRawItem(project.SourceProjectId, "Нужно добавить экспорт отчётов.", "Test author")!;
        Assert.Equal("user", raw.Source);
        Assert.Equal(project.SourceProjectId, raw.SourceProjectId);
        Assert.Equal("Test author", raw.CreatedBy);
        Assert.Contains(store.List(projectId: project.SourceProjectId), item => item.Id == raw.Id && item.Source == "user");
        Assert.DoesNotContain(store.List(projectId: 77), item => item.Id == raw.Id);
        Assert.Equal("boyscout", projectCandidate.Source);

        var context = store.BuildGraphContext(raw.Id)!;
        var firstReview = store.SaveAgentReview(context, "Нужно уточнить требования.", [], null,
            ["Какой формат экспорта нужен?", "Какие данные должны войти в отчёт?"]);
        Assert.Null(firstReview.TaskPlan);
        Assert.Equal(2, Assert.Single(store.Get(raw.Id)!.ClarificationRounds!).Questions.Count);
        Assert.Empty(store.ListTaskNodes(project.SourceProjectId));
        Assert.Throws<ArgumentException>(() => store.SaveAgentReview(context, "Критерии неясны.", [],
            new AgentTaskPlan("create_task", null, "Создать экспорт", "Добавить экспорт отчётов.", "Критерии уточняются.", "План рано создавать."), []));
        Assert.Empty(store.ListTaskNodes(project.SourceProjectId));

        store.SetAgentJobState(raw.Id, "ready");
        Assert.NotNull(store.AnswerRawItem(raw.Id, "Нужен CSV, включая дату и итог."));
        Assert.Equal("queued", store.GetAgentJob(raw.Id)!.Status);
        var answeredContext = store.BuildGraphContext(raw.Id)!;
        Assert.Equal("Нужен CSV, включая дату и итог.", Assert.Single(answeredContext.ClarificationRounds!).Answer);
        var secondQuestions = store.SaveAgentReview(answeredContext, "Нужно уточнить частоту экспорта.", [], null,
            ["Экспорт запускается вручную или по расписанию?"]);
        Assert.Equal("Экспорт запускается вручную или по расписанию?", Assert.Single(secondQuestions.Questions!));
        store.SetAgentJobState(raw.Id, "ready");
        store.AnswerRawItem(raw.Id, "Пользователь запускает экспорт вручную.");
        var finalContext = store.BuildGraphContext(raw.Id)!;
        Assert.Equal(2, finalContext.ClarificationRounds!.Count);
        Assert.All(finalContext.ClarificationRounds, round => Assert.NotNull(round.Answer));
        var reopened = new CandidateStore(Path.Combine(directory, "tasks.db"));
        Assert.Equal("Пользователь запускает экспорт вручную.", reopened.Get(raw.Id)!.ClarificationRounds![1].Answer);
        var final = store.SaveAgentReview(finalContext with
        {
            LocalizedCandidateTitle = "Экспорт отчётов",
            LocalizedCandidateDescription = "Добавить экспорт отчётов в CSV с датой и итогом.",
        }, "Критерии и формат установлены.", [],
            new AgentTaskPlan("create_task", null, "Добавить экспорт отчётов", "Экспортировать отчёты в CSV с датой и итогом.", "CSV содержит дату и итог; файл открывается стандартным табличным редактором.", "Требования подтверждены."), []);

        var created = Assert.Single(store.ListTaskNodes(project.SourceProjectId));
        Assert.Contains(store.GetGraphSnapshot().Edges, edge => edge.From == $"candidate:{raw.Id}" && edge.To == $"task:{created.Id}");
        Assert.Equal("Test author", created.CreatedBy);
        Assert.Equal("accepted", final.TaskPlan!.Status);
        Assert.Contains(store.Get(raw.Id)!.Links, link => link.Relation == "source");
        store.SaveAgentReview(store.BuildGraphContext(raw.Id)!, "Повторный финальный анализ.", [],
            new AgentTaskPlan("create_task", null, "Добавить экспорт отчётов", "Экспортировать отчёты в CSV с датой и итогом.", "CSV содержит дату и итог; файл открывается стандартным табличным редактором.", "Требования подтверждены."), []);
        Assert.Single(store.ListTaskNodes(project.SourceProjectId));
    }

    [Fact]
    public void ExistingCandidateRowsMigrateToBoyScoutSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "legacy-candidate.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE candidates(id INTEGER PRIMARY KEY, source_report_id TEXT NOT NULL UNIQUE, project TEXT NOT NULL, title TEXT NOT NULL, observation TEXT NOT NULL, location TEXT, impact TEXT, suggested_action TEXT, discovered_during TEXT, source_status TEXT NOT NULL, source_created_at TEXT NOT NULL, received_at TEXT NOT NULL, source_url TEXT NOT NULL, triage_status TEXT NOT NULL DEFAULT 'pending'); INSERT INTO candidates(id,source_report_id,project,title,observation,source_status,source_created_at,received_at,source_url) VALUES(1,'BSR-OLD','legacy','Old item','Old observation','accepted','2026-01-01','2026-01-01','http://127.0.0.1');";
            command.ExecuteNonQuery();
        }

        Assert.Equal("boyscout", new CandidateStore(path).Get(1)!.Source);
    }

    [Fact]
    public void BoyScoutQueueCandidateMustBeClarifiedBeforeExactlyOneTaskIsCreated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "todo-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new CandidateStore(Path.Combine(directory, "tasks.db"));
        var received = store.Receive(Note("BSR-RAW-CLARIFY", "Не работает экспорт", "При выгрузке теряется итоговая сумма.") with
        {
            SourceProjectId = 81,
            SuggestedAction = "Исправить выгрузку",
        });
        var candidate = received.Candidate;
        var context = store.BuildGraphContext(candidate.Id)!;

        Assert.Equal("boyscout", context.Source);
        Assert.Null(context.RawText);
        Assert.Equal(candidate.Observation, store.Get(candidate.Id)!.Observation);
        var questions = store.SaveAgentReview(context, "Нужно уточнить ожидаемый формат.", [], null,
            ["В каком формате должна сохраняться выгрузка?", "Какие поля и суммы должны присутствовать?"]);

        Assert.Null(questions.TaskPlan);
        Assert.Empty(store.ListTaskNodes(81));
        Assert.Equal(2, Assert.Single(store.Get(candidate.Id)!.ClarificationRounds!).Questions.Count);

        store.SetAgentJobState(candidate.Id, "ready");
        Assert.NotNull(store.AnswerRawItem(candidate.Id, "CSV: дата, позиции и итоговая сумма; сумма сверяется с исходными данными."));
        var answered = store.BuildGraphContext(candidate.Id)!;
        Assert.Equal("boyscout", answered.Source);
        Assert.Equal("CSV: дата, позиции и итоговая сумма; сумма сверяется с исходными данными.",
            Assert.Single(answered.ClarificationRounds!).Answer);
        var finalReview = store.SaveAgentReview(answered with
        {
            LocalizedCandidateTitle = "Сохранить итоговую сумму при экспорте",
            LocalizedCandidateDescription = "Экспортировать позиции с датой и итоговой суммой, сверенной с исходными данными.",
        }, "Формат и проверяемый итог уточнены.", [],
            new AgentTaskPlan("create_task", null, "Исправить итоговую сумму в экспорте",
                "Сохранять позиции, дату и итоговую сумму, сверенную с исходными данными.",
                "CSV содержит дату, позиции и итоговую сумму; итог совпадает с суммой исходных позиций.",
                "Требования подтверждены ответом автора."), []);

        var task = Assert.Single(store.ListTaskNodes(81));
        Assert.Equal("Исправить итоговую сумму в экспорте", task.Title);
        Assert.Contains("CSV содержит дату", task.AcceptanceCriteria, StringComparison.Ordinal);
        Assert.Equal("boyscout", store.Get(candidate.Id)!.Source);
        Assert.Equal("Не работает экспорт", store.Get(candidate.Id)!.Title);
        Assert.Contains(store.GetGraphSnapshot().Edges, edge => edge.From == $"candidate:{candidate.Id}" && edge.To == $"task:{task.Id}");
        Assert.Equal("accepted", finalReview.TaskPlan!.Status);
        store.SaveAgentReview(store.BuildGraphContext(candidate.Id)!, "Повторный анализ.", [],
            new AgentTaskPlan("create_task", null, "Исправить итоговую сумму в экспорте",
                "Сохранять позиции, дату и итоговую сумму, сверенную с исходными данными.",
                "CSV содержит дату, позиции и итоговую сумму; итог совпадает с суммой исходных позиций.",
                "Требования подтверждены ответом автора."), []);
        Assert.Single(store.ListTaskNodes(81));
    }

    [Fact]
    public void AgentParserAllowsMultipleClarifyingQuestionsButNoPlan()
    {
        var parsed = TodoGraphAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"action\":\"finish\",\"summary\":\"Нужно уточнить детали.\",\"localizedCandidateTitle\":null,\"localizedCandidateDescription\":null,\"query\":null,\"candidateId\":null,\"links\":[],\"taskPlan\":null,\"clarifyingQuestions\":[\"Какой формат нужен?\",\"Какие поля включить?\"]}"}}
            """);
        Assert.Equal(2, parsed.Questions.Count);
        Assert.Null(parsed.TaskPlan);
        Assert.Throws<InvalidOperationException>(() => TodoGraphAgent.ParseResponse("""
            {"type":"text","part":{"text":"{\"action\":\"finish\",\"summary\":\"Требуются уточнения.\",\"localizedCandidateTitle\":null,\"localizedCandidateDescription\":null,\"query\":null,\"candidateId\":null,\"links\":[],\"taskPlan\":{\"action\":\"create_task\",\"targetTaskId\":null,\"title\":\"Создать отчёт\",\"description\":\"Добавить отчёт.\",\"acceptanceCriteria\":\"Критерии установлены.\",\"rationale\":\"Нужен план.\"},\"clarifyingQuestions\":[\"Какой формат нужен?\"]}"}}
            """));
    }

    private static CandidateImport Note(string id, string title, string observation) => new(
        id, "C:\\project", title, observation, null, "Impacts connection handling", "Fix cleanup", null,
        "accepted", "2026-10-19T00:00:00Z", "http://127.0.0.1:5187");

    private static ImportedTask ImportedTask(string id, long projectId, string title) => new(
        id, projectId, "sample-project", title, "Task description", "Pass tests", null, "todo",
        "2026-10-01T00:00:00Z", "2026-10-02T00:00:00Z", []);

    private sealed class FakeMergeRequestCommentSource(
        IReadOnlyList<MergeRequestCommentInput> comments, MergeRequestStatus state = MergeRequestStatus.Open, string sourceBranch = "feature/test", string targetBranch = "main") : IMergeRequestCommentSource
    {
        public bool IsConfiguredFor(TaskNode task) => true;
        public List<long> PolledTaskIds { get; } = [];

        public Task<MergeRequestPollResult> FetchCommentsAsync(TaskNode task, CancellationToken cancellationToken)
        {
            PolledTaskIds.Add(task.Id);
            return Task.FromResult(new MergeRequestPollResult(state, sourceBranch, comments, MergeRequestTargetBranch: targetBranch));
        }
    }

    private sealed class FakeMergeRequestReplyPublisher(string replyId) : IMergeRequestReplyPublisher
    {
        public bool IsConfigured => true;
        public List<MergeRequestReply> Published { get; } = [];

        public Task<string> SendReplyAsync(MergeRequestReply reply, CancellationToken cancellationToken)
        {
            Published.Add(reply);
            return Task.FromResult(replyId);
        }
    }

    private sealed class FakeWebHostEnvironment(string contentRoot) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TodoGraphTests";
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeTaskPreflightSource(TaskPreflightResult first, TaskPreflightResult? afterClarification = null) : ITaskPreflightSource
    {
        public int Attempts { get; private set; }

        public Task<TaskPreflightResult> ExplainAsync(TaskNode task, TaskPreflight? previous, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(previous?.Answers is not null && afterClarification is not null ? afterClarification : first);
        }
    }

    private sealed class FakeImplementationAgent(string action = "edited") : ITaskImplementationAgent
    {
        public int Attempts { get; private set; }
        public List<string?> BuildFailures { get; } = [];
        public List<MergeRequestComment> ReceivedComments { get; } = [];

        public Task<TaskImplementationResult> ImplementAsync(TaskNode task, TaskPreflight? preflight, IReadOnlyList<MergeRequestComment> reviewComments, string? buildFailure, CancellationToken token)
        {
            Attempts++;
            BuildFailures.Add(buildFailure);
            ReceivedComments.AddRange(reviewComments);
            return Task.FromResult(new TaskImplementationResult(action, "Комментарий разобран.", action == "question" ? "Уточните требование." : string.Empty));
        }
    }

    private sealed class FakeBuildRunner(IReadOnlyList<TaskBuildResult> attempts, Action<TaskNode>? onBuild = null) : IProjectBuildRunner
    {
        public int Attempts { get; private set; }
        public Task<bool> HasChangesAsync(TaskNode task, CancellationToken token) => Task.FromResult(true);
        public Task<TaskBuildResult> BuildAsync(TaskNode task, TaskPreflight? preflight, CancellationToken token)
        {
            onBuild?.Invoke(task);
            return Task.FromResult(attempts[Attempts++]);
        }
    }

    private sealed class FakeMergeRequestCreator(string url) : IMergeRequestCreator
    {
        public bool IsConfigured => true;
        public int Calls { get; private set; }

        public Task<string> FindOrCreateAsync(TaskNode task, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(url);
        }
    }
}
