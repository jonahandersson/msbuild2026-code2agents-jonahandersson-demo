# Demo materials

This folder is everything *adjacent* to the code — the things you'll need on stage but don't ship in the Function app.

## Contents

| File | Purpose |
|---|---|
| `DEMO-SCRIPT.md` | 20-minute beat-by-beat speaker notes |
| `cached-responses/*.json` | Canned MCP tool responses for Wi-Fi fallback |
| `recording.mp4` | **You record this in rehearsal** — the absolute last-resort fallback |

## Recording the fallback video

During your final rehearsal, run the **whole demo end-to-end** with screen recording on. Use:

- macOS: `Cmd+Shift+5`, full screen
- Windows: Xbox Game Bar (`Win+G`) → Record
- Linux: OBS

Trim it to 90 seconds. The point is to show *what the audience would have seen* — the agent reasoning, the tool calls, the PR appearing — not to be a polished produced video.

Save it as `demo/recording.mp4`. The `.gitignore` keeps it out of git so the repo stays clean for attendees.

## Using the cached responses

Set `DEMO_MODE=cached` when running the agent. The MCP server's `FakeDeploymentService` already returns the same data shapes as these JSON files — keep both in sync if you change the story.

If you change the failure scenario (e.g. swap "schema migration timeout" for something else), update **all three** files plus `FakeDeploymentService.cs` together. They tell the same story.
