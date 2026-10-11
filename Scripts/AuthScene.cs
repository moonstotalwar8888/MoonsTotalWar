using Godot;
using System;
using MoonsTotalWar.Engine;

namespace MoonsTotalWar.UI
{
	/// <summary>
	/// MOONS TOTAL WAR: CENTRAL COMMAND AUTHENTICATION PORTAL
	/// Handles Master Login, Registration, and Character Genesis Callsigns with Supabase C# Client.
	/// </summary>
	public partial class AuthScene : Control
	{
		private PanelContainer _centerPanel;
		private PanelContainer _modeBannerPanel;
		private Label _modeBannerLabel;
		private Label _userLbl;
		private Label _passLbl;
		private LineEdit _usernameInput;
		private LineEdit _passwordInput;
		private LineEdit _callsignInput;
		
		private Button _loginTabBtn;
		private Button _registerTabBtn;
		private Button _submitBtn;
		private Button _genesisBtn;
		private Button _backBtn;
		private CheckBox _termsCheckBox;

		private VBoxContainer _authFormBox;
		private VBoxContainer _genesisFormBox;
		private Label _statusLabel;

		private bool _isRegisterMode = false;
		private SupabaseService _supabase;

		public override void _Ready()
		{
			_supabase = GetNodeOrNull<SupabaseService>("/root/SupabaseService");
			if (_supabase == null)
			{
				_supabase = new SupabaseService();
				AddChild(_supabase);
			}

			BuildAuthUI();
			MusicManager.PlayTrack(this, MusicManager.TRACK_LOGIN);
		}

		private void BuildAuthUI()
		{
			// Fullscreen background anchor
			AnchorsPreset = (int)LayoutPreset.FullRect;

			// Dark Glass Center Panel
			_centerPanel = new PanelContainer();
			_centerPanel.CustomMinimumSize = new Vector2(540, 560);
			_centerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			AddChild(_centerPanel);

			VBoxContainer contentBox = new VBoxContainer();
			contentBox.AddThemeConstantOverride("separation", 14);
			
			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 28);
			margin.AddThemeConstantOverride("margin_right", 28);
			margin.AddThemeConstantOverride("margin_top", 24);
			margin.AddThemeConstantOverride("margin_bottom", 24);
			margin.AddChild(contentBox);
			_centerPanel.AddChild(margin);

			// Title
			Label titleLabel = new Label();
			titleLabel.Text = "M O O N S : T O T A L   W A R";
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			titleLabel.AddThemeFontSizeOverride("font_size", 26);
			titleLabel.Modulate = new Color(0.96f, 0.24f, 0.24f);
			contentBox.AddChild(titleLabel);

