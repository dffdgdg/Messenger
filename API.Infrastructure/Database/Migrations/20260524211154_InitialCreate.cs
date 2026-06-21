using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Shared.Enum;

#nullable disable

namespace API.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:chat_role", "member,admin,owner")
            .Annotation("Npgsql:Enum:chat_type", "chat,department,contact,department_heads")
            .Annotation("Npgsql:Enum:system_event_type", "chat_created,member_added,member_removed,member_left,role_changed,call_started,call_ended,message_pinned,message_unpinned,chat_avatar_updated")
            .Annotation("Npgsql:Enum:theme", "dark,light,system")
            .Annotation("Npgsql:Enum:theme.theme", "light,dark,system")
            .Annotation("Npgsql:Enum:user_status_type", "online,away,busy,do_not_disturb");

        migrationBuilder.CreateTable(
            name: "system_settings",
            columns: table => new
            {
                key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                value = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("system_settings_pkey", x => x.key);
            });

        migrationBuilder.CreateTable(
            name: "chat_members",
            columns: table => new
            {
                chat_id = table.Column<int>(type: "integer", nullable: false),
                user_id = table.Column<int>(type: "integer", nullable: false),
                joined_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                notifications_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                last_read_message_id = table.Column<int>(type: "integer", nullable: true),
                last_read_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                role = table.Column<ChatRole>(type: "chat_role", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("chat_members_pkey", x => new { x.chat_id, x.user_id });
            });

        migrationBuilder.CreateTable(
            name: "chats",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                created_by_id = table.Column<int>(type: "integer", nullable: true),
                last_message_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                avatar = table.Column<string>(type: "text", nullable: true),
                show_history_for_new_members = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                type = table.Column<ChatType>(type: "chat_type", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("chats_pkey", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "departments",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                parent_department_id = table.Column<int>(type: "integer", nullable: true),
                chat_id = table.Column<int>(type: "integer", nullable: true),
                head_id = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("departments_pkey", x => x.id);
                table.ForeignKey(
                    name: "Departments_ChatId_fkey",
                    column: x => x.chat_id,
                    principalTable: "chats",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "Departments_Parent_fkey",
                    column: x => x.parent_department_id,
                    principalTable: "departments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                password_hash = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "now()"),
                last_online = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                department_id = table.Column<int>(type: "integer", nullable: true),
                avatar = table.Column<string>(type: "text", nullable: true),
                midname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                surname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                is_banned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                status_type = table.Column<UserStatusType>(type: "user_status_type", nullable: false, defaultValue: UserStatusType.Online),
                status_expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("users_pkey", x => x.id);
                table.ForeignKey(
                    name: "Users_DepartmentId_fkey",
                    column: x => x.department_id,
                    principalTable: "departments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "messages",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                chat_id = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                is_deleted = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                pinned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                pinned_by_user_id = table.Column<int>(type: "integer", nullable: true),
                message_type = table.Column<bool>(type: "boolean", nullable: false),
                sender_id = table.Column<int>(type: "integer", nullable: true),
                target_user_id = table.Column<int>(type: "integer", nullable: true),
                system_event_type = table.Column<SystemEventType>(type: "system_event_type", nullable: true),
                content = table.Column<string>(type: "text", nullable: true),
                edited_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                reply_to_message_id = table.Column<int>(type: "integer", nullable: true),
                forwarded_from_message_id = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("messages_pkey", x => x.id);
                table.ForeignKey(
                    name: "Messages_ChatId_fkey",
                    column: x => x.chat_id,
                    principalTable: "chats",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "Messages_ForwardedFromMessageId_fkey",
                    column: x => x.forwarded_from_message_id,
                    principalTable: "messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "Messages_PinnedByUserId_fkey",
                    column: x => x.pinned_by_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "Messages_ReplyToMessageId_fkey",
                    column: x => x.reply_to_message_id,
                    principalTable: "messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "Messages_SenderId_fkey",
                    column: x => x.sender_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "Messages_TargetUserId_fkey",
                    column: x => x.target_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<int>(type: "integer", nullable: false),
                token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                jwt_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                used_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                revoked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                replaced_by_token_id = table.Column<int>(type: "integer", nullable: true),
                family_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("refresh_tokens_pkey", x => x.id);
                table.ForeignKey(
                    name: "RefreshTokens_ReplacedBy_fkey",
                    column: x => x.replaced_by_token_id,
                    principalTable: "refresh_tokens",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "RefreshTokens_UserId_fkey",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_settings",
            columns: table => new
            {
                user_id = table.Column<int>(type: "integer", nullable: false),
                theme = table.Column<Theme>(type: "theme", nullable: true),
                notifications_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("user_settings_pkey", x => x.user_id);
                table.ForeignKey(
                    name: "UserSettings_UserId_fkey",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "message_files",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                message_id = table.Column<int>(type: "integer", nullable: false),
                path = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("message_files_pkey", x => x.id);
                table.ForeignKey(
                    name: "MessageFiles_MessageId_fkey",
                    column: x => x.message_id,
                    principalTable: "messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "polls",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                message_id = table.Column<int>(type: "integer", nullable: false),
                is_anonymous = table.Column<bool>(type: "boolean", nullable: true, defaultValue: true),
                allows_multiple_answers = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                closes_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("polls_pkey", x => x.id);
                table.ForeignKey(
                    name: "Polls_MessageId_fkey",
                    column: x => x.message_id,
                    principalTable: "messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "voice_messages",
            columns: table => new
            {
                message_id = table.Column<int>(type: "integer", nullable: false),
                duration_seconds = table.Column<double>(type: "double precision", nullable: false),
                waveform = table.Column<string>(type: "text", nullable: true),
                file_path = table.Column<string>(type: "text", nullable: false),
                file_size = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("voice_messages_pkey", x => x.message_id);
                table.ForeignKey(
                    name: "VoiceMessages_MessageId_fkey",
                    column: x => x.message_id,
                    principalTable: "messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "poll_options",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                poll_id = table.Column<int>(type: "integer", nullable: false),
                option_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                position = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("poll_options_pkey", x => x.id);
                table.ForeignKey(
                    name: "PollOptions_PollId_fkey",
                    column: x => x.poll_id,
                    principalTable: "polls",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "poll_votes",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                poll_id = table.Column<int>(type: "integer", nullable: false),
                option_id = table.Column<int>(type: "integer", nullable: false),
                user_id = table.Column<int>(type: "integer", nullable: false),
                voted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("poll_votes_pkey", x => x.id);
                table.ForeignKey(
                    name: "PollVotes_OptionId_fkey",
                    column: x => x.option_id,
                    principalTable: "poll_options",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "PollVotes_PollId_fkey",
                    column: x => x.poll_id,
                    principalTable: "polls",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "PollVotes_UserId_fkey",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "idx_chat_members_last_read_message_id",
            table: "chat_members",
            column: "last_read_message_id");

        migrationBuilder.CreateIndex(
            name: "idx_chat_members_user_id",
            table: "chat_members",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_chats_created_by_id",
            table: "chats",
            column: "created_by_id");

        migrationBuilder.CreateIndex(
            name: "Departments_ChatId_key",
            table: "departments",
            column: "chat_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "idx_departments_head_id",
            table: "departments",
            column: "head_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_departments_parent_department_id",
            table: "departments",
            column: "parent_department_id");

        migrationBuilder.CreateIndex(
            name: "IX_message_files_message_id",
            table: "message_files",
            column: "message_id");

        migrationBuilder.CreateIndex(
            name: "idx_messages_chatid_createdat",
            table: "messages",
            columns: new[] { "chat_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "idx_messages_chatid_pinnedat",
            table: "messages",
            columns: new[] { "chat_id", "pinned_at" },
            filter: "pinned_at IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "idx_messages_forwarded_from_message_id",
            table: "messages",
            column: "forwarded_from_message_id");

        migrationBuilder.CreateIndex(
            name: "idx_messages_reply_to_message_id",
            table: "messages",
            column: "reply_to_message_id");

        migrationBuilder.CreateIndex(
            name: "idx_messages_target_user_id",
            table: "messages",
            column: "target_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_messages_pinned_by_user_id",
            table: "messages",
            column: "pinned_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_messages_sender_id",
            table: "messages",
            column: "sender_id");

        migrationBuilder.CreateIndex(
            name: "IX_poll_options_poll_id",
            table: "poll_options",
            column: "poll_id");

        migrationBuilder.CreateIndex(
            name: "idx_poll_votes_user_id",
            table: "poll_votes",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_poll_votes_option_id",
            table: "poll_votes",
            column: "option_id");

        migrationBuilder.CreateIndex(
            name: "UQ_Poll_User_Option_Vote",
            table: "poll_votes",
            columns: new[] { "poll_id", "user_id", "option_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "idx_polls_message_id",
            table: "polls",
            column: "message_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "idx_refresh_tokens_expires_at",
            table: "refresh_tokens",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "idx_refresh_tokens_family_id",
            table: "refresh_tokens",
            column: "family_id");

        migrationBuilder.CreateIndex(
            name: "idx_refresh_tokens_replaced_by",
            table: "refresh_tokens",
            column: "replaced_by_token_id");

        migrationBuilder.CreateIndex(
            name: "idx_refresh_tokens_token_hash",
            table: "refresh_tokens",
            column: "token_hash");

        migrationBuilder.CreateIndex(
            name: "idx_refresh_tokens_user_id",
            table: "refresh_tokens",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "idx_users_department_id",
            table: "users",
            column: "department_id");

        migrationBuilder.CreateIndex(
            name: "users_username_key",
            table: "users",
            column: "username",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "ChatMembers_ChatId_fkey",
            table: "chat_members",
            column: "chat_id",
            principalTable: "chats",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "ChatMembers_LastReadMessageId_fkey",
            table: "chat_members",
            column: "last_read_message_id",
            principalTable: "messages",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "ChatMembers_UserId_fkey",
            table: "chat_members",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "Chats_CreatedById_fkey",
            table: "chats",
            column: "created_by_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "Departments_Head_fkey",
            table: "departments",
            column: "head_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "Departments_ChatId_fkey",
            table: "departments");

        migrationBuilder.DropForeignKey(
            name: "Departments_Head_fkey",
            table: "departments");

        migrationBuilder.DropTable(
            name: "chat_members");

        migrationBuilder.DropTable(
            name: "message_files");

        migrationBuilder.DropTable(
            name: "poll_votes");

        migrationBuilder.DropTable(
            name: "refresh_tokens");

        migrationBuilder.DropTable(
            name: "system_settings");

        migrationBuilder.DropTable(
            name: "user_settings");

        migrationBuilder.DropTable(
            name: "voice_messages");

        migrationBuilder.DropTable(
            name: "poll_options");

        migrationBuilder.DropTable(
            name: "polls");

        migrationBuilder.DropTable(
            name: "messages");

        migrationBuilder.DropTable(
            name: "chats");

        migrationBuilder.DropTable(
            name: "users");

        migrationBuilder.DropTable(
            name: "departments");
    }
}
