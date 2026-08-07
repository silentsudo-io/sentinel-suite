#!/usr/bin/env python3
"""
Sentinel Trade Explorer — filterable Plotly view over the SQLite tick corpus.

    cd Sentinel\\Lab
    .\\.venv\\Scripts\\streamlit run viz\\explorer.py     → http://localhost:8501

Reads db\\sentinel.db (populated by ingest\\ingest.py). Filter the whole corpus in the sidebar,
scan the blotter + MFE/MAE scatter, then drop into any trade's tick-by-tick PATH.
The path chart's y-axis is FAVORABLE EXCURSION (ticks) — entry = 0 — so early-vs-late jumps out.
Spec: bin\\Custom\\Docs\\SENTINEL_DATA_PLATFORM_SPEC.md.
"""
from __future__ import annotations
import os, sys, sqlite3
import pandas as pd
import streamlit as st
import plotly.graph_objects as go

# Human bar-type labels — DISPLAY ONLY (values stay the raw machine bartag). Mirror of SentinelCore.FriendlyBartag.
_LAB_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if _LAB_ROOT not in sys.path:
    sys.path.insert(0, _LAB_ROOT)
from sentinel_lab.bartag import friendly_bartag

DB = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "db", "sentinel.db"))

st.set_page_config(page_title="Sentinel Trade Explorer", layout="wide")

# Filter pills use the cyan primaryColor; Streamlit's auto text color washes out on that bright ground.
# Force a readable dark slate on the tag label + its x icon (same fix as the Sentinel skin's cyan chips).
st.markdown("""<style>
span[data-baseweb="tag"], span[data-baseweb="tag"] span { color: #0c1620 !important; }
span[data-baseweb="tag"] svg { fill: #0c1620 !important; }
</style>""", unsafe_allow_html=True)
st.title("🎯 Sentinel Trade Explorer")

if not os.path.exists(DB):
    st.error(f"no DB at {DB} — run  `python ingest\\ingest.py`  first.")
    st.stop()


@st.cache_data(ttl=5)
def load_trades():
    with sqlite3.connect(DB) as c:
        return pd.read_sql_query("SELECT * FROM trades", c, parse_dates=["entry_utc", "exit_utc"])


def load_ticks(tid):
    with sqlite3.connect(DB) as c:
        return pd.read_sql_query("SELECT ms, px, fav_t FROM ticks WHERE trade_id=? ORDER BY ms",
                                 c, params=(tid,))


df = load_trades()
if df.empty:
    st.warning("DB is empty — take a trade with the Deck's Log Tick Path ON, then the ingester loads it.")
    st.stop()

# ── filters ──
sb = st.sidebar
sb.header("filters")
if sb.button("↻ refresh"):
    st.cache_data.clear(); st.rerun()

insts = sb.multiselect("instrument", sorted(df["inst"].dropna().unique()), [])
bts   = sb.multiselect("bar type",   sorted(df["bartype"].dropna().unique()), [], format_func=friendly_bartag)
srcs  = sb.multiselect("source",     sorted(df["source"].dropna().unique()), [])
side  = sb.radio("side", ["both", "long", "short"], horizontal=True)
res   = sb.radio("result", ["all", "winners", "losers"], horizontal=True)
minT  = sb.slider("min ticks", 0, int(df["n_ticks"].max() or 0), 0)
inc_partial = sb.checkbox("include partial captures", True)
# fill fidelity: tick.2+ (src='last') = raw last-trade; tick.1 = synthetic brick Close[0]. Exclude brick for any fill study.
fidelity = sb.radio("fill fidelity", ["all", "raw only", "brick only"], horizontal=True,
                    help="raw = tick.2+ last-trade (grid-true); brick = legacy tick.1 (HA/TBars Close[0], off-grid)")
has_conv = df["conviction"].notna().any()
conv_rng = sb.slider("conviction", 0.0, 1.0, (0.0, 1.0)) if has_conv else None

q = df.copy()
if insts: q = q[q["inst"].isin(insts)]
if bts:   q = q[q["bartype"].isin(bts)]
if srcs:  q = q[q["source"].isin(srcs)]
if side == "long":  q = q[q["dir"] == 1]
if side == "short": q = q[q["dir"] == -1]
if res == "winners": q = q[q["pnl_ticks"] > 0]
if res == "losers":  q = q[q["pnl_ticks"] <= 0]
q = q[q["n_ticks"] >= minT]
if not inc_partial: q = q[q["partial"] == 0]
_israw = q["src"].eq("last") if "src" in q.columns else pd.Series(False, index=q.index)
if fidelity == "raw only":   q = q[_israw]
if fidelity == "brick only": q = q[~_israw]
if conv_rng is not None:
    q = q[q["conviction"].between(*conv_rng) | q["conviction"].isna()]
q = q.sort_values("entry_utc", ascending=False).reset_index(drop=True)

