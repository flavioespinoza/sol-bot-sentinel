using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SolBotSentinel.Domain;
using SolBotSentinel.Rules;

namespace SolBotSentinel.Tests;

/// <summary>Starts the real service against the simulated market and reads it over HTTP.</summary>
public class ServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly HttpClient _client;

	public ServiceTests(WebApplicationFactory<Program> factory)
	{
		_client = factory
			.WithWebHostBuilder(b => b
				.UseSetting("Watch:PollMs", "20")
				.UseSetting("Watch:FlipGraceMs", "100"))
			.CreateClient();
	}

	[Fact]
	public async Task Health_answers_ok()
	{
		var response = await _client.GetAsync("/health");
		Assert.True(response.IsSuccessStatusCode);
	}

	[Fact]
	public async Task Each_watcher_raises_its_alert_once()
	{
		static bool Has(List<Alert> alerts, string bot, string reason) =>
			alerts.Any(a => a.BotId == bot && a.Reason == reason);

		List<Alert> alerts = [];
		for (var i = 0; i < 100; i++)
		{
			alerts = await _client.GetFromJsonAsync<List<Alert>>("/alerts/history") ?? [];
			if (Has(alerts, "tallboy", RiskRules.PriceStop)
				&& Has(alerts, "shorty", ConductRules.CapExceeded)
				&& Has(alerts, "tallboy", TrendRules.FlipNotClosed)) break;
			await Task.Delay(50);
		}

		Assert.Single(alerts, a => a.BotId == "tallboy" && a.Reason == RiskRules.PriceStop);
		Assert.Single(alerts, a => a.BotId == "shorty" && a.Reason == ConductRules.CapExceeded);
		Assert.Single(alerts, a => a.BotId == "tallboy" && a.Reason == TrendRules.FlipNotClosed);

		// Keep polling past the trips: the count must not grow.
		await Task.Delay(300);
		var later = await _client.GetFromJsonAsync<List<Alert>>("/alerts/history") ?? [];
		Assert.Equal(3, later.Count);
	}
}
