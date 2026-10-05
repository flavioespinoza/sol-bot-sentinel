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

/// <summary>Where the trend watcher gets the trend signal. Read only, by design.</summary>
public interface ITrendSource
{
	Task<TrendReading> ReadAsync(CancellationToken ct);
}

/// <summary>Reads a JSON array of snapshots from the bot host. The address is configuration.</summary>
public sealed class HttpPositionSource(HttpClient http, IOptions<WatchOptions> options) : IPositionSource
{
	public string Name => "http";

	public async Task<IReadOnlyList<PositionSnapshot>> ReadAsync(CancellationToken ct)
	{
		var list = await http.GetFromJsonAsync<List<PositionSnapshot>>(options.Value.SourceUrl, ct);
		return list ?? [];
	}
}

/// <summary>Reads the trend signal as JSON. The address is configuration.</summary>
public sealed class HttpTrendSource(HttpClient http, IOptions<WatchOptions> options) : ITrendSource
{
	public async Task<TrendReading> ReadAsync(CancellationToken ct)
	{
		var reading = await http.GetFromJsonAsync<TrendReading>(options.Value.TrendUrl, ct);
		return reading ?? throw new InvalidOperationException("The trend source returned nothing.");
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

	Task<TrendReading> ITrendSource.ReadAsync(CancellationToken ct)
	{
		var flipped = Step(clock.GetUtcNow()) >= FlipStep;
		return Task.FromResult(new TrendReading
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
