# SentinelExhaustion_v1_0_0.cs

> `bin/Custom/Indicators/SentinelExhaustion_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 402 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelExhaustion_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `ExhaustionState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Exhaustion — Leledc exhaustion-bar reversal detector (CLEAN-ROOM)  |   Version v1.0.0
 File: SentinelExhaustion_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors   |   display Name "Sentinel Exhaustion"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC "Leledc exhaustion bar" method — a published,
 non-copyrightable trading formula built from simple consecutive-close counters plus a high/low extreme
 confirmation. It uses NO third-party code. The installed LeledcExhaustionPro.cs (author "glaz", TradingView)
 in the tree was surveyed as a design reference only — none of its code was copied. See the provenance
 audit + NOTICE.

 WHY IT MATTERS — this is a MEAN-REVERSION / EXHAUSTION voter, orthogonal to the suite's trend/momentum
 sensors: it flags when a run of same-direction closes finally prints a bar that reverses AND pokes a new
 extreme, i.e. the move likely spent itself. It CONFIRMS or CONTRADICTS the trend axes at turning points.

 THE PUBLIC METHOD:
   • two counters bindex / sindex — each bar: Close[0] > Close[4] ⇒ bindex++,  Close[0] < Close[4] ⇒ sindex++.
   • BEARISH (major) exhaustion → DOWN reversal (Signal −1): bindex > MajQual AND a down bar (Close < Open)
     AND High[0] pokes the highest High of the last MajLen bars → reset bindex, fire −1, major = true.
   • BULLISH (major) exhaustion → UP reversal (Signal +1): sindex > MajQual AND an up bar (Close > Open)
     AND Low[0] pokes the lowest Low of the last MajLen bars → reset sindex, fire +1, major = true.
   • MINOR (secondary) — same shape with the smaller MinQual / MinLen thresholds, considered only when no
     major fired this bar. Minor fires do not reset the major counters.
   • Signal = the pulse this bar (+1/−1/0). Dir = last non-zero Signal, HELD HoldBars bars then decays to 0.

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.ExhaustionState (Signal / Dir / Major).
   • Draws a reversal MARKER — up-triangle below the bar on +1, down-triangle above on −1.
   • Hidden ±1 "Signal" PLOT (Values[0], transparent) for the Deck SIGNAL ARM / generic consumers.
   • A SentinelSkin.Painter glass card + label remover + roster heartbeat + scope key.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room Leledc exhaustion-bar detector (consecutive-close counters + extreme
            confirm; major + optional minor). ExhaustionState publish, reversal triangles, hidden Signal plot,
            glass card, scope key + heartbeat, label remover.
```

