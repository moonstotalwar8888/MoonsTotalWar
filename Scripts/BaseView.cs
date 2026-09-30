using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER 2.5D BASE VIEWPORT (v48.0 Titanium Smelter & Standard Seating Edition)
	/// - Central Citadel: 6x6 (36 Tiles) Megastructure Industrial Core seated on ground diamond.
	/// - Storage Silos: 4x4 (16 Tiles) Pressurized Containment Vault Facility.
	/// - Power Station Grid: 2x2 Footprints for Nodes A, B, and C with live plasma sprites.
	/// - Iron Extraction Grid: 2x2 Footprints for Nodes A, B, and C with standard uniform diamond seating.
	/// - Titanium Smelter Grid: 2x2 Footprints for Nodes A, B, and C with high-heat industrial refinery sprites.
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

		public const int GRID_COLS = 48; 
		public const int GRID_ROWS = 50; 

		public const float TILE_WIDTH_HALF = 40.0f;
		public const float TILE_HEIGHT_HALF = 22.5f;

		public const float TERRAIN_WIDTH = 4500.0f;
		public const float TERRAIN_HEIGHT = 2512.0f;

		public const float MIN_ZOOM = 0.52f;
		public const float MAX_ZOOM = 2.0f;

		// Camera Viewport Controller
		private Camera2D _camera;
		private Vector2 _targetCameraPos;
		private float _targetZoom = 0.85f;
		private bool _isDraggingCamera = false;
		private Vector2 _dragStartMousePos;
		private Vector2 _dragStartCameraPos;
		private float _dragAccumulatedDistance = 0.0f;
		private Vector2 _terrainCenterPos = Vector2.Zero;

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
			public Sprite2D BuildingSprite;
			public Control BadgeContainer;
			public Label BadgeLabel;
		}

		private Dictionary<string, BuildingNodeData> _buildingRegistry = new Dictionary<string, BuildingNodeData>();

		public override void _Ready()
		{
			Vector2 baseCenter = GridToIso(24, 25);
			_terrainCenterPos = baseCenter;

			// 1. Initialize Camera
			_camera = GetNodeOrNull<Camera2D>("Camera2D");
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
			_hudLayer.AddChild(_hudInstance);

			// 8. Build Layer 110: Centered Inspector Modal
			_modalLayer = new CanvasLayer { Name = "ModalLayer", Layer = 110 };
			AddChild(_modalLayer);

			_inspectorModal = new BuildingInspectorModal();
			_inspectorModal.BuildingUpgraded += OnBuildingUpgraded;
			_modalLayer.AddChild(_inspectorModal);

			_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
			_camera.Position = _targetCameraPos;

			GD.Print("[BASE VIEW] Master Viewport Engaged: Standard Uniform Diamond Seating active.");
		}

		private void OnColonyDataLoaded()
		{
			if (BaseHUD.Instance == null || BaseHUD.Instance.BuildingLevels == null) return;

			var cloudLevels = BaseHUD.Instance.BuildingLevels;

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
					data.BadgeLabel.Text = $"{data.Icon} {data.Name.ToUpper()}  ·  LVL {cloudLvl}";
				}
			}

			GD.Print("[BASE VIEW] 2.5D Viewport Synchronized with Supabase Cloud Data.");
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
				_camera.Position = _camera.Position.Lerp(_targetCameraPos, dt * 14.0f);
				_camera.Zoom = _camera.Zoom.Lerp(new Vector2(_targetZoom, _targetZoom), dt * 14.0f);
			}
		}

		public override void _Input(InputEvent @event)
		{
			if (@event is InputEventMouseButton mb)
			{
				if (mb.IsPressed())
				{
					if (mb.ButtonIndex == MouseButton.WheelUp)
					{
						_targetZoom = Mathf.Clamp(_targetZoom * 1.12f, MIN_ZOOM, MAX_ZOOM);
						_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.WheelDown)
					{
						_targetZoom = Mathf.Clamp(_targetZoom * 0.88f, MIN_ZOOM, MAX_ZOOM);
						_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						if (mb.Position.Y > 65 && mb.Position.Y < GetViewportRect().Size.Y - 65 && (_inspectorModal == null || !_inspectorModal.Visible))
						{
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
						_isDraggingCamera = false;

						// Stationary click/tap detection: threshold 7px
						if (_dragAccumulatedDistance < 7.0f && (_inspectorModal == null || !_inspectorModal.Visible))
						{
							if (mb.Position.Y > 65 && mb.Position.Y < GetViewportRect().Size.Y - 65)
							{
								Vector2 worldClickPos = GetGlobalMousePosition();
								HandleCentralizedBuildingClick(worldClickPos);
							}
						}
					}
					else if (mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						_isDraggingCamera = false;
					}
				}
			}

			if (@event is InputEventMouseMotion mm && _isDraggingCamera)
			{
				_dragAccumulatedDistance += mm.Relative.Length();
				Vector2 delta = (mm.Position - _dragStartMousePos) / _camera.Zoom.X;
				_targetCameraPos = ClampCameraPosition(_dragStartCameraPos - delta, _targetZoom);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventScreenDrag sd)
			{
				_dragAccumulatedDistance += sd.Relative.Length();
				Vector2 delta = sd.Relative / _camera.Zoom.X;
				_targetCameraPos = ClampCameraPosition(_targetCameraPos - delta, _targetZoom);
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		private void HandleCentralizedBuildingClick(Vector2 worldClickPos)
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
					OnBuildingClicked(bData.Id);
					return;
				}
			}
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
				_terrainSprite.Scale = Vector2.One; 
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

		private void SpawnAudited25BuildingLayout()
		{
			Texture2D coreTexture = IndustrialCoreTexture;
			if (coreTexture == null && ResourceLoader.Exists("res://Assets/Buildings/industrial_core.png"))
			{
				coreTexture = ResourceLoader.Load<Texture2D>("res://Assets/Buildings/industrial_core.png");
			}

			Texture2D siloTexture = StorageSiloTexture;
			if (siloTexture == null && ResourceLoader.Exists("res://Assets/Buildings/storage_silos.png"))
			{
				siloTexture = ResourceLoader.Load<Texture2D>("res://Assets/Buildings/storage_silos.png");
			}

			Texture2D powerTexture = PowerStationTexture;
			if (powerTexture == null && ResourceLoader.Exists("res://Assets/Buildings/power_station.png"))
			{
				powerTexture = ResourceLoader.Load<Texture2D>("res://Assets/Buildings/power_station.png");
			}

			Texture2D ironTexture = IronMineTexture;
			if (ironTexture == null && ResourceLoader.Exists("res://Assets/Buildings/iron_mine.png"))
			{
				ironTexture = ResourceLoader.Load<Texture2D>("res://Assets/Buildings/iron_mine.png");
			}

			Texture2D titaniumTexture = TitaniumSmelterTexture;
			if (titaniumTexture == null && ResourceLoader.Exists("res://Assets/Buildings/titanium_smelter.png"))
			{
				titaniumTexture = ResourceLoader.Load<Texture2D>("res://Assets/Buildings/titanium_smelter.png");
			}

			var defs = new[]
			{
				new { Id = "hub_cmd", Name = "Industrial Core", Col = 19, Row = 17, W = 6, H = 6, ColorHex = "#ffffff", Icon = "🏢", District = "", Slot = 0 },
				new { Id = "hub_shd", Name = "Planetary Shield", Col = 21, Row = 13, W = 2, H = 2, ColorHex = "#38bdf8", Icon = "🛡️", District = "", Slot = 0 },
				new { Id = "hub_mil", Name = "Orbital Shipyard", Col = 21, Row = 10, W = 2, H = 2, ColorHex = "#ef4444", Icon = "⚔️", District = "", Slot = 0 },
				new { Id = "hub_silo", Name = "Storage Silos", Col = 20, Row = 24, W = 4, H = 4, ColorHex = "#00f0ff", Icon = "🛢️", District = "", Slot = 0 },
				new { Id = "hub_mgd", Name = "Galaxy Gold Exchange", Col = 21, Row = 29, W = 2, H = 2, ColorHex = "#fbbf24", Icon = "💰", District = "", Slot = 0 },
				new { Id = "hub_arm", Name = "Garrison Armory", Col = 21, Row = 32, W = 2, H = 2, ColorHex = "#22c55e", Icon = "🎖️", District = "", Slot = 0 },
				new { Id = "hub_ahq", Name = "Alliance HQ", Col = 18, Row = 10, W = 2, H = 2, ColorHex = "#38bdf8", Icon = "🏛️", District = "", Slot = 0 },
				new { Id = "hub_com", Name = "Commanders Spire", Col = 15, Row = 14, W = 2, H = 2, ColorHex = "#22c55e", Icon = "🤝", District = "", Slot = 0 },
				new { Id = "hub_flt", Name = "Fleet Station", Col = 27, Row = 14, W = 2, H = 2, ColorHex = "#3b82f6", Icon = "🛰️", District = "", Slot = 0 },
				new { Id = "hub_rng", Name = "The Deep Radar", Col = 12, Row = 11, W = 2, H = 2, ColorHex = "#f59e0b", Icon = "📡", District = "", Slot = 0 },
				new { Id = "hub_trd", Name = "Trade Logistics", Col = 30, Row = 11, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "📦", District = "", Slot = 0 },
				new { Id = "hub_rsh", Name = "Research Directorate", Col = 9, Row = 14, W = 2, H = 2, ColorHex = "#a855f7", Icon = "🔬", District = "", Slot = 0 },
				new { Id = "dist_e_0", Name = "Power Station A", Col = 13, Row = 18, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡", District = "E", Slot = 0 },
				new { Id = "dist_e_1", Name = "Power Station B", Col = 10, Row = 20, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡", District = "E", Slot = 1 },
				new { Id = "dist_e_2", Name = "Power Station C", Col = 13, Row = 22, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡", District = "E", Slot = 2 },
				new { Id = "dist_i_0", Name = "Iron Mine A", Col = 16, Row = 25, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️", District = "I", Slot = 0 },
				new { Id = "dist_i_1", Name = "Iron Mine B", Col = 13, Row = 27, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️", District = "I", Slot = 1 },
				new { Id = "dist_i_2", Name = "Iron Mine C", Col = 16, Row = 29, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️", District = "I", Slot = 2 },
				new { Id = "dist_t_0", Name = "Titanium Smelter A", Col = 26, Row = 24, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎", District = "T", Slot = 0 },
				new { Id = "dist_t_1", Name = "Titanium Smelter B", Col = 29, Row = 27, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎", District = "T", Slot = 1 },
				new { Id = "dist_t_2", Name = "Titanium Smelter C", Col = 26, Row = 28, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎", District = "T", Slot = 2 },
				new { Id = "dist_h3_0", Name = "H3 Distillery A", Col = 29, Row = 18, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 0 },
				new { Id = "dist_h3_1", Name = "H3 Distillery B", Col = 32, Row = 19, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 1 },
				new { Id = "dist_h3_2", Name = "H3 Distillery C", Col = 29, Row = 21, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 2 },
				new { Id = "dist_h3_3", Name = "H3 Distillery D", Col = 32, Row = 22, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 3 },
				new { Id = "dist_h3_4", Name = "H3 Distillery E", Col = 29, Row = 24, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", District = "H3", Slot = 4 }
			};

			foreach (var def in defs)
			{
				Color themeColor = new Color(def.ColorHex);

				Vector2 top = GridToIso(def.Col, def.Row);
				Vector2 right = GridToIso(def.Col + def.W, def.Row);
				Vector2 bottom = GridToIso(def.Col + def.W, def.Row + def.H);
				Vector2 left = GridToIso(def.Col, def.Row + def.H);
				Vector2 center = (top + bottom) * 0.5f;

				float footprintW = def.W * TILE_WIDTH_HALF * 2.0f; // 480px (6x6), 320px (4x4), 160px (2x2)
				float footprintH = def.H * TILE_HEIGHT_HALF * 2.0f; // 270px (6x6), 180px (4x4), 90px (2x2)

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
					Color = new Color(themeColor.R, themeColor.G, themeColor.B, def.W >= 4 ? 0.25f : 0.35f)
				};
				nodeAnchor.AddChild(poly);

				Line2D outline = new Line2D
				{
					Width = def.W >= 4 ? 2.5f : 1.8f,
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

				Sprite2D bSprite = null;

				// 1. Industrial Core (6x6)
				if (def.Id == "hub_cmd" && coreTexture != null)
				{
					bSprite = new Sprite2D
					{
						Name = "CoreVisualSprite",
						Texture = coreTexture,
						Centered = true
					};

					float scaleFactor = footprintW / coreTexture.GetWidth();
					bSprite.Scale = new Vector2(scaleFactor, scaleFactor);

					float spriteScaledHeight = coreTexture.GetHeight() * scaleFactor;
					bSprite.Position = new Vector2(0, (footprintH * 0.5f) - (spriteScaledHeight * 0.5f) - 8.0f);

					nodeAnchor.AddChild(bSprite);
					poly.Visible = false;
				}
				// 2. Storage Silos (4x4)
				else if (def.Id == "hub_silo" && siloTexture != null)
				{
					bSprite = new Sprite2D
					{
						Name = "SiloVisualSprite",
						Texture = siloTexture,
						Centered = true
					};

					float scaleFactor = footprintW / siloTexture.GetWidth();
					bSprite.Scale = new Vector2(scaleFactor, scaleFactor);

					float spriteScaledHeight = siloTexture.GetHeight() * scaleFactor;
					bSprite.Position = new Vector2(0, (footprintH * 0.5f) - (spriteScaledHeight * 0.5f) - 6.0f);

					nodeAnchor.AddChild(bSprite);
					poly.Visible = false;
				}
				// 3. Power Stations A, B, and C (2x2)
				else if (def.Id.StartsWith("dist_e_") && powerTexture != null)
				{
					bSprite = new Sprite2D
					{
						Name = $"PowerVisualSprite_{def.Id}",
						Texture = powerTexture,
						Centered = true
					};

					float scaleFactor = footprintW / powerTexture.GetWidth();
					bSprite.Scale = new Vector2(scaleFactor, scaleFactor);

					float spriteScaledHeight = powerTexture.GetHeight() * scaleFactor;
					bSprite.Position = new Vector2(0, (footprintH * 0.5f) - (spriteScaledHeight * 0.5f) - 4.0f);

					nodeAnchor.AddChild(bSprite);
					poly.Visible = false;
				}
				// 4. Iron Mines A, B, and C (2x2 - Standard Seating Formula)
				else if (def.Id.StartsWith("dist_i_") && ironTexture != null)
				{
					bSprite = new Sprite2D
					{
						Name = $"IronVisualSprite_{def.Id}",
						Texture = ironTexture,
						Centered = true
					};

					float scaleFactor = footprintW / ironTexture.GetWidth();
					bSprite.Scale = new Vector2(scaleFactor, scaleFactor);

					float spriteScaledHeight = ironTexture.GetHeight() * scaleFactor;
					bSprite.Position = new Vector2(0, (footprintH * 0.5f) - (spriteScaledHeight * 0.5f) - 4.0f);

					nodeAnchor.AddChild(bSprite);
					poly.Visible = false;
				}
				// 5. Titanium Smelters A, B, and C (2x2 - Standard Seating Formula)
				else if (def.Id.StartsWith("dist_t_") && titaniumTexture != null)
				{
					bSprite = new Sprite2D
					{
						Name = $"TitaniumVisualSprite_{def.Id}",
						Texture = titaniumTexture,
						Centered = true
					};

					float scaleFactor = footprintW / titaniumTexture.GetWidth();
					bSprite.Scale = new Vector2(scaleFactor, scaleFactor);

					float spriteScaledHeight = titaniumTexture.GetHeight() * scaleFactor;
					bSprite.Position = new Vector2(0, (footprintH * 0.5f) - (spriteScaledHeight * 0.5f) - 4.0f);

					nodeAnchor.AddChild(bSprite);
					poly.Visible = false;
				}

				int startLevel = 1;
				if (def.District == "E") startLevel = _elecDist[def.Slot];
				else if (def.District == "I") startLevel = _ironDist[def.Slot];
				else if (def.District == "T") startLevel = _titanDist[def.Slot];
				else if (def.District == "H3") startLevel = _h3Dist[def.Slot];
				else if (def.Id == "hub_cmd") startLevel = _industrialCoreLevel;

				// Dedicated Low-Profile Badge Container (ZIndex = 4000)
				PanelContainer badge = new PanelContainer
				{
					MouseFilter = Control.MouseFilterEnum.Ignore
				};

				float badgeY = (def.W >= 4) ? (footprintH * 0.5f + 6.0f) : (footprintH * 0.5f - 6.0f);
				badge.Position = center + new Vector2(-75, badgeY);
				badge.CustomMinimumSize = new Vector2(150, 22);

				StyleBoxFlat badgeStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.02f, 0.04f, 0.07f, 0.92f),
					BorderColor = new Color(themeColor.R, themeColor.G, themeColor.B, 0.85f),
					BorderWidthLeft = 1,
					BorderWidthRight = 1,
					BorderWidthTop = 1,
					BorderWidthBottom = 1,
					CornerRadiusTopLeft = 11,
					CornerRadiusTopRight = 11,
					CornerRadiusBottomLeft = 11,
					CornerRadiusBottomRight = 11,
					ShadowColor = new Color(0f, 0f, 0f, 0.7f),
					ShadowSize = 4
				};
				badge.AddThemeStyleboxOverride("panel", badgeStyle);

				Label badgeLbl = new Label
				{
					Text = $"{def.Icon} {def.Name.ToUpper()}  ·  LVL {startLevel}",
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					Modulate = themeColor
				};
				badgeLbl.AddThemeFontSizeOverride("font_size", 9);
				badge.AddChild(badgeLbl);

				_badgeContainer.AddChild(badge);
				_buildingContainer.AddChild(nodeAnchor);

				_buildingRegistry[def.Id] = new BuildingNodeData
				{
					Id = def.Id,
					Name = def.Name,
					Col = def.Col,
					Row = def.Row,
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
					BuildingSprite = bSprite,
					BadgeContainer = badge,
					BadgeLabel = badgeLbl
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
				if (kvp.Value.DiamondPoly != null && kvp.Value.BuildingSprite == null)
				{
					kvp.Value.DiamondPoly.Visible = true;
				}
			}

			if (!IsEditLayoutMode)
			{
				ClearTeleportReticle();
				SelectedBuildingId = null;
			}

			GD.Print($"[BASE VIEW] Edit Mode Active: {IsEditLayoutMode}. Grid visibility synced.");
		}

		private void OnSaveLayoutRequested()
		{
			ClearTeleportReticle();
			SelectedBuildingId = null;

			if (_gridLineCanvas != null) _gridLineCanvas.Visible = false;

			foreach (var kvp in _buildingRegistry)
			{
				if (kvp.Value.DiamondOutline != null) kvp.Value.DiamondOutline.Visible = false;
				if (kvp.Value.BuildingSprite != null && kvp.Value.DiamondPoly != null)
				{
					kvp.Value.DiamondPoly.Visible = false;
				}
			}

			GD.Print("[BASE VIEW] Layout Saved! Defense Matrix Deployed.");
		}

		private void SelectBuildingForRelocation(string buildingId)
		{
			if (!_buildingRegistry.TryGetValue(buildingId, out BuildingNodeData data)) return;

			SelectedBuildingId = buildingId;
			StagedGridPos = new Vector2I(data.Col, data.Row);
			_preMoveGridPos = StagedGridPos;

			RenderTeleportReticle(data);
		}

		private void NudgeStagedBuilding(int dCol, int dRow)
		{
			if (string.IsNullOrEmpty(SelectedBuildingId) || !_buildingRegistry.TryGetValue(SelectedBuildingId, out BuildingNodeData data)) return;

			int nextCol = Math.Clamp(StagedGridPos.X + dCol, 0, GRID_COLS - data.W);
			int nextRow = Math.Clamp(StagedGridPos.Y + dRow, 0, GRID_ROWS - data.H);

			StagedGridPos = new Vector2I(nextCol, nextRow);
			UpdateBuildingPositionAndVisuals(data, StagedGridPos);
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

			ClearTeleportReticle();
			SelectedBuildingId = null;
			GD.Print($"[BASE VIEW] Placement confirmed for {data.Name} at [{data.Col}, {data.Row}]");
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
				data.BadgeContainer.Position = center + new Vector2(-75, badgeY);
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

			_teleportReticleContainer.AddChild(CreateNudgeArrow("▲", center + new Vector2(0, -reticleRadius - 20), () => NudgeStagedBuilding(0, -1)));
			_teleportReticleContainer.AddChild(CreateNudgeArrow("▼", center + new Vector2(0, reticleRadius + 20), () => NudgeStagedBuilding(0, 1)));
			_teleportReticleContainer.AddChild(CreateNudgeArrow("◀", center + new Vector2(-reticleRadius - 40, 0), () => NudgeStagedBuilding(-1, 0)));
			_teleportReticleContainer.AddChild(CreateNudgeArrow("▶", center + new Vector2(reticleRadius + 40, 0), () => NudgeStagedBuilding(1, 0)));

			Button confirmBtn = CreateActionBubble("✓", center + new Vector2(reticleRadius + 30, -reticleRadius), isValid ? new Color("#22C55E") : new Color("#64748B"), ConfirmRelocation);
			confirmBtn.Disabled = !isValid;
			_teleportReticleContainer.AddChild(confirmBtn);

			_teleportReticleContainer.AddChild(CreateActionBubble("✕", center + new Vector2(-reticleRadius - 30, -reticleRadius), new Color("#EF4444"), CancelRelocation));
		}

		private Button CreateNudgeArrow(string symbol, Vector2 pos, Action onPress)
		{
			Button btn = new Button
			{
				Text = symbol,
				CustomMinimumSize = new Vector2(36, 36),
				Position = pos - new Vector2(18, 18),
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			btn.AddThemeFontSizeOverride("font_size", 14);

			StyleBoxFlat s = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.08f, 0.14f, 0.95f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 18,
				CornerRadiusTopRight = 18,
				CornerRadiusBottomLeft = 18,
				CornerRadiusBottomRight = 18
			};
			btn.AddThemeStyleboxOverride("normal", s);
			btn.Pressed += onPress;
			return btn;
		}

		private Button CreateActionBubble(string symbol, Vector2 pos, Color bgCol, Action onPress)
		{
			Button btn = new Button
			{
				Text = symbol,
				CustomMinimumSize = new Vector2(40, 40),
				Position = pos - new Vector2(20, 20),
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			btn.AddThemeFontSizeOverride("font_size", 16);

			StyleBoxFlat s = new StyleBoxFlat
			{
				BgColor = bgCol,
				BorderColor = Colors.White,
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 20,
				CornerRadiusTopRight = 20,
				CornerRadiusBottomLeft = 20,
				CornerRadiusBottomRight = 20
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
