using BoyScout.Domain.Tasks;

namespace BoyScout.Application.Reports;

public sealed record ReprocessedNote(
    NoteWorkflowInfo Workflow,
    TaskProposal Proposal,
    string? DeletedTaskId);
