namespace POSApp.Security;

public static class LocalReturnUrl
{
    public static string GetSafePath(string? value) =>
        !string.IsNullOrEmpty(value) && value[0] == '/'
        && (value.Length == 1 || value[1] is not ('/' or '\\'))
        && !value.Any(char.IsControl) && !value.Contains('\\') ? value : "/";
}
