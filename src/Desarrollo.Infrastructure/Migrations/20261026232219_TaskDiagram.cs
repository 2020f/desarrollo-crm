using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Desarrollo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaskDiagram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccentColor",
                table: "WorkItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "WorkItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentTaskId",
                table: "WorkItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgressPercent",
                table: "WorkItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskDiagramPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskDiagramPositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskDiagramUserViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewportX = table.Column<double>(type: "float", nullable: false),
                    ViewportY = table.Column<double>(type: "float", nullable: false),
                    Zoom = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskDiagramUserViews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ParentTaskId",
                table: "WorkItems",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDiagramPositions_ProjectId",
                table: "TaskDiagramPositions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDiagramPositions_TaskId",
                table: "TaskDiagramPositions",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskDiagramUserViews_ProjectId_UserId",
                table: "TaskDiagramUserViews",
                columns: new[] { "ProjectId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_WorkItems_ParentTaskId",
                table: "WorkItems",
                column: "ParentTaskId",
                principalTable: "WorkItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_WorkItems_ParentTaskId",
                table: "WorkItems");

            migrationBuilder.DropTable(
                name: "TaskDiagramPositions");

            migrationBuilder.DropTable(
                name: "TaskDiagramUserViews");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_ParentTaskId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "AccentColor",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ParentTaskId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ProgressPercent",
                table: "WorkItems");
        }
    }
}
