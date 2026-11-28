using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeNest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefundSuccessMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SucceededAt",
                table: "PaymentRefunds",
                type: "datetimeoffset",
                nullable: true);

            // Historical success time is unknown; conservatively restart its monitoring window.
            migrationBuilder.Sql("UPDATE [PaymentRefunds] SET [SucceededAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') WHERE [Status] = N'Succeeded';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SucceededAt",
                table: "PaymentRefunds");
        }
    }
}
