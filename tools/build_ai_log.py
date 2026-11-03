#!/usr/bin/env python3
"""Build the structured AI traceability log for this assignment.

Inputs (all read-only):
  - the Claude Code session transcript (JSONL) and its sub-agent transcripts
  - git history of the current branch (commits, files, timestamps)
  - ai-logs/phases.json: the only hand-curated input (goal, human intervention,
    validation, outcome); everything else is derived automatically

Outputs:
  - ai-logs/ai-log.jsonl  one session record, one record per work phase, one per sub-agent run.
                          Every phase/sub-agent record has the nine required sections:
                          ai_tool, agent, prompts, skills_and_subagents, tools_and_integrations,
                          actions_and_outputs, human_intervention, evidence, outcome
  - ai-logs/AI_LOG.md     readable summary of the same records

Usage: python3 tools/build_ai_log.py [session.jsonl]
       (default: the newest transcript for this project under ~/.claude/projects)
"""
import collections, datetime, glob, json, os, pathlib, re, subprocess, sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOGS = ROOT / "ai-logs"
SECRET = re.compile(r"(gh[opsu]_[A-Za-z0-9]{20,}|Bearer\s+[A-Za-z0-9._$-]{8,}|[\w.+-]+@[\w-]+\.[\w.]+)")
HARNESS = re.compile(r"<(system-reminder|local-command-[a-z]+|command-[a-z]+|task-notification)[^>]*>.*?</\1[^>]*>", re.S)
PASTED = re.compile(r"<pasted_content[^>]*>(.*?)</pasted_content[^>]*>", re.S)
ANSI = re.compile(r"\x1b\[[0-9;]*m|\x1b\]8;[^\x1b]*\x1b\\")
EVIDENCE = [
    re.compile(r"(?:Passed|Failed)!\s+-\s+Failed:\s+\d+, Passed:\s+\d+, Skipped:\s+\d+, Total:\s+\d+"),
    re.compile(r"^\s*\d+ (?:passed|failed)(?: \([\d.]+m?s\))?\s*$", re.M),
    re.compile(r"final mutation score is [\d.]+ ?%"),
    re.compile(r"Change '[^']+' is valid"),
    re.compile(r"^(?:build-and-test|e2e)\s+(?:pass|fail)\b", re.M),
    re.compile(r"Commit message rejected by validate_commit hook"),
    re.compile(r"https://github\.com/[\w.-]+/[\w.-]+/pull/\d+"),
    re.compile(r"\{\"error\":\"[^\"]{10,140}\"\} \[\d{3}\]"),
    re.compile(r"Experiment [AB][^\n]*"),
]
CLI_PATTERNS = [
    ("dotnet test (xUnit)", r"\bdotnet test\b"), ("dotnet-stryker (mutation testing)", r"\bstryker\b"),
    ("dotnet run (web app)", r"\bdotnet run\b"), ("dotnet-install.sh (.NET SDK)", r"dotnet-install"),
    ("OpenSpec CLI", r"@fission-ai/openspec|\bopenspec\b"), ("Playwright test runner", r"\bplaywright\b|npm test"),
    ("npm", r"\bnpm (install|ci)\b"), ("git", r"\bgit (commit|push|checkout|add|diff|log|status)\b"),
    ("GitHub CLI (gh)", r"\bgh (pr|auth)\b"), ("curl (API smoke test)", r"\bcurl\b"), ("python3 (scripts)", r"\bpython3\b"),
]
TOOL_NAME = {"Claude Code": "Claude Code (Anthropic) CLI agent"}


def clean(text, limit=600):
    text = ANSI.sub("", str(text or ""))
    text = PASTED.sub(lambda m: f"[pasted text: {' '.join(m.group(1).split())[:160]}…]", HARNESS.sub("", text))
    text = SECRET.sub("[redacted]", text)
    text = " ".join(text.split())
    return text if len(text) <= limit else text[: limit - 1] + "…"


def rel(path):
    try:
        return str(pathlib.Path(path).resolve().relative_to(ROOT))
    except (ValueError, OSError):
        return str(path).replace(str(pathlib.Path.home()), "~")


def parse_ts(value):
    return datetime.datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(datetime.timezone.utc)


def iso(ts):
    return ts.strftime("%Y-%m-%dT%H:%M:%SZ") if ts else None


