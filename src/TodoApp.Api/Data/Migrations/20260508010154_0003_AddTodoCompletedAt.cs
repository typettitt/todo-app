using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class _0003_AddTodoCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompletedAt",
                table: "Todos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"Todos\" SET \"CompletedAt\" = \"UpdatedAt\" WHERE \"IsCompleted\" = 1 AND \"CompletedAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Todos");
        }
    }
}
