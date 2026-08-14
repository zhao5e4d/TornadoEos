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
/// <param name="ChineseName">Simplified Chinese name shown by the Chinese-first desktop UI.</param>
/// <param name="EnglishName">English name kept for protocol logs and international compatibility.</param>
/// <param name="NativeName">The language name written in its own script.</param>
public sealed record CameraLanguage(int Id, string IsoCode, string ChineseName, string EnglishName, string NativeName)
{
    public override string ToString() => $"{ChineseName}（{NativeName}）";
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
        new(0,  "en",    "英语",       "English",              "English"),
        new(1,  "de",    "德语",       "German",               "Deutsch"),
        new(2,  "fr",    "法语",       "French",               "Français"),
        new(3,  "nl",    "荷兰语",     "Dutch",                "Nederlands"),
        new(4,  "da",    "丹麦语",     "Danish",               "Dansk"),
        new(5,  "pt",    "葡萄牙语",   "Portuguese",           "Português"),
        new(6,  "fi",    "芬兰语",     "Finnish",              "Suomi"),
        new(7,  "it",    "意大利语",   "Italian",              "Italiano"),
        new(8,  "no",    "挪威语",     "Norwegian",            "Norsk"),
        new(9,  "sv",    "瑞典语",     "Swedish",              "Svenska"),
        new(10, "es",    "西班牙语",   "Spanish",              "Español"),
        new(11, "el",    "希腊语",     "Greek",                "Ελληνικά"),
        new(12, "ru",    "俄语",       "Russian",              "Русский"),
        new(13, "pl",    "波兰语",     "Polish",               "Polski"),
        new(14, "cs",    "捷克语",     "Czech",                "Čeština"),
        new(15, "hu",    "匈牙利语",   "Hungarian",            "Magyar"),
        new(16, "ro",    "罗马尼亚语", "Romanian",            "Română"),
        new(17, "uk",    "乌克兰语",   "Ukrainian",            "Українська"),
        new(18, "tr",    "土耳其语",   "Turkish",              "Türkçe"),
        new(19, "ar",    "阿拉伯语",   "Arabic",               "العربية"),
        new(20, "th",    "泰语",       "Thai",                 "ไทย"),
        new(21, "zh-CN", "简体中文",   "Simplified Chinese",   "简体中文"),
        new(22, "zh-TW", "繁体中文",   "Traditional Chinese",  "繁體中文"),
        new(23, "ko",    "韩语",       "Korean",               "한국어"),
        new(24, "ms",    "马来语",     "Malay",                "Bahasa Melayu"),
        new(25, "id",    "印度尼西亚语", "Indonesian",        "Bahasa Indonesia"),
        new(26, "vi",    "越南语",     "Vietnamese",           "Tiếng Việt"),
        new(27, "hi",    "印地语",     "Hindi",                "हिन्दी"),
        new(28, "ja",    "日语",       "Japanese",             "日本語"),
    });

    /// <summary>Placeholder used when the current menu language cannot be read.</summary>
    public static readonly CameraLanguage Unknown = new(-1, "?", "未知", "Unknown", "—");

    public static CameraLanguage? FindByIso(string isoCode) =>
        All.FirstOrDefault(l => string.Equals(l.IsoCode, isoCode, System.StringComparison.OrdinalIgnoreCase));

    public static CameraLanguage? FindById(int id) =>
        All.FirstOrDefault(l => l.Id == id);

    /// <summary>The languages visible when the regional language lock is active.</summary>
    public static IReadOnlyList<CameraLanguage> LockedSet =>
        All.Where(l => LockedSetIsoCodes.Contains(l.IsoCode)).ToList();
}
