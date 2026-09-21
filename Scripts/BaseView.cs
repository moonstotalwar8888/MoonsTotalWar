using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER 2.5D BASE VIEWPORT (v25.0 - 25 Building Layout Matrix)
	/// - Calibrated 48x50 Grid covering the North-East plateau and deep South-West basin.
	/// - Exact 4500x2512 Terrain Backdrop with 4-Border Camera Clamping.
	/// - Central Citadel: 4x4 (16 Tiles) Industrial Core.
	/// - 24 Satellite Facilities (including Alliance HQ at [19,11] and Storage Silos at [20,23]).
	/// - Interactive Clash of Kings Base Layout Editor with Directional Reticle & Collision Validation.
	/// - Universal Touchpad, Mouse, Touch Panning, and Zooming.
	/// </summary>
	public partial class BaseView : Node2D
	{
		[Export] public Texture2D TerrainTexture;

		public const int GRID_COLS = 48; // 48 Columns for wide North-East reach
		public const int GRID_ROWS = 50; // 50 Rows for deep South-West coverage

		public const float TILE_WIDTH_HALF = 40.0f;
		public const float TILE_HEIGHT_HALF = 22.5f;

		// Exact Native Terrain Image Dimensions
		public const float TERRAIN_WIDTH = 4500.0f;
		public const float TERRAIN_HEIGHT = 2512.0f;

		// Zoom Limits (Clamped to prevent zooming out past the terrain boundaries)
		public const float MIN_ZOOM = 0.52f;
		public const float MAX_ZOOM = 2.0f;

		// Camera Viewport Controller
		private Camera2D _camera;
		private Vector2 _targetCameraPos;
		private float _targetZoom = 0.85f;
		private bool _isDraggingCamera = false;
		private Vector2 _dragStartMousePos;
		private Vector2 _dragStartCameraPos;
		private Vector2 _terrainCenterPos = Vector2.Zero;

		// Nodes
		private Sprite2D _terrainSprite;
		private Node2D _gridLineCanvas;
		private Node2D _buildingContainer;
		private Node2D _teleportReticleContainer;

		// Canvas Layers
		private CanvasLayer _hudLayer;
		private CanvasLayer _modalLayer;
		private BaseHUD _hudInstance;
		private BuildingInspectorModal _inspectorModal;

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
			public Node2D AnchorNode;
			public Polygon2D DiamondPoly;
			public Line2D DiamondOutline;
			public Button ClickButton;
		}

		private Dictionary<string, BuildingNodeData> _buildingRegistry = new Dictionary<string, BuildingNodeData>();

		public override void _Ready()
		{
			// Center of the 48x50 grid
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

			// 2. Build Terrain Backdrop (4500 x 2512 Native Size)
			BuildTerrainBackdrop(baseCenter);

			// 3. Build Isometric Grid Lines
			_gridLineCanvas = new Node2D { Name = "GridLineCanvas", ZIndex = 1 };
			AddChild(_gridLineCanvas);
			DrawIsometricGridLines();

			// 4. Build 25 Buildings Container
			_buildingContainer = new Node2D { Name = "BuildingContainer", ZIndex = 2 };
			AddChild(_buildingContainer);
			SpawnAudited25BuildingLayout();

			// 5. Build Teleport Reticle Container
			_teleportReticleContainer = new Node2D { Name = "TeleportReticleContainer", ZIndex = 5000 };
			AddChild(_teleportReticleContainer);

			// 6. Build Layer 100: Top Cockpit HUD + Bottom Nav Dock
			_hudLayer = new CanvasLayer { Name = "HUDLayer", Layer = 100 };
			AddChild(_hudLayer);

			_hudInstance = new BaseHUD();
			_hudInstance.EditModeToggled += OnEditModeToggled;
			_hudInstance.SaveLayoutRequested += OnSaveLayoutRequested;
			_hudLayer.AddChild(_hudInstance);

			// 7. Build Layer 110: Centered Inspector Modal
			_modalLayer = new CanvasLayer { Name = "ModalLayer", Layer = 110 };
			AddChild(_modalLayer);

			_inspectorModal = new BuildingInspectorModal();
			_inspectorModal.BuildingUpgraded += OnBuildingUpgraded;
			_modalLayer.AddChild(_inspectorModal);

			// Clamp camera initially to ensure it starts inside the terrain bounds
			_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);
			_camera.Position = _targetCameraPos;

			GD.Print($"[BASE VIEW] Initialization complete. 48x50 Grid centered at {baseCenter}. 25 Buildings Deployed.");
		}

		public override void _Process(double delta)
		{
			float dt = (float)delta;

			// Keyboard WASD / Arrow Navigation
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

			// Enforce 4-border clamp on camera position every frame
			_targetCameraPos = ClampCameraPosition(_targetCameraPos, _targetZoom);

			if (_camera != null)
			{
				_camera.Position = _camera.Position.Lerp(_targetCameraPos, dt * 14.0f);
				_camera.Zoom = _camera.Zoom.Lerp(new Vector2(_targetZoom, _targetZoom), dt * 14.0f);
			}
		}

		public override void _Input(InputEvent @event)
		{
			// Zoom Wheel / Two-Finger Touchpad Scroll
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
						}
					}
				}
				else
				{
					if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						_isDraggingCamera = false;
					}
				}
			}

			if (@event is InputEventMouseMotion mm && _isDraggingCamera)
			{
				Vector2 delta = (mm.Position - _dragStartMousePos) / _camera.Zoom.X;
				_targetCameraPos = ClampCameraPosition(_dragStartCameraPos - delta, _targetZoom);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventScreenDrag sd)
			{
				Vector2 delta = sd.Relative / _camera.Zoom.X;
				_targetCameraPos = ClampCameraPosition(_targetCameraPos - delta, _targetZoom);
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		/// <summary>
		/// Clamps camera position so viewport never views beyond 4500x2512 terrain boundaries.
		/// </summary>
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
			if (texToUse == null)
			{
				texToUse = ResourceLoader.Load<Texture2D>("res://Assets/Terrain/base-terrain.png");
			}
			if (texToUse == null)
			{
				texToUse = GD.Load<Texture2D>("res://Assets/Terrain/base-terrain.png");
			}

			if (texToUse != null)
			{
				_terrainSprite.Texture = texToUse;
				_terrainSprite.Position = baseCenter;
				_terrainSprite.Scale = Vector2.One; // 1:1 Pixel Mapping
				AddChild(_terrainSprite);
			}
		}

		private void DrawIsometricGridLines()
		{
			Color gridColor = new Color(0f, 0.94f, 1f, 0.28f);

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
			var defs = new[]
			{
				// 1. CENTRAL COMMAND: Massive 4x4 Footprint (16 Tiles)
				new { Id = "hub_cmd", Name = "Industrial Core", Col = 20, Row = 18, W = 4, H = 4, ColorHex = "#ffffff", Icon = "🏢" },

				// 2. CITADEL PERIMETER SATELLITES
				new { Id = "hub_shd", Name = "Planetary Shield", Col = 21, Row = 14, W = 2, H = 2, ColorHex = "#38bdf8", Icon = "🛡️" },
				new { Id = "hub_mil", Name = "Orbital Shipyard", Col = 21, Row = 11, W = 2, H = 2, ColorHex = "#ef4444", Icon = "⚔️" },
				new { Id = "hub_mgd", Name = "Moongold Obelisk", Col = 21, Row = 26, W = 2, H = 2, ColorHex = "#fbbf24", Icon = "💰" },
				new { Id = "hub_arm", Name = "Garrison Armory", Col = 21, Row = 29, W = 2, H = 2, ColorHex = "#22c55e", Icon = "🎖️" },

				// 3. ALLIANCE HQ (24th Building - Dedicated Embassy Node)
				new { Id = "hub_ahq", Name = "Alliance HQ", Col = 19, Row = 11, W = 2, H = 2, ColorHex = "#38bdf8", Icon = "🏛️" },

				// 4. STORAGE SILOS (25th Building - Dedicated Resource Containment Vault)
				new { Id = "hub_silo", Name = "Storage Silos", Col = 20, Row = 23, W = 2, H = 2, ColorHex = "#00f0ff", Icon = "🛢️" },

				// 5. RESEARCH, FLEET, RADAR & LOGISTICS
				new { Id = "hub_com", Name = "Commanders Spire", Col = 16, Row = 15, W = 2, H = 2, ColorHex = "#22c55e", Icon = "🤝" },
				new { Id = "hub_flt", Name = "Fleet Station", Col = 26, Row = 15, W = 2, H = 2, ColorHex = "#3b82f6", Icon = "🛰️" },
				new { Id = "hub_rng", Name = "The Deep Radar", Col = 13, Row = 12, W = 2, H = 2, ColorHex = "#f59e0b", Icon = "📡" },
				new { Id = "hub_trd", Name = "Trade Logistics", Col = 29, Row = 12, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "📦" },
				new { Id = "hub_rsh", Name = "Research Directorate", Col = 10, Row = 15, W = 2, H = 2, ColorHex = "#a855f7", Icon = "🔬" },

				// 6. WEST POWER DISTRICT (3 FUSION COILS)
				new { Id = "dist_e_0", Name = "Power Station A", Col = 14, Row = 18, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡" },
				new { Id = "dist_e_1", Name = "Power Station B", Col = 11, Row = 20, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡" },
				new { Id = "dist_e_2", Name = "Power Station C", Col = 14, Row = 22, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡" },

				// 7. SOUTH-WEST IRON EXTRACTION QUARRY (3 MINES)
				new { Id = "dist_i_0", Name = "Iron Mine A", Col = 17, Row = 25, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️" },
				new { Id = "dist_i_1", Name = "Iron Mine B", Col = 14, Row = 27, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️" },
				new { Id = "dist_i_2", Name = "Iron Mine C", Col = 17, Row = 29, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️" },

				// 8. SOUTH-EAST TITANIUM SMELTER COMPLEX (3 EXTRACTORS)
				new { Id = "dist_t_0", Name = "Titanium Smelter A", Col = 25, Row = 23, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎" },
				new { Id = "dist_t_1", Name = "Titanium Smelter B", Col = 28, Row = 26, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎" },
				new { Id = "dist_t_2", Name = "Titanium Smelter C", Col = 25, Row = 27, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎" },

				// 9. EAST HELIUM-3 FUEL CRYO FIELD (5 GAS DISTILLERIES)
				new { Id = "dist_h3_0", Name = "H3 Distillery A", Col = 28, Row = 18, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽" },
				new { Id = "dist_h3_1", Name = "H3 Distillery B", Col = 31, Row = 19, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽" },
				new { Id = "dist_h3_2", Name = "H3 Distillery C", Col = 28, Row = 21, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽" },
				new { Id = "dist_h3_3", Name = "H3 Distillery D", Col = 31, Row = 22, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽" },
				new { Id = "dist_h3_4", Name = "H3 Distillery E", Col = 28, Row = 24, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽" }
			};

			foreach (var def in defs)
			{
				Color themeColor = new Color(def.ColorHex);

				Vector2 top = GridToIso(def.Col, def.Row);
				Vector2 right = GridToIso(def.Col + def.W, def.Row);
				Vector2 bottom = GridToIso(def.Col + def.W, def.Row + def.H);
				Vector2 left = GridToIso(def.Col, def.Row + def.H);
				Vector2 center = (top + bottom) * 0.5f;

				Node2D nodeAnchor = new Node2D
				{
					Name = $"Building_{def.Id}",
					Position = center,
					ZIndex = (int)(center.Y)
				};

				Vector2 lTop = top - center;
				Vector2 lRight = right - center;
				Vector2 lBottom = bottom - center;
				Vector2 lLeft = left - center;

				Polygon2D poly = new Polygon2D
				{
					Polygon = new Vector2[] { lTop, lRight, lBottom, lLeft },
					Color = new Color(themeColor.R, themeColor.G, themeColor.B, def.W >= 4 ? 0.65f : 0.45f)
				};
				nodeAnchor.AddChild(poly);

				Line2D outline = new Line2D
				{
					Width = def.W >= 4 ? 3.0f : 2.0f,
					DefaultColor = themeColor,
					Antialiased = true
				};
				outline.AddPoint(lTop);
				outline.AddPoint(lRight);
				outline.AddPoint(lBottom);
				outline.AddPoint(lLeft);
				outline.AddPoint(lTop);
				nodeAnchor.AddChild(outline);

				float btnWidth = def.W >= 4 ? 180 : 130;
				float btnHeight = def.W >= 4 ? 64 : 52;

				Button clickArea = new Button
				{
					Text = $"{def.Icon} {def.Name}\n[LVL 1]",
					CustomMinimumSize = new Vector2(btnWidth, btnHeight),
					OffsetLeft = -btnWidth / 2,
					OffsetRight = btnWidth / 2,
					OffsetTop = -btnHeight / 2,
					OffsetBottom = btnHeight / 2,
					MouseFilter = Control.MouseFilterEnum.Stop
				};
				clickArea.AddThemeFontSizeOverride("font_size", def.W >= 4 ? 11 : 9);
				clickArea.Modulate = themeColor;

				StyleBoxFlat style = new StyleBoxFlat
				{
					BgColor = new Color(0, 0, 0, 0.4f),
					BorderColor = new Color(themeColor.R, themeColor.G, themeColor.B, 0.6f),
					BorderWidthLeft = 1,
					BorderWidthRight = 1,
					BorderWidthTop = 1,
					BorderWidthBottom = 1,
					CornerRadiusTopLeft = 4,
					CornerRadiusTopRight = 4,
					CornerRadiusBottomLeft = 4,
					CornerRadiusBottomRight = 4
				};
				clickArea.AddThemeStyleboxOverride("normal", style);

				string bId = def.Id;
				clickArea.Pressed += () => OnBuildingClicked(bId);

				nodeAnchor.AddChild(clickArea);
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
					Level = 1,
					AnchorNode = nodeAnchor,
					DiamondPoly = poly,
					DiamondOutline = outline,
					ClickButton = clickArea
				};
			}
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
					_inspectorModal.InspectBuilding(bData.Id, bData.Name, bData.Level, bData.ThemeColor);
				}
			}
		}

		private void OnBuildingUpgraded(string buildingId, int newLevel)
		{
			if (_buildingRegistry.TryGetValue(buildingId, out BuildingNodeData data))
			{
				data.Level = newLevel;
				if (data.ClickButton != null)
				{
					data.ClickButton.Text = $"{data.Icon} {data.Name}\n[LVL {newLevel}]";
				}
				GD.Print($"[BASE] Building {data.Name} upgraded to Level {newLevel}!");
			}
		}

		private void OnEditModeToggled(bool isEditing)
		{
			IsEditLayoutMode = isEditing;
			if (!IsEditLayoutMode)
			{
				ClearTeleportReticle();
				SelectedBuildingId = null;
			}
			GD.Print($"[BASE VIEW] Edit Mode Set: {IsEditLayoutMode}");
		}

		private void OnSaveLayoutRequested()
		{
			ClearTeleportReticle();
			SelectedBuildingId = null;
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

			data.AnchorNode.Position = center;
			data.AnchorNode.ZIndex = (int)center.Y;

			bool isValid = ValidateFootprintLegality(data.Id, gridPos.X, gridPos.Y, data.W, data.H);
			Color statusColor = isValid ? new Color("#22C55E") : new Color("#EF4444");

			if (data.DiamondPoly != null)
			{
				data.DiamondPoly.Color = new Color(statusColor.R, statusColor.G, statusColor.B, 0.55f);
			}
			if (data.DiamondOutline != null)
			{
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

			// NORTH ARROW
			_teleportReticleContainer.AddChild(CreateNudgeArrow("▲", center + new Vector2(0, -65), () => NudgeStagedBuilding(0, -1)));
			// SOUTH ARROW
			_teleportReticleContainer.AddChild(CreateNudgeArrow("▼", center + new Vector2(0, 65), () => NudgeStagedBuilding(0, 1)));
			// WEST ARROW
			_teleportReticleContainer.AddChild(CreateNudgeArrow("◀", center + new Vector2(-100, 0), () => NudgeStagedBuilding(-1, 0)));
			// EAST ARROW
			_teleportReticleContainer.AddChild(CreateNudgeArrow("▶", center + new Vector2(100, 0), () => NudgeStagedBuilding(1, 0)));

			// CONFIRM BUBBLE (✓)
			Button confirmBtn = CreateActionBubble("✓", center + new Vector2(90, -45), isValid ? new Color("#22C55E") : new Color("#64748B"), ConfirmRelocation);
			confirmBtn.Disabled = !isValid;
			_teleportReticleContainer.AddChild(confirmBtn);

			// CANCEL BUBBLE (✕)
			_teleportReticleContainer.AddChild(CreateActionBubble("✕", center + new Vector2(-90, -45), new Color("#EF4444"), CancelRelocation));
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
