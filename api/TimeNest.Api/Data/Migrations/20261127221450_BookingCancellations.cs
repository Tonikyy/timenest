using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeNest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BookingCancellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Bookings_Active",
                table: "Bookings");

            migrationBuilder.AddColumn<string>(
                name: "CancellationPolicy",
                table: "Bookings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "LegacyFullRefundBeforeStart");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckoutCancellationRequestedAt",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookingCancellations",
                columns: table => new
                {
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RefundAmountCents = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingCancellations", x => x.BookingId);
                    table.CheckConstraint("CK_Cancellations_Refund", "[RefundAmountCents] >= 0");
                    table.ForeignKey(
                        name: "FK_BookingCancellations_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StripeRefundId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.BookingId);
                    table.CheckConstraint("CK_Refunds_Amount", "[AmountCents] > 0");
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_BookingCancellations_BookingId",
                        column: x => x.BookingId,
                        principalTable: "BookingCancellations",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bookings_Active",
                table: "Bookings",
                sql: "([Status] IN ('Pending', 'Confirmed') AND [IsActive] = 1) OR ([Status] IN ('Expired', 'Cancelled') AND [IsActive] = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_StripeRefundId",
                table: "PaymentRefunds",
                column: "StripeRefundId",
                unique: true,
                filter: "[StripeRefundId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "BookingCancellations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Bookings_Active",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationPolicy",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckoutCancellationRequestedAt",
                table: "Bookings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bookings_Active",
                table: "Bookings",
                sql: "([Status] IN ('Pending', 'Confirmed') AND [IsActive] = 1) OR ([Status] = 'Expired' AND [IsActive] = 0)");
        }
    }
}
