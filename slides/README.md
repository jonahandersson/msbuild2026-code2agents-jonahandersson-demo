# Slides

The talk uses **3 slides total**. Demo-only talks should not be slide-heavy.

## Brand

- **Primary color:** Cobalt blue `#0047AB`
- **Accent:** White and a single warm accent (amber `#FFC107`) for callouts
- **Typography:** Segoe UI for body, Segoe UI Display for titles
- **Avoid:** Stock photos, generic icons, lots of bullets

## Slide 1 — Title

```
From Code to Agents
Build Production MCP Servers on Azure Functions

Jonah Andersson  ·  Microsoft Build 2026
@<handle>
```

Cobalt blue background, white text. Large title (72pt+). One line of subtitle.

## Slide 2 — The problem + architecture

Left side: one sentence in big type.

> "Agents are stuck in pilot. Tools are why."

Right side: the architecture diagram showing:

```
[Foundry model]  ←→  [DevOps Agent]
                          ↓ MCP
                  [Azure Function = MCP server]
                          ↓ Managed Identity
                  [Azure DevOps Repos + Pipelines]
```

This is the *only* diagram in the talk. Audience refers back to it mentally for the rest of the demo.

## Slide 3 — Takeaways

Three numbered points, cobalt blue header, the rest in body text:

```
1. MCP turns your tools into a contract.

2. Managed Identity all the way down.

3. Azure Functions is the right host for production MCP.
```

QR code below pointing to this repo. Speaker handle + lounge location.

---

## Recommended slide tool

PowerPoint (Microsoft Build crowd, native rendering on the conference deck system). Save **both `.pptx` and an exported `.pdf`** — bring the PDF on a USB stick as a fallback.

Place the final `.pptx` in this folder as `build2026-mcp.pptx` and commit it; small deck = small repo.
