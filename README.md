# NT8 Kat 34 Scalper — EMA 34/89 Rejection Signal Indicator

**Current Version**: `v0.95` (Released: `2026-08-08`)

Signal indicator for **NinjaTrader 8 (NT8)**: draws Sell/Buy signals on the chart with entry, SL and TP dash lines. Appears under the **KAT** folder when adding to a chart.

## Module structure (partial classes)

| File | Module | Owns |
|---|---|---|
| `Kat34Scalper.cs` | **Main** | lifecycle (`OnStateChange`), settings (NinjaScript properties), per-bar orchestration |
| `src/Kat34ScalperLogic.cs` | **Pure logic** | signal state machines + filter math + ATM parser — zero NT8 deps, xunit-tested |
| `src/Kat34Scalper.AlertSignal.cs` | **Alert Signal (shared)** | shared alert backfill helpers |
| `../nt8-kat-A1-TradeBackground/Kat34Scalper.AlertSignal.A1.cs` | **Alert Signal A1** | independent sibling repo (no submodule): EmaZone30s fan, zone gate, background bands, drawings, sound |
| `src/Kat34Scalper.AlertSignal.A2.cs` | **Alert Signal A2** | independent alert sub-module (placeholder template: chart drawings & alert sounds only) |
| `src/Kat34Scalper.Signal.cs` | **Bot Signal (shared)** | backfill window helper, shared diagnostics |
| `src/Kat34Scalper.Signal.B1.cs` | **Bot Signal B1** | independent bot signal sub-module: 34bounce8+ (`B1 (34bounce8+)` — own toggle, settings group, drawings, bot order execution) |
| `src/Kat34Scalper.Signal.B2.cs` | **Bot Signal B2** | independent bot signal sub-module: 89uturn34 (`B2 (89uturn34)` — own toggle, settings group, drawings, bot order execution) |
| `src/Kat34Scalper.Filter.cs` | **Filter** | two independent sides — BOT FILTER (MTF/ADX/Volume/Time/ER/CI → B1+B2) and ALERT FILTER (ADX/ER/CI + A1-only ADX rising & ADX MTF → A1+A2), per-bar `*At(barsAgo)` variants for backfill replay |
| `src/Kat34Scalper.Bot.cs` | **Bot** | signal → order conversion (stop on valid side, limit when price ran past), ATM brackets, migration, trend-flip cancel, Close/Flatten |
| `src/Kat34Scalper.Draw.cs` | **Draw** | entry/SL/TP + ATM trigger lines, labels, version label, alert sound, HUD (sections titled ALERT SIGNAL / BOT SIGNAL / ALERT FILTER / BOT FILTER / BOT / DRAW) |
| `../nt8-kat-StackEMA/nt8-kat-StackEMA.cs` | **Standalone StackEMA** | independent sibling repo (no submodule): `nt8-kat-StackEMA` indicator - five configurable timeframe packs, EMA 8/21/34/55/89, top-left status HUD |
| `../nt8-kat-StackEMA/StackEmaLogic.cs` | **StackEMA pure logic** | independent sibling repo: Positive/Negative/Neutral direction, closed-bar MTF mapping, Scalper filter rule |
| `src/Kat34Scalper.StackEMA.cs` | **StackEMA filter adapter** | mapped secondary series (reuses A1/zone series when periods match) and directional bot filter |

Every signal sub-module is **independent and default OFF**; its stages are specified in **`docs/SIGNALS.md`** (the standard every new signal must follow). Per bar the pipeline runs: **Signal A0** (direction/marker) → **Filter** (A1-only gates) → **Signal A1** → fires **Draw** + **Bot**.

## Signals

**Every signal is an independent sub-module, default OFF.** Switching one ON (settings or the HUD SIGNAL toggle) immediately computes it and draws it on the chart over its **`History Days`** window (default 3 days) — a one-shot backfill with no alert sounds and no bot orders; afterwards the live state machine continues seamlessly. Switching OFF removes only that module's own drawings. Full per-stage spec: **`docs/SIGNALS.md`**.

### 1. Signal A0 (EMA fan)
- **A0 fan signal**: EMAs 9/21/34/55/89/144/200 strictly ordered **and** spreading (EMA9↔EMA200 wider than `Fan Spread Lookback` bars ago, at least `Fan Min Spread (ticks)`). First bar of a fan episode draws a small triangle (buy blue below / sell orange above) and plays the `Alert Sound` when the SIGNAL `A0 fan` toggle is ON (default OFF). Re-arms when the fan collapses. Stages: `idle → fanned` (docs/SIGNALS.md).

