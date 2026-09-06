using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wida.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Processor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessorVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RawResult = table.Column<string>(type: "jsonb", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingRuns_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExtractedFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RawValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NormalizedValue = table.Column<string>(type: "jsonb", nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    BoundingBox = table.Column<string>(type: "jsonb", nullable: true),
                    RequiresReview = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractedFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtractedFields_ProcessingRuns_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "ProcessingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractedFields_FieldName",
                table: "ExtractedFields",
                column: "FieldName");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractedFields_ProcessingRunId",
                table: "ExtractedFields",
                column: "ProcessingRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRuns_DocumentId",
                table: "ProcessingRuns",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtractedFields");

            migrationBuilder.DropTable(
                name: "ProcessingRuns");
        }
    }
}
