using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingCapacityConfigsAndOrderCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedCapacityUnits",
                table: "SchedulingOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchedulingCapacityConfigs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActiveFromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DailyCapacityUnits = table.Column<decimal>(type: "TEXT", nullable: false),
                    WeeklyCapacityUnits = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingCapacityConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingCapacityConfigs_ActiveFromDate",
                table: "SchedulingCapacityConfigs",
                column: "ActiveFromDate",
                unique: true);

            migrationBuilder.InsertData(
                table: "SchedulingCapacityConfigs",
                columns: ["Id", "ActiveFromDate", "DailyCapacityUnits", "WeeklyCapacityUnits", "CreatedAt", "UpdatedAt"],
                values: new object[]
                {
                    1L,
                    new DateOnly(2026, 1, 1),
                    100.0m,
                    500.0m,
                    new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero),
                    new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero)
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SchedulingCapacityConfigs",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DropTable(
                name: "SchedulingCapacityConfigs");

            migrationBuilder.DropColumn(
                name: "CalculatedCapacityUnits",
                table: "SchedulingOrders");
        }
    }
}
