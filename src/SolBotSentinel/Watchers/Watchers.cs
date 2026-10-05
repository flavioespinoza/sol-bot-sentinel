using Microsoft.Extensions.Options;
using SolBotSentinel.Domain;
using SolBotSentinel.Rules;
using SolBotSentinel.Sources;

namespace SolBotSentinel.Watchers;

/// <summary>
/// The shared loop. A watcher never exits on a read error: it logs, keeps running, and
/// tries again on the next tick.
/// </summary>
public abstract class WatcherBase(
	IPositionSource source,
	AlertStore alerts,
	IOptions<WatchOptions> options,
	TimeProvider clock,
	ILogger logger) : BackgroundService
{
	protected AlertStore Alerts => alerts;
	protected WatchOptions Options => options.Value;
	protected TimeProvider Clock => clock;
	protected abstract string WatcherName { get; }

	protected abstract void Check(PositionSnapshot snapshot);

	/// <summary>Runs once per tick before the positions are checked.</summary>
	protected virtual Task PrepareAsync(CancellationToken ct) => Task.CompletedTask;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Options.PollMs), clock);
		do
		{
			try
			{
				await PrepareAsync(stoppingToken);
				foreach (var snapshot in await source.ReadAsync(stoppingToken)) Check(snapshot);
				alerts.MarkPolled(WatcherName, clock.GetUtcNow());
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "{Watcher} could not read positions; retrying", WatcherName);
			}
		}
		while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	protected void Raise(PositionSnapshot s, string reason, string detail)
	{
		var alert = new Alert
		{
			BotId = s.BotId,
			Watcher = WatcherName,
			Reason = reason,
			Detail = detail,
			Price = s.Price,
			Ltv = RiskRules.Ltv(s),
			NetCarryApr = RiskRules.NetCarryApr(s),
			At = s.At,
		};
		if (alerts.Raise(alert))
		{
			logger.LogWarning("{Watcher} {Bot} {Reason}: {Detail}", WatcherName, s.BotId, reason, detail);
		}
	}
}

/// <summary>Watches an open position against the three exit checks.</summary>
public sealed class RiskWatcher(
	IPositionSource source,
	AlertStore alerts,
	IOptions<WatchOptions> options,
	TimeProvider clock,
	ILogger<RiskWatcher> logger) : WatcherBase(source, alerts, options, clock, logger)
{
	private readonly Dictionary<string, DateTimeOffset> _carryBelowSince = [];

	protected override string WatcherName => "risk";

	protected override void Check(PositionSnapshot s)
	{
		if (!s.IsOpen)
		{
			_carryBelowSince.Remove(s.BotId);
			foreach (var r in new[] { RiskRules.PriceStop, RiskRules.LtvGuard, RiskRules.BorrowRate })
			{
				Alerts.Clear(s.BotId, r);
			}
			return;
		}

		if (RiskRules.CarryIsBelowFloor(s, Options)) _carryBelowSince.TryAdd(s.BotId, s.At);
		else _carryBelowSince.Remove(s.BotId);

		DateTimeOffset? since = _carryBelowSince.TryGetValue(s.BotId, out var t) ? t : null;
		var reason = RiskRules.Evaluate(s, Options, since);
		if (reason is null) return;

		var stop = RiskRules.StopLossPrice(s.EntryPrice, Options.StopLossPct, s.Leverage);
		Raise(s, reason, $"price {s.Price:0.0000}, stop {stop:0.0000}, entry {s.EntryPrice:0.0000}");
	}
}

/// <summary>Watches that the bot stays inside its own limits and keeps reporting.</summary>
public sealed class ConductWatcher(
	IPositionSource source,
	AlertStore alerts,
	IOptions<WatchOptions> options,
	TimeProvider clock,
	ILogger<ConductWatcher> logger) : WatcherBase(source, alerts, options, clock, logger)
{
	private static readonly string[] All =
		[ConductRules.CapExceeded, ConductRules.LeverageExceeded, ConductRules.StaleHeartbeat];

	protected override string WatcherName => "conduct";

	protected override void Check(PositionSnapshot s)
	{
		var reasons = ConductRules.Evaluate(s, Options, Clock.GetUtcNow());
		foreach (var reason in All)
		{
			if (!reasons.Contains(reason))
			{
				Alerts.Clear(s.BotId, reason);
				continue;
			}
			Raise(s, reason, $"deposit {s.DepositUsd:0.##} USD, cap {Options.PositionCapUsd:0.##} USD, leverage {s.Leverage:0.##}x");
		}
	}
}

/// <summary>
/// The double-check on the trend flip. When the trend flips, the bot for the old trend must
/// close and the bot for the new trend must open. If either has not happened once the grace
/// window is over, this watcher says so.
/// </summary>
public sealed class TrendWatcher(
	IPositionSource source,
	ITrendSource trendSource,
	AlertStore alerts,
	IOptions<WatchOptions> options,
	TimeProvider clock,
	ILogger<TrendWatcher> logger) : WatcherBase(source, alerts, options, clock, logger)
{
	private static readonly string[] All = [TrendRules.FlipNotClosed, TrendRules.FlipNotOpened];
	private TrendReading? _trend;

	protected override string WatcherName => "trend";

	protected override async Task PrepareAsync(CancellationToken ct)
	{
		_trend = await trendSource.ReadAsync(ct);
	}

	protected override void Check(PositionSnapshot s)
	{
		if (_trend is null) return;

		var reason = TrendRules.Evaluate(_trend, s, Options, Clock.GetUtcNow());
		foreach (var r in All)
		{
			if (r != reason) Alerts.Clear(s.BotId, r);
		}
		if (reason is null) return;

		Raise(s, reason, $"trend {_trend.Trend} since {_trend.Since:HH:mm:ss} UTC, {s.BotId} is {s.Status}");
	}
}
