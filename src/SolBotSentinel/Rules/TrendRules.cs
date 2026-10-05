using SolBotSentinel.Domain;

namespace SolBotSentinel.Rules;

/// <summary>
/// After a trend flip, each bot has one right state: open if it trades the new trend, closed
/// if it trades the old one. The grace window gives the bot time to act before it is flagged.
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
		if (s.OpensOn == "") return null;
		if ((now - trend.Since).TotalMilliseconds < o.FlipGraceMs) return null;

		var shouldBeOpen = s.OpensOn == trend.Trend;
		if (shouldBeOpen && !s.IsOpen) return FlipNotOpened;
		if (!shouldBeOpen && s.IsOpen) return FlipNotClosed;
		return null;
	}
}
