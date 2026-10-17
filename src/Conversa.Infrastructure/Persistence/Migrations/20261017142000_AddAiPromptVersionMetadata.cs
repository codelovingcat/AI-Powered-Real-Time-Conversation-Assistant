using System;
using Conversa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conversa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261017142000_AddAiPromptVersionMetadata")]
public partial class AddAiPromptVersionMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PromptVersion",
            table: "Messages",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PromptVersion",
            table: "ConversationContextSummaries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PromptVersion", table: "Messages");
        migrationBuilder.DropColumn(name: "PromptVersion", table: "ConversationContextSummaries");
    }
}
