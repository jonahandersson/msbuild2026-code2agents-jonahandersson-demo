# Demo script — 20 minutes

> **Talk title:** From Code to Agents: Build Production MCP Servers on Azure Functions
> **Venue:** Microsoft Build 2026
> **Format:** 20-minute demo-only talk
> **Audience:** L300–400 .NET / Azure developers

## How to read this

Each row is one **beat**. The middle column is what's on the projector. The right column is what comes out of your mouth — one sentence, not a script. If you memorize the right column too tightly you'll sound stiff. Read it as a *cue card*, not a transcript.

Open both repositories before going on stage:
- VS Code window 1: this repo
- VS Code window 2: a fresh tab showing your Azure DevOps PR list (refreshable)
- A browser tab: Foundry portal showing the agent

Pre-warm the Foundry model with a throwaway call ~2 minutes before going on. First-call latency is real.

---

## The 20 minutes

| Beat | T+    | On screen                                         | One-liner                                                                                       |
|------|-------|---------------------------------------------------|-------------------------------------------------------------------------------------------------|
| 1    | 00:00 | **Title slide** (cobalt blue)                     | "Hi! Hej! Mabuhay! I'm Jonah. 20 minutes, zero slides after this one, lots of code."            |
| 2    | 00:15 | **The problem** slide                             | "Every team has agents in pilot. Almost none in production. Tools are why."                     |
| 3    | 01:00 | **Architecture** slide (cobalt blue diagram)      | "Function = MCP server. Agent = MCP client. The protocol is the contract."                      |
| 4    | 02:00 | `git checkout step-1-mcp-tool` → `DeploymentTools.cs` | "Three tools, three attributes. That's the whole MCP API."                                  |
| 5    | 04:00 | Run `func start` in terminal pane                 | "It's just an Azure Function. Same hosting, same CI/CD, same observability."                    |
| 6    | 05:00 | Open MCP Inspector against `/runtime/webhooks/mcp/sse` | "The extension exposed our tools as standard MCP. Any MCP-compatible agent can call them now."  |
| 7    | 07:30 | `git checkout step-2-production` → `AzureDevOpsClient.cs` | "Production hardening. Watch the diff."                                                  |
| 8    | 08:30 | Highlight `DefaultAzureCredential` + Bicep        | "No PAT. No connection string. Managed Identity, same code, dev and prod."                      |
| 9    | 10:00 | Show App Insights traces (live)                   | "Every tool call shows up here. That's a real audit trail for what your agents are doing. (Cloud Aspire dashboard is not deployed in this environment, so use App Insights for observability.)"      |
| 10   | 11:30 | `git checkout step-3-agent-azdo` → `DevOpsAgent/Program.cs` | "Now the *client* side. Microsoft Agent Framework, GA in April."                       |
| 11   | 13:00 | Run `dotnet run`, paste the rollback prompt       | "Watch the agent decide. I'm not telling it which tools to call."                               |
| 12   | 14:00 | Function logs in pane 2 light up tool by tool     | "get_recent_deployments... diagnose_deployment... create_rollback_pr..."                        |
| 13   | 15:30 | **Switch to Azure DevOps tab, refresh**           | *(pause for 3 seconds — let them see it)* "There's the PR. The reasoning is in the description."|
| 14   | 16:30 | Show the PR description and reviewers             | "Auditable. Reviewable. Your existing PR policies still apply. The agent didn't go rogue."      |
| 15   | 18:00 | **Takeaways slide** (cobalt blue)                 | Read the three takeaways verbatim.                                                              |
| 16   | 19:30 | **QR code** to this repo                          | "Repo's on the QR code. Come find me at the speaker lounge."                                    |
| 17   | 20:00 | END                                               | *(silence, smile, take a bow)*                                                                  |

---

## The three takeaways slide

```
1. MCP turns your tools into a contract.
   One attribute (McpToolTrigger) replaces a dozen brittle SDKs per agent.

2. Managed Identity all the way down.
   No PATs, no secrets, same code in dev and prod.
   This is the line between "demo" and "production."

3. Azure Functions is the right host for production MCP.
   Scale to zero. Entra-backed auth. Your existing CI/CD just works.
```

---

## Rehearsal checklist

Do these in order, three times before the talk. Time yourself.

- [ ] Open both VS Code windows + Azure DevOps tab + Foundry tab.
- [ ] Pre-warm Foundry with `dotnet run -- "ping"` from `DevOpsAgent`.
- [ ] Switch to `step-1-mcp-tool` branch.
- [ ] `func start` and confirm it boots.
- [ ] Switch to `step-2-production`, restart `func`.
- [ ] Switch to `step-3-agent-azdo`.
- [ ] Run the agent, paste the prompt, watch the PR appear.
- [ ] Total time: must be **under 18 minutes** in rehearsal. (You'll naturally slow down on stage.)

---

## Fallback decision tree

```
Wi-Fi solid + Foundry responding?
└── Yes → run full demo with real Azure DevOps PR creation
└── No
    ├── Foundry down? → set DEMO_MODE=cached, agent replays canned tool responses
    │                   (LLM call still real if Foundry is reachable at all)
    └── Everything down? → "Let me show you what this looks like end-to-end"
                           play demo/recording.mp4 (you record this yourself in rehearsal)
```

The audience forgives Wi-Fi. They don't forgive panic. Whichever path you take, **say nothing about the fallback**. The recording shows the same demo. They can't tell.

---

## Pre-stage logistics

- Slide 1 title font: 60pt minimum (back row test)
- Code font in VS Code: 16pt minimum, 18pt better
- Terminal font: 16pt minimum
- Theme: GitHub Dark Default (high contrast under stage lights)
- Hide the activity bar in VS Code (`View → Appearance → Activity Bar`)
- Disable autosave-on-focus-loss prompts
- Disable any notifications (Slack, Mail, Teams) — put your laptop in Do Not Disturb
- Wired backup network? Ask the conference AV team. Build typically has a wired option at the speaker desk.

---

## Q&A — likely questions

Pre-cook these answers so you don't get caught flat-footed.

**Q: Why Functions instead of Container Apps for the MCP server?**
A: Either works. Functions wins on three things for MCP: the built-in MCP extension means you write less boilerplate; scale-to-zero is free for sporadic agent traffic; and the `/runtime/webhooks/mcp` endpoint comes Entra-authenticated out of the box. Container Apps is a better fit if you need a custom runtime, GPU, or sidecars.

**Q: How does the agent authenticate to the MCP server?**
A: Two options. (1) Entra-backed: the agent's identity hits `/runtime/webhooks/mcp` with a bearer token, Functions validates it via the MCP extension's built-in auth. (2) Function key: simpler, fine for trusted internal agents. We use Entra in this demo.

**Q: Is this actually safe? An agent opening PRs in your repo?**
A: It opens a PR. It doesn't merge. Your branch policies still apply — required reviewers, build validation, all of it. The agent is a junior dev that types fast. A human still approves.

**Q: What about prompt injection on the MCP responses?**
A: Real concern. Foundry's Content Safety + Cross-Prompt Injection Attack (XPIA) detection is the first line. The second is: never let MCP tool responses contain instructions to the agent — sanitize at the tool boundary. Treat tool output as data, not as prompt.

**Q: .NET 10 — isn't that still preview?**
A: Yes, at Build 2026. .NET 9 is the LTS option and the code in this repo runs on it with no changes — see the README. We're showing .NET 10 because the Functions team is already producing isolated-worker images for it.
