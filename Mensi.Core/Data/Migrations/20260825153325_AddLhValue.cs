using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mensi.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLhValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "lh_value",
                table: "daily_log",
                type: "numeric(3,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lh_value",
                table: "daily_log");
        }
    }
}
