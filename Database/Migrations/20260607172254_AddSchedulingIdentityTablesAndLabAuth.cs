using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingIdentityTablesAndLabAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SchedulingAuthSessions_ClinicCode_CredentialId",
                table: "SchedulingAuthSessions");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationType",
                table: "SchedulingAuthSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "Clinic");

            migrationBuilder.CreateTable(
                name: "SchedulingClinics",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    LinkedClientNickname = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayColor = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingClinics", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "SchedulingLabs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingLabs", x => x.Id);
                    table.CheckConstraint("CK_SchedulingLabs_Singleton", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "SchedulingMembers",
                columns: table => new
                {
                    OrganizationType = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationCode = table.Column<string>(type: "TEXT", nullable: false),
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    PinHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingMembers", x => new { x.OrganizationType, x.OrganizationCode, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingAuthSessions_OrganizationType_ClinicCode_CredentialId",
                table: "SchedulingAuthSessions",
                columns: new[] { "OrganizationType", "ClinicCode", "CredentialId" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingLabs_Code",
                table: "SchedulingLabs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingMembers_OrganizationType_OrganizationCode_IsActive",
                table: "SchedulingMembers",
                columns: new[] { "OrganizationType", "OrganizationCode", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulingClinics");

            migrationBuilder.DropTable(
                name: "SchedulingLabs");

            migrationBuilder.DropTable(
                name: "SchedulingMembers");

            migrationBuilder.DropIndex(
                name: "IX_SchedulingAuthSessions_OrganizationType_ClinicCode_CredentialId",
                table: "SchedulingAuthSessions");

            migrationBuilder.DropColumn(
                name: "OrganizationType",
                table: "SchedulingAuthSessions");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingAuthSessions_ClinicCode_CredentialId",
                table: "SchedulingAuthSessions",
                columns: new[] { "ClinicCode", "CredentialId" });
        }
    }
}
