using BoyScout.Core.Tasks;

namespace BoyScout.Core.Reports;

public sealed record ReprocessedNote(
    NoteWorkflowInfo Workflow,
    TaskProposal Proposal,
    string? DeletedTaskId);
