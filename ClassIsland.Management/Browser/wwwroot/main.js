import { dotnet } from './_framework/dotnet.js';

try {
    const runtime = await dotnet.create();
    await runtime.runMain();
    document.getElementById('loading')?.remove();
} catch (error) {
    const loading = document.getElementById('loading');
    if (loading) loading.textContent = '暂时无法加载管理工作台，请刷新页面重试。';
    console.error('Management workspace failed to load.', error);
}
