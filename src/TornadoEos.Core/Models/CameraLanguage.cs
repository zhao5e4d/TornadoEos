using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TornadoEos.Core.Models;

/// <summary>
/// Represents a single menu (UI) language supported by Canon EOS firmware.
/// </summary>
/// <param name="Id">
/// Firmware language identifier. These values are illustrative placeholders for the
/// simulated backend. On real hardware the identifiers come from the camera's
/// service-mode language table and are model specific.
/// </param>
/// <param name="IsoCode">BCP-47 / ISO style code used for sorting and display.</param>
/// <param name="EnglishName">English name of the language.</param>
/// <param name="NativeName">The language name written in its own script.</param>
public sealed record CameraLanguage(int Id, string IsoCode, string EnglishName, string NativeName)
{
    public override string ToString() => $"{EnglishName} ({NativeName})";
}

/// <summary>
/// Catalog of menu languages commonly found in Canon EOS bodies (such as the EOS R50).
/// </summary>
public static class CameraLanguages
{
    /// <summary>
    /// The two languages that remain available when the regional "language lock"
    /// is enabled on grey-market / Japan-region bodies.
    /// </summary>
    public static readonly ReadOnlyCollection<string> LockedSetIsoCodes =
        new(new[] { "en", "ja" });

    /// <summary>Full set of menu languages the catalog knows about.</summary>
    public static readonly ReadOnlyCollection<CameraLanguage> All = new(new List<CameraLanguage>
    {
        new(0,  "en",    "English",              "English"),
        new(1,  "de",    "German",               "Deutsch"),
        new(2,  "fr",    "French",               "Français"),
        new(3,  "nl",    "Dutch",                "Nederlands"),
        new(4,  "da",    "Danish",               "Dansk"),
        new(5,  "pt",    "Portuguese",           "Português"),
        new(6,  "fi",    "Finnish",              "Suomi"),
        new(7,  "it",    "Italian",              "Italiano"),
        new(8,  "no",    "Norwegian",            "Norsk"),
        new(9,  "sv",    "Swedish",              "Svenska"),
        new(10, "es",    "Spanish",              "Español"),
        new(11, "el",    "Greek",                "Ελληνικά"),
        new(12, "ru",    "Russian",              "Русский"),
        new(13, "pl",    "Polish",               "Polski"),
        new(14, "cs",    "Czech",                "Čeština"),
        new(15, "hu",    "Hungarian",            "Magyar"),
        new(16, "ro",    "Romanian",             "Română"),
        new(17, "uk",    "Ukrainian",            "Українська"),
        new(18, "tr",    "Turkish",              "Türkçe"),
        new(19, "ar",    "Arabic",               "العربية"),
        new(20, "th",    "Thai",                 "ไทย"),
        new(21, "zh-CN", "Simplified Chinese",   "简体中文"),
        new(22, "zh-TW", "Traditional Chinese",  "繁體中文"),
        new(23, "ko",    "Korean",               "한국어"),
        new(24, "ms",    "Malay",                "Bahasa Melayu"),
        new(25, "id",    "Indonesian",           "Bahasa Indonesia"),
        new(26, "vi",    "Vietnamese",           "Tiếng Việt"),
        new(27, "hi",    "Hindi",                "हिन्दी"),
        new(28, "ja",    "Japanese",             "日本語"),
    });

    /// <summary>Placeholder used when the current menu language cannot be read.</summary>
    public static readonly CameraLanguage Unknown = new(-1, "?", "Unknown", "—");

    public static CameraLanguage? FindByIso(string isoCode) =>
        All.FirstOrDefault(l => string.Equals(l.IsoCode, isoCode, System.StringComparison.OrdinalIgnoreCase));

    public static CameraLanguage? FindById(int id) =>
        All.FirstOrDefault(l => l.Id == id);

    /// <summary>The languages visible when the regional language lock is active.</summary>
    public static IReadOnlyList<CameraLanguage> LockedSet =>
        All.Where(l => LockedSetIsoCodes.Contains(l.IsoCode)).ToList();
}
