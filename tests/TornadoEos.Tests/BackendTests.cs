using System;
using System.Threading.Tasks;
using TornadoEos.Core.Backends;
using TornadoEos.Core.Exceptions;
using TornadoEos.Core.Models;
using Xunit;

namespace TornadoEos.Tests;

public class BackendTests
{
    private static SimulatedCameraBackend NewBackend() =>
        new(SimulatedCameraBackend.TimeProfile.Instant);

    [Fact]
    public void Simulated_backend_reports_its_capabilities()
    {
        using var backend = NewBackend();

        Assert.True(backend.SupportsMenuLanguageWrite);
        Assert.True(backend.IsSimulation);
    }

    [Fact]
    public async Task Writing_language_without_service_mode_throws()
    {
        using var backend = NewBackend();
        await backend.ConnectAsync();

        await Assert.ThrowsAsync<ServiceModeRequiredException>(
            () => backend.SetMenuLanguageAsync(CameraLanguages.FindByIso("en")!));
    }

    [Fact]
    public async Task Service_mode_enables_writes()
    {
        using var backend = NewBackend();
        await backend.ConnectAsync();
        await backend.EnterServiceModeAsync();

        Assert.True(backend.IsInServiceMode);
        await backend.SetMenuLanguageAsync(CameraLanguages.FindByIso("en")!);
        var current = await backend.GetCurrentLanguageAsync();
        Assert.Equal("en", current.IsoCode);
    }

    [Fact]
    public async Task Disabling_lock_exposes_all_languages()
    {
        using var backend = NewBackend();
        await backend.ConnectAsync();
        await backend.EnterServiceModeAsync();

        await backend.SetLanguageLockAsync(false);
        var available = await backend.GetAvailableLanguagesAsync();

        Assert.Equal(CameraLanguages.All.Count, available.Count);
    }

    [Fact]
    public async Task Real_service_backend_is_not_implemented()
    {
        using var backend = new ServiceModeCameraBackend();

        Assert.False(backend.SupportsMenuLanguageWrite);
        Assert.False(backend.IsSimulation);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => backend.ConnectAsync());
        Assert.Contains("未包含", error.Message);
    }
}
