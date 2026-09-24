const account = process.env.CLOUDFLARE_ACCOUNT_ID;
const token = process.env.CLOUDFLARE_API_TOKEN;
if (!account || !token) throw new Error('Set CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN in repository Actions secrets.');
if (!/^[a-f0-9]{32}$/i.test(account)) throw new Error('Invalid Cloudflare account ID.');
const url = `https://api.cloudflare.com/client/v4/accounts/${account}/pages/projects`;
const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
const existing = await fetch(url + '/comic-editor', { headers });
if (existing.ok) {
    const { result } = await existing.json();
    if (result.production_branch !== 'main') throw new Error('The existing comic-editor Pages project must use main as its production branch.');
    if (result.subdomain !== 'comic-editor.pages.dev') throw new Error('The requested comic-editor.pages.dev address is not available on this project.');
    console.log('Using existing comic-editor Pages project.');
} else {
    if (existing.status !== 404) throw new Error(`Cloudflare project lookup failed (HTTP ${existing.status}). Check token permissions and account ID.`);
    const created = await fetch(url, {
        method: 'POST', headers,
        body: JSON.stringify({ name: 'comic-editor', production_branch: 'main' })
    });
    if (!created.ok) throw new Error(`Cloudflare project creation failed (HTTP ${created.status}).`);
    const { result } = await created.json();
    if (result.subdomain !== 'comic-editor.pages.dev') throw new Error('Cloudflare assigned a different address; comic-editor.pages.dev is unavailable.');
    console.log('Created comic-editor.pages.dev.');
}