def result_text(content):
    if isinstance(content, list):
        return "\n".join(part.get("text", "") for part in content if isinstance(part, dict))
    return str(content or "")


def read_transcript(path):
    """Events in order: prompt, response, tool_call (with its result)."""
    meta = {"versions": set(), "models": set(), "permission_modes": set(), "entrypoints": set(), "branches": set(), "pr": None}
    events, results = [], {}
    for line in open(path, encoding="utf-8"):
        try:
            entry = json.loads(line)
        except json.JSONDecodeError:
            continue
        kind = entry.get("type")
        if kind == "permission-mode":
            meta["permission_modes"].add(entry.get("permissionMode"))
        if kind == "pr-link":
            meta["pr"] = entry.get("prUrl")
        message = entry.get("message")
        if kind not in ("user", "assistant") or not isinstance(message, dict) or not entry.get("timestamp"):
            continue
        ts = parse_ts(entry["timestamp"])
        if entry.get("permissionMode"):
            meta["permission_modes"].add(entry["permissionMode"])
        if kind == "assistant":
            meta["versions"].add(entry.get("version"))
            if message.get("model") and not message["model"].startswith("<"):
                meta["models"].add(message["model"])  # skip harness placeholders such as <synthetic>
            meta["entrypoints"].add(entry.get("entrypoint"))
            meta["branches"].add(entry.get("gitBranch"))
        content = message.get("content")
        origin = entry.get("origin") or {}
        if kind == "user" and (entry.get("isMeta") or origin.get("kind") not in (None, "human")):
            # Not typed by the human: sub-agent hand-backs are kept as reports, command output is dropped.
            if origin.get("handback"):
                body = origin.get("body", "").split("The report follows:", 1)[-1]
                events.append({"ts": ts, "kind": "subagent_report", "text": f"[{origin.get('name')}] " + clean(body, 400)})
            continue
        if isinstance(content, str):
            text = clean(content, 2000)
            if text and kind == "user":
                events.append({"ts": ts, "kind": "prompt", "text": text})
            continue
        for block in content or []:
            btype = block.get("type")
            if btype == "text":
                text = clean(block.get("text"), 2000)
                if text:
                    events.append({"ts": ts, "kind": "prompt" if kind == "user" else "response", "text": text})
            elif btype == "tool_use":
                events.append({"ts": ts, "kind": "tool_call", "id": block.get("id"), "name": block.get("name"), "input": block.get("input") or {}})
            elif btype == "tool_result":
                results[block.get("tool_use_id")] = (result_text(block.get("content")), bool(block.get("is_error")))
    for index, event in enumerate(events):
        if event["kind"] == "tool_call":
            event["result"], event["is_error"] = results.get(event["id"], ("", False))
        # A prompt or report belongs to the work it triggers: place it at the next tool call.
        following = next((e for e in events[index:] if e["kind"] == "tool_call"), event)
        event["at"] = following["ts"] if event["kind"] in ("prompt", "subagent_report") else event["ts"]
    for key in ("versions", "models", "permission_modes", "entrypoints", "branches"):
        meta[key] = sorted(v for v in meta[key] if v)
    return events, meta


def describe(call):
    data = call["input"]
    for key in ("description", "file_path", "skill", "query", "url"):
        if data.get(key):
            return clean(rel(data[key]) if key == "file_path" else data[key], 160)
    return clean(json.dumps(data), 160)


def evidence_of(call):
    found = []
    text = ANSI.sub("", call.get("result", ""))
    for pattern in EVIDENCE:
        for match in pattern.findall(text):
            item = " ".join(match.split())
            if item not in found:
                found.append(item)
    return found


def git_commits():
    fmt = "%x1e%h%x09%cI%x09%s"
    out = subprocess.run(["git", "log", f"--format={fmt}", "--name-only", "main..HEAD"], cwd=ROOT, capture_output=True, text=True).stdout
    commits = []
    for chunk in out.split("\x1e")[1:]:
        lines = [line for line in chunk.strip().splitlines() if line]
        sha, when, subject = lines[0].split("\t", 2)
        commits.append({"sha": sha, "ts": parse_ts(when), "subject": subject, "files": lines[1:]})
    return sorted(commits, key=lambda commit: commit["ts"])


