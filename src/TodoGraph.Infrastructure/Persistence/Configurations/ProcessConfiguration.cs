using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoGraph.Domain.Processes;

namespace TodoGraph.Infrastructure.Persistence.Configurations;

internal sealed class ProcessConfiguration : IEntityTypeConfiguration<Process>
{
    public void Configure(EntityTypeBuilder<Process> builder)
    {
        builder.ToTable("processes");
        builder.HasKey(process => process.Id);
        builder.Property(process => process.Id).ValueGeneratedNever();
        builder.Property(process => process.Name).IsRequired();

        builder.OwnsMany(process => process.Blocks, blocks =>
        {
            blocks.ToTable("process_blocks");
            blocks.WithOwner().HasForeignKey("ProcessId");
            blocks.HasKey(block => block.Id);
            blocks.Property(block => block.Id).ValueGeneratedNever();
            blocks.Property(block => block.Position).IsRequired();
            blocks.HasIndex("ProcessId", nameof(ProcessBlock.Position));

            blocks.OwnsMany(block => block.Stages, stages =>
            {
                stages.ToTable("process_stages");
                stages.WithOwner().HasForeignKey("ProcessBlockId");
                stages.HasKey(stage => stage.Id);
                stages.Property(stage => stage.Id).ValueGeneratedNever();
                stages.Property(stage => stage.Position).IsRequired();
                stages.Property(stage => stage.Instructions).IsRequired();
                stages.HasIndex("ProcessBlockId", nameof(ProcessStage.Position));
                stages.OwnsOne(stage => stage.BackwardLink, backwardLink =>
                {
                    backwardLink.Property(link => link.TargetStageId).HasColumnName("BackwardTargetStageId");
                    backwardLink.Property(link => link.MaxRepeats).HasColumnName("BackwardMaxRepeats");
                });
                stages.Navigation(stage => stage.BackwardLink).IsRequired(false);
            });

            blocks.Navigation(block => block.Stages)
                .HasField("stages")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Navigation(process => process.Blocks)
            .HasField("blocks")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
