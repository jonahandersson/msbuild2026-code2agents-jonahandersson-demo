# DevOpsAgentChat Local Development

## Prerequisites
- .NET 10 SDK
- MCP backend (DevOps Agent) running locally (e.g., Azure Functions or ASP.NET)

## Running Locally
1. Start the MCP backend (ensure it listens on http://localhost:7071/api/agent).
2. In this folder, run:
   ```sh
   dotnet run
   ```
3. Open your browser to https://localhost:5001/chat
4. Chat messages will be forwarded to the MCP backend as configured in `appsettings.Development.json`.

## Troubleshooting
- If you see connection errors, ensure the MCP backend is running and accessible.
- Update `MCP_AGENT_ENDPOINT` in `appsettings.Development.json` if your backend runs on a different port or path.
