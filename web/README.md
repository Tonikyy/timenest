# TimeNest web

Angular discovery interface using the original TimeNest SVG and visual brand.

## Run

Start SQL Server and the API using [local development](../docs/local-development.md), then:

```sh
npm ci
npm start
```

Open http://localhost:4200. The development proxy forwards /api to http://localhost:5080.

## Verify

```sh
npm run build
npm test
npm run format:check
```

Tests cover Helsinki date/DST boundaries and search request/error behavior. The UI loads categories and appointments from the API; it never substitutes local sample data on errors.

## Implemented

Nearby search from preset areas or browser geolocation, category/date/radius filters, paginated results, appointment details, loading/error/empty states, responsive Finnish interface.

Bookings and payments are not enabled in this first slice. Fictional demo data is explicitly marked. The logo is copied unchanged from tnest-website/public/images/logo.svg. Original font families are self-hosted via Fontsource.

## Build output

npm run build writes dist/web/browser. The Azure milestone will publish this with the ASP.NET Core application on the same origin.