def aggregate(events, commits, start, end):
    """The nine-section content that can be derived from events and git for one time window."""
    window = [event for event in events if start <= event["at"] < end]
    calls = [event for event in window if event["kind"] == "tool_call"]
    commands = " ".join(str(call["input"].get("command", "")) for call in calls)
    files = []
    for call in calls:
        if call["name"] in ("Write", "Edit", "NotebookEdit") and call["input"].get("file_path"):
            files.append(rel(call["input"]["file_path"]))
    in_window = [commit for commit in commits if start <= commit["ts"] < end]
    for commit in in_window:
        files.extend(commit["files"])
    files = sorted(set(files))
    instruction_files = sorted({match for call in calls for match in re.findall(
        r"(AGENTS\.md|\.claude/skills/[\w-]+/SKILL\.md|\.claude/agents/[\w-]+\.md|openspec/changes/[\w-]+/[\w/]+\.md)",
        json.dumps(call["input"]))})
    return {
        "window": window,
        "prompts": [event["text"] for event in window if event["kind"] == "prompt"],
        "reports": [event["text"] for event in window if event["kind"] == "subagent_report"],
        "responses": [event["text"] for event in window if event["kind"] == "response"],
        "tool_counts": dict(collections.Counter(call["name"] for call in calls)),
        "skills": sorted({call["input"].get("skill") for call in calls if call["name"] == "Skill"} - {None}),
        "instruction_files": instruction_files,
        "subagents": [{"description": call["input"].get("description"), "subagent_type": call["input"].get("subagent_type", "general-purpose"),
                       "run_in_background": True} for call in calls if call["name"] in ("Agent", "Task")],
        "mcp": sorted({call["name"].split("__")[1] for call in calls if call["name"].startswith("mcp__")}),
        "clis": [name for name, pattern in CLI_PATTERNS if re.search(pattern, commands)],
        "actions": [f"{iso(call['ts'])} {call['name']}: {describe(call)}" for call in calls],
        "files": files,
        "tests_files": [path for path in files if re.search(r"(Tests?\.cs|\.spec\.ts|/pages/\w+\.ts)$", path)],
        "commits": [{"sha": commit["sha"], "time_utc": iso(commit["ts"]), "subject": commit["subject"], "files": commit["files"]} for commit in in_window],
        "evidence": [{"time_utc": iso(call["ts"]), "action": describe(call), "result": evidence_of(call)} for call in calls if evidence_of(call)],
        "errors": [f"{iso(call['ts'])} {call['name']}: {describe(call)}" for call in calls if call.get("is_error")],
        # Phase time comes from what was done (tool calls) and asked (prompts), not from chat text.
        "start": min((e["ts"] for e in window if e["kind"] != "response"), default=None),
        "end": max((e["ts"] for e in window if e["kind"] == "tool_call"), default=None),
    }


def nine_sections(record_type, ident, title, ai_tool, agent, data, curated):
    return {
        "record_type": record_type, "id": ident, "title": title,
        "ai_tool": ai_tool,
        "agent": agent,
        "prompts": {"user_or_parent_prompts": data["prompts"], "subagent_reports_received": data["reports"],
                    "agent_responses": len(data["responses"]),
                    "final_response": data["responses"][-1] if data["responses"] else None},
        "skills_and_subagents": {"skills_invoked": data["skills"], "custom_instructions_followed": data["instruction_files"],
                                 "subagents_spawned": data["subagents"]},
        "tools_and_integrations": {"agent_tools_used": data["tool_counts"], "mcp_servers_used": data["mcp"], "external_clis": data["clis"]},
        "actions_and_outputs": {"goal": curated.get("goal"), "actions": data["actions"], "files_affected": data["files"],
                                "tests_created_or_changed": data["tests_files"]},
        "human_intervention": {"interventions": curated.get("human_intervention", []), "validation": curated.get("validation", [])},
        "evidence": {"start_utc": iso(data["start"]), "end_utc": iso(data["end"]), "commits": data["commits"],
                     "test_and_tool_results": data["evidence"], "tool_errors": data["errors"]},
        "outcome": {"status": curated.get("outcome", "unknown"), "rework": curated.get("rework")},
    }


