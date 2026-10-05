namespace SolBotSentinel.Domain;

/// <summary>One reading of a bot's position. The watchers only ever read these.</summary>
public sealed record PositionSnapshot
{
	public required string BotId { get; init; }
	public required string Status { get; init; }
	public decimal EntryPrice { get; init; }
	public decimal Price { get; init; }
	public decimal CollateralUnits { get; init; }
	public decimal DebtUsd { get; init; }
	public decimal DepositUsd { get; init; }
	public decimal Leverage { get; init; }
	public decimal CollateralYieldApr { get; init; }
	public decimal DebtBorrowApr { get; init; }
	public DateTimeOffset At { get; init; }

	/// <summary>The trend this bot trades: "long" or "short". Empty when unknown.</summary>
	public string OpensOn { get; init; } = "";

	/// <summary>The bot's own state name, for a person reading the alert.</summary>
	public string State { get; init; } = "";

	/// <summary>When the bot last acted on a trend flip. Null when it never has.</summary>
	public DateTimeOffset? LastFlipTime { get; init; }

	public bool IsOpen => Status == "open";
}

/// <summary>The trend signal the bots act on, and when it last flipped.</summary>
public sealed record TrendReading
{
	public required string Trend { get; init; }

	/// <summary>When the trend last flipped. Null when the engine has not seen a flip yet.</summary>
	public DateTimeOffset? Since { get; init; }
}

/// <summary>What the engine's read-only feed returns: the trend and every bot, in one read.</summary>
public sealed record EngineFeed
{
	public TrendReading? Trend { get; init; }
	public List<PositionSnapshot> Positions { get; init; } = [];
}

/// <summary>Something a watcher saw that a person should look at.</summary>
public sealed record Alert
{
	public required string BotId { get; init; }
	public required string Watcher { get; init; }
	public required string Reason { get; init; }
	public required string Detail { get; init; }
	public decimal Price { get; init; }
	public decimal? Ltv { get; init; }
	public decimal? NetCarryApr { get; init; }
	public DateTimeOffset At { get; init; }
}

/// <summary>Every threshold comes from configuration. None is written in the rules.</summary>
public sealed class WatchOptions
{
	public string Source { get; set; } = "simulated";
	public string FeedUrl { get; set; } = "";
	public string FeedToken { get; set; } = "";
	public string ApiToken { get; set; } = "";
	public int PollMs { get; set; } = 2000;
	public decimal StopLossPct { get; set; } = 0.10m;
	public decimal LtvTripThreshold { get; set; } = 0.80m;
	public decimal CarryFloorApr { get; set; } = 0m;
	public long CarryDwellMs { get; set; } = 1_200_000;
	public decimal PositionCapUsd { get; set; } = 100m;
	public decimal MaxLeverage { get; set; } = 3.0m;
	public long HeartbeatStaleMs { get; set; } = 60_000;
	public long FlipGraceMs { get; set; } = 30_000;
	public long FlipToleranceMs { get; set; } = 2_000;
}
