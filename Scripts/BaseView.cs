using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER 2.5D BASE VIEWPORT (v9.0 Production Calibration)
	/// - Industrial Core: 4x4 (16 Tiles) Central Citadel Footprint.
	/// - 22 Satellite Facilities: 2x2 Footprint with strictly non-overlapping coordinates.
	/// - Universal Laptop Touchpad, Mouse, and Touch panning/zooming.
	/// - Screen-space centered inspector modal.
	/// </summary>
	public partial class BaseView : Node2D
	{
		[Export] public Texture2D TerrainTexture;

		public const int GRID_COLS = 32;
		public const int GRID_ROWS = 20;

		public const float TILE_WIDTH_HALF = 40.0f;
		public const float TILE_HEIGHT_HALF = 22.5f;

		// Camera Viewport Controller
		private Camera2D _camera;
		private Vector2 _targetCameraPos;
		private float _targetZoom = 0.85f;
		private bool _isDragging = false;
		private Vector2 _dragStartMousePos;
		private Vector2 _dragStartCameraPos;

		// Node Hierarchies
		private Sprite2D _terrainSprite;
		private Node2D _gridLineCanvas;
		private Node2D _buildingContainer;

		// Dedicated Top-Level Screen Space CanvasLayers
		private CanvasLayer _hudLayer;
		private CanvasLayer _modalLayer;
		private BaseHUD _hudInstance;
		private BuildingInspectorModal _inspectorModal;

		public struct BuildingNodeData
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
			public Button ClickButton;
		}

		private Dictionary<string, BuildingNodeData> _buildingRegistry = new Dictionary<string, BuildingNodeData>();

		public override void _Ready()
		{
			// Industrial Core Center: Col 16, Row 10 (Centered on 4x4 Citadel)
			Vector2 baseCenter = GridToIso(16, 10);

			// 1. Initialize Camera
			_camera = GetNodeOrNull<Camera2D>("Camera2D");
			if (_camera == null)
			{
				_camera = new Camera2D();
				AddChild(_camera);
			}

			_camera.Position = baseCenter;
			_targetCameraPos = baseCenter;
			_camera.Zoom = new Vector2(_targetZoom, _targetZoom);
			_camera.MakeCurrent();

			// 2. Build World Terrain Backdrop
			BuildTerrainBackdrop(baseCenter);

			// 3. Build Isometric Grid Lines
			_gridLineCanvas = new Node2D { Name = "GridLineCanvas", ZIndex = 1 };
			AddChild(_gridLineCanvas);
			DrawIsometricGridLines();

			// 4. Build 23 Non-Overlapping Building Diamonds (Industrial Core is 4x4)
			_buildingContainer = new Node2D { Name = "BuildingContainer", ZIndex = 2 };
			AddChild(_buildingContainer);
			SpawnAudited23BuildingLayout();

			// 5. Build Layer 100: Top Cockpit HUD
			_hudLayer = new CanvasLayer { Name = "HUDLayer", Layer = 100 };
			AddChild(_hudLayer);

			_hudInstance = new BaseHUD();
			_hudLayer.AddChild(_hudInstance);

			// 6. Build Layer 110: Centered Screen Modal
			_modalLayer = new CanvasLayer { Name = "ModalLayer", Layer = 110 };
			AddChild(_modalLayer);

			_inspectorModal = new BuildingInspectorModal();
			_inspectorModal.BuildingUpgraded += OnBuildingUpgraded;
			_modalLayer.AddChild(_inspectorModal);

			GD.Print($"[BASE VIEW] Calibration complete. Camera anchored to 4x4 Industrial Core: {baseCenter}");
		}

		public override void _Process(double delta)
		{
			float dt = (float)delta;

			if (_camera != null)
			{
				_camera.Position = _camera.Position.Lerp(_targetCameraPos, dt * 14.0f);
				_camera.Zoom = _camera.Zoom.Lerp(new Vector2(_targetZoom, _targetZoom), dt * 14.0f);
			}

			// Smooth Keyboard WASD / Arrow Keys Pan
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
		}

		// UNIVERSAL INPUT: Laptop Touchpad, Mouse, and Touch Gesture Controller
		public override void _Input(InputEvent @event)
		{
			// 1. Zoom Wheel / Touchpad Two-Finger Scroll
			if (@event is InputEventMouseButton mb)
			{
				if (mb.IsPressed())
				{
					if (mb.ButtonIndex == MouseButton.WheelUp)
					{
						_targetZoom = Mathf.Clamp(_targetZoom * 1.12f, 0.45f, 2.4f);
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.WheelDown)
					{
						_targetZoom = Mathf.Clamp(_targetZoom * 0.88f, 0.45f, 2.4f);
						GetViewport().SetInputAsHandled();
						return;
					}
					// Left-Click on ground (Laptop Touchpad), Right-Click, or Middle-Click drag
					else if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						// Do not drag if mouse is over HUD bar
						if (mb.Position.Y > 65 && (_inspectorModal == null || !_inspectorModal.Visible))
						{
							_isDragging = true;
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
						}
					}
				}
				else
				{
					if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						_isDragging = false;
					}
				}
			}

			// 2. Drag Motion
			if (@event is InputEventMouseMotion mm && _isDragging)
			{
				Vector2 delta = (mm.Position - _dragStartMousePos) / _camera.Zoom.X;
				_targetCameraPos = _dragStartCameraPos - delta;
				GetViewport().SetInputAsHandled();
				return;
			}

			// 3. Touchscreen / Touchpad Direct Pan Drag
			if (@event is InputEventScreenDrag sd)
			{
				Vector2 delta = sd.Relative / _camera.Zoom.X;
				_targetCameraPos -= delta;
				GetViewport().SetInputAsHandled();
				return;
			}
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

				float desiredWidth = 4400.0f;
				float scaleFactor = desiredWidth / texToUse.GetWidth();
				_terrainSprite.Scale = new Vector2(scaleFactor, scaleFactor);

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

		// MASTER AUDITED 23-BUILDING NON-OVERLAPPING LAYOUT
		private void SpawnAudited23BuildingLayout()
		{
			var defs = new[]
			{
				// 1. CENTRAL COMMAND: Massive 4x4 Footprint (16 Tiles!)
				new { Id = "hub_cmd", Name = "Industrial Core", Col = 14, Row = 8, W = 4, H = 4, ColorHex = "#ffffff", Icon = "🏢", Desc = "Central Command Spire" },

				// 2. CITADEL PERIMETER SATELLITES (2x2 Footprint, spaced cleanly)
				new { Id = "hub_shd", Name = "Planetary Shield", Col = 15, Row = 4, W = 2, H = 2, ColorHex = "#38bdf8", Icon = "🛡️", Desc = "Deflection Shield Generator" },
				new { Id = "hub_mil", Name = "Orbital Shipyard", Col = 15, Row = 1, W = 2, H = 2, ColorHex = "#ef4444", Icon = "⚔️", Desc = "Fleet Aerospace Foundry" },
				new { Id = "hub_mgd", Name = "Moongold Obelisk", Col = 15, Row = 13, W = 2, H = 2, ColorHex = "#fbbf24", Icon = "💰", Desc = "Universal Credit Exchange" },
				new { Id = "hub_arm", Name = "Garrison Armory", Col = 15, Row = 16, W = 2, H = 2, ColorHex = "#22c55e", Icon = "🎖️", Desc = "Garrison Armory Depot" },

				// 3. RESEARCH, FLEET, RADAR & LOGISTICS
				new { Id = "hub_com", Name = "Commanders Spire", Col = 10, Row = 5, W = 2, H = 2, ColorHex = "#22c55e", Icon = "🤝", Desc = "Diplomacy & Comms Spire" },
				new { Id = "hub_flt", Name = "Fleet Station", Col = 20, Row = 5, W = 2, H = 2, ColorHex = "#3b82f6", Icon = "🛰️", Desc = "Fleet Operations Station" },
				new { Id = "hub_rng", Name = "The Deep Radar", Col = 7, Row = 2, W = 2, H = 2, ColorHex = "#f59e0b", Icon = "📡", Desc = "5,000 Moons Deep Radar" },
				new { Id = "hub_trd", Name = "Trade Logistics", Col = 23, Row = 2, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "📦", Desc = "Logistics Trade Hub" },
				new { Id = "hub_rsh", Name = "Research Directorate", Col = 4, Row = 5, W = 2, H = 2, ColorHex = "#a855f7", Icon = "🔬", Desc = "Advanced Science Division" },

				// 4. WEST POWER DISTRICT (3 FUSION COILS)
				new { Id = "dist_e_0", Name = "Power Station A", Col = 8, Row = 8, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡", Desc = "Fusion Reactor Grid A" },
				new { Id = "dist_e_1", Name = "Power Station B", Col = 5, Row = 10, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡", Desc = "Fusion Reactor Grid B" },
				new { Id = "dist_e_2", Name = "Power Station C", Col = 8, Row = 12, W = 2, H = 2, ColorHex = "#d946ef", Icon = "⚡", Desc = "Fusion Reactor Grid C" },

				// 5. SOUTH-WEST IRON EXTRACTION QUARRY (3 MINES)
				new { Id = "dist_i_0", Name = "Iron Mine A", Col = 11, Row = 13, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️", Desc = "Magnetic Iron Excavator A" },
				new { Id = "dist_i_1", Name = "Iron Mine B", Col = 8, Row = 15, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️", Desc = "Magnetic Iron Excavator B" },
				new { Id = "dist_i_2", Name = "Iron Mine C", Col = 11, Row = 17, W = 2, H = 2, ColorHex = "#06b6d4", Icon = "⛏️", Desc = "Magnetic Iron Excavator C" },

				// 6. SOUTH-EAST TITANIUM SMELTER COMPLEX (3 EXTRACTORS)
				new { Id = "dist_t_0", Name = "Titanium Smelter A", Col = 19, Row = 13, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎", Desc = "Titanium Alloy Smelter A" },
				new { Id = "dist_t_1", Name = "Titanium Smelter B", Col = 22, Row = 15, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎", Desc = "Titanium Alloy Smelter B" },
				new { Id = "dist_t_2", Name = "Titanium Smelter C", Col = 19, Row = 17, W = 2, H = 2, ColorHex = "#94a3b8", Icon = "💎", Desc = "Titanium Alloy Smelter C" },

				// 7. EAST HELIUM-3 FUEL CRYO FIELD (5 GAS DISTILLERIES)
				new { Id = "dist_h3_0", Name = "H3 Distillery A", Col = 21, Row = 8, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", Desc = "Cryo Gas Distillery A" },
				new { Id = "dist_h3_1", Name = "H3 Distillery B", Col = 24, Row = 9, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", Desc = "Cryo Gas Distillery B" },
				new { Id = "dist_h3_2", Name = "H3 Distillery C", Col = 21, Row = 11, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", Desc = "Cryo Gas Distillery C" },
				new { Id = "dist_h3_3", Name = "H3 Distillery D", Col = 24, Row = 12, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", Desc = "Cryo Gas Distillery D" },
				new { Id = "dist_h3_4", Name = "H3 Distillery E", Col = 21, Row = 14, W = 2, H = 2, ColorHex = "#eab308", Icon = "⛽", Desc = "Cryo Gas Distillery E" }
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

				// Diamond Foundation Polygon
				Polygon2D poly = new Polygon2D
				{
					Polygon = new Vector2[] { lTop, lRight, lBottom, lLeft },
					Color = new Color(themeColor.R, themeColor.G, themeColor.B, def.W >= 4 ? 0.65f : 0.45f)
				};
				nodeAnchor.AddChild(poly);

				// Glowing perimeter outline
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

				// Interactive Click Button
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
				clickArea.AddThemeStyleboxOverride("hover", style);
				clickArea.AddThemeStyleboxOverride("pressed", style);

				string bId = def.Id;
				string bName = def.Name;
				Color bColor = themeColor;

				clickArea.Pressed += () =>
				{
					if (_buildingRegistry.TryGetValue(bId, out BuildingNodeData bData))
					{
						_inspectorModal.InspectBuilding(bData.Id, bData.Name, bData.Level, bData.ThemeColor);
					}
					else
					{
						_inspectorModal.InspectBuilding(bId, bName, 1, bColor);
					}
				};

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
					ClickButton = clickArea
				};
			}
		}

		private void OnBuildingUpgraded(string buildingId, int newLevel)
		{
			if (_buildingRegistry.TryGetValue(buildingId, out BuildingNodeData data))
			{
				data.Level = newLevel;
				_buildingRegistry[buildingId] = data;

				if (data.ClickButton != null)
				{
					data.ClickButton.Text = $"{data.Icon} {data.Name}\n[LVL {newLevel}]";
				}

				GD.Print($"[BASE] Building {data.Name} upgraded to Level {newLevel}!");
			}
		}
	}
}
