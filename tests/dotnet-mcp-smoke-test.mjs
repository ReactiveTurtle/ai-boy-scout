import { randomUUID } from "node:crypto";
import { existsSync, mkdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { DatabaseSync } from "node:sqlite";

import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const temporaryDirectory = join(tmpdir(), `boy-scout-dotnet-mcp-${randomUUID()}`);
const databasePath = join(temporaryDirectory, "reports.db");
const serverPath = process.env.BOY_SCOUT_MCP_DLL
  ? resolve(process.env.BOY_SCOUT_MCP_DLL)
  : fileURLToPath(
      new URL("../src/BoyScout.Mcp/bin/Debug/net10.0/BoyScout.Mcp.dll", import.meta.url),
    );
const project = join(temporaryDirectory, "sample-project");
const legacyProject = join(temporaryDirectory, "legacy-project");

mkdirSync(temporaryDirectory, { recursive: true });
createLegacyDatabase();

const transport = new StdioClientTransport({
  command: "dotnet",
  args: [serverPath],
  env: { ...process.env, BOY_SCOUT_DB: databasePath },
  stderr: "pipe",
});
const client = new Client({ name: "boy-scout-dotnet-smoke-test", version: "1.0.0" });
let clientConnected = false;

function parseResult(result) {
  if (result.isError) {
    throw new Error(result.content?.[0]?.text ?? "MCP tool returned an error");
  }
  return JSON.parse(result.content[0].text);
}

try {
  await client.connect(transport);
  clientConnected = true;

  const tools = await client.listTools();
  const toolNames = tools.tools.map((tool) => tool.name).sort();
  const expectedToolNames = [
    "boy_scout_add",
    "boy_scout_get",
    "boy_scout_list",
    "boy_scout_update_status",
  ];
  if (JSON.stringify(toolNames) !== JSON.stringify(expectedToolNames)) {
    throw new Error(`Unexpected MCP tools: ${toolNames.join(", ")}`);
  }

  const addTool = tools.tools.find((tool) => tool.name === "boy_scout_add");
  if (!("suggested_action" in addTool.inputSchema.properties)) {
    throw new Error("boy_scout_add did not preserve the suggested_action parameter name");
  }
  const updateTool = tools.tools.find((tool) => tool.name === "boy_scout_update_status");
  const statuses = updateTool.inputSchema.properties.status.enum;
  if (JSON.stringify(statuses) !== JSON.stringify(["open", "accepted", "planned", "resolved", "dismissed"])) {
    throw new Error(`boy_scout_update_status exposed unexpected statuses: ${statuses}`);
  }

  const legacy = parseResult(
    await client.callTool({ name: "boy_scout_get", arguments: { id: "BSR-000001" } }),
  );
  if (legacy.title !== "Legacy report") {
    throw new Error("The .NET MCP did not preserve the legacy report");
  }

  const created = parseResult(
    await client.callTool({
      name: "boy_scout_add",
      arguments: {
        project,
        title: "Duplicate timeout handling",
        location: "src/client.ts:42",
        observation: "Two branches apply the same timeout independently.",
        impact: "The timeout behavior can diverge during maintenance.",
        suggested_action: "Choose one owner in a separate iteration.",
        discovered_during: "dotnet-smoke-test",
      },
    }),
  );
  if (created.id !== "BSR-000002" || created.status !== "open") {
    throw new Error("boy_scout_add returned an unexpected report");
  }

  const listed = parseResult(
    await client.callTool({
      name: "boy_scout_list",
      arguments: { project, status: "open" },
    }),
  );
  if (listed.length !== 1 || listed[0].id !== created.id) {
    throw new Error("boy_scout_list did not return the created report");
  }

  const updated = parseResult(
    await client.callTool({
      name: "boy_scout_update_status",
      arguments: { id: created.id, status: "accepted" },
    }),
  );
  if (updated.status !== "accepted") {
    throw new Error("boy_scout_update_status did not persist the new status");
  }

  const missing = await client.callTool({
    name: "boy_scout_get",
    arguments: { id: "BSR-999999" },
  });
  if (!missing.isError || !missing.content[0].text.includes("was not found")) {
    throw new Error("boy_scout_get did not return the expected not-found tool error");
  }

  await client.close();
  clientConnected = false;

  const database = new DatabaseSync(databasePath, { readOnly: true });
  try {
    if (database.prepare("PRAGMA user_version").get().user_version !== 9) {
      throw new Error("The .NET MCP did not migrate the legacy database to version 9");
    }
    if (database.prepare("SELECT COUNT(*) AS count FROM reports WHERE project_id IS NULL").get().count !== 0) {
      throw new Error("The .NET MCP left reports without stable projects");
    }
    for (const table of ["task_proposals", "tasks", "report_tasks", "project_ai_access"]) {
      if (
        database
          .prepare("SELECT COUNT(*) AS count FROM sqlite_master WHERE type = 'table' AND name = ?")
          .get(table).count !== 1
      ) {
        throw new Error(`The .NET MCP did not create the ${table} table`);
      }
    }
    if (database.prepare("SELECT COUNT(*) AS count FROM project_ai_access").get().count !== 0) {
      throw new Error("The .NET MCP granted project access during migration");
    }
    const proposalColumns = database
      .prepare("PRAGMA table_info(task_proposals)")
      .all()
      .map((column) => column.name);
    if (
      ![
        "review_intent",
        "requested_rejection_reason",
        "requested_clarification",
        "recommended_action",
        "final_rejection_reason",
        "localized_report_title",
        "localized_report_observation",
        "localized_report_impact",
        "localized_report_suggested_action",
      ].every((column) => proposalColumns.includes(column))
    ) {
      throw new Error("The .NET MCP did not create the schema v9 proposal columns");
    }
    const proposalTableSql = database
      .prepare("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'task_proposals'")
      .get()?.sql;
    if (!proposalTableSql?.includes("'queued'")) {
      throw new Error("The .NET MCP did not enable queued task proposals");
    }
    if (
      database
        .prepare("SELECT value FROM automation_settings WHERE key = 'ai_processing_enabled'")
        .get()?.value !== "true"
    ) {
      throw new Error("The .NET MCP did not initialize AI processing settings");
    }
  } finally {
    database.close();
  }

  console.log("Boy Scout .NET MCP smoke test passed.");
} finally {
  if (clientConnected) {
    await client.close();
  }
  if (existsSync(temporaryDirectory)) {
    rmSync(temporaryDirectory, { recursive: true, force: true });
  }
}

function createLegacyDatabase() {
  const database = new DatabaseSync(databasePath);
  try {
    database.exec(`
      CREATE TABLE reports (
        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
        project TEXT NOT NULL,
        title TEXT NOT NULL,
        location TEXT,
        observation TEXT NOT NULL,
        impact TEXT,
        suggested_action TEXT,
        discovered_during TEXT,
        status TEXT NOT NULL DEFAULT 'open'
          CHECK (status IN ('open', 'accepted', 'planned', 'resolved', 'dismissed')),
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );
      CREATE INDEX reports_project_status
        ON reports (project, status, sequence DESC);
      INSERT INTO reports (
        sequence, project, title, observation, status, created_at, updated_at
      ) VALUES (
        1, '${legacyProject.replaceAll("'", "''")}', 'Legacy report', 'Existing observation',
        'open', '2026-09-29T00:00:00.000Z', '2026-09-29T00:00:00.000Z'
      );
    `);
  } finally {
    database.close();
  }
}
