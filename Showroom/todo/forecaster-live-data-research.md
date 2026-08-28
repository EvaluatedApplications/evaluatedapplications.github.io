# The Forecaster — live multi-market data source research

Researched 2026-08-28. Goal: replace the single bundled 450-row static AAPL JSON
with continuous live data across multiple markets, switching to whichever market
is open right now. Constraint: Showroom is Blazor WebAssembly on GitHub Pages —
**no server**, so every fetch happens as plain browser `fetch()` from the
visitor's own tab, and any API key placed in the compiled wasm/JS is effectively
public and unrotatable.

All claims below were checked against live endpoints from this machine
(`Invoke-WebRequest` with an explicit `Origin: https://evaluatedapplications.github.io`
header, which is what a browser would send) on 2026-08-28, not just vendor docs.
A server sends the same CORS headers regardless of which HTTP client asked, so
this is a valid proxy for real browser behavior.

## 1. Yahoo Finance chart endpoint — still the best DATA, but CORS-BLOCKED for browser use

Endpoint: `https://query1.finance.yahoo.com/v8/finance/chart/{SYMBOL}?interval=1h&range=5d`
(also `query2.finance.yahoo.com`, same behavior, used for load balancing).

Verified live and working today, no API key, no auth, no crumb needed for this
specific endpoint (the `crumb`/cookie lockdown Yahoo added in 2024 applies to
`v7/finance/quote` and `quoteSummary`, not to `v8/finance/chart`):

- `AAPL` (NASDAQ) → 200 OK, live `regularMarketPrice`, `regularMarketTime` etc.
- `VOD.L` (LSE, London) → 200 OK, `"exchangeName":"LSE"`, `"currency":"GBp"`, `"exchangeTimezoneName":"Europe/London"`
- `7203.T` (Tokyo, Toyota) → 200 OK, `"exchangeName":"JPX"`, `"exchangeTimezoneName":"Asia/Tokyo"`
- `0700.HK` (Hong Kong, Tencent) → 200 OK, `"exchangeName":"HKG"`, `"exchangeTimezoneName":"Asia/Hong_Kong"`

