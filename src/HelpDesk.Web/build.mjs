// Compila la interfaz (React + TypeScript) y la deja en ../HelpDesk.Api/wwwroot,
// desde donde la sirve el backend ASP.NET Core. Uso: node build.mjs [--watch]
import * as esbuild from 'esbuild';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const raiz = path.dirname(fileURLToPath(import.meta.url));
const salida = path.resolve(raiz, '../HelpDesk.Api/wwwroot');
const carpetaAssets = path.join(salida, 'assets');
const modoWatch = process.argv.includes('--watch');

const escribirHtml = {
  name: 'escribir-html',
  setup(build) {
    build.onStart(() => {
      fs.mkdirSync(carpetaAssets, { recursive: true });
      for (const archivo of fs.readdirSync(carpetaAssets)) {
        if (/^app-.*\.(js|css)(\.map)?$/.test(archivo)) fs.rmSync(path.join(carpetaAssets, archivo));
      }
    });
    build.onEnd((resultado) => {
      if (!resultado.metafile) return;
      const salidas = Object.keys(resultado.metafile.outputs).map((ruta) => path.basename(ruta));
      const js = salidas.find((n) => n.endsWith('.js'));
      const css = salidas.find((n) => n.endsWith('.css'));
      const plantilla = fs.readFileSync(path.join(raiz, 'index.html'), 'utf8');
      const html = plantilla
        .replace('<!--CSS-->', css ? `<link rel="stylesheet" href="/assets/${css}">` : '')
        .replace('<!--JS-->', js ? `<script src="/assets/${js}" defer></script>` : '');
      fs.writeFileSync(path.join(salida, 'index.html'), html);
      fs.copyFileSync(path.join(raiz, 'favicon.svg'), path.join(salida, 'favicon.svg'));
      console.log(`Interfaz generada en ${salida} (${js}, ${css})`);
    });
  },
};

const opciones = {
  entryPoints: [path.join(raiz, 'src/main.tsx')],
  bundle: true,
  outdir: carpetaAssets,
  entryNames: 'app-[hash]',
  minify: !modoWatch,
  sourcemap: modoWatch ? 'inline' : false,
  target: ['es2020', 'chrome100', 'edge100', 'firefox100', 'safari15'],
  jsx: 'automatic',
  metafile: true,
  legalComments: 'none',
  logLevel: 'info',
  define: { 'process.env.NODE_ENV': JSON.stringify(modoWatch ? 'development' : 'production') },
  plugins: [escribirHtml],
};

if (modoWatch) {
  const contexto = await esbuild.context(opciones);
  await contexto.watch();
  console.log('Compilando en modo watch...');
} else {
  await esbuild.build(opciones);
}
