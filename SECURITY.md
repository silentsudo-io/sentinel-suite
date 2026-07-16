# Security Policy

## Reporting a vulnerability
If you find a security issue — or a **safety** issue in a tool that places or manages orders (anything that
could cause unintended trades, wrong sizing, a bypassed risk gate, or loss) — please report it **privately**,
not in a public issue.

- Use **GitHub Security Advisories** ("Report a vulnerability") on the repository — the preferred private
  channel — or contact the maintainer **[@silentsudo-io](https://github.com/silentsudo-io)** on GitHub.
- Please include: the tool + version, NinjaTrader version, steps to reproduce, and the impact.
- Give us a reasonable window to investigate and fix before any public disclosure.

We'll acknowledge your report, keep you updated, and credit you (if you wish) once a fix is available.

## Scope
In scope: the Sentinel Suite source in this repository. In particular we take seriously any defect in the
**Safety layer** (the order gate, governor, kill-switch, sizing) or any path that could submit or modify
an order unexpectedly.

Out of scope: NinjaTrader itself and its platform assemblies (report those to NinjaTrader); your broker,
data feed, or account configuration; and market outcomes.

## No warranty / not advice
This software is provided **as-is, with no warranty of any kind**, for **educational purposes only**. It is
**not financial advice**. Trading carries substantial risk of loss. You are solely responsible for testing
any tool on a simulated account and for your own trading decisions. See the top-level README and NOTICE.

## Good practice for users
- Test on **SIM** first, always.
- The execution tools (Deck, Bridge, Copier) and the Prop-Survival Kit are **later** releases with their own
  warnings; the P1 bundles (Skins, Sensors) **place no orders**.
