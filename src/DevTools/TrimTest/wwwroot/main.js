import { dotnet } from './_framework/dotnet.js';

const { getAssemblyExports, getConfig } = await dotnet.create();
const exports = await getAssemblyExports(getConfig().mainAssemblyName);

const results = { run: exports.Smoke.Run(), libraries: exports.Smoke.RunLibraries() };

window.smokeResult = results;
document.getElementById('out').textContent = JSON.stringify(results, null, 2);
