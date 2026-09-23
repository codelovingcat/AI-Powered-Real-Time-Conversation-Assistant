using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conversa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260922223000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Conversations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Instruction = table.Column<string>(type: "text", nullable: false),
                SourceLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                TargetLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Conversations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OriginalText = table.Column<string>(type: "text", nullable: false),
                Translation = table.Column<string>(type: "text", nullable: true),
                Explanation = table.Column<string>(type: "text", nullable: true),
                SuggestedAnswer = table.Column<string>(type: "text", nullable: true),
                SuggestedAnswerTranslation = table.Column<string>(type: "text", nullable: true),
                ResponseType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                QuestionDetected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                QuestionDirectedAtUser = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Messages", x => x.Id);
                table.ForeignKey(
                    name: "FK_Messages_Conversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "Conversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Conversations_UserId_UpdatedAt",
            table: "Conversations",
            columns: new[] { "UserId", "UpdatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Messages_ConversationId_CreatedAt",
            table: "Messages",
            columns: new[] { "ConversationId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Messages");
        migrationBuilder.DropTable(name: "Conversations");
    }
}
