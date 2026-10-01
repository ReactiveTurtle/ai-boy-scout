import { mkdirSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { DatabaseSync, backup } from "node:sqlite";

const dataDirectory = process.env.LOCALAPPDATA
  ? join(process.env.LOCALAPPDATA, "boy-scout-mcp")
  : join(homedir(), ".boy-scout-mcp");
const sourcePath = resolve(process.env.BOY_SCOUT_DB ?? join(dataDirectory, "reports.db"));
const backupDirectory = join(dirname(sourcePath), "backups");
const timestamp = new Date().toISOString().replaceAll(":", "-");
const destinationPath = join(backupDirectory, `reports-${timestamp}.db`);

mkdirSync(backupDirectory, { recursive: true });

const source = new DatabaseSync(sourcePath, { readOnly: true });
try {
  await backup(source, destinationPath);
} finally {
  source.close();
}

const snapshot = new DatabaseSync(destinationPath, { readOnly: true });
try {
  const integrity = snapshot.prepare("PRAGMA integrity_check").get().integrity_check;
  if (integrity !== "ok") {
    throw new Error(`Backup integrity check failed: ${integrity}`);
  }

  const reportStats = snapshot
    .prepare("SELECT COUNT(*) AS reports, MIN(sequence) AS first, MAX(sequence) AS last FROM reports")
    .get();
  const userVersion = snapshot.prepare("PRAGMA user_version").get().user_version;

  console.log(JSON.stringify({ path: destinationPath, userVersion, ...reportStats }, null, 2));
} finally {
  snapshot.close();
}
