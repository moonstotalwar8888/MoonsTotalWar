using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: SUPABASE HTTP & REST DATA SERVICE (v19.0 Boost Sync Edition)
	/// Connected to Production Supabase Database: kaldvuinxvqtompmagxj
	/// Uses dynamic Godot HttpRequest nodes to guarantee infinite concurrent saves.
	/// Serializes and syncs the Premium Build Queue, Active Builds, and Active Production Boosts.
	/// </summary>
	public partial class SupabaseService : Node
	{
		public static SupabaseService Instance { get; private set; }

		[Export] public string SupabaseUrl = "https://kaldvuinxvqtompmagxj.supabase.co";
		[Export] public string SupabaseAnonKey = "sb_publishable_zR5AJCm-hlbUs7URghPZ5g_Jv9MsJGe";

		public string AccessToken { get; private set; } = "";
		public string UserId { get; private set; } = "";

		public class ColonySaveData
		{
			public string commander_name { get; set; }
			public double res_e { get; set; }
			public double res_i { get; set; }
			public double res_t { get; set; }
			public double res_h3 { get; set; }
			public long mgold { get; set; }
			public DateTimeOffset last_sync_time { get; set; }
			public Dictionary<string, int> building_levels { get; set; }
			public GameMath.BuildQueueItem active_build { get; set; }
			public List<GameMath.BuildQueueItem> build_queue { get; set; }
			
			// New Boost Sync Property
			public Dictionary<string, GameMath.BoostData> active_boosts { get; set; }
		}

		public override void _Ready()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}

			Instance = this;
			GD.Print("[SUPABASE SERVICE] Initialized with Live Database Target: " + SupabaseUrl);
			CallDeferred(nameof(MakeImmortal));
		}

		private void MakeImmortal()
		{
			Node root = GetTree().Root;
			if (GetParent() != root)
			{
				GetParent().RemoveChild(this);
				root.AddChild(this);
				GD.Print("[SUPABASE SERVICE] Reparented to Root. Persistence achieved.");
			}
		}

		private async Task<(long responseCode, string responseText)> SendGodotRequestAsync(string url, string[] headers, HttpClient.Method method, string body = "")
		{
			HttpRequest reqNode = new HttpRequest();
			AddChild(reqNode);
			reqNode.Timeout = 10.0f;

			Error err = reqNode.Request(url, headers, method, body);
			if (err != Error.Ok)
			{
				reqNode.QueueFree();
				throw new Exception($"Godot HttpRequest failed to start: {err}");
			}

			var result = await ToSignal(reqNode, HttpRequest.SignalName.RequestCompleted);
			
			long responseCode = (long)result[1];
			byte[] responseBytes = (byte[])result[3];
			string responseText = "";

			if (responseBytes != null && responseBytes.Length > 0)
			{
				responseText = Encoding.UTF8.GetString(responseBytes);
			}

			reqNode.QueueFree();
			return (responseCode, responseText);
		}

		public async Task<(bool Success, string Error)> SignUpAsync(string username, string password)
		{
			string cleanUser = username.Trim().ToLower();
			if (string.IsNullOrEmpty(cleanUser) || string.IsNullOrEmpty(password))
				return (false, "Username and Security Keycode required.");

			string internalEmail = $"{cleanUser}@moons.global";
			string url = $"{SupabaseUrl}/auth/v1/signup";

			var payload = new { email = internalEmail, password = password };
			string jsonBody = JsonSerializer.Serialize(payload);

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}"
			};

			GD.Print($"[SUPABASE] Sending SignUp request for {cleanUser}...");

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Post, jsonBody);

				if (code == 200 || code == 201)
				{
					return await SignInAsync(username, password);
				}
				else
				{
					if (responseText.Contains("already registered") || responseText.Contains("already_exists"))
					{
						GD.Print("[SUPABASE] User already registered in cloud DB. Redirecting to SignIn...");
						return await SignInAsync(username, password);
					}
					return (false, $"Auth Error ({code}): {responseText}");
				}
			}
			catch (Exception ex)
			{
				return (false, $"Network Error: {ex.Message}");
			}
		}

		public async Task<(bool Success, string Error)> SignInAsync(string username, string password)
		{
			string cleanUser = username.Trim().ToLower();
			string internalEmail = $"{cleanUser}@moons.global";
			string url = $"{SupabaseUrl}/auth/v1/token?grant_type=password";

			var payload = new { email = internalEmail, password = password };
			string jsonBody = JsonSerializer.Serialize(payload);

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}"
			};

			GD.Print($"[SUPABASE] Sending SignIn request for {cleanUser}...");

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Post, jsonBody);

				if (code == 200)
				{
					using var doc = JsonDocument.Parse(responseText);
					var root = doc.RootElement;
					if (root.TryGetProperty("access_token", out var tokenProp)) AccessToken = tokenProp.GetString();
					if (root.TryGetProperty("user", out var userProp) && userProp.TryGetProperty("id", out var idProp)) UserId = idProp.GetString();

					GD.Print($"[SUPABASE] Cloud Auth Granted! User ID: {UserId}");
					return (true, "");
				}
				else
				{
					return (false, "Invalid Master Username or Security Keycode.");
				}
			}
			catch (Exception ex)
			{
				return (false, $"Network Error: {ex.Message}");
			}
		}

		public async Task<ColonySaveData> FetchColonyStateAsync()
		{
			if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(AccessToken)) return null;

			string url = $"{SupabaseUrl}/rest/v1/colonies?id=eq.{UserId}&select=*";

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {AccessToken}"
			};

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);

				if (code == 200)
				{
					if (responseText.Trim() == "[]")
					{
						GD.Print("[SUPABASE] Colony row missing! Initiating Self-Healing Genesis...");
						bool created = await CreateInitialColonyRowAsync();
						if (created)
						{
							var retry = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
							responseText = retry.responseText;
						}
						else
						{
							return null;
						}
					}

					var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
					var colonies = JsonSerializer.Deserialize<List<ColonySaveData>>(responseText, options);

					if (colonies != null && colonies.Count > 0)
					{
						GD.Print("[SUPABASE] Colony state successfully fetched from cloud.");
						return colonies[0];
					}
				}
				
				GD.PrintErr($"[SUPABASE] Fetch failed ({code}): {responseText}");
				return null;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] Fetch Network Error: {ex.Message}");
				return null;
			}
		}

		private async Task<bool> CreateInitialColonyRowAsync()
		{
			string url = $"{SupabaseUrl}/rest/v1/colonies";

			var payload = new
			{
				id = UserId,
				commander_name = "COMMANDER ALPHA",
				res_e = 5000,
				res_i = 5000,
				res_t = 5000,
				res_h3 = 2500,
				mgold = 100,
				last_sync_time = DateTimeOffset.UtcNow.ToString("o") 
			};

			string jsonBody = JsonSerializer.Serialize(payload);

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {AccessToken}",
				"Prefer: return=minimal"
			};

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Post, jsonBody);
				
				if (code == 201 || code == 204 || code == 200)
				{
					GD.Print("[SUPABASE] Self-Healing Genesis Complete. Row created.");
					return true;
				}
				else
				{
					GD.PrintErr($"[SUPABASE] Self-Healing failed ({code}): {responseText}");
					return false;
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] Self-Healing Network Error: {ex.Message}");
				return false;
			}
		}

		public async Task SaveColonyStateAsync(
			double resE, double resI, double resT, double resH3, long gGold, 
			Dictionary<string, int> buildingLevels,
			GameMath.BuildQueueItem activeBuild,
			List<GameMath.BuildQueueItem> buildQueue,
			Dictionary<string, GameMath.BoostData> activeBoosts)
		{
			if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(AccessToken)) return;

			string url = $"{SupabaseUrl}/rest/v1/colonies?id=eq.{UserId}";

			var payload = new
			{
				res_e = resE,
				res_i = resI,
				res_t = resT,
				res_h3 = resH3,
				mgold = gGold,
				building_levels = buildingLevels,
				active_build = activeBuild,
				build_queue = buildQueue ?? new List<GameMath.BuildQueueItem>(),
				active_boosts = activeBoosts ?? new Dictionary<string, GameMath.BoostData>(),
				last_sync_time = DateTimeOffset.UtcNow.ToString("o") 
			};

			string jsonBody = JsonSerializer.Serialize(payload);

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {AccessToken}",
				"Prefer: return=minimal"
			};

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Patch, jsonBody);
				
				if (code != 204 && code != 200)
				{
					GD.PrintErr($"[SUPABASE] Save failed ({code}): {responseText}");
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] Save Network Error: {ex.Message}");
			}
		}
	}
}
