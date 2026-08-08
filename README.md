# NT8 Kat 34 Scalper — EMA 34/89 Rejection Signal Indicator

**Current Version**: `v0.96` (Released: `2026-08-08`)

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
| `src/Kat34Scalper.Filter.cs` | **Filter** | BOT gates (ADX rising, ADX MTF, ER, CI, Volume, Time, StackEMA → B1+B2+A2), per-bar `*At(barsAgo)` for backfill replay (A1 is pure, no filter) |
| `src/Kat34Scalper.Bot.cs` | **Bot** | signal → order conversion (stop/limit), ATM brackets, migration, trend-flip cancel, Close/Flatten |
| `src/Kat34Scalper.Bot.Risk.cs` | **Bot.Risk** | Daily MaxDD/MaxProfit session baseline + breach gate (NY 18:00 session) |
| `src/Kat34Scalper.Bot.AtmMerge.cs` | **Bot.AtmMerge** | ATM bracket MERGE reconciliation (anchor resize, duplicate/stale cancel, flat cleanup grace) |
| `src/Kat34Scalper.Draw.cs` | **Draw** | entry/SL/TP + ATM trigger lines, labels, alert sound, HUD assembly |
| `src/Kat34Scalper.Draw.HudFactory.cs` | **Draw.HudFactory** | TradeManager pixel-perfect factory: buttons, grids, cards, templates (HudGap 2, width 250→238) |
| `../nt8-kat-StackEMA/nt8-kat-StackEMA.cs` | **Standalone StackEMA** | independent sibling repo (no submodule): `nt8-kat-StackEMA` indicator - five configurable timeframe packs, EMA 8/21/34/55/89, top-left status HUD |
| `../nt8-kat-StackEMA/StackEmaLogic.cs` | **StackEMA pure logic** | independent sibling repo: Positive/Negative/Neutral direction, closed-bar MTF mapping, Scalper filter rule |
| `src/Kat34Scalper.StackEMA.cs` | **StackEMA filter adapter** | mapped secondary series (reuses ADX MTF series when periods match) and directional bot filter |

Every signal sub-module is **independent and default OFF**; its stages are specified in **`docs/SIGNALS.md`** (the standard every new signal must follow). Per bar the pipeline runs: **Filter** (`PassFiltersAt` → BOT gates) → **Alert A2** (placeholder) + **Bot B1** + **Bot B2** → **Bot** (one order) → **Draw**.

## Signals

**Every signal is an independent sub-module, default OFF.** Switching one ON (settings or the HUD SIGNAL toggle) immediately computes it and draws it on the chart over its **`History Days`** window (default 3 days) — a one-shot backfill with no alert sounds and no bot orders; afterwards the live state machine continues seamlessly. Switching OFF removes only that module's own drawings. Full per-stage spec: **`docs/SIGNALS.md`**. A1 lives in the independent sibling repo `nt8-kat-A1-TradeBackground` and is **not** compiled inside this host (host only filters B1/B2/A2).

### A2 — Alert Signal A2 (placeholder)
- **Purpose**: template for future alert-only signals (sound + chart drawings, no bot orders).
- **Status**: placeholder — `EvaluateAlertA2` is a no-op, backfill replays `PassFiltersAt` only. Will host alert-only logic later.

### B1 — Bot Signal B1 (34bounce8+ — shared by Sell/Buy, mirrored)
- **Context (trend stack)**: BUY 34+++ = EMA 8 above/touching EMA 34 (no cross down) + EMA 34 > EMA 89 > EMA 144 > EMA 200 — **each condition individually toggleable** in settings. SELL mirrors. Trend loss cancels the pending entry.
- **Setup**: price runs above EMA 34, pulls back and **touches** it (wick low ≤ EMA 34) while **closing above** → pending stop LONG at the touch candle's **high (wick included)** + `Entry Offset`. A later touch candle with a **lower high migrates the entry down** (a higher high means the stop already filled). A **close below EMA 34** cancels the entry — a touch candle must close above EMA 34 to place the pending stop Buy at all.
- **No stage markers** (single-phase setup): drawings are the entry/SL/TP lines plus a `Buy B1` label below the entry candle (Buy Text Color) / `Sell B1` above (Sell Text Color). Lines stay rendered while the entry is pending (`KeepAlive` — no `Line Length` fade); cancel removes lines + label; fill lets them fade per `Line Length`.

