using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: STEP-CHRONO SIMULATION ENGINE (v16.0 Fractional Boost Edition)
	/// Reconstructs the exact timeline gap when the player was offline.
	/// Calculates offline resource production, processes the Premium Build Queue,
	/// and mathematically splits offline time to handle expiring Premium Boosts.
	/// </summary>
	public static class ChronoEngine
	{
		/// <summary>
		/// Simulates resource accumulation, queue progressions, and boost expirations between the last sync and now.
		/// </summary>
		public static (
			double resE, double resI, double resT, double resH3, double elapsedSec, 
			GameMath.BuildQueueItem activeBuild, List<GameMath.BuildQueueItem> buildQueue,
			Dictionary<string, GameMath.BoostData> activeBoosts
		) SimulateOfflineTimeline(
			double startE, 
			double startI, 
			double startT, 
			double startH3,
			Dictionary<string, int> buildingLevels,
			GameMath.BuildQueueItem activeBuild,
			List<GameMath.BuildQueueItem> buildQueue,
			Dictionary<string, GameMath.BoostData> activeBoosts,
			DateTimeOffset lastSyncTimeUtc,
			DateTimeOffset nowUtc,
			float serverSpeed = 1f,
			double h3DrainPerHour = 0)
		{
			if (buildingLevels == null) return (startE, startI, startT, startH3, 0, activeBuild, buildQueue, activeBoosts);
			if (buildQueue == null) buildQueue = new List<GameMath.BuildQueueItem>();
			if (activeBoosts == null) activeBoosts = new Dictionary<string, GameMath.BoostData>();

			// Calculate exact offline time gap in seconds using Absolute UTC Time
			double elapsedSeconds = (nowUtc - lastSyncTimeUtc).TotalSeconds;
			
			// If time is negative or zero (e.g., clock sync issues), do not simulate
			if (elapsedSeconds <= 0) return (startE, startI, startT, startH3, 0, activeBuild, buildQueue, activeBoosts);

			double speed = Math.Max(1.0, serverSpeed);

			// ====================================================================
			// 1. PROCESS OFFLINE BUILD QUEUE
			// ====================================================================
			double remainingBuildTime = elapsedSeconds * speed;

			while (remainingBuildTime > 0 && activeBuild != null)
			{
				if (remainingBuildTime >= activeBuild.DurationLeft)
				{
					// The active build finished while we were offline!
					remainingBuildTime -= activeBuild.DurationLeft;
					
					// Apply the new level to the base
					buildingLevels[activeBuild.BuildingId] = activeBuild.TargetLevel;
					GD.Print($"[CHRONO ENGINE] Offline Completion: {activeBuild.BuildingName} reached Lvl {activeBuild.TargetLevel}!");

					// Pop the next build from the queue
					if (buildQueue.Count > 0)
					{
						activeBuild = buildQueue[0];
						buildQueue.RemoveAt(0);
					}
					else
					{
						activeBuild = null; // Queue is empty
					}
				}
				else
				{
					// The active build is still running, just subtract the offline time
					activeBuild.DurationLeft -= remainingBuildTime;
					remainingBuildTime = 0;
				}
			}

			// ====================================================================
			// 2. CALCULATE OFFLINE RESOURCE PRODUCTION WITH FRACTIONAL BOOSTS
			// ====================================================================
			
			// Get Dynamic Silo Cap (using potentially newly upgraded silo level)
			int siloLevel = buildingLevels.GetValueOrDefault("hub_silo", 1);
			long siloCap = GameMath.CalcSiloCapacity(siloLevel);

			// Calculate Base Hourly Production Rates from Sub-Mines
			double rateE = 0, rateI = 0, rateT = 0, rateH3 = 0;

			rateE += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_e_0", 0));
			rateE += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_e_1", 0));
			rateE += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_e_2", 0));

			rateI += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_i_0", 0));
			rateI += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_i_1", 0));
			rateI += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_i_2", 0));

			rateT += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_t_0", 0));
			rateT += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_t_1", 0));
			rateT += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_t_2", 0));

			rateH3 += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_h3_0", 0));
			rateH3 += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_h3_1", 0));
			rateH3 += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_h3_2", 0));
			rateH3 += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_h3_3", 0));
			rateH3 += GameMath.CalcSubMineYield(buildingLevels.GetValueOrDefault("dist_h3_4", 0));

			// Calculate Effective Multipliers (Handles boosts expiring mid-offline)
			double multE = GetEffectiveMultiplier("E", activeBoosts, lastSyncTimeUtc, nowUtc);
			double multI = GetEffectiveMultiplier("I", activeBoosts, lastSyncTimeUtc, nowUtc);
			double multT = GetEffectiveMultiplier("T", activeBoosts, lastSyncTimeUtc, nowUtc);
			double multH3 = GetEffectiveMultiplier("H3", activeBoosts, lastSyncTimeUtc, nowUtc);

			// Apply Multipliers
			double finalRateE = rateE * multE;
			double finalRateI = rateI * multI;
			double finalRateT = rateT * multT;
			double finalRateH3 = rateH3 * multH3;

			// Subtract fleet upkeep drain AFTER boost is applied
			double netH3Rate = finalRateH3 - h3DrainPerHour;

			// Accumulate offline production slices and hard-clamp to Silo Cap
			double newE = Math.Min(siloCap, startE + (finalRateE * speed * elapsedSeconds) / 3600.0);
			double newI = Math.Min(siloCap, startI + (finalRateI * speed * elapsedSeconds) / 3600.0);
			double newT = Math.Min(siloCap, startT + (finalRateT * speed * elapsedSeconds) / 3600.0);
			
			// H3 can drain to 0 if upkeep exceeds production
			double newH3 = Math.Max(0.0, Math.Min(siloCap, startH3 + (netH3Rate * speed * elapsedSeconds) / 3600.0));

			// ====================================================================
			// 3. CLEANUP EXPIRED BOOSTS
			// ====================================================================
			var expiredKeys = new List<string>();
			foreach (var kvp in activeBoosts)
			{
				if (kvp.Value.Expiration <= nowUtc)
				{
					expiredKeys.Add(kvp.Key);
				}
			}
			foreach (var key in expiredKeys)
			{
				activeBoosts.Remove(key);
				GD.Print($"[CHRONO ENGINE] Boost for {key} expired while offline and was removed.");
			}

			GD.Print($"[CHRONO ENGINE] Simulated {elapsedSeconds:F1}s offline. Yielded E:+{newE - startE:F0} I:+{newI - startI:F0} T:+{newT - startT:F0} H3:+{newH3 - startH3:F0}");

			return (newE, newI, newT, newH3, elapsedSeconds, activeBuild, buildQueue, activeBoosts);
		}

		/// <summary>
		/// Mathematically splits the offline time into "Boosted" and "Normal" chunks if a boost expires while offline.
		/// Returns the average effective multiplier for the entire offline duration.
		/// </summary>
		private static double GetEffectiveMultiplier(string resCode, Dictionary<string, GameMath.BoostData> boosts, DateTimeOffset start, DateTimeOffset end)
		{
			if (boosts == null || !boosts.TryGetValue(resCode, out var boost)) return 1.0;

			double totalSec = (end - start).TotalSeconds;
			if (totalSec <= 0) return 1.0;

			// If the boost already expired before we went offline
			if (boost.Expiration <= start) return 1.0;

			// Calculate how many seconds of the offline time were actually boosted
			double boostedSec = Math.Min(totalSec, (boost.Expiration - start).TotalSeconds);
			double normalSec = totalSec - boostedSec;

			// Weighted average multiplier
			return ((boostedSec * boost.Multiplier) + (normalSec * 1.0)) / totalSec;
		}

		/// <summary>
		/// Calculates total Empire Population based on all active building levels.
		/// </summary>
		public static int CalculateTotalPopulation(Dictionary<string, int> buildingLevels)
		{
			if (buildingLevels == null) return 0;
			
			int totalPop = 0;
			foreach (var kvp in buildingLevels)
			{
				totalPop += GameMath.GetStructurePop(kvp.Key, kvp.Value);
			}
			return totalPop;
		}

		/// <summary>
		/// Calculates total Helium-3 hourly consumption for stationed fleets and active missions.
		/// </summary>
		public static double CalculateEmpireH3Drain(Dictionary<string, int> inventory, Dictionary<string, int> unitH3Rates)
		{
			double totalDrain = 0;
			if (inventory == null || unitH3Rates == null) return 0;

			foreach (var kvp in inventory)
			{
				if (unitH3Rates.TryGetValue(kvp.Key, out int h3PerUnit))
				{
					totalDrain += h3PerUnit * Math.Max(0, kvp.Value);
				}
			}

			return totalDrain;
		}
	}
}
