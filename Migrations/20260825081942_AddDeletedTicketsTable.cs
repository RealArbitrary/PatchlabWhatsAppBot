using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchlabWhatsAppBot.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedTicketsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeletedTickets",
                columns: table => new
                {
                    ArchiveId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    TicketNumber = table.Column<string>(type: "varchar(9)", nullable: true),
                    CellphoneNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Issue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CommentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FeedbackJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedTickets", x => x.ArchiveId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeletedTickets_DeletedAt",
                table: "DeletedTickets",
                column: "DeletedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeletedTickets");
        }
    }
}
