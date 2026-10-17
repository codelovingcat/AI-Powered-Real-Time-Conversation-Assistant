using System;
using Conversa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conversa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261017190000_AddUserAssistantPreferences")]
public partial class AddUserAssistantPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserAssistantPreferences",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ResponseLength = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Tone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ExplanationLevel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                SuggestReplies = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserAssistantPreferences", x => x.UserId);
                table.CheckConstraint(
                    "CK_UserAssistantPreferences_ResponseLength",
                    "\"ResponseLength\" IN ('concise', 'balanced', 'detailed')");
                table.CheckConstraint(
                    "CK_UserAssistantPreferences_Tone",
                    "\"Tone\" IN ('casual', 'contextual', 'formal')");
                table.CheckConstraint(
                    "CK_UserAssistantPreferences_ExplanationLevel",
                    "\"ExplanationLevel\" IN ('minimal', 'balanced', 'detailed')");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserAssistantPreferences");
    }
}
