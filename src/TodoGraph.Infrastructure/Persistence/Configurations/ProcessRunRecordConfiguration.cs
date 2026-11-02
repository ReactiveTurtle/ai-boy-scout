using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoGraph.Infrastructure.Persistence.Entities;

namespace TodoGraph.Infrastructure.Persistence.Configurations;

internal sealed class ProcessRunRecordConfiguration : IEntityTypeConfiguration<ProcessRunRecord>
{
    public void Configure(EntityTypeBuilder<ProcessRunRecord> builder)
    {
        builder.ToTable("process_runs");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.ProjectTaskId).IsRequired();
        builder.HasIndex(run => run.ProjectTaskId).IsUnique();
        builder.Property(run => run.ProcessId).IsRequired();
        builder.Property(run => run.ProcessName).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().IsRequired();
    }
}

internal sealed class ProcessRunBlockRecordConfiguration : IEntityTypeConfiguration<ProcessRunBlockRecord>
{
    public void Configure(EntityTypeBuilder<ProcessRunBlockRecord> builder)
    {
        builder.ToTable("process_run_blocks");
        builder.HasKey(block => new { block.ProcessRunId, block.BlockId });
        builder.Property(block => block.Position).IsRequired();
        builder.HasIndex(block => new { block.ProcessRunId, block.Position });
        builder.HasOne<ProcessRunRecord>()
            .WithMany()
            .HasForeignKey(block => block.ProcessRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProcessRunStageRecordConfiguration : IEntityTypeConfiguration<ProcessRunStageRecord>
{
    public void Configure(EntityTypeBuilder<ProcessRunStageRecord> builder)
    {
        builder.ToTable("process_run_stages");
        builder.HasKey(stage => new { stage.ProcessRunId, stage.BlockId, stage.StageId });
        builder.Property(stage => stage.StageHash).HasMaxLength(64).IsRequired();
        builder.Property(stage => stage.Position).IsRequired();
        builder.HasIndex(stage => new { stage.ProcessRunId, stage.BlockId, stage.Position });
        builder.HasOne<ProcessRunBlockRecord>()
            .WithMany()
            .HasForeignKey(stage => new { stage.ProcessRunId, stage.BlockId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProcessStageSnapshotRecord>()
            .WithMany()
            .HasForeignKey(stage => stage.StageHash)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProcessStageSnapshotRecordConfiguration : IEntityTypeConfiguration<ProcessStageSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<ProcessStageSnapshotRecord> builder)
    {
        builder.ToTable("process_stage_snapshots");
        builder.HasKey(snapshot => snapshot.Hash);
        builder.Property(snapshot => snapshot.Hash).HasMaxLength(64).ValueGeneratedNever();
        builder.Property(snapshot => snapshot.Instructions).IsRequired();
    }
}

internal sealed class ProcessRunLinkCounterRecordConfiguration : IEntityTypeConfiguration<ProcessRunLinkCounterRecord>
{
    public void Configure(EntityTypeBuilder<ProcessRunLinkCounterRecord> builder)
    {
        builder.ToTable("process_run_link_counters");
        builder.HasKey(counter => new { counter.ProcessRunId, counter.BlockId, counter.SourceStageId });
        builder.Property(counter => counter.RepeatCount).IsRequired();
        builder.HasOne<ProcessRunStageRecord>()
            .WithMany()
            .HasForeignKey(counter => new { counter.ProcessRunId, counter.BlockId, StageId = counter.SourceStageId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
