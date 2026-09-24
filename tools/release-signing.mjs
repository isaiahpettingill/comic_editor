import { constants, createPrivateKey, createPublicKey, createSign, createVerify } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

const options = { padding: constants.RSA_PKCS1_PSS_PADDING, saltLength: 32 };
export const publicKeyPath = new URL('../signing/desktop-public.pem', import.meta.url);

export async function signFile(file, privatePem, publicPem) {
    const key = createPrivateKey(privatePem);
    if (key.asymmetricKeyType !== 'rsa' || key.asymmetricKeyDetails.modulusLength !== 3072)
        throw new Error('Expected a 3072-bit RSA signing key.');
    const encode = value => createPublicKey(value).export({ type: 'spki', format: 'der' });
    if (!encode(key).equals(encode(publicPem))) throw new Error('Signing secret does not match the committed public key.');
    const signer = createSign('sha256');
    for await (const chunk of createReadStream(file)) signer.update(chunk);
    await writeFile(file + '.sig', signer.sign({ key, ...options }));
    await verifyFile(file, publicPem);
}

export async function verifyFile(file, publicPem) {
    const signature = await readFile(file + '.sig');
    if (signature.length !== 384) throw new Error(`Invalid signature length: ${file}`);
    const verifier = createVerify('sha256');
    for await (const chunk of createReadStream(file)) verifier.update(chunk);
    if (!verifier.verify({ key: publicPem, ...options }, signature)) throw new Error(`Signature verification failed: ${file}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    const [command, ...files] = process.argv.slice(2);
    if (!['sign', 'verify'].includes(command) || !files.length) throw new Error('Usage: node tools/release-signing.mjs <sign|verify> <files...>');
    const publicPem = await readFile(publicKeyPath, 'utf8');
    const privatePem = process.env.DESKTOP_SIGNING_PRIVATE_KEY;
    if (command === 'sign' && !privatePem) throw new Error('Missing DESKTOP_SIGNING_PRIVATE_KEY GitHub secret.');
    for (const file of files) {
        if (command === 'sign') await signFile(file, privatePem, publicPem);
        else await verifyFile(file, publicPem);
        console.log(`${command === 'sign' ? 'Signed and verified' : 'Verified'} ${file}`);
    }
}
