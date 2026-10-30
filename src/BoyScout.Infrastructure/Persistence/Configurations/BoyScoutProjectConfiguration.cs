using BoyScout.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoyScout.Infrastructure.Persistence.Configurations;

internal sealed class BoyScoutProjectConfiguration : IEntityTypeConfiguration<BoyScoutProject>
{
    public void Configure(EntityTypeBuilder<BoyScoutProject> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(project => project.Name).HasColumnName("name").IsRequired();
        builder.Property(project => project.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(project => project.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasMany(project => project.Paths).WithOne().HasForeignKey(path => path.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(project => project.Paths).HasField("paths").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BoyScoutProjectPathConfiguration : IEntityTypeConfiguration<BoyScoutProjectPath>
{
    public void Configure(EntityTypeBuilder<BoyScoutProjectPath> builder)
    {
        builder.ToTable("project_paths");
        builder.HasKey(path => path.Id);
        builder.Property(path => path.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(path => path.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(path => path.Path).HasColumnName("path").UseCollation("NOCASE").IsRequired();
        builder.Property(path => path.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(path => path.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(path => path.Path).IsUnique();
        builder.HasIndex(path => new { path.ProjectId, path.Id }).HasDatabaseName("project_paths_project");
        builder.HasIndex(path => path.ProjectId).IsUnique().HasFilter("is_primary = 1").HasDatabaseName("project_paths_one_primary");
    }
}

internal sealed class ProjectReportProjectionConfiguration : IEntityTypeConfiguration<ProjectReportProjection>
{
    public void Configure(EntityTypeBuilder<ProjectReportProjection> builder)
    {
        builder.ToTable("reports");
        builder.HasKey(report => report.Sequence);
        builder.Property(report => report.Sequence).HasColumnName("sequence").ValueGeneratedOnAdd();
        builder.Property(report => report.ProjectId).HasColumnName("project_id");
        builder.Property(report => report.Status).HasColumnName("status").IsRequired();
        builder.Property(report => report.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

internal sealed class ProjectAiAccessConfiguration : IEntityTypeConfiguration<ProjectAiAccess>
{
    public void Configure(EntityTypeBuilder<ProjectAiAccess> builder)
    {
        builder.ToTable("project_ai_access");
        builder.HasKey(access => access.ProjectId);
        builder.Property(access => access.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
        builder.Property(access => access.GrantedAt).HasColumnName("granted_at").IsRequired();
    }
}
