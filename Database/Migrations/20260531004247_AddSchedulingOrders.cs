using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchedulingAuthSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ClinicCode = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialId = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AbsoluteExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedIp = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUserAgent = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingAuthSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SchedulingOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderCode = table.Column<string>(type: "TEXT", nullable: false),
                    ClinicCode = table.Column<string>(type: "TEXT", nullable: false),
                    ClinicDisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialId = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialLabel = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialPinHashFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    CaseName = table.Column<string>(type: "TEXT", nullable: false),
                    ImpressionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProductCategory = table.Column<string>(type: "TEXT", nullable: false),
                    WorkType = table.Column<string>(type: "TEXT", nullable: false),
                    Material = table.Column<string>(type: "TEXT", nullable: false),
                    ConstructionType = table.Column<string>(type: "TEXT", nullable: false),
                    ToothStart = table.Column<int>(type: "INTEGER", nullable: false),
                    ToothEnd = table.Column<int>(type: "INTEGER", nullable: false),
                    AbutmentTeeth = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedDeliveryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedIp = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUserAgent = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingOrders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingAuthSessions_ClinicCode_CredentialId",
                table: "SchedulingAuthSessions",
                columns: new[] { "ClinicCode", "CredentialId" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingAuthSessions_TokenHash",
                table: "SchedulingAuthSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_ClinicCode",
                table: "SchedulingOrders",
                column: "ClinicCode");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_CreatedAt",
                table: "SchedulingOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_OrderCode",
                table: "SchedulingOrders",
                column: "OrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_RequestedDeliveryDate",
                table: "SchedulingOrders",
                column: "RequestedDeliveryDate");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_Status",
                table: "SchedulingOrders",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulingAuthSessions");

            migrationBuilder.DropTable(
                name: "SchedulingOrders");
        }
    }
}
