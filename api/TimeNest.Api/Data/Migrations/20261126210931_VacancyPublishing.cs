using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeNest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class VacancyPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Vacancies",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "Vacancies",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Vacancies");
        }
    }
}
