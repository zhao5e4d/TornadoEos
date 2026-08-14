using System.Threading.Tasks;
using TornadoEos.Core.Backends;
using TornadoEos.Core.Exceptions;
using TornadoEos.Core.Models;
using TornadoEos.Core.Services;
using Xunit;

namespace TornadoEos.Tests;

public class CameraServiceTests
{
    private static CameraService NewService() =>
        new(new SimulatedCameraBackend(SimulatedCameraBackend.TimeProfile.Instant));

    [Fact]
    public async Task Connect_returns_r50_info_and_starts_locked()
    {
        using var service = NewService();

        var info = await service.ConnectAsync();

        Assert.Equal("Canon EOS R50", info.ModelName);
        Assert.Equal(ConnectionState.Connected, service.State);
        Assert.True(service.IsLanguageLockEnabled);
        Assert.Equal("ja", service.CurrentLanguage!.IsoCode);
    }

    [Fact]
    public async Task Available_languages_are_limited_while_locked()
    {
        using var service = NewService();
        await service.ConnectAsync();

        var available = await service.GetAvailableLanguagesAsync();

        Assert.Equal(2, available.Count);
        Assert.Contains(available, l => l.IsoCode == "en");
        Assert.Contains(available, l => l.IsoCode == "ja");
    }

    [Fact]
    public async Task Setting_a_locked_language_does_not_require_unlock()
    {
        using var service = NewService();
        await service.ConnectAsync();

        await service.SetMenuLanguageAsync("en", autoUnlock: false);

        Assert.Equal("en", service.CurrentLanguage!.IsoCode);
        Assert.True(service.IsLanguageLockEnabled);
    }

    [Fact]
    public async Task Setting_hidden_language_auto_unlocks_and_applies()
    {
        using var service = NewService();
        await service.ConnectAsync();

        await service.SetMenuLanguageAsync("zh-CN", autoUnlock: true);

        Assert.Equal("zh-CN", service.CurrentLanguage!.IsoCode);
        Assert.False(service.IsLanguageLockEnabled);
        Assert.Equal(ConnectionState.ServiceMode, service.State);
    }

    [Fact]
    public async Task Setting_hidden_language_without_autounlock_throws()
    {
        using var service = NewService();
        await service.ConnectAsync();

        await Assert.ThrowsAsync<UnsupportedLanguageException>(
            () => service.SetMenuLanguageAsync("ru", autoUnlock: false));
    }

    [Fact]
    public async Task Unknown_iso_code_throws()
    {
        using var service = NewService();
        await service.ConnectAsync();

        await Assert.ThrowsAsync<UnsupportedLanguageException>(
            () => service.SetMenuLanguageAsync("xx"));
    }

    [Fact]
    public async Task Operations_without_connection_throw()
    {
        using var service = NewService();

        await Assert.ThrowsAsync<CameraNotConnectedException>(
            () => service.SetMenuLanguageAsync("en"));
    }

    [Fact]
    public async Task Current_language_changed_event_fires()
    {
        using var service = NewService();
        await service.ConnectAsync();
        CameraLanguage? observed = null;
        service.CurrentLanguageChanged += (_, lang) => observed = lang;

        await service.SetMenuLanguageAsync("fr");

        Assert.NotNull(observed);
        Assert.Equal("fr", observed!.IsoCode);
    }

    [Fact]
    public async Task Disconnect_resets_state()
    {
        using var service = NewService();
        await service.ConnectAsync();

        await service.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, service.State);
        Assert.Null(service.CameraInfo);
    }

    [Fact]
    public void Language_catalog_has_simplified_chinese_display_names()
    {
        var chinese = CameraLanguages.FindByIso("zh-CN");

        Assert.NotNull(chinese);
        Assert.Equal("简体中文", chinese!.ChineseName);
        Assert.Equal("简体中文（简体中文）", chinese.ToString());
    }

    [Fact]
    public async Task Simulated_workflow_emits_chinese_user_messages()
    {
        using var service = NewService();
        var messages = new System.Collections.Generic.List<string>();
        service.LogReceived += (_, entry) => messages.Add(entry.Message);

        await service.ConnectAsync();
        await service.SetMenuLanguageAsync("zh-CN");

        Assert.Contains(messages, message => message.Contains("已连接"));
        Assert.Contains(messages, message => message.Contains("简体中文"));
        Assert.Contains(messages, message => message.Contains("区域语言锁"));
    }
}
