# DevOpsAgentChat Admin Portal

This web app provides a secure chat interface for DevOps/admins to interact with MCP agents, manage deployments, and monitor operations. It is deployed alongside eShop (ShopWeb) and the agent backend in the same Azure resource group.

## Features
- Real-time chat with DevOps agents (MCP)
- Secure, scalable Azure Web App deployment
- Easily extendable for additional admin features

## Usage
- Access the portal at the deployed web app URL (see Azure Portal or deployment output)
- Authenticate (if enabled)
- Start chatting with agents for deployment, rollback, or status operations

## Architecture
- DevOpsAgentChat UI (Blazor Server) → `/api/agentchat` → MCP backend (Function App/Web App)
- Both apps are deployed as separate Azure Web Apps in the same resource group as eShop
- MCP endpoint is configurable via app settings

## Security
- (Recommended) Enable Azure AD authentication for both UI and backend
- Restrict access to authorized DevOps/admin users only

---

For deployment and configuration, see `../infra/DEPLOYMENT.md`.
