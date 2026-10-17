using System;
using Conversa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conversa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261017135500_AddConversationContextSummaries")]
public partial class AddConversationContextSummaries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ConversationContextSummaries",
            columns: table => new
            {
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                Summary = table.Column<string>(type: "text", nullable: false),
                ThroughCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ThroughMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConversationContextSummaries", x => x.ConversationId);
                table.ForeignKey(
                    name: "FK_ConversationContextSummaries_Conversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "Conversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ConversationContextSummaries");
    }
}