def main():
    transcript = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else max(
        (pathlib.Path(p) for p in glob.glob(str(pathlib.Path.home() / ".claude/projects/*AI-in-SLDC/*.jsonl"))), key=os.path.getmtime)
    curated = json.loads((LOGS / "phases.json").read_text())
    events, meta = read_transcript(transcript)
    commits = git_commits()
    session_start = events[0]["ts"]
    far_future = datetime.datetime.max.replace(tzinfo=datetime.timezone.utc)

    # Phase boundaries: the first tool call whose description/file matches each marker.
    boundaries = []
    for phase in curated["phases"]:
        for marker in phase["markers"]:
            if marker == "@session-start":
                boundaries.append((session_start, phase["id"]))
                continue
            hit = next((e for e in events if e["kind"] == "tool_call" and marker in describe(e)), None)
            if hit:
                boundaries.append((hit["ts"], phase["id"]))
            else:
                print(f"warning: marker not found: {marker}", file=sys.stderr)
    boundaries.sort()

    ai_tool = {"name": TOOL_NAME["Claude Code"], "versions": meta["versions"], "models": meta["models"],
               "entrypoints": meta["entrypoints"]}
    main_agent = {"name": "main (orchestrator)", "type": "built-in: Claude Code main agent",
                  "execution_mode": "Agent (autonomous tool use; plan mode not used)", "permission_modes": meta["permission_modes"]}
    settings = json.loads((ROOT / ".claude/settings.json").read_text())
    mcp_config = json.loads((ROOT / ".mcp.json").read_text()).get("mcpServers", {}) if (ROOT / ".mcp.json").exists() else {}

    records = [{
        "record_type": "session", "session_id": transcript.stem, "ai_tool": ai_tool, "agent": main_agent,
        "git_branches": [subprocess.run(["git", "branch", "--show-current"], cwd=ROOT, capture_output=True, text=True).stdout.strip()],
        "pull_request": meta["pr"],
        "start_utc": iso(session_start), "end_utc": iso(events[-1]["ts"]),
        "configured": {
            "context_files": ["AGENTS.md", "CLAUDE.md (imports AGENTS.md)"],
            "skills": sorted(p.parent.name for p in (ROOT / ".claude/skills").glob("*/SKILL.md")),
            "custom_agents": sorted(p.stem for p in (ROOT / ".claude/agents").glob("*.md")),
            "hooks": {event: [h["command"].split("/")[-1].rstrip('"') for group in groups for h in group["hooks"]] for event, groups in settings.get("hooks", {}).items()},
            "mcp_servers": sorted(mcp_config),
        },
        "human_review": curated.get("human_review"),
    }]

    # Phase records (main agent).
    agent_runs = []
    for index, (start, phase_id) in enumerate(boundaries):
        end = boundaries[index + 1][0] if index + 1 < len(boundaries) else far_future
        agent_runs.append((phase_id, start, end))
    by_phase = collections.defaultdict(list)
    for phase_id, start, end in agent_runs:
        by_phase[phase_id].append((start, end))
    phase_of_time = lambda ts: next((pid for pid, start, end in agent_runs if start <= ts < end), None)

    for phase in curated["phases"]:
        merged = None
        for start, end in by_phase.get(phase["id"], []):
            data = aggregate(events, commits, start, end)
            if merged is None:
                merged = data
            else:
                for key, value in data.items():
                    if isinstance(value, list) and key != "window":
                        merged[key] = merged[key] + [v for v in value if v not in merged[key]]
                    elif isinstance(value, dict):
                        merged[key] = dict(collections.Counter(merged[key]) + collections.Counter(value))
                merged["start"] = min(filter(None, [merged["start"], data["start"]]), default=None)
                merged["end"] = max(filter(None, [merged["end"], data["end"]]), default=None)
        if merged is None:
            continue
        merged["files"] = sorted(set(merged["files"]))
        merged["tests_files"] = sorted(set(merged["tests_files"]))
        records.append(nine_sections("phase", phase["id"], phase["title"], ai_tool, main_agent, merged, phase))

    # Sub-agent records.
    subagent_dir = transcript.with_suffix("") / "subagents"
    agent_calls = {e["input"].get("description"): e for e in events if e["kind"] == "tool_call" and e["name"] in ("Agent", "Task")}
    for path in sorted(subagent_dir.glob("agent-*.jsonl")):
        info = json.loads(path.with_suffix(".meta.json").read_text()) if path.with_suffix(".meta.json").exists() else {}
        description = info.get("description", path.stem)
        sub_events, sub_meta = read_transcript(path)
        if not sub_events:
            continue
        # Sub-agents were told not to commit; commits (and their files) belong to the orchestrator phases.
        data = aggregate(sub_events, [], sub_events[0]["ts"], far_future)
        landed = [c for c in commits if c["sha"] == curated.get("subagents", {}).get(description, {}).get("landed_in_commit")]
        if landed:
            data["files"] = sorted(set(data["files"]) | set(landed[0]["files"]))
            data["tests_files"] = [f for f in data["files"] if re.search(r"(Tests?\.cs|\.spec\.ts|/pages/\w+\.ts)$", f)]
            data["actions"].append(f"work landed by the orchestrator in commit {landed[0]['sha']}: {landed[0]['subject']}")
        call = agent_calls.get(description)
        curated_sub = curated.get("subagents", {}).get(description, {})
        agent = {"name": description, "type": f"sub-agent: built-in '{info.get('agentType', 'general-purpose')}' type"
                 + (f" running custom definition {curated_sub['custom_definition']}" if curated_sub.get("custom_definition") else ""),
                 "execution_mode": f"Agent ({info.get('requestShape', 'background')}, non-interactive)",
                 "spawned_by": "main (orchestrator)", "parent_phase": phase_of_time(call["ts"]) if call else None,
                 "permission_modes": sub_meta["permission_modes"] or meta["permission_modes"]}
        sub_tool = {"name": TOOL_NAME["Claude Code"], "versions": sub_meta["versions"], "models": sub_meta["models"], "entrypoints": sub_meta["entrypoints"]}
        records.append(nine_sections("subagent", path.stem.replace("agent-", "SA-")[:11], description, sub_tool, agent, data,
                                     {"goal": data["prompts"][0][:300] if data["prompts"] else None, **curated_sub}))

    with open(LOGS / "ai-log.jsonl", "w", encoding="utf-8") as handle:
        for record in records:
            handle.write(json.dumps(record, ensure_ascii=False) + "\n")
    (LOGS / "AI_LOG.md").write_text(render(records), encoding="utf-8")
    print(f"wrote ai-logs/ai-log.jsonl and ai-logs/AI_LOG.md ({len(records)} records)")


