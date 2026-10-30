using BoyScout.Domain.Tasks;

namespace BoyScout.Application.Tasks;

public sealed record TaskPage(IReadOnlyList<BoyScoutTask> Items, long Total, long? NextBefore);
