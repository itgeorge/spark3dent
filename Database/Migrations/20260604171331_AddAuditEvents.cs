using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceName = table.Column<string>(type: "TEXT", nullable: false),
                    Operation = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    EntityDisplay = table.Column<string>(type: "TEXT", nullable: true),
                    ActorRole = table.Column<string>(type: "TEXT", nullable: false),
                    ActorClinicCode = table.Column<string>(type: "TEXT", nullable: true),
                    ActorCredentialId = table.Column<string>(type: "TEXT", nullable: true),
                    ActorCredentialLabel = table.Column<string>(type: "TEXT", nullable: true),
                    ActorSessionId = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OccurredAtUnixTimeMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    Ip = table.Column<string>(type: "TEXT", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorClinicCode_OccurredAtUnixTimeMilliseconds",
                table: "AuditEvents",
                columns: new[] { "ActorClinicCode", "OccurredAtUnixTimeMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EntityType_EntityId",
                table: "AuditEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAtUnixTimeMilliseconds",
                table: "AuditEvents",
                column: "OccurredAtUnixTimeMilliseconds");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ServiceName_Operation_OccurredAtUnixTimeMilliseconds",
                table: "AuditEvents",
                columns: new[] { "ServiceName", "Operation", "OccurredAtUnixTimeMilliseconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
