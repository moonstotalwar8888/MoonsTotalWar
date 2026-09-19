using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: STEP-CHRONO SIMULATION ENGINE (C# Godot 4 Edition)
	/// Reconstructs the exact timeline gap when the player was offline.
	/// </summary>
	public static class ChronoEngine
	{
		public class ColonyState
		{
			public double ResE { get; set; }
			public double ResI { get; set; }
			public double ResT { get; set; }
			public double ResH3 { get; set; }
			public int Pop { get; set; }

			public int[] ElecDist { get; set; } = new int[3] { 1, 0, 0 };
			public int[] IronDist { get; set; } = new int[3] { 1, 0, 0 };
			public int[] TitanDist { get; set; } = new int[3] { 1, 0, 0 };
			public int[] H3Dist { get; set; } = new int[5] { 1, 0, 0, 0, 0 };

			public Dictionary<string, int> HubLvls { get; set; } = new Dictionary<string, int>
			{
				{ "silo", 1 }, { "wall", 0 }, { "cmd", 1 }, { "mil", 1 },
				{ "flt", 1 },  { "rng", 1 },  { "trd", 1 }, { "rsh", 1 },
				{ "shd", 0 },  { "com", 1 }
			};

			public Dictionary<string, int> UnitInventory { get; set; } = new Dictionary<string, int>();
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

		/// <summary>
		/// Simulates resource accumulation and timer progressions between lastSyncTimeMs and nowMs.
		/// </summary>
		public static ColonyState SimulateOfflineTimeline(
			ColonyState initial,
			long lastSyncTimeMs,
			long nowMs,
			float serverSpeed = 1f,
			double h3Drain = 0)
		{
			if (initial == null) return new ColonyState();

			double speed = Math.Max(1.0, serverSpeed);
			double elapsedSeconds = Math.Max(0.0, (nowMs - lastSyncTimeMs) / 1000.0);

			long siloCap = GameMath.CalcStorageCap(initial.HubLvls.GetValueOrDefault("silo", 1));

			// Hourly production rates
			double rateE = 0, rateI = 0, rateT = 0, rateH3 = 0;
			foreach (int lvl in initial.ElecDist) rateE += GameMath.CalcOutput(lvl, false);
			foreach (int lvl in initial.IronDist) rateI += GameMath.CalcOutput(lvl, false);
			foreach (int lvl in initial.TitanDist) rateT += GameMath.CalcOutput(lvl, false);
			foreach (int lvl in initial.H3Dist) rateH3 += GameMath.CalcOutput(lvl, true);

			double netH3Rate = rateH3 - (h3Drain / 5.0);

			// Accumulate slices
			initial.ResE = Math.Min(siloCap, initial.ResE + (rateE * speed * elapsedSeconds) / 3600.0);
			initial.ResI = Math.Min(siloCap, initial.ResI + (rateI * speed * elapsedSeconds) / 3600.0);
			initial.ResT = Math.Min(siloCap, initial.ResT + (rateT * speed * elapsedSeconds) / 3600.0);
			initial.ResH3 = Math.Max(0.0, Math.Min(siloCap, initial.ResH3 + (netH3Rate * speed * elapsedSeconds) / 3600.0));

			// Recalculate Empire Population
			int totalLevels = 0;
			foreach (int lvl in initial.ElecDist) totalLevels += lvl;
			foreach (int lvl in initial.IronDist) totalLevels += lvl;
			foreach (int lvl in initial.TitanDist) totalLevels += lvl;
			foreach (int lvl in initial.H3Dist) totalLevels += lvl;
			foreach (var kvp in initial.HubLvls) totalLevels += kvp.Value;

			initial.Pop = totalLevels * 100;

			return initial;
		}
	}
}
