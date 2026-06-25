using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class PollConfiguration : IEntityTypeConfiguration<Poll>
{
    public void Configure(EntityTypeBuilder<Poll> builder)
    {
        builder.HasKey(e => e.Id).HasName("polls_pkey");
        builder.ToTable("polls");

        builder.HasIndex(e => e.MessageId, "idx_polls_message_id").IsUnique();

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.AllowsMultipleAnswers).HasDefaultValue(false).HasColumnName("allows_multiple_answers");
        builder.Property(e => e.ClosesAt).HasColumnType("timestamp without time zone").HasColumnName("closes_at");
        builder.Property(e => e.IsAnonymous).HasDefaultValue(true).HasColumnName("is_anonymous");
        builder.Property(e => e.MessageId).HasColumnName("message_id");
    }
}

internal sealed class PollOptionConfiguration : IEntityTypeConfiguration<PollOption>
{
    public void Configure(EntityTypeBuilder<PollOption> builder)
    {
        builder.HasKey(e => e.Id).HasName("poll_options_pkey");
        builder.ToTable("poll_options");

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.OptionText).HasMaxLength(50).HasColumnName("option_text");
        builder.Property(e => e.PollId).HasColumnName("poll_id");
        builder.Property(e => e.Position).HasColumnName("position");

        builder.HasOne(d => d.Poll).WithMany(p => p.PollOptions).HasForeignKey(d => d.PollId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("PollOptions_PollId_fkey");
    }
}

internal sealed class PollVoteConfiguration : IEntityTypeConfiguration<PollVote>
{
    public void Configure(EntityTypeBuilder<PollVote> builder)
    {
        builder.HasKey(e => e.Id).HasName("poll_votes_pkey");
        builder.ToTable("poll_votes");

        builder.HasIndex(e => new { e.PollId, e.UserId, e.OptionId }, "UQ_Poll_User_Option_Vote").IsUnique();
        builder.HasIndex(e => e.UserId, "idx_poll_votes_user_id");

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.OptionId).HasColumnName("option_id");
        builder.Property(e => e.PollId).HasColumnName("poll_id");
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.VotedAt).HasDefaultValueSql("now()").HasColumnType("timestamp without time zone").HasColumnName("voted_at");

        builder.HasOne(d => d.Option).WithMany(p => p.PollVotes).HasForeignKey(d => d.OptionId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("PollVotes_OptionId_fkey");
        builder.HasOne(d => d.Poll).WithMany(p => p.PollVotes).HasForeignKey(d => d.PollId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("PollVotes_PollId_fkey");
        builder.HasOne(d => d.User).WithMany(p => p.PollVotes).HasForeignKey(d => d.UserId).HasConstraintName("PollVotes_UserId_fkey");
    }
}