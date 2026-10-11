using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER MATHEMATICAL ENGINE (v43.2 Universal Modal Parity Edition)
	/// - Complete Overload Support for BuildingInspectorModal, BaseHUD, and BaseView.
	/// - Dynamic Complexity Tiers (0.5 to 5.0) with separate Cost & Time scaling.
	/// - Dynamic Time Floors: 30s (Tier 0.5), 60s (Tier 1), 300s (Tier 2), 900s (Tier 3), 1800s (Tier 4), 3600s (Tier 5).
	/// - Node C Anchor (dist_h3_2) properly set to Tier 1 capstone node.
	/// - Retains 100% of all 28 Core prerequisites, 6 Blueprints formulas, and queue math.
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
			public long CompletionTimeUnixMs { get; set; } = 0;
			public long CostE { get; set; }
			public long CostI { get; set; }
			public long CostT { get; set; }
			public long CostH3 { get; set; }
		}

		public class BoostData
		{
			public float Multiplier { get; set; }
			public DateTimeOffset Expiration { get; set; }
			public string Label { get; set; } = "";
		}

		public class UnitAssemblyBayJob
		{
			public string UnitId { get; set; }
			public int RemainingCount { get; set; }
			public double NextUnitTimerSec { get; set; }
			public double SingleUnitDurationSec { get; set; }
			public long NextUnitDropUnixMs { get; set; } = 0;
		}

		public class WatchlistEntry
		{
			public string UserId { get; set; } = "";
			public string ServerId { get; set; } = "S1";
			public int MoonId { get; set; } = 100;
			public int SlotId { get; set; } = 1;
			public string CommanderName { get; set; } = "EMPTY MOON SLOT";
			public string BaseName { get; set; } = "Open Sector";
			public string AllianceName { get; set; } = "NONE";
			public int CoreLevel { get; set; } = 0;
			public bool IsOccupied { get; set; } = false;
		}

		public class FleetCargoHold
		{
			public long E { get; set; }
			public long I { get; set; }
			public long T { get; set; }
			public long H3 { get; set; }
			public List<WatchlistEntry> Watchlist { get; set; }
		}

		public class FleetManifest
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public Dictionary<string, int> Units { get; set; } = new Dictionary<string, int>();
			public FleetCargoHold Cargo { get; set; } = new FleetCargoHold();
			public string LocationCoord { get; set; } = "S1:100:1";
			public string Status { get; set; } = "DOCKED"; // DOCKED, IN_TRANSIT, STATIONED
			public string Protocol { get; set; } = "IDLE"; // IDLE, RAID, MOVE, REINFORCE, RETURN
			public string TargetCoord { get; set; } = "";
			public string TargetCommanderName { get; set; } = "";
			public string OriginCommanderName { get; set; } = "";
			public string TargetBuildingId { get; set; } = "hub_cmd"; // Laser Cannon Siege Target
			public double EtaSecondsLeft { get; set; } = 0;
			public double TotalFlightDurationSec { get; set; } = 0; // Total mission duration for exact 0..1 visual line progress
			public long DepartureTimeUnixMs { get; set; } = 0; // Absolute UTC Departure Timestamp
			public long ArrivalTimeUnixMs { get; set; } = 0; // Absolute UTC Arrival Timestamp (Immune to offline & scene reloads!)
			public long LastUpdatedUtcMs { get; set; } = 0; // Monotonic mutation timestamp so older cloud snapshots can NEVER revert a newly launched fleet!
		}

		// ============================================================================
		// 0B. VAST GALAXY SPATIAL GEOMETRY, EUCLIDEAN TRAVEL DISTANCE & RADAR HORIZON
		// ============================================================================
		public struct GalaxyRingDef
		{
			public float Radius;
			public int Count;
		}

		/// <summary>
		/// Spacious 8-Ring Galactic Layout (Radii from 700px to 7,000px — 4.5x larger than old cramped map).
		/// Guarantees every circular Moon node has generous breathing room (~190px to 440px between moons)
		/// and that visual 2D distance between any two Moons directly determines Fleet Travel Time!
		/// </summary>
		public static readonly GalaxyRingDef[] GalaxyRings = new GalaxyRingDef[]
		{
			new GalaxyRingDef { Radius = 700f,  Count = 10  }, // Ring 1: M-1..M-10
			new GalaxyRingDef { Radius = 1400f, Count = 30  }, // Ring 2: M-11..M-40
			new GalaxyRingDef { Radius = 2200f, Count = 60  }, // Ring 3: M-41..M-100
			new GalaxyRingDef { Radius = 3050f, Count = 100 }, // Ring 4: M-101..M-200
			new GalaxyRingDef { Radius = 3950f, Count = 150 }, // Ring 5: M-201..M-350
			new GalaxyRingDef { Radius = 4900f, Count = 200 }, // Ring 6: M-351..M-550
			new GalaxyRingDef { Radius = 5900f, Count = 220 }, // Ring 7: M-551..M-770
			new GalaxyRingDef { Radius = 7000f, Count = 230 }  // Ring 8: M-771..M-1000
		};

		public const float GalaxyOuterRadius = 7000f;
		public const float GalaxyBossOrbitRadius = 7650f;
		public const float GalaxyTerrainBorderHalfSize = 8400f; // Hard terrain border so players cannot scroll out of the Galaxy UI!

		private static Vector2[] _cachedMoonPositions;

		private static void EnsureMoonPositionsInitialized()
		{
			if (_cachedMoonPositions != null && _cachedMoonPositions.Length == TotalMoons + 1) return;
			_cachedMoonPositions = new Vector2[TotalMoons + 1];
			_cachedMoonPositions[0] = Vector2.Zero;

			int moonSeq = 1;
			for (int ringIdx = 0; ringIdx < GalaxyRings.Length; ringIdx++)
			{
				var r = GalaxyRings[ringIdx];
				float angleStep = Mathf.Tau / r.Count;
				for (int i = 0; i < r.Count; i++)
				{
					if (moonSeq > TotalMoons) break;
					// Golden-angle phase offset per ring so moons never form artificial spokes
					float angle = (i * angleStep) + (ringIdx * 0.618034f * Mathf.Tau);
					_cachedMoonPositions[moonSeq] = new Vector2(Mathf.Cos(angle) * r.Radius, Mathf.Sin(angle) * r.Radius);
					moonSeq++;
				}
			}
		}

		/// <summary>
		/// Returns the exact 2D Galaxy Map position of Moon #1..1000.
		/// </summary>
		public static Vector2 GetMoonGalaxyPosition(int moonId)
		{
			EnsureMoonPositionsInitialized();
			int clamped = Math.Clamp(moonId, 1, TotalMoons);
			return _cachedMoonPositions[clamped];
		}

		/// <summary>
		/// Returns the unit direction vector pointing from Origin Moon toward Target Moon in the Galaxy.
		/// Used both on the Galaxy Map and in MoonView so outbound/inbound fleets exit/enter the Moon screen at the exact real-world angle!
		/// </summary>
		public static Vector2 GetGalaxyDirectionBetweenMoons(int fromMoon, int toMoon)
		{
			if (fromMoon == toMoon) return new Vector2(1f, 0f);
			Vector2 pFrom = GetMoonGalaxyPosition(fromMoon);
			Vector2 pTo = GetMoonGalaxyPosition(toMoon);
			Vector2 diff = pTo - pFrom;
			return diff.LengthSquared() > 0.001f ? diff.Normalized() : new Vector2(1f, 0f);
		}

		/// <summary>
		/// Calculates the exact circular Radar Visibility Radius (in Galaxy world pixels) around a player's home moon.
		/// Level 1 Radar = 10% visibility of the galaxy's moons around the base's coordinates.
		/// Level 10 Radar = 100% visibility of the entire galaxy (including outer ring & Alien World Bosses).
		/// </summary>
		public static (int visibleMoonCount, float visibilityPercent, float worldRadiusPx) GetRadarVisibilityCircle(int homeMoonId, int radarLevel)
		{
			EnsureMoonPositionsInitialized();
			int clampedLvl = Math.Clamp(radarLevel, 1, 10);
			float pct = clampedLvl * 10.0f; // Lvl 1 = 10%, Lvl 2 = 20% ... Lvl 10 = 100%

			if (clampedLvl >= 10)
			{
				return (TotalMoons, 100f, GalaxyOuterRadius * 2.25f);
			}

			int targetCount = Math.Clamp((int)Math.Round(TotalMoons * (clampedLvl / 10.0)), 1, TotalMoons);
			Vector2 origin = GetMoonGalaxyPosition(homeMoonId);

			float[] distances = new float[TotalMoons];
			for (int m = 1; m <= TotalMoons; m++)
			{
				distances[m - 1] = origin.DistanceTo(_cachedMoonPositions[m]);
			}
			Array.Sort(distances);

			// Exact radius of a perfect circle centered on homeMoonId that encloses targetCount (e.g. 10% = 100) moons
			float radiusPx = distances[targetCount - 1] + 24f;
			return (targetCount, pct, radiusPx);
		}

		public static bool IsMoonVisibleInRadarCircle(int homeMoonId, int targetMoonId, int radarLevel)
		{
			if (homeMoonId == targetMoonId) return true;
			if (radarLevel >= 10) return true;
			if (radarLevel <= 0) return false;

			var (_, _, radiusPx) = GetRadarVisibilityCircle(homeMoonId, radarLevel);
			Vector2 origin = GetMoonGalaxyPosition(homeMoonId);
			Vector2 target = GetMoonGalaxyPosition(targetMoonId);
			return origin.DistanceTo(target) <= radiusPx;
		}

		public struct LunarSlotCoord
		{
			public int Slot;
			public float X;
			public float Y;
		}

		/// <summary>
		/// Upgraded Spacious 20-Slot Lunar Sphere Coordinates (Radius 520px Lunar Disc)
		/// Provides generous spacing for circular crater-pad nodes and fleet trajectory lines!
		/// </summary>
		public static readonly LunarSlotCoord[] LunarSlotCoords = new LunarSlotCoord[]
		{
			new LunarSlotCoord { Slot = 1,  X = -220, Y = -320 },
			new LunarSlotCoord { Slot = 2,  X = 0,    Y = -390 },
			new LunarSlotCoord { Slot = 3,  X = 220,  Y = -320 },
			new LunarSlotCoord { Slot = 4,  X = -370, Y = -180 },
			new LunarSlotCoord { Slot = 5,  X = -145, Y = -195 },
			new LunarSlotCoord { Slot = 6,  X = 145,  Y = -195 },
			new LunarSlotCoord { Slot = 7,  X = 370,  Y = -180 },
			new LunarSlotCoord { Slot = 8,  X = 0,    Y = -15  }, // Central Lunar Citadel Slot
			new LunarSlotCoord { Slot = 9,  X = -235, Y = -35  },
			new LunarSlotCoord { Slot = 10, X = 235,  Y = -35  },
			new LunarSlotCoord { Slot = 11, X = -390, Y = 85   },
			new LunarSlotCoord { Slot = 12, X = -150, Y = 120  },
			new LunarSlotCoord { Slot = 13, X = 150,  Y = 120  },
			new LunarSlotCoord { Slot = 14, X = 390,  Y = 85   },
			new LunarSlotCoord { Slot = 15, X = -270, Y = 255  },
			new LunarSlotCoord { Slot = 16, X = 0,    Y = 215  },
			new LunarSlotCoord { Slot = 17, X = 270,  Y = 255  },
			new LunarSlotCoord { Slot = 18, X = -175, Y = 375  },
			new LunarSlotCoord { Slot = 19, X = 0,    Y = 400  },
			new LunarSlotCoord { Slot = 20, X = 175,  Y = 375  }
		};

		public static Vector2 GetLunarSlotPosition(int slotId)
		{
			int clamped = Math.Clamp(slotId, 1, 20);
			var c = LunarSlotCoords[clamped - 1];
			return new Vector2(c.X, c.Y);
		}

		public static bool TryParseSectorCoordinate(string coordStr, out int moonId, out int slotId)
		{
			moonId = 100;
			slotId = 1;
			if (string.IsNullOrWhiteSpace(coordStr)) return false;

			var parts = coordStr.Trim().Split(':');
			if (parts.Length >= 3)
			{
				bool okM = int.TryParse(parts[1], out moonId);
				bool okS = int.TryParse(parts[2], out slotId);
				return okM && okS;
			}
			else if (parts.Length == 2)
			{
				bool okM = int.TryParse(parts[0], out moonId);
				bool okS = int.TryParse(parts[1], out slotId);
				return okM && okS;
			}
			return false;
		}

		public static readonly (string Id, string Name)[] SiegeTargetBuildings = new (string Id, string Name)[]
		{
			("hub_cmd",  "Industrial Core (Main Hub)"),
			("hub_silo", "Storage Silos"),
			("hub_mil",  "Orbital Shipyard"),
			("hub_flt",  "Fleet Station"),
			("hub_shd",  "Planetary Shield"),
			("hub_rng",  "Deep Space Radar"),
			("hub_ahq",  "Alliance HQ"),
			("hub_trd",  "Trade Logistics"),
			("hub_rsh",  "Research Directorate"),
			("hub_com",  "Commanders Spire"),
			("dist_e_0", "Power Station A"),
			("dist_e_1", "Power Station B"),
			("dist_e_2", "Power Station C"),
			("dist_i_0", "Iron Mine A"),
			("dist_i_1", "Iron Mine B"),
			("dist_i_2", "Iron Mine C"),
			("dist_t_0", "Titanium Smelter A"),
			("dist_t_1", "Titanium Smelter B"),
			("dist_t_2", "Titanium Smelter C"),
			("dist_h3_0", "H3 Distillery A"),
			("dist_h3_1", "H3 Distillery B"),
			("dist_h3_2", "H3 Distillery C"),
			("dist_h3_3", "H3 Distillery D"),
			("dist_h3_4", "H3 Distillery E")
		};

		public static string GetSiegeTargetDisplayName(string buildingId)
		{
			foreach (var item in SiegeTargetBuildings)
			{
				if (item.Id == buildingId) return item.Name;
			}
			return "Industrial Core (Main Hub)";
		}

		/// <summary>
		/// Calculates Laser Cannon (u6) structural siege damage against a target building.
		/// Blueprint Rule:
		/// - Each level of a building adds 100 HP (Max Level 20 = 2,000 HP).
		/// - 1 Laser Cannon deals 1 HP damage directly (NOT 1% / 20 HP damage).
		/// - An attack fleet ONLY deals damage to a building if at least 1 Laser Cannon (u6) exists & survives inside the fleet!
		/// - Example: 300 damage drops Level 20 (2,000 HP) to Level 17 (1,700 HP).
		/// - Fractional levels: 50 HP loss drops Level 18 (1,800 HP) to Level 17.5 (1,750 HP, stored as Level 18 with 50 damaged HP requiring repair before further upgrades).
		/// </summary>
		public static (int oldLevel, int newLevel, double oldEffectiveLevel, double newEffectiveLevel, int oldHp, int newHp, int hpDamageDealt, bool isBaseCompletelyDestroyed) ApplyLaserCannonSiegeDamage(
			Dictionary<string, int> defenderBuildingLevels,
			string targetBuildingId,
			int survivingLaserCannons)
		{
			if (defenderBuildingLevels == null) defenderBuildingLevels = new Dictionary<string, int>();
			if (string.IsNullOrEmpty(targetBuildingId)) targetBuildingId = "hub_cmd";

			int oldLvl = Math.Max(0, defenderBuildingLevels.GetValueOrDefault(targetBuildingId, 0));
			int partialHpKeyVal = defenderBuildingLevels.GetValueOrDefault($"__hp_{targetBuildingId}", oldLvl * 100);
			int oldHp = oldLvl > 0 ? Math.Clamp(partialHpKeyVal, 1, oldLvl * 100) : 0;
			double oldEffLvl = oldHp / 100.0;

			// Strict Blueprint Rule:
			// - Only deals structural damage if a Laser Cannon unit actually exists inside the fleet (survivingLaserCannons > 0).
			// - 1 Laser Cannon deals 1 HP damage directly (NOT 1% damage).
			int hpDamage = survivingLaserCannons > 0 ? survivingLaserCannons * 1 : 0;
			int newHp = Math.Max(0, oldHp - hpDamage);
			double newEffLvl = newHp / 100.0;
			int newLvl = (newHp <= 0) ? 0 : (int)Math.Ceiling(newHp / 100.0);

			defenderBuildingLevels[targetBuildingId] = newLvl;
			if (newHp >= newLvl * 100)
			{
				defenderBuildingLevels.Remove($"__hp_{targetBuildingId}");
			}
			else
			{
				defenderBuildingLevels[$"__hp_{targetBuildingId}"] = newHp;
			}

			// Check if ALL upgradable buildings on the base have dropped to Level 0 (0 HP total structural health)
			bool anyBuildingStanding = false;
			foreach (var b in SiegeTargetBuildings)
			{
				if (defenderBuildingLevels.GetValueOrDefault(b.Id, 0) > 0)
				{
					anyBuildingStanding = true;
					break;
				}
			}

			return (oldLvl, newLvl, oldEffLvl, newEffLvl, oldHp, newHp, hpDamage, !anyBuildingStanding);
		}

		public static int CalcFleetTravelTimeSeconds(int originMoon, int originSlot, int targetMoon, int targetSlot, Dictionary<string, int> units, float serverSpeed = 1.0f)
		{
			// 1. Measure real 2D Euclidean spatial distance on the Galaxy Map & Lunar Sphere!
			Vector2 moonPosA = GetMoonGalaxyPosition(originMoon);
			Vector2 moonPosB = GetMoonGalaxyPosition(targetMoon);
			float galaxyPixelDist = (originMoon == targetMoon) ? 0f : moonPosA.DistanceTo(moonPosB);

			Vector2 slotPosA = GetLunarSlotPosition(originSlot);
			Vector2 slotPosB = GetLunarSlotPosition(targetSlot);
			float lunarPixelDist = slotPosA.DistanceTo(slotPosB);

			// Scale spatial pixel distance into tactical flight distance units so visual distance 100% matches travel time
			int totalDist = (int)Math.Round((galaxyPixelDist * 1.65f) + (lunarPixelDist * 0.85f));

			float slowestSpeed = 999f;
			if (units != null)
			{
				foreach (var kvp in units)
				{
					if (kvp.Value > 0)
					{
						var u = GameData.Units.Find(x => x.Id == kvp.Key);
						if (u != null && u.Speed > 0f && u.Speed < slowestSpeed)
						{
							slowestSpeed = u.Speed;
						}
					}
				}
			}
			if (slowestSpeed >= 999f) slowestSpeed = 100f;

			float speedRatio = slowestSpeed / 100f;
			float rawSeconds = totalDist / speedRatio;
			int accelSec = (int)Math.Floor(rawSeconds / Math.Max(0.1f, serverSpeed));

			int minTime = (totalDist > 0)
				? (int)Math.Max(20, Math.Round((originMoon == targetMoon ? 120f : 360f) / Math.Max(1.0f, serverSpeed)))
				: 30;
			return Math.Max(minTime, accelSec);
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
		public const int CanvasWidth = 5600;
		public const int CanvasHeight = 3200;

		public const int GridCols = 64; 
		public const int GridRows = 64; 
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
		// 2. DYNAMIC COMPLEXITY TIERS & PROGRESSION MATRIX
		// ============================================================================

		public static float GetBuildingComplexityTier(string buildingId, int targetLevel)
		{
			string b = (buildingId ?? "").ToLower();
			int lvl = Math.Max(1, targetLevel);

			// Non-upgradable facilities
			if (b == "hub_arm" || b == "hub_mgd") return 0.0f;

			// 1. Industrial Core
			if (b == "hub_cmd" || b == "core")
			{
				if (lvl <= 5) return 2.0f;
				if (lvl <= 10) return 3.0f;
				if (lvl <= 15) return 4.0f;
				return 5.0f;
			}

			// 2. Sub-Mine Districts A, B, D, E (Foundational Starter Nodes)
			if (b == "dist_e_0" || b == "dist_e_1" ||
				b == "dist_i_0" || b == "dist_i_1" ||
				b == "dist_t_0" || b == "dist_t_1" ||
				b == "dist_h3_0" || b == "dist_h3_1" || b == "dist_h3_3" || b == "dist_h3_4")
			{
				if (lvl <= 5) return 0.5f;
				if (lvl <= 10) return 1.0f;
				if (lvl <= 15) return 2.0f;
				if (lvl <= 20) return 3.0f;
				if (lvl <= 25) return 4.0f;
				return 5.0f;
			}

			// 3. Sub-Mine Anchor Node C (District Capstone Node - dist_*_2)
			if (b == "dist_e_2" || b == "dist_i_2" || b == "dist_t_2" || b == "dist_h3_2")
			{
				if (lvl <= 5) return 1.0f;
				if (lvl <= 10) return 2.0f;
				if (lvl <= 15) return 3.0f;
				if (lvl <= 20) return 3.0f;
				if (lvl <= 25) return 4.0f;
				return 5.0f;
			}

			// 4. Fleet Station
			if (b == "hub_flt")
			{
				if (lvl <= 5) return 3.0f;
				if (lvl <= 10) return 4.0f;
				if (lvl <= 15) return 4.0f;
				return 5.0f;
			}

			// 5. Trade Logistics (Max Lvl 10)
			if (b == "hub_trd")
			{
				if (lvl <= 5) return 3.0f;
				return 4.0f;
			}

			// 6. Orbital Shipyard
			if (b == "hub_mil")
			{
				if (lvl <= 5) return 2.0f;
				if (lvl <= 10) return 3.0f;
				if (lvl <= 15) return 4.0f;
				return 5.0f;
			}

			// 7. Planetary Shield (Max Lvl 10): Lvl 1-3: Tier 2 · Lvl 4-6: Tier 3 · Lvl 7: Tier 4 · Lvl 8-10: Tier 5
			if (b == "hub_shd")
			{
				if (lvl <= 3) return 2.0f;
				if (lvl <= 6) return 3.0f;
				if (lvl == 7) return 4.0f;
				return 5.0f;
			}

			// 7B. The Deep Radar (Max Lvl 10)
			if (b == "hub_rng")
			{
				if (lvl <= 5) return 2.0f;
				return 5.0f;
			}

			// 8. Alliance HQ & Storage Silos
			if (b == "hub_ahq" || b == "hub_silo")
			{
				if (lvl <= 5) return 1.0f;
				if (lvl <= 10) return 2.0f;
				if (lvl <= 15) return 3.0f;
				return 4.0f;
			}

			// 9. Research Directorate
			if (b == "hub_rsh")
			{
				if (lvl <= 5) return 2.0f;
				if (lvl <= 10) return 3.0f;
				if (lvl <= 15) return 4.0f;
				return 5.0f;
			}

			// 10. Commanders Spire
			if (b == "hub_com")
			{
				if (lvl <= 5) return 2.0f;
				if (lvl <= 10) return 4.0f;
				return 5.0f;
			}

			return 1.0f;
		}

		public static (float costMultiplier, float timeMultiplier, int timeFloorSec) GetComplexityMultipliers(float tier)
		{
			if (tier <= 0.0f) return (0.0f, 0.0f, 0);
			if (tier <= 0.5f) return (0.7f, 0.5f, 30);      // 30s floor (Fast addictive starter clicks)
			if (tier <= 1.0f) return (1.0f, 1.0f, 60);      // 60s (1 min floor)
			if (tier <= 2.0f) return (1.2f, 1.5f, 300);     // 300s (5 min floor)
			if (tier <= 3.0f) return (1.5f, 2.5f, 900);     // 900s (15 min floor)
			if (tier <= 4.0f) return (2.0f, 4.0f, 1800);    // 1800s (30 min floor)
			return (3.0f, 6.0f, 3600);                      // 3600s (1 hour floor)
		}

		public static bool IsBuildingUpgradable(string buildingId)
		{
			string b = (buildingId ?? "").ToLower();
			return b != "hub_arm" && b != "hub_mgd";
		}

		public static int GetBuildingMaxLevel(string buildingId)
		{
			string b = (buildingId ?? "").ToLower();
			if (b == "hub_arm" || b == "hub_mgd") return 1;
			if (b == "hub_trd" || b == "hub_shd" || b == "hub_rng") return 10;
			if (b.StartsWith("dist_")) return 30;
			return 20;
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
		// 5. BLUEPRINT STRUCTURAL STATS: HP, FRACTIONAL LEVELS & REPAIR COSTS
		// ============================================================================
		public static int GetStructureHp(int level)
		{
			return Math.Max(0, level) * 100;
		}

		public static string FormatEffectiveLevel(double effectiveLevel)
		{
			double rounded = Math.Round(effectiveLevel, 2);
			if (Math.Abs(rounded - Math.Round(rounded)) < 0.005)
			{
				return $"{(int)Math.Round(rounded)}";
			}
			return $"{rounded:0.##}";
		}

		public static (int currentHp, int maxHp, int healthPercent, int damagedHp, double effectiveLevel, bool isDamaged) GetStructureHealthDetails(
			string buildingId,
			int currentLevel,
			Dictionary<string, int> allBuildingLevels)
		{
			if (currentLevel <= 0) return (0, 0, 0, 0, 0.0, false);

			int maxHp = currentLevel * 100;
			int curHp = maxHp;
			if (allBuildingLevels != null && allBuildingLevels.TryGetValue($"__hp_{buildingId}", out int savedHp))
			{
				curHp = Math.Clamp(savedHp, 1, maxHp);
			}

			int damagedHp = Math.Max(0, maxHp - curHp);
			double effectiveLvl = curHp / 100.0;
			int pct = (int)Math.Round((curHp / (double)maxHp) * 100.0);
			return (curHp, maxHp, pct, damagedHp, effectiveLvl, damagedHp > 0);
		}

		/// <summary>
		/// Calculates the exact resource cost to repair a damaged building back to full structural HP for its current level.
		/// Blueprint Rule:
		/// - Each building level has 100 HP.
		/// - Fractional levels occur when a building takes < 100 HP damage within its current level bracket (e.g., 50 HP loss drops Level 18 [1,800 HP] to Level 17.5 [1,750 HP]).
		/// - Repair costs 1% of the specific level's upgrade cost per damaged HP (so 50 damaged HP costs 50% of that level's upgrade cost).
		/// - Repair is required before further upgrades for affected levels!
		/// </summary>
		public static (long costE, long costI, long costT, long costH3, int damagedHp, double effectiveLevel) CalcBuildingRepairCost(
			string buildingId,
			int currentLevel,
			Dictionary<string, int> allBuildingLevels)
		{
			var hp = GetStructureHealthDetails(buildingId, currentLevel, allBuildingLevels);
			if (!hp.isDamaged || hp.damagedHp <= 0 || currentLevel <= 0 || !IsBuildingUpgradable(buildingId))
			{
				return (0, 0, 0, 0, 0, currentLevel);
			}

			// Upgrade cost to reach this specific level (from currentLevel - 1 -> currentLevel)
			int baseLvlForSpecificLevelCost = Math.Max(0, currentLevel - 1);
			var levelUpgradeMetrics = GetBuildingUpgradeMetrics(buildingId, baseLvlForSpecificLevelCost);

			// 1% (0.01) of the specific level's upgrade cost per 1 damaged HP
			double repairFraction = hp.damagedHp * 0.01;
			long repE = levelUpgradeMetrics.costE > 0 ? Math.Max(1L, (long)Math.Ceiling(levelUpgradeMetrics.costE * repairFraction)) : 0L;
			long repI = levelUpgradeMetrics.costI > 0 ? Math.Max(1L, (long)Math.Ceiling(levelUpgradeMetrics.costI * repairFraction)) : 0L;
			long repT = levelUpgradeMetrics.costT > 0 ? Math.Max(1L, (long)Math.Ceiling(levelUpgradeMetrics.costT * repairFraction)) : 0L;
			long repH3 = levelUpgradeMetrics.costH3 > 0 ? Math.Max(1L, (long)Math.Ceiling(levelUpgradeMetrics.costH3 * repairFraction)) : 0L;

			return (repE, repI, repT, repH3, hp.damagedHp, hp.effectiveLevel);
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
		// 6. BLUEPRINT COST CALCULATIONS (OVERLOADS FOR MODAL & HUD)
		// ============================================================================

		// Overload A (2 Arguments): Directly called by BuildingInspectorModal
		public static (long e, long i, long t, long h) CalcSubMineUpgradeCost(string districtCode, int currentLevel)
		{
			string dist = (districtCode ?? "E").ToLower();
			return CalcSubMineUpgradeCost($"dist_{dist}_0", districtCode, currentLevel);
		}

		// Overload B (3 Arguments): Called with explicit buildingId
		public static (long e, long i, long t, long h) CalcSubMineUpgradeCost(string buildingId, string districtCode, int currentLevel)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			float tier = GetBuildingComplexityTier(buildingId, nextLvl);
			var (cMult, _, _) = GetComplexityMultipliers(tier);

			double scale = Math.Pow(1.38, nextLvl - 1); 
			long baseTotal = (long)(100 * scale * cMult); 

			string dist = (districtCode ?? "E").ToUpper();

			if (dist == "E") return (Math.Max(10, (long)(baseTotal * 0.333)), Math.Max(10, (long)(baseTotal * 0.500)), Math.Max(5, (long)(baseTotal * 0.167)), 0);
			else if (dist == "I") return (Math.Max(10, (long)(baseTotal * 0.500)), Math.Max(10, (long)(baseTotal * 0.167)), Math.Max(5, (long)(baseTotal * 0.333)), 0);
			else if (dist == "T") return (Math.Max(5, (long)(baseTotal * 0.167)), Math.Max(10, (long)(baseTotal * 0.333)), Math.Max(10, (long)(baseTotal * 0.500)), 0);
			else return (Math.Max(20, (long)(100 * scale * cMult)), Math.Max(15, (long)(80 * scale * cMult)), Math.Max(10, (long)(70 * scale * cMult)), 0);
		}

		// Overload A (5 Arguments): Directly called by BuildingInspectorModal
		public static (long e, long i, long t, long h) CalcCost(int lvl, long baseE, long baseI, long baseT, long baseH = 0)
		{
			int safeLvl = Math.Max(0, lvl);
			double scaling = 1.38; 
			return (
				(long)Math.Floor(baseE * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseI * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseT * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseH * Math.Pow(scaling, safeLvl))
			);
		}

		// Overload B (6 Arguments): Called with explicit buildingId
		public static (long e, long i, long t, long h) CalcCost(string buildingId, int lvl, long baseE, long baseI, long baseT, long baseH = 0)
		{
			int nextLvl = Math.Max(1, lvl + 1);
			float tier = GetBuildingComplexityTier(buildingId, nextLvl);
			var (cMult, _, _) = GetComplexityMultipliers(tier);

			double scaling = 1.38; 
			return (
				(long)Math.Floor(baseE * Math.Pow(scaling, lvl) * cMult),
				(long)Math.Floor(baseI * Math.Pow(scaling, lvl) * cMult),
				(long)Math.Floor(baseT * Math.Pow(scaling, lvl) * cMult),
				(long)Math.Floor(baseH * Math.Pow(scaling, lvl) * cMult)
			);
		}

		public static (long e, long i, long t, long h) CalcIndustrialCoreCost(int currentLevel)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			float tier = GetBuildingComplexityTier("hub_cmd", nextLvl);
			var (cMult, _, _) = GetComplexityMultipliers(tier);

			double factor = Math.Pow(1.40, nextLvl - 1) * (cMult / 1.2f);
			return ((long)(350 * factor), (long)(350 * factor), (long)(150 * factor), 0);
		}

		public static (long e, long i, long t, long h) CalcStorageSiloCost(int currentLevel)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			float tier = GetBuildingComplexityTier("hub_silo", nextLvl);
			var (cMult, _, _) = GetComplexityMultipliers(tier);

			double factor = Math.Pow(1.30, nextLvl - 1) * cMult;
			return ((long)(150 * factor), (long)(200 * factor), (long)(100 * factor), 0);
		}

		// ============================================================================
		// 7. EXPONENTIAL BUILD TIME CALCULATIONS (OVERLOADS FOR MODAL & HUD)
		// ============================================================================

		// Overload A (1 or 2 Arguments): Directly called by BuildingInspectorModal
		public static int GetSubMineBuildTimeSec(int currentLevel, float serverSpeed = 1.0f)
		{
			return GetSubMineBuildTimeSec("dist_e_0", currentLevel, serverSpeed);
		}

		// Overload B (2 or 3 Arguments): Called with explicit buildingId
		public static int GetSubMineBuildTimeSec(string buildingId, int currentLevel, float serverSpeed = 1.0f)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			float tier = GetBuildingComplexityTier(buildingId, nextLvl);
			var (_, tMult, floorSec) = GetComplexityMultipliers(tier);

			double baseSec = 45.0 * Math.Pow(1.30, nextLvl - 1) * tMult;
			int effectiveSec = (int)Math.Max(floorSec, Math.Floor(baseSec));

			return (int)Math.Max(1, Math.Floor(effectiveSec / Math.Max(1.0f, serverSpeed)));
		}

		public static int GetIndustrialCoreBuildTimeSec(int currentLevel, float serverSpeed = 1.0f)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			float tier = GetBuildingComplexityTier("hub_cmd", nextLvl);
			var (_, tMult, floorSec) = GetComplexityMultipliers(tier);

			double baseSec = 90.0 * Math.Pow(1.35, nextLvl - 1) * (tMult / 1.5f);
			int effectiveSec = (int)Math.Max(floorSec, Math.Floor(baseSec));

			return (int)Math.Max(1, Math.Floor(effectiveSec / Math.Max(1.0f, serverSpeed)));
		}

		public static int GetStorageSiloBuildTimeSec(int currentLevel, float serverSpeed = 1.0f)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			float tier = GetBuildingComplexityTier("hub_silo", nextLvl);
			var (_, tMult, floorSec) = GetComplexityMultipliers(tier);

			double baseSec = 60.0 * Math.Pow(1.30, nextLvl - 1) * tMult;
			int effectiveSec = (int)Math.Max(floorSec, Math.Floor(baseSec));

			return (int)Math.Max(1, Math.Floor(effectiveSec / Math.Max(1.0f, serverSpeed)));
		}

		/// <summary>
		/// Master universal metrics evaluator: Computes Cost, Build Time, Complexity Tier, and Time Floor for ANY building.
		/// </summary>
		public static (long costE, long costI, long costT, long costH3, int buildTimeSec, float tier) GetBuildingUpgradeMetrics(
			string buildingId, int currentLevel, float serverSpeed = 1.0f)
		{
			string b = (buildingId ?? "").ToLower();
			if (!IsBuildingUpgradable(b))
			{
				return (0, 0, 0, 0, 0, 0.0f);
			}

			int nextLvl = Math.Max(1, currentLevel + 1);
			float tier = GetBuildingComplexityTier(b, nextLvl);
			var (cMult, tMult, floorSec) = GetComplexityMultipliers(tier);

			// 1. Sub-Mines
			if (b.StartsWith("dist_"))
			{
				string dist = b.Substring(5, b.IndexOf('_', 5) - 5).ToUpper();
				var costs = CalcSubMineUpgradeCost(b, dist, currentLevel);
				int time = GetSubMineBuildTimeSec(b, currentLevel, serverSpeed);
				return (costs.e, costs.i, costs.t, costs.h, time, tier);
			}

			// 2. Industrial Core
			if (b == "hub_cmd")
			{
				var costs = CalcIndustrialCoreCost(currentLevel);
				int time = GetIndustrialCoreBuildTimeSec(currentLevel, serverSpeed);
				return (costs.e, costs.i, costs.t, costs.h, time, tier);
			}

			// 3. Storage Silos
			if (b == "hub_silo")
			{
				var costs = CalcStorageSiloCost(currentLevel);
				int time = GetStorageSiloBuildTimeSec(currentLevel, serverSpeed);
				return (costs.e, costs.i, costs.t, costs.h, time, tier);
			}

			// 4. All Other Hub Buildings
			long baseE = 200;
			long baseI = 200;
			long baseT = 100;
			long baseH = 0;

			if (b == "hub_mil") { baseE = 300; baseI = 400; baseT = 200; }
			else if (b == "hub_shd") { baseE = 500; baseI = 200; baseT = 400; }
			else if (b == "hub_com") { baseE = 400; baseI = 400; baseT = 300; }
			else if (b == "hub_flt") { baseE = 250; baseI = 350; baseT = 150; }

			double costFactor = Math.Pow(1.36, nextLvl - 1) * cMult;
			long finalE = (long)Math.Max(50, baseE * costFactor);
			long finalI = (long)Math.Max(50, baseI * costFactor);
			long finalT = (long)Math.Max(25, baseT * costFactor);
			long finalH = (long)Math.Max(0, baseH * costFactor);

			double baseSec = 80.0 * Math.Pow(1.32, nextLvl - 1) * tMult;
			int effectiveSec = (int)Math.Max(floorSec, Math.Floor(baseSec));
			int finalTime = (int)Math.Max(1, Math.Floor(effectiveSec / Math.Max(1.0f, serverSpeed)));

			return (finalE, finalI, finalT, finalH, finalTime, tier);
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

		public static List<RequirementStatus> GetFacilityRequirements(
			string buildingId, int targetLevel, string districtCode, int slotIndex, Dictionary<string, int> simLevels)
		{
			var reqs = new List<RequirementStatus>();

			if (buildingId == "hub_cmd")
			{
				return CheckIndustrialCoreRequirements(targetLevel, simLevels).Requirements;
			}

			var coreUnlock = CheckIndustrialCoreUnlock(buildingId, 1);
			// Every sub-building or facility also requires Industrial Core >= targetLevel (capped at Core Lvl 20, which unlocks Lvl 30 sub-mines)
			int capReqCoreLvl = Math.Min(20, Math.Max(1, targetLevel));
			int effectiveReqCoreLvl = Math.Max(coreUnlock.requiredCoreLevel, capReqCoreLvl);

			if (effectiveReqCoreLvl > 1 || buildingId == "hub_silo" || buildingId == "hub_mgd") 
			{
				int simCoreLvl = simLevels.GetValueOrDefault("hub_cmd", 1);
				reqs.Add(new RequirementStatus {
					BuildingId = "hub_cmd",
					BuildingName = "Industrial Core",
					RequiredLevel = effectiveReqCoreLvl,
					CurrentLevel = simCoreLvl,
					IsMet = simCoreLvl >= effectiveReqCoreLvl
				});
			}

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
				case 2:
					break;
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

		// ============================================================================
		// 10. PLANETARY SHIELD, RESEARCH DIRECTORATE & ALLIANCE SCIENCE MITIGATION MATH
		// ============================================================================
		public const string ResearchDirectorateShieldKey = "__rsh_shield_mit";
		public const string ResearchDirectorateShieldTimerKey = "__rsh_shield_end_sec";
		public const string AllianceShieldScienceKey = "sci_shield_mit";
		public const int AllianceScienceUpgradeDurationSec = 3600; // 1 Hour (3,600s) strict upgrade timer once Leader/Officer presses Upgrade!

		public static long GetPlanetaryShieldDefensePower(int shieldLevel)
		{
			return Math.Clamp(shieldLevel, 0, 10) * 450L;
		}

		/// <summary>
		/// Calculates the complete Defender Casualty Mitigation breakdown:
		/// - Planetary Shield Building (hub_shd, Lvl 1-10): +1% per level (up to 10% at Lvl 10).
		/// - Research Directorate First Research (__rsh_shield_mit, Lvl 1-20): +1% per level (up to 20% at Lvl 20) — leaves 15% remaining to reach the 35% cap, or 5% after Shield Lvl 10 + Research Lvl 20.
		/// - Alliance Science ("Planetary Shield Matrix", Lvl 1-10): +1% per level (up to 10% at Lvl 10) — shared by all alliance members!
		/// - Total Defender Casualty Mitigation is strictly capped at 35% (0.35).
		/// </summary>
		public static (int shieldBuildingPct, int researchDirectoratePct, int allianceSciencePct, int uncappedTotalPct, int cappedTotalPct, double mitigationRatio) GetDefenderCasualtyMitigationBreakdown(
			int shieldLevel,
			int researchDirectorateShieldLvl,
			int allianceShieldScienceLvl)
		{
			int shdPct = Math.Clamp(shieldLevel, 0, 10) * 1; // +1% per Shield Level (Max 10%)
			int rshPct = Math.Clamp(researchDirectorateShieldLvl, 0, 20) * 1; // +1% per Research Level (Max 20%)
			int aliPct = Math.Clamp(allianceShieldScienceLvl, 0, 10) * 1; // +1% per Alliance Science Level (Max 10%)

			int uncapped = shdPct + rshPct + aliPct;
			int capped = Math.Min(35, uncapped);
			return (shdPct, rshPct, aliPct, uncapped, capped, capped / 100.0);
		}

		/// <summary>
		/// First Research inside Research Directorate (hub_rsh):
		/// "HARMONIC SHIELD DEFLECTION MATRIX" (Levels 1-20, +1% Planetary Shield Defender Casualty Mitigation per Level).
		/// </summary>
		public static (long costE, long costI, long costT, long costH3, int researchTimeSec, int reqResearchDirectorateLvl) GetResearchDirectorateShieldMetrics(
			int currentResearchLevel,
			float serverSpeed = 1.0f)
		{
			int nextLvl = Math.Clamp(currentResearchLevel + 1, 1, 20);
			int reqRshBldLvl = Math.Clamp(nextLvl, 1, 20);

			float tier = nextLvl <= 5 ? 2.0f : (nextLvl <= 10 ? 3.0f : (nextLvl <= 15 ? 4.0f : 5.0f));
			var (cMult, tMult, floorSec) = GetComplexityMultipliers(tier);

			double scale = Math.Pow(1.34, nextLvl - 1) * cMult;
			long costE = (long)Math.Max(300, Math.Round(450 * scale));
			long costI = (long)Math.Max(200, Math.Round(300 * scale));
			long costT = (long)Math.Max(250, Math.Round(380 * scale));
			long costH3 = nextLvl >= 6 ? (long)Math.Max(100, Math.Round(160 * scale)) : 0L;

			double rawSec = 75.0 * Math.Pow(1.28, nextLvl - 1) * tMult;
			int effectiveSec = (int)Math.Max(floorSec, Math.Floor(rawSec));
			int finalSec = (int)Math.Max(1, Math.Floor(effectiveSec / Math.Max(1.0f, serverSpeed)));

			return (costE, costI, costT, costH3, finalSec, reqRshBldLvl);
		}

		/// <summary>
		/// Expensive Coalition-Wide Alliance Science Upgrade Cost Pool (Members contribute together; once 100% funded,
		/// only the Alliance Leader or Assigned Officers may press UPGRADE, which takes 1 Hour / 3,600s to complete!).
		/// </summary>
		public static (long reqE, long reqI, long reqT, long reqH3) GetAllianceSciencePoolRequirement(string scienceKey, int currentLevel)
		{
			int nextLvl = Math.Max(1, currentLevel + 1);
			double scale = Math.Pow(1.55, nextLvl - 1);

			if (scienceKey == AllianceShieldScienceKey)
			{
				// Planetary Shield Casualty Mitigation Matrix (Levels 1-10, +1% Defender Casualty Mitigation per Level = +10% Max)
				// Expensive coalition-scale costs designed for all alliance members to contribute together!
				return (
					(long)Math.Round(25000 * scale),
					(long)Math.Round(18000 * scale),
					(long)Math.Round(22000 * scale),
					(long)Math.Round(8000 * scale)
				);
			}

			return (
				(long)Math.Round(20000 * scale),
				(long)Math.Round(20000 * scale),
				(long)Math.Round(15000 * scale),
				(long)Math.Round(5000 * scale)
			);
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
