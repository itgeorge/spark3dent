using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingDeadlineRecommendationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchedulingDeadlineRecommendationLogs",
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
                    OrderCreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EffectiveIntakeBusinessDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CutoffTimeUsed = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    Material = table.Column<string>(type: "TEXT", nullable: false),
                    ToothCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LeadTimeBusinessDaysUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    FixedLeadTimeBusinessDaysUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    ExtraLeadTimeBusinessDaysUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    TeethPerExtraLeadDayUsed = table.Column<int>(type: "INTEGER", nullable: true),
                    CapacityUnitsPerToothUsed = table.Column<decimal>(type: "TEXT", nullable: false),
                    CalculatedOrderCapacityUnits = table.Column<decimal>(type: "TEXT", nullable: false),
                    MinimumDeadlineDateFromLeadTime = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FinalRecommendedDeadlineDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SelectedDeadlineDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SearchStartedAtDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SearchEndedAtDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SearchLimitDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ResultStatus = table.Column<string>(type: "TEXT", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    CandidateChecksJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigSnapshotJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingDeadlineRecommendationLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingDeadlineRecommendationLogs_CreatedAtUnixTimeMilliseconds",
                table: "SchedulingDeadlineRecommendationLogs",
                column: "CreatedAtUnixTimeMilliseconds");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingDeadlineRecommendationLogs_OrderCode",
                table: "SchedulingDeadlineRecommendationLogs",
                column: "OrderCode");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingDeadlineRecommendationLogs_OrderId",
                table: "SchedulingDeadlineRecommendationLogs",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulingDeadlineRecommendationLogs");
        }
    }
}
