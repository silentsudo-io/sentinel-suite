#!/usr/bin/env python
"""session_export - copy a Claude session transcript somewhere durable, plus a readable render.

WHY THIS EXISTS: the authoritative transcript lives under
`%USERPROFILE%\\.claude\\projects\\<project>\\<session-id>.jsonl`, which is subject to periodic cleanup
(there is a `.last-cleanup` marker beside it). A long working session can be the only record of decisions
that never made it into a doc. On 2026-07-31 a session was killed mid-tool-call by a VS Code extension-host
restart and had to be recovered from exactly this file - see the `continue-here-2026-07-31-now-chat-recovery`
memory. Copying it out is cheap; losing it is not.

usage:
    python sync/session_export.py <session-id> [label]
    python sync/session_export.py --latest [label]

Writes to Sentinel\\_sessions\\<date>_<label>_<shortid>_raw.jsonl  +  ..._readable.md
Read-only with respect to the source. Safe to re-run: a later run overwrites with a fresher snapshot.
"""
from __future__ import annotations
import io, json, os, sys, glob, shutil, datetime as dt

PROJ = os.path.expandvars(
    r"%USERPROFILE%\.claude\projects\c--Users-Administrator-Documents-NinjaTrader-8-bin-Custom-Strategies")
DEST = os.path.expandvars(r"%USERPROFILE%\Documents\NinjaTrader 8\Sentinel\_sessions")

TRUNC = 1200  # tool-result characters kept; full text stays in the raw .jsonl


def render(src: str, out: str) -> int:
    n = 0
    with io.open(out, "w", encoding="utf-8", newline="\n") as w:
        for i, raw in enumerate(io.open(src, encoding="utf-8"), 1):
            raw = raw.strip()
            if not raw:
                continue
            try:
                o = json.loads(raw)
            except Exception:                      # a partial last line while the session is live
                w.write(f"\n[[unparsable line {i}]]\n")
                continue
            t, ts = o.get("type"), o.get("timestamp", "")
            if t == "user":
                c = o.get("message", {}).get("content")
                if isinstance(c, str):
                    c = [{"type": "text", "text": c}]
                for b in c or []:
                    if b.get("type") == "text":
                        w.write(f"\n\n## [{i}] USER  {ts}\n\n{b['text']}\n"); n += 1
                    elif b.get("type") == "tool_result":
                        cc = b.get("content")
                        s = ("".join(x.get("text", "") for x in cc if isinstance(x, dict))
                             if isinstance(cc, list) else str(cc)).strip()
                        if len(s) > TRUNC:
                            s = s[:TRUNC] + f"\n…[+{len(s)-TRUNC} chars]"
                        w.write(f"\n[{i}] RESULT: {s}\n")
            elif t == "assistant":
                for b in o.get("message", {}).get("content") or []:
                    k = b.get("type")
                    if k == "text":
                        w.write(f"\n\n## [{i}] CLAUDE  {ts}\n\n{b['text']}\n"); n += 1
                    elif k == "thinking":
                        w.write(f"\n[{i}] (thinking {len(b.get('thinking',''))} chars)\n")
                    elif k == "tool_use":
                        w.write(f"\n[{i}] TOOL {b.get('name')}  {json.dumps(b.get('input', {}))[:900]}\n")
            elif t == "summary":
                w.write(f"\n\n## [{i}] SUMMARY\n\n{o.get('summary','')}\n")
    return n


def main() -> int:
    args = [a for a in sys.argv[1:]]
    if not args:
        print(__doc__); return 2
    if args[0] == "--latest":
        files = sorted(glob.glob(os.path.join(PROJ, "*.jsonl")), key=os.path.getmtime)
        if not files:
            print("no transcripts found"); return 1
        src = files[-1]
        sid = os.path.splitext(os.path.basename(src))[0]
        label = args[1] if len(args) > 1 else "session"
    else:
        sid = args[0]
        src = os.path.join(PROJ, sid + ".jsonl")
        label = args[1] if len(args) > 1 else "session"
    if not os.path.exists(src):
        print("no such transcript:", src); return 1

    os.makedirs(DEST, exist_ok=True)
    day = dt.datetime.fromtimestamp(os.path.getmtime(src)).strftime("%Y-%m-%d")
    stem = f"{day}_{label}_{sid.split('-')[0]}"
    raw_out = os.path.join(DEST, stem + "_raw.jsonl")
    md_out = os.path.join(DEST, stem + "_readable.md")

    shutil.copy2(src, raw_out)                     # authoritative record first
    msgs = render(raw_out, md_out)                 # render from the COPY, so a live source cannot shift under us

    print(f"raw      {raw_out}  ({os.path.getsize(raw_out):,} bytes)")
    print(f"readable {md_out}  ({os.path.getsize(md_out):,} bytes, {msgs} messages)")
    print("NOTE: a snapshot - re-run at the end of a live session to capture the tail.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