			_modeBannerPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 38) };
			_modeBannerLabel = new Label
			{
				Text = "🔓 EXISTING COMMANDER LOGIN",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			_modeBannerLabel.AddThemeFontSizeOverride("font_size", 14);
			_modeBannerPanel.AddChild(_modeBannerLabel);
			contentBox.AddChild(_modeBannerPanel);

			// Tab switch buttons (LOGIN / REGISTER)
			HBoxContainer tabRow = new HBoxContainer();
			tabRow.Alignment = BoxContainer.AlignmentMode.Center;
			tabRow.AddThemeConstantOverride("separation", 12);
			
			_loginTabBtn = new Button();
			_loginTabBtn.Text = "🔓 LOGIN";
			_loginTabBtn.CustomMinimumSize = new Vector2(220, 46);
			_loginTabBtn.AddThemeFontSizeOverride("font_size", 15);
			_loginTabBtn.Pressed += () => SetAuthMode(false);
			tabRow.AddChild(_loginTabBtn);

			_registerTabBtn = new Button();
			_registerTabBtn.Text = "🎖️ REGISTER NEW";
			_registerTabBtn.CustomMinimumSize = new Vector2(220, 46);
			_registerTabBtn.AddThemeFontSizeOverride("font_size", 15);
			_registerTabBtn.Pressed += () => SetAuthMode(true);
			tabRow.AddChild(_registerTabBtn);

			contentBox.AddChild(tabRow);

			// AUTH FORM BOX
			_authFormBox = new VBoxContainer();
			_authFormBox.AddThemeConstantOverride("separation", 12);
			contentBox.AddChild(_authFormBox);

			_userLbl = new Label();
			_userLbl.Text = "MASTER USERNAME:";
			_userLbl.AddThemeFontSizeOverride("font_size", 14);
			_authFormBox.AddChild(_userLbl);

			_usernameInput = new LineEdit();
			_usernameInput.PlaceholderText = "Enter callsign or username...";
			_usernameInput.CustomMinimumSize = new Vector2(0, 48);
			_usernameInput.AddThemeFontSizeOverride("font_size", 16);
			_authFormBox.AddChild(_usernameInput);

			_passLbl = new Label();
			_passLbl.Text = "SECURITY KEYCODE:";
			_passLbl.AddThemeFontSizeOverride("font_size", 14);
			_authFormBox.AddChild(_passLbl);

			_passwordInput = new LineEdit();
			_passwordInput.PlaceholderText = "Enter security key...";
			_passwordInput.Secret = true;
			_passwordInput.CustomMinimumSize = new Vector2(0, 48);
			_passwordInput.AddThemeFontSizeOverride("font_size", 16);
			_passwordInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_passwordInput.TextSubmitted += (_) => OnSubmitAuth();
			_usernameInput.TextSubmitted += (_) => _passwordInput.GrabFocus();

			HBoxContainer passRow = new HBoxContainer();
			passRow.AddThemeConstantOverride("separation", 8);
			passRow.AddChild(_passwordInput);

			Button showPassBtn = new Button
			{
				Text = "👁",
				CustomMinimumSize = new Vector2(48, 48),
				TooltipText = "Show / Hide Security Keycode"
			};
			showPassBtn.AddThemeFontSizeOverride("font_size", 16);
			showPassBtn.Pressed += () =>
			{
				_passwordInput.Secret = !_passwordInput.Secret;
				showPassBtn.Modulate = _passwordInput.Secret ? Colors.White : new Color("#00F0FF");
			};
			passRow.AddChild(showPassBtn);
			_authFormBox.AddChild(passRow);

			// Pre-fill saved commander credentials if available on device
			if (_supabase != null)
			{
				(string savedUser, string savedPass) = _supabase.LoadSavedLoginCredentials();
				if (!string.IsNullOrEmpty(savedUser)) _usernameInput.Text = savedUser;
				if (!string.IsNullOrEmpty(savedPass)) _passwordInput.Text = savedPass;
			}

			_termsCheckBox = new CheckBox
			{
				Text = "I accept the Terms, Privacy & © 2026 Copyright Agreement",
				CustomMinimumSize = new Vector2(0, 36),
				Visible = false
			};
			_termsCheckBox.AddThemeFontSizeOverride("font_size", 13);
			_termsCheckBox.Modulate = new Color("#FDE68A");
			_authFormBox.AddChild(_termsCheckBox);

			_submitBtn = new Button();
			_submitBtn.Text = "AUTHORIZE ACCESS";
			_submitBtn.CustomMinimumSize = new Vector2(0, 54);
			_submitBtn.AddThemeFontSizeOverride("font_size", 16);
			_submitBtn.Pressed += OnSubmitAuth;
			_authFormBox.AddChild(_submitBtn);

			// GENESIS FORM BOX (Hidden by default)
			_genesisFormBox = new VBoxContainer();
			_genesisFormBox.AddThemeConstantOverride("separation", 10);
			_genesisFormBox.Visible = false;
			contentBox.AddChild(_genesisFormBox);

			Label genLbl = new Label();
			genLbl.Text = "INITIALIZE COMMANDER CALLSIGN:";
			genLbl.AddThemeFontSizeOverride("font_size", 10);
			_genesisFormBox.AddChild(genLbl);

			_callsignInput = new LineEdit();
			_callsignInput.PlaceholderText = "Commander Callsign...";
			_callsignInput.CustomMinimumSize = new Vector2(0, 42);
			_genesisFormBox.AddChild(_callsignInput);

			_genesisBtn = new Button();
			_genesisBtn.Text = "INITIALIZE COLONY";
			_genesisBtn.CustomMinimumSize = new Vector2(0, 48);
			_genesisBtn.Modulate = new Color(1f, 0.75f, 0f);
			_genesisBtn.Pressed += OnGenesisSubmit;
			_genesisFormBox.AddChild(_genesisBtn);

			// Status output label
			_statusLabel = new Label();
			_statusLabel.Text = "Awaiting portal credentials...";
			_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_statusLabel.AddThemeFontSizeOverride("font_size", 10);
			_statusLabel.Modulate = new Color(0.6f, 0.7f, 0.8f);
			contentBox.AddChild(_statusLabel);

			SetAuthMode(false);
		}

		private void SetAuthMode(bool isRegister)
		{
			_isRegisterMode = isRegister;

			Color primaryBorder = isRegister ? new Color("#F59E0B") : new Color("#00F0FF");
			Color cardBg = isRegister ? new Color(0.11f, 0.05f, 0.04f, 0.96f) : new Color(0.03f, 0.07f, 0.12f, 0.96f);

			if (_centerPanel != null)
			{
				StyleBoxFlat panelStyle = new StyleBoxFlat { BgColor = cardBg, BorderColor = primaryBorder };
				panelStyle.SetCornerRadiusAll(12);
				panelStyle.SetBorderWidthAll(3);
				_centerPanel.AddThemeStyleboxOverride("panel", panelStyle);
			}

			if (_modeBannerPanel != null)
			{
				StyleBoxFlat bStyle = new StyleBoxFlat
				{
					BgColor = isRegister ? new Color(0.35f, 0.12f, 0.04f, 0.95f) : new Color(0.02f, 0.22f, 0.34f, 0.95f),
					BorderColor = primaryBorder
				};
				bStyle.SetCornerRadiusAll(6);
				bStyle.SetBorderWidthAll(1);
				_modeBannerPanel.AddThemeStyleboxOverride("panel", bStyle);
			}

			if (_modeBannerLabel != null)
			{
				_modeBannerLabel.Text = isRegister ? "🎖️ NEW COMMANDER REGISTRATION" : "🔓 EXISTING COMMANDER LOGIN";
				_modeBannerLabel.Modulate = isRegister ? new Color("#FBBF24") : new Color("#00F0FF");
			}

			if (_userLbl != null)
			{
				_userLbl.Text = isRegister ? "CREATE NEW COMMANDER USERNAME:" : "EXISTING COMMANDER USERNAME:";
				_userLbl.Modulate = isRegister ? new Color("#FBBF24") : new Color("#38BDF8");
			}

			if (_passLbl != null)
			{
				_passLbl.Text = isRegister ? "CREATE NEW SECURITY KEYCODE:" : "ENTER SECURITY KEYCODE:";
				_passLbl.Modulate = isRegister ? new Color("#FBBF24") : new Color("#38BDF8");
			}

			if (_termsCheckBox != null)
			{
				_termsCheckBox.Visible = isRegister;
			}

			_submitBtn.Text = isRegister ? "🎖️ ENLIST & CREATE ACCOUNT" : "🔓 AUTHORIZE COMMAND LOGIN";
			_submitBtn.Modulate = isRegister ? new Color("#FBBF24") : new Color("#00F0FF");
			_statusLabel.Text = isRegister ? "Registration Mode: Tick the Terms & Copyright box to enlist." : "Login Mode: Enter your existing Commander credentials.";
			_statusLabel.Modulate = isRegister ? new Color("#FDE68A") : new Color("#7DD3FC");
		}

		private async void OnSubmitAuth()
		{
			string user = _usernameInput.Text.Trim();
			string pass = _passwordInput.Text.Trim();

			if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
			{
				_statusLabel.Text = "ERROR: Username and password required.";
				_statusLabel.Modulate = Colors.Red;
				return;
			}

			if (_isRegisterMode && (_termsCheckBox == null || !_termsCheckBox.ButtonPressed))
			{
				_statusLabel.Text = "REQUIRED: Please tick the Terms & Copyright box before registering.";
				_statusLabel.Modulate = new Color("#EF4444");
				return;
			}

			_statusLabel.Text = "Transmitting authentication request...";
			_statusLabel.Modulate = Colors.Yellow;

			if (_isRegisterMode)
			{
				var (success, error) = await _supabase.SignUpAsync(user, pass);
				if (success)
				{
					_statusLabel.Text = "ACCOUNT CREATED & AUTHORIZED! Entering Sector...";
					_statusLabel.Modulate = Colors.Green;
					TransitionToServerSelect();
				}
				else
				{
					_statusLabel.Text = $"REGISTER ERROR: {error}";
					_statusLabel.Modulate = Colors.Red;
				}
			}
			else
			{
				var (success, error) = await _supabase.SignInAsync(user, pass);
				if (success)
				{
					_statusLabel.Text = "ACCESS GRANTED! Transitioning...";
					_statusLabel.Modulate = Colors.Green;
					TransitionToServerSelect();
				}
				else
				{
					_statusLabel.Text = $"LOGIN DENIED: {error}";
					_statusLabel.Modulate = Colors.Red;
				}
			}
		}

		private void TransitionToServerSelect()
		{
			string[] candidates = new[]
			{
				"res://Scenes/ServerSelect.tscn",
				"res://ServerSelect.tscn",
				"res://Scripts/ServerSelect.tscn",
				"res://Scenes/BaseView.tscn",
				"res://base_view.tscn",
				"res://Scripts/base_view.tscn"
			};
			foreach (string path in candidates)
			{
				if (ResourceLoader.Exists(path))
				{
					GetTree().ChangeSceneToFile(path);
					return;
				}
			}
			// Fallback: instantiate ServerSelectScene directly if no .tscn wrapper exists
			var serverSelectNode = new ServerSelectScene();
			GetTree().Root.AddChild(serverSelectNode);
			QueueFree();
		}

		private void OnGenesisSubmit()
		{
			// Character Genesis logic
		}
	}
}
