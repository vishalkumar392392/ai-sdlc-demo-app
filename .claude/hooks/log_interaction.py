#!/usr/bin/env python3
"""Claude Code hook: append every AI interaction to ai-logs/ai-interactions.jsonl
in one fixed schema, and re-render ai-logs/AI_INTERACTIONS.md on Stop.

Schema (one JSON object per line, always these keys; see ai-logs/README.md):
  ts, session, event, ai_tool{name,version,model}, agent{name,type,mode},
  permission_mode, tool, skill, mcp_server, files, summary, outcome
"""
import json, os, re, subprocess, sys, datetime

ROOT = os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
LOG_DIR = os.path.join(ROOT, "ai-logs")
JSONL = os.path.join(LOG_DIR, "ai-interactions.jsonl")
MARKDOWN = os.path.join(LOG_DIR, "AI_INTERACTIONS.md")
MAX = 400
SECRET = re.compile(r"(gh[opsu]_[A-Za-z0-9]{20,}|Bearer\s+[A-Za-z0-9._$-]{8,}|[\w.+-]+@[\w-]+\.[\w.]+)")
FILE_TOOLS = ("Write", "Edit", "Read", "NotebookEdit")


def clean(text):
    text = SECRET.sub("[redacted]", str(text or ""))
    text = " ".join(text.split())
    return text if len(text) <= MAX else text[: MAX - 1] + "…"


def session_facts(transcript_path):
    """Model and Claude Code version from the tail of the session transcript."""
    facts = {"version": None, "model": None}
    try:
        with open(transcript_path, "rb") as handle:
            handle.seek(0, os.SEEK_END)
            handle.seek(max(0, handle.tell() - 512_000))
            lines = handle.read().decode("utf-8", "ignore").splitlines()
    except (OSError, TypeError):
        return facts
    for line in reversed(lines):
        try:
            entry = json.loads(line)
        except json.JSONDecodeError:
            continue
        if entry.get("type") == "assistant":
            facts["version"] = entry.get("version")
            facts["model"] = (entry.get("message") or {}).get("model")
            if facts["model"]:
                break
    return facts


def summarize_tool_input(data):
    if not isinstance(data, dict):
        return clean(data)
    for key in ("description", "command", "file_path", "prompt", "pattern", "skill", "url"):
        if data.get(key):
            return clean(f"{key}={data[key]}")
    return clean(json.dumps(data)[:MAX])


def agent_of(event):
    agent_type = event.get("agent_type")
    if event.get("hook_event_name") == "UserPromptSubmit":
        return {"name": "user", "type": "human", "mode": None}
    if agent_type or event.get("agent_id"):
        return {"name": agent_type or "subagent", "type": "sub-agent", "mode": "Agent (background, non-interactive)"}
    return {"name": "main", "type": "built-in (Claude Code main agent)", "mode": "Agent"}


def record(event):
    name = event.get("hook_event_name", "Unknown")
    tool = event.get("tool_name", "") or ""
    tool_input = event.get("tool_input") if isinstance(event.get("tool_input"), dict) else {}
    outcome = None
    if name == "UserPromptSubmit":
        summary = clean(event.get("prompt"))
    elif name == "PreToolUse":
        summary = summarize_tool_input(tool_input)
    elif name == "PostToolUse":
        response = event.get("tool_response")
        outcome = "error" if isinstance(response, dict) and (response.get("is_error") or response.get("interrupted")) else "ok"
        summary = summarize_tool_input(tool_input)
    elif name in ("SubagentStop", "Stop"):
        summary = clean(event.get("last_assistant_message") or f"{name} (turn finished)")
        outcome = "completed"
    else:
        summary = clean(json.dumps({k: v for k, v in event.items() if k not in ("transcript_path", "cwd")})[:MAX])
    facts = session_facts(event.get("transcript_path"))
    return {
        "ts": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
        "session": str(event.get("session_id", ""))[:8],
        "event": name,
        "ai_tool": {"name": "Claude Code", "version": facts["version"], "model": facts["model"]},
        "agent": agent_of(event),
        "permission_mode": event.get("permission_mode"),
        "tool": tool,
        "skill": tool_input.get("skill") if tool == "Skill" else None,
        "mcp_server": tool.split("__")[1] if tool.startswith("mcp__") else None,
        "files": [tool_input["file_path"]] if tool in FILE_TOOLS and tool_input.get("file_path") else [],
        "summary": summary,
        "outcome": outcome,
    }


def render_markdown():
    rows = []
    with open(JSONL, encoding="utf-8") as handle:
        for line in handle:
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                continue
    out = ["# Live AI interaction log (hook output)", "",
           "Written automatically by `.claude/hooks/log_interaction.py` on every prompt, tool call and agent stop.",
           "Machine-readable source: `ai-interactions.jsonl`. Phase-level summary: `AI_LOG.md`. Do not edit by hand.", "",
           "| Time (UTC) | Event | Agent | Model | Mode | Tool | Skill / MCP | Outcome | Summary |",
           "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for row in rows:
        agent = row.get("agent") or {}
        extra = row.get("skill") or (f"mcp:{row['mcp_server']}" if row.get("mcp_server") else "")
        cells = [row.get("ts", ""), row.get("event", ""), agent.get("name", ""),
                 (row.get("ai_tool") or {}).get("model") or "", row.get("permission_mode") or "",
                 row.get("tool", ""), extra or "", row.get("outcome") or "", row.get("summary", "")]
        out.append("| " + " | ".join(str(c).replace("|", "\\|") for c in cells) + " |")
    with open(MARKDOWN, "w", encoding="utf-8") as handle:
        handle.write("\n".join(out) + "\n")


def main():
    try:
        event = json.loads(sys.stdin.read(), strict=False)
    except json.JSONDecodeError:
        return 0
    os.makedirs(LOG_DIR, exist_ok=True)
    with open(JSONL, "a", encoding="utf-8") as handle:
        handle.write(json.dumps(record(event), ensure_ascii=False) + "\n")
    if event.get("hook_event_name") in ("Stop", "SubagentStop"):
        render_markdown()
    if event.get("hook_event_name") == "Stop" and event.get("transcript_path"):
        # Keep the phase-level traceability log (ai-log.jsonl / AI_LOG.md) current after every turn.
        builder = os.path.join(ROOT, "tools", "build_ai_log.py")
        try:
            subprocess.run([sys.executable, builder, event["transcript_path"]], cwd=ROOT, timeout=60,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except (OSError, subprocess.SubprocessError):
            pass  # logging must never break the session
    return 0


if __name__ == "__main__":
    sys.exit(main())
