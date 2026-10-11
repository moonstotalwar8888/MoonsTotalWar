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
			// 1. PROCESS OFFLINE BUILD QUEUE (Anchored to CompletionTimeUnixMs & DurationLeft)
			// ====================================================================
			long nowMs = nowUtc.ToUnixTimeMilliseconds();
			if (activeBuild != null && activeBuild.CompletionTimeUnixMs > 0)
			{
				// Use exact UTC completion timestamp first so even if lastSyncTimeUtc was slightly older/newer,
				// the build finishes at its exact real-world second!
				activeBuild.DurationLeft = (activeBuild.CompletionTimeUnixMs - nowMs) / 1000.0;
				while (activeBuild != null && activeBuild.DurationLeft <= 0)
				{
					long finishedAtMs = activeBuild.CompletionTimeUnixMs;
					double overdueSec = Math.Max(0.0, -activeBuild.DurationLeft);

					buildingLevels[activeBuild.BuildingId] = activeBuild.TargetLevel;
					GD.Print($"[CHRONO ENGINE] Offline UTC Completion: {activeBuild.BuildingName} reached Lvl {activeBuild.TargetLevel}!");

					if (buildQueue.Count > 0)
					{
						activeBuild = buildQueue[0];
						buildQueue.RemoveAt(0);
						double nextDurSec = Math.Max(1.0, activeBuild.DurationLeft);
						activeBuild.CompletionTimeUnixMs = finishedAtMs + (long)(nextDurSec * 1000.0);
						activeBuild.DurationLeft = (activeBuild.CompletionTimeUnixMs - nowMs) / 1000.0;
					}
					else
					{
						activeBuild = null;
					}
				}
			}
			else
			{
				double remainingBuildTime = elapsedSeconds;

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
						activeBuild.CompletionTimeUnixMs = nowMs + (long)(Math.Max(0.1, activeBuild.DurationLeft) * 1000.0);
						remainingBuildTime = 0;
					}
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
			double newE = Math.Min(siloCap, startE + (finalRateE * elapsedSeconds) / 3600.0);
			double newI = Math.Min(siloCap, startI + (finalRateI * elapsedSeconds) / 3600.0);
			double newT = Math.Min(siloCap, startT + (finalRateT * elapsedSeconds) / 3600.0);
			
			// H3 can drain to 0 if upkeep exceeds production
			double newH3 = Math.Max(0.0, Math.Min(siloCap, startH3 + (netH3Rate * elapsedSeconds) / 3600.0));

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

			// ====================================================================
			// 4. PROCESS OFFLINE SIMULTANEOUS UNIT BAYS & IN-FLIGHT FLEETS
			// ====================================================================
			SimulateOfflineFleetsAndUnitBays(
				SupabaseService.CachedUnitInventory,
				SupabaseService.CachedUnitBays,
				SupabaseService.CachedSavedFleets,
				elapsedSeconds,
				nowUtc
			);

			// Calculate actual total H3 drain from all built units (Garrison Armory + Saved Fleets) if not explicitly passed
			double effectiveH3DrainPerHour = h3DrainPerHour > 0
				? h3DrainPerHour
				: CalculateEmpireH3Drain(SupabaseService.CachedUnitInventory, SupabaseService.CachedSavedFleets);

			double netH3RateWithUnits = finalRateH3 - effectiveH3DrainPerHour;
			newH3 = Math.Max(0.0, Math.Min(siloCap, startH3 + (netH3RateWithUnits * elapsedSeconds) / 3600.0));

			// If net H3 production is negative and H3 hit 0.00 while offline, apply 10-minute (600s) H3 starvation deaths
			// until either offline time ends or enough units die that net H3 production is >= 0 again!
			if (netH3RateWithUnits < 0.0 && newH3 <= 0.0 && elapsedSeconds >= 600.0)
			{
				double secToZeroH3 = startH3 > 0.0 ? Math.Min(elapsedSeconds, (startH3 / Math.Abs(netH3RateWithUnits)) * 3600.0) : 0.0;
				double starveSeconds = Math.Max(0.0, elapsedSeconds - secToZeroH3);
				int starveCycles = (int)Math.Floor(starveSeconds / 600.0);
				for (int c = 0; c < starveCycles; c++)
				{
					double currentDrain = CalculateEmpireH3Drain(SupabaseService.CachedUnitInventory, SupabaseService.CachedSavedFleets);
					if (finalRateH3 - currentDrain >= 0.0) break;
					int lost = ApplyOneH3StarvationCycle(SupabaseService.CachedUnitInventory, SupabaseService.CachedSavedFleets, buildingLevels);
					if (lost <= 0) break;
				}
			}

			GD.Print($"[CHRONO ENGINE] Simulated {elapsedSeconds:F1}s offline. Yielded E:+{newE - startE:F0} I:+{newI - startI:F0} T:+{newT - startT:F0} H3:{newH3 - startH3:+0.00;-0.00;0.00}");

			return (newE, newI, newT, newH3, elapsedSeconds, activeBuild, buildQueue, activeBoosts);
		}

		/// <summary>
		/// Catches up Simultaneous Orbital Shipyard Assembly Bays and In-Flight Fleets while the player was offline.
		/// Uses absolute UTC ArrivalTimeUnixMs / NextUnitDropUnixMs so that even if the player logs off or switches scenes,
		/// the exact real-world seconds elapsed are deducted!
		/// </summary>
		public static void SimulateOfflineFleetsAndUnitBays(
			Dictionary<string, int> unitInventory,
			Dictionary<string, GameMath.UnitAssemblyBayJob> unitBays,
			List<GameMath.FleetManifest> savedFleets,
			double elapsedSeconds,
			DateTimeOffset nowUtc)
		{
			long nowMs = nowUtc.ToUnixTimeMilliseconds();

			// 1. Offline Simultaneous Unit Bays
			if (unitBays != null && unitInventory != null && unitBays.Count > 0)
			{
				var completedBays = new List<string>();
				foreach (var kvp in unitBays)
				{
					string uId = kvp.Key;
					var job = kvp.Value;
					if (job == null || job.RemainingCount <= 0)
					{
						completedBays.Add(uId);
						continue;
					}

					double singleSec = Math.Max(1.0, job.SingleUnitDurationSec);
					if (job.NextUnitDropUnixMs > 0)
					{
						while (job.RemainingCount > 0 && nowMs >= job.NextUnitDropUnixMs)
						{
							unitInventory[uId] = unitInventory.GetValueOrDefault(uId, 0) + 1;
							job.RemainingCount--;
							if (job.RemainingCount > 0)
							{
								job.NextUnitDropUnixMs += (long)(singleSec * 1000.0);
							}
						}

						if (job.RemainingCount <= 0)
						{
							job.NextUnitTimerSec = 0;
							job.NextUnitDropUnixMs = 0;
							completedBays.Add(uId);
						}
						else
						{
							job.NextUnitTimerSec = Math.Max(0.1, (job.NextUnitDropUnixMs - nowMs) / 1000.0);
						}
					}
					else if (elapsedSeconds > 0)
					{
						job.NextUnitTimerSec -= elapsedSeconds;
						while (job.NextUnitTimerSec <= 0 && job.RemainingCount > 0)
						{
							unitInventory[uId] = unitInventory.GetValueOrDefault(uId, 0) + 1;
							job.RemainingCount--;
							if (job.RemainingCount > 0) job.NextUnitTimerSec += singleSec;
						}
						if (job.RemainingCount <= 0)
						{
							completedBays.Add(uId);
						}
						else
						{
							job.NextUnitDropUnixMs = nowMs + (long)(job.NextUnitTimerSec * 1000.0);
						}
					}
				}

				foreach (string k in completedBays) unitBays.Remove(k);
			}

			// 2. Offline In-Flight Fleets
			if (savedFleets != null)
			{
				foreach (var f in savedFleets)
				{
					if (f == null || f.Status != "IN_TRANSIT") continue;

					if (f.ArrivalTimeUnixMs > 0)
					{
						f.EtaSecondsLeft = Math.Max(0.0, (f.ArrivalTimeUnixMs - nowMs) / 1000.0);
					}
					else if (elapsedSeconds > 0)
					{
						f.EtaSecondsLeft = Math.Max(0.0, f.EtaSecondsLeft - elapsedSeconds);
						f.ArrivalTimeUnixMs = nowMs + (long)(f.EtaSecondsLeft * 1000.0);
					}
				}
			}
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
				if (kvp.Key.StartsWith("__")) continue;
				totalPop += GameMath.GetStructurePop(kvp.Key, kvp.Value);
			}
			return totalPop;
		}

		/// <summary>
		/// Calculates total Helium-3 hourly consumption for all built units across the player's base (Garrison Armory + Docked/Stationed/In-Flight Fleets).
		/// </summary>
		public static double CalculateEmpireH3Drain(
			Dictionary<string, int> inventory,
			List<GameMath.FleetManifest> savedFleets,
			Dictionary<string, double> unitH3Rates = null)
		{
			double totalDrain = CalculateEmpireH3Drain(inventory, unitH3Rates);
			if (savedFleets != null)
			{
				foreach (var f in savedFleets)
				{
					if (f == null || f.Status == "WATCHLIST_DATA" || f.Units == null) continue;
					foreach (var kvp in f.Units)
					{
						int count = Math.Max(0, kvp.Value);
						if (count <= 0) continue;
						if (unitH3Rates != null && unitH3Rates.TryGetValue(kvp.Key, out double h3PerUnit))
						{
							totalDrain += h3PerUnit * count;
						}
						else
						{
							var u = GameData.Units.Find(x => x.Id == kvp.Key);
							if (u != null) totalDrain += (double)u.H3Drain * count;
						}
					}
				}
			}
			return totalDrain;
		}

		/// <summary>
		/// Executes one 10-minute Helium-3 Starvation cycle when H3 is 0 and Net Production is negative.
		/// Removes 5% of total units (minimum 1 unit) starting from Garrison Armory and then Docked/Stationed Fleets.
		/// Returns the number of units that died from lack of fuel.
		/// </summary>
		public static int ApplyOneH3StarvationCycle(
			Dictionary<string, int> unitInventory,
			List<GameMath.FleetManifest> savedFleets,
			Dictionary<string, int> buildingLevels = null)
		{
			int totalUnits = 0;
			if (unitInventory != null)
			{
				foreach (var kv in unitInventory) totalUnits += Math.Max(0, kv.Value);
			}
			if (savedFleets != null)
			{
				foreach (var f in savedFleets)
				{
					if (f == null || f.Status == "WATCHLIST_DATA" || f.Units == null) continue;
					foreach (var kv in f.Units) totalUnits += Math.Max(0, kv.Value);
				}
			}
			if (totalUnits <= 0) return 0;

			int unitsToLose = Math.Max(1, (int)Math.Ceiling(totalUnits * 0.05));
			int remainingToLose = unitsToLose;

			// 1. Lose units from Garrison Armory first (highest H3 upkeep units first so production recovers faster)
			if (unitInventory != null)
			{
				for (int i = GameData.Units.Count - 1; i >= 0 && remainingToLose > 0; i--)
				{
					string uId = GameData.Units[i].Id;
					int have = unitInventory.GetValueOrDefault(uId, 0);
					if (have > 0)
					{
						int kill = Math.Min(have, remainingToLose);
						unitInventory[uId] = have - kill;
						if (buildingLevels != null) buildingLevels[$"__meta_unit_{uId}"] = have - kill;
						remainingToLose -= kill;
					}
				}
			}

			// 2. If more units still need to die, lose from Docked/Stationed/In-Flight Fleets
			if (remainingToLose > 0 && savedFleets != null)
			{
				for (int fIdx = savedFleets.Count - 1; fIdx >= 0 && remainingToLose > 0; fIdx--)
				{
					var f = savedFleets[fIdx];
					if (f == null || f.Status == "WATCHLIST_DATA" || f.Units == null) continue;

					for (int i = GameData.Units.Count - 1; i >= 0 && remainingToLose > 0; i--)
					{
						string uId = GameData.Units[i].Id;
						int have = f.Units.GetValueOrDefault(uId, 0);
						if (have > 0)
						{
							int kill = Math.Min(have, remainingToLose);
							f.Units[uId] = have - kill;
							remainingToLose -= kill;
						}
					}

					int remInFleet = 0;
					foreach (var kv in f.Units) remInFleet += Math.Max(0, kv.Value);
					if (remInFleet <= 0)
					{
						savedFleets.RemoveAt(fIdx);
					}
				}
			}

			return unitsToLose - remainingToLose;
		}

		/// <summary>
		/// Calculates total Helium-3 hourly consumption for stationed fleets and active missions (supports decimal H3/h rates like 0.2 H3/h).
		/// </summary>
		public static double CalculateEmpireH3Drain(Dictionary<string, int> inventory, Dictionary<string, double> unitH3Rates = null)
		{
			double totalDrain = 0.0;
			if (inventory == null) return 0.0;

			foreach (var kvp in inventory)
			{
				int count = Math.Max(0, kvp.Value);
				if (count <= 0) continue;

				if (unitH3Rates != null && unitH3Rates.TryGetValue(kvp.Key, out double h3PerUnit))
				{
					totalDrain += h3PerUnit * count;
				}
				else
				{
					var u = GameData.Units.Find(x => x.Id == kvp.Key);
					if (u != null) totalDrain += (double)u.H3Drain * count;
				}
			}

			return totalDrain;
		}
	}
}
