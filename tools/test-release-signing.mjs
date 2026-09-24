import { generateKeyPairSync } from 'node:crypto';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { signFile, verifyFile } from './release-signing.mjs';

test('signatures reject tampered binaries, wrong keys, and corrupted signatures', async () => {
    const keys = generateKeyPairSync('rsa', { modulusLength: 3072,
        publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
    const other = generateKeyPairSync('rsa', { modulusLength: 3072 }).publicKey.export({ type: 'spki', format: 'pem' });
    const directory = await mkdtemp(join(tmpdir(), 'comic-signing-'));
    const file = join(directory, 'editor.exe');
    try {
        await writeFile(file, 'release binary');
        await signFile(file, keys.privateKey, keys.publicKey);
        await verifyFile(file, keys.publicKey);
        await assert.rejects(verifyFile(file, other), /verification failed/);
        await assert.rejects(signFile(file, keys.privateKey, other), /does not match/);
        await writeFile(file, 'changed binary');
        await assert.rejects(verifyFile(file, keys.publicKey), /verification failed/);
        await writeFile(file, 'release binary');
        const signature = await readFile(file + '.sig'); signature[0] ^= 1;
        await writeFile(file + '.sig', signature);
        await assert.rejects(verifyFile(file, keys.publicKey), /verification failed/);
        await writeFile(file + '.sig', signature.subarray(1));
        await assert.rejects(verifyFile(file, keys.publicKey), /length/);
    } finally { await rm(directory, { recursive: true }); }
});
