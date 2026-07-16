# Consistency Governor — Design Spec
**Status:** ✅ **BUILT 2026-07-03** (pending live validation). Written 2026-07-02.

> **BUILT (2026-07-03):** phases 1-4 shipped. SentinelCore v1.0.7 `GovernorState` registry +
> `TradingAllowedToday`/`RecommendedSize`; **Risk v1.0.5/1.0.6 hosts it** (per-account daily realized-P&L
> tracking, baseline reset at session rollover, DayComplete@cap / DayHalted@loss). Consumers: **GTrader21
> gets it free via `CanEnter`**; **Copier** blocks a flat follower's new entry. Dashboard **Risk-tab governor
> section** + **state.json `governor` block**.
>
> **EVOLVED into ACCOUNT PROFILES (SentinelCore v1.0.8, Risk v1.0.6):** the governor's config source is now
> **`Sentinel\Profiles.conf`** (rich per-account profile — firm preset OR custom: size/contracts/ddType/ddAmt/
> dailyLoss/ratio/target/manualDaily/session), edited via the dashboard **Accounts tab**. `Governor.conf`
> (below) is the legacy fallback name. Firm presets: lucid .20 / bulenox .40 / tpt .50 / apex .30. Profiles
> also drive **sizing** (`SizedQuantity` = baseQty × SizeScale × RecommendedSize, clamped to ContractLimit)
> and **per-account session gating** (both via SentinelCore v1.0.9).
>
> **NOT yet built:** phase-5 SizeScale/dilution after an over-cap day (RecommendedSize=1.0 for now); live
> validation of caps/loss-stop/session on a Sim account. CAVEAT: dayPnl = realized − baseline (validate the
> P&L reset semantics against how your prop accounts actually report).
**Purpose:** automatically keep each prop account inside its firm's consistency rule *and* a daily
base-hit target, by gating entries once the day's profit hits the cap (or a loss stop) — making rule
violations *and* green-day overtrading structurally impossible. Operationalizes runbook §1 + discussion #4.

---

## The reframe
The consistency rule (Lucid 20% / Bulenox 40% / TPT 50%) isn't a constraint — it *describes* the base-hit
strategy. The Governor just enforces the behavior we already want, so we can't violate it by hand.

## The math — cap each day, guarantee compliance by construction
Rule (checked at payout / eval completion): **best_day ≤ R × total_cycle_profit.**
If no single day exceeds **DailyCap = R × ProfitTarget**, then when total reaches target, no day can
exceed R × total. Compliance falls out automatically.

| Firm | R | Target | **DailyCap** | Min days |
|---|---|---|---|---|
| Lucid | 0.20 | $9,000 | **$1,800/day** | ≥5 |
| Bulenox | 0.40 | $9,000 | **$3,600/day** | ≥3 |
| TPT | 0.50 | $9,000 | **$4,500/day** | ≥2 |

Governor uses `min(manualDailyTarget, R × ProfitTarget)` so you can trade *tighter* than the rule.
**Day-1 edge case:** the ratio is only evaluated at payout, never intraday — so the mechanism is the
per-day *cap* (distribute profit), not an intraday ratio calc. Simple and correct.

## Scope: PER ACCOUNT (not per instrument)
Consistency is a per-account rule, so the Governor tracks each *prop account's* realized daily P&L vs its
firm's cap — a different axis from Arc (per-instrument on the leader). It applies where the money is:
- **Direct-EA (Bulenox):** gate the strategy running *on* that account.
- **Auto-copier (Lucid):** gate the copier's mirror *to* that follower.
- **Manual-assist (TPT):** advisory — suppress tickets once that account's daily cap is hit.

## Two daily stop triggers
1. **Profit cap (consistency):** today's realized ≥ DailyCap → `DayComplete` → stop entries for the
   session. Banks the base hit, respects the ratio, and stops you overtrading a green day into a DD breach.
2. **Loss stop (drawdown):** today's realized ≤ −DailyLossStop → `DayHalted` → stop. Set *inside* the
   firm's trailing DD; complements Risk's kill-switch.
Both reset at session rollover.

## The big-day trap (the hard part)
If a single trade *overshoots* DailyCap (a fast TP), today > cap → future days must dilute it. Governor's
answer: **trade SMALLER, longer — never MORE to fix a ratio.**
- After an over-cap day, compute the dilution needed (best_day/total ≤ R) and apply a **SizeScale < 1** to
  subsequent days so the dilution profit accrues *without adding risk*.
- Expose a **recommended daily size** so the user/strategy sizes down automatically.

## Integration (publish/consult — same pattern as the whole suite)
- **SentinelCore:** per-account governor state — `SetGovernorState(account, allowed, reason, dailyPnl, cap)`
  + consult `TradingAllowedToday(account)` + `RecommendedSize(account)`.
- **Consumers:** GTrader21 (Direct-EA) adds `&& TradingAllowedToday(acct)` to its entry gate; the copier's
  `MirrorToFollower` checks it per follower; manual-assist suppresses tickets; dashboard shows a Governor row.
- **Host (recommended): the Risk service** — it already reads account P&L and drives the kill-switch, so
  extend it to per-account daily caps. (Or a standalone service.) Publishes to SentinelCore; all consult.
- **Composition:** an entry fires iff
  `SlotLive(Arc) && EyeQualified(if gated) && TradingAllowedToday(Governor) && CanAct(kill/feed)`.

## Config — Governor.conf (per account)
```
account=<name>|firm=lucid|ratio=0.20|target=9000|dailyLossStop=1500|manualDailyTarget=0
```
`ratio`/`target` can be firm-presets; `manualDailyTarget=0` → use R×target. `firm` sets sensible defaults.

## Non-goals (what it is NOT)
- **Not** the trailing-DD calculator — that's Risk's job (real-time floor vs −$4,500). The Governor owns
  daily *distribution* + overtrading + the daily loss stop; Risk owns the trailing-DD breach + feed health.
  They compose; neither duplicates the other.

## Build phases
1. SentinelCore governor registry + `TradingAllowedToday` / `RecommendedSize` consults.
2. Risk-hosted per-account daily P&L tracking + cap/loss triggers + session reset + publish.
3. Consumers: GTrader21 entry gate, copier per-follower check, manual-assist ticket suppression.
4. Dashboard **Governor** row (per account: dailyPnl / cap / status / recommended size).
5. Big-day SizeScale / dilution logic.
