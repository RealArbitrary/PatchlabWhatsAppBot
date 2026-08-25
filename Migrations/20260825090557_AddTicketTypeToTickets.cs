using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchlabWhatsAppBot.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketTypeToTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // a) Add nullable first — required so the backfill below has
            // somewhere to write existing rows before NOT NULL is enforced.
            migrationBuilder.AddColumn<int>(
                name: "TicketType",
                table: "Tickets",
                type: "int",
                nullable: true);

            // b) Backfill existing rows. 0 = TicketType.IT.
            migrationBuilder.Sql("UPDATE Tickets SET TicketType = 0 WHERE TicketType IS NULL;");

            // c) Now that every row has a value, enforce NOT NULL.
            migrationBuilder.AlterColumn<int>(
                name: "TicketType",
                table: "Tickets",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TicketType",
                table: "Tickets");
        }
    }
}
