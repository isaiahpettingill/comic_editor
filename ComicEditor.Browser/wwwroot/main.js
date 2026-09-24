import { dotnet } from './_framework/dotnet.js';

globalThis.comicEditorPreferences = {
    load() { try { return localStorage.getItem('comic-editor.preferences'); } catch { return null; } },
    save(json) { try { localStorage.setItem('comic-editor.preferences', json); } catch { /* Private storage may be unavailable. */ } }
};

// IndexedDB accommodates project artwork without localStorage's small quota.
let sessionDatabase;
function openSessionDatabase() {
    return sessionDatabase ??= new Promise((resolve, reject) => {
        const request = indexedDB.open('comic-editor', 1);
        request.onupgradeneeded = () => request.result.createObjectStore('session');
        request.onerror = () => { sessionDatabase = null; reject(request.error); };
        request.onsuccess = () => resolve(request.result);
    });
}
globalThis.comicEditorSession = {
    async load() {
        const db = await openSessionDatabase();
        return new Promise((resolve, reject) => {
            const request = db.transaction('session').objectStore('session').get('last');
            request.onsuccess = () => resolve(request.result ?? null);
            request.onerror = () => reject(request.error);
        });
    },
    async save(json) {
        const db = await openSessionDatabase();
        return new Promise((resolve, reject) => {
            const transaction = db.transaction('session', 'readwrite');
            transaction.objectStore('session').put(json, 'last');
            transaction.oncomplete = () => resolve();
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error ?? new Error('Recovery write was interrupted.'));
        });
    },
    onBackground(callback) {
        document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') callback(); });
    }
};

// Browser equivalent of the app-data palettes directory; values remain GPL text.
globalThis.comicEditorPalettes = {
    async listJson() { return JSON.stringify(await globalThis.comicEditorPalettes.list()); },
    async list() {
        const db = await openSessionDatabase();
        return new Promise((resolve, reject) => {
            const request = db.transaction('session').objectStore('session').getAllKeys();
            request.onsuccess = () => resolve(request.result.filter(k => typeof k === 'string' && k.startsWith('palettes/')).map(k => k.slice(9)).sort());
            request.onerror = () => reject(request.error);
        });
    },
    async read(name) {
        const db = await openSessionDatabase();
        return new Promise((resolve, reject) => {
            const request = db.transaction('session').objectStore('session').get('palettes/' + name);
            request.onsuccess = () => request.result == null ? reject(new Error('Palette not found.')) : resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    },
    async write(name, text) {
        const db = await openSessionDatabase();
        return new Promise((resolve, reject) => {
            const transaction = db.transaction('session', 'readwrite');
            transaction.objectStore('session').put(text, 'palettes/' + name);
            transaction.oncomplete = () => resolve();
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error ?? new Error('Palette write was interrupted.'));
        });
    }
};

globalThis.comicEditorFonts = {
    async read(key) {
        const db = await openSessionDatabase();
        return new Promise((resolve, reject) => {
            const request = db.transaction('session').objectStore('session').get('fonts/' + key);
            request.onsuccess = () => resolve(request.result ?? null);
            request.onerror = () => reject(request.error);
        });
    },
    async write(key, json) {
        const db = await openSessionDatabase();
        return new Promise((resolve, reject) => {
            const transaction = db.transaction('session', 'readwrite');
            transaction.objectStore('session').put(json, 'fonts/' + key);
            transaction.oncomplete = () => resolve();
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error ?? new Error('Font cache write was interrupted.'));
        });
    }
};

try {
    const runtime = await dotnet.withDiagnosticTracing(false).create();
    await runtime.runMain(runtime.getConfig().mainAssemblyName, [globalThis.location.href]);
} catch (error) {
    console.error('ComicEditor startup failed', error);
    const status = document.getElementById('loading-status');
    if (status) status.textContent = 'ComicEditor could not start. Reload this page or try a current browser.';
}
