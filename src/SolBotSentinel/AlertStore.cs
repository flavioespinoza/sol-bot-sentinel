using System.Collections.Concurrent;
using SolBotSentinel.Domain;

namespace SolBotSentinel;

/// <summary>
/// Holds alerts in memory. One alert per bot and reason while the condition lasts, so a
/// check that trips on every poll is reported once, at the first crossing.
/// </summary>
public sealed class AlertStore
{
	private readonly ConcurrentDictionary<string, Alert> _active = new();
	private readonly ConcurrentQueue<Alert> _history = new();
	private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPoll = new();

	private static string Key(string botId, string reason) => botId + "|" + reason;

	/// <summary>Returns true only the first time this bot and reason is raised.</summary>
	public bool Raise(Alert alert)
	{
		if (!_active.TryAdd(Key(alert.BotId, alert.Reason), alert)) return false;
		_history.Enqueue(alert);
		return true;
	}

	/// <summary>The condition ended, so the same reason may be raised again later.</summary>
	public void Clear(string botId, string reason) => _active.TryRemove(Key(botId, reason), out _);

	public void MarkPolled(string watcher, DateTimeOffset at) => _lastPoll[watcher] = at;

	public IReadOnlyCollection<Alert> Active => _active.Values.OrderBy(a => a.At).ToList();
	public IReadOnlyCollection<Alert> History => _history.ToList();
	public IReadOnlyDictionary<string, DateTimeOffset> LastPoll => _lastPoll;
}
