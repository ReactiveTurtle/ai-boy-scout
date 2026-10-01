using BoyScout.Core.Tasks;

namespace BoyScout.Core.Reports;

public sealed record RestoredNote(NoteWorkflowInfo Workflow, TaskProposal Proposal);
