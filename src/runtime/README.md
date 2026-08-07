# ⚙️ The Runtime Floor

**Cross-cutting foundation — required by every other rung.** Copy this once; everything else assumes
it is present.

## What's here
- **`SentinelCore_v1_0_0.cs`** — the shared runtime: the `…State` seam store, the entry Gate, sizing,
  the Ledger, and `Swallow()`.
- **`SentinelCore.Foundation.cs`** / **`.Safety.cs`** / **`.SystemBuilder.cs`** — partial-class halves
  of the same type.
- **`SentinelSkin.cs`** — the drawing framework: glass cards, the palette, the label remover.

## Two things that will bite you otherwise
1. **The class is `SentinelCore`, unversioned, on purpose.** The *file* carries a version suffix; the
   *class* must not, or every consumer breaks on a bump. Never keep two copies in the tree — two
   `class SentinelCore` is `CS0101`, which breaks the entire compile, not just that file.
2. **NinjaTrader compiles every `.cs` under `bin\Custom` into ONE assembly.** One broken file means
   *nothing* new appears in any list. If a tool you just installed "isn't showing up", suspect a
   compile error somewhere else first.

## The seam, in one paragraph
A sensor calls `SentinelCore.SetXState(...)`; a consumer calls `GetXState(...)`. Neither knows the
other exists. That is how the suite fuses tools without coupling them — and why adding a sensor costs
nothing anywhere else.

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `AddOns/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. The runtime has no UI; it loads with the assembly.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
