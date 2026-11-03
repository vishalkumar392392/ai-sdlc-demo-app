#!/usr/bin/env python3
"""PreToolUse hook on Bash: block `git commit -m` messages that do not follow
the project format `<type>(<scope>): <summary>` + a `Why:` line (see AGENTS.md)."""
import json, re, sys

HEADER = re.compile(r"^(feat|fix|test|docs|chore|refactor)(\([a-z0-9-]+\))?: .{1,72}$")

try:
    event = json.loads(sys.stdin.read(), strict=False)
except json.JSONDecodeError:
    sys.exit(0)  # never block on unreadable hook input
command = (event.get("tool_input") or {}).get("command", "")
if not re.search(r"\bgit\s+commit\b", command) or ("--amend" in command and "--no-edit" in command):
    sys.exit(0)

match = re.search(r"-m\s+\"\$\(cat <<'?EOF'?\n(.*?)\nEOF", command, re.S) or re.search(r"-m\s+([\"'])(.*?)\1", command, re.S)
if not match:
    sys.exit(0)  # editor or -F commit; nothing to check here
message = match.group(match.lastindex)
lines = message.strip().splitlines()
problems = []
if not lines or not HEADER.match(lines[0].strip()):
    problems.append("first line must be '<type>(<scope>): <summary>' (types: feat fix test docs chore refactor, <=72 chars)")
if not any(line.startswith("Why:") for line in lines[1:]):
    problems.append("body must contain a 'Why:' line explaining the reason for the change")
if problems:
    print("Commit message rejected by validate_commit hook:\n- " + "\n- ".join(problems), file=sys.stderr)
    sys.exit(2)
sys.exit(0)
