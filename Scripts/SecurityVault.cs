using System;
using System.Security.Cryptography;
using System.Text;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: BINARY & NETWORK SECURITY VAULT (v25.0)
	/// - Eliminates plain-text API URLs, keys, and secrets from compiled .dll/.exe string tables.
	/// - Signs outgoing API/Edge Function requests with HMAC-SHA256 + anti-replay timestamps.
	/// - Encrypts local session & journal files on disk so credentials/tokens are never stored in plain text.
	/// </summary>
	public static class SecurityVault
	{
		private static readonly byte[] _rollingCipherKey = new byte[]
		{
			0x4D, 0x54, 0x57, 0x5F, 0x56, 0x32, 0x35, 0x5F, 0x4B, 0x45, 0x59, 0x39
		};

		// Obfuscated Cloud Endpoint (no plain-text URL in binary strings)
		private static readonly byte[] _obfuscatedEndpointBytes = new byte[]
		{
			0x25, 0x31, 0x01, 0x1C, 0x61, 0x5D, 0x7C, 0x07, 0xA8, 0xBD, 0x9F, 0xE6,
			0xF7, 0xFC, 0xD0, 0xCE, 0x3E, 0x65, 0x76, 0x68, 0x70, 0x4D, 0x5F, 0xD3,
			0xB4, 0x9A, 0x95, 0xFE, 0xA4, 0xAC, 0xBE, 0x20, 0x0A, 0x16, 0x7A, 0x19,
			0x4C, 0x0F, 0xB2, 0xA7
		};

		// Obfuscated Publishable Client Key (no plain-text key in binary strings)
		private static readonly byte[] _obfuscatedClientKeyBytes = new byte[]
		{
			0x3E, 0x27, 0x2A, 0x1C, 0x67, 0x05, 0x3F, 0x41, 0xB0, 0xB4, 0x92, 0xE0,
			0xED, 0xEC, 0xE6, 0xDA, 0x14, 0x26, 0x46, 0x56, 0x5C, 0x4D, 0x02, 0xD6,
			0xB9, 0x9F, 0xB8, 0xE7, 0xBD, 0x8A, 0x99, 0x37, 0x03, 0x24, 0x41, 0x5F,
			0x4E, 0x7E, 0x9B, 0xBE, 0xC7, 0xC6, 0x8C, 0xCE, 0xE0, 0xDD
		};

		// Obfuscated Edge Function HMAC Signing Secret
		private static readonly byte[] _obfuscatedHmacSecretBytes = new byte[]
		{
			0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x86, 0x98, 0xB4, 0xC7,
			0xDE, 0xC1, 0xF4, 0xE1, 0x05, 0x4C, 0x40, 0x49, 0x5E, 0x72, 0x6B, 0xE1,
			0x86, 0xB8, 0xAE, 0xC6, 0xCF, 0x8B, 0x94, 0x68, 0x53, 0x40, 0x29
		};

		private const string LOCAL_ENC_PREFIX = "ENCv1:";

		private static string DecodeBytes(byte[] encoded)
		{
			if (encoded == null || encoded.Length == 0) return string.Empty;
			byte[] decoded = new byte[encoded.Length];
			int keyLen = _rollingCipherKey.Length;
			for (int i = 0; i < encoded.Length; i++)
			{
				decoded[i] = (byte)((encoded[i] ^ _rollingCipherKey[i % keyLen] ^ ((i * 17) & 0xFF)) & 0xFF);
			}
			string result = Encoding.UTF8.GetString(decoded);
			Array.Clear(decoded, 0, decoded.Length);
			return result;
		}

		public static string GetEndpointUrl() => DecodeBytes(_obfuscatedEndpointBytes);

		public static string GetPublishableKey() => DecodeBytes(_obfuscatedClientKeyBytes);

		public static string GetEdgeGatewayUrl() => $"{GetEndpointUrl()}/functions/v1/moons-gateway";

		/// <summary>
		/// Computes an HMAC-SHA256 signature over (Timestamp + Method + RelativePath + Body)
		/// so the Supabase Edge Function can verify the request came from the official game client.
		/// </summary>
		public static (string timestampHeader, string signatureHeader) ComputeRequestSignature(string httpMethod, string targetUrl, string body)
		{
			long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			string ts = nowUnix.ToString();
			string secret = DecodeBytes(_obfuscatedHmacSecretBytes);

			string relPath = targetUrl ?? "";
			string baseEndpoint = GetEndpointUrl();
			if (relPath.StartsWith(baseEndpoint, StringComparison.OrdinalIgnoreCase))
			{
				relPath = relPath.Substring(baseEndpoint.Length);
			}

			string canonicalPayload = $"{ts}|{(httpMethod ?? "GET").ToUpperInvariant()}|{relPath}|{body ?? ""}";
			byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
			byte[] msgBytes = Encoding.UTF8.GetBytes(canonicalPayload);

			using var hmac = new HMACSHA256(keyBytes);
			byte[] hashBytes = hmac.ComputeHash(msgBytes);
			string hexSig = Convert.ToHexString(hashBytes).ToLowerInvariant();

			Array.Clear(keyBytes, 0, keyBytes.Length);
			return (ts, hexSig);
		}

		/// <summary>
		/// Encrypts sensitive local session JSON before saving to user://commander_auth_session.json
		/// so plain-text passwords and JWTs are never exposed on disk.
		/// </summary>
		public static string EncryptLocalText(string plainText)
		{
			if (string.IsNullOrEmpty(plainText)) return string.Empty;
			byte[] raw = Encoding.UTF8.GetBytes(plainText);
			byte[] outBytes = new byte[raw.Length];
			int keyLen = _rollingCipherKey.Length;
			for (int i = 0; i < raw.Length; i++)
			{
				outBytes[i] = (byte)((raw[i] ^ _rollingCipherKey[(i + 5) % keyLen] ^ ((i * 31 + 19) & 0xFF)) & 0xFF);
			}
			return LOCAL_ENC_PREFIX + Convert.ToBase64String(outBytes);
		}

		/// <summary>
		/// Decrypts user://commander_auth_session.json (transparently supports legacy plain-text JSON so existing testers don't get logged out).
		/// </summary>
		public static string DecryptLocalText(string storedText)
		{
			if (string.IsNullOrWhiteSpace(storedText)) return string.Empty;
			if (!storedText.StartsWith(LOCAL_ENC_PREFIX, StringComparison.Ordinal))
			{
				// Backwards compatibility for pre-existing plain-text JSON file
				return storedText;
			}
			try
			{
				string b64 = storedText.Substring(LOCAL_ENC_PREFIX.Length).Trim();
				byte[] encBytes = Convert.FromBase64String(b64);
				byte[] raw = new byte[encBytes.Length];
				int keyLen = _rollingCipherKey.Length;
				for (int i = 0; i < encBytes.Length; i++)
				{
					raw[i] = (byte)((encBytes[i] ^ _rollingCipherKey[(i + 5) % keyLen] ^ ((i * 31 + 19) & 0xFF)) & 0xFF);
				}
				return Encoding.UTF8.GetString(raw);
			}
			catch
			{
				return string.Empty;
			}
		}
	}
}
