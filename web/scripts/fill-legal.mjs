// Fills the operator's name and contact address into the built about, privacy and terms pages (dist/). They are kept out of the
// repository: on main they come from the repository variables LEGAL_OPERATOR_NAME and LEGAL_CONTACT_EMAIL, on this PC
// from web/.env.local. Without them the placeholders stay, which is fine for test builds; the pages workflow requires
// them on main.
import { existsSync } from 'node:fs';
import { readFile, writeFile } from 'node:fs/promises';

for (const file of ['.env.local', '.env']) {
  if (existsSync(file)) {
    process.loadEnvFile(file);
  }
}

const name = process.env.LEGAL_OPERATOR_NAME?.trim();
const email = process.env.LEGAL_CONTACT_EMAIL?.trim();
if (!name || !email) {
  console.warn('fill-legal: LEGAL_OPERATOR_NAME or LEGAL_CONTACT_EMAIL not set; the about, privacy and terms pages keep their placeholders.');
  process.exit(0);
}

if (name.length > 100 || !/^[^@\s<>"'&]{1,64}@[^@\s<>"'&]+\.[a-z]{2,}$/i.test(email)) {
  console.error('fill-legal: LEGAL_OPERATOR_NAME must be at most 100 characters and LEGAL_CONTACT_EMAIL an e-mail address.');
  process.exit(1);
}

const escape = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

for (const file of ['dist/about.html', 'dist/privacy.html', 'dist/terms.html']) {
  const html = await readFile(file, 'utf8');
  const filled = html.replaceAll('[OPERATOR NAME]', escape(name)).replaceAll('[CONTACT E-MAIL]', escape(email));
  if (filled === html) {
    console.error(`fill-legal: ${file} has no placeholders to fill.`);
    process.exit(1);
  }

  await writeFile(file, filled, 'utf8');
}

console.log('fill-legal: about, privacy and terms pages filled in.');
