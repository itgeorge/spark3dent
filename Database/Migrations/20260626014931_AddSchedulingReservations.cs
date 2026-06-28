using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchedulingReservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClinicCode = table.Column<string>(type: "TEXT", nullable: false),
                    ClinicDisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialId = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialLabel = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialPinHashFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    CaseName = table.Column<string>(type: "TEXT", nullable: false),
                    ImpressionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProductCategory = table.Column<string>(type: "TEXT", nullable: false),
                    Material = table.Column<string>(type: "TEXT", nullable: false),
                    WorkItemsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedDeliveryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Shade = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    ColorNote = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUnixTimeMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CalculatedCapacityUnits = table.Column<decimal>(type: "TEXT", nullable: true),
                    CreatedIp = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUserAgent = table.Column<string>(type: "TEXT", nullable: false),
                    PromotedOrderId = table.Column<long>(type: "INTEGER", nullable: true),
                    PromotedOrderCode = table.Column<string>(type: "TEXT", nullable: true),
                    PromotedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingReservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingReservations_ClinicCode",
                table: "SchedulingReservations",
                column: "ClinicCode");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingReservations_CreatedAtUnixTimeMilliseconds",
                table: "SchedulingReservations",
                column: "CreatedAtUnixTimeMilliseconds");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingReservations_ImpressionDate",
                table: "SchedulingReservations",
                column: "ImpressionDate");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingReservations_RequestedDeliveryDate",
                table: "SchedulingReservations",
                column: "RequestedDeliveryDate");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingReservations_Status",
                table: "SchedulingReservations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulingReservations");
        }
    }
}
