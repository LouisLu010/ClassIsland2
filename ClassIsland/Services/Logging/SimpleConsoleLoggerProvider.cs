using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using ClassIsland.Core.Helpers;
using ClassIsland.Models.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services.Logging;

/// <summary>
/// 直接写 stdout 的控制台日志提供程序，用于不支持后台线程的平台（如浏览器 WASM）。
/// <remarks>
/// <see cref="Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider"/> 依赖
/// <see cref="Thread"/> 做异步队列，在单线程的浏览器运行时下构造即抛
/// <see cref="PlatformNotSupportedException"/>。
/// </remarks>
/// </summary>
public class SimpleConsoleLoggerProvider : ILoggerProvider
{
    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName) => new SimpleConsoleLogger(categoryName);
}

/// <summary>
/// <see cref="SimpleConsoleLoggerProvider"/> 使用的同步日志记录器。
/// </summary>
public class SimpleConsoleLogger(string categoryName) : ILogger
{
    private string CategoryName { get; } = categoryName;

    private static readonly AsyncLocal<ImmutableStack<object>> ScopeStack = new();

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var scopes = new List<string>();
        if (ScopeStack.Value != null)
        {
            scopes.AddRange(ScopeStack.Value.Select(scope => (scope.ToString() ?? "") + " => "));
        }

        var message = string.Join("", scopes) + formatter(state, exception);
        Console.WriteLine($"[{ToShortName(logLevel)}] {CategoryName}: {LogMaskingHelper.MaskLog(message)}");
        if (exception != null)
        {
            Console.WriteLine(exception.ToString());
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        var previous = ScopeStack.Value;
        ScopeStack.Value = (previous ?? ImmutableStack<object>.Empty).Push(state);
        return new LoggingScope(() => ScopeStack.Value = previous);
    }

    private static string ToShortName(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "none"
    };
}

public static class SimpleConsoleLoggerExtensions
{
    /// <summary>
    /// 注册 <see cref="SimpleConsoleLoggerProvider"/>，替代依赖后台线程的标准控制台日志提供程序。
    /// </summary>
    public static ILoggingBuilder AddSimpleConsoleFallback(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider, SimpleConsoleLoggerProvider>();
        return builder;
    }
}
