using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mensi.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cycle",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    length_days = table.Column<int>(type: "integer", nullable: true),
                    ovulation_day_estimated = table.Column<int>(type: "integer", nullable: true),
                    ovulation_day_confirmed = table.Column<int>(type: "integer", nullable: true),
                    luteal_phase_length = table.Column<int>(type: "integer", nullable: true),
                    anovulatory = table.Column<bool>(type: "boolean", nullable: false),
                    predicted_length_days = table.Column<int>(type: "integer", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycle", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "daily_log",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    bbt_celsius = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    cervical_mucus = table.Column<short>(type: "smallint", nullable: true),
                    lh_test = table.Column<short>(type: "smallint", nullable: true),
                    cramp_type = table.Column<short>(type: "smallint", nullable: true),
                    cramp_severity = table.Column<short>(type: "smallint", nullable: true),
                    flow_intensity = table.Column<short>(type: "smallint", nullable: true),
                    period_start = table.Column<bool>(type: "boolean", nullable: false),
                    moods = table.Column<short[]>(type: "smallint[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_log", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "intercourse_event",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    @protected = table.Column<bool>(name: "protected", type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intercourse_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_intercourse_event_daily_log_date",
                        column: x => x.date,
                        principalTable: "daily_log",
                        principalColumn: "date",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_at",
                table: "audit_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_start_date",
                table: "cycle",
                column: "start_date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_intercourse_event_date",
                table: "intercourse_event",
                column: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "cycle");

            migrationBuilder.DropTable(
                name: "intercourse_event");

            migrationBuilder.DropTable(
                name: "daily_log");
        }
    }
}
