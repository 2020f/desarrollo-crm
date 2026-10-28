using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Desarrollo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CalendarEventFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeId",
                table: "CalendarEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "CalendarEvents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "CalendarEvents",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssigneeId",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "CalendarEvents");
        }
    }
}
