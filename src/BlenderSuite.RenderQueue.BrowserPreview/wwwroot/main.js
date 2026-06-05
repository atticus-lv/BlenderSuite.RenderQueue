import { dotnet } from './_framework/dotnet.js'

const isBrowser = typeof window !== 'undefined';
if (!isBrowser) {
  throw new Error('Expected to be running in a browser');
}

const bootLoading = document.getElementById('boot-loading');
const out = document.getElementById('out');
let bootLoadingHidden = false;
const hideBootLoading = () => {
  if (bootLoadingHidden) return;

  bootLoadingHidden = true;
  bootLoading?.classList.add('boot-loading-hidden');
  window.setTimeout(() => bootLoading?.remove(), 240);
};

const observer = out
  ? new MutationObserver(() => {
      if (out.querySelector('canvas')) {
        observer?.disconnect();
        hideBootLoading();
      }
    })
  : null;

observer?.observe(out, { childList: true, subtree: true });

const dotnetRuntime = await dotnet
  .withDiagnosticTracing(false)
  .withApplicationArgumentsFromQuery()
  .create();

const config = dotnetRuntime.getConfig();

window.setTimeout(hideBootLoading, 2500);
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
