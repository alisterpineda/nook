# Nook web app

The Angular client for Nook. It is a client-rendered app that talks to the API under `/api` on its own origin.

## Development server

Start the web app from the Aspire AppHost, which runs Postgres, the API and this dev server together:

```bash
cd ../Nook.AppHost
aspire run
```

Then open `http://localhost:4200/`. The app reloads when a source file changes.

`npm start` and `ng serve` do not work on their own: the dev server proxies `/api` to the address the AppHost passes in `services__api__http__0`, and stops with an error when that variable is not set.

## API types

`src/app/api/schema.d.ts` is generated from `../Nook.Api/openapi.json`, which the API build writes. `npm start` and `npm run build` regenerate it first. To regenerate it by hand:

```bash
npm run generate:api
```

## Building

```bash
npm run build
```

The build output goes to `dist/`. Publishing the API (`dotnet publish` of `../Nook.Api`) runs this build and puts the result in the output's `wwwroot`.

## Tests, lint and formatting

```bash
npm test -- --watch=false
npm run lint
npm run format:check
```

Unit tests run on [Vitest](https://vitest.dev/). `npm run format` rewrites files to the Prettier style.

## Spartan components

The Spartan helm components live in `libs/spartan` and are imported as `@spartan-ng/helm/<name>`. Add one with:

```bash
npx ng g @spartan-ng/cli:ui <name>
```

ESLint and Prettier skip that folder.
