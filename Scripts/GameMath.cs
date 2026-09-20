using System;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER MATHEMATICAL ENGINE (C# Godot 4 Edition)
	/// Precision Calibrated 2.5D Isometric Projection, Storage Scaling & Orbital Telemetry.
	/// </summary>
	public static class GameMath
	{
		// ============================================================================
		// 1. ISOMETRIC 2.5D GROUND PROJECTION METRICS (36x36 EXPANDED GRID)
		// ============================================================================
		public const int CanvasWidth = 4000;
		public const int CanvasHeight = 2200;

		public const int GridCols = 36;
		public const int GridRows = 36;
		public const float TileWidth = 110f;  // Diamond X-span
		public const float TileHeight = 55f;  // Diamond Y-span (2:1 Ratio)

		public const float IsoOriginX = CanvasWidth / 2f;
		public const float IsoOriginY = 60f;

		// Master Galactic Scale (Recalibrated to 1,000 Moons)
		public const int TotalMoons = 1000;

		public const double SixMonthsMs = 180.0 * 24.0 * 60.0 * 60.0 * 1000.0;
		public const double ThreeWeeksMs = 21.0 * 24.0 * 60.0 * 60.0 * 1000.0;

		/// <summary>
		/// Converts Grid (col, row) coordinates into Canvas ground pixel centers.
		/// </summary>
		public static Vector2 GridToIsometric(float gridX, float gridY)
		{
			float screenX = IsoOriginX + (gridX - gridY) * (TileWidth / 2f);
			float screenY = IsoOriginY + (gridX + gridY) * (TileHeight / 2f);
			return new Vector2(screenX, screenY);
		}

		/// <summary>
		/// Inverse Projection: Canvas touch coordinates (canvasX, canvasY) to Isometric Grid (col, row).
		/// </summary>
		public static Vector2I ScreenToGrid(float canvasX, float canvasY)
		{
			float dx = canvasX - IsoOriginX;
			float dy = canvasY - IsoOriginY;

			int col = (int)Math.Floor((dy / (TileHeight / 2f) + dx / (TileWidth / 2f)) / 2f);
			int row = (int)Math.Floor((dy / (TileHeight / 2f) - dx / (TileWidth / 2f)) / 2f);

			col = Math.Clamp(col, 0, GridCols - 1);
			row = Math.Clamp(row, 0, GridRows - 1);

			return new Vector2I(col, row);
		}

		/// <summary>
		/// Sequential Tile Indexing: 1-indexed bottom-left to top-right.
		/// </summary>
		public static int GetTileIndex(int col, int row)
		{
			int rowFromBottom = (GridRows - 1) - row;
			return rowFromBottom * GridCols + col + 1;
		}

		// ============================================================================
		// 2. FOOTPRINT VALIDATION & OVERLAP CHECKER
		// ============================================================================
		public struct CellCoord
		{
			public int Col;
			public int Row;
			public CellCoord(int c, int r) { Col = c; Row = r; }
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
		// 3. ECONOMY & UPGRADE MATHEMATICAL FORMULAS
		// ============================================================================
		public static long CalcStorageCap(int lvl)
		{
			long baseCap = 50000;
			int safeLvl = Math.Max(1, lvl);
			return (long)Math.Floor(baseCap * Math.Pow(1.17, safeLvl - 1));
		}

		public static float CalcOutput(int lvl, bool isH3 = false)
		{
			int safeLvl = Math.Max(0, lvl);
			float baseRate = isH3 ? 450f : 210f;
			return safeLvl * baseRate;
		}

		public static (long e, long i, long t, long h) CalcCost(int lvl, long baseE, long baseI, long baseT, long baseH = 0)
		{
			int safeLvl = Math.Max(0, lvl);
			double scaling = 1.35;
			return (
				(long)Math.Floor(baseE * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseI * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseT * Math.Pow(scaling, safeLvl)),
				(long)Math.Floor(baseH * Math.Pow(scaling, safeLvl))
			);
		}

		public static (long e, long i, long t, long h) CalcHubCost(int lvl, long baseE, long baseI, long baseT, long baseH = 0)
		{
			int safeLvl = Math.Max(0, lvl);
			double factor = 1.55;
			return (
				(long)Math.Floor(baseE * Math.Pow(factor, safeLvl)),
				(long)Math.Floor(baseI * Math.Pow(factor, safeLvl)),
				(long)Math.Floor(baseT * Math.Pow(factor, safeLvl)),
				(long)Math.Floor(baseH * Math.Pow(factor, safeLvl))
			);
		}

		public static float GetWallMitigation(int lvl)
		{
			int safeLvl = Math.Max(0, lvl);
			return Math.Min(0.75f, safeLvl * 0.015f);
		}

		// ============================================================================
		// 4. SECTOR ID PARSER & FLEET NAVIGATION
		// ============================================================================
		public static (string serverId, int moon, int baseSlot) ParseSectorId(string sectorId)
		{
			if (string.IsNullOrEmpty(sectorId))
				return ("S1", 100, 1);

			string[] parts = sectorId.Split(':');
			string serverId = parts.Length > 0 ? parts[0] : "S1";
			int moon = parts.Length > 1 && int.TryParse(parts[1], out int m) ? m : 100;
			int baseSlot = parts.Length > 2 && int.TryParse(parts[2], out int b) ? b : 1;

			return (serverId, moon, baseSlot);
		}

		public static int CalcTravelTime(string originId, string targetId, float slowestUnitSpeed = 100f, float serverSpeed = 1f)
		{
			var o = ParseSectorId(originId);
			var t = ParseSectorId(targetId);

			int mDiff = Math.Abs(o.moon - t.moon);
			int moonDist = Math.Min(mDiff, TotalMoons - mDiff);
			int baseDist = Math.Abs(o.baseSlot - t.baseSlot);

			int totalDist = moonDist * 500 + baseDist * 50;
			float speedRatio = Math.Max(10f, slowestUnitSpeed) / 100f;

			double travelTimeSeconds = totalDist / speedRatio;
			int acceleratedSeconds = (int)Math.Floor(travelTimeSeconds / Math.Max(1f, serverSpeed));

			int minTime = 60;
			if (totalDist > 0)
			{
				minTime = (o.moon == t.moon) ? 120 : 600;
			}

			return Math.Max(minTime, acceleratedSeconds);
		}

		// ============================================================================
		// 5. TIME FORMATTER & ORBITAL BOSS TELEMETRY
		// ============================================================================
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
