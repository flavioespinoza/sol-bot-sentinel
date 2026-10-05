using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SolBotSentinel.Domain;
using SolBotSentinel.Rules;

namespace SolBotSentinel.Tests;

public class NotifyTests
{
	private static readonly Alert Sample = new()
	{
		BotId = "tallboy:AAAAAA",
		Watcher = "trend",
		Reason = TrendRules.FlipNotClosed,
		Detail = "trend short, tallboy:AAAAAA is OPEN",
		At = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
	};

	private static PushoverNotifier Notifier(Recorder recorder, string token = "app", string user = "usr") =>
		new(
			new OneClientFactory(recorder),
			Options.Create(new WatchOptions
			{
				PushoverUrl = "http://pushover.test/messages",
				PushoverToken = token,
				PushoverUser = user,
			}),
			NullLogger<PushoverNotifier>.Instance);

	[Fact]
	public async Task Sends_the_alert_with_the_token_the_user_and_high_priority()
	{
		var recorder = new Recorder(HttpStatusCode.OK);
		await Notifier(recorder).NotifyAsync(Sample, CancellationToken.None);

		Assert.Equal(1, recorder.Calls);
		Assert.Contains("token=app", recorder.LastBody);
		Assert.Contains("user=usr", recorder.LastBody);
		Assert.Contains("priority=1", recorder.LastBody);
		Assert.Contains("flip-not-closed", recorder.LastBody);
	}

	[Fact]
	public async Task Is_disabled_and_silent_without_a_token_or_a_user()
	{
		var recorder = new Recorder(HttpStatusCode.OK);
		var noToken = Notifier(recorder, token: "");
		var noUser = Notifier(recorder, user: "");

		Assert.False(noToken.Enabled);
		Assert.False(noUser.Enabled);
		await noToken.NotifyAsync(Sample, CancellationToken.None);
		await noUser.NotifyAsync(Sample, CancellationToken.None);
		Assert.Equal(0, recorder.Calls);
	}

	[Fact]
	public async Task Never_throws_when_the_push_fails()
	{
		await Notifier(new Recorder(HttpStatusCode.InternalServerError)).NotifyAsync(Sample, CancellationToken.None);
		await Notifier(new Recorder(null)).NotifyAsync(Sample, CancellationToken.None);
	}

	private sealed class OneClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
	}

	private sealed class Recorder(HttpStatusCode? status) : HttpMessageHandler
	{
		public int Calls;
		public string LastBody = "";

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken ct)
		{
			Interlocked.Increment(ref Calls);
			LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
			if (status is null) throw new HttpRequestException("unreachable");
			return new HttpResponseMessage(status.Value);
		}
	}
}
