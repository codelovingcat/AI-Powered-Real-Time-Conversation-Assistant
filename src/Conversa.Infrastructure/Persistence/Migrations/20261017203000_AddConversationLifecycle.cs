using System;
using Conversa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conversa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261017203000_AddConversationLifecycle")]
public partial class AddConversationLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LifecycleState",
            table: "Conversations",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Active");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "StartedAt",
            table: "Conversations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CompletedAt",
            table: "Conversations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Conversations_LifecycleState",
            table: "Conversations",
            sql: "\"LifecycleState\" IN ('Ready', 'Active', 'Paused', 'Completed')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Conversations_LifecycleState",
            table: "Conversations");

        migrationBuilder.DropColumn(name: "LifecycleState", table: "Conversations");
        migrationBuilder.DropColumn(name: "StartedAt", table: "Conversations");
        migrationBuilder.DropColumn(name: "CompletedAt", table: "Conversations");
    }
}