So coverage is genuinely global (ticker + exchange suffix, e.g. `.L`, `.T`, `.HK`,
`.DE`, `.PA`) and the JSON payload already carries each market's own timezone
name and UTC offset (`gmtoffset`, `timezone`, `exchangeTimezoneName`) plus
`regularMarketTime` (unix seconds) — genuinely useful for driving "is this
market open" logic. Interval can go down to `1m`/`5m` for near-live granularity
(subject to Yahoo's own limits: 1m only for the last ~7 days, etc.).

**The catch, confirmed by direct header inspection:** the response carries
`vary: Origin` but **no `Access-Control-Allow-Origin` header at all** (checked
explicitly, not just eyeballed — `Access-Control-Allow-Origin` and
`Access-Control-Allow-Credentials` were both empty/absent on every request
above). That means:
- The HTTP request itself succeeds (no network-level block), which is why a
  **build-time** fetch (Node/PowerShell/C# during CI, not running in a
  visitor's browser) works fine — this is exactly why the current bundled
  450-row JSON works today.
- A real browser `fetch()` from `evaluatedapplications.github.io` at runtime
  will get the response over the wire but the browser will refuse to expose it
  to JS (`TypeError: Failed to fetch` / CORS console error, response is
  "opaque"). This matches widely-reported community experience (multiple
  GitHub issues, e.g. `pilwon/node-yahoo-finance#34`, `torreyleonard/algotrader#8`)
  — Yahoo has never added CORS headers to these endpoints; any project that
  "uses Yahoo Finance from the browser" is either running through a server/
  serverless proxy or through a third-party CORS proxy (see §6).

**Conclusion: Yahoo chart JSON is the best free/keyless/global data, but it is
architecturally incompatible with "no server, direct browser fetch" as stated.**
It can still be used for periodic **build-time refresh** (regenerate the bundled
JSON file on a schedule via GitHub Actions, e.g. hourly), which gets you "fresher
than once, per-build" data across all 4 target markets with zero key and zero
rate-limit risk — just not true continuous live-in-tab updates.

## 2. Genuinely CORS-open free APIs (verified via live header check)

| Provider | ACAO header seen live? | Needs key? | Free rate limit | Market coverage on FREE tier |
|---|---|---|---|---|
| **Finnhub** | `Access-Control-Allow-Origin: *` (confirmed even on a 401 error response, i.e. CORS is unconditional at the edge, not gated by valid auth) | Yes, free self-serve key (`finnhub.io/register`, instant, no card) | **60 calls/minute** (generous) | **Real-time is US-exchanges only** on the free tier per Finnhub's own docs and multiple independent reviews (IBKR Campus, TradingBrokers); non-US real-time/intraday quote data is a paid add-on. Company/reference data spans more exchanges, but the live `/quote` endpoint for LSE/TSE/HKEX tickers is **not confirmed working on free tier** — needs a real registered key to verify definitively (I could not create one from this sandboxed environment). |
| **Twelve Data** | `Access-Control-Allow-Origin: *` (confirmed live, `GET /quote?symbol=AAPL&apikey=demo` returned real live data, 200 OK) | Yes, free self-serve key | **8 credits/minute, 800/day** (quite low) | Free/Basic tier real-time is documented as **US equities + forex + crypto only**; international equities require a paid plan. Confirmed empirically: the public `demo` key works ONLY for `AAPL` (Twelve Data's fixed showcase symbol) — `MSFT` and any UK (`VOD`/`LSE`) or Japan (`7203`/`TSE`) symbol returned `401 Unauthorized` even with `demo`, so free-tier non-US coverage could not be confirmed and is unlikely given their own pricing page. |
| **CoinGecko** | `Access-Control-Allow-Origin: *` (confirmed, used here only as a sanity check that my CORS test methodology is valid) | No | Generous, ~10-30/min | Crypto only, not applicable to stocks — mentioned only in case "market" gets redefined to include crypto (which never closes, so it sidesteps the whole "which market is open" problem). |

Both Finnhub and Twelve Data genuinely support direct browser `fetch()` calls
(this is real, verified, not marketing copy) — that part of the original ask is
satisfiable. The problem is **coverage + rate limit**, not CORS:

- Neither confirms live UK/Japan/HK stock quotes on the zero-cost tier.
- Both keys, if baked into the public Showroom JS, are shared by every visitor
  worldwide simultaneously. Finnhub's 60/min is workable for light traffic;
  Twelve Data's 8/min would be exhausted by a single visitor cycling through
  3-4 markets on a short poll interval, let alone concurrent visitors.
- A key baked into public wasm/JS **will** eventually get scraped and abused by
  unrelated third parties (bots harvesting exposed keys from public repos is
  routine) — at that point the shared rate limit gets consumed by strangers,
  degrading or breaking the tool for real visitors, with no way to rotate the
  key without a rebuild+redeploy.

Checked and ruled out:

- **Alpha Vantage**: does NOT support CORS (client apps must proxy); free tier
  is only 25 requests/day, 5/min — too low regardless.
- **Financial Modeling Prep**: docs site blocks scripted access (403); the
  `apikey=demo` quote endpoint returned `401 Unauthorized` (demo key is
  restricted/expired for `/v3/quote`). Widely reported as CORS-unfriendly for
  direct browser use; not pursued further given the FMP-shaped answer (paid
  key, server-recommended) is a known pattern across their whole product.
- **Stooq** (the CSV endpoint used by many open-source finance projects,
  `stooq.com/q/l/` and `stooq.com/q/d/l/`): **as of ~2026-04-01 Stooq now
  requires an `apikey`** obtained by emailing `www@stooq.com` and describing
  your use case — no more anonymous/keyless access. All anonymous requests
  from this environment returned `404` (consistent with the lockdown, possibly
  combined with bot/JS-challenge detection — one plain request returned an
  "enable JavaScript to verify your browser" HTML challenge page instead of
  data). Previously a strong keyless multi-exchange option; **no longer viable
  without manual approval**, ruling it out for a self-serve/instant-deploy tool.
- **IEX Cloud**: permanently shut down (Aug 2024) — a common recommendation in
  older articles, dead now, don't chase it.
- **Alpaca Markets data API**: requires a key **and** secret pair (broker-style
  credentials, not a single publishable key), and is oriented at authenticated
  trading accounts — not designed to be embedded in public client-side code.
- **Google Finance**: no public JSON API; only an HTML page and the
  spreadsheet-only `GOOGLEFINANCE()` function, neither CORS-open nor
  fetch-friendly. TradingView's free embeddable ticker/chart **widgets**
  (`<script>` tag, not a data API) do cover LSE/TSE/HKEX and require no key,
  but they render inside their own sandboxed iframe — you cannot read numeric
  values back out of them via JS due to the same cross-origin restriction, so
  they're only useful as a visual "market is open" decoration, not as a data
  source to feed the training model.

## 3. Market hours (for "which market is open right now" logic)

DST is flagged as "exists, not solved here" per each entry — implement using
IANA timezone-aware conversion (e.g. `TimeZoneInfo` with `"Europe/London"`,
`"America/New_York"`, `"Asia/Tokyo"`, `"Asia/Hong_Kong"`, `"Europe/Berlin"` —
all of which conveniently come back directly in the Yahoo chart payload's
`exchangeTimezoneName` field, so you don't have to hardcode them).

| Market | Local hours | Lunch break? | UTC (standard time) | UTC (that region's DST/summer time) | DST note |
|---|---|---|---|---|---|
| NYSE / NASDAQ (US) | 09:30–16:00 ET | No | 14:30–21:00 UTC (EST, Nov–Mar) | 13:30–20:00 UTC (EDT, Mar–Nov) | US DST dates differ from EU's, so the UTC open/close time shifts by an hour on different calendar days than European markets |
| LSE (London, UK) | 08:00–16:30 GMT/BST | No | 08:00–16:30 UTC (GMT, winter) | 07:00–15:30 UTC (BST, summer) | UK/EU DST switch dates differ from US |
| Deutsche Börse / Xetra (Frankfurt) | 09:00–17:30 CET/CEST | No | 08:00–16:30 UTC (CET, winter) | 07:00–15:30 UTC (CEST, summer) | Same EU DST calendar as London, offset differs (CET is UTC+1, GMT is UTC+0) |
| TSE (Tokyo, Japan) | 09:00–11:30 and 12:30–15:30 JST | **Yes**, 11:30–12:30 JST | 00:00–02:30 and 03:30–06:30 UTC | (no change — Japan does not observe DST) | Afternoon session was extended from 15:00 to 15:30 JST in Nov 2024 — if any reference material still says "15:00 close," it's stale |
| HKEX (Hong Kong) | 09:30–12:00 and 13:00–16:00 HKT | **Yes**, 12:00–13:00 HKT | 01:30–04:00 and 05:00–08:00 UTC | (no change — Hong Kong does not observe DST) | — |

Practically: at almost any given UTC hour, at least one of {NYSE, LSE, TSE,
HKEX} is open, which is exactly the "always something live" property the tool
wants. Tokyo/HK's lunch gap and the US/EU DST mismatch are the two wrinkles an
implementation needs to handle (compute "is open" per-market independently in
its own IANA zone, don't assume a shared UTC session block).

## 4. Rate limits / bursty public traffic risk — summary

| Source | Real limit | Survives bursty public traffic from many simultaneous browser tabs? |
|---|---|---|
| Yahoo chart JSON (build-time only) | Unofficial/unpublished, empirically loose for occasional single-origin build fetches, but **not usable from the browser at all** (no CORS) so visitor traffic never reaches it directly — a build-time cron pull is a single request, trivially safe | N/A — not a runtime dependency once it's build-time only |
| Finnhub free key | 60/min, shared globally across every visitor+key-scraper | Workable at low-to-moderate traffic; a moderately popular showcase page could exhaust it, especially once the key gets scraped from the public bundle |
| Twelve Data free key | 8/min, 800/day, shared globally | Not workable even at modest traffic — a handful of visitors polling a couple of markets each will blow the daily cap within minutes |
| Public CORS proxies (allorigins.win, etc., wrapping Yahoo) | ~20/min, no SLA, volunteer-run (see §6) | No — explicitly not for production, will rate-limit or vanish |

## 5. Practical recommendation

**There is no clean, fully-free, single-API, direct-browser, multi-market live
option today.** The honest tradeoff space is:

**Recommended: hybrid, matches the "no server" constraint without lying to
visitors about "live":**
1. **Build-time refresh via GitHub Actions**, expanded from "once, forever" to
   "on a schedule" (e.g. every 15–60 min via `workflow_dispatch`/`schedule`
   cron) — keep using the Yahoo `v8/finance/chart` endpoint exactly as today
   (proven reliable, zero key, genuinely covers `AAPL`/`VOD.L`/`7203.T`/
   `0700.HK` and more), but pull **one bundle per target market** (US, UK,
   Tokyo, HK, optionally a Frankfurt/Xetra name) each run, with `interval=1m`
   or `5m` and enough `range` to give the model a decent recent window. Commit
   the refreshed JSON files to the repo (or publish as a GitHub Pages asset)
   so each Pages deploy — or each scheduled workflow run — ships fresher data.
   The client picks whichever bundle corresponds to the market that's
   currently open (per §3 logic) and trains on that. This is honestly "data
   refreshed every N minutes by the build," not true tick-by-tick live, but it
   is the only zero-key, unlimited-coverage, un-rate-limitable option, and it's
   a straightforward, low-risk extension of what's already shipping.
2. **Layer in Finnhub as an optional true-live top-up for the US session
   only** (where its free tier is solid: 60/min, real-time, genuine browser
   CORS): when NYSE/NASDAQ is the open market, let the page make a light,
   throttled client-side `fetch()` (e.g. one call every 30–60s, well under the
   60/min ceiling even with several tabs open) to Finnhub's `/quote` endpoint
   using a free, embedded, expect-it-to-get-scraped-eventually key, layered on
   top of the build-time US bundle as the freshest tip of the series. Treat
   the key as disposable: if/when it gets abused, swap it in a redeploy — no
   worse than the status quo, and it degrades gracefully (falls back to the
   build-time bundle) rather than breaking. Do **not** rely on Finnhub or
   Twelve Data for the non-US markets — free-tier coverage there is unverified
   at best, documented-as-unavailable at worst.
3. Treat every public CORS-proxy-in-front-of-Yahoo approach (allorigins.win
   etc.) as explicitly **out of scope for production** — verified working
   today (200 OK, `ACAO: *`), but it's a volunteer-run third party with no SLA,
   a ~20/min limit, and a track record of free CORS proxies disappearing;
   fine for local dev/testing, not something to point a public showcase tool
   at.

If "continuous live" is a hard requirement rather than "refreshed
periodically," the only way to get it honestly within the stated constraints
(no server, no paid key) is option 2 (Finnhub, US-hours only, modest visitor
load) layered on top of option 1 for everything else — there is no vendor found
in this research that offers free, keyless-or-cheap, CORS-open, multi-market,
truly-real-time data that would survive unpredictable public traffic.
