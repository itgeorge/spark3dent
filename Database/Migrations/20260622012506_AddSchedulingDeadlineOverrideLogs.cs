using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingDeadlineOverrideLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchedulingDeadlineOverrideLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<long>(type: "INTEGER", nullable: false),
                    OrderCode = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUnixTimeMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByOrganizationType = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByOrganizationCode = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByMemberId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByMemberLabel = table.Column<string>(type: "TEXT", nullable: false),
                    SelectedDeadlineDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SystemRecommendedDeadlineDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    MinimumDeadlineDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    OrderCapacityUnits = table.Column<decimal>(type: "TEXT", nullable: false),
                    RulesBypassedJson = table.Column<string>(type: "TEXT", nullable: false),
                    OverrideReason = table.Column<string>(type: "TEXT", nullable: false),
                    RecommendationLogId = table.Column<long>(type: "INTEGER", nullable: true),
                    ExistingDailyCapacityUsed = table.Column<decimal>(type: "TEXT", nullable: true),
                    ExistingWeeklyCapacityUsed = table.Column<decimal>(type: "TEXT", nullable: true),
                    DailyCapacityLimitUsed = table.Column<decimal>(type: "TEXT", nullable: true),
                    WeeklyCapacityLimitUsed = table.Column<decimal>(type: "TEXT", nullable: true),
                    DailyCapacityAfterOverride = table.Column<decimal>(type: "TEXT", nullable: true),
                    WeeklyCapacityAfterOverride = table.Column<decimal>(type: "TEXT", nullable: true),
                    CalendarReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingDeadlineOverrideLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingDeadlineOverrideLogs_CreatedAtUnixTimeMilliseconds",
                table: "SchedulingDeadlineOverrideLogs",
                column: "CreatedAtUnixTimeMilliseconds");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingDeadlineOverrideLogs_OrderCode",
                table: "SchedulingDeadlineOverrideLogs",
                column: "OrderCode");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingDeadlineOverrideLogs_OrderId",
                table: "SchedulingDeadlineOverrideLogs",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulingDeadlineOverrideLogs");
        }
    }
}
