using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoGraph.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessRunPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "process_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentBlockId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentStageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentUserPrompt = table.Column<string>(type: "TEXT", nullable: true),
                    PendingUserResponse = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "process_stage_snapshots",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false),
                    BackwardTargetStageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BackwardMaxRepeats = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_stage_snapshots", x => x.Hash);
                });

            migrationBuilder.CreateTable(
                name: "process_run_blocks",
                columns: table => new
                {
                    ProcessRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_run_blocks", x => new { x.ProcessRunId, x.BlockId });
                    table.ForeignKey(
                        name: "FK_process_run_blocks_process_runs_ProcessRunId",
                        column: x => x.ProcessRunId,
                        principalTable: "process_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "process_run_stages",
                columns: table => new
                {
                    ProcessRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StageHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_run_stages", x => new { x.ProcessRunId, x.BlockId, x.StageId });
                    table.ForeignKey(
                        name: "FK_process_run_stages_process_run_blocks_ProcessRunId_BlockId",
                        columns: x => new { x.ProcessRunId, x.BlockId },
                        principalTable: "process_run_blocks",
                        principalColumns: new[] { "ProcessRunId", "BlockId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_process_run_stages_process_stage_snapshots_StageHash",
                        column: x => x.StageHash,
                        principalTable: "process_stage_snapshots",
                        principalColumn: "Hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "process_run_link_counters",
                columns: table => new
                {
                    ProcessRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceStageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepeatCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_run_link_counters", x => new { x.ProcessRunId, x.BlockId, x.SourceStageId });
                    table.ForeignKey(
                        name: "FK_process_run_link_counters_process_run_stages_ProcessRunId_BlockId_SourceStageId",
                        columns: x => new { x.ProcessRunId, x.BlockId, x.SourceStageId },
                        principalTable: "process_run_stages",
                        principalColumns: new[] { "ProcessRunId", "BlockId", "StageId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_process_run_blocks_ProcessRunId_Position",
                table: "process_run_blocks",
                columns: new[] { "ProcessRunId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_process_run_stages_ProcessRunId_BlockId_Position",
                table: "process_run_stages",
                columns: new[] { "ProcessRunId", "BlockId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_process_run_stages_StageHash",
                table: "process_run_stages",
                column: "StageHash");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_ProjectTaskId",
                table: "process_runs",
                column: "ProjectTaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "process_run_link_counters");

            migrationBuilder.DropTable(
                name: "process_run_stages");

            migrationBuilder.DropTable(
                name: "process_run_blocks");

            migrationBuilder.DropTable(
                name: "process_stage_snapshots");

            migrationBuilder.DropTable(
                name: "process_runs");
        }
    }
}
