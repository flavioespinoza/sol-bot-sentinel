using SolBotSentinel.Domain;

namespace SolBotSentinel.Rules;

/// <summary>
/// The three exit checks on an open position: price stop, LTV guard, and borrow-rate
/// carry. Pure functions, so every number can be tested without a clock or a network.
/// </summary>
public static class RiskRules
{
	public const string PriceStop = "price-stop";
	public const string LtvGuard = "ltv-guard";
	public const string BorrowRate = "borrow-rate";

	/// <summary>The stop is a share of the deposit, so leverage pulls it closer to entry.</summary>
	public static decimal StopLossPrice(decimal entryPrice, decimal stopLossPct, decimal leverage)
	{
		if (leverage <= 0) return 0;
		return entryPrice * (1 - stopLossPct / leverage);
	}

	public static decimal? Ltv(PositionSnapshot s)
	{
		var collateral = s.CollateralUnits * s.Price;
		if (collateral <= 0) return null;
		return s.DebtUsd / collateral;
	}

	/// <summary>What the collateral earns, minus what the loan costs scaled by leverage.</summary>
	public static decimal? NetCarryApr(PositionSnapshot s)
	{
		var equity = s.CollateralUnits * s.Price - s.DebtUsd;
		if (equity <= 0) return null;
		return s.CollateralYieldApr - s.DebtBorrowApr * (s.DebtUsd / equity);
	}

	/// <summary>
	/// Returns the first check that trips, or null. The carry check needs to stay below the
	/// floor for the whole dwell window, so the caller passes when it first went below.
	/// </summary>
	public static string? Evaluate(
		PositionSnapshot s,
		WatchOptions o,
		DateTimeOffset? carryBelowSince)
	{
		if (!s.IsOpen) return null;

		if (s.Price <= StopLossPrice(s.EntryPrice, o.StopLossPct, s.Leverage)) return PriceStop;

		var ltv = Ltv(s);
		if (ltv is not null && ltv >= o.LtvTripThreshold) return LtvGuard;

		if (carryBelowSince is not null)
		{
			var heldMs = (s.At - carryBelowSince.Value).TotalMilliseconds;
			if (heldMs >= o.CarryDwellMs) return BorrowRate;
		}

		return null;
	}

	public static bool CarryIsBelowFloor(PositionSnapshot s, WatchOptions o)
	{
		var carry = NetCarryApr(s);
		return carry is not null && carry < o.CarryFloorApr;
	}
}
