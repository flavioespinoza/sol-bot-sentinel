namespace SolBotSentinel;

/// <summary>Watches the watchers: one that has stopped polling turns the service degraded.</summary>
public static class WatcherHealth
{
	public static readonly string[] Expected = ["risk", "conduct", "trend"];

	public static string Status(
		IReadOnlyDictionary<string, DateTimeOffset> lastPoll,
		DateTimeOffset now,
		int pollMs)
	{
		foreach (var name in Expected)
		{
			if (!lastPoll.TryGetValue(name, out var at)) return "starting";
			if ((now - at).TotalMilliseconds > pollMs * 3) return "degraded";
		}
		return "ok";
	}
}
