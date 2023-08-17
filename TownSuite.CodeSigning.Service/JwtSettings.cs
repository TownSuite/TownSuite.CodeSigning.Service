namespace TownSuite.CodeSigning.Service;

public class JwtSettings
{
    public string ValidAudience { get; init; }
    public string ValidIssuer { get; init; }
    public string Secret { get; init; }
    public string PolicyName { get; init;}
}