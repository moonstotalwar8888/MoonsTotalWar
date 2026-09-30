using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER MATHEMATICAL ENGINE (v41.0 Unified Matrix Edition)
	/// Precision Calibrated 2.5D Isometric Projection, Storage Scaling & Orbital Telemetry.
	/// Implements exact formulas from the 6 Blueprints:
	/// - Sub-Mines Production: 100 -> 7,640 units/hr (Levels 1-30).
	/// - Exponential Build Times: Fast addictive start (seconds) scaling to challenging late game (hours).
	/// - Unified Requirements Matrix: Dynamically generates checklists for Core Caps, Prerequisites, and Sequence Locks.
	/// - Storage Silo: 15x Hourly Yield Capacity, 5%->30% Vault Protection.
	/// - Queue Simulation: Calculates future levels based on pending Engineering Dock queues.
	/// </summary>
	public static class GameMath
	{
		// ============================================================================
		// 0. QUEUE & BOOST DATA STRUCTURES
		// ============================================================================
		public class BuildQueueItem
		{
			public string BuildingId { get; set; }
			public string BuildingName { get; set; }
			public int TargetLevel { get; set; }
			public double DurationLeft { get; set; }
			public long CostE { get; set; }
			public long CostI { get; set; }
			public long CostT { get; set; }
			public long CostH3 { get; set; }
		}

		public class BoostData
		{
			public float Multiplier { get; set; }
			public DateTimeOffset Expiration { get; set; }
		}

		public static Dictionary<string, int> GetSimulatedLevels(Dictionary<string, int> currentLevels, List<BuildQueueItem> queue)
		{
			var sim = new Dictionary<string, int>(currentLevels);
			if (queue != null)
			{
				foreach (var item in queue)
				{
					sim[item.BuildingId] = item.TargetLevel;
				}
			}
			return sim;
		}

		public static int[] GetSimulatedDistrictArray(string districtCode, Dictionary<string, int> simLevels)
		{
			string code = districtCode.ToLower();
			if (code == "e" || code == "i" || code == "t")
			{
				return new int[] {
					simLevels.GetValueOrDefault($"dist_{code}_0", 1),
					simLevels.GetValueOrDefault($"dist_{code}_1", 0),
					simLevels.GetValueOrDefault($"dist_{code}_2", 0)
				};
			}
			else if (code == "h3")
			{
				return new int[] {
					simLevels.GetValueOrDefault("dist_h3_0", 1),
					simLevels.GetValueOrDefault("dist_h3_1", 0),
					simLevels.GetValueOrDefault("dist_h3_2", 0),
					simLevels.GetValueOrDefault("dist_h3_3", 0),
					simLevels.GetValueOrDefault("dist_h3_4", 0)
				};
			}
			return new int[0];
		}

		// ============================================================================
		// 1. ISOMETRIC 2.5D GROUND PROJECTION METRICS
		// ============================================================================
		public const int CanvasWidth = 4600;
		public const int CanvasHeight = 2800;

		public const int GridCols = 48; 
		public const int GridRows = 50; 
		public const float TileWidth = 80.0f;   
		public const float TileHeight = 45.0f;  

		public const float IsoOriginX = CanvasWidth / 2f;
		public const float IsoOriginY = 100f;

		public const int TotalMoons = 1000;

		public static Vector2 GridToIsometric(float gridX, float gridY)
		{
			float screenX = (gridX - gridY) * 40.0f;
			float screenY = (gridX + gridY) * 22.5f;
			return new Vector2(screenX, screenY);
		}

		public static Vector2I ScreenToGrid(float screenX, float screenY)
		{
			int col = (int)Math.Floor((screenY / 22.5f + screenX / 40.0f) / 2f);
			int row = (int)Math.Floor((screenY / 22.5f - screenX / 40.0f) / 2f);

			col = Math.Clamp(col, 0, GridCols - 1);
			row = Math.Clamp(row, 0, GridRows - 1);

			return new Vector2I(col, row);
		}

		public static bool IsFootprintInBounds(int col, int row, int w, int h)
		{
			return col >= 0 && row >= 0 && (col + w) <= GridCols && (row + h) <= GridRows;
		}

		public static bool DoFootprintsOverlap(int colA, int rowA, int wA, int hA, int colB, int rowB, int wB, int hB)
		{
			return colA < (colB + wB) && (colA + wA) > colB &&
				   rowA < (rowB + hB) && (rowA + hA) > rowB;
		}

		// ============================================================================
		// 3. BLUEPRINT PRODUCTION CURVES
		// ============================================================================
		public static long CalcSubMineYield(int level)
		{
			if (level <= 0) return 0;
			if (level == 1) return 100;
			
			double exponent = 1.272;
			return (long)Math.Round(100.0 * Math.Pow(level, exponent));
		}

		// ============================================================================
		// 4. STORAGE SILO BLUEPRINT MATH
		// ============================================================================
		public static long CalcSiloCapacity(int siloLevel)
		{
			int safeLvl = Math.Clamp(siloLevel, 1, 50);
			long singleMineYield = CalcSubMineYield(safeLvl);
			long peakHourlyProduction = singleMineYield * 5;
			return peakHourlyProduction * 15;
		}

		public static float GetSiloVaultProtection(int siloLevel)
		{
			int safeLvl = Math.Clamp(siloLevel, 1, 30);
			return 0.05f + ((safeLvl - 1) / 29.0f) * 0.25f;
		}

		// ============================================================================
		// 5. BLUEPRINT STRUCTURAL STATS: HP & POPULATION
		// ============================================================================
		public static int GetStructureHp(int level)
		{
			return Math.Max(1, level) * 100;
		}

		public static int GetStructurePop(string buildingTypeOrCode, int level)
		{
			int safeLvl = Math.Max(0, level);
			string code = (buildingTypeOrCode ?? "").ToLower();

			if (code == "cmd" || code.StartsWith("hub_cmd") || code == "core") return safeLvl * 100; 
			else if (code == "silo" || code.StartsWith("hub_silo")) return safeLvl * 25;  
			else return safeLvl * 10;  
		}

		// ============================================================================
		// 6. BLUEPRINT COST CALCULATIONS
		// ============================================================================
		public static (long e, long i, long t, long h) CalcSubMineUpgradeCost(string districtCode, int currentLevel)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			double scale = Math.Pow(1.22, nextLvl - 1);
			long baseTotal = (long)(100 * scale); 

			string dist = (districtCode ?? "E").ToUpper();

			if (dist == "E") return (Math.Max(10, (long)(baseTotal * 0.333)), Math.Max(10, (long)(baseTotal * 0.500)), Math.Max(5, (long)(baseTotal * 0.167)), 0);
			else if (dist == "I") return (Math.Max(10, (long)(baseTotal * 0.500)), Math.Max(10, (long)(baseTotal * 0.167)), Math.Max(5, (long)(baseTotal * 0.333)), 0);
			else if (dist == "T") return (Math.Max(5, (long)(baseTotal * 0.167)), Math.Max(10, (long)(baseTotal * 0.333)), Math.Max(10, (long)(baseTotal * 0.500)), 0);
			else return (Math.Max(20, (long)(100 * scale)), Math.Max(15, (long)(80 * scale)), Math.Max(10, (long)(70 * scale)), 0);
		}

		public static (long e, long i, long t, long h) CalcCost(int lvl, long baseE, long baseI, long baseT, long baseH = 0)
		{
			int safeLvl = Math.Max(0, lvl);
			double scaling = 1.32;
			return (
				(long)Math.Floor(baseE * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseI * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseT * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseH * Math.Pow(scaling, safeLvl))
			);
		}

		public static (long e, long i, long t, long h) CalcIndustrialCoreCost(int currentLevel)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			double factor = Math.Pow(1.35, nextLvl - 1);
			return ((long)(350 * factor), (long)(350 * factor), (long)(150 * factor), 0);
		}

		public static (long e, long i, long t, long h) CalcStorageSiloCost(int currentLevel)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			double factor = Math.Pow(1.26, nextLvl - 1);
			return ((long)(150 * factor), (long)(200 * factor), (long)(100 * factor), 0);
		}

		// ============================================================================
		// 7. EXPONENTIAL BUILD TIME CALCULATIONS
		// ============================================================================
		public static int GetSubMineBuildTimeSec(int currentLevel, float serverSpeed = 1.0f)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			double baseSec = 30.0 * Math.Pow(1.25, nextLvl - 1);
			return (int)Math.Max(5, Math.Floor(baseSec / Math.Max(1.0f, serverSpeed)));
		}

		public static int GetIndustrialCoreBuildTimeSec(int currentLevel, float serverSpeed = 1.0f)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			double baseSec = 60.0 * Math.Pow(1.33, nextLvl - 1);
			return (int)Math.Max(10, Math.Floor(baseSec / Math.Max(1.0f, serverSpeed)));
		}

		public static int GetStorageSiloBuildTimeSec(int currentLevel, float serverSpeed = 1.0f)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			double baseSec = 45.0 * Math.Pow(1.26, nextLvl - 1);
			return (int)Math.Max(10, Math.Floor(baseSec / Math.Max(1.0f, serverSpeed)));
		}

		// ============================================================================
		// 8. UNIFIED REQUIREMENTS MATRIX ENGINE
		// ============================================================================
		public struct RequirementStatus
		{
			public string BuildingId;
			public string BuildingName;
			public int RequiredLevel;
			public int CurrentLevel;
			public bool IsMet;
		}

		public struct CorePrereqResult
		{
			public bool AllMet;
			public List<RequirementStatus> Requirements;
		}

		/// <summary>
		/// Generates a unified checklist of requirements for ANY building (Core, Mines, Hubs).
		/// </summary>
		public static List<RequirementStatus> GetFacilityRequirements(
			string buildingId, int targetLevel, string districtCode, int slotIndex, Dictionary<string, int> simLevels)
		{
			var reqs = new List<RequirementStatus>();

			// 1. If it's the Industrial Core, return its massive sub-building matrix
			if (buildingId == "hub_cmd")
			{
				return CheckIndustrialCoreRequirements(targetLevel, simLevels).Requirements;
			}

			// 2. For all other buildings, check the Industrial Core Level Requirement
			var coreUnlock = CheckIndustrialCoreUnlock(buildingId, 1); 
			if (coreUnlock.requiredCoreLevel > 1 || buildingId == "hub_silo" || buildingId == "hub_mgd") 
			{
				int simCoreLvl = simLevels.GetValueOrDefault("hub_cmd", 1);
				reqs.Add(new RequirementStatus {
					BuildingId = "hub_cmd",
					BuildingName = "Industrial Core",
					RequiredLevel = coreUnlock.requiredCoreLevel,
					CurrentLevel = simCoreLvl,
					IsMet = simCoreLvl >= coreUnlock.requiredCoreLevel
				});
			}

			// 3. For Sub-Mines, check the Sequence Lock Requirement (Node B requires Node A)
			if (!string.IsNullOrEmpty(districtCode) && slotIndex > 0)
			{
				string prevId = $"dist_{districtCode.ToLower()}_{slotIndex - 1}";
				string prevName = GetBuildingNameFromId(prevId);
				int prevLvl = simLevels.GetValueOrDefault(prevId, 0);
				
				reqs.Add(new RequirementStatus {
					BuildingId = prevId,
					BuildingName = prevName,
					RequiredLevel = targetLevel, 
					CurrentLevel = prevLvl,
					IsMet = prevLvl >= targetLevel
				});
			}

			return reqs;
		}

		public static string GetBuildingNameFromId(string id)
		{
			if (id.StartsWith("dist_e_")) return "Power Station " + (char)('A' + int.Parse(id.Substring(7)));
			if (id.StartsWith("dist_i_")) return "Iron Mine " + (char)('A' + int.Parse(id.Substring(7)));
			if (id.StartsWith("dist_t_")) return "Titanium Smelter " + (char)('A' + int.Parse(id.Substring(7)));
			if (id.StartsWith("dist_h3_")) return "H3 Distillery " + (char)('A' + int.Parse(id.Substring(8)));
			return "Facility";
		}

		public static CorePrereqResult CheckIndustrialCoreRequirements(int targetCoreLevel, Dictionary<string, int> allBuildingLevels)
		{
			var result = new CorePrereqResult { AllMet = true, Requirements = new List<RequirementStatus>() };
			if (targetCoreLevel <= 1) return result;

			var reqDefs = new List<(string id, string name, int reqLvl)>();

			switch (targetCoreLevel)
			{
				case 2: break;
				case 3:
					reqDefs.Add(("dist_e_2", "Power Station C", 2));
					reqDefs.Add(("dist_i_2", "Iron Mine C", 2));
					reqDefs.Add(("dist_t_2", "Titanium Smelter C", 2));
					break;
				case 4:
					reqDefs.Add(("hub_silo", "Storage Silos", 3));
					reqDefs.Add(("dist_e_0", "Power Station A", 3));
					reqDefs.Add(("dist_i_0", "Iron Mine A", 3));
					reqDefs.Add(("dist_t_2", "Titanium Smelter C", 3));
					break;
				case 5:
					reqDefs.Add(("hub_ahq", "Alliance HQ", 3));
					break;
				case 6:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 5));
					reqDefs.Add(("hub_silo", "Storage Silos", 5));
					break;
				case 7:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 6));
					break;
				case 8:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 7));
					break;
				case 9:
					reqDefs.Add(("hub_silo", "Storage Silos", 8));
					reqDefs.Add(("dist_h3_4", "H3 Distillery E", 8));
					break;
				case 10:
					reqDefs.Add(("hub_trd", "Trade Logistics", 5));
					break;
				case 11:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 10));
					reqDefs.Add(("hub_shd", "Planetary Shield", 5));
					break;
				case 12:
					reqDefs.Add(("hub_silo", "Storage Silos", 11));
					break;
				case 13:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 12));
					break;
				case 14:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 13));
					break;
				case 15:
					reqDefs.Add(("hub_silo", "Storage Silos", 14));
					break;
				case 16:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 15));
					break;
				case 17:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 16));
					break;
				case 18:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 17));
					reqDefs.Add(("hub_silo", "Storage Silos", 16));
					break;
				case 19:
					reqDefs.Add(("hub_silo", "Storage Silos", 18));
					reqDefs.Add(("dist_h3_2", "H3 Distillery C", 15));
					break;
				case 20:
					reqDefs.Add(("hub_mil", "Orbital Shipyard", 19));
					break;
			}

			foreach (var req in reqDefs)
			{
				int cur = allBuildingLevels != null ? allBuildingLevels.GetValueOrDefault(req.id, 0) : 0;
				bool met = cur >= req.reqLvl;
				if (!met) result.AllMet = false;

				result.Requirements.Add(new RequirementStatus
				{
					BuildingId = req.id,
					BuildingName = req.name,
					RequiredLevel = req.reqLvl,
					CurrentLevel = cur,
					IsMet = met
				});
			}

			return result;
		}

		// ============================================================================
		// 9. CORE CAPS & UNLOCKS
		// ============================================================================
		public static int GetMaxAllowedSubBuildingLevel(int coreLevel)
		{
			if (coreLevel >= 20) return 50;
			return coreLevel;
		}

		public static (bool isUnlocked, int requiredCoreLevel, string description) CheckIndustrialCoreUnlock(string buildingCode, int coreLevel)
		{
			string code = (buildingCode ?? "").ToLower();

			if (code == "mgd" || code == "silo" || code.StartsWith("hub_mgd") || code.StartsWith("hub_silo")) return (true, 1, "Starting Structure");
			if (code == "e" || code == "i" || code == "t" || code.StartsWith("dist_e") || code.StartsWith("dist_i") || code.StartsWith("dist_t")) return (coreLevel >= 2, 2, "Requires Industrial Core Level 2");
			if (code == "h3" || code.StartsWith("dist_h3") || code == "flt" || code == "ahq" || code == "arm" || code == "rng" || code.StartsWith("hub_flt") || code.StartsWith("hub_ahq") || code.StartsWith("hub_arm") || code.StartsWith("hub_rng")) return (coreLevel >= 4, 4, "Requires Industrial Core Level 4");
			if (code == "mil" || code == "trd" || code.StartsWith("hub_mil") || code.StartsWith("hub_trd")) return (coreLevel >= 5, 5, "Requires Industrial Core Level 5");
			if (code == "shd" || code == "rsh" || code.StartsWith("hub_shd") || code.StartsWith("hub_rsh")) return (coreLevel >= 7, 7, "Requires Industrial Core Level 7");
			if (code == "com" || code.StartsWith("hub_com")) return (coreLevel >= 11, 11, "Requires Industrial Core Level 11");

			return (true, 1, "Standard Facility");
		}

		public static float GetWallMitigation(int lvl)
		{
			int safeLvl = Math.Max(0, lvl);
			return Math.Min(0.75f, safeLvl * 0.015f);
		}

		public static string FormatTime(double seconds)
		{
			long s = Math.Max(0, (long)Math.Floor(seconds));
			if (s <= 0) return "00s";

			long d = s / 86400;
			long remS = s % 86400;
			long h = remS / 3600;
			long m = (remS % 3600) / 60;
			long sec = remS % 60;

			if (d > 0) return $"{d}d {h}h {m}m {sec}s";
			if (h > 0) return $"{h}h {m}m {sec}s";
			if (m > 0) return $"{m}m {sec}s";
			return $"{sec}s";
		}
	}
}
