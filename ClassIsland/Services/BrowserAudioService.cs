using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;

namespace ClassIsland.Services;

/// <summary>
/// 浏览器端的音频服务空实现。
/// <remarks>
/// SoundFlow 的 MiniAudio 后端需要加载平台原生库，在 browser-wasm 上不存在，
/// 构造 <see cref="SoundFlow.Backends.MiniAudio.MiniAudioEngine"/> 即抛
/// <see cref="PlatformNotSupportedException"/>。此实现让依赖音频的界面能正常显示，
/// 播放请求只记录日志。
/// </remarks>
/// </summary>
public class BrowserAudioService(ILogger<BrowserAudioService> logger) : IAudioService
{
    private ILogger<BrowserAudioService> Logger { get; } = logger;

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">浏览器端没有可用的音频引擎。</exception>
    public AudioEngine AudioEngine =>
        throw new PlatformNotSupportedException("浏览器端没有可用的音频引擎。");

    public AudioPlaybackDevice? TryInitializeDefaultPlaybackDevice() => null;

    public Task<AudioPlaybackDevice?> TryInitializeDefaultPlaybackDeviceAsync() =>
        Task.FromResult<AudioPlaybackDevice?>(null);

    public Task<RefCounted<AudioPlaybackDevice>.Lease?> TryInitializeDefaultPlaybackDeviceSafeAsync() =>
        Task.FromResult<RefCounted<AudioPlaybackDevice>.Lease?>(null);

    public Task PlayAudioAsync(Stream audio, float volume, CancellationToken? cancellationToken = null)
    {
        audio.Dispose();
        Logger.LogDebug("浏览器端不支持播放音频，已忽略播放请求。");
        return Task.CompletedTask;
    }

    public Task PlayAudioAsync(string filePath, float volume, CancellationToken? cancellationToken = null)
    {
        Logger.LogDebug("浏览器端不支持播放音频，已忽略播放请求 {}。", filePath);
        return Task.CompletedTask;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