def bullet(items, empty="none"):
    return "\n".join(f"- {item}" for item in items) if items else f"- {empty}"


def render(records):
    session, rest = records[0], records[1:]
    tool = session["ai_tool"]
    lines = ["# AI traceability log", "",
             "Generated by `tools/build_ai_log.py` from the Claude Code session transcript, the sub-agent transcripts and git.",
             "Machine-readable version: `ai-log.jsonl` (same records). Live per-event hook log: `ai-interactions.jsonl` / `AI_INTERACTIONS.md`.",
             "Only goal, human intervention, validation and outcome come from the hand-curated `phases.json`.", "",
             "## Session", "",
             "| Field | Value |", "| --- | --- |",
             f"| AI tool | {tool['name']} {', '.join(tool['versions'])} ({', '.join(tool['entrypoints'])}) |",
             f"| Model | {', '.join(tool['models'])} |",
             f"| Main agent | {session['agent']['type']}; mode: {session['agent']['execution_mode']}; permission mode: {', '.join(session['agent']['permission_modes'])} |",
             f"| Session | `{session['session_id']}`, {session['start_utc']} → {session['end_utc']} |",
             f"| Branch / PR | {', '.join(session['git_branches'])} / {session['pull_request']} |",
             f"| Skills configured | {', '.join(session['configured']['skills'])} |",
             f"| Custom agents | {', '.join(session['configured']['custom_agents'])} |",
             f"| Hooks | {'; '.join(f'{event}: {', '.join(cmds)}' for event, cmds in session['configured']['hooks'].items())} |",
             f"| MCP servers configured | {', '.join(session['configured']['mcp_servers']) or 'none'} |",
             f"| Human review of the AI output | **{session['human_review']['status']}** {('by ' + session['human_review']['reviewer']) if session['human_review'].get('reviewer') else ''} |",
             "", "## Overview", "",
             "| ID | Type | Title | Time (UTC) | Agent | Commits | Outcome |", "| --- | --- | --- | --- | --- | --- | --- |"]
    for record in rest:
        evidence = record["evidence"]
        lines.append(f"| {record['id']} | {record['record_type']} | {record['title']} | {(evidence['start_utc'] or '')[5:16]} → {(evidence['end_utc'] or '')[11:16]} | "
                     f"{record['agent']['name']} | {', '.join(c['sha'] for c in evidence['commits']) or '–'} | {record['outcome']['status']} |")
    for record in rest:
        sections = record
        lines += ["", f"## {record['id']}: {record['title']}", "",
                  f"**1. AI tool & model:** {sections['ai_tool']['name']} {', '.join(sections['ai_tool']['versions'])}, model {', '.join(sections['ai_tool']['models'])}", "",
                  f"**2. Agent:** {sections['agent']['name']} ({sections['agent']['type']}); execution mode: {sections['agent']['execution_mode']}; permission mode: {', '.join(sections['agent']['permission_modes'])}"
                  + (f"; spawned by {sections['agent']['spawned_by']} in {sections['agent']['parent_phase']}" if sections['agent'].get('spawned_by') else ""), "",
                  "**3. Prompts & interactions:**", bullet([f"“{p[:500]}”" for p in sections['prompts']['user_or_parent_prompts']], "no new user prompt in this phase (continued from the previous instruction)"),
                  bullet([f"sub-agent report received: {r[:300]}" for r in sections['prompts']['subagent_reports_received']], "sub-agent reports received: none"),
                  f"- agent responses: {sections['prompts']['agent_responses']}", "",
                  "**4. Skills, instructions & sub-agents:**",
                  f"- skills invoked (Skill tool): {', '.join(sections['skills_and_subagents']['skills_invoked']) or 'none'}",
                  f"- instruction / spec files used by tool calls: {', '.join(sections['skills_and_subagents']['custom_instructions_followed']) or 'none beyond AGENTS.md (loaded as session context via CLAUDE.md)'}",
                  f"- sub-agents spawned: {', '.join(s['description'] + ' [' + s['subagent_type'] + ']' for s in sections['skills_and_subagents']['subagents_spawned']) or 'none'}", "",
                  "**5. Tools & integrations:**",
                  f"- agent tools: {', '.join(f'{k}×{v}' for k, v in sorted(sections['tools_and_integrations']['agent_tools_used'].items()))}",
                  f"- MCP servers used: {', '.join(sections['tools_and_integrations']['mcp_servers_used']) or 'none'}",
                  f"- external CLIs: {', '.join(sections['tools_and_integrations']['external_clis']) or 'none'}", "",
                  "**6. AI actions & outputs:**",
                  f"- goal: {sections['actions_and_outputs']['goal']}",
                  "<details><summary>actions (" + str(len(sections['actions_and_outputs']['actions'])) + ")</summary>", "",
                  bullet(sections['actions_and_outputs']['actions']), "", "</details>", "",
                  f"- files affected: {', '.join(f'`{f}`' for f in sections['actions_and_outputs']['files_affected']) or 'none'}",
                  f"- tests created/changed: {', '.join(f'`{f}`' for f in sections['actions_and_outputs']['tests_created_or_changed']) or 'none'}", "",
                  "**7. Human intervention & validation:**",
                  bullet([f"[{i['by']}] {i['what']}" for i in sections['human_intervention']['interventions']], "none recorded"),
                  bullet([f"validated: {v}" for v in sections['human_intervention']['validation']], "no validation recorded"), "",
                  "**8. Execution evidence:**",
                  f"- time: {sections['evidence']['start_utc']} → {sections['evidence']['end_utc']}",
                  bullet([f"commit `{c['sha']}` {c['time_utc']}: {c['subject']}" for c in sections['evidence']['commits']], "no commits in this phase"),
                  bullet([f"{e['time_utc']} {e['action']}: {'; '.join(e['result'])}" for e in sections['evidence']['test_and_tool_results']], "no test output captured"),
                  bullet([f"tool error: {e}" for e in sections['evidence']['tool_errors']], "tool errors: none"), "",
                  f"**9. Outcome:** {sections['outcome']['status']}" + (f": {sections['outcome']['rework']}" if sections['outcome'].get('rework') else "")]
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    main()
