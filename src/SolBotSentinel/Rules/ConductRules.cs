using SolBotSentinel.Domain;

namespace SolBotSentinel.Rules;

/// <summary>
/// Checks that the bot is behaving inside its own limits: it has not opened more than the
/// cap, it is not over-leveraged, and it is still reporting.
/// </summary>
public static class ConductRules
{
	public const string CapExceeded = "cap-exceeded";
	public const string LeverageExceeded = "leverage-exceeded";
	public const string StaleHeartbeat = "stale-heartbeat";

	public static List<string> Evaluate(PositionSnapshot s, WatchOptions o, DateTimeOffset now)
	{
		var reasons = new List<string>();

		if ((now - s.At).TotalMilliseconds > o.HeartbeatStaleMs) reasons.Add(StaleHeartbeat);

		if (!s.IsOpen) return reasons;

		if (s.DepositUsd > o.PositionCapUsd) reasons.Add(CapExceeded);
		if (s.Leverage > o.MaxLeverage) reasons.Add(LeverageExceeded);

		return reasons;
	}
}
