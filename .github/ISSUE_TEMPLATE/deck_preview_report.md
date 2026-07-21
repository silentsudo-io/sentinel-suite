---
name: Deck preview report
about: Feedback or a bug from the Sentinel Deck testers' preview (the build that places orders)
title: "[deck] "
labels: deck-preview
---

<!--
  Thank you for testing the Deck.

  FIRST: click "Export diagnostics for a bug report" in the Deck panel, ideally BEFORE you change any
  settings. Explorer opens with the file selected. Drag that .txt onto this issue.

  It carries your build version, the account and how the Deck classified it, every panel setting, the
  indicators on your chart, the Deck's log trail and the day's order ledger — which is most of what we
  would otherwise have to ask you for.

  PRIVACY: that file contains account NAMES, instruments and the day's fills. No passwords or API keys.
  It is plain text — read it before posting, and redact anything you would rather not publish.
-->

**Diagnostics file attached?**
<!-- yes / no. If no, please paste the "deck :" and "account :" lines from an export. -->

**Was this SIM or a real (funded / prop evaluation) account?**
<!-- This changes how urgently we treat it. -->

**What did you do?**
<!-- The clicks, in order. "Selected STP, set qty 2, clicked SELL, dragged the stop up." -->

**What did you expect?**

**What happened instead?**

---

**Which area?**
<!-- Tick what applies. -->
- [ ] Entry (order type / quantity / $ RISK)
- [ ] Exits (Close / Reverse / Close Half / **FLATTEN THIS CHART**)
- [ ] Bracket / Breakeven / Trailing — which mode? ______
- [ ] On-chart order lines (drag / click-to-price / attach)
- [ ] **SIGNAL ARM / auto-fire**
- [ ] The preview band (account name or SIM-vs-REAL classification)
- [ ] Risk card
- [ ] Themes / layout / ergonomics
- [ ] Something else

**Did money move in a way you did not intend?**
<!-- Wrong size, wrong side, wrong instrument, an exit that did not exit. Say so plainly — this is
     the top priority regardless of anything else in this report. -->

---

<!--
  KNOWN ALREADY — no need to report these:
    • drag-to-attach: the grab works but the snap to an indicator plot can fail
    • manual clicks are NOT blocked by the kill switch / governor — that is by design, so a human can
      always act, especially to exit. Auto-fire IS blocked (fail-closed).

  WORTH REPORTING EVEN THOUGH IT LOOKS CORRECT:
    • "auto-fire BLOCKED: <reason>" — that is the safety system working, and we want to confirm the
      block was right. Include the reason.
-->
