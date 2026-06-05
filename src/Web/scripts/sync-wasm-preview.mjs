import { brotliCompressSync, gzipSync } from 'node:zlib'
import {
  copyFileSync,
  cpSync,
  existsSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptDir = dirname(fileURLToPath(import.meta.url))
const webRoot = resolve(scriptDir, '..')
const repoSrcRoot = resolve(webRoot, '..')
const configuration = process.env.BRQ_WASM_CONFIGURATION ?? 'Release'

const candidates = [
  resolve(
    repoSrcRoot,
    'BlenderSuite.RenderQueue.BrowserPreview',
    'bin',
    configuration,
    'net10.0-browser',
    'publish',
    'wwwroot',
  ),
  resolve(
    repoSrcRoot,
    'BlenderSuite.RenderQueue.BrowserPreview',
    'bin',
    configuration,
    'net10.0-browser',
    'wwwroot',
  ),
  resolve(
    repoSrcRoot,
    'BlenderSuite.RenderQueue.BrowserPreview',
    'bin',
    'Debug',
    'net10.0-browser',
    'wwwroot',
  ),
]

const source = candidates.find((candidate) => existsSync(resolve(candidate, '_framework', 'dotnet.js')))

if (!source) {
  console.error('BrowserPreview wwwroot was not found.')
  console.error('Run `npm run wasm-preview:build` first, then retry.')
  process.exit(1)
}

const target = resolve(webRoot, 'public', 'wasm-preview')
const browserPreviewSourceRoot = resolve(repoSrcRoot, 'BlenderSuite.RenderQueue.BrowserPreview', 'wwwroot')
const appLogoSource = resolve(repoSrcRoot, 'BlenderSuite.RenderQueue', 'Assets', 'logo.png')

rmSync(target, { recursive: true, force: true })
cpSync(source, target, { recursive: true })
cpSync(browserPreviewSourceRoot, target, { recursive: true })
copyFileSync(resolve(browserPreviewSourceRoot, 'main.js'), resolve(target, 'main.js'))
copyFileSync(appLogoSource, resolve(target, 'logo.png'))

const mainJs = readFileSync(resolve(target, 'main.js'))
writeFileSync(resolve(target, 'main.js.gz'), gzipSync(mainJs))
writeFileSync(resolve(target, 'main.js.br'), brotliCompressSync(mainJs))

const frameworkTarget = resolve(target, '_framework')
const dotnetJs = readFileSync(resolve(frameworkTarget, 'dotnet.js'), 'utf8')
const manifestAssetNames = new Set(
  Array.from(dotnetJs.matchAll(/"name"\s*:\s*"([^"]+)"/g), (match) => match[1]),
)
const alwaysKeepFrameworkFiles = new Set([
  'avalonia.js',
  'avalonia.js.br',
  'avalonia.js.gz',
  'avalonia.js.map',
  'avalonia.js.map.br',
  'avalonia.js.map.gz',
  'dotnet.js',
  'dotnet.js.br',
  'dotnet.js.gz',
  'dotnet.runtime.js.map',
  'dotnet.runtime.js.map.br',
  'dotnet.runtime.js.map.gz',
  'storage.js',
  'storage.js.br',
  'storage.js.gz',
  'storage.js.map',
  'storage.js.map.br',
  'storage.js.map.gz',
  'sw.js',
  'sw.js.br',
  'sw.js.gz',
  'sw.js.map',
  'sw.js.map.br',
  'sw.js.map.gz',
])
const generatedAssetPattern = /\.(wasm|dat|pdb|dll|mjs|js)(\.(br|gz))?$/

for (const fileName of readdirSync(frameworkTarget)) {
  if (alwaysKeepFrameworkFiles.has(fileName)) {
    continue
  }

  const uncompressedName = fileName.replace(/\.(br|gz)$/, '')
  if (generatedAssetPattern.test(fileName) && !manifestAssetNames.has(uncompressedName)) {
    rmSync(resolve(frameworkTarget, fileName), { force: true })
  }
}

console.log(`Synced BrowserPreview assets:`)
console.log(`  ${source}`)
console.log(`  -> ${target}`)
console.log('  main.js and compressed variants overridden from BrowserPreview source')
console.log('  app logo copied for deployed BrowserPreview asset requests')
console.log('  stale framework hash assets pruned from dotnet.js manifest')
