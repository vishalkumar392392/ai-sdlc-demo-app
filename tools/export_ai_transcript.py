#!/usr/bin/env python3
"""Render a Claude Code session transcript (main agent + every sub-agent) into
ai-logs/AI_TRANSCRIPT.md, using the same columns as the hook log:
  Time (UTC) | Agent | Event | Tool | Summary

The hook log (.claude/hooks/log_interaction.py) records interactions live; this
exporter covers the whole session, including steps that ran before the hooks
were installed. Secrets, tokens and email addresses are redacted, model
"thinking" blocks and harness system reminders are left out.

Usage: python3 tools/export_ai_transcript.py <session.jsonl> [output.md]
"""
import json, pathlib, re, sys

MAX = 500
SECRET = re.compile(r"(gh[opsu]_[A-Za-z0-9]{20,}|Bearer\s+[A-Za-z0-9._$-]{8,}|[\w.+-]+@[\w-]+\.[\w.]+)")
REMINDER = re.compile(r"<(system-reminder|local-command-[a-z]+|command-[a-z]+)[^>]*>.*?</\1[^>]*>", re.S)
PASTED = re.compile(r"<pasted_content[^>]*>.*?</pasted_content[^>]*>", re.S)


def clean(text, limit=MAX):
    text = PASTED.sub("[pasted assignment brief]", REMINDER.sub("", str(text or "")))
    text = SECRET.sub("[redacted]", text)
    text = " ".join(text.split())
    return text if len(text) <= limit else text[: limit - 1] + "…"


def tool_summary(data):
    if not isinstance(data, dict):
        return clean(data)
    for key in ("description", "file_path", "command", "prompt", "skill", "query", "url"):
        if data.get(key):
            return clean(f"{key}={data[key]}", 300)
    return clean(json.dumps(data), 300)


def rows(path, agent):
    for line in open(path, encoding="utf-8"):
        try:
            entry = json.loads(line)
        except json.JSONDecodeError:
            continue
        kind, message = entry.get("type"), entry.get("message")
        if kind not in ("user", "assistant") or not isinstance(message, dict):
            continue
        ts = (entry.get("timestamp") or "")[:19].replace("T", " ")
        content = message.get("content")
        if isinstance(content, str):
            text = clean(content)
            if text:
                yield ts, agent if agent != "main" else "user", "prompt", "", text
            continue
        for block in content or []:
            btype = block.get("type")
            if btype == "text" and kind == "assistant":
                text = clean(block.get("text"))
                if text:
                    yield ts, agent, "response", "", text
            elif btype == "text" and kind == "user":
                text = clean(block.get("text"))
                if text:
                    yield ts, "user" if agent == "main" else agent, "prompt", "", text
            elif btype == "tool_use":
                yield ts, agent, "tool call", block.get("name", ""), tool_summary(block.get("input"))
            elif btype == "tool_result" and block.get("is_error"):
                yield ts, agent, "tool error", "", clean(block.get("content"), 200)


def main():
    session = pathlib.Path(sys.argv[1])
    output = pathlib.Path(sys.argv[2] if len(sys.argv) > 2 else "ai-logs/AI_TRANSCRIPT.md")
    sources = [(session, "main")]
    subagent_dir = session.with_suffix("") / "subagents"
    for transcript in sorted(subagent_dir.glob("agent-*.jsonl")):
        meta = transcript.with_suffix(".meta.json")
        name = transcript.stem
        if meta.exists():
            info = json.loads(meta.read_text())
            name = f'{info.get("description", name)} [{info.get("agentType", "agent")}]'
        sources.append((transcript, f"subagent:{name}"))

    table = sorted(row for path, agent in sources for row in rows(path, agent))
    lines = ["# AI transcript log", "",
             "Full record of the AI interactions for this assignment, exported from the Claude Code session",
             "transcript by `tools/export_ai_transcript.py`. Same columns as the live hook log in `AI_INTERACTIONS.md`.",
             "Model reasoning, harness reminders and secrets are excluded; long entries are truncated.", "",
             f"Entries: {len(table)} from {len(sources)} transcripts (main agent + {len(sources) - 1} sub-agents).", "",
             "| Time (UTC) | Agent | Event | Tool | Summary |", "| --- | --- | --- | --- | --- |"]
    for row in table:
        lines.append("| " + " | ".join(str(cell).replace("|", "\\|") for cell in row) + " |")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"wrote {output} ({len(table)} entries)")


if __name__ == "__main__":
    main()
