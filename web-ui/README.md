# Boy Scout web UI

Russian Angular workspace for raw engineering notes, AI proposals, and confirmed implementation tasks.

## Development server

Start `BoyScout.Api` on port 5187 from the repository root, then run:

```bash
npm start
```

Open `http://localhost:4200/`. Requests under `/api` are proxied to the local ASP.NET Core API.

## Production build

```bash
npm run build
```

The production build is written directly to `../src/BoyScout.Api/wwwroot`. Prefer `npm run publish:ui` from the repository root when preparing the runnable application.
