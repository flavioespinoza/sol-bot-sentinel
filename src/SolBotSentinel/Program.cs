using Microsoft.Extensions.Options;
using SolBotSentinel;
using SolBotSentinel.Domain;
using SolBotSentinel.Sources;
using SolBotSentinel.Watchers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<WatchOptions>(builder.Configuration.GetSection("Watch"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AlertStore>();

var watch = builder.Configuration.GetSection("Watch").Get<WatchOptions>() ?? new WatchOptions();
if (watch.Source == "http")
{
	builder.Services.AddHttpClient<IPositionSource, HttpPositionSource>();
	builder.Services.AddHttpClient<ITrendSource, HttpTrendSource>();
}
else
{
	builder.Services.AddSingleton<SimulatedMarket>();
	builder.Services.AddSingleton<IPositionSource>(sp => sp.GetRequiredService<SimulatedMarket>());
	builder.Services.AddSingleton<ITrendSource>(sp => sp.GetRequiredService<SimulatedMarket>());
}

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

app.MapGet("/alerts", (AlertStore alerts) => Results.Ok(alerts.Active));
app.MapGet("/alerts/history", (AlertStore alerts) => Results.Ok(alerts.History));

app.Run();

public partial class Program;
