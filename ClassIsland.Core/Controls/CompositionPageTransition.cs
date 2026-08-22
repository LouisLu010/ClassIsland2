using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Rendering.Composition.Transport;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClassIsland.Core.Controls;

/// <summary>
/// 基于 Composition 实现的页面过渡，视觉效果与 <c>PageSlide</c> 和
/// <c>CrossFade</c> 的组合过渡一致：旧页面水平滑出并淡出，新页面从相反方向
/// 滑入并淡入。
/// </summary>
/// <remarks>
/// Composition 动画不由 UI 线程驱动，而是序列化到 composition batch 后再由
/// 渲染线程应用。因此在高负载下，携带 <c>StartAnimation</c> 的 batch 可能在本方法
/// 已尝试停止动画后才到达 compositor。若不处理，延迟应用的动画会一直附着在
/// presenter visual 上，并覆盖后续显式属性赋值（TabControl 会为所有切页复用同一对
/// presenter）。清理时会先经过一个不同的临时值写入静止值，以撤回尚未提交的动画，
/// 再立即停止活动动画；渲染线程确认 batch 后，还会排入一次带世代校验的额外清理，
/// 以停止延迟应用的动画。世代校验也能防止已取消的过渡停止同一 presenter 上的新过渡。
/// </remarks>
public class CompositionPageTransition : IPageTransition
{
    private static readonly ConditionalWeakTable<CompositionVisual, GenerationBox> VisualGenerations = new();

    private static long _currentGeneration;

    /// <summary>
    /// 等待渲染线程确认过渡动画已经应用的时间上限。超时后先尽力清理，
    /// 再由 batch 完成后的清理流程收尾。
    /// </summary>
    private static readonly TimeSpan ApplyConfirmationTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 滑动和淡入淡出动画的持续时间。
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// 旧页面滑出动画使用的缓动。
    /// </summary>
    public Easing SlideOutEasing { get; set; } = Easing.Parse("0,0 0,1");

    /// <summary>
    /// 新页面滑入动画使用的缓动。
    /// </summary>
    public Easing SlideInEasing { get; set; } = Easing.Parse("0,0 0,1");

    /// <summary>
    /// 旧页面淡出动画使用的缓动。
    /// </summary>
    public Easing FadeOutEasing { get; set; } = Easing.Parse("0,0 0,1");

    /// <summary>
    /// 新页面淡入动画使用的缓动。
    /// </summary>
    public Easing FadeInEasing { get; set; } = Easing.Parse("0,0 0,1");

    /// <inheritdoc />
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (from is null || to is null)
        {
            if (from is not null)
            {
                from.IsVisible = false;
            }

            if (to is not null)
            {
                to.IsVisible = true;
            }

            return;
        }

        var fromCompositionVisual = ElementComposition.GetElementVisual(from);
        var toCompositionVisual = ElementComposition.GetElementVisual(to);

        if (fromCompositionVisual is null || toCompositionVisual is null)
        {
            // 任一页面无法使用 Composition 时直接切换，避免两个页面保持可见并相互重叠。
            from.IsVisible = false;
            to.IsVisible = true;
            return;
        }

        var parent = from.GetVisualParent() ?? to.GetVisualParent();
        var distance = parent?.Bounds.Width
                       ?? Math.Max(from.Bounds.Width, to.Bounds.Width);

        if (distance <= 0)
        {
            from.IsVisible = false;
            to.IsVisible = true;
            return;
        }

        var fromBaseTranslation = fromCompositionVisual.Translation;
        var toBaseTranslation = toCompositionVisual.Translation;
        var fromBaseOpacity = fromCompositionVisual.Opacity;
        var toBaseOpacity = toCompositionVisual.Opacity;

        // TabControl 会在调用 Start 前显示新页面；此处仍按 PageSlide 的行为主动显示，
        // 以兼容独立调用。
        to.IsVisible = true;

        // 为本次过渡占用两个 visual，防止旧过渡的清理触碰已被新过渡复用的 visual。
        // TabControl 始终只保留两个 presenter visual。
        var generation = Interlocked.Increment(ref _currentGeneration);
        ClaimGeneration(fromCompositionVisual, generation);
        ClaimGeneration(toCompositionVisual, generation);

        // PageSlide 会用从零开始的位移替换渲染变换，因此从零而非 visual 的基础位移开始。
        StartSlideAnimation(
            fromCompositionVisual,
            new Vector3D(),
            new Vector3D(forward ? -distance : distance, 0, 0),
            SlideOutEasing);

        StartSlideAnimation(
            toCompositionVisual,
            new Vector3D(forward ? distance : -distance, 0, 0),
            new Vector3D(),
            SlideInEasing);

        // CrossFade 将旧页面的 Opacity 从 1 变为 0，并将新页面从 0 变为 1。
        StartFadeAnimation(fromCompositionVisual, 1f, 0f, FadeOutEasing);
        StartFadeAnimation(toCompositionVisual, 0f, 1f, FadeInEasing);

