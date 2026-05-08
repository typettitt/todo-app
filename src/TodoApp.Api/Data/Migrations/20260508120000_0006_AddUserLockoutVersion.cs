using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MaintenanceDbContext))]
    [Migration("20260508120000_0006_AddUserLockoutVersion")]
    public partial class _0006_AddUserLockoutVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LockoutVersion",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockoutVersion",
                table: "Users");
        }
    }
}
