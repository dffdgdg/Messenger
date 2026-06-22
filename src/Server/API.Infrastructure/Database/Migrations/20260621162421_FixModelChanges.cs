using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class FixModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:chat_role", "member,admin,owner")
                .Annotation("Npgsql:Enum:chat_type", "chat,department,contact,department_heads")
                .Annotation("Npgsql:Enum:system_event_type", "chat_created,member_added,member_removed,member_left,role_changed,call_started,call_ended,message_pinned,message_unpinned,chat_avatar_updated")
                .Annotation("Npgsql:Enum:theme", "light,dark,system,colored")
                .Annotation("Npgsql:Enum:user_status_type", "online,away,busy,do_not_disturb")
                .OldAnnotation("Npgsql:Enum:chat_role", "member,admin,owner")
                .OldAnnotation("Npgsql:Enum:chat_type", "chat,department,contact,department_heads")
                .OldAnnotation("Npgsql:Enum:system_event_type", "chat_created,member_added,member_removed,member_left,role_changed,call_started,call_ended,message_pinned,message_unpinned,chat_avatar_updated")
                .OldAnnotation("Npgsql:Enum:theme", "light,dark,system")
                .OldAnnotation("Npgsql:Enum:user_status_type", "online,away,busy,do_not_disturb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:chat_role", "member,admin,owner")
                .Annotation("Npgsql:Enum:chat_type", "chat,department,contact,department_heads")
                .Annotation("Npgsql:Enum:system_event_type", "chat_created,member_added,member_removed,member_left,role_changed,call_started,call_ended,message_pinned,message_unpinned,chat_avatar_updated")
                .Annotation("Npgsql:Enum:theme", "light,dark,system")
                .Annotation("Npgsql:Enum:user_status_type", "online,away,busy,do_not_disturb")
                .OldAnnotation("Npgsql:Enum:chat_role", "member,admin,owner")
                .OldAnnotation("Npgsql:Enum:chat_type", "chat,department,contact,department_heads")
                .OldAnnotation("Npgsql:Enum:system_event_type", "chat_created,member_added,member_removed,member_left,role_changed,call_started,call_ended,message_pinned,message_unpinned,chat_avatar_updated")
                .OldAnnotation("Npgsql:Enum:theme", "light,dark,system,colored")
                .OldAnnotation("Npgsql:Enum:user_status_type", "online,away,busy,do_not_disturb");
        }
    }
}
