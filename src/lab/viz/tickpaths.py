#!/usr/bin/env python3
"""
Sentinel Tick-Path Viewer — graphical browser for the Deck's manual tick-path records.

    cd Sentinel\\Lab
    .\\.venv\\Scripts\\streamlit run viz\\tickpaths.py
    → http://localhost:8501

Reads the sidecars the Deck writes to  Sentinel\\Excursions\\ticks\\<id>.jsonl :
  line 1  = JSON header (schema "tick.1"): tradeId, inst, bartype, dir, entry/exit, MFE/MAE, partial, ticks
  line 2+ = {"ms": <ms-from-entry>, "px": <price>}  per tick

The point of the chart is to READ THE ENTRY SHAPE — did the trade go favorable first (early/good entry),
or eat adverse heat first (late)? So the primary y-axis is FAVORABLE EXCURSION in ticks (entry = 0,
positive = in your favor, negative = heat), which makes the early-vs-late fingerprint jump out.
"""
from __future__ import annotations
import os, sys, glob, json
import pandas as pd
import streamlit as st
import plotly.graph_objects as go

# Human bar-type labels — display only (raw bartag stays the key). Mirror of SentinelCore.FriendlyBartag.
_LAB_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if _LAB_ROOT not in sys.path:
    sys.path.insert(0, _LAB_ROOT)
from lab_faults import swallow
from sentinel_lab.bartag import friendly_bartag

# tick sizes (fallback 0.1); the captured px is the bar CLOSE, so this is approximate for HA/Renko bar types
TICK = {"GC": 0.1, "MGC": 0.1, "SI": 0.005, "CL": 0.01, "ES": 0.25, "MES": 0.25,
        "NQ": 0.25, "MNQ": 0.25, "YM": 1.0, "ZN": 0.015625, "ZB": 0.03125}

TICKS_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Excursions", "ticks"))

st.set_page_config(page_title="Sentinel Tick Paths", layout="wide")
st.title("🎯 Sentinel Tick-Path Viewer")
st.caption(f"reading `{TICKS_DIR}`")


def load(path):
    with open(path, "r", encoding="utf-8") as fh:
        lines = [ln for ln in (l.strip() for l in fh) if ln]
    if not lines:
        return None, None
    hdr = json.loads(lines[0])
    rows = []
    for ln in lines[1:]:
        try:
            rows.append(json.loads(ln))
        except json.JSONDecodeError as _swex:
            swallow("viz.tickpaths.load", _swex)
    df = pd.DataFrame(rows)
    return hdr, df


files = sorted(glob.glob(os.path.join(TICKS_DIR, "*.jsonl")), key=os.path.getmtime, reverse=True)
if not files:
    st.warning("No tick-path records yet. Flip the Deck's **RECORD ▸ Log Tick Path** ON and take a trade.")
    st.stop()

labels = [os.path.basename(f) for f in files]
choice = st.sidebar.selectbox("record", labels, index=0)
if st.sidebar.button("↻ refresh"):
    st.rerun()
path = files[labels.index(choice)]

hdr, df = load(path)
if hdr is None or df is None or df.empty:
    st.error("empty / unreadable record (trade may still be open — the sidecar writes on exit)")
    st.stop()

inst   = hdr.get("inst", "?")
dirn   = int(hdr.get("dir", 1))
entry  = float(hdr.get("entryPx", df["px"].iloc[0]))
tick   = TICK.get(inst, 0.1)

# favorable excursion in ticks: +ve = in your favor, -ve = heat (entry = 0)
df = df.sort_values("ms").reset_index(drop=True)
df["sec"] = df["ms"] / 1000.0
df["favT"] = dirn * (df["px"] - entry) / tick
df["runMFE"] = df["favT"].cummax()
df["runMAE"] = (-df["favT"]).cummax()

mfe = float(hdr.get("maxFavTicks", df["favT"].max()))
mae = float(hdr.get("maxAdvTicks", (-df["favT"]).max()))
dur = df["sec"].iloc[-1]

# ── header stats ──
side = "LONG" if dirn > 0 else "SHORT"
partial = hdr.get("partial", False)
c1, c2, c3, c4, c5, c6 = st.columns(6)
c1.metric("side", side + (" ⚠partial" if partial else ""))
c2.metric("instrument", f"{inst} · {friendly_bartag(hdr.get('bartype',''))}")
c3.metric("entry → exit", f"{entry:g} → {hdr.get('exitPx','?')}")
c4.metric("MFE (ticks)", f"{mfe:.1f}")
c5.metric("MAE (ticks)", f"{mae:.1f}")
c6.metric("duration / ticks", f"{dur:.0f}s / {len(df)}")

# ── the shape chart: favorable ticks over time ──
fig = go.Figure()
fig.add_hline(y=0, line=dict(color="#888", width=1), annotation_text="entry")
fig.add_hline(y=mfe,  line=dict(color="#26a69a", width=1, dash="dot"), annotation_text=f"MFE {mfe:.1f}t")
fig.add_hline(y=-mae, line=dict(color="#ef5350", width=1, dash="dot"), annotation_text=f"MAE {mae:.1f}t")
# favorable / adverse fills
fig.add_trace(go.Scatter(x=df["sec"], y=df["favT"].clip(lower=0), fill="tozeroy",
                         line=dict(width=0), fillcolor="rgba(38,166,154,0.15)", hoverinfo="skip", showlegend=False))
fig.add_trace(go.Scatter(x=df["sec"], y=df["favT"].clip(upper=0), fill="tozeroy",
                         line=dict(width=0), fillcolor="rgba(239,83,80,0.15)", hoverinfo="skip", showlegend=False))
fig.add_trace(go.Scatter(x=df["sec"], y=df["favT"], mode="lines",
                         line=dict(color="#4fc3f7", width=1.6), name="favorable ticks",
                         hovertemplate="%{x:.1f}s · %{y:.1f}t<extra></extra>"))
fig.update_layout(height=520, margin=dict(l=40, r=20, t=30, b=40),
                  template="plotly_dark", xaxis_title="seconds from entry",
                  yaxis_title="favorable excursion (ticks)  ·  entry = 0",
                  title=f"{side} {inst}  ·  {choice}")
st.plotly_chart(fig, use_container_width=True)

with st.expander("raw price path + header"):
    st.json(hdr)
    pf = go.Figure()
    pf.add_hline(y=entry, line=dict(color="#888", width=1), annotation_text="entry")
    pf.add_trace(go.Scatter(x=df["sec"], y=df["px"], mode="lines", line=dict(color="#4fc3f7", width=1.4)))
    pf.update_layout(height=320, margin=dict(l=40, r=20, t=10, b=40), template="plotly_dark",
                     xaxis_title="seconds from entry", yaxis_title="price (bar close)")
    st.plotly_chart(pf, use_container_width=True)

st.caption("⚠ px = bar Close[0] (brick close on HA/Renko bar types), not raw last-trade — approximate for now.")
