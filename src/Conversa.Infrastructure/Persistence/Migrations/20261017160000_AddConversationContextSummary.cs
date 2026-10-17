using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conversa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261017160000_AddConversationContextSummary")]
public partial class AddConversationContextSummary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContextSummary",
            table: "Conversations",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ContextSummaryThroughMessageId",
            table: "Conversations",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ContextSummaryThroughCreatedAt",
            table: "Conversations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ContextSummaryRevision",
            table: "Conversations",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ContextSummary", table: "Conversations");
        migrationBuilder.DropColumn(name: "ContextSummaryThroughMessageId", table: "Conversations");
        migrationBuilder.DropColumn(name: "ContextSummaryThroughCreatedAt", table: "Conversations");
        migrationBuilder.DropColumn(name: "ContextSummaryRevision", table: "Conversations");
    }
}