### B2 — Bot Signal B2 (89uturn34 — shared by Sell and Buy, mirrored)
- **Context**: Fast EMA vs Slow EMA — Sell: fast below slow (downtrend); Buy: fast above slow.
- **Sequence** (every step on close basis, wicks don't cross):
  1. **Armed**: price closes beyond the fast EMA (Sell: below / Buy: above).
  2. **Pullback**: price crosses back through the fast EMA toward the slow EMA — the sequence clock starts.
  3. **Touch**: price touches or crosses the slow EMA.
  4. **U-turn**: price reverses and closes back through the fast EMA (with-trend again).
  - The whole sequence must complete within **`Max Sequence Bars`** (default 30, counted from the cross bar) or the setup expires and rearms. A pullback that reverses before ever touching the slow EMA simply rearms.
- **Phase milestone markers**: at every B2 phase transition a persistent label is drawn — `B2-arm` (armed beyond ema34), `B2-pull` (crossed back through ema34), `B2-pull-T` (pullback touched ema89). Buy markers below the low, sell markers above the high. Cleared by HUD Clear or switching B2 OFF.
- **Trigger**: fires immediately on the U-turn close — 4-step sequence (arm → cross → touch → U-turn).
- **Entry**: the entry sits at the U-turn bar's extreme — **C1/C2** = its low (sell) / high (buy) — with `Entry Offset` ticks (sell below, buy above).
- **Drawing**: sell entry line **solid red**, buy entry line **solid lime green** (both with `Entry Offset` ticks); SL dashed red, TP dashed green — taken from the selected **ATM template** when it defines StopLoss/Target (settings `Stop/Target Distance` are the fallback); ATM **trailing-SL trigger lines** — **BE** DeepSkyBlue dash-dot, **SL1** orange dot, **SL2** magenta dot (1 px, profit side of entry). Lines remain visible for up to `Line Length` bars.

### Filters — BOT FILTER (gates B1+B2+A2)
- **Every filter gate is OFF by default** (session-only HUD toggles boot OFF). Enable one by one as needed.
- **ADX rising** (`AdxRisingBars` 5): ADX must be rising over that lookback.
- **ADX MTF** (`AdxMtfMinutes` 3, `AdxMtfPeriod` 14, `AdxMtfMin` 22): regime ADX on a dedicated MTF series (BarsArray[1]), no lookahead via `ClosedBarCutoff`.
- **ER (trend)** (`ErPeriod` 40, `ErMin` 0.25): Kaufman Efficiency Ratio ≥ min — blocks choppy.
- **CI (chop)** (`CiPeriod` 40, `CiMax` 50): Choppiness Index ≤ max — blocks ranging (>61.8 chop).
- **Volume** (`VolumeSmaPeriod` 20, `VolumeMinMult` 1.0): bar volume ≥ SMA × mult — blocks dead bars.
- **Time window** (`TimeFilterStart` 08:00 `TimeFilterEnd` 17:00, overnight wraps midnight, equal disables).
- **StackEMA** (`StackEmaFilterEnabled` false; EMAs 8/21/34/55/89 on five packs S30/M1/M3/M5/M15, visible-packs select participation; all hidden = bypass). Uses closed-bar MTF mapping.
- A1 is **pure** — no filter gates touch it (alert-side filters abolished v0.79).


## Bot (semi-auto)
- Trades **only** while the HUD **BOT: ON** button is active (off by default) *and* `Bot Enabled` is set — never runs on its own. Switching BOT OFF cancels the pending entry immediately.
- **Respects the signal toggles**: BOT trades **every signal switched ON** (A1/A2/A3) on the chosen account + ATM, and **never** a signal that is OFF. Switching a signal OFF also **cancels its pending bot order** (and blocks any migration re-place), so an OFF signal can neither open a new entry nor fill a stale one. One bot order at a time.
- On an A1 signal it submits a stop order (sell stop below the better candidate low / buy stop above the better candidate high) through the selected **ATM template** on the selected **account**; `None` or a missing template falls back to a bare stop order. If price has **already run past the entry**, the order is submitted as a **limit** instead (a stop on the wrong side of the market would be rejected) — same rule as KatTradeManager.
- **Migration**: while the entry is still working, a newer bar closing on the setup side of the fast EMA with a better extreme (sell: higher low / buy: lower high) cancels the order and re-places it at the better price once the cancel settles. A 34/89 trend flip cancels the pending entry. One bot order at a time; once filled, the ATM owns the brackets.

## Settings (6 sections + StackEMA packs)

| Section | Settings |
|---|---|
| 1. Filters | `AdxPeriod` 60, `AdxRisingBars` 5, `AdxMtfMinutes` 3 / `AdxMtfPeriod` 14 / `AdxMtfMin` 22, `ErPeriod` 40 `ErMin` 0.25, `CiPeriod` 40 `CiMax` 50, `VolumeSmaPeriod` 20 `VolumeMinMult` 1.0, `Time Start/End` 08:00–17:00, `StackEmaFilterEnabled` false + EMA 8/21/34/55/89 + 5 timeframes (S30/M1/M3/M5/M15) + 5 visible toggles, `Alert Sound` (.wav dropdown, user `Documents\NinjaTrader 8\sounds` wins) |
| 2.5 Alert Signal A2 | `AlertA2Enabled` false, `AlertA2HistoryDays` 3 (placeholder) |
| 3. Bot Signal B1 — 34bounce8+ | `B1Enabled` false, `B1HistoryDays` 3, Cond `EMA8>=34` `34>89` `89>144` `144>200` (true), `B1EntryOffsetTicks` 1, `B1StopDistanceTicks` 60 / `B1TargetDistanceTicks` 120 (ATM fallback) |
| 3.5 Bot Signal B2 — 89uturn34 | `B2Enabled` false, `B2HistoryDays` 3, `EmaFastPeriod` 34 `EmaSlowPeriod` 89 `MaxSequenceBars` 30, `B2EntryOffsetTicks` 1, `B2StopDistanceTicks` 60 / `B2TargetDistanceTicks` 120 (ATM fallback) |
| 4. Lines & Text | `LineLengthBars` 7, `LineWidth` 2, `ArrowOffsetTicks` 3, `SellEntryLineColor` Red `BuyEntryLineColor` LimeGreen `SLLineColor` Red `TPLineColor` Green `SellTextColor` Red `BuyTextColor` LimeGreen |
| 5. Bot | `BotEnabled` false, `BotOrderQuantity` 1, `BotAtmTemplate` (`mnq. 1ct. 15-be20-35move15-50triggertrail5step1` default, dropdown + None), `BotAccountName` Sim101, `BotBufferTicks` 2, `DailyMaxDDEnabled` false `DailyMaxDD` 500, `DailyMaxProfitEnabled` false `DailyMaxProfit` 1000 |
| 6. ATM Quick Sets | Set 1–6 `Name` (max 3 chars, A–F) + `ATM` (assigned template, default none). HUD amber = current. |

All signal math runs on the primary series of the chart the indicator is added to. A1's standalone indicator lives in the sibling repo and renders independently.

## HUD
TradeManager pixel-perfect panel (HudGap 2, HudPanelWidth 250 → inner 238, UseLayoutRounding): dark navy card `Argb(240,20,24,33)` on draggable canvas (clamped 40px visible), `⚡ KAT 34-ScalperBot vX.XX` header, status line (5 s auto-clear) mirrors bot events.
- **BOT**: account dropdown (syncs ChartTrader), ATM template dropdown (sorted, `None` = bare order), 6 ATM quick-set buttons (amber ON), `⚡ BOT: ON/OFF` (OFF cancels pending), `SELL/BUY MARKET` 43px, `Revert` / `Break Even` 43px, `Close/flatten` 59px, `Max DD` / `Max Profit` purple toggles (2-col span).
- **ALERT SIGNAL**: `A2` placeholder toggle.
- **BOT SIGNAL**: `B1 (34bounce8+)` + `B2 (89uturn34)` dark-blue ON (`#0F3C82`).
- **BOT FILTER**: 3×2 grid `ADX rising | ADX MTF`, `ER (trend) | CI (chop)`, `Volume | Time window` — blue ON / gray OFF, all OFF by default, session-only.
- **DRAW**: `Clear` (removes all K34S_* + legacy K8934_*).
- Drag anywhere outside buttons; HUD position persists per instance.

## Installation in NinjaTrader 8

1. Open **NinjaTrader 8**.
2. Go to **Tools** -> **NinjaScript Editor**.
3. Open or import `Kat34Scalper.cs` under `Indicators`.
4. Press **F5** to Compile (chart indicators auto-reload with the new version label).
5. Add `Kat34Scalper` to any NT8 Chart.

`KAT-StackEMA` is installed from the same source sync and can be added independently to a chart. Its standalone settings are separate from `Kat34Scalper`; configure the host's matching five pack settings when using the StackEMA filter. Visible host packs select filter participation; with all five hidden, that filter bypasses.

When `StackEMA Filter Enabled` is OFF, Scalper does not add StackEMA secondary series. When ON, matching ADX MTF `Minute` (BarsArray[1]) reuse is checked; only unique StackEMA `Second` timeframes are added as BarsArray[2..]. Filter bypasses when all five packs hidden.

## Development workflow
- `pwsh scripts/Run-AllChecks.ps1` — xunit suite + net48 compile gate.
- `pwsh scripts/Deploy-NT8.ps1` — copies sources into NT8 + verifies auto-recompile.
- `nt8-kat-A1-TradeBackground` and `nt8-kat-StackEMA` — INDEPENDENT Git repositories. They are NOT submodules; they sit as sibling folders next to this repo, and the compile gate / deploy script reference their canonical sources by relative path.
- `pwsh scripts/connect-Repos.ps1` — after a fresh clone, verifies (and reports) the sibling A1 + StackEMA repos so compile/deploy can find them.
- Version bump, diary, graphify and GitHub sync per `AGENTS.md` / `RULES.md`.

## License

MIT
