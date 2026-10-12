namespace Mono.Api.Services;

/// <summary>
/// Evaluates the HTTP If-None-Match condition using the weak comparison required for GET.
/// </summary>
public static class MonoIfNoneMatch
{
    public static bool Matches(string? headerValue, string currentEntityTag)
    {
        if (string.IsNullOrEmpty(headerValue)
            || !TryParseTag(currentEntityTag, out _, out var currentOpaqueTag))
            return false;

        var index = 0;
        SkipOptionalWhitespace(headerValue, ref index);
        if (index < headerValue.Length && headerValue[index] == '*')
        {
            index++;
            SkipOptionalWhitespace(headerValue, ref index);
            return index == headerValue.Length;
        }

        while (index < headerValue.Length)
        {
            SkipOptionalWhitespace(headerValue, ref index);
            if (!TryParseTag(headerValue, ref index, out _, out var candidateOpaqueTag))
                return false;

            if (string.Equals(candidateOpaqueTag, currentOpaqueTag, StringComparison.Ordinal))
                return true;

            SkipOptionalWhitespace(headerValue, ref index);
            if (index == headerValue.Length)
                return false;
            if (headerValue[index] != ',')
                return false;

            index++;
            SkipOptionalWhitespace(headerValue, ref index);
            if (index == headerValue.Length)
                return false;
        }

        return false;
    }

    private static bool TryParseTag(
        string value,
        out bool isWeak,
        out string opaqueTag)
    {
        var index = 0;
        return TryParseTag(value, ref index, out isWeak, out opaqueTag)
            && index == value.Length;
    }

    private static bool TryParseTag(
        string value,
        ref int index,
        out bool isWeak,
        out string opaqueTag)
    {
        isWeak = false;
        opaqueTag = string.Empty;

        if (index + 1 < value.Length
            && value[index] == 'W'
            && value[index + 1] == '/')
        {
            isWeak = true;
            index += 2;
        }

        if (index >= value.Length || value[index] != '"')
            return false;

        var start = ++index;
        while (index < value.Length && value[index] != '"')
        {
            var character = value[index];
            if (character < 0x21 || character == 0x7f || character > 0xff)
                return false;
            index++;
        }

        if (index == value.Length)
            return false;

        opaqueTag = value[start..index];
        index++;
        return true;
    }

    private static void SkipOptionalWhitespace(string value, ref int index)
    {
        while (index < value.Length && value[index] is ' ' or '\t')
            index++;
    }
}
