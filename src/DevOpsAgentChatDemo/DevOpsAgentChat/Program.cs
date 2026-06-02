using DevOpsAgentChat;
using DevOpsAgentChat.Components;
using DevOpsAgentChat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddAuthorization();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRazorPages();
builder.Services.AddSingleton<AgentService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapRazorPages();

app.MapPost("/api/agentchat", async (AgentChatRequest? req, AgentService agentSvc, ILogger<Program> logger) =>
{
    var prompt = req?.Message?.Trim();
    if (string.IsNullOrWhiteSpace(prompt))
    {
        return Results.BadRequest("message is required");
    }

    try
    {
        var reply = await agentSvc.RunAsync(prompt);
        return Results.Text(string.IsNullOrWhiteSpace(reply) ? "(no response)" : reply);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Agent turn failed");
        return Results.Problem(
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway,
            title: "Agent error");
    }
})
.WithName("AgentChat");

app.Run();
