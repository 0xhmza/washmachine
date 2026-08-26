// Pre-compile JSX → JS at build time so the runtime app doesn't need Babel.
// Eliminates ~3MB Babel download and ~200ms parse on cold start.
//
// Usage: node build.cjs
//   produces dist/*.js mirroring the .jsx source layout.

const babel = require('@babel/core');
const fs = require('fs');
const path = require('path');

const SRC = __dirname;
const DIST = path.join(__dirname, 'dist');

const SOURCES = [
  'shell.jsx',
  'frames/01-workspace.jsx',
  'frames/02-encode.jsx',
  'frames/03-template.jsx',
  'frames/04-compile.jsx',
  'frames/05-backdoor.jsx',
  'frames/06-finalize.jsx',
  'frames/06b-packing.jsx',
  'frames/07-history.jsx',
  'frames/08-webwizard.jsx',
  'frames/09-startup.jsx',
  'frames/10-pipeline.jsx',
  'frames/11-settings.jsx',
];

function ensureDir(p) {
  fs.mkdirSync(p, { recursive: true });
}

function compileOne(rel) {
  const inPath = path.join(SRC, rel);
  const outPath = path.join(DIST, rel.replace(/\.jsx$/, '.js'));
  ensureDir(path.dirname(outPath));

  const code = fs.readFileSync(inPath, 'utf8');
  const result = babel.transformSync(code, {
    presets: [
      ['@babel/preset-env', { targets: { chrome: '110' }, modules: false }],
      ['@babel/preset-react', { runtime: 'classic' }],
    ],
    filename: inPath,
    sourceMaps: false,
    compact: false,
    comments: false,
  });

  fs.writeFileSync(outPath, result.code, 'utf8');
  return { rel, bytes: Buffer.byteLength(result.code, 'utf8') };
}

ensureDir(DIST);

let total = 0;
for (const rel of SOURCES) {
  try {
    const { bytes } = compileOne(rel);
    total += bytes;
    console.log(`  ok  ${rel}  (${bytes} bytes)`);
  } catch (e) {
    console.error(`  err ${rel}: ${e.message}`);
    process.exitCode = 1;
  }
}

console.log(`\nCompiled ${SOURCES.length} files · ${total} bytes total`);
