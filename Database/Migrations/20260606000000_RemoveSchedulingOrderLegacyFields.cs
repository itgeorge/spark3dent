#nullable disable

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Database.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260606000000_RemoveSchedulingOrderLegacyFields")]
    public partial class RemoveSchedulingOrderLegacyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM SchedulingOrders;");
            migrationBuilder.Sql("DELETE FROM AuditEvents WHERE EntityType = 'SchedulingOrder';");
            migrationBuilder.Sql("DROP TABLE SchedulingOrders;");
            CreateSchedulingOrdersTable(migrationBuilder, includeLegacyColumns: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM SchedulingOrders;");
            migrationBuilder.Sql("DROP TABLE SchedulingOrders;");
            CreateSchedulingOrdersTable(migrationBuilder, includeLegacyColumns: true);
        }

        private static void CreateSchedulingOrdersTable(MigrationBuilder migrationBuilder, bool includeLegacyColumns)
        {
            var legacyColumns = includeLegacyColumns
                ? """
                WorkType TEXT NOT NULL DEFAULT '',
                ConstructionType TEXT NOT NULL DEFAULT '',
                ToothStart INTEGER NOT NULL DEFAULT 0,
                ToothEnd INTEGER NOT NULL DEFAULT 0,
                AbutmentTeeth TEXT NOT NULL DEFAULT '',
                """
                : "";

            migrationBuilder.Sql($$"""
                CREATE TABLE SchedulingOrders (
                    Id INTEGER NOT NULL CONSTRAINT PK_SchedulingOrders PRIMARY KEY AUTOINCREMENT,
                    OrderCode TEXT NOT NULL,
                    ClinicCode TEXT NOT NULL,
                    ClinicDisplayName TEXT NOT NULL,
                    CredentialId TEXT NOT NULL,
                    CredentialLabel TEXT NOT NULL,
                    CredentialPinHashFingerprint TEXT NOT NULL,
                    CaseName TEXT NOT NULL,
                    ImpressionDate TEXT NOT NULL,
                    ProductCategory TEXT NOT NULL,
                    {{legacyColumns}}Material TEXT NOT NULL,
                    WorkItemsJson TEXT NOT NULL,
                    RequestedDeliveryDate TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    Shade INTEGER NOT NULL,
                    Notes TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    CreatedAtUnixTimeMilliseconds INTEGER NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    CreatedIp TEXT NOT NULL,
                    CreatedUserAgent TEXT NOT NULL
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_ClinicCode",
                table: "SchedulingOrders",
                column: "ClinicCode");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_CreatedAtUnixTimeMilliseconds",
                table: "SchedulingOrders",
                column: "CreatedAtUnixTimeMilliseconds");

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
    }
}
