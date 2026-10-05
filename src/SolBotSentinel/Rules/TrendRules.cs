using SolBotSentinel.Domain;

namespace SolBotSentinel.Rules;

/// <summary>
/// After a trend flip, each bot has one right move: the bot for the old trend closes and the
/// bot for the new trend opens. The grace window gives the bot time to act before it is
/// flagged.
/// </summary>
public static class TrendRules
{
	public const string FlipNotClosed = "flip-not-closed";
	public const string FlipNotOpened = "flip-not-opened";

	public static string? Evaluate(
		TrendReading trend,
		PositionSnapshot s,
		WatchOptions o,
		DateTimeOffset now)
	{
		// An empty OpensOn is the engine saying this bot is not trading flips right now.
		if (s.OpensOn == "") return null;
		if (trend.Since is not { } since) return null;
		if ((now - since).TotalMilliseconds < o.FlipGraceMs) return null;

		var shouldHold = s.OpensOn == trend.Trend;

		// Holding a position against the trend is wrong however it got there.
		if (!shouldHold && s.IsOpen) return FlipNotClosed;

		// Idle on the right side of the trend is only wrong if the bot never acted on this
		// flip. A bot that opened and was then stopped out did its job.
		if (shouldHold && !s.IsOpen && !ActedOn(s, since, o)) return FlipNotOpened;

		return null;
	}

	private static bool ActedOn(PositionSnapshot s, DateTimeOffset flip, WatchOptions o)
	{
		if (s.LastFlipTime is not { } acted) return false;
		return acted >= flip.AddMilliseconds(-o.FlipToleranceMs);
	}
}
