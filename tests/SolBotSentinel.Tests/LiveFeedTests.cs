using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using SolBotSentinel.Domain;
using SolBotSentinel.Rules;

namespace SolBotSentinel.Tests;

/// <summary>
/// The contract with the engine. The fixture is real output of the engine's feed builder: the
/// trend flipped short an hour of grace ago; one bot is still open on the old trend, one broke
/// and never opened, one opened and was stopped out, and one is disabled.
/// </summary>
public class LiveFeedTests : IClassFixture<WebApplicationFactory<Program>>
{
	private const string FeedToken = "feed-token-for-tests";
	private const string ApiToken = "api-token-for-tests";
	private readonly WebApplicationFactory<Program> _factory;
	private readonly StubEngine _engine = new();

	public LiveFeedTests(WebApplicationFactory<Program> factory)
	{
		_factory = factory.WithWebHostBuilder(b => b
			.UseSetting("Watch:Source", "http")
			.UseSetting("Watch:FeedUrl", "http://engine.test/sentinel-feed")
			.UseSetting("Watch:FeedToken", FeedToken)
			.UseSetting("Watch:ApiToken", ApiToken)
			.UseSetting("Watch:PollMs", "20")
			.UseSetting("Watch:FlipGraceMs", "100")
			.ConfigureTestServices(services => services
				.Configure<HttpClientFactoryOptions>(Options.DefaultName, o =>
					o.HttpMessageHandlerBuilderActions.Add(h => h.PrimaryHandler = _engine))));
	}

	private async Task<List<Alert>> AlertsOnceTrendWatcherHasRun()
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new("Bearer", ApiToken);
		List<Alert> alerts = [];
		for (var i = 0; i < 100; i++)
		{
			alerts = await client.GetFromJsonAsync<List<Alert>>("/alerts/history") ?? [];
			if (alerts.Any(a => a.Watcher == "trend")) break;
			await Task.Delay(50);
		}
		return alerts;
	}

	[Fact]
	public async Task Reads_the_engine_feed_and_flags_exactly_the_two_bots_that_did_not_act()
	{
		var alerts = await AlertsOnceTrendWatcherHasRun();
		await Task.Delay(200);
		var trend = (await AlertsOnceTrendWatcherHasRun()).Where(a => a.Watcher == "trend").ToList();

		Assert.Equal(2, trend.Count);
		Assert.Contains(trend, a => a.BotId == "tallboy:AAAAAA" && a.Reason == TrendRules.FlipNotClosed);
		Assert.Contains(trend, a => a.BotId == "shorty:BBBBBB" && a.Reason == TrendRules.FlipNotOpened);
		Assert.NotEmpty(alerts);
	}

	[Fact]
	public async Task A_feed_without_prices_raises_no_price_alerts()
	{
		var alerts = await AlertsOnceTrendWatcherHasRun();
		Assert.DoesNotContain(alerts, a => a.Watcher == "risk");
	}

	[Fact]
	public async Task Sends_the_feed_token_on_every_read()
	{
		await AlertsOnceTrendWatcherHasRun();
		Assert.True(_engine.Authorized > 0);
		Assert.Equal(0, _engine.Unauthorized);
	}

	[Fact]
	public async Task Alerts_need_the_api_token_when_one_is_set()
	{
		var anonymous = _factory.CreateClient();
		var response = await anonymous.GetAsync("/alerts");
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

		var health = await anonymous.GetAsync("/health");
		Assert.True(health.IsSuccessStatusCode);
	}

	private sealed class StubEngine : HttpMessageHandler
	{
		private readonly string _body = File.ReadAllText(
			Path.Combine(AppContext.BaseDirectory, "engine-feed.fixture.json"));

		public int Authorized;
		public int Unauthorized;

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken ct)
		{
			var ok = request.Headers.Authorization?.Scheme == "Bearer"
				&& request.Headers.Authorization.Parameter == FeedToken;
			if (!ok)
			{
				Interlocked.Increment(ref Unauthorized);
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
			}
			Interlocked.Increment(ref Authorized);
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
			});
		}
	}
}
