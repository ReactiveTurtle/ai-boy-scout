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
        builder.OwnsMany(project => project.Tasks, tasks =>
        {
            tasks.ToTable("project_tasks");
            tasks.WithOwner().HasForeignKey("ProjectId");
            tasks.HasKey(task => task.Id);
            tasks.Property(task => task.Id).ValueGeneratedNever();
            tasks.Property(task => task.Title);
            tasks.Property(task => task.Description).IsRequired();
            tasks.Ignore(task => task.CurrentProcessRun);
        });
        builder.Navigation(project => project.Tasks).HasField("tasks").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
