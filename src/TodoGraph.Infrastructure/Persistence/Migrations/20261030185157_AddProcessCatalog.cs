using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoGraph.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "process_blocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_blocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_process_blocks_processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "process_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false),
                    BackwardTargetStageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BackwardMaxRepeats = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessBlockId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_process_stages_process_blocks_ProcessBlockId",
                        column: x => x.ProcessBlockId,
                        principalTable: "process_blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_process_blocks_ProcessId_Position",
                table: "process_blocks",
                columns: new[] { "ProcessId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_process_stages_ProcessBlockId_Position",
                table: "process_stages",
                columns: new[] { "ProcessBlockId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "process_stages");

            migrationBuilder.DropTable(
                name: "process_blocks");

            migrationBuilder.DropTable(
                name: "processes");
        }
    }
}
