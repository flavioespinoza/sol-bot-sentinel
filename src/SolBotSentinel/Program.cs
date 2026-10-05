using Microsoft.Extensions.Options;
using SolBotSentinel;
using SolBotSentinel.Domain;
using SolBotSentinel.Sources;
using SolBotSentinel.Watchers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<WatchOptions>(builder.Configuration.GetSection("Watch"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AlertStore>();

// Both sources are registered and the choice is made when a watcher first asks, so the
// Source setting can come from any configuration provider, including the environment.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<HttpFeed>();
builder.Services.AddSingleton<SimulatedMarket>();
builder.Services.AddSingleton<IPositionSource>(sp => UseHttp(sp)
	? sp.GetRequiredService<HttpFeed>()
	: sp.GetRequiredService<SimulatedMarket>());
builder.Services.AddSingleton<ITrendSource>(sp => UseHttp(sp)
	? sp.GetRequiredService<HttpFeed>()
	: sp.GetRequiredService<SimulatedMarket>());

// Each watcher is one class and one line here. A new watcher is added the same way.
builder.Services.AddHostedService<RiskWatcher>();
builder.Services.AddHostedService<ConductWatcher>();
builder.Services.AddHostedService<TrendWatcher>();

var app = builder.Build();

app.MapGet("/health", (
	AlertStore alerts,
	IPositionSource source,
	TimeProvider clock,
	IOptions<WatchOptions> options) => Results.Ok(new
{
	status = WatcherHealth.Status(alerts.LastPoll, clock.GetUtcNow(), options.Value.PollMs),
	source = source.Name,
	lastPoll = alerts.LastPoll,
	activeAlerts = alerts.Active.Count,
}));

// An alert says which way the trend went, so when a token is configured the alerts need it.
var alertRoutes = app.MapGroup("/alerts").AddEndpointFilter(async (context, next) =>
{
	var expected = context.HttpContext.RequestServices
		.GetRequiredService<IOptions<WatchOptions>>().Value.ApiToken;
	var header = context.HttpContext.Request.Headers.Authorization.ToString();
	return ApiAuth.Allowed(header, expected) ? await next(context) : Results.Unauthorized();
});
alertRoutes.MapGet("", (AlertStore alerts) => Results.Ok(alerts.Active));
alertRoutes.MapGet("/history", (AlertStore alerts) => Results.Ok(alerts.History));

app.Run();

static bool UseHttp(IServiceProvider sp) =>
	sp.GetRequiredService<IOptions<WatchOptions>>().Value.Source == "http";

public partial class Program;
