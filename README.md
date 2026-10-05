# sol-bot-sentinel

An independent, read-only sentinel for the Sol Bot auto-traders, written in C# on ASP.NET Core. It watches the bots from outside and raises an alert when a position crosses a risk line, a bot steps outside its limits, or the trend flips and a bot does not act. It cannot place, change, or close a trade.

For where this sits in the whole system, read [how the security is layered](_docs/DOC__sol_bot_sentinel--architecture--security-layers.md).

Proof of concept, built Oct 05 2026 with Claude Code agents. It runs against a scripted market today. Reading a live system is a matter of two settings, `SourceUrl` and `TrendUrl`; that path is written and has not been run against a live feed.

## The Three Watchers

Each watcher is one class and one hosted background service in the same process. A new watcher is one more class and one line in `Program.cs`.

- **Trend watcher** -- the double-check on the trend flip. When the trend flips, the bot for the old trend must close and the bot for the new trend must open:
  - `flip-not-closed`: the old-trend bot is still open after the grace window.
  - `flip-not-opened`: the new-trend bot is still idle after the grace window.
- **Risk watcher** -- three exit checks on an open position:
  - `price-stop`: price at or under `entry * (1 - stopLossPct / leverage)`.
  - `ltv-guard`: debt over collateral value at or over the threshold, ahead of the lender's own liquidation line.
  - `borrow-rate`: net carry under the floor for a full dwell window, so a brief rate spike does not trip it.
- **Conduct watcher** -- that the bot is behaving:
  - `cap-exceeded`: a position opened over the per-position cap.
  - `leverage-exceeded`: leverage over the allowed maximum.
  - `stale-heartbeat`: the bot has stopped reporting.

The service also watches its own watchers: `/health` reports `degraded` when any one of them has stopped polling.

An alert is raised once, at the first crossing, and not again until the condition clears. A read error never stops a watcher: it logs and tries again on the next tick.

## Run It

```bash
dotnet run --project src/SolBotSentinel
```

Or in a container:

```bash
docker build -t sol-bot-sentinel:1 .
docker run --rm -p 8080:8080 --name sol-bot-sentinel-1 sol-bot-sentinel:1
```

```bash
curl http://localhost:5037/health
curl http://localhost:5037/alerts
curl http://localhost:5037/alerts/history
```

The scripted market runs about a minute. One bot's price walks down to its stop and the risk watcher raises `price-stop`. Then the trend flips: the second bot opens over the cap and the conduct watcher raises `cap-exceeded`; the first bot never closes, and once the grace window is over the trend watcher raises `flip-not-closed`.

## Test It

```bash
dotnet test
```

Twenty-three tests: the rule arithmetic at each boundary, the dwell window, the flip grace window, the raise-once store, the health of the watchers, and the running service read over HTTP.

## Configure It

Every threshold is in the `Watch` section of `appsettings.json` and can be overridden by environment variable, for example `Watch__PollMs=500`. No address, key, or URL is written in source.

| Setting | Default | Meaning |
|---------|---------|---------|
| `Source` | `simulated` | `simulated`, or `http` to read live |
| `SourceUrl` | empty | where the `http` source reads a JSON array of positions |
| `TrendUrl` | empty | where the `http` source reads the trend signal |
| `PollMs` | `2000` | how often the watchers read |
| `StopLossPct` | `0.10` | share of the deposit that may be lost before the price stop |
| `LtvTripThreshold` | `0.80` | loan-to-value that trips the guard |
| `CarryFloorApr` | `0` | net carry under this starts the dwell clock |
| `CarryDwellMs` | `1200000` | how long carry must stay under the floor |
| `PositionCapUsd` | `100` | largest deposit a bot may open with |
| `MaxLeverage` | `3.0` | highest leverage a bot may use |
| `HeartbeatStaleMs` | `60000` | age at which a reading counts as stale |
| `FlipGraceMs` | `30000` | how long a bot has to act on a trend flip |

## Layout

```txt
src/SolBotSentinel/
  Program.cs            wiring and the three endpoints
  Domain/Models.cs      snapshot, trend reading, alert, options
  Rules/                the checks, as pure functions
  Sources/              where readings come from
  Watchers/             the three background services
  AlertStore.cs         raise-once alert memory
  Health.cs             the check on the watchers themselves
tests/SolBotSentinel.Tests/
_docs/                  the architecture
Dockerfile
.github/workflows/ci.yml
```
