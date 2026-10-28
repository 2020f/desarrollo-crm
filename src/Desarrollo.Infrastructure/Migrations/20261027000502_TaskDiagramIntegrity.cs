using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Desarrollo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaskDiagramIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreationRequestId",
                table: "WorkItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollapsedTaskIds",
                table: "TaskDiagramUserViews",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_AuthorId_CreationRequestId",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "AuthorId", "CreationRequestId" },
                unique: true,
                filter: "[CreationRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDiagramUserViews_UserId",
                table: "TaskDiagramUserViews",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskDiagramPositions_Projects_ProjectId",
                table: "TaskDiagramPositions",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskDiagramPositions_WorkItems_TaskId",
                table: "TaskDiagramPositions",
                column: "TaskId",
                principalTable: "WorkItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskDiagramUserViews_Projects_ProjectId",
                table: "TaskDiagramUserViews",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskDiagramUserViews_Users_UserId",
                table: "TaskDiagramUserViews",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskDiagramPositions_Projects_ProjectId",
                table: "TaskDiagramPositions");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskDiagramPositions_WorkItems_TaskId",
                table: "TaskDiagramPositions");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskDiagramUserViews_Projects_ProjectId",
                table: "TaskDiagramUserViews");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskDiagramUserViews_Users_UserId",
                table: "TaskDiagramUserViews");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_AuthorId_CreationRequestId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskDiagramUserViews_UserId",
                table: "TaskDiagramUserViews");

            migrationBuilder.DropColumn(
                name: "CreationRequestId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "CollapsedTaskIds",
                table: "TaskDiagramUserViews");
        }
    }
}
