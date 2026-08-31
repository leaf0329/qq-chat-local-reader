using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QQChatLocalReader.Infrastructure.Exporting;

internal sealed partial class BasicMessageRedactor : IDisposable
{
    private readonly byte[] salt = RandomNumberGenerator.GetBytes(32);

    public string Identity(string category, string value)
    {
        var input = Encoding.UTF8.GetBytes($"{category}\0{value}");
        try
        {
            var digest = HMACSHA256.HashData(salt, input);
            try
            {
                return $"{category}-{Convert.ToHexString(digest.AsSpan(0, 5))}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(digest);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public string? VisibleText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var result = WindowsPathRegex().Replace(value, "[本地路径]");
        result = IdentityCardRegex().Replace(result, match => Identity("身份证", match.Value));
        result = PhoneRegex().Replace(result, match => Identity("手机号", match.Value));
        return QqNumberRegex().Replace(result, match => Identity("QQ号", match.Value));
    }

    public static string? LocalPath(string? value) => value is null ? null : "[本地路径]";

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(salt);
        GC.SuppressFinalize(this);
    }

    [GeneratedRegex(@"(?i)(?<![\w])(?:[a-z]:\\|\\\\)[^\r\n<>|?*]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<!\d)\d{17}[\dXx](?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityCardRegex();

    [GeneratedRegex(@"(?<!\d)1[3-9]\d{9}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?<!\d)\d{5,12}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex QqNumberRegex();
}
