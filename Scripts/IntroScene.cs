using Godot;
using System;
using MoonsTotalWar.Engine;

namespace MoonsTotalWar.UI
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER ANIMATED INTRO & PORTAL
	/// Handles animated background, story portal, authentication, and server selection.
	/// </summary>
	public partial class IntroScene : Control
	{
		private TextureRect _bgImage;
		private float _animTime = 0f;

		// UI Containers
		private CenterContainer _storyCenter;
		private CenterContainer _authCenter;
		private CenterContainer _serverCenter;

		// Auth Inputs & Buttons
		private LineEdit _usernameInput;
		private LineEdit _passwordInput;
		private Button _loginTabBtn;
		private Button _registerTabBtn;
		private Button _authSubmitBtn;
		private Label _authStatusLabel;
		private bool _isRegisterMode = false;

		private SupabaseService _supabase;

		public override void _Ready()
		{
			// Force Fullscreen Control Root
			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

			_supabase = GetNodeOrNull<SupabaseService>("/root/SupabaseService");
			if (_supabase == null)
			{
				_supabase = new SupabaseService();
				_supabase.Name = "SupabaseService";
				AddChild(_supabase);
			}

			BuildCinematicBackground();
			BuildStoryIntroUI();
			BuildAuthUI();
			BuildServerSelectUI();

			// Show Story Intro First
			ShowState("STORY");
		}

		public override void _Process(double delta)
		{
			// 60FPS Subtle Pan & Breathing Zoom Loop
			_animTime += (float)delta;
			if (_bgImage != null)
			{
				float panX = MathF.Sin(_animTime * 0.3f) * 20f;
				float panY = MathF.Cos(_animTime * 0.25f) * 12f;
				float zoomScale = 1.05f + MathF.Sin(_animTime * 0.15f) * 0.03f;

				_bgImage.Position = new Vector2(-80f + panX, -50f + panY);
				_bgImage.Scale = new Vector2(zoomScale, zoomScale);
			}
		}

		private void BuildCinematicBackground()
		{
			_bgImage = new TextureRect();
			_bgImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			_bgImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
			_bgImage.CustomMinimumSize = new Vector2(1600, 900);
			_bgImage.Position = new Vector2(-80, -50);

			Texture2D bgTex = GD.Load<Texture2D>("res://Assets/login-bg.jpg");
			if (bgTex != null)
			{
				_bgImage.Texture = bgTex;
			}
			AddChild(_bgImage);

			// Translucent Dark Vignette
			ColorRect darkOverlay = new ColorRect();
			darkOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			darkOverlay.Color = new Color(0.02f, 0.04f, 0.08f, 0.55f);
			AddChild(darkOverlay);
		}

		private void BuildStoryIntroUI()
		{
			_storyCenter = new CenterContainer();
			_storyCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			AddChild(_storyCenter);

			VBoxContainer storyBox = new VBoxContainer();
			storyBox.CustomMinimumSize = new Vector2(480, 0);
			storyBox.AddThemeConstantOverride("separation", 18);
			_storyCenter.AddChild(storyBox);

			// Header Titles
			Label titleLabel = new Label();
			titleLabel.Text = "MOONS";
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			titleLabel.AddThemeFontSizeOverride("font_size", 44);
			titleLabel.Modulate = new Color(0.94f, 0.27f, 0.27f); // Crimson
			storyBox.AddChild(titleLabel);

			Label subTitleLabel = new Label();
			subTitleLabel.Text = "TOTAL WAR";
			subTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			subTitleLabel.AddThemeFontSizeOverride("font_size", 20);
			subTitleLabel.Modulate = Colors.White;
			storyBox.AddChild(subTitleLabel);

			// Description Card
			PanelContainer storyCard = new PanelContainer();
			StyleBoxFlat cardStyle = new StyleBoxFlat();
			cardStyle.BgColor = new Color(0.06f, 0.09f, 0.16f, 0.90f);
			cardStyle.SetCornerRadiusAll(8);
			cardStyle.SetBorderWidthAll(2);
			cardStyle.BorderColor = new Color(0.94f, 0.27f, 0.27f);
			storyCard.AddThemeStyleboxOverride("panel", cardStyle);

			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 20);
			margin.AddThemeConstantOverride("margin_right", 20);
			margin.AddThemeConstantOverride("margin_top", 20);
			margin.AddThemeConstantOverride("margin_bottom", 20);

			Label bodyText = new Label();
			bodyText.Text = "YEAR 2148: Earth is a frozen tomb. Humanity fights across 5,000 Moons for the galaxy's rarest life-blood: Helium-H3 Fuel.\n\nTHE AETHER-REAPER arrives every 6 months to purge the stars. Fortify your spaceports, construct mighty fleets, and claim galactic dominion.";
			bodyText.HorizontalAlignment = HorizontalAlignment.Center;
			bodyText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			bodyText.AddThemeFontSizeOverride("font_size", 13);
			bodyText.Modulate = new Color(0.88f, 0.92f, 0.96f);
			margin.AddChild(bodyText);

			storyCard.AddChild(margin);
			storyBox.AddChild(storyCard);

			// Enter Portal Button
			Button enterPortalBtn = new Button();
			enterPortalBtn.Text = "ENTER WAR PORTAL";
			enterPortalBtn.CustomMinimumSize = new Vector2(0, 54);
			enterPortalBtn.AddThemeFontSizeOverride("font_size", 15);

			StyleBoxFlat btnStyle = new StyleBoxFlat();
			btnStyle.BgColor = new Color(0.94f, 0.27f, 0.27f);
			btnStyle.SetCornerRadiusAll(6);
			enterPortalBtn.AddThemeStyleboxOverride("normal", btnStyle);

			// Connect Button Pressed Event Directly
			enterPortalBtn.Pressed += OnEnterWarPortalPressed;
			storyBox.AddChild(enterPortalBtn);
		}

		private void BuildAuthUI()
		{
			_authCenter = new CenterContainer();
			_authCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_authCenter.Visible = false;
			AddChild(_authCenter);

			VBoxContainer authBox = new VBoxContainer();
			authBox.CustomMinimumSize = new Vector2(440, 0);
			authBox.AddThemeConstantOverride("separation", 15);
			_authCenter.AddChild(authBox);

			PanelContainer authCard = new PanelContainer();
			StyleBoxFlat cardStyle = new StyleBoxFlat();
			cardStyle.BgColor = new Color(0.04f, 0.06f, 0.10f, 0.95f);
			cardStyle.SetCornerRadiusAll(10);
			cardStyle.SetBorderWidthAll(2);
			cardStyle.BorderColor = new Color(0f, 0.94f, 1f, 0.8f);
			authCard.AddThemeStyleboxOverride("panel", cardStyle);

			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 24);
			margin.AddThemeConstantOverride("margin_right", 24);
			margin.AddThemeConstantOverride("margin_top", 24);
			margin.AddThemeConstantOverride("margin_bottom", 24);

			VBoxContainer content = new VBoxContainer();
			content.AddThemeConstantOverride("separation", 12);

			Label header = new Label();
			header.Text = "CENTRAL COMMAND ACCESS PORTAL";
			header.HorizontalAlignment = HorizontalAlignment.Center;
			header.AddThemeFontSizeOverride("font_size", 13);
			header.Modulate = new Color(0f, 0.94f, 1f);
			content.AddChild(header);

			// Login/Register Tab Buttons
			HBoxContainer tabRow = new HBoxContainer();
			tabRow.Alignment = BoxContainer.AlignmentMode.Center;

			_loginTabBtn = new Button();
			_loginTabBtn.Text = "LOGIN";
			_loginTabBtn.CustomMinimumSize = new Vector2(170, 40);
			_loginTabBtn.Pressed += () => SetAuthMode(false);
			tabRow.AddChild(_loginTabBtn);

			_registerTabBtn = new Button();
			_registerTabBtn.Text = "REGISTER";
			_registerTabBtn.CustomMinimumSize = new Vector2(170, 40);
			_registerTabBtn.Pressed += () => SetAuthMode(true);
			tabRow.AddChild(_registerTabBtn);

			content.AddChild(tabRow);

			// Inputs
			Label userLbl = new Label();
			userLbl.Text = "MASTER USERNAME:";
			userLbl.AddThemeFontSizeOverride("font_size", 10);
			content.AddChild(userLbl);

			_usernameInput = new LineEdit();
			_usernameInput.PlaceholderText = "Enter master username...";
			_usernameInput.CustomMinimumSize = new Vector2(0, 42);
			content.AddChild(_usernameInput);

			Label passLbl = new Label();
			passLbl.Text = "SECURITY KEYCODE:";
			passLbl.AddThemeFontSizeOverride("font_size", 10);
			content.AddChild(passLbl);

			_passwordInput = new LineEdit();
			_passwordInput.PlaceholderText = "Enter security keycode...";
			_passwordInput.Secret = true;
			_passwordInput.CustomMinimumSize = new Vector2(0, 42);
			content.AddChild(_passwordInput);

			_authSubmitBtn = new Button();
			_authSubmitBtn.Text = "AUTHORIZE ACCESS";
			_authSubmitBtn.CustomMinimumSize = new Vector2(0, 48);
			_authSubmitBtn.Pressed += OnSubmitAuth;
			content.AddChild(_authSubmitBtn);

			_authStatusLabel = new Label();
			_authStatusLabel.Text = "Awaiting portal credentials...";
			_authStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_authStatusLabel.AddThemeFontSizeOverride("font_size", 10);
			_authStatusLabel.Modulate = new Color(0.6f, 0.7f, 0.8f);
			content.AddChild(_authStatusLabel);

			// Back to Story
			Button backBtn = new Button();
			backBtn.Text = "← BACK TO INTRO";
			backBtn.Flat = true;
			backBtn.Pressed += () => ShowState("STORY");
			content.AddChild(backBtn);

			margin.AddChild(content);
			authCard.AddChild(margin);
			authBox.AddChild(authCard);

			SetAuthMode(false);
		}

		private void BuildServerSelectUI()
		{
			_serverCenter = new CenterContainer();
			_serverCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_serverCenter.Visible = false;
			AddChild(_serverCenter);

			VBoxContainer serverBox = new VBoxContainer();
			serverBox.CustomMinimumSize = new Vector2(520, 0);
			serverBox.AddThemeConstantOverride("separation", 15);
			_serverCenter.AddChild(serverBox);

			PanelContainer card = new PanelContainer();
			StyleBoxFlat cardStyle = new StyleBoxFlat();
			cardStyle.BgColor = new Color(0.04f, 0.06f, 0.10f, 0.95f);
			cardStyle.SetCornerRadiusAll(10);
			cardStyle.SetBorderWidthAll(2);
			cardStyle.BorderColor = new Color(0f, 0.94f, 1f, 0.8f);
			card.AddThemeStyleboxOverride("panel", cardStyle);

			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 20);
			margin.AddThemeConstantOverride("margin_right", 20);
			margin.AddThemeConstantOverride("margin_top", 20);
			margin.AddThemeConstantOverride("margin_bottom", 20);

			VBoxContainer content = new VBoxContainer();
			content.AddThemeConstantOverride("separation", 12);

			Label header = new Label();
			header.Text = "OPERATIONAL UNIVERSES";
			header.HorizontalAlignment = HorizontalAlignment.Center;
			header.AddThemeFontSizeOverride("font_size", 16);
			header.Modulate = new Color(0f, 0.94f, 1f);
			content.AddChild(header);

			foreach (var srv in GameData.Servers)
			{
				PanelContainer srvCard = new PanelContainer();
				StyleBoxFlat sStyle = new StyleBoxFlat();
				sStyle.BgColor = new Color(0.08f, 0.12f, 0.18f, 0.90f);
				sStyle.SetCornerRadiusAll(6);
				srvCard.AddThemeStyleboxOverride("panel", sStyle);

				HBoxContainer row = new HBoxContainer();

				VBoxContainer info = new VBoxContainer();
				info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				Label n = new Label();
				n.Text = srv.Name;
				info.AddChild(n);

				Label d = new Label();
				d.Text = srv.Desc;
				d.AddThemeFontSizeOverride("font_size", 9);
				d.Modulate = new Color(0.6f, 0.7f, 0.8f);
				info.AddChild(d);

				row.AddChild(info);

				Button enterBtn = new Button();
				enterBtn.Text = srv.Status == "ONLINE" ? $"ENTER ({srv.Speed}X)" : "LOCKED";
				enterBtn.Disabled = srv.Status != "ONLINE";
				enterBtn.Pressed += OnLaunchBaseView;
				row.AddChild(enterBtn);

				MarginContainer pad = new MarginContainer();
				pad.AddThemeConstantOverride("margin_left", 10);
				pad.AddThemeConstantOverride("margin_right", 10);
				pad.AddThemeConstantOverride("margin_top", 8);
				pad.AddThemeConstantOverride("margin_bottom", 8);
				pad.AddChild(row);

				srvCard.AddChild(pad);
				content.AddChild(srvCard);
			}

			margin.AddChild(content);
			card.AddChild(margin);
			serverBox.AddChild(card);
		}

		private void OnEnterWarPortalPressed()
		{
			GD.Print("[INTRO] Enter War Portal Pressed -> Transitioning to Auth Portal.");
			ShowState("AUTH");
		}

		private void ShowState(string state)
		{
			_storyCenter.Visible = state == "STORY";
			_authCenter.Visible = state == "AUTH";
			_serverCenter.Visible = state == "SERVER";
		}

		private void SetAuthMode(bool isRegister)
		{
			_isRegisterMode = isRegister;
			_authSubmitBtn.Text = isRegister ? "CREATE ACCOUNT" : "AUTHORIZE ACCESS";
			_authStatusLabel.Text = isRegister ? "Register new commander account." : "Enter credentials to log in.";
		}

		private async void OnSubmitAuth()
		{
			string user = _usernameInput.Text.Trim();
			string pass = _passwordInput.Text.Trim();

			if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
			{
				_authStatusLabel.Text = "ERROR: Master Username and Keycode required.";
				_authStatusLabel.Modulate = Colors.Red;
				return;
			}

			_authStatusLabel.Text = "Transmitting authentication request...";
			_authStatusLabel.Modulate = Colors.Yellow;

			if (_isRegisterMode)
			{
				var (success, error) = await _supabase.SignUpAsync(user, pass);
				if (success)
				{
					_authStatusLabel.Text = "ACCOUNT CREATED! Opening Universes...";
					_authStatusLabel.Modulate = Colors.Green;
					ShowState("SERVER");
				}
				else
				{
					_authStatusLabel.Text = $"REGISTER ERROR: {error}";
					_authStatusLabel.Modulate = Colors.Red;
				}
			}
			else
			{
				var (success, error) = await _supabase.SignInAsync(user, pass);
				if (success)
				{
					_authStatusLabel.Text = "ACCESS GRANTED! Opening Universes...";
					_authStatusLabel.Modulate = Colors.Green;
					ShowState("SERVER");
				}
				else
				{
					_authStatusLabel.Text = $"LOGIN DENIED: {error}";
					_authStatusLabel.Modulate = Colors.Red;
				}
			}
		}

		private void OnLaunchBaseView()
		{
			GD.Print("[INTRO] Launching BaseView Scene...");
			GetTree().ChangeSceneToFile("res://Scenes/base_view.tscn");
		}
	}
}
