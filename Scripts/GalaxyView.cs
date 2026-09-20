using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: TIER 3 - 1,000 MOONS DEEP SPACE GALAXY MAP (v3.0 Production)
	/// - 8 Concentric Orbital Rings holding 1,000 Moons (M-1 to M-1000).
	/// - 2 Giant Alien World Boss Moons (Dread Citadel stationary & Aether-Reaper 6-month orbit).
	/// - Dynamically instantiates BaseHUD on CanvasLayer (Layer 100) so Top Resources & Bottom Nav stay anchored.
	/// - Full camera panning (touchpad, mouse drag, WASD) and mouse wheel zooming.
	/// </summary>
	public partial class GalaxyView : Node2D
	{
		public static GalaxyView Instance { get; private set; }

		public int CurrentMoon = 100;
		public int MaxRadarRange = 500; // 500 moons default scan

		// Camera
		private Camera2D _camera;
		private Vector2 _targetCameraPos = Vector2.Zero;
		private float _targetZoom = 0.55f;
		private bool _isDragging = false;
		private Vector2 _dragStartMousePos;
		private Vector2 _dragStartCameraPos;

		// Layers & Containers
		private Node2D _galaxyContainer;
		private CanvasLayer _hudLayer;
		private BaseHUD _hudInstance;

		public struct GalaxyMoonNode
		{
			public int MoonNumber;
			public Vector2 Position;
			public bool InRange;
			public bool IsCurrent;
		}

		private List<GalaxyMoonNode> _moonNodes = new List<GalaxyMoonNode>();

		public override void _Ready()
		{
			Instance = this;

			// 1. Camera Initialization
			_camera = GetNodeOrNull<Camera2D>("Camera2D");
			if (_camera == null)
			{
				_camera = new Camera2D { Name = "Camera2D" };
				AddChild(_camera);
			}

			_camera.Position = Vector2.Zero;
			_targetCameraPos = Vector2.Zero;
			_camera.Zoom = new Vector2(_targetZoom, _targetZoom);
			_camera.MakeCurrent();

			// 2. Deep Space Backdrop
			ColorRect spaceBackdrop = new ColorRect
			{
				Name = "SpaceBackdrop",
				Color = new Color(0.01f, 0.02f, 0.03f, 1.0f),
				Position = new Vector2(-4000, -4000),
				Size = new Vector2(8000, 8000),
				ZIndex = -10
			};
			AddChild(spaceBackdrop);

			// 3. Build Galaxy Map Container
			_galaxyContainer = new Node2D { Name = "GalaxyContainer", ZIndex = 0 };
			AddChild(_galaxyContainer);

			BuildGalacticCore();
			BuildOrbitalRingsAnd1000Moons();
			BuildAlienWorldBosses();

			// 4. Anchor BaseHUD on CanvasLayer (Layer 100) - Persistent Top Bar & Bottom Nav
			_hudLayer = new CanvasLayer { Name = "HUDLayer", Layer = 100 };
			AddChild(_hudLayer);

			_hudInstance = new BaseHUD();
			_hudLayer.AddChild(_hudInstance);

			GD.Print("[GALAXY VIEW] Initialized 1,000 Moons Orbital Map. HUD Layer locked.");
		}

		public override void _Process(double delta)
		{
			float dt = (float)delta;

			if (_camera != null)
			{
				_camera.Position = _camera.Position.Lerp(_targetCameraPos, dt * 14.0f);
				_camera.Zoom = _camera.Zoom.Lerp(new Vector2(_targetZoom, _targetZoom), dt * 14.0f);
			}

			// Smooth Keyboard Navigation (WASD / Arrows)
			Vector2 panDir = Vector2.Zero;
			if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) panDir.Y -= 1.0f;
			if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) panDir.Y += 1.0f;
			if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) panDir.X -= 1.0f;
			if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) panDir.X += 1.0f;

			if (panDir != Vector2.Zero)
			{
				float speed = 1200.0f / _targetZoom;
				_targetCameraPos += panDir.Normalized() * speed * dt;
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
						_targetZoom = Mathf.Clamp(_targetZoom * 1.15f, 0.25f, 2.2f);
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.WheelDown)
					{
						_targetZoom = Mathf.Clamp(_targetZoom * 0.85f, 0.25f, 2.2f);
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						if (mb.Position.Y > 65 && mb.Position.Y < GetViewportRect().Size.Y - 65)
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

			if (@event is InputEventMouseMotion mm && _isDragging)
			{
				Vector2 delta = (mm.Position - _dragStartMousePos) / _camera.Zoom.X;
				_targetCameraPos = _dragStartCameraPos - delta;
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventScreenDrag sd)
			{
				Vector2 delta = sd.Relative / _camera.Zoom.X;
				_targetCameraPos -= delta;
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		private void BuildGalacticCore()
		{
			// Radiant Galactic Spiral Core
			Polygon2D coreDisc = new Polygon2D
			{
				Color = new Color(0.95f, 0.98f, 1f, 0.85f)
			};
			int points = 32;
			Vector2[] poly = new Vector2[points];
			for (int i = 0; i < points; i++)
			{
				float a = (i / (float)points) * Mathf.Tau;
				poly[i] = new Vector2(Mathf.Cos(a) * 45, Mathf.Sin(a) * 45);
			}
			coreDisc.Polygon = poly;
			_galaxyContainer.AddChild(coreDisc);

			Label coreLbl = new Label
			{
				Text = "GALACTIC CORE\n[M-000]",
				HorizontalAlignment = HorizontalAlignment.Center,
				Position = new Vector2(-60, -18),
				Size = new Vector2(120, 36),
				Modulate = new Color("#00F0FF")
			};
			coreLbl.AddThemeFontSizeOverride("font_size", 9);
			_galaxyContainer.AddChild(coreLbl);
		}

		private void BuildOrbitalRingsAnd1000Moons()
		{
			var rings = new[]
			{
				new { Radius = 180f,  Count = 10  },
				new { Radius = 320f,  Count = 30  },
				new { Radius = 480f,  Count = 60  },
				new { Radius = 660f,  Count = 100 },
				new { Radius = 860f,  Count = 150 },
				new { Radius = 1080f, Count = 200 },
				new { Radius = 1320f, Count = 220 },
				new { Radius = 1580f, Count = 230 }
			};

			int moonSeq = 1;
			int circlePoints = 64;

			for (int ringIdx = 0; ringIdx < rings.Length; ringIdx++)
			{
				var r = rings[ringIdx];

				// Draw Dashed Orbit Ring
				Line2D orbitLine = new Line2D
				{
					Width = 1.2f,
					DefaultColor = new Color(0f, 0.94f, 1f, 0.18f),
					Antialiased = true
				};
				for (int i = 0; i < circlePoints; i++)
				{
					float a = (i / (float)circlePoints) * Mathf.Tau;
					orbitLine.AddPoint(new Vector2(Mathf.Cos(a) * r.Radius, Mathf.Sin(a) * r.Radius * 0.65f));
				}
				orbitLine.AddPoint(orbitLine.GetPointPosition(0));
				_galaxyContainer.AddChild(orbitLine);

				// Populate Moons along this ring
				float angleStep = Mathf.Tau / r.Count;
				for (int i = 0; i < r.Count; i++)
				{
					if (moonSeq > 1000) break;

					float angle = i * angleStep + (ringIdx * 0.42f);
					Vector2 pos = new Vector2(Mathf.Cos(angle) * r.Radius, Mathf.Sin(angle) * r.Radius * 0.65f);

					int diff = Math.Abs(moonSeq - CurrentMoon);
					int dist = Math.Min(diff, 1000 - diff);
					bool inRange = dist <= MaxRadarRange;
					bool isCurrent = (moonSeq == CurrentMoon);

					Button moonBtn = new Button
					{
						Text = isCurrent ? $"🌕 M-{moonSeq}" : $"M-{moonSeq}",
						CustomMinimumSize = new Vector2(52, 28),
						Position = pos - new Vector2(26, 14),
						MouseFilter = Control.MouseFilterEnum.Stop
					};
					moonBtn.AddThemeFontSizeOverride("font_size", 8);
					moonBtn.Modulate = isCurrent ? new Color("#00F0FF") : inRange ? new Color("#22C55E") : new Color("#64748B");

					StyleBoxFlat mStyle = new StyleBoxFlat
					{
						BgColor = new Color(0.02f, 0.04f, 0.06f, 0.9f),
						BorderColor = isCurrent ? new Color("#00F0FF") : inRange ? new Color("#22C55E") : new Color(0.2f, 0.3f, 0.4f, 0.4f),
						BorderWidthLeft = 1,
						BorderWidthRight = 1,
						BorderWidthTop = 1,
						BorderWidthBottom = 1,
						CornerRadiusTopLeft = 4,
						CornerRadiusTopRight = 4,
						CornerRadiusBottomLeft = 4,
						CornerRadiusBottomRight = 4
					};
					moonBtn.AddThemeStyleboxOverride("normal", mStyle);

					int capturedMoon = moonSeq;
					moonBtn.Pressed += () => OnMoonSelected(capturedMoon);

					_galaxyContainer.AddChild(moonBtn);
					moonSeq++;
				}
			}
		}

		private void BuildAlienWorldBosses()
		{
			float bossRadius = 1880.0f;
			float citadelAngle = -2.35f; // Top-left quadrant

			// 1. Dread Citadel (Stationary Alien Flagship)
			Vector2 citadelPos = new Vector2(Mathf.Cos(citadelAngle) * bossRadius, Mathf.Sin(citadelAngle) * bossRadius * 0.65f);
			Button citadelBtn = new Button
			{
				Text = "🛸 DREAD CITADEL\n[3-WEEK SIEGE]",
				CustomMinimumSize = new Vector2(140, 52),
				Position = citadelPos - new Vector2(70, 26),
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			citadelBtn.AddThemeFontSizeOverride("font_size", 9);
			citadelBtn.Modulate = new Color("#9333EA");
			citadelBtn.Pressed += () => GD.Print("[GALAXY VIEW] Dread Citadel Target Selected.");
			_galaxyContainer.AddChild(citadelBtn);

			// 2. The Aether-Reaper (6-Month Orbital Harvester)
			float reaperAngle = citadelAngle + 0.35f;
			Vector2 reaperPos = new Vector2(Mathf.Cos(reaperAngle) * bossRadius, Mathf.Sin(reaperAngle) * bossRadius * 0.65f);
			Button reaperBtn = new Button
			{
				Text = "💀 THE AETHER-REAPER\n[6-MONTH FUSION]",
				CustomMinimumSize = new Vector2(150, 52),
				Position = reaperPos - new Vector2(75, 26),
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			reaperBtn.AddThemeFontSizeOverride("font_size", 9);
			reaperBtn.Modulate = new Color("#FF0055");
			reaperBtn.Pressed += () => GD.Print("[GALAXY VIEW] The Aether-Reaper Target Selected.");
			_galaxyContainer.AddChild(reaperBtn);
		}

		private void OnMoonSelected(int moonNumber)
		{
			GD.Print($"[GALAXY VIEW] Warping to Moon #{moonNumber} Lunar Sphere.");
			// Switch scene to Tier 2: MoonView
			GetTree().ChangeSceneToFile("res://Scenes/MoonView.tscn");
		}
	}
}
