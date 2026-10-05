using SolBotSentinel;
using SolBotSentinel.Domain;
using SolBotSentinel.Rules;

namespace SolBotSentinel.Tests;

public class RulesTests
{
	private static readonly DateTimeOffset T0 = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

	private static PositionSnapshot Open(
		decimal price = 4.60m,
		decimal borrowApr = 0.0416m,
		decimal deposit = 100m,
		decimal leverage = 2m,
		DateTimeOffset? at = null) => new()
	{
		BotId = "tallboy",
		Status = "open",
		EntryPrice = 4.60m,
		Price = price,
		CollateralUnits = 43.48m,
		DebtUsd = 100m,
		DepositUsd = deposit,
		Leverage = leverage,
		CollateralYieldApr = 0.18m,
		DebtBorrowApr = borrowApr,
		At = at ?? T0,
	};

	[Fact]
	public void Stop_at_2x_and_10_percent_sits_5_percent_under_entry()
	{
		Assert.Equal(4.37m, RiskRules.StopLossPrice(4.60m, 0.10m, 2m));
	}

	[Fact]
	public void Healthy_position_trips_nothing()
	{
		Assert.Null(RiskRules.Evaluate(Open(), new WatchOptions(), null));
	}

	[Fact]
	public void Price_at_the_stop_trips_price_stop()
	{
		Assert.Equal(RiskRules.PriceStop, RiskRules.Evaluate(Open(price: 4.37m), new WatchOptions(), null));
	}

	[Fact]
	public void Price_one_tick_above_the_stop_does_not_trip()
	{
		Assert.Null(RiskRules.Evaluate(Open(price: 4.3701m), new WatchOptions(), null));
	}

	[Fact]
	public void Ltv_is_borrowed_over_collateral_value()
	{
		var ltv = RiskRules.Ltv(Open());
		Assert.NotNull(ltv);
		Assert.Equal(0.5m, Math.Round(ltv.Value, 3));
	}

	[Fact]
	public void Ltv_at_the_threshold_trips_ltv_guard()
	{
		// A wide stop keeps the price check out of the way so the LTV check is what trips.
		var options = new WatchOptions { StopLossPct = 0.90m };
		var s = Open(price: 2.87m);
		Assert.True(RiskRules.Ltv(s) >= 0.80m);
		Assert.Equal(RiskRules.LtvGuard, RiskRules.Evaluate(s, options, null));
	}

	[Fact]
	public void Carry_goes_negative_when_the_borrow_rate_spikes()
	{
		Assert.True(RiskRules.NetCarryApr(Open()) > 0);
		Assert.True(RiskRules.NetCarryApr(Open(borrowApr: 0.40m)) < 0);
	}

	[Fact]
	public void Negative_carry_trips_only_after_the_dwell_window()
	{
		var options = new WatchOptions();
		var early = Open(borrowApr: 0.40m, at: T0.AddMinutes(19));
		var late = Open(borrowApr: 0.40m, at: T0.AddMinutes(20));

		Assert.Null(RiskRules.Evaluate(early, options, T0));
		Assert.Equal(RiskRules.BorrowRate, RiskRules.Evaluate(late, options, T0));
	}

	[Fact]
	public void A_reading_without_prices_trips_nothing()
	{
		var noPrices = Open() with { EntryPrice = 0, Price = 0, CollateralUnits = 0, DebtUsd = 0 };
		Assert.Null(RiskRules.Evaluate(noPrices, new WatchOptions(), null));
	}

	[Fact]
	public void Idle_bot_trips_nothing()
	{
		var idle = Open(price: 1m) with { Status = "idle" };
		Assert.Null(RiskRules.Evaluate(idle, new WatchOptions(), null));
	}

	[Fact]
	public void Deposit_over_the_cap_is_flagged()
	{
		var reasons = ConductRules.Evaluate(Open(deposit: 250m), new WatchOptions(), T0);
		Assert.Equal([ConductRules.CapExceeded], reasons);
	}

	[Fact]
	public void Deposit_at_the_cap_is_fine()
	{
		Assert.Empty(ConductRules.Evaluate(Open(deposit: 100m), new WatchOptions(), T0));
	}

	[Fact]
	public void Leverage_over_the_limit_is_flagged()
	{
		var reasons = ConductRules.Evaluate(Open(leverage: 3.5m), new WatchOptions(), T0);
		Assert.Equal([ConductRules.LeverageExceeded], reasons);
	}

	[Fact]
	public void A_reading_older_than_the_limit_is_a_stale_heartbeat()
	{
		var reasons = ConductRules.Evaluate(Open(), new WatchOptions(), T0.AddSeconds(61));
		Assert.Equal([ConductRules.StaleHeartbeat], reasons);
	}

	[Fact]
	public void The_same_alert_is_raised_once_until_it_clears()
	{
		var store = new AlertStore();
		var alert = new Alert
		{
			BotId = "tallboy", Watcher = "risk", Reason = RiskRules.PriceStop, Detail = "x", At = T0,
		};

		Assert.True(store.Raise(alert));
		Assert.False(store.Raise(alert));
		Assert.Single(store.History);

		store.Clear("tallboy", RiskRules.PriceStop);
		Assert.True(store.Raise(alert));
	}
}
