using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchlabWhatsAppBot.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketFeedback",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketFeedback_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketFeedback_TicketId",
                table: "TicketFeedback",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketFeedback");
        }
    }
}
