# SSO & Entra ID Setup for DevOpsAgentChat

## 1. Register Entra ID Apps
- Go to Azure Portal → Entra ID → App registrations
- Register two apps:
  - **DevOpsAgentChat UI** (Web)
  - **DevOpsAgent Backend** (Web/API)
- For each app:
  - Set the redirect URI to `https://<webapp-name>.azurewebsites.net/signin-oidc`
  - Note the Application (client) ID and Directory (tenant) ID
  - Add your Azure login account as a user (or assign to a group)

## 2. Configure appsettings.json
- Update `AzureAd` section with your TenantId, ClientId, and Domain
- Set `CallbackPath` to `/signin-oidc`

## 3. Update Bicep/infra
- Output the required redirect URIs for both apps
- (Optional) Automate app registration with az cli or Bicep

## 4. Deploy with azd
- Scaffold `azure.yaml` and `azure.json` for azd
- Run `azd init` and select your dryrun resource group
- Run `azd up` to provision and deploy

## 5. Validate
- Access the UI and backend in the browser
- Login with your Azure account
- Confirm SSO is enforced

---

For more details, see Microsoft.Identity.Web docs and azd documentation.
