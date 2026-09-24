// Keep the generic release ZIP unchanged. Load oversized WASM through .NET's
// resource loader with browser-native gzip decompression and integrity checks.
import { mkdir, readdir, readFile, writeFile } from 'node:fs/promises';
import { resolve, join, relative } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';

const [sourceArg, outputArg] = process.argv.slice(2);
if (!sourceArg || !outputArg) throw new Error('Usage: node tools/prepare-pages.mjs <wwwroot> <new-output-directory>');
const source = resolve(sourceArg);
const output = resolve(outputArg);
if (output === source || output.startsWith(source + '/') || output.startsWith(source + '\\'))
    throw new Error('Output must be separate from the source.');
await mkdir(output, { recursive: false }); // Refuse stale files from earlier builds.
let count = 0;
let total = 0;
const headers = ['/*\n  X-Content-Type-Options: nosniff\n', '/\n  Cache-Control: no-cache\n', '/index.html\n  Cache-Control: no-cache\n'];
const compressedFiles = [];
async function copy(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
        const path = join(directory, entry.name);
        const name = relative(source, path).replaceAll('\\', '/');
        let target = join(output, name);
        if (entry.isSymbolicLink()) throw new Error(`Unexpected symbolic link: ${name}`);
        if (entry.isDirectory()) {
            await mkdir(target);
            await copy(path);
            continue;
        }
        if (/\.(br|gz|pdb|map)$/.test(name)) continue;
        if (name === '_headers' || name === '_worker.js') throw new Error(`Unexpected hosting configuration: ${name}`);
        let bytes = await readFile(path);
        if (bytes.length > 25 * 1024 * 1024 && name.endsWith('.wasm')) {
            const compressed = await readFile(path + '.gz');
            if (!gunzipSync(compressed).equals(bytes)) throw new Error(`Gzip content mismatch: ${name}`);
            bytes = compressed;
            target += '.gz';
            compressedFiles.push(name);
            headers.push(`/${name}.gz\n  Content-Type: application/gzip\n  Cache-Control: public, max-age=31536000, immutable, no-transform\n`);
        }
        if (bytes.length > 25 * 1024 * 1024) throw new Error(`Pages asset exceeds 25 MiB: ${name}`);
        await writeFile(target, bytes);
        count++;
        total += bytes.length;
    }
}
await copy(source);
if (compressedFiles.length) {
    let html = await readFile(join(output, 'index.html'), 'utf8');
    const mainScript = /<script type="module" src="([^"\n]+)"><\/script>/;
    const match = html.match(mainScript);
    if (!match) throw new Error('Cannot locate the application module in index.html.');
    const bootstrap = `import { dotnet } from './_framework/dotnet.js';
const compressed = ${JSON.stringify(compressedFiles)};
dotnet.withResourceLoader((type, name, uri, integrity) => {
    if (!compressed.some(path => new URL(uri, location.href).pathname === '/' + path)) return;
    return (async () => {
        const response = await fetch(uri + '.gz');
        if (!response.ok) throw new Error('WASM download failed: ' + response.status);
        const bytes = await new Response(response.body.pipeThrough(new DecompressionStream('gzip'))).arrayBuffer();
        const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
        const hash = 'sha256-' + btoa(String.fromCharCode(...digest));
        if (hash !== integrity) throw new Error('WASM integrity check failed: ' + name);
        return new Response(bytes, { headers: { 'Content-Type': 'application/wasm' } });
    })();
});
await import(new URL(${JSON.stringify(match[1])}, location.href).href);
`;
    const filename = 'pages-start.' + createHash('sha256').update(bootstrap).digest('hex').slice(0, 16) + '.js';
    await writeFile(join(output, filename), bootstrap);
    html = html.replace(mainScript, `<script type="module" src="./${filename}"></script>`);
    await writeFile(join(output, 'index.html'), html);
}
if (headers.length > 100) throw new Error('Pages supports at most 100 header rules.');
if (count > 19999) throw new Error('Pages supports at most 20,000 assets.');
await writeFile(join(output, '_headers'), headers.join('\n'));
console.log(`Prepared ${count} assets (${(total / 1024 / 1024).toFixed(2)} MiB) for Cloudflare Pages.`);
