using SolBotSentinel;
using SolBotSentinel.Domain;
using SolBotSentinel.Rules;

namespace SolBotSentinel.Tests;

public class TrendAndHealthTests
{
	private static readonly DateTimeOffset Flip = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
	private static readonly TrendReading Short = new() { Trend = "short", Since = Flip };

	private static PositionSnapshot Bot(string id, string opensOn, string status) => new()
	{
		BotId = id, OpensOn = opensOn, Status = status, At = Flip,
	};

	[Fact]
	public void Inside_the_grace_window_nothing_is_flagged()
	{
		var tallboy = Bot("tallboy", "long", "open");
		Assert.Null(TrendRules.Evaluate(Short, tallboy, new WatchOptions(), Flip.AddSeconds(29)));
	}

	[Fact]
	public void Old_trend_bot_still_open_after_the_grace_window_is_flagged()
	{
		var tallboy = Bot("tallboy", "long", "open");
		Assert.Equal(
			TrendRules.FlipNotClosed,
			TrendRules.Evaluate(Short, tallboy, new WatchOptions(), Flip.AddSeconds(30)));
	}

	[Fact]
	public void New_trend_bot_still_idle_after_the_grace_window_is_flagged()
	{
		var shorty = Bot("shorty", "short", "idle");
		Assert.Equal(
			TrendRules.FlipNotOpened,
			TrendRules.Evaluate(Short, shorty, new WatchOptions(), Flip.AddSeconds(30)));
	}

	[Fact]
	public void Bots_that_acted_on_the_flip_are_not_flagged()
	{
		var options = new WatchOptions();
		var later = Flip.AddMinutes(5);
		Assert.Null(TrendRules.Evaluate(Short, Bot("tallboy", "long", "idle"), options, later));
		Assert.Null(TrendRules.Evaluate(Short, Bot("shorty", "short", "open"), options, later));
	}

	[Fact]
	public void A_bot_that_opened_on_the_flip_and_was_stopped_out_is_not_flagged()
	{
		var shorty = Bot("shorty", "short", "idle") with { LastFlipTime = Flip };
		Assert.Null(TrendRules.Evaluate(Short, shorty, new WatchOptions(), Flip.AddMinutes(5)));
	}

	[Fact]
	public void A_bot_whose_last_action_was_an_earlier_flip_is_flagged()
	{
		var shorty = Bot("shorty", "short", "idle") with { LastFlipTime = Flip.AddHours(-4) };
		Assert.Equal(
			TrendRules.FlipNotOpened,
			TrendRules.Evaluate(Short, shorty, new WatchOptions(), Flip.AddMinutes(5)));
	}

	[Fact]
	public void A_bot_the_engine_is_not_trading_is_never_judged()
	{
		var manual = Bot("tallboy", "", "open");
		Assert.Null(TrendRules.Evaluate(Short, manual, new WatchOptions(), Flip.AddMinutes(5)));
	}

	[Fact]
	public void A_trend_that_has_not_flipped_yet_judges_nobody()
	{
		var noFlip = new TrendReading { Trend = "short", Since = null };
		var tallboy = Bot("tallboy", "long", "open");
		Assert.Null(TrendRules.Evaluate(noFlip, tallboy, new WatchOptions(), Flip.AddMinutes(5)));
	}

	[Fact]
	public void The_alert_routes_are_open_without_a_token_and_closed_with_one()
	{
		Assert.True(ApiAuth.Allowed("", ""));
		Assert.True(ApiAuth.Allowed("Bearer right", "right"));
		Assert.False(ApiAuth.Allowed("Bearer wrong", "right"));
		Assert.False(ApiAuth.Allowed("right", "right"));
		Assert.False(ApiAuth.Allowed("", "right"));
	}

	[Fact]
	public void Health_is_starting_until_every_watcher_has_polled()
	{
		var polled = new Dictionary<string, DateTimeOffset> { ["risk"] = Flip, ["conduct"] = Flip };
		Assert.Equal("starting", WatcherHealth.Status(polled, Flip, 2000));
	}

	[Fact]
	public void Health_is_degraded_when_one_watcher_stops_polling()
	{
		var polled = new Dictionary<string, DateTimeOffset>
		{
			["risk"] = Flip.AddSeconds(10), ["conduct"] = Flip.AddSeconds(10), ["trend"] = Flip,
		};
		Assert.Equal("degraded", WatcherHealth.Status(polled, Flip.AddSeconds(10), 2000));
	}

	[Fact]
	public void Health_is_ok_when_every_watcher_polled_recently()
	{
		var polled = new Dictionary<string, DateTimeOffset>
		{
			["risk"] = Flip, ["conduct"] = Flip, ["trend"] = Flip,
		};
		Assert.Equal("ok", WatcherHealth.Status(polled, Flip.AddSeconds(1), 2000));
	}
}
