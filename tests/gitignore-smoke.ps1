$ErrorActionPreference = 'Stop'
if (-not $env:BOY_SCOUT_VERIFY_SESSION) {
    throw 'Run through scripts/invoke-verify.ps1.'
}

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ignored = @(
    'node_modules/dependency/index.js'
    'src/Plugins/Example/node_modules/dependency/index.js'
    'artifacts/public-source-audit/EXCLUSIONS.md'
    'src/Plugins/Example/bin/Release/Example.dll'
    'src/Plugins/Example/obj/project.assets.json'
    'src/TodoGraph.WebApp/dist/main.js'
    'src/TodoGraph.WebApp/.angular/cache/output'
    'src/BoyScout.Api/wwwroot/main.js'
    'src/TodoGraph.Api/wwwroot/main.js'
    '.agent-work/tasks/local-task/index.md'
    '.local/session.json'
    'data/todo-graph.db'
    'logs/api.txt'
    'backups/snapshot.json'
    '.todograph/runtime/stands/example/stand.json'
    '.todograph/data/session.json'
    '.todograph/auth/state.json'
    '.todograph/private-config.json'
    '.todograph/launch/preparation.json'
    'todo-graph.db'
    'todo-graph.db-wal'
    'todo-graph.db-shm'
    'todo-graph.db-journal'
    'reports.sqlite'
    'reports.sqlite-wal'
    'reports.sqlite3'
    'reports.sqlite3-shm'
    '.env'
    '.env.local'
    '.env.production'
    '.env.example.local'
    'src/Plugins/Example/.env.local'
    'secrets/service-account.json'
    '.secrets/credentials.json'
    'certificates/client.pfx'
    'certificates/client.p12'
    'certificates/client.key'
    'certificates/client-private.pem'
    'certificates/client.private.pem'
    'src/TodoGraph.Api/appsettings.Local.json'
    'src/TodoGraph.Api/appsettings.Development.local.json'
    'TestResults/results.trx'
    'coverage/index.html'
    'test-results/run/result.json'
    'playwright-report/index.html'
    'blob-report/report.zip'
    'playwright/.auth/user.json'
    'src/TodoGraph.WebApp/playwright/.auth/user.json'
    'logs/api.log'
    'todo-graph.db.bak'
    'tools/example/__pycache__/script.pyc'
    '.vs/workspace/settings.json'
    'src/TodoGraph.Api/TodoGraph.Api.csproj.user'
    '.DS_Store'
    'Thumbs.db'
)
$visible = @(
    '.gitignore'
    'AGENTS.md'
    'Directory.Build.props'
    'BoyScout.sln'
    'package-lock.json'
    'src/TodoGraph.WebApp/package-lock.json'
    'opencode.jsonc'
    '.opencode/skills/example/SKILL.md'
    '.env.example'
    '.env.sample'
    '.env.template'
    '.env.production.example'
    'src/Plugins/Example/.env.example'
    'src/TodoGraph.Api/appsettings.json'
    'src/TodoGraph.Api/appsettings.Development.json'
    'src/TodoGraph.Api/Feedback/Recovery/RepairSentryFacts.cs'
    'src/TodoGraph.Infrastructure/Persistence/Templates/default-development.json'
    'src/TodoGraph.Infrastructure/Persistence/Migrations/Example.cs'
    'src/Plugins/Example/plugin.json'
    'src/Plugins/Example/Plugin.cs'
    'src/Plugins/Example/Web/main.mjs'
    'src/Plugins/Optional/Example/plugin.json'
    '.todograph/plugins/example/plugin.json'
    '.todograph/plugins/example/Plugin.cs'
    '.todograph/launch/versions/example/service-1.ps1'
    '.todograph/launch/profile.json'
    'src/ui/_typography.scss'
    'tests/Fixtures/reply-image.png.base64'
    'tests/Fixtures/configuration.json'
    'tests/Fixtures/public-certificate.pem'
    'tests/ExampleSmoke/Program.cs'
    'tests/ExampleSmoke/browser.mjs'
    'tests/gitignore-smoke.ps1'
    'tools/Example/Program.cs'
    'docs/example.md'
    'src/data/example.json'
    'src/Plugins/Host/licenses/Example-LICENSE.txt'
    'src/TodoGraph.WebApp/public/fonts/LICENSE-material-symbols.txt'
    'LICENSE'
)

Push-Location $repository
try {
    # --no-index also checks tracked paths without changing files or the index.
    # Disable personal/global excludes so this tests the repository policy itself.
    # Windows PowerShell pipes use CRLF; Git --stdin treats CR as part of a path.
    # Pass the bounded list as native arguments instead, preserving exact paths.
    $checkedPaths = $ignored + $visible
    $ignoredPaths = @(git -c core.excludesFile=NUL check-ignore --no-index -- $checkedPaths)
    if ($LASTEXITCODE -ne 0) {
        throw "git check-ignore failed: $LASTEXITCODE"
    }
    foreach ($path in $ignored) {
        if ($path -cnotin $ignoredPaths) { throw "Local/generated path is not ignored: $path" }
    }
    foreach ($path in $visible) {
        if ($path -cin $ignoredPaths) { throw "Distributable source is incorrectly ignored: $path" }
    }
    Write-Output "PASS: $($ignored.Count) local/generated paths ignored; $($visible.Count) source/example paths remain visible. No files or Git index entries changed."
}
finally {
    Pop-Location
}
