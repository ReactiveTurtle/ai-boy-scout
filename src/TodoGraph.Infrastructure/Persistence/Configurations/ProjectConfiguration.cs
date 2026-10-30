using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoGraph.Domain.Projects;

namespace TodoGraph.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();
        builder.Property(project => project.Name).IsRequired();
        builder.Property(project => project.FullPath).IsRequired();
    }
}