### 2. Filters (A1-only gates)
- **A0 Fan Filter**: when enabled, A1 requires the A0 ribbon direction; it never controls A0 marker/alert rendering. OFF by default; **session-only** (not serialized) — it boots OFF on every load, so stale chart templates can no longer resurrect it ON.
- **MTF**: optional 3m / 5m / 15m ribbons must fan in the same direction (per-TF ON/OFF in settings). A secondary data series is added **only** for enabled timeframes — with all MTF off (default) the chart keeps its single primary series and every other chart indicator (your EMAs) is completely untouched.
- **Market**: ADX ≥ `ADX Min` (blocks sideways) and bar volume ≥ `Volume Min (x SMA)` × volume SMA (blocks dead bars).
- **Time window**: `HH:mm` machine-local start/end; overnight wraps midnight; equal start/end disables.
- **Every filter gate is OFF by default** (A0 Fan Filter, MTF, ADX, Volume, Time, and HUD toggles) — A1 fires on trend alone out of the box. Enable gates one by one as needed.
- A1 (Sell/Buy) setup state progresses while 34/89 trend remains valid; A0 fan and other enabled gates decide whether a completed trigger is emitted.
### A1 Signal (shared by Sell and Buy — mirrored mechanism)
- **Context**: Fast EMA vs Slow EMA — Sell: fast below slow (downtrend); Buy: fast above slow.
- **Sequence** (every step on close basis, wicks don't cross):
  1. **Armed**: price closes beyond the fast EMA (Sell: below / Buy: above).
  2. **Pullback**: price crosses back through the fast EMA toward the slow EMA — the sequence clock starts.
  3. **Touch**: price touches or crosses the slow EMA.
  4. **U-turn**: price reverses and closes back through the fast EMA (with-trend again).
  - The whole sequence must complete within **`Max Sequence Bars`** (default 30, counted from the cross bar) or the setup expires and rearms. A pullback that reverses before ever touching the slow EMA simply rearms.
- **Phase milestone markers**: at every A1 phase transition a persistent label is drawn at that bar so the setup progression is visible across chart history — `A1-arm` (armed beyond ema34), `A1-pull` (crossed back through ema34), `A1-pull-T` (pullback touched ema89). Buy markers below the low, sell markers above the high. Drawn while A1 is ON (default OFF; `History Days` backfill covers history); cleared by the HUD Clear button or by switching A1 OFF.
- **Trigger**: fires immediately on the U-turn close — 4-step sequence (arm → cross → touch → U-turn). (The Retest Bounce mode was removed in v0.37.)
- **Entry (A1)**: the entry sits at the U-turn bar's extreme — **C1** = its low (sell) / high (buy) — with `Entry Offset` ticks (sell below, buy above).
- **Drawing (KatTradeManager style)**: sell entry line **solid red**, buy entry line **solid lime green** (both with `Entry Offset` ticks); SL dashed red, TP dashed green — taken from the selected **ATM template** when it defines StopLoss/Target (settings `Stop/Target Distance` are the fallback); ATM **trailing-SL trigger lines** when the template defines them — **BE** DeepSkyBlue dash-dot, **SL1** orange dot, **SL2** magenta dot (1 px, profit side of entry); lines use supported historical-to-current anchors and remain visible for up to `Line Length` bars; one deterministic per-side arrow uses the entry color at the signal candle; optional BUY/SELL label at the candle (default off, toggled from the HUD).

### A2 Signal (34+8+Bounce — shared by Sell and Buy, mirrored)
- **Context (trend stack)**: BUY 34+++ = EMA 8 above/touching EMA 34 (no cross down) + EMA 34 > EMA 89 > EMA 144 > EMA 200 — **each condition individually toggleable** in settings. SELL mirrors. Trend loss cancels the pending entry.
- **Setup**: price runs above EMA 34, pulls back and **touches** it (wick low ≤ EMA 34) while **closing above** → pending stop LONG at the touch candle's **high (wick included)** + `Entry Offset`. A later touch candle with a **lower high migrates the entry down** (a higher high means the stop already filled). A **close below EMA 34** cancels the entry — a touch candle must close above EMA 34 to place the pending stop Buy at all.
- **No stage markers** (single-phase setup): drawings are the entry/SL/TP lines plus a `Buy A2` label below the entry candle (Buy Text Color) / `Sell A2` above (Sell Text Color). Lines stay rendered while the entry is pending (`KeepAlive` — no `Line Length` fade); cancel removes lines + label; fill lets them fade per `Line Length`.
- **Filters**: none yet — A2 computes on the chart's own timeframe only; higher-TF filters will plug into the Filter section later.

### A3 Signal (8cross34 — shared by Sell and Buy)
- **Trigger**: EMA 8 crosses **up** through EMA 34 → **BUY**; crosses **down** → **SELL**. Cross = previous bar on one side, current bar on the other (an exact touch on the previous bar counts as the old side).
- **Stateless**: single-bar event — no sequence, no stage markers, no filters. EMA 34 is the fixed 34 from the ribbon (independent of A1's Fast EMA Period).
- **Entry**: stop at the cross candle's extreme — buy = high + `Entry Offset`, sell = low − `Entry Offset`. An opposite cross cancels A3's own pending bot entry.

### A4 Signal (OCO Prev Bar — shared by Sell and Buy)
- **Trigger**: Always creates a BUY signal at previous bar High (`Highs[0][1] + Entry Offset`) and a SELL signal at previous bar Low (`Lows[0][1] - Entry Offset`).
- **OCO Pair**: Submits BUY and SELL entry orders simultaneously as an OCO (One-Cancels-Other) order pair when BOT is ON. When one order fills, the remaining active order is automatically cancelled.
- **Stop-to-Limit Conversion**: Converts pending StopMarket orders to Limit orders if market price has already run past the trigger price.
- **Level Prioritization**: Maximum 1 BUY and 1 SELL signal simultaneously. Always prioritizes the **LOWEST** BUY level and the **HIGHEST** SELL level.


## Bot (semi-auto)
- Trades **only** while the HUD **BOT: ON** button is active (off by default) *and* `Bot Enabled` is set — never runs on its own. Switching BOT OFF cancels the pending entry immediately.
- **Respects the signal toggles**: BOT trades **every signal switched ON** (A1/A2/A3) on the chosen account + ATM, and **never** a signal that is OFF. Switching a signal OFF also **cancels its pending bot order** (and blocks any migration re-place), so an OFF signal can neither open a new entry nor fill a stale one. One bot order at a time.
- On an A1 signal it submits a stop order (sell stop below the better candidate low / buy stop above the better candidate high) through the selected **ATM template** on the selected **account**; `None` or a missing template falls back to a bare stop order. If price has **already run past the entry**, the order is submitted as a **limit** instead (a stop on the wrong side of the market would be rejected) — same rule as KatTradeManager.
- **Migration**: while the entry is still working, a newer bar closing on the setup side of the fast EMA with a better extreme (sell: higher low / buy: lower high) cancels the order and re-places it at the better price once the cancel settles. A 34/89 trend flip cancels the pending entry. One bot order at a time; once filled, the ATM owns the brackets.

## Settings (6 sections)
|| Section | Settings |
||---|---|
|| 1. Filters | A0 Fan Filter Enabled (**session-only**, boots OFF), Fan Min Spread (20 ticks), Fan Spread Lookback (5 bars), Use 3m/5m/15m Fan (off), ADX Period (14), ADX Min (20), Volume SMA Period (20), Volume Min x SMA (1.0), Time Start/End (08:00–17:00), StackEMA filter + EMA 8/21/34/55/89 + five timeframes (30s/1m/3m/5m/15m) + five visible-pack toggles, Alert Sound (dropdown of NT8 .wav files) |
|| 2. Signal A0 — EMA Fan | Enabled (**default OFF**), History Days (3 — ON backfills + draws this window) |
|| 3. Signal A1 — 89/34 Pullback | Enabled (**default OFF**), History Days (3), Fast EMA Period (34), Slow EMA Period (89), Max Sequence Bars (30), Entry Offset (1 tick), Stop Distance (60, ATM fallback), Target Distance (120, ATM fallback) |
|| 3.5 Signal A2 — 34+8+Bounce | Enabled (**default OFF**), History Days (3), Cond toggles: EMA 8 above EMA 34 / EMA 34 above EMA 89 / EMA 89 above EMA 144 / EMA 144 above EMA 200 (all on), Entry Offset (1 tick), Stop Distance (60, ATM fallback), Target Distance (120, ATM fallback) |
|| 3.6 Signal A3 — 8cross34 | Enabled (**default OFF**), History Days (3), Entry Offset (1 tick), Stop Distance (60, ATM fallback), Target Distance (120, ATM fallback) |
|| 4. Lines & Text | Line Length (7 bars), Line Width (2 px), Arrow Offset (3 ticks), Sell/Buy Entry Line Colors (solid), SL/TP Line Colors, Sell/Buy Text Colors, Show Arrows, Show Buy/Sell Labels (default off) |
|| 5. Bot | Bot Enabled (off), Order Quantity (1), ATM Template (default `mnq. 1ct. 15-be20-35move15-50triggertrail5step1` — its SL 60 / TP 120 / BE / trail levels drive the signal lines; dropdown of NT8 ATM templates + None), Account Name (default **Sim101**) |
|| 6. ATM Quick Sets | 6 quick-set buttons under the HUD ATM dropdown: Set 1–6 Name (button label, max 3 chars, defaults A–F) + Set 1–6 ATM (assigned template, default none). Click selects the assigned ATM immediately (amber = the current selection). |

Parameters group: `Show Version Label` — draws `Kat34Scalper vX.XX (date) [chart timeframe]` top-left on the chart (updates on every F5 recompile). All signal math runs on the primary series of the chart the indicator is added to — the label proves which timeframe that is (e.g. `[30 Second]`).

## HUD
TradeManager-style panel (same colors, sizes and structure): dark navy card `Argb(240,20,24,33)` on a draggable canvas (drag anywhere outside the buttons, clamped so it can't leave the chart), `⚡ KAT 34 SCALPER vX.XX` steel-blue header, and a status line (5 s auto-clear) that mirrors bot events — submits, migrations, cancels, fills. Each section carries a **module title** naming the module it controls:
- **SIGNAL**: `A0 fan` + `A1 89-34` + `A2 34+8` + `A3 8x34` independent sub-module toggles (all default OFF) — ON backfills + draws the module's `History Days` window immediately, OFF removes only that module's drawings.
- **FILTER**: `A0 Fan | MTF`, `ADX | Volume`, `Time window` toggles — blue ON / gray OFF, effective from the next bar. All OFF by default (the A0 Fan gate boots OFF every load — session-only).
- **BOT**: `Acc:` row (account dropdown), ATM template dropdown (sorted, `None` = bare stop order), 6 ATM quick-set buttons (labels + assigned ATM from settings group 6; amber = current selection, click selects it), `⚡ BOT: ON/OFF` (default OFF; OFF cancels the pending entry immediately).
- **DRAW**: `Arrow | Text` drawing toggles, dark `Clear` button.

## Installation in NinjaTrader 8

1. Open **NinjaTrader 8**.
2. Go to **Tools** -> **NinjaScript Editor**.
3. Open or import `Kat34Scalper.cs` under `Indicators`.
4. Press **F5** to Compile (chart indicators auto-reload with the new version label).
5. Add `Kat34Scalper` to any NT8 Chart.

`KAT-StackEMA` is installed from the same source sync and can be added independently to a chart. Its standalone settings are separate from `Kat34Scalper`; configure the host's matching five pack settings when using the StackEMA filter. Visible host packs select filter participation; with all five hidden, that filter bypasses.

When `StackEMA Filter Enabled` is OFF, Scalper does not add StackEMA secondary series. When ON, matching A1/zone `Second` timeframes are reused; only unique StackEMA `Second` timeframes are added. The ADX `Minute` series is never reused. This preserves A1's BIP 1 path.

## Development workflow
- `pwsh scripts/Run-AllChecks.ps1` — xunit suite + net48 compile gate.
- `pwsh scripts/Deploy-NT8.ps1` — copies sources into NT8 + verifies auto-recompile.
- `nt8-kat-A1-TradeBackground` and `nt8-kat-StackEMA` — INDEPENDENT Git repositories. They are NOT submodules; they sit as sibling folders next to this repo, and the compile gate / deploy script reference their canonical sources by relative path.
- `pwsh scripts/connect-Repos.ps1` — after a fresh clone, verifies (and reports) the sibling A1 + StackEMA repos so compile/deploy can find them.
- Version bump, diary, graphify and GitHub sync per `AGENTS.md` / `RULES.md`.

## License

MIT
