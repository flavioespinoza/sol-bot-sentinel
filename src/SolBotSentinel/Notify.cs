using Microsoft.Extensions.Options;
using SolBotSentinel.Domain;

namespace SolBotSentinel;

/// <summary>Carries a newly raised alert to a person.</summary>
public interface INotifier
{
	/// <summary>False when nothing is configured, so the service can say so instead of pretending.</summary>
	bool Enabled { get; }

	Task NotifyAsync(Alert alert, CancellationToken ct);
}

/// <summary>
/// Sends an alert as a Pushover push. It never throws: an alert that cannot be delivered is
/// logged and the watcher keeps running. With no token or user it is disabled and says so.
/// </summary>
public sealed class PushoverNotifier(
	IHttpClientFactory clients,
	IOptions<WatchOptions> options,
	ILogger<PushoverNotifier> logger) : INotifier
{
	public bool Enabled =>
		options.Value.PushoverToken != "" && options.Value.PushoverUser != "" && options.Value.PushoverUrl != "";

	public async Task NotifyAsync(Alert alert, CancellationToken ct)
	{
		if (!Enabled) return;
		try
		{
			using var form = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["token"] = options.Value.PushoverToken,
				["user"] = options.Value.PushoverUser,
				["title"] = $"Sol Bot Sentinel: {alert.Reason}",
				["message"] = $"{alert.BotId} ({alert.Watcher}). {alert.Detail}",
				["priority"] = "1",
			});
			using var response = await clients.CreateClient().PostAsync(options.Value.PushoverUrl, form, ct);
			if (!response.IsSuccessStatusCode)
			{
				logger.LogError("Pushover answered {Status} for {Reason}", (int)response.StatusCode, alert.Reason);
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Pushover could not be reached for {Reason}", alert.Reason);
		}
	}
}