        // 动画会进入当前 composition batch；保留它以便清理流程先确认渲染线程已实际应用动画。
        CompositionBatch? batch = null;
        try
        {
            batch = fromCompositionVisual.Compositor.RequestCompositionBatchCommitAsync();
        }
        catch
        {
            // 跟踪 batch 只用于提高时序精度；即使无法跟踪，后续清理仍然有效。
        }

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            if (batch is not null)
            {
                try
                {
                    await batch.Processed.WaitAsync(ApplyConfirmationTimeout, cancellationToken);
                }
                catch (TimeoutException)
                {
                    // compositor 严重阻塞时不再等待，由 batch 完成后的清理处理延迟应用的动画。
                }
            }

            // 动画时间线从 batch 提交时开始，因此确认后只需等待剩余时长。
            var remaining = Duration - Stopwatch.GetElapsedTime(startedAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            RestoreVisuals(
                generation,
                fromCompositionVisual, fromBaseTranslation, fromBaseOpacity,
                toCompositionVisual, toBaseTranslation, toBaseOpacity);
            QueuePostApplyCleanup(
                batch, generation,
                fromCompositionVisual, fromBaseTranslation, fromBaseOpacity,
                toCompositionVisual, toBaseTranslation, toBaseOpacity);
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            from.IsVisible = false;
        }

        RestoreVisuals(
            generation,
            fromCompositionVisual, fromBaseTranslation, fromBaseOpacity,
            toCompositionVisual, toBaseTranslation, toBaseOpacity);
        QueuePostApplyCleanup(
            batch, generation,
            fromCompositionVisual, fromBaseTranslation, fromBaseOpacity,
            toCompositionVisual, toBaseTranslation, toBaseOpacity);
    }

    private void StartSlideAnimation(
        CompositionVisual visual,
        Vector3D from,
        Vector3D to,
        Easing easing)
    {
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = nameof(CompositionVisual.Translation);
        animation.Duration = Duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, easing);
        visual.StartAnimation(nameof(CompositionVisual.Translation), animation);
    }

    private void StartFadeAnimation(
        CompositionVisual visual,
        float from,
        float to,
        Easing easing)
    {
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Target = nameof(CompositionVisual.Opacity);
        animation.Duration = Duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, easing);
        visual.StartAnimation(nameof(CompositionVisual.Opacity), animation);
    }

    private static void RestoreVisuals(
        long generation,
        CompositionVisual from, Vector3D fromTranslation, float fromOpacity,
        CompositionVisual to, Vector3D toTranslation, float toOpacity)
    {
        RestoreVisualIfOwned(from, generation, fromTranslation, fromOpacity);
        RestoreVisualIfOwned(to, generation, toTranslation, toOpacity);
    }

    private static void RestoreVisualIfOwned(
        CompositionVisual visual,
        long generation,
        Vector3D translation,
        float opacity)
    {
        if (!VisualGenerations.TryGetValue(visual, out var box) || box.Value != generation)
        {
            return;
        }

        // CompositionVisual 会忽略与本地基础值相同的赋值，使未提交动画残留在
        // PendingAnimations 中。先把每个属性写成不同值，再写入最终值，确保最终赋值
        // 被序列化为直接值并可靠替换待处理的动画启动。只有最终值会被序列化，
        // 临时值不会到达渲染线程。
        visual.Translation = new Vector3D(
            translation.X == 0 ? 1 : 0,
            translation.Y,
            translation.Z);
        visual.Translation = translation;
        visual.Opacity = opacity == 0 ? 1 : 0;
        visual.Opacity = opacity;

        // 直接赋值会撤回尚未提交的动画启动，StopAnimation 则处理已附着到 compositor 的动画。
        visual.StopAnimation(nameof(CompositionVisual.Translation));
        visual.StopAnimation(nameof(CompositionVisual.Opacity));
    }

    private static void QueuePostApplyCleanup(
        CompositionBatch? batch,
        long generation,
        CompositionVisual from, Vector3D fromTranslation, float fromOpacity,
        CompositionVisual to, Vector3D toTranslation, float toOpacity)
    {
        void Cleanup()
        {
            RestoreVisualIfOwned(from, generation, fromTranslation, fromOpacity);
            RestoreVisualIfOwned(to, generation, toTranslation, toOpacity);
        }

        if (batch is null)
        {
            Dispatcher.UIThread.Post(Cleanup, DispatcherPriority.Background);
            return;
        }

        // 渲染线程处理携带动画启动的 batch 后再次执行。更早发出的停止操作会漏掉尚未
        // 应用的动画；若即时恢复已经成功，这次清理仍保持幂等。
        batch.Processed.ContinueWith(
            _ => Dispatcher.UIThread.Post(Cleanup, DispatcherPriority.Send),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ClaimGeneration(CompositionVisual visual, long generation)
    {
        VisualGenerations.GetOrCreateValue(visual).Value = generation;
    }

    private sealed class GenerationBox
    {
        public long Value;
    }
}
