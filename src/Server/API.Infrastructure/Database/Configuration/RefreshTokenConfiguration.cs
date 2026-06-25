using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(e => e.Id).HasName("refresh_tokens_pkey");
        builder.ToTable("refresh_tokens");

        builder.HasIndex(e => e.TokenHash, "idx_refresh_tokens_token_hash");
        builder.HasIndex(e => e.UserId, "idx_refresh_tokens_user_id");
        builder.HasIndex(e => e.FamilyId, "idx_refresh_tokens_family_id");
        builder.HasIndex(e => e.ExpiresAt, "idx_refresh_tokens_expires_at");
        builder.HasIndex(e => e.ReplacedByTokenId, "idx_refresh_tokens_replaced_by");

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.TokenHash).HasMaxLength(128).HasColumnName("token_hash");
        builder.Property(e => e.JwtId).HasMaxLength(64).HasColumnName("jwt_id");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        builder.Property(e => e.ExpiresAt).HasColumnType("timestamp without time zone").HasColumnName("expires_at");
        builder.Property(e => e.UsedAt).HasColumnType("timestamp without time zone").HasColumnName("used_at");
        builder.Property(e => e.RevokedAt).HasColumnType("timestamp without time zone").HasColumnName("revoked_at");
        builder.Property(e => e.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
        builder.Property(e => e.FamilyId).HasMaxLength(64).HasColumnName("family_id");

        builder.HasOne(d => d.User).WithMany(p => p.RefreshTokens).HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("RefreshTokens_UserId_fkey");
        builder.HasOne(d => d.ReplacedByToken).WithMany().HasForeignKey(d => d.ReplacedByTokenId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("RefreshTokens_ReplacedBy_fkey");
    }
}