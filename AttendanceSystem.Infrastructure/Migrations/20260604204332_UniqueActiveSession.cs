using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueActiveSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_OneActiveSession",
                table: "AttendanceLogs",
                column: "EmployeeId",
                unique: true,
                filter: "[ClockOutUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OneActiveSession",
                table: "AttendanceLogs");
        }
    }
}
