using BoyScout.Domain.Tasks;

namespace BoyScout.Application.Reports;

public sealed record RestoredNote(NoteWorkflowInfo Workflow, TaskProposal Proposal);
