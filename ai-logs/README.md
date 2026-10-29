# AI interaction logs

All logs are produced automatically from Claude Code itself. The only hand-written input is `phases.json`, which holds judgement calls a tool cannot make: goal, human intervention, validation and outcome.

| File | Produced by | When | Granularity |
| --- | --- | --- | --- |
| `ai-interactions.jsonl` | `.claude/hooks/log_interaction.py` (hooks in `.claude/settings.json`) | live, on every prompt, tool call and agent stop | one line per event |
| `AI_INTERACTIONS.md` | same hook | on every Stop / SubagentStop | readable table of the JSONL |
| `ai-log.jsonl` | `tools/build_ai_log.py` (run by the Stop hook, or by hand) | after every turn | one record per work phase and per sub-agent run, plus one session record |
| `AI_LOG.md` | same script | after every turn | **readable summary; start here** |
| `AI_TRANSCRIPT.md` | `tools/export_ai_transcript.py` | on demand | every prompt, response and tool call, including steps before the hooks existed |
| `phases.json` | hand-curated | when a phase ends | goal, human intervention, validation, outcome, rework, and the pending human-review sign-off |

Secrets, tokens and email addresses are redacted, and model reasoning is never logged.

## Where each required detail lives

| # | Requirement | `ai-log.jsonl` field (phase / sub-agent records) | Live hook field (`ai-interactions.jsonl`) | Source |
| --- | --- | --- | --- | --- |
| 1 | AI tool & model | `ai_tool.name`, `ai_tool.versions`, `ai_tool.models` | `ai_tool{name,version,model}` | transcript (`version`, `message.model`) |
| 2 | Agent type & execution mode | `agent.type` (built-in main agent / sub-agent running a custom definition), `agent.execution_mode`, `agent.permission_modes`, `agent.parent_phase` | `agent{name,type,mode}`, `permission_mode` | transcript, sub-agent `meta.json`, hook input |
| 3 | Prompts & interactions | `prompts.user_or_parent_prompts` (only human-typed prompts, from `origin.kind == human`), `prompts.subagent_reports_received`, `prompts.agent_responses`, `prompts.final_response` | `event == UserPromptSubmit` → `summary` | transcript |
| 4 | Skills & sub-agents | `skills_and_subagents.skills_invoked`, `.custom_instructions_followed`, `.subagents_spawned` | `skill`, `agent` | transcript tool calls |
| 5 | Tools & integrations | `tools_and_integrations.agent_tools_used`, `.mcp_servers_used`, `.external_clis` | `tool`, `mcp_server` | transcript tool calls and commands |
| 6 | AI actions & outputs | `actions_and_outputs.actions`, `.files_affected`, `.tests_created_or_changed` | `summary`, `files` | transcript plus git |
| 7 | Human intervention | `human_intervention.interventions` (`by: user` or `by: orchestrator`), `.validation`; session `human_review` | — | `phases.json` (curated) |
| 8 | Execution evidence | `evidence.start_utc/end_utc`, `.commits` (sha, time, files), `.test_and_tool_results` (test totals, mutation score, CI, HTTP results), `.tool_errors` | `ts`, `session`, `outcome` | transcript tool results plus git |
| 9 | Outcome | `outcome.status` (successful / partial / rework), `outcome.rework` | `outcome` (ok / error / completed) | `phases.json` (curated), tool results |

## Who did what

- **`by: user`** is the human participant: the prompts they typed and the decisions they made.
- **`by: orchestrator`** is the main Claude Code agent checking its own output or a sub-agent's. Examples are diff reviews, break-it experiments and re-running tests.
- **`human_review`** in `phases.json` is the participant's own sign-off. It stays **pending** until they have reviewed the PR. It is not filled in on their behalf.

## Regenerate

```bash
python3 tools/build_ai_log.py          # newest session transcript for this project
python3 tools/export_ai_transcript.py ~/.claude/projects/<project>/<session>.jsonl
```
