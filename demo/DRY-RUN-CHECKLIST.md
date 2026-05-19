dotnet run --project demo/shop-api-seed/src/ShopWeb/ShopWeb.csproj
curl.exe -N http://localhost:7071/runtime/webhooks/mcp/sse
az functionapp keys list -g <rg> -n <app> --query systemKeys.mcp_extension -o tsv
az functionapp restart -g <rg> -n <app>

