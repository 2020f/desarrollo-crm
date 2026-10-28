using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Desarrollo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentFileVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "StoredFiles",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "StoredFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "StoredFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContentFormat",
                table: "DocumentVersions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "plain");

            migrationBuilder.AddColumn<string>(
                name: "ContentFormat",
                table: "Documents",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "plain");

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DocumentReaders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentReaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentReaders_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentReaders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OrganizationId_UploaderId",
                table: "StoredFiles",
                columns: new[] { "OrganizationId", "UploaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OrganizationId_Visibility_ProjectId",
                table: "StoredFiles",
                columns: new[] { "OrganizationId", "Visibility", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ProjectId",
                table: "StoredFiles",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ProjectId_Visibility_AuthorId",
                table: "Documents",
                columns: new[] { "ProjectId", "Visibility", "AuthorId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReaders_DocumentId_UserId",
                table: "DocumentReaders",
                columns: new[] { "DocumentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReaders_UserId",
                table: "DocumentReaders",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_StoredFiles_Projects_ProjectId",
                table: "StoredFiles",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoredFiles_Projects_ProjectId",
                table: "StoredFiles");

            migrationBuilder.DropTable(
                name: "DocumentReaders");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_OrganizationId_UploaderId",
                table: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_OrganizationId_Visibility_ProjectId",
                table: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_ProjectId",
                table: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ProjectId_Visibility_AuthorId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "ContentFormat",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "ContentFormat",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Documents");
        }
    }
}
