using System.Security.Cryptography;
using System.Text;

namespace SolBotSentinel;

/// <summary>The bearer check for the alert routes. No token configured means open.</summary>
public static class ApiAuth
{
	private const string Prefix = "Bearer ";

	public static bool Allowed(string authorizationHeader, string expectedToken)
	{
		if (expectedToken == "") return true;
		if (!authorizationHeader.StartsWith(Prefix, StringComparison.Ordinal)) return false;

		var provided = Encoding.UTF8.GetBytes(authorizationHeader[Prefix.Length..]);
		var expected = Encoding.UTF8.GetBytes(expectedToken);
		return CryptographicOperations.FixedTimeEquals(provided, expected);
	}
}
