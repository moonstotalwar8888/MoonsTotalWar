using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER 2.5D BASE VIEWPORT (v50.0 Expanded 8x8 / 6x6 / 4x4 Megastructure Grid Edition)
	/// - Central Citadel: 8x8 (64 Tiles) Megastructure Industrial Core seated on ground diamond.
	/// - Storage Silos: 6x6 (36 Tiles) Pressurized Containment Vault Facility.
	/// - All Other Facilities & Sub-Mines: 4x4 (16 Tiles) Footprints with perfectly adapted sprites & diamond seating.
	/// - Dedicated Badge Layer (ZIndex = 4000): Prioritizes building names and level displays so they NEVER hide behind any building art.
	/// - Centralized Diamond Hitbox Testing: Eliminates false clicks outside node boundaries.
	/// - Drag vs Click Disambiguation: Moving the camera will never trigger accidental building opens.
	/// - Edit Mode Dynamic Grid: Grid lines hidden during combat/command, illuminated in Edit Mode.
	/// - Full Supabase Cloud Sync & Realtime Construction Queue integration.
	/// </summary>
	public partial class BaseView : Node2D
	{
		[Export] public Texture2D TerrainTexture;
		[Export] public Texture2D IndustrialCoreTexture;
		[Export] public Texture2D StorageSiloTexture;
		[Export] public Texture2D PowerStationTexture;
		[Export] public Texture2D IronMineTexture;
		[Export] public Texture2D TitaniumSmelterTexture;
		[Export] public Texture2D H3DistilleryTexture;
		[Export] public Texture2D PlanetaryShieldTexture;
		[Export] public Texture2D OrbitalShipyardTexture;
		[Export] public Texture2D GalaxyGoldExchangeTexture;
		[Export] public Texture2D GarrisonArmoryTexture;
		[Export] public Texture2D AllianceHQTexture;
		[Export] public Texture2D CommandersSpireTexture;
		[Export] public Texture2D FleetStationTexture;
		[Export] public Texture2D DeepRadarTexture;
		[Export] public Texture2D TradeLogisticsTexture;
		[Export] public Texture2D ResearchDirectorateTexture;

		public const int GRID_COLS = 64; 
		public const int GRID_ROWS = 64; 
		public const int LAYOUT_SCHEMA_VERSION = 4; // v4 = 64x64 grid with 8x8 Core, 6x6 Silo, 4x4 Facilities spread out layout 

		public const float TILE_WIDTH_HALF = 40.0f;
		public const float TILE_HEIGHT_HALF = 22.5f;

		public const float TERRAIN_WIDTH = 5600.0f;
		public const float TERRAIN_HEIGHT = 3200.0f;

		public const float MIN_ZOOM = 0.42f;
		public const float MAX_ZOOM = 2.0f;

		// Camera Viewport Controller
		private Camera2D _camera;
		private Vector2 _targetCameraPos;
		private float _targetZoom = 0.78f;
		private bool _isDraggingCamera = false;
		private Vector2 _dragStartMousePos;
		private Vector2 _dragStartCameraPos;
		private float _dragAccumulatedDistance = 0.0f;
		private Vector2 _terrainCenterPos = Vector2.Zero;
		private readonly Dictionary<int, Vector2> _activeTouches = new Dictionary<int, Vector2>();
		private bool _wasMultiTouchGesture = false;
		private float _badgeAnimTime = 0.0f;

		// Nodes
		private Sprite2D _terrainSprite;
		private Node2D _gridLineCanvas;
		private Node2D _buildingContainer;
		private Node2D _badgeContainer;
		private Node2D _teleportReticleContainer;

		// Canvas Layers
		private CanvasLayer _hudLayer;
		private CanvasLayer _modalLayer;
		private BaseHUD _hudInstance;
		private BuildingInspectorModal _inspectorModal;
		private ColorRect _cloudSyncVeil;
		private Label _cloudSyncLabel;

		// Sub-Mine District Arrays
		private int[] _elecDist = new int[3] { 1, 0, 0 };
		private int[] _ironDist = new int[3] { 1, 0, 0 };
		private int[] _titanDist = new int[3] { 1, 0, 0 };
		private int[] _h3Dist = new int[5] { 1, 0, 0, 0, 0 };
		private int _industrialCoreLevel = 1;

		// Edit Mode States
		public bool IsEditLayoutMode { get; private set; } = false;
		public string SelectedBuildingId { get; private set; } = null;
		public Vector2I StagedGridPos { get; private set; } = Vector2I.Zero;
		private Vector2I _preMoveGridPos = Vector2I.Zero;
		private bool _isDraggingSelectedBuilding = false;
		private Vector2 _buildingDragGrabOffset = Vector2.Zero;

		public class BuildingNodeData
		{
			public string Id;
			public string Name;
			public int Col;
			public int Row;
			public int W;
			public int H;
			public Color ThemeColor;
			public string Icon;
			public int Level;
			public string DistrictCode;
			public int SlotIndex;
			public Node2D AnchorNode;
			public Polygon2D DiamondPoly;
			public Line2D DiamondOutline;
			public Node2D SubGridContainer;
			public Sprite2D BuildingSprite;
			public Control BadgeContainer;
			public Label BadgeLabel;
			public StyleBoxFlat BadgeStyle;
		}

		private Dictionary<string, BuildingNodeData> _buildingRegistry = new Dictionary<string, BuildingNodeData>();

		public override void _Ready()
		{
			Vector2 baseCenter = GridToIso(GRID_COLS / 2, GRID_ROWS / 2);
			_terrainCenterPos = baseCenter;

			// 1. Initialize Camera
			_camera = GetNodeOrNull<Camera2D>("Camera2D") ?? GetNodeOrNull<Camera2D>("MainCamera");
			if (_camera == null)
			{
				_camera = new Camera2D { Name = "Camera2D" };
				AddChild(_camera);
			}

			_camera.Position = baseCenter;
			_targetCameraPos = baseCenter;
			_camera.Zoom = new Vector2(_targetZoom, _targetZoom);
			_camera.MakeCurrent();

			// 2. Build Terrain Backdrop
			BuildTerrainBackdrop(baseCenter);

			// 3. Build Isometric Grid Lines (Default Hidden in Cinematic Mode)
			_gridLineCanvas = new Node2D { Name = "GridLineCanvas", ZIndex = 1, Visible = false };
			AddChild(_gridLineCanvas);
			DrawIsometricGridLines();

			// 4. Build 25 Buildings Container (Y-Sorted)
			_buildingContainer = new Node2D { Name = "BuildingContainer", ZIndex = 2 };
			AddChild(_buildingContainer);

			// 5. Dedicated Badge Container (ZIndex = 4000: Guaranteed to render on top of all building art)
			_badgeContainer = new Node2D { Name = "BadgeContainer", ZIndex = 4000 };
			AddChild(_badgeContainer);

			SpawnAudited25BuildingLayout();

			// 6. Build Teleport Reticle Container
			_teleportReticleContainer = new Node2D { Name = "TeleportReticleContainer", ZIndex = 5000 };
			AddChild(_teleportReticleContainer);

			// 7. Build Layer 100: Top Cockpit HUD + Bottom Nav Dock
			_hudLayer = new CanvasLayer { Name = "HUDLayer", Layer = 100 };
			AddChild(_hudLayer);

			_hudInstance = new BaseHUD();
			_hudInstance.EditModeToggled += OnEditModeToggled;
			_hudInstance.SaveLayoutRequested += OnSaveLayoutRequested;
			_hudInstance.ColonyDataLoaded += OnColonyDataLoaded; 
			_hudInstance.QueuedBuildingCompleted += OnBuildingUpgraded;
			_hudInstance.CameraZoomRequested += OnCameraZoomRequested;
			_hudInstance.CameraResetRequested += OnCameraResetRequested;
			_hudLayer.AddChild(_hudInstance);

			// 8. Build Layer 110: Centered Inspector Modal
			_modalLayer = new CanvasLayer { Name = "ModalLayer", Layer = 110 };
			AddChild(_modalLayer);

			_inspectorModal = new BuildingInspectorModal();
			_inspectorModal.BuildingUpgraded += OnBuildingUpgraded;
			_modalLayer.AddChild(_inspectorModal);

			// If we already have the player's verified session cache (from ServerSelect or switching between Base / Moon / Galaxy),
			// apply it immediately on Frame 0 so there is ZERO 1-second flash or building jump!
			if (SupabaseService.HasValidSessionColonyCache && SupabaseService.CachedBuildingLevels.Count > 0)
			{
				OnColonyDataLoaded();
			}
			else
			{
				// First cold load before cloud responds: hide the template layout behind a sleek dark tactical veil
				// so the player NEVER sees Level 1 template buildings jump across the screen!
				_buildingContainer.Visible = false;
				_badgeContainer.Visible = false;
				BuildCloudSyncVeil();
			}

			_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
			_camera.Position = _targetCameraPos;

			// Engage seamless 'base.wav' loop whenever entering or returning to the Colony Base
			MusicManager.PlayTrack(this, MusicManager.TRACK_BASE);

			GD.Print("[BASE VIEW] Master Viewport Engaged: Standard Uniform Diamond Seating active.");
		}

		private void BuildCloudSyncVeil()
		{
			_cloudSyncVeil = new ColorRect
			{
				Color = new Color(0.01f, 0.02f, 0.05f, 0.94f),
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			_cloudSyncVeil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			_modalLayer.AddChild(_cloudSyncVeil);

			_cloudSyncLabel = new Label
			{
				Text = "🛰️  SYNCHRONIZING COLONY TELEMETRY WITH ORBITAL SATELLITE...",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Modulate = new Color("#00F0FF")
			};
			_cloudSyncLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			_cloudSyncLabel.AddThemeFontSizeOverride("font_size", 18);
			_cloudSyncVeil.AddChild(_cloudSyncLabel);
		}

		private void OnColonyDataLoaded()
		{
			if (_buildingContainer != null) _buildingContainer.Visible = true;
			if (_badgeContainer != null) _badgeContainer.Visible = true;
			if (_cloudSyncVeil != null)
			{
				_cloudSyncVeil.QueueFree();
				_cloudSyncVeil = null;
			}

			var cloudLevels = (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null && BaseHUD.Instance.BuildingLevels.Count > 0)
				? BaseHUD.Instance.BuildingLevels
				: SupabaseService.CachedBuildingLevels;
			if (cloudLevels == null || cloudLevels.Count == 0) return;

			_elecDist[0] = cloudLevels.GetValueOrDefault("dist_e_0", 1);
			_elecDist[1] = cloudLevels.GetValueOrDefault("dist_e_1", 0);
			_elecDist[2] = cloudLevels.GetValueOrDefault("dist_e_2", 0);

			_ironDist[0] = cloudLevels.GetValueOrDefault("dist_i_0", 1);
			_ironDist[1] = cloudLevels.GetValueOrDefault("dist_i_1", 0);
			_ironDist[2] = cloudLevels.GetValueOrDefault("dist_i_2", 0);

			_titanDist[0] = cloudLevels.GetValueOrDefault("dist_t_0", 1);
			_titanDist[1] = cloudLevels.GetValueOrDefault("dist_t_1", 0);
			_titanDist[2] = cloudLevels.GetValueOrDefault("dist_t_2", 0);

			_h3Dist[0] = cloudLevels.GetValueOrDefault("dist_h3_0", 1);
			_h3Dist[1] = cloudLevels.GetValueOrDefault("dist_h3_1", 0);
			_h3Dist[2] = cloudLevels.GetValueOrDefault("dist_h3_2", 0);
			_h3Dist[3] = cloudLevels.GetValueOrDefault("dist_h3_3", 0);
			_h3Dist[4] = cloudLevels.GetValueOrDefault("dist_h3_4", 0);

			_industrialCoreLevel = cloudLevels.GetValueOrDefault("hub_cmd", 1);

			bool canUseSavedCustomPositions = CanRestoreSavedLayoutWithoutOverlaps(cloudLevels);

			foreach (var kvp in _buildingRegistry)
			{
				string bId = kvp.Key;
				BuildingNodeData data = kvp.Value;
				
				int cloudLvl = cloudLevels.GetValueOrDefault(bId, 0);
				
				if (cloudLvl == 0 && (bId == "hub_cmd" || bId == "hub_silo" || bId == "hub_mgd" || bId == "dist_e_0" || bId == "dist_i_0" || bId == "dist_t_0" || bId == "dist_h3_0"))
				{
					cloudLvl = 1;
				}

				data.Level = cloudLvl;
				if (data.BadgeLabel != null)
				{
					data.BadgeLabel.Text = GameMath.IsBuildingUpgradable(bId)
						? $"{data.Icon} {data.Name.ToUpper()}  ·  LVL {cloudLvl}"
						: $"{data.Icon} {data.Name.ToUpper()}";
				}

				// Restore custom base layout coordinates saved by the player in Edit Mode if valid for the 8x8 / 6x6 / 4x4 grid!
				if (canUseSavedCustomPositions &&
					cloudLevels.TryGetValue($"__pos_col_{bId}", out int savedCol) &&
					cloudLevels.TryGetValue($"__pos_row_{bId}", out int savedRow))
				{
					int safeCol = Math.Clamp(savedCol, 0, GRID_COLS - data.W);
					int safeRow = Math.Clamp(savedRow, 0, GRID_ROWS - data.H);
					if (safeCol != data.Col || safeRow != data.Row)
					{
						data.Col = safeCol;
						data.Row = safeRow;
						ApplySavedBuildingPositionToViewport(data);
					}
				}
			}

			// If legacy 6x6/4x4/2x2 coordinates were present in cloudLevels and caused overlaps under the new 8x8/6x6/4x4 footprints,
			// automatically stamp the clean spread-out v3 coordinates into BuildingLevels so future saves stay overlap-free!
			if (!canUseSavedCustomPositions && BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null)
			{
				BaseHUD.Instance.BuildingLevels["__layout_schema_ver"] = LAYOUT_SCHEMA_VERSION;
				foreach (var kvp in _buildingRegistry)
				{
					BaseHUD.Instance.BuildingLevels[$"__pos_col_{kvp.Key}"] = kvp.Value.Col;
					BaseHUD.Instance.BuildingLevels[$"__pos_row_{kvp.Key}"] = kvp.Value.Row;
				}
			}

			GD.Print("[BASE VIEW] 2.5D Viewport & Expanded 8x8/6x6/4x4 Layout Synchronized with Supabase Cloud Data.");
		}

		private bool CanRestoreSavedLayoutWithoutOverlaps(Dictionary<string, int> cloudLevels)
		{
			if (cloudLevels == null || cloudLevels.Count == 0 || _buildingRegistry.Count == 0) return false;

			int schemaVer = cloudLevels.GetValueOrDefault("__layout_schema_ver", 0);
			if (schemaVer < LAYOUT_SCHEMA_VERSION)
			{
				return false;
			}

			var tempRects = new List<(string id, int col, int row, int w, int h)>();
			bool anyCustomPosFound = false;

			foreach (var kvp in _buildingRegistry)
			{
				string bId = kvp.Key;
				var data = kvp.Value;
				int c = data.Col;
				int r = data.Row;

				if (cloudLevels.TryGetValue($"__pos_col_{bId}", out int savedCol) &&
					cloudLevels.TryGetValue($"__pos_row_{bId}", out int savedRow))
				{
					c = Math.Clamp(savedCol, 0, GRID_COLS - data.W);
					r = Math.Clamp(savedRow, 0, GRID_ROWS - data.H);
					anyCustomPosFound = true;
				}

				if (!GameMath.IsFootprintInBounds(c, r, data.W, data.H))
				{
					return false;
				}

				for (int i = 0; i < tempRects.Count; i++)
				{
					var other = tempRects[i];
					if (GameMath.DoFootprintsOverlap(c, r, data.W, data.H, other.col, other.row, other.w, other.h))
					{
						return false;
					}
				}

				tempRects.Add((bId, c, r, data.W, data.H));
			}

			return anyCustomPosFound;
		}

		private void ApplySavedBuildingPositionToViewport(BuildingNodeData data)
		{
			Vector2 top = GridToIso(data.Col, data.Row);
			Vector2 bottom = GridToIso(data.Col + data.W, data.Row + data.H);
			Vector2 center = (top + bottom) * 0.5f;
			float footprintH = data.H * TILE_HEIGHT_HALF * 2.0f;

			if (data.AnchorNode != null)
			{
				data.AnchorNode.Position = center;
				data.AnchorNode.ZIndex = (int)(center.Y + footprintH * 0.5f);
			}

			float badgeY = (data.W >= 4) ? (footprintH * 0.5f + 6.0f) : (footprintH * 0.5f - 6.0f);
			if (data.BadgeContainer != null)
			{
				float actualW = Mathf.Max(data.BadgeContainer.Size.X, data.BadgeContainer.GetCombinedMinimumSize().X);
				data.BadgeContainer.Position = new Vector2(center.X - (actualW * 0.5f), center.Y + badgeY);
			}
		}

		public override void _Process(double delta)
		{
			float dt = (float)delta;

			Vector2 panDir = Vector2.Zero;
			if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) panDir.Y -= 1.0f;
			if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) panDir.Y += 1.0f;
			if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) panDir.X -= 1.0f;
			if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) panDir.X += 1.0f;

			if (panDir != Vector2.Zero)
			{
				float speed = 850.0f / _targetZoom;
				_targetCameraPos += panDir.Normalized() * speed * dt;
			}

			_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);

			if (_camera != null)
			{
				float lerpWeight = 1.0f - Mathf.Exp(-14.0f * dt);
				_camera.Position = _camera.Position.Lerp(_targetCameraPos, lerpWeight);
				_camera.Zoom = _camera.Zoom.Lerp(new Vector2(_targetZoom, _targetZoom), lerpWeight);
			}

			_badgeAnimTime += dt;
			UpdateBuildingBadgesAnimation();
		}

		private void UpdateBuildingBadgesAnimation()
		{
			string activeBuildId = (_hudInstance != null && _hudInstance.ActiveBuild != null)
				? _hudInstance.ActiveBuild.BuildingId
				: null;

			// Calm sinusoidal breathing wave (0.0 to 1.0 over ~2.6 seconds)
			float pulse = (Mathf.Sin(_badgeAnimTime * 2.4f) + 1.0f) * 0.5f;

			foreach (var kvp in _buildingRegistry)
			{
				var data = kvp.Value;
				if (data.BadgeContainer == null || data.BadgeLabel == null || data.BadgeStyle == null) continue;

				bool isActivelyUpgrading = (activeBuildId != null && data.Id == activeBuildId);
				bool isQueued = false;

				if (!isActivelyUpgrading && _hudInstance != null && _hudInstance.BuildQueue != null)
				{
					for (int i = 0; i < _hudInstance.BuildQueue.Count; i++)
					{
						if (_hudInstance.BuildQueue[i].BuildingId == data.Id)
						{
							isQueued = true;
							break;
						}
					}
				}

				if (isActivelyUpgrading)
				{
					int targetLvl = _hudInstance.ActiveBuild.TargetLevel;
					string upgText = $"⚙ {data.Name.ToUpper()}  ·  LVL {data.Level} ➔ {targetLvl}";
					if (data.BadgeLabel.Text != upgText) data.BadgeLabel.Text = upgText;

					// Calmly glow and fade between warm engineering amber and bright solar gold/white
					Color amberCore = new Color("#F59E0B");
					Color goldGlow = new Color("#FEF08A");
					Color currentBorder = amberCore.Lerp(goldGlow, pulse);

					data.BadgeStyle.BorderColor = currentBorder;
					data.BadgeStyle.BgColor = new Color(0.05f, 0.04f, 0.01f, 0.96f).Lerp(new Color(0.24f, 0.14f, 0.03f, 0.98f), pulse);
					data.BadgeStyle.ShadowColor = new Color(1.0f, 0.68f, 0.10f, Mathf.Lerp(0.25f, 0.85f, pulse));
					data.BadgeStyle.ShadowSize = (int)Mathf.Lerp(6f, 15f, pulse);

					data.BadgeLabel.Modulate = new Color(1.0f, 0.85f, 0.45f, 0.82f).Lerp(Colors.White, pulse);
				}
				else if (isQueued)
				{
					string qText = $"⏳ {data.Name.ToUpper()}  ·  LVL {data.Level} (QUEUED)";
					if (data.BadgeLabel.Text != qText) data.BadgeLabel.Text = qText;

					float slowPulse = (Mathf.Sin(_badgeAnimTime * 1.5f) + 1.0f) * 0.5f;
					Color cyanBorder = new Color("#0284C7").Lerp(new Color("#38BDF8"), slowPulse);
					data.BadgeStyle.BorderColor = cyanBorder;
					data.BadgeStyle.BgColor = new Color(0.02f, 0.06f, 0.12f, 0.96f);
					data.BadgeStyle.ShadowColor = new Color(0f, 0.75f, 1.0f, Mathf.Lerp(0.2f, 0.5f, slowPulse));
					data.BadgeStyle.ShadowSize = 8;

					data.BadgeLabel.Modulate = new Color("#7DD3FC").Lerp(Colors.White, slowPulse * 0.6f);
				}
				else
				{
					bool isUpgradable = GameMath.IsBuildingUpgradable(data.Id);
					int hpPct = 100;
					bool isDamaged = false;
					string lvlBadgeStr = $"{data.Level}";
					if (isUpgradable && _hudInstance != null && _hudInstance.BuildingLevels != null && data.Level > 0)
					{
						var hpDetails = GameMath.GetStructureHealthDetails(data.Id, data.Level, _hudInstance.BuildingLevels);
						hpPct = hpDetails.healthPercent;
						isDamaged = hpDetails.isDamaged;
						lvlBadgeStr = GameMath.FormatEffectiveLevel(hpDetails.effectiveLevel);
					}

					string stdText = !isUpgradable
						? $"{data.Icon} {data.Name.ToUpper()}"
						: ((isDamaged && data.Level > 0)
							? $"🔧 {data.Name.ToUpper()}  ·  LVL {lvlBadgeStr} ({hpPct}% HP · REPAIR REQ)"
							: $"{data.Icon} {data.Name.ToUpper()}  ·  LVL {data.Level}");
					if (data.BadgeLabel.Text != stdText) data.BadgeLabel.Text = stdText;

					data.BadgeStyle.BorderColor = (isDamaged && data.Level > 0)
						? new Color("#EF4444")
						: new Color(data.ThemeColor.R, data.ThemeColor.G, data.ThemeColor.B, 0.95f);
					data.BadgeStyle.BgColor = new Color(0.02f, 0.04f, 0.08f, 0.96f);
					data.BadgeStyle.ShadowColor = new Color(0f, 0f, 0f, 0.8f);
					data.BadgeStyle.ShadowSize = 6;

					data.BadgeLabel.Modulate = (isDamaged && data.Level > 0)
						? new Color("#FCA5A5")
						: data.ThemeColor.Lerp(Colors.White, 0.35f);
				}

				// Always keep the badge horizontally centered on the building's live visual position (including during Edit Mode drag & staging)
				Vector2 center;
				if (data.AnchorNode != null)
				{
					center = data.AnchorNode.Position;
				}
				else
				{
					int activeCol = (IsEditLayoutMode && SelectedBuildingId == data.Id) ? StagedGridPos.X : data.Col;
					int activeRow = (IsEditLayoutMode && SelectedBuildingId == data.Id) ? StagedGridPos.Y : data.Row;
					Vector2 top = GridToIso(activeCol, activeRow);
					Vector2 bottom = GridToIso(activeCol + data.W, activeRow + data.H);
					center = (top + bottom) * 0.5f;
				}
				float footprintH = data.H * TILE_HEIGHT_HALF * 2.0f;
				float badgeY = (data.W >= 4) ? (footprintH * 0.5f + 6.0f) : (footprintH * 0.5f - 6.0f);
				float actualW = Mathf.Max(data.BadgeContainer.Size.X, data.BadgeContainer.GetCombinedMinimumSize().X);
				data.BadgeContainer.Position = new Vector2(center.X - (actualW * 0.5f), center.Y + badgeY);
			}
		}

		private void OnCameraZoomRequested(float zoomMultiplier)
		{
			_targetZoom = Mathf.Clamp(_targetZoom * zoomMultiplier, MIN_ZOOM, MAX_ZOOM);
			_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
		}

		private void OnCameraResetRequested()
		{
			_targetZoom = 0.78f;
			_targetCameraPos = ClampCameraPosition(_terrainCenterPos, _targetZoom);
		}

		public override void _UnhandledInput(InputEvent @event)
		{
			if ((_inspectorModal != null && _inspectorModal.Visible) || (_hudInstance != null && _hudInstance.IsAnyOverlayModalOpen()))
			{
				_isDraggingCamera = false;
				_isDraggingSelectedBuilding = false;
				_activeTouches.Clear();
				_wasMultiTouchGesture = false;
				return;
			}

			// 0. Laptop Trackpad Two-Finger Pan & Pinch-Magnify Gestures
			if (@event is InputEventMagnifyGesture mag)
			{
				float factor = Mathf.Clamp(mag.Factor, 0.80f, 1.25f);
				_targetZoom = Mathf.Clamp(_targetZoom * factor, MIN_ZOOM, MAX_ZOOM);
				_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
				if (_isDraggingCamera)
				{
					_dragStartMousePos = GetViewport().GetMousePosition();
					_dragStartCameraPos = _targetCameraPos;
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventPanGesture panGesture)
			{
				float effZoom = Math.Max(0.25f, _targetZoom);
				_targetCameraPos = ClampCameraPosition(_targetCameraPos + panGesture.Delta * (28.0f / effZoom), _targetZoom);
				if (_isDraggingCamera)
				{
					_dragStartMousePos = GetViewport().GetMousePosition();
					_dragStartCameraPos = _targetCameraPos;
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// 1. Multi-Touch Tracking for Mobile Pinch-to-Zoom & Smooth 2->1 Finger Re-Anchoring
			if (@event is InputEventScreenTouch st)
			{
				if (st.Pressed)
				{
					_activeTouches[st.Index] = st.Position;
					if (_activeTouches.Count >= 2)
					{
						_wasMultiTouchGesture = true;
						_isDraggingCamera = false;
					}
					else if (_activeTouches.Count == 1)
					{
						_wasMultiTouchGesture = false;
					}
				}
				else
				{
					_activeTouches.Remove(st.Index);
					if (_activeTouches.Count == 1)
					{
						foreach (var kvp in _activeTouches)
						{
							_dragStartMousePos = kvp.Value;
							_dragStartCameraPos = _targetCameraPos;
							break;
						}
					}
				}
			}

			if (@event is InputEventScreenDrag sd && _activeTouches.Count >= 2)
			{
				if (_activeTouches.ContainsKey(sd.Index))
				{
					var keys = new List<int>(_activeTouches.Keys);
					int idx0 = keys[0];
					int idx1 = keys[1];

					Vector2 p0Before = _activeTouches[idx0];
					Vector2 p1Before = _activeTouches[idx1];
					float prevDist = p0Before.DistanceTo(p1Before);
					Vector2 prevMid = (p0Before + p1Before) * 0.5f;

					_activeTouches[sd.Index] = sd.Position;

					Vector2 p0After = _activeTouches[idx0];
					Vector2 p1After = _activeTouches[idx1];
					float newDist = p0After.DistanceTo(p1After);
					Vector2 newMid = (p0After + p1After) * 0.5f;

					if (prevDist > 12.0f && newDist > 12.0f)
					{
						float pinchRatio = Mathf.Clamp(newDist / prevDist, 0.80f, 1.25f);
						_targetZoom = Mathf.Clamp(_targetZoom * pinchRatio, MIN_ZOOM, MAX_ZOOM);
					}

					float effZoom = Math.Max(0.25f, _targetZoom);
					_targetCameraPos = ClampCameraPosition(_targetCameraPos - (newMid - prevMid) / effZoom, _targetZoom);
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// 2. PC Mouse & Single-Finger Emulated Touch Pan / Tap / Building Drag
			if (@event is InputEventMouseButton mb)
			{
				if (mb.IsPressed())
				{
					if (mb.ButtonIndex == MouseButton.WheelUp)
					{
						float step = mb.Factor > 0.01f ? Mathf.Lerp(1.0f, 1.12f, Mathf.Clamp(mb.Factor, 0.15f, 1.5f)) : 1.12f;
						_targetZoom = Mathf.Clamp(_targetZoom * step, MIN_ZOOM, MAX_ZOOM);
						_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
						if (_isDraggingCamera)
						{
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
						}
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.WheelDown)
					{
						float step = mb.Factor > 0.01f ? Mathf.Lerp(1.0f, 0.88f, Mathf.Clamp(mb.Factor, 0.15f, 1.5f)) : 0.88f;
						_targetZoom = Mathf.Clamp(_targetZoom * step, MIN_ZOOM, MAX_ZOOM);
						_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
						if (_isDraggingCamera)
						{
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
						}
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.Left)
					{
						if (mb.Position.Y > 68 && mb.Position.Y < GetViewportRect().Size.Y - 72 && _activeTouches.Count <= 1)
						{
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
							_dragAccumulatedDistance = 0.0f;

							// In Edit Mode: Click/Touch-Hold on ANY building immediately selects & grabs it for drag-and-drop!
							if (IsEditLayoutMode)
							{
								Vector2 worldPressPos = GetGlobalMousePosition();
								BuildingNodeData hitBuilding = FindBuildingAtWorldPos(worldPressPos);
								if (hitBuilding != null)
								{
									if (SelectedBuildingId != hitBuilding.Id)
									{
										// If another building was previously staged, auto-confirm or revert it before grabbing the new one
										if (!string.IsNullOrEmpty(SelectedBuildingId) && _buildingRegistry.TryGetValue(SelectedBuildingId, out var prevData))
										{
											if (ValidateFootprintLegality(SelectedBuildingId, StagedGridPos.X, StagedGridPos.Y, prevData.W, prevData.H))
											{
												prevData.Col = StagedGridPos.X;
												prevData.Row = StagedGridPos.Y;
												ApplySavedBuildingPositionToViewport(prevData);
											}
											else
											{
												prevData.Col = _preMoveGridPos.X;
												prevData.Row = _preMoveGridPos.Y;
												ApplySavedBuildingPositionToViewport(prevData);
											}
										}
										SelectBuildingForRelocation(hitBuilding.Id);
									}

									_isDraggingSelectedBuilding = true;
									_isDraggingCamera = false;
									_buildingDragGrabOffset = worldPressPos - hitBuilding.AnchorNode.Position;
									GetViewport().SetInputAsHandled();
									return;
								}
							}

							_isDraggingSelectedBuilding = false;
							_isDraggingCamera = true;
						}
					}
					else if (mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						if (mb.Position.Y > 68 && mb.Position.Y < GetViewportRect().Size.Y - 72 && _activeTouches.Count <= 1)
						{
							_isDraggingSelectedBuilding = false;
							_isDraggingCamera = true;
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
							_dragAccumulatedDistance = 0.0f;
						}
					}
				}
				else
				{
					if (mb.ButtonIndex == MouseButton.Left)
					{
						if (_isDraggingSelectedBuilding)
						{
							_isDraggingSelectedBuilding = false;
							GetViewport().SetInputAsHandled();
							return;
						}

						_isDraggingCamera = false;

						// Mobile + PC stationary tap detection (18px threshold)
						if (!_wasMultiTouchGesture && _dragAccumulatedDistance <= 18.0f)
						{
							if (mb.Position.Y > 68 && mb.Position.Y < GetViewportRect().Size.Y - 72)
							{
								Vector2 worldClickPos = GetGlobalMousePosition();
								HandleCentralizedBuildingClick(worldClickPos);
							}
						}
						_dragAccumulatedDistance = 0.0f;
						if (_activeTouches.Count == 0)
						{
							_wasMultiTouchGesture = false;
						}
					}
					else if (mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						_isDraggingCamera = false;
						_dragAccumulatedDistance = 0.0f;
					}
				}
			}

			if (@event is InputEventMouseMotion mm && _activeTouches.Count <= 1 && !_wasMultiTouchGesture)
			{
				// Dragging a selected building in Edit Mode (PC Click-Hold-Drag & Mobile Touch-Hold-Move)
				if (_isDraggingSelectedBuilding && IsEditLayoutMode && !string.IsNullOrEmpty(SelectedBuildingId))
				{
					if (_buildingRegistry.TryGetValue(SelectedBuildingId, out BuildingNodeData draggedData))
					{
						Vector2 worldTargetCenter = GetGlobalMousePosition() - _buildingDragGrabOffset;
						Vector2I newGridPos = IsoCenterToGrid(worldTargetCenter, draggedData.W, draggedData.H);
						if (newGridPos != StagedGridPos)
						{
							StagedGridPos = newGridPos;
							UpdateBuildingPositionAndVisuals(draggedData, StagedGridPos);
							RenderTeleportReticle(draggedData);
						}
					}
					GetViewport().SetInputAsHandled();
					return;
				}

				if (_isDraggingCamera)
				{
					_dragAccumulatedDistance += mm.Relative.Length();
					if (_dragAccumulatedDistance <= 2.5f)
					{
						_dragStartMousePos = mm.Position;
						return;
					}
					float effZoom = Math.Max(0.25f, _targetZoom);
					Vector2 stepDelta = (mm.Position - _dragStartMousePos) / effZoom;
					_dragStartMousePos = mm.Position;
					_targetCameraPos = ClampCameraPosition(_targetCameraPos - stepDelta, _targetZoom);
					_dragStartCameraPos = _targetCameraPos;
					GetViewport().SetInputAsHandled();
					return;
				}
			}
		}

		private BuildingNodeData FindBuildingAtWorldPos(Vector2 worldClickPos)
		{
			var candidates = new List<BuildingNodeData>(_buildingRegistry.Values);
			candidates.Sort((a, b) => b.AnchorNode.ZIndex.CompareTo(a.AnchorNode.ZIndex));

			foreach (var bData in candidates)
			{
				Vector2 localPos = worldClickPos - bData.AnchorNode.Position;

				float halfW = bData.W * TILE_WIDTH_HALF;
				float halfH = bData.H * TILE_HEIGHT_HALF;

				float diamondNorm = (Math.Abs(localPos.X) / halfW) + (Math.Abs(localPos.Y) / halfH);

				bool insideDiamond = diamondNorm <= 1.05f;
				bool insideCoreStructure = (bData.BuildingSprite != null) && 
										   (Math.Abs(localPos.X) <= halfW * 0.75f) && 
										   (localPos.Y >= -halfH * 1.5f && localPos.Y <= halfH * 0.5f);

				if (insideDiamond || insideCoreStructure)
				{
					return bData;
				}
			}
			return null;
		}

		private void HandleCentralizedBuildingClick(Vector2 worldClickPos)
		{
			BuildingNodeData hit = FindBuildingAtWorldPos(worldClickPos);
			if (hit != null)
			{
				OnBuildingClicked(hit.Id);
			}
		}

		public static Vector2I IsoCenterToGrid(Vector2 worldCenter, int w, int h)
		{
			float sum = worldCenter.Y / TILE_HEIGHT_HALF;
			float diff = worldCenter.X / TILE_WIDTH_HALF;

			float centerCol = (sum + diff) * 0.5f;
			float centerRow = (sum - diff) * 0.5f;

			int col = (int)Math.Round(centerCol - (w * 0.5f));
			int row = (int)Math.Round(centerRow - (h * 0.5f));

			col = Math.Clamp(col, 0, GRID_COLS - w);
			row = Math.Clamp(row, 0, GRID_ROWS - h);
			return new Vector2I(col, row);
		}

		public Vector2 ClampCameraPosition(Vector2 rawPos, float currentZoom)
		{
			Vector2 vpSize = GetViewportRect().Size;
			float visibleHalfWidth = (vpSize.X / currentZoom) * 0.5f;
			float visibleHalfHeight = (vpSize.Y / currentZoom) * 0.5f;

			float terrainHalfW = TERRAIN_WIDTH * 0.5f;
			float terrainHalfH = TERRAIN_HEIGHT * 0.5f;

			float minX = (_terrainCenterPos.X - terrainHalfW) + visibleHalfWidth;
			float maxX = (_terrainCenterPos.X + terrainHalfW) - visibleHalfWidth;
			float minY = (_terrainCenterPos.Y - terrainHalfH) + visibleHalfHeight;
			float maxY = (_terrainCenterPos.Y + terrainHalfH) - visibleHalfHeight;

			float clampedX = (minX > maxX) ? _terrainCenterPos.X : Mathf.Clamp(rawPos.X, minX, maxX);
			float clampedY = (minY > maxY) ? _terrainCenterPos.Y : Mathf.Clamp(rawPos.Y, minY, maxY);

			return new Vector2(clampedX, clampedY);
		}

		public static Vector2 GridToIso(int col, int row)
		{
			float isoX = (col - row) * TILE_WIDTH_HALF;
			float isoY = (col + row) * TILE_HEIGHT_HALF;
			return new Vector2(isoX, isoY);
		}

		private void BuildTerrainBackdrop(Vector2 baseCenter)
		{
			_terrainSprite = new Sprite2D { Name = "TerrainBackdropSprite", ZIndex = 0 };

			Texture2D texToUse = TerrainTexture;
			if (texToUse == null) texToUse = ResourceLoader.Load<Texture2D>("res://Assets/Terrain/base-terrain.png");
			if (texToUse == null) texToUse = GD.Load<Texture2D>("res://Assets/Terrain/base-terrain.png");

			if (texToUse != null)
			{
				_terrainSprite.Texture = texToUse;
				_terrainSprite.Position = baseCenter;
				float scaleX = texToUse.GetWidth() > 0 ? TERRAIN_WIDTH / texToUse.GetWidth() : 1.0f;
				float scaleY = texToUse.GetHeight() > 0 ? TERRAIN_HEIGHT / texToUse.GetHeight() : 1.0f;
				float uniformScale = Math.Max(1.0f, Math.Max(scaleX, scaleY));
				_terrainSprite.Scale = new Vector2(uniformScale, uniformScale);
				AddChild(_terrainSprite);
			}
		}

		private void DrawIsometricGridLines()
		{
			Color gridColor = new Color(0f, 0.94f, 1f, 0.30f);

			for (int r = 0; r <= GRID_ROWS; r++)
			{
				Vector2 start = GridToIso(0, r);
				Vector2 end = GridToIso(GRID_COLS, r);
				Line2D line = new Line2D { Width = 1.2f, DefaultColor = gridColor, Antialiased = true };
				line.AddPoint(start);
				line.AddPoint(end);
				_gridLineCanvas.AddChild(line);
			}

			for (int c = 0; c <= GRID_COLS; c++)
			{
				Vector2 start = GridToIso(c, 0);
				Vector2 end = GridToIso(c, GRID_ROWS);
				Line2D line = new Line2D { Width = 1.2f, DefaultColor = gridColor, Antialiased = true };
				line.AddPoint(start);
				line.AddPoint(end);
				_gridLineCanvas.AddChild(line);
			}
		}

		private static Texture2D LoadBuildingTextureOrFallback(Texture2D exportedTex, params string[] candidatePaths)
		{
			if (exportedTex != null) return exportedTex;
			foreach (string p in candidatePaths)
			{
				if (ResourceLoader.Exists(p))
				{
					var loaded = ResourceLoader.Load<Texture2D>(p);
					if (loaded != null) return loaded;
				}
			}
			return null;
		}

		private void SpawnAudited25BuildingLayout()
		{
			Texture2D coreTexture = LoadBuildingTextureOrFallback(IndustrialCoreTexture, "res://Assets/Buildings/industrial_core.png");
			Texture2D siloTexture = LoadBuildingTextureOrFallback(StorageSiloTexture, "res://Assets/Buildings/storage_silos.png", "res://Assets/Buildings/storage_silo.png");
			Texture2D powerTexture = LoadBuildingTextureOrFallback(PowerStationTexture, "res://Assets/Buildings/power_station.png");
			Texture2D ironTexture = LoadBuildingTextureOrFallback(IronMineTexture, "res://Assets/Buildings/iron_mine.png");
			Texture2D titaniumTexture = LoadBuildingTextureOrFallback(TitaniumSmelterTexture, "res://Assets/Buildings/titanium_smelter.png");
			Texture2D h3Texture = LoadBuildingTextureOrFallback(H3DistilleryTexture, "res://Assets/Buildings/h3_distillery.png");
			Texture2D shieldTexture = LoadBuildingTextureOrFallback(PlanetaryShieldTexture, "res://Assets/Buildings/planetary_shield.png", "res://Assets/Buildings/shield.png");
			Texture2D shipyardTexture = LoadBuildingTextureOrFallback(OrbitalShipyardTexture, "res://Assets/Buildings/orbital_shipyard.png", "res://Assets/Buildings/shipyard.png");
			Texture2D goldExchangeTexture = LoadBuildingTextureOrFallback(GalaxyGoldExchangeTexture, "res://Assets/Buildings/galaxy_gold_exchange.png", "res://Assets/Buildings/gold_exchange.png");
			Texture2D armoryTexture = LoadBuildingTextureOrFallback(GarrisonArmoryTexture, "res://Assets/Buildings/garrison_armory.png", "res://Assets/Buildings/armory.png");
			Texture2D allianceHqTexture = LoadBuildingTextureOrFallback(AllianceHQTexture, "res://Assets/Buildings/alliance_hq.png");
			Texture2D commandersSpireTexture = LoadBuildingTextureOrFallback(CommandersSpireTexture, "res://Assets/Buildings/commanders_spire.png");
			Texture2D fleetStationTexture = LoadBuildingTextureOrFallback(FleetStationTexture, "res://Assets/Buildings/fleet_station.png");
			Texture2D deepRadarTexture = LoadBuildingTextureOrFallback(DeepRadarTexture, "res://Assets/Buildings/deep_radar.png", "res://Assets/Buildings/the_deep_radar.png");
			Texture2D tradeLogisticsTexture = LoadBuildingTextureOrFallback(TradeLogisticsTexture, "res://Assets/Buildings/trade_logistics.png");
			Texture2D researchTexture = LoadBuildingTextureOrFallback(ResearchDirectorateTexture, "res://Assets/Buildings/research_directorate.png");

			// Master 26-Building Expanded Base Layout on 64x64 Lunar Grid:
			// - Industrial Core ("hub_cmd"): 8x8 Grid Block (640x360px isometric diamond)
			// - Storage Silos ("hub_silo"): 6x6 Grid Block (480x270px isometric diamond)
			// - All 24 Other Facilities & Sub-Mines: 4x4 Grid Blocks (320x180px isometric diamonds)
			// - Spread out across the 64x64 lunar grid with wide 4-to-6 tile avenues so NO buildings or badges ever overlap!
			var defs = new[]
			{
				// Central Command & Vault Axis (Centered around Col 28, Row 24)
				new { Id = "hub_cmd", Name = "Industrial Core", Col = 28, Row = 22, W = 8, H = 8, ColorHex = "#ffffff", Icon = "🏢", District = "", Slot = 0 },
				new { Id = "hub_silo", Name = "Storage Silos", Col = 29, Row = 35, W = 6, H = 6, ColorHex = "#00f0ff", Icon = "🛢️", District = "", Slot = 0 },
				new { Id = "hub_shd", Name = "Planetary Shield", Col = 30, Row = 14, W = 4, H = 4, ColorHex = "#38bdf8", Icon = "🛡️", District = "", Slot = 0 },
				new { Id = "hub_mil", Name = "Orbital Shipyard", Col = 30, Row = 6, W = 4, H = 4, ColorHex = "#ef4444", Icon = "⚔️", District = "", Slot = 0 },
				new { Id = "hub_mgd", Name = "Galaxy Gold Exchange", Col = 30, Row = 45, W = 4, H = 4, ColorHex = "#fbbf24", Icon = "💰", District = "", Slot = 0 },
				new { Id = "hub_arm", Name = "Garrison Armory", Col = 30, Row = 53, W = 4, H = 4, ColorHex = "#22c55e", Icon = "🎖️", District = "", Slot = 0 },

				// North-West Diplomatic, Sensor & Science Wing (4x4 each)
				new { Id = "hub_ahq", Name = "Alliance HQ", Col = 20, Row = 8, W = 4, H = 4, ColorHex = "#38bdf8", Icon = "🏛️", District = "", Slot = 0 },
				new { Id = "hub_com", Name = "Commanders Spire", Col = 19, Row = 16, W = 4, H = 4, ColorHex = "#22c55e", Icon = "🤝", District = "", Slot = 0 },
				new { Id = "hub_rng", Name = "The Deep Radar", Col = 11, Row = 10, W = 4, H = 4, ColorHex = "#f59e0b", Icon = "📡", District = "", Slot = 0 },
				new { Id = "hub_rsh", Name = "Research Directorate", Col = 10, Row = 18, W = 4, H = 4, ColorHex = "#a855f7", Icon = "🔬", District = "", Slot = 0 },

				// North-East Orbital Flight & Commerce Wing (4x4 each)
				new { Id = "hub_flt", Name = "Fleet Station", Col = 40, Row = 14, W = 4, H = 4, ColorHex = "#3b82f6", Icon = "🛰️", District = "", Slot = 0 },
				new { Id = "hub_trd", Name = "Trade Logistics", Col = 47, Row = 9, W = 4, H = 4, ColorHex = "#06b6d4", Icon = "📦", District = "", Slot = 0 },

				// West Power Station District (4x4 each)
				new { Id = "dist_e_0", Name = "Power Station A", Col = 17, Row = 24, W = 4, H = 4, ColorHex = "#d946ef", Icon = "⚡", District = "E", Slot = 0 },
				new { Id = "dist_e_1", Name = "Power Station B", Col = 9, Row = 27, W = 4, H = 4, ColorHex = "#d946ef", Icon = "⚡", District = "E", Slot = 1 },
				new { Id = "dist_e_2", Name = "Power Station C", Col = 17, Row = 32, W = 4, H = 4, ColorHex = "#d946ef", Icon = "⚡", District = "E", Slot = 2 },

				// South-West Iron Extraction District (4x4 each)
				new { Id = "dist_i_0", Name = "Iron Mine A", Col = 19, Row = 40, W = 4, H = 4, ColorHex = "#06b6d4", Icon = "⛏️", District = "I", Slot = 0 },
				new { Id = "dist_i_1", Name = "Iron Mine B", Col = 11, Row = 44, W = 4, H = 4, ColorHex = "#06b6d4", Icon = "⛏️", District = "I", Slot = 1 },
				new { Id = "dist_i_2", Name = "Iron Mine C", Col = 19, Row = 49, W = 4, H = 4, ColorHex = "#06b6d4", Icon = "⛏️", District = "I", Slot = 2 },

				// South-East Titanium Smelter District (4x4 each)
				new { Id = "dist_t_0", Name = "Titanium Smelter A", Col = 40, Row = 39, W = 4, H = 4, ColorHex = "#94a3b8", Icon = "💎", District = "T", Slot = 0 },
				new { Id = "dist_t_1", Name = "Titanium Smelter B", Col = 48, Row = 43, W = 4, H = 4, ColorHex = "#94a3b8", Icon = "💎", District = "T", Slot = 1 },
				new { Id = "dist_t_2", Name = "Titanium Smelter C", Col = 40, Row = 48, W = 4, H = 4, ColorHex = "#94a3b8", Icon = "💎", District = "T", Slot = 2 },

				// East Helium-3 Distillery District (4x4 each)
				new { Id = "dist_h3_0", Name = "H3 Distillery A", Col = 41, Row = 22, W = 4, H = 4, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 0 },
				new { Id = "dist_h3_1", Name = "H3 Distillery B", Col = 49, Row = 19, W = 4, H = 4, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 1 },
				new { Id = "dist_h3_2", Name = "H3 Distillery C", Col = 41, Row = 30, W = 4, H = 4, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 2 },
				new { Id = "dist_h3_3", Name = "H3 Distillery D", Col = 49, Row = 27, W = 4, H = 4, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 3 },
				new { Id = "dist_h3_4", Name = "H3 Distillery E", Col = 49, Row = 35, W = 4, H = 4, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 4 }
			};

			var cachedLevels = SupabaseService.HasValidSessionColonyCache ? SupabaseService.CachedBuildingLevels : null;
			if (cachedLevels != null && cachedLevels.Count > 0)
			{
				_elecDist[0] = cachedLevels.GetValueOrDefault("dist_e_0", 1);
				_elecDist[1] = cachedLevels.GetValueOrDefault("dist_e_1", 0);
				_elecDist[2] = cachedLevels.GetValueOrDefault("dist_e_2", 0);

				_ironDist[0] = cachedLevels.GetValueOrDefault("dist_i_0", 1);
				_ironDist[1] = cachedLevels.GetValueOrDefault("dist_i_1", 0);
				_ironDist[2] = cachedLevels.GetValueOrDefault("dist_i_2", 0);

				_titanDist[0] = cachedLevels.GetValueOrDefault("dist_t_0", 1);
				_titanDist[1] = cachedLevels.GetValueOrDefault("dist_t_1", 0);
				_titanDist[2] = cachedLevels.GetValueOrDefault("dist_t_2", 0);

				_h3Dist[0] = cachedLevels.GetValueOrDefault("dist_h3_0", 1);
				_h3Dist[1] = cachedLevels.GetValueOrDefault("dist_h3_1", 0);
				_h3Dist[2] = cachedLevels.GetValueOrDefault("dist_h3_2", 0);
				_h3Dist[3] = cachedLevels.GetValueOrDefault("dist_h3_3", 0);
				_h3Dist[4] = cachedLevels.GetValueOrDefault("dist_h3_4", 0);

				_industrialCoreLevel = cachedLevels.GetValueOrDefault("hub_cmd", 1);
			}

			bool canUseCachedPositions = false;
			if (cachedLevels != null && cachedLevels.GetValueOrDefault("__layout_schema_ver", 0) >= LAYOUT_SCHEMA_VERSION)
			{
				canUseCachedPositions = true;
				var checkList = new List<(int col, int row, int w, int h)>();
				foreach (var d in defs)
				{
					int c = d.Col;
					int r = d.Row;
					if (cachedLevels.TryGetValue($"__pos_col_{d.Id}", out int sc) &&
						cachedLevels.TryGetValue($"__pos_row_{d.Id}", out int sr))
					{
						c = Math.Clamp(sc, 0, GRID_COLS - d.W);
						r = Math.Clamp(sr, 0, GRID_ROWS - d.H);
					}
					if (!GameMath.IsFootprintInBounds(c, r, d.W, d.H))
					{
						canUseCachedPositions = false;
						break;
					}
					for (int i = 0; i < checkList.Count; i++)
					{
						var o = checkList[i];
						if (GameMath.DoFootprintsOverlap(c, r, d.W, d.H, o.col, o.row, o.w, o.h))
						{
							canUseCachedPositions = false;
							break;
						}
					}
					if (!canUseCachedPositions) break;
					checkList.Add((c, r, d.W, d.H));
				}
			}

			foreach (var def in defs)
			{
				Color themeColor = new Color(def.ColorHex);

				int spawnCol = def.Col;
				int spawnRow = def.Row;
				if (canUseCachedPositions &&
					cachedLevels != null &&
					cachedLevels.TryGetValue($"__pos_col_{def.Id}", out int cCol) &&
					cachedLevels.TryGetValue($"__pos_row_{def.Id}", out int cRow))
				{
					spawnCol = Math.Clamp(cCol, 0, GRID_COLS - def.W);
					spawnRow = Math.Clamp(cRow, 0, GRID_ROWS - def.H);
				}

				Vector2 top = GridToIso(spawnCol, spawnRow);
				Vector2 right = GridToIso(spawnCol + def.W, spawnRow);
				Vector2 bottom = GridToIso(spawnCol + def.W, spawnRow + def.H);
				Vector2 left = GridToIso(spawnCol, spawnRow + def.H);
				Vector2 center = (top + bottom) * 0.5f;

				float footprintW = def.W * TILE_WIDTH_HALF * 2.0f; // 640px (8x8), 480px (6x6), 320px (4x4)
				float footprintH = def.H * TILE_HEIGHT_HALF * 2.0f; // 360px (8x8), 270px (6x6), 180px (4x4)

				Node2D nodeAnchor = new Node2D
				{
					Name = $"Building_{def.Id}",
					Position = center,
					ZIndex = (int)(center.Y + footprintH * 0.5f)
				};

				Vector2 lTop = top - center;
				Vector2 lRight = right - center;
				Vector2 lBottom = bottom - center;
				Vector2 lLeft = left - center;

				Polygon2D poly = new Polygon2D
				{
					Polygon = new Vector2[] { lTop, lRight, lBottom, lLeft },
					Color = new Color(themeColor.R, themeColor.G, themeColor.B, 0.25f)
				};
				nodeAnchor.AddChild(poly);

				Line2D outline = new Line2D
				{
					Width = def.W >= 6 ? 2.8f : 2.2f,
					DefaultColor = themeColor,
					Antialiased = true,
					Visible = false
				};
				outline.AddPoint(lTop);
				outline.AddPoint(lRight);
				outline.AddPoint(lBottom);
				outline.AddPoint(lLeft);
				outline.AddPoint(lTop);
				nodeAnchor.AddChild(outline);

				// Internal 1x1 Sub-Tile Grid Lines (Visible in Edit Mode so 8x8, 6x6, and 4x4 block counts are unmistakably clear!)
				Node2D subGrid = new Node2D
				{
					Name = $"SubGrid_{def.Id}",
					Visible = false
				};
				Color subLineCol = new Color(themeColor.R, themeColor.G, themeColor.B, 0.55f);
				for (int sc = 1; sc < def.W; sc++)
				{
					Vector2 pStart = GridToIso(spawnCol + sc, spawnRow) - center;
					Vector2 pEnd = GridToIso(spawnCol + sc, spawnRow + def.H) - center;
					Line2D sLine = new Line2D { Width = 1.3f, DefaultColor = subLineCol, Antialiased = true };
					sLine.AddPoint(pStart);
					sLine.AddPoint(pEnd);
					subGrid.AddChild(sLine);
				}
				for (int sr = 1; sr < def.H; sr++)
				{
					Vector2 pStart = GridToIso(spawnCol, spawnRow + sr) - center;
					Vector2 pEnd = GridToIso(spawnCol + def.W, spawnRow + sr) - center;
					Line2D sLine = new Line2D { Width = 1.3f, DefaultColor = subLineCol, Antialiased = true };
					sLine.AddPoint(pStart);
					sLine.AddPoint(pEnd);
					subGrid.AddChild(sLine);
				}
				nodeAnchor.AddChild(subGrid);

				// Resolve texture for this building node so its sprite adapts cleanly inside its 8x8, 6x6, or 4x4 diamond footprint!
				Texture2D matchedTex = null;
				if (def.Id == "hub_cmd") matchedTex = coreTexture;
				else if (def.Id == "hub_silo") matchedTex = siloTexture;
				else if (def.Id.StartsWith("dist_e_")) matchedTex = powerTexture;
				else if (def.Id.StartsWith("dist_i_")) matchedTex = ironTexture;
				else if (def.Id.StartsWith("dist_t_")) matchedTex = titaniumTexture;
				else if (def.Id.StartsWith("dist_h3_")) matchedTex = h3Texture;
				else if (def.Id == "hub_shd") matchedTex = shieldTexture;
				else if (def.Id == "hub_mil") matchedTex = shipyardTexture;
				else if (def.Id == "hub_mgd") matchedTex = goldExchangeTexture;
				else if (def.Id == "hub_arm") matchedTex = armoryTexture;
				else if (def.Id == "hub_ahq") matchedTex = allianceHqTexture;
				else if (def.Id == "hub_com") matchedTex = commandersSpireTexture;
				else if (def.Id == "hub_flt") matchedTex = fleetStationTexture;
				else if (def.Id == "hub_rng") matchedTex = deepRadarTexture;
				else if (def.Id == "hub_trd") matchedTex = tradeLogisticsTexture;
				else if (def.Id == "hub_rsh") matchedTex = researchTexture;

				Sprite2D bSprite = null;
				if (matchedTex != null && matchedTex.GetWidth() > 0)
				{
					bSprite = new Sprite2D
					{
						Name = $"VisualSprite_{def.Id}",
						Texture = matchedTex,
						Centered = true
					};

					// Dynamically scale the image to fit the exact width of its 8x8 (640px), 6x6 (480px), or 4x4 (320px) isometric diamond node
					float scaleFactor = footprintW / (float)matchedTex.GetWidth();
					bSprite.Scale = new Vector2(scaleFactor, scaleFactor);

					float spriteScaledHeight = matchedTex.GetHeight() * scaleFactor;
					float bottomSeatOffset = (def.W >= 8) ? 10.0f : (def.W >= 6 ? 8.0f : 6.0f);
					bSprite.Position = new Vector2(0, (footprintH * 0.5f) - (spriteScaledHeight * 0.5f) - bottomSeatOffset);

					nodeAnchor.AddChild(bSprite);
					poly.Visible = false;
				}
				else
				{
					// Architectural 2.5D Inner Isometric Citadel Pad for any 4x4 node without an external PNG texture
					Polygon2D innerPad = new Polygon2D
					{
						Name = $"InnerPad_{def.Id}",
						Polygon = new Vector2[] { lTop * 0.68f, lRight * 0.68f, lBottom * 0.68f, lLeft * 0.68f },
						Color = new Color(themeColor.R, themeColor.G, themeColor.B, 0.32f)
					};
					nodeAnchor.AddChild(innerPad);

					Line2D innerRim = new Line2D
					{
						Width = 2.0f,
						DefaultColor = new Color(themeColor.R, themeColor.G, themeColor.B, 0.85f),
						Antialiased = true
					};
					innerRim.AddPoint(lTop * 0.68f);
					innerRim.AddPoint(lRight * 0.68f);
					innerRim.AddPoint(lBottom * 0.68f);
					innerRim.AddPoint(lLeft * 0.68f);
					innerRim.AddPoint(lTop * 0.68f);
					nodeAnchor.AddChild(innerRim);
				}

				int startLevel = 1;
				if (cachedLevels != null && cachedLevels.Count > 0)
				{
					startLevel = cachedLevels.GetValueOrDefault(def.Id, 0);
					if (startLevel == 0 && (def.Id == "hub_cmd" || def.Id == "hub_silo" || def.Id == "hub_mgd" || def.Id == "dist_e_0" || def.Id == "dist_i_0" || def.Id == "dist_t_0" || def.Id == "dist_h3_0"))
					{
						startLevel = 1;
					}
				}
				else
				{
					if (def.District == "E") startLevel = _elecDist[def.Slot];
					else if (def.District == "I") startLevel = _ironDist[def.Slot];
					else if (def.District == "T") startLevel = _titanDist[def.Slot];
					else if (def.District == "H3") startLevel = _h3Dist[def.Slot];
					else if (def.Id == "hub_cmd") startLevel = _industrialCoreLevel;
				}

				// Dedicated High-Visibility Badge Container (ZIndex = 4000)
				PanelContainer badge = new PanelContainer
				{
					MouseFilter = Control.MouseFilterEnum.Ignore
				};

				float badgeY = (def.W >= 4) ? (footprintH * 0.5f + 6.0f) : (footprintH * 0.5f - 6.0f);
				badge.CustomMinimumSize = new Vector2(196, 36);

				StyleBoxFlat badgeStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.02f, 0.04f, 0.08f, 0.96f),
					BorderColor = new Color(themeColor.R, themeColor.G, themeColor.B, 0.95f),
					BorderWidthLeft = 2,
					BorderWidthRight = 2,
					BorderWidthTop = 2,
					BorderWidthBottom = 2,
					CornerRadiusTopLeft = 18,
					CornerRadiusTopRight = 18,
					CornerRadiusBottomLeft = 18,
					CornerRadiusBottomRight = 18,
					ContentMarginLeft = 18,
					ContentMarginRight = 18,
					ContentMarginTop = 6,
					ContentMarginBottom = 6,
					ShadowColor = new Color(0f, 0f, 0f, 0.85f),
					ShadowSize = 6
				};
				badge.AddThemeStyleboxOverride("panel", badgeStyle);

				Color brightBadgeText = themeColor.Lerp(Colors.White, 0.35f);
				string initialBadgeText = GameMath.IsBuildingUpgradable(def.Id)
					? $"{def.Icon} {def.Name.ToUpper()}  ·  LVL {startLevel}"
					: $"{def.Icon} {def.Name.ToUpper()}";
				Label badgeLbl = new Label
				{
					Text = initialBadgeText,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					Modulate = brightBadgeText,
					MouseFilter = Control.MouseFilterEnum.Ignore
				};
				badgeLbl.AddThemeFontSizeOverride("font_size", 15);
				badgeLbl.AddThemeColorOverride("font_outline_color", Colors.Black);
				badgeLbl.AddThemeConstantOverride("outline_size", 4);
				badge.AddChild(badgeLbl);

				float initBadgeW = Mathf.Max(196f, badge.GetCombinedMinimumSize().X);
				badge.Position = new Vector2(center.X - (initBadgeW * 0.5f), center.Y + badgeY);

				_badgeContainer.AddChild(badge);
				_buildingContainer.AddChild(nodeAnchor);

				_buildingRegistry[def.Id] = new BuildingNodeData
				{
					Id = def.Id,
					Name = def.Name,
					Col = spawnCol,
					Row = spawnRow,
					W = def.W,
					H = def.H,
					ThemeColor = themeColor,
					Icon = def.Icon,
					Level = startLevel,
					DistrictCode = def.District,
					SlotIndex = def.Slot,
					AnchorNode = nodeAnchor,
					DiamondPoly = poly,
					DiamondOutline = outline,
					SubGridContainer = subGrid,
					BuildingSprite = bSprite,
					BadgeContainer = badge,
					BadgeLabel = badgeLbl,
					BadgeStyle = badgeStyle
				};
			}
		}

		private Dictionary<string, int> GetAllBuildingLevels()
		{
			if (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null && BaseHUD.Instance.BuildingLevels.Count > 0)
			{
				return BaseHUD.Instance.BuildingLevels;
			}

			var dict = new Dictionary<string, int>();
			foreach (var kvp in _buildingRegistry)
			{
				dict[kvp.Key] = kvp.Value.Level;
			}
			return dict;
		}

		private void OnBuildingClicked(string buildingId)
		{
			if (IsEditLayoutMode)
			{
				SelectBuildingForRelocation(buildingId);
			}
			else
			{
				if (_buildingRegistry.TryGetValue(buildingId, out BuildingNodeData bData))
				{
					int[] activeLevels = null;
					if (bData.DistrictCode == "E") activeLevels = _elecDist;
					else if (bData.DistrictCode == "I") activeLevels = _ironDist;
					else if (bData.DistrictCode == "T") activeLevels = _titanDist;
					else if (bData.DistrictCode == "H3") activeLevels = _h3Dist;

					_inspectorModal.InspectBuilding(
						bData.Id,
						bData.Name,
						bData.Level,
						bData.ThemeColor,
						bData.DistrictCode,
						bData.SlotIndex,
						activeLevels,
						_industrialCoreLevel,
						GetAllBuildingLevels()
					);
				}
			}
		}

		private void OnBuildingUpgraded(string buildingId, int newLevel)
		{
			if (_buildingRegistry.TryGetValue(buildingId, out BuildingNodeData data))
			{
				data.Level = newLevel;

				if (data.DistrictCode == "E" && data.SlotIndex < _elecDist.Length) _elecDist[data.SlotIndex] = newLevel;
				else if (data.DistrictCode == "I" && data.SlotIndex < _ironDist.Length) _ironDist[data.SlotIndex] = newLevel;
				else if (data.DistrictCode == "T" && data.SlotIndex < _titanDist.Length) _titanDist[data.SlotIndex] = newLevel;
				else if (data.DistrictCode == "H3" && data.SlotIndex < _h3Dist.Length) _h3Dist[data.SlotIndex] = newLevel;
				else if (data.Id == "hub_cmd") _industrialCoreLevel = newLevel;

				if (data.BadgeLabel != null)
				{
					data.BadgeLabel.Text = $"{data.Icon} {data.Name.ToUpper()}  ·  LVL {newLevel}";
				}

				GD.Print($"[BASE VIEW] 2.5D Sprite Updated: {data.Name} is now Level {newLevel}!");
			}
		}

		private void OnEditModeToggled(bool isEditing)
		{
			IsEditLayoutMode = isEditing;

			if (_gridLineCanvas != null)
			{
				_gridLineCanvas.Visible = IsEditLayoutMode;
			}

			foreach (var kvp in _buildingRegistry)
			{
				if (kvp.Value.DiamondOutline != null)
				{
					kvp.Value.DiamondOutline.Visible = IsEditLayoutMode;
				}
				if (kvp.Value.SubGridContainer != null)
				{
					kvp.Value.SubGridContainer.Visible = IsEditLayoutMode;
				}
				if (kvp.Value.DiamondPoly != null)
				{
					kvp.Value.DiamondPoly.Visible = IsEditLayoutMode || (kvp.Value.BuildingSprite == null);
				}
			}

			if (!IsEditLayoutMode)
			{
				if (!string.IsNullOrEmpty(SelectedBuildingId) && _buildingRegistry.TryGetValue(SelectedBuildingId, out var stagedData))
				{
					stagedData.Col = _preMoveGridPos.X;
					stagedData.Row = _preMoveGridPos.Y;
					ApplySavedBuildingPositionToViewport(stagedData);
					if (stagedData.BuildingSprite == null && stagedData.DiamondPoly != null)
					{
						stagedData.DiamondPoly.Color = new Color(stagedData.ThemeColor.R, stagedData.ThemeColor.G, stagedData.ThemeColor.B, 0.25f);
					}
				}
				ClearTeleportReticle();
				SelectedBuildingId = null;
			}

			GD.Print($"[BASE VIEW] Edit Mode Active: {IsEditLayoutMode}. Grid visibility synced.");
		}

		private void OnSaveLayoutRequested()
		{
			// If the player still had a building selected in the reticle when clicking "✓ SAVE BASE", auto-confirm it if valid!
			if (!string.IsNullOrEmpty(SelectedBuildingId) && _buildingRegistry.TryGetValue(SelectedBuildingId, out BuildingNodeData stagedData))
			{
				if (ValidateFootprintLegality(SelectedBuildingId, StagedGridPos.X, StagedGridPos.Y, stagedData.W, stagedData.H))
				{
					stagedData.Col = StagedGridPos.X;
					stagedData.Row = StagedGridPos.Y;
					ApplySavedBuildingPositionToViewport(stagedData);
				}
				else
				{
					stagedData.Col = _preMoveGridPos.X;
					stagedData.Row = _preMoveGridPos.Y;
					ApplySavedBuildingPositionToViewport(stagedData);
				}
			}

			ClearTeleportReticle();
			SelectedBuildingId = null;

			if (_gridLineCanvas != null) _gridLineCanvas.Visible = false;

			foreach (var kvp in _buildingRegistry)
			{
				var bData = kvp.Value;
				if (bData.DiamondOutline != null) bData.DiamondOutline.Visible = false;
				if (bData.SubGridContainer != null) bData.SubGridContainer.Visible = false;
				if (bData.BuildingSprite != null && bData.DiamondPoly != null)
				{
					bData.DiamondPoly.Visible = false;
				}
				else if (bData.DiamondPoly != null)
				{
					bData.DiamondPoly.Color = new Color(bData.ThemeColor.R, bData.ThemeColor.G, bData.ThemeColor.B, 0.25f);
				}

				if (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null)
				{
					BaseHUD.Instance.BuildingLevels["__layout_schema_ver"] = LAYOUT_SCHEMA_VERSION;
					BaseHUD.Instance.BuildingLevels[$"__pos_col_{kvp.Key}"] = bData.Col;
					BaseHUD.Instance.BuildingLevels[$"__pos_row_{kvp.Key}"] = bData.Row;
				}
			}

			BaseHUD.Instance?.TriggerInstantSave();
			GD.Print("[BASE VIEW] Custom Base Layout Saved to Supabase Cloud! Defense Matrix Deployed.");
		}

		private void SelectBuildingForRelocation(string buildingId)
		{
			if (!_buildingRegistry.TryGetValue(buildingId, out BuildingNodeData data)) return;

			SelectedBuildingId = buildingId;
			StagedGridPos = new Vector2I(data.Col, data.Row);
			_preMoveGridPos = StagedGridPos;

			RenderTeleportReticle(data);
		}

		private void ConfirmRelocation()
		{
			if (string.IsNullOrEmpty(SelectedBuildingId) || !_buildingRegistry.TryGetValue(SelectedBuildingId, out BuildingNodeData data)) return;

			if (!ValidateFootprintLegality(SelectedBuildingId, StagedGridPos.X, StagedGridPos.Y, data.W, data.H))
			{
				GD.PrintErr("[BASE VIEW] Cannot place building here! Area obstructed or overlapping.");
				return;
			}

			data.Col = StagedGridPos.X;
			data.Row = StagedGridPos.Y;
			UpdateBuildingPositionAndVisuals(data, StagedGridPos);

			if (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null)
			{
				BaseHUD.Instance.BuildingLevels["__layout_schema_ver"] = LAYOUT_SCHEMA_VERSION;
				foreach (var kvp in _buildingRegistry)
				{
					BaseHUD.Instance.BuildingLevels[$"__pos_col_{kvp.Key}"] = kvp.Value.Col;
					BaseHUD.Instance.BuildingLevels[$"__pos_row_{kvp.Key}"] = kvp.Value.Row;
				}
				BaseHUD.Instance.TriggerInstantSave();
			}

			ClearTeleportReticle();
			SelectedBuildingId = null;
			GD.Print($"[BASE VIEW] Placement confirmed & cloud-saved for {data.Name} at [{data.Col}, {data.Row}]");
		}

		private void CancelRelocation()
		{
			if (string.IsNullOrEmpty(SelectedBuildingId) || !_buildingRegistry.TryGetValue(SelectedBuildingId, out BuildingNodeData data)) return;

			StagedGridPos = _preMoveGridPos;
			UpdateBuildingPositionAndVisuals(data, _preMoveGridPos);

			ClearTeleportReticle();
			SelectedBuildingId = null;
			GD.Print($"[BASE VIEW] Placement cancelled for {data.Name}");
		}

		private bool ValidateFootprintLegality(string buildingId, int targetCol, int targetRow, int w, int h)
		{
			if (!GameMath.IsFootprintInBounds(targetCol, targetRow, w, h)) return false;

			foreach (var kvp in _buildingRegistry)
			{
				if (kvp.Key == buildingId) continue;
				var other = kvp.Value;
				if (GameMath.DoFootprintsOverlap(targetCol, targetRow, w, h, other.Col, other.Row, other.W, other.H))
				{
					return false;
				}
			}

			return true;
		}

		private void UpdateBuildingPositionAndVisuals(BuildingNodeData data, Vector2I gridPos)
		{
			Vector2 top = GridToIso(gridPos.X, gridPos.Y);
			Vector2 bottom = GridToIso(gridPos.X + data.W, gridPos.Y + data.H);
			Vector2 center = (top + bottom) * 0.5f;

			float footprintH = data.H * TILE_HEIGHT_HALF * 2.0f;

			data.AnchorNode.Position = center;
			data.AnchorNode.ZIndex = (int)(center.Y + footprintH * 0.5f);

			float badgeY = (data.W >= 4) ? (footprintH * 0.5f + 6.0f) : (footprintH * 0.5f - 6.0f);
			if (data.BadgeContainer != null)
			{
				float actualW = Mathf.Max(data.BadgeContainer.Size.X, data.BadgeContainer.GetCombinedMinimumSize().X);
				data.BadgeContainer.Position = new Vector2(center.X - (actualW * 0.5f), center.Y + badgeY);
			}

			bool isValid = ValidateFootprintLegality(data.Id, gridPos.X, gridPos.Y, data.W, data.H);
			Color statusColor = isValid ? new Color("#22C55E") : new Color("#EF4444");

			if (data.DiamondPoly != null)
			{
				data.DiamondPoly.Visible = true;
				data.DiamondPoly.Color = new Color(statusColor.R, statusColor.G, statusColor.B, 0.55f);
			}
			if (data.DiamondOutline != null)
			{
				data.DiamondOutline.Visible = true;
				data.DiamondOutline.DefaultColor = statusColor;
			}
		}

		private void RenderTeleportReticle(BuildingNodeData data)
		{
			ClearTeleportReticle();

			Vector2 top = GridToIso(StagedGridPos.X, StagedGridPos.Y);
			Vector2 bottom = GridToIso(StagedGridPos.X + data.W, StagedGridPos.Y + data.H);
			Vector2 center = (top + bottom) * 0.5f;

			bool isValid = ValidateFootprintLegality(data.Id, StagedGridPos.X, StagedGridPos.Y, data.W, data.H);

			float reticleRadius = Math.Max(data.W, data.H) * 22.0f;

			// Keep ONLY the Confirm (✓) and Cancel (✕) buttons — directional arrows removed in favor of direct Click/Touch-Hold & Drag!
			Button confirmBtn = CreateActionBubble("✓", center + new Vector2(reticleRadius + 30, -reticleRadius), isValid ? new Color("#22C55E") : new Color("#64748B"), ConfirmRelocation);
			confirmBtn.Disabled = !isValid;
			_teleportReticleContainer.AddChild(confirmBtn);

			_teleportReticleContainer.AddChild(CreateActionBubble("✕", center + new Vector2(-reticleRadius - 30, -reticleRadius), new Color("#EF4444"), CancelRelocation));
		}

		private Button CreateActionBubble(string symbol, Vector2 pos, Color bgCol, Action onPress)
		{
			Button btn = new Button
			{
				Text = symbol,
				CustomMinimumSize = new Vector2(46, 46),
				Position = pos - new Vector2(23, 23),
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			btn.AddThemeFontSizeOverride("font_size", 17);

			StyleBoxFlat s = new StyleBoxFlat
			{
				BgColor = bgCol,
				BorderColor = Colors.White,
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 23,
				CornerRadiusTopRight = 23,
				CornerRadiusBottomLeft = 23,
				CornerRadiusBottomRight = 23
			};
			btn.AddThemeStyleboxOverride("normal", s);
			btn.Pressed += onPress;
			return btn;
		}

		private void ClearTeleportReticle()
		{
			if (_teleportReticleContainer != null)
			{
				foreach (Node child in _teleportReticleContainer.GetChildren())
				{
					child.QueueFree();
				}
			}
		}
	}
}
