using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SolBotSentinel.Domain;

namespace SolBotSentinel.Sources;

/// <summary>Where the watchers get their position readings. Read only, by design.</summary>
public interface IPositionSource
{
	string Name { get; }
	Task<IReadOnlyList<PositionSnapshot>> ReadAsync(CancellationToken ct);
}

/// <summary>Where the trend watcher gets the trend signal. Null when there is none yet.</summary>
public interface ITrendSource
{
	Task<TrendReading?> ReadAsync(CancellationToken ct);
}

/// <summary>
/// Reads the engine's read-only feed: one authenticated GET that returns the trend and every
/// bot. All three watchers share one read per poll, so the engine is asked once, not six times.
/// The address and the token are configuration; neither is written here.
/// </summary>
public sealed class HttpFeed(
	IHttpClientFactory clients,
	IOptions<WatchOptions> options,
	TimeProvider clock) : IPositionSource, ITrendSource
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private EngineFeed? _last;
	private DateTimeOffset _lastAt;

	public string Name => "http";

	async Task<IReadOnlyList<PositionSnapshot>> IPositionSource.ReadAsync(CancellationToken ct) =>
		(await ReadFeedAsync(ct)).Positions;

	async Task<TrendReading?> ITrendSource.ReadAsync(CancellationToken ct) =>
		(await ReadFeedAsync(ct)).Trend;

	private async Task<EngineFeed> ReadFeedAsync(CancellationToken ct)
	{
		await _gate.WaitAsync(ct);
		try
		{
			var now = clock.GetUtcNow();
			var freshMs = Math.Max(1, options.Value.PollMs) / 2;
			if (_last is not null && (now - _lastAt).TotalMilliseconds < freshMs) return _last;

			using var request = new HttpRequestMessage(HttpMethod.Get, options.Value.FeedUrl);
			if (options.Value.FeedToken != "")
			{
				request.Headers.Authorization = new("Bearer", options.Value.FeedToken);
			}
			using var response = await clients.CreateClient().SendAsync(request, ct);
			response.EnsureSuccessStatusCode();
			_last = await response.Content.ReadFromJsonAsync<EngineFeed>(ct)
				?? throw new InvalidOperationException("The engine feed returned nothing.");
			_lastAt = now;
			return _last;
		}
		finally
		{
			_gate.Release();
		}
	}
}

/// <summary>
/// A scripted market for the demo and the tests, driven by the clock so every watcher sees
/// the same thing. The trend starts long with Tallboy open. Tallboy's price falls half a
/// percent each poll interval until its stop is crossed. At step twelve the trend flips to
/// short: Shorty opens, over the cap, and Tallboy never closes.
/// </summary>
public sealed class SimulatedMarket(TimeProvider clock, IOptions<WatchOptions> options)
	: IPositionSource, ITrendSource
{
	private const int MaxSteps = 20;
	private const int FlipStep = 12;
	private readonly DateTimeOffset _start = clock.GetUtcNow();

	public string Name => "simulated";

	private int PollMs => Math.Max(1, options.Value.PollMs);

	private int Step(DateTimeOffset now) => (int)((now - _start).TotalMilliseconds / PollMs);

	Task<TrendReading?> ITrendSource.ReadAsync(CancellationToken ct)
	{
		var flipped = Step(clock.GetUtcNow()) >= FlipStep;
		return Task.FromResult<TrendReading?>(new TrendReading
		{
			Trend = flipped ? "short" : "long",
			Since = flipped ? _start.AddMilliseconds((double)FlipStep * PollMs) : _start,
		});
	}

	Task<IReadOnlyList<PositionSnapshot>> IPositionSource.ReadAsync(CancellationToken ct)
	{
		var now = clock.GetUtcNow();
		var step = Step(now);
		var entry = 4.60m;
		var price = entry * (1 - 0.005m * Math.Min(MaxSteps, step));

		IReadOnlyList<PositionSnapshot> snapshots =
		[
			new PositionSnapshot
			{
				BotId = "tallboy",
				OpensOn = "long",
				Status = "open",
				EntryPrice = entry,
				Price = price,
				CollateralUnits = 43.48m,
				DebtUsd = 100m,
				DepositUsd = 100m,
				Leverage = 2m,
				CollateralYieldApr = 0.18m,
				DebtBorrowApr = 0.0416m,
				At = now,
			},
			new PositionSnapshot
			{
				BotId = "shorty",
				OpensOn = "short",
				Status = step >= FlipStep ? "open" : "idle",
				EntryPrice = entry,
				Price = entry,
				CollateralUnits = 108.70m,
				DebtUsd = 250m,
				DepositUsd = 250m,
				Leverage = 2m,
				CollateralYieldApr = 0.18m,
				DebtBorrowApr = 0.0416m,
				At = now,
			},
		];
		return Task.FromResult(snapshots);
	}
}
