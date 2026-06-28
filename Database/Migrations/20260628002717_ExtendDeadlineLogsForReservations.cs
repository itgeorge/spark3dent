using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class ExtendDeadlineLogsForReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "OrderId",
                table: "SchedulingDeadlineRecommendationLogs",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "OrderCode",
                table: "SchedulingDeadlineRecommendationLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "SchedulingDeadlineRecommendationLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "order");

            migrationBuilder.AddColumn<long>(
                name: "ReservationId",
                table: "SchedulingDeadlineRecommendationLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "OrderId",
                table: "SchedulingDeadlineOverrideLogs",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "OrderCode",
                table: "SchedulingDeadlineOverrideLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "SchedulingDeadlineOverrideLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "order");

            migrationBuilder.AddColumn<long>(
                name: "ReservationId",
                table: "SchedulingDeadlineOverrideLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingDeadlineRecommendationLogs_ReservationId",
                table: "SchedulingDeadlineRecommendationLogs",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingDeadlineOverrideLogs_ReservationId",
                table: "SchedulingDeadlineOverrideLogs",
                column: "ReservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SchedulingDeadlineRecommendationLogs_ReservationId",
                table: "SchedulingDeadlineRecommendationLogs");

            migrationBuilder.DropIndex(
                name: "IX_SchedulingDeadlineOverrideLogs_ReservationId",
                table: "SchedulingDeadlineOverrideLogs");

            migrationBuilder.DropColumn(
                name: "EntityType",
                table: "SchedulingDeadlineRecommendationLogs");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "SchedulingDeadlineRecommendationLogs");

            migrationBuilder.DropColumn(
                name: "EntityType",
                table: "SchedulingDeadlineOverrideLogs");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "SchedulingDeadlineOverrideLogs");

            migrationBuilder.AlterColumn<long>(
                name: "OrderId",
                table: "SchedulingDeadlineRecommendationLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OrderCode",
                table: "SchedulingDeadlineRecommendationLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "OrderId",
                table: "SchedulingDeadlineOverrideLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OrderCode",
                table: "SchedulingDeadlineOverrideLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
