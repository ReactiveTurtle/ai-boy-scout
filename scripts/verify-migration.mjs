import { createHash } from "node:crypto";
import { homedir } from "node:os";
import { join, resolve } from "node:path";
import { DatabaseSync } from "node:sqlite";

const backupPath = process.argv[2];
if (!backupPath) {
  throw new Error("Usage: node scripts/verify-migration.mjs <backup-database-path>");
}

const dataDirectory = process.env.LOCALAPPDATA
  ? join(process.env.LOCALAPPDATA, "boy-scout-mcp")
  : join(homedir(), ".boy-scout-mcp");
const livePath = resolve(process.env.BOY_SCOUT_DB ?? join(dataDirectory, "reports.db"));

const backup = new DatabaseSync(resolve(backupPath), { readOnly: true });
const live = new DatabaseSync(livePath, { readOnly: true });

try {
  const immutableReportColumns = [
    "sequence",
    "project",
    "title",
    "location",
    "observation",
    "impact",
    "suggested_action",
    "discovered_during",
    "created_at",
  ];
  const select = `SELECT ${immutableReportColumns.join(", ")} FROM reports ORDER BY sequence`;
  const backupRows = backup.prepare(select).all();
  const liveRows = live.prepare(select).all();
  const backupHash = hash(backupRows);
  const livePrefixHash = hash(liveRows.slice(0, backupRows.length));

  if (backupHash !== livePrefixHash) {
    throw new Error("Immutable report content differs from the pre-migration backup");
  }

  const userVersion = live.prepare("PRAGMA user_version").get().user_version;
  const orphanReports = live
    .prepare("SELECT COUNT(*) AS count FROM reports WHERE project_id IS NULL")
    .get().count;
  const foreignKeyErrors = live.prepare("PRAGMA foreign_key_check").all().length;
  const triggerCount = live
    .prepare(
      "SELECT COUNT(*) AS count FROM sqlite_master WHERE type = 'trigger' AND name = 'reports_assign_project_after_legacy_insert'",
    )
    .get().count;
  const taskMigrationCount = live
    .prepare(
      "SELECT COUNT(*) AS count FROM schema_migrations WHERE version = 3 AND name = 'task_pipeline'",
    )
    .get().count;
  const relevanceMigrationCount = live
    .prepare(
      "SELECT COUNT(*) AS count FROM schema_migrations WHERE version = 4 AND name = 'verify_note_relevance'",
    )
    .get().count;
  const accessMigrationCount = live
    .prepare(
      "SELECT COUNT(*) AS count FROM schema_migrations WHERE version = 5 AND name = 'project_ai_read_consent'",
    )
    .get().count;
  const rejectionReviewMigrationCount = live
    .prepare(
      "SELECT COUNT(*) AS count FROM schema_migrations WHERE version = 6 AND name = 'rejection_review_intent'",
    )
    .get().count;
  const queuedProposalMigrationCount = live
    .prepare(
      "SELECT COUNT(*) AS count FROM schema_migrations WHERE version = 7 AND name = 'queued_task_proposals'",
    )
    .get().count;
  const clarificationMigrationCount = live
    .prepare(
      "SELECT COUNT(*) AS count FROM schema_migrations WHERE version = 8 AND name = 'proposal_clarification'",
    )
    .get().count;
  const humanDecisionMigrationCount = live
    .prepare(
      "SELECT COUNT(*) AS count FROM schema_migrations WHERE version = 9 AND name = 'human_confirmed_agent_decision'",
    )
    .get().count;
  const proposalTableSql = live
    .prepare("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'task_proposals'")
    .get()?.sql;
  const proposalColumns = live.prepare("PRAGMA table_info(task_proposals)").all().map((column) => column.name);
  const hasReviewRequestColumns = [
    "review_intent",
    "requested_rejection_reason",
    "requested_clarification",
  ].every((column) => proposalColumns.includes(column));
  const hasDecisionColumns = [
    "recommended_action",
    "final_rejection_reason",
    "localized_report_title",
    "localized_report_observation",
    "localized_report_impact",
    "localized_report_suggested_action",
  ].every((column) => proposalColumns.includes(column));
  const grantedProjectCount = live.prepare("SELECT COUNT(*) AS count FROM project_ai_access").get().count;
  const taskProposalCount = live.prepare("SELECT COUNT(*) AS count FROM task_proposals").get().count;
  const taskCount = live.prepare("SELECT COUNT(*) AS count FROM tasks").get().count;
  const taskLinkCount = live.prepare("SELECT COUNT(*) AS count FROM report_tasks").get().count;
  const aiProcessingEnabled = live
    .prepare("SELECT value FROM automation_settings WHERE key = 'ai_processing_enabled'")
    .get()?.value;

  if (
    userVersion !== 9 ||
    orphanReports !== 0 ||
    foreignKeyErrors !== 0 ||
    triggerCount !== 1 ||
    taskMigrationCount !== 1 ||
    relevanceMigrationCount !== 1 ||
    accessMigrationCount !== 1 ||
    rejectionReviewMigrationCount !== 1 ||
    queuedProposalMigrationCount !== 1 ||
    clarificationMigrationCount !== 1 ||
    humanDecisionMigrationCount !== 1 ||
    !hasReviewRequestColumns ||
    !hasDecisionColumns ||
    !proposalTableSql?.includes("'queued'") ||
    !["true", "false"].includes(aiProcessingEnabled)
  ) {
    throw new Error(
      `Invalid migrated schema: version=${userVersion}, orphans=${orphanReports}, foreignKeyErrors=${foreignKeyErrors}, trigger=${triggerCount}, taskMigration=${taskMigrationCount}, relevanceMigration=${relevanceMigrationCount}, accessMigration=${accessMigrationCount}, rejectionReviewMigration=${rejectionReviewMigrationCount}, queuedProposalMigration=${queuedProposalMigrationCount}, clarificationMigration=${clarificationMigrationCount}, humanDecisionMigration=${humanDecisionMigrationCount}, reviewRequestColumns=${hasReviewRequestColumns}, decisionColumns=${hasDecisionColumns}, queuedState=${proposalTableSql?.includes("'queued'")}, aiProcessing=${aiProcessingEnabled}`,
    );
  }

  console.log(
    JSON.stringify(
      {
        livePath,
        backupPath: resolve(backupPath),
        reportsInBackup: backupRows.length,
        reportsInLiveDatabase: liveRows.length,
        lastSequence: liveRows.at(-1)?.sequence ?? null,
        projects: live.prepare("SELECT COUNT(*) AS count FROM projects").get().count,
        projectPaths: live.prepare("SELECT COUNT(*) AS count FROM project_paths").get().count,
        userVersion,
        orphanReports,
        foreignKeyErrors,
        taskProposals: taskProposalCount,
        tasks: taskCount,
        taskLinks: taskLinkCount,
        aiProcessingEnabled,
        grantedProjects: grantedProjectCount,
        originalContentHash: backupHash,
      },
      null,
      2,
    ),
  );
} finally {
  backup.close();
  live.close();
}

function hash(rows) {
  return createHash("sha256").update(JSON.stringify(rows)).digest("hex");
}
