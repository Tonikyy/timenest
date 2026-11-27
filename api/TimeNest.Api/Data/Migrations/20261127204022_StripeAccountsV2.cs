using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeNest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StripeAccountsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "StripeConnections",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreationApiVersion",
                table: "StripeConnections",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "StripeConnections");

            migrationBuilder.DropColumn(
                name: "CreationApiVersion",
                table: "StripeConnections");
        }
    }
}
