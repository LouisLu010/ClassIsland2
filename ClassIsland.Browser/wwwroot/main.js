import { dotnet } from './_framework/dotnet.js';

// 浏览器端没有终端，把启动期的致命错误直接渲染到页面上，方便定位。
function reportFatal(label, detail) {
    const box = document.createElement('pre');
    box.style.cssText =
        'position:fixed;inset:0;z-index:9999;margin:0;padding:16px;overflow:auto;' +
        'background:#1b1b1b;color:#ff8a80;font:12px/1.5 Consolas,monospace;white-space:pre-wrap';
    box.textContent = `[${label}]\n\n${detail}`;
    document.body.appendChild(box);
}

window.addEventListener('error', e => reportFatal('window.error', e.error?.stack ?? e.message));
window.addEventListener('unhandledrejection', e =>
    reportFatal('unhandledrejection', e.reason?.stack ?? String(e.reason)));

try {
    const dotnetRuntime = await dotnet
        .withDiagnosticTracing(false)
        .create();

    const config = dotnetRuntime.getConfig();

    // 注意：runMain 的第二个参数会整体覆盖运行时自己解析的参数，
    // 所以查询串里的 ?arg=xxx 必须在这里显式拼进去，否则会被丢掉。
    const appArgs = new URLSearchParams(globalThis.location.search).getAll('arg');
    await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href, ...appArgs]);
} catch (err) {
    reportFatal('runMain', err?.stack ?? String(err));
    throw err;
}
