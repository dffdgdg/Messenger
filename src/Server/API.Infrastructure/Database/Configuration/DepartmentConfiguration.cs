using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(e => e.Id).HasName("departments_pkey");
        builder.ToTable("departments");

        builder.HasIndex(e => e.ChatId, "Departments_ChatId_key").IsUnique();
        builder.HasIndex(e => e.HeadId, "idx_departments_head_id").IsUnique();

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.ChatId).HasColumnName("chat_id");
        builder.Property(e => e.HeadId).HasColumnName("head_id");
        builder.Property(e => e.Name).HasMaxLength(100).HasColumnName("name");
        builder.Property(e => e.ParentDepartmentId).HasColumnName("parent_department_id");

        builder.HasOne(d => d.Chat).WithOne(p => p.Department).HasForeignKey<Department>(d => d.ChatId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Departments_ChatId_fkey");
        builder.HasOne(d => d.Head).WithMany(p => p.HeadedDepartments).HasForeignKey(d => d.HeadId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Departments_Head_fkey");
        builder.HasOne(d => d.ParentDepartment).WithMany(p => p.InverseParentDepartment).HasForeignKey(d => d.ParentDepartmentId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Departments_Parent_fkey");
    }
}