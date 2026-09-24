import { dotnet } from './_framework/dotnet.js';

try {
    const runtime = await dotnet.withDiagnosticTracing(false).create();
    await runtime.runMain(runtime.getConfig().mainAssemblyName, [globalThis.location.href]);
} catch (error) {
    console.error('ComicEditor startup failed', error);
    const status = document.getElementById('loading-status');
    if (status) status.textContent = 'ComicEditor could not start. Reload this page or try a current browser.';
}