# ── corpus metrics ──
n = len(q)
win = float((q["pnl_ticks"] > 0).mean() * 100) if n else 0
c1, c2, c3, c4, c5 = st.columns(5)
c1.metric("trades", n)
c2.metric("win rate", f"{win:.0f}%")
c3.metric("avg MFE (t)", f"{q['max_fav_ticks'].mean():.1f}" if n else "—")
c4.metric("avg MAE (t)", f"{q['max_adv_ticks'].mean():.1f}" if n else "—")
c5.metric("avg exit (t)", f"{q['pnl_ticks'].mean():.1f}" if n else "—")

if n == 0:
    st.info("no trades match the filters."); st.stop()

left, right = st.columns([3, 2])

# ── blotter ──
with left:
    st.subheader("blotter")
    show = q[["entry_utc", "inst", "bartype", "dir", "n_ticks", "max_fav_ticks",
              "max_adv_ticks", "mfe_mae_ratio", "pnl_ticks", "partial", "source"]].copy()
    show["dir"] = show["dir"].map({1: "LONG", -1: "SHORT"})
    st.dataframe(show, use_container_width=True, height=360, hide_index=True)

# ── MFE/MAE scatter (each trade a point) ──
with right:
    st.subheader("MFE vs MAE")
    sc = go.Figure()
    for lab, sub, col in [("win", q[q["pnl_ticks"] > 0], "#26a69a"), ("loss", q[q["pnl_ticks"] <= 0], "#ef5350")]:
        sc.add_trace(go.Scatter(x=sub["max_adv_ticks"], y=sub["max_fav_ticks"], mode="markers",
                                name=lab, marker=dict(color=col, size=9, opacity=0.8),
                                text=sub["trade_id"], hovertemplate="%{text}<br>MAE %{x:.0f}t · MFE %{y:.0f}t<extra></extra>"))
    mx = float(max(q["max_fav_ticks"].max(), q["max_adv_ticks"].max(), 1))
    sc.add_trace(go.Scatter(x=[0, mx], y=[0, mx], mode="lines", line=dict(color="#666", dash="dot"),
                            name="1:1", hoverinfo="skip"))
    sc.update_layout(template="plotly_dark", height=360, margin=dict(l=40, r=10, t=10, b=40),
                     xaxis_title="MAE (heat, ticks)", yaxis_title="MFE (ticks)")
    st.plotly_chart(sc, use_container_width=True)

# ── per-trade path ──
st.subheader("trade path")
tid = st.selectbox("record", q["trade_id"].tolist(), index=0)
row = q[q["trade_id"] == tid].iloc[0]
t = load_ticks(tid)
if t.empty:
    st.info("no ticks for this record."); st.stop()
t["sec"] = t["ms"] / 1000.0
mfe, mae = float(row["max_fav_ticks"]), float(row["max_adv_ticks"])
side_s = "LONG" if row["dir"] == 1 else "SHORT"

fig = go.Figure()
fig.add_hline(y=0, line=dict(color="#888", width=1), annotation_text="entry")
fig.add_hline(y=mfe,  line=dict(color="#26a69a", width=1, dash="dot"), annotation_text=f"MFE {mfe:.1f}t")
fig.add_hline(y=-mae, line=dict(color="#ef5350", width=1, dash="dot"), annotation_text=f"MAE {mae:.1f}t")
fig.add_trace(go.Scatter(x=t["sec"], y=t["fav_t"].clip(lower=0), fill="tozeroy", line=dict(width=0),
                         fillcolor="rgba(38,166,154,0.15)", hoverinfo="skip", showlegend=False))
fig.add_trace(go.Scatter(x=t["sec"], y=t["fav_t"].clip(upper=0), fill="tozeroy", line=dict(width=0),
                         fillcolor="rgba(239,83,80,0.15)", hoverinfo="skip", showlegend=False))
fig.add_trace(go.Scatter(x=t["sec"], y=t["fav_t"], mode="lines", line=dict(color="#4fc3f7", width=1.6),
                         name="favorable ticks", hovertemplate="%{x:.1f}s · %{y:.1f}t<extra></extra>"))
fig.update_layout(template="plotly_dark", height=480, margin=dict(l=40, r=20, t=30, b=40),
                  xaxis_title="seconds from entry",
                  yaxis_title="favorable excursion (ticks) · entry = 0",
                  title=f"{side_s} {row['inst']} · {tid}  (exit {row['pnl_ticks']:+.1f}t)")
st.plotly_chart(fig, use_container_width=True)
if row.get("src") == "last":
    st.caption("✅ raw last-trade px (tick.2 · grid-true) — fill-level detail is honest.")
else:
    st.caption("⚠ px = bar Close[0] (brick close, legacy tick.1) — shape is honest, but off-grid; exclude from fill studies via the 'fill fidelity' filter.")
