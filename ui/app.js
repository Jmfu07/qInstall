/* ==========================================================================
   QINSTALL — PRODUCTION WEBVIEW2 CONTROLLER
   Bridges all prototype UX features with real C# InstallerEngine backend.
   ========================================================================== */
const CHECK_SVG = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>';
const LOADER_DURATION_MS = 7000;

const loaderState = {
  running: false,
  startTime: 0,
  rafId: null,
  backendReady: false,
  lastText: 'Verificando red y Winget runtime...',
  pendingOutdated: null
};

const state = {
  apps: [],
  onlineResults: [],
  selectedKeys: new Set(),
  selectedOnlineMap: new Map(),
  env: {
    hasInternet: true,
    wingetAvailable: true,
    wingetVersion: '',
    isAdmin: false,
    localDir: 'local\\',
    installedCount: 0,
    outdatedCount: 0
  },
  isBusy: false,
  isSearchingOnline: false,
  searchQuery: '',
  searchSeq: 0,
  searchTimer: null,
  viewMode: localStorage.getItem('qinstall_view') || 'cards',
  galleryCategory: '',
  themeMode: localStorage.getItem('qinstall_theme') || 'dark',
  contextKey: null
};

const CATEGORY_GRADIENTS = {
  'navegadores': ['#2563eb', '#06b6d4'],
  'utilidades': ['#4f46e5', '#8b5cf6'],
  'optimizacion': ['#d97706', '#f59e0b'],
  'arranque y reparacion': ['#dc2626', '#f97316'],
  'soporte y drivers': ['#059669', '#0ea5e9'],
  'multimedia': ['#db2777', '#ec4899'],
  'comunicacion': ['#059669', '#10b981'],
  'oficina': ['#0284c7', '#3b82f6'],
  'ia y agentes': ['#7c3aed', '#06b6d4'],
  'ides y jetbrains': ['#ec4899', '#8b5cf6'],
  'desarrollo': ['#0d9488', '#22d3ee'],
  'juegos': ['#9333ea', '#ec4899'],
  'runtimes': ['#475569', '#64748b'],
  'java': ['#b45309', '#ea580c'],
  'instaladores locales': ['#0f766e', '#14b8a6'],
  'resultados online (winget)': ['#059669', '#22c55e']
};

const CATEGORY_ICONS = {
  'navegadores': 'compass',
  'utilidades': 'wrench',
  'optimizacion': 'gauge',
  'optimización': 'gauge',
  'arranque y reparacion': 'hard-drive',
  'arranque y reparación': 'hard-drive',
  'soporte y drivers': 'shield-check',
  'multimedia': 'film',
  'comunicacion': 'message-square',
  'comunicación': 'message-square',
  'oficina': 'file-text',
  'ia y agentes': 'sparkles',
  'ides y jetbrains': 'terminal',
  'desarrollo': 'code-2',
  'juegos': 'gamepad-2',
  'runtimes': 'cpu',
  'runtimes y java': 'cpu',
  'java': 'coffee',
  'instaladores locales': 'folder-down',
  'resultados online (winget)': 'globe'
};

const APP_ICON_ALIASES = {
  office2024: 'office',
  office2021: 'office',
  office2019: 'office',
  office2016: 'office',
  codex_cli: 'chatgpt',
  claude_code: 'claude',
  gemini_cli: 'gemini',
  antigravity_ide: 'antigravity',
  antigravity_cli: 'antigravity',
  opencode_cli: 'opencode',
  copilot_app: 'copilot',
  copilot_cli: 'copilot',
  intellij_community: 'intellij',
  pycharm_community: 'pycharm',
  eclipse_jee: 'eclipse',
  eclipse_cpp: 'eclipse',
  devcpp_592: 'devcpp',
  vcredist_aio: 'vcredist',
  vcredist_x86: 'vcredist',
  vcredist_2013_x64: 'vcredist',
  vcredist_2013_x86: 'vcredist',
  vcredist_2012_x64: 'vcredist',
  vcredist_2012_x86: 'vcredist',
  vcredist_2010_x64: 'vcredist',
  vcredist_2010_x86: 'vcredist',
  vcredist_2008_x64: 'vcredist',
  vcredist_2008_x86: 'vcredist',
  vcredist_2005_x64: 'vcredist',
  vcredist_2005_x86: 'vcredist',
  dotnet6: 'dotnet',
  dotnet8: 'dotnet',
  dotnet9: 'dotnet',
  dotnet10: 'dotnet',
  dotnet11: 'dotnet',
  java8: 'java',
  java25: 'java',
  java11_jre: 'temurin',
  java11_jdk: 'temurin',
  java17_jre: 'temurin',
  java17: 'temurin',
  java21_jre: 'temurin',
  java21: 'temurin'
};

function getAppIconSrc(app) {
  if (!app || app.isOnlineSearch) return null;
  const rawId = (app.id || '').toLowerCase().trim();
  if (!rawId || rawId.startsWith('local_')) return null;
  const iconKey = APP_ICON_ALIASES[rawId] || rawId;
  return `icons/${encodeURIComponent(iconKey)}.png`;
}

function renderMiniAppIcon(app) {
  const src = getAppIconSrc(app);
  const catIcon = CATEGORY_ICONS[(app.category || '').toLowerCase()] || 'box';
  if (!src) {
    return `<span class="app-mini-icon fallback"><i data-lucide="${catIcon}" style="width:13px;height:13px;"></i></span>`;
  }
  return `<span class="app-mini-icon"><img src="${src}" alt="" loading="lazy" decoding="async" onerror="this.parentElement.classList.add('fallback'); this.remove();"><i data-lucide="${catIcon}" class="mini-fallback-svg" style="width:13px;height:13px;"></i></span>`;
}

const ESSENTIAL_PRESETS = {
  standard: ['Google Chrome', '7-Zip', 'Everything', 'VLC Media Player', 'SumatraPDF', 'Visual Studio Code', 'Git', 'Notepad++', 'DirectX Runtime', 'Visual C++ 2015-2022 x64'],
  gamer: ['Opera GX', 'Discord', 'OBS Studio', 'Steam', 'Epic Games Launcher', '7-Zip', 'Process Lasso', 'Windhawk', 'DirectX Runtime', 'Visual C++ 2015-2022 x64'],
  dev: ['Visual Studio Code', 'Git', 'GitHub Desktop', 'Notepad++', 'Python 3.12', 'Node.js LTS', 'Everything', 'Microsoft PowerToys', '7-Zip', 'Google Antigravity'],
  tech: ['CrystalDiskInfo', 'CPU-Z', 'WizTree', 'Everything', 'Rufus', 'Ventoy', 'DiskGenius', 'BleachBit', 'Revo Uninstaller', 'Autoruns (Sysinternals)'],
  office: ['Google Chrome', 'Microsoft Office 2024', 'SumatraPDF', 'Zoom Workplace', 'Telegram Desktop', '7-Zip', 'VLC Media Player']
};

let currentPresetName = localStorage.getItem('qinstall_active_preset') || 'standard';
let currentEssentialApps = JSON.parse(localStorage.getItem('qinstall_essentials_list') || 'null');
if (!Array.isArray(currentEssentialApps) || currentEssentialApps.length === 0) {
  currentEssentialApps = [...(ESSENTIAL_PRESETS[currentPresetName] || ESSENTIAL_PRESETS.standard)];
}

function postToHost(payload) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(payload);
  }
}

let iconRafId = null;
function refreshIcons() {
  if (iconRafId) return;
  iconRafId = requestAnimationFrame(() => {
    iconRafId = null;
    if (window.lucide && typeof window.lucide.createIcons === 'function') {
      window.lucide.createIcons();
    }
  });
}

function applyNyanBarPosition(bar, runner, pct) {
  const safe = Math.max(0, Math.min(100, Number(pct) || 0));
  if (safe <= 0.05) {
    if (bar) bar.style.width = '0px';
    if (runner) runner.style.left = '16px';
    return;
  }
  // Anclar el borde derecho del arcoiris exactamente al cuerpo del gato (misma coordenada CSS)
  const pos = `clamp(16px, ${safe.toFixed(2)}%, calc(100% - 16px))`;
  if (bar) bar.style.width = pos;
  if (runner) runner.style.left = pos;
}

function escapeHtml(str) {
  return String(str || '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function encodeJsArg(str) {
  return encodeURIComponent(String(str || '')).replace(/'/g, '%27');
}

/* ==========================================================================
   HOST MESSAGE HANDLER (C# -> JS)
   ========================================================================== */
if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener('message', (ev) => {
    const msg = typeof ev.data === 'string' ? JSON.parse(ev.data) : ev.data;
    if (!msg || !msg.type) return;

    switch (msg.type) {
      case 'loaderStep':
        if (msg.text) loaderState.lastText = msg.text;
        if (msg.pct >= 100) loaderState.backendReady = true;
        break;
      case 'stateUpdate':
        state.apps = msg.apps || [];
        state.env = msg.env || state.env;
        if (msg.storage) state.storage = msg.storage;
        if (msg.profiles && !state.portableProfilesLoaded) {
          state.portableProfilesLoaded = true;
          applyPortableProfiles(msg.profiles);
        }
        updateEnvHeader();
        updateStorageSettingsView();
        if (loaderState.running) {
          // Diferir el renderizado para no trabar la animacion fluida del loader
          setTimeout(() => {
            renderActiveView();
            updateCounter();
          }, 50);
        } else {
          renderActiveView();
          updateCounter();
        }
        const pathEl = document.getElementById('settingsLocalPath');
        if (pathEl && state.env.localDir) pathEl.textContent = state.env.localDir;
        if (!state.isBusy) {
          setStatusText(
            `Listo: ${state.apps.length} apps en catálogo · ${state.env.installedCount || 0} detectadas instaladas` +
            (state.env.outdatedCount > 0 ? ` · ${state.env.outdatedCount} caché(s) desactualizada(s)` : ''),
            0
          );
        }
        break;
      case 'onlineResults':
        if (msg.seq === state.searchSeq) {
          state.onlineResults = msg.items || [];
          state.isSearchingOnline = false;
          renderActiveView();
        }
        break;
      case 'log':
        appendLog(msg.time, msg.message, msg.level);
        break;
      case 'progress':
        setBusyState(!!msg.busy, msg.mode || 'idle');
        setStatusText(msg.statusText || '', msg.pct || 0);
        break;
      case 'downloadPct':
        if (state.isBusy) {
          const curText = document.getElementById('statusMessageText').textContent.replace(/\s*\(\d+%\)$/, '');
          setStatusText(`${curText} (${msg.pct}%)`, msg.pct);
        }
        break;
      case 'appUpdated':
        applySingleAppUpdate(msg);
        break;
      case 'outdatedAlert':
        if (loaderState.running) {
          loaderState.pendingOutdated = msg.items || [];
        } else {
          showOutdatedModal(msg.items || [], false);
        }
        break;
      case 'mandatoryReplacePrompt':
        showOutdatedModal(msg.items || [], true, msg.silent, msg.upgrade);
        break;
      case 'offlinePrompt':
        showOfflinePromptModal(msg.needsInternet || [], msg.offlineCount || 0);
        break;
      case 'drivesList':
        renderExportDrivesList(msg.drives || []);
        break;
      case 'exportFolderPicked':
        if (msg.path) {
          state.exportTargetPath = msg.path;
          const inp = document.getElementById('exportDestInput');
          if (inp) inp.value = msg.path;
          document.querySelectorAll('.drive-card').forEach(el => el.classList.remove('selected'));
        }
        break;
      case 'exportDone':
        handleExportDone(msg);
        break;
      case 'localInstallerPicked':
        handleLocalInstallerPicked(msg);
        break;
      case 'localInstallerSaved':
        handleLocalInstallerSaved(msg);
        break;
      case 'networkUpdate': {
        const wasOnline = !!state.env.hasInternet;
        state.env.hasInternet = !!msg.hasInternet;
        state.env.networkLabel = msg.networkLabel || '';
        updateEnvHeader();
        renderActiveView();
        if (wasOnline !== state.env.hasInternet) {
          if (state.env.hasInternet) {
            const via = state.env.networkLabel ? ` (${state.env.networkLabel})` : '';
            showToast(`Conexión detectada automáticamente${via}.`, 'success');
          } else {
            showToast('Se perdió la conexión — Modo Offline activado automáticamente.', 'warn');
          }
        }
        break;
      }
      case 'summary':
        setBusyState(false, 'idle');
        showSummaryModal(msg.title, msg.headline, msg.items || [], !!msg.hasFailures);
        break;
      case 'toast':
        showToast(msg.message, msg.level || 'info');
        break;
    }
  });
}

/* ==========================================================================
   LOADING OVERLAY (7 SEGUNDOS FLUIDOS A 60 FPS)
   ========================================================================== */
function startSmoothLoader(waitForBackend) {
  if (loaderState.rafId) cancelAnimationFrame(loaderState.rafId);

  const ov = document.getElementById('loadingOverlay');
  const bar = document.getElementById('loaderBar');
  const runner = document.getElementById('loaderNyanRunner');
  const span = document.getElementById('loaderStatusSpan');
  const dots = [0, 1, 2, 3].map(i => document.getElementById('dot' + i));

  if (ov) ov.classList.remove('hidden');
  loaderState.running = true;
  loaderState.startTime = performance.now();
  if (!waitForBackend) loaderState.backendReady = true;

  let lastDot = -1;
  let lastShownText = '';

  function tick(now) {
    const elapsed = now - loaderState.startTime;
    const rawRatio = Math.min(1, elapsed / LOADER_DURATION_MS);
    // Curva suave ease-in-out cubica
    const eased = rawRatio < 0.5
      ? 2 * rawRatio * rawRatio
      : 1 - Math.pow(-2 * rawRatio + 2, 2) / 2;

    let pct = eased * 100;
    if (waitForBackend && !loaderState.backendReady && pct > 95) {
      pct = 95;
    }

    applyNyanBarPosition(bar, runner, pct);

    const dotIdx = pct < 25 ? 0 : (pct < 55 ? 1 : (pct < 85 ? 2 : 3));
    if (dotIdx !== lastDot) {
      lastDot = dotIdx;
      dots.forEach((d, idx) => d && d.classList.toggle('active', idx <= dotIdx));
    }

    let stepText;
    if (pct < 25) {
      stepText = 'Verificando red y Winget runtime...';
    } else if (pct < 55) {
      stepText = state.apps.length > 0
        ? `Cargando catálogo (${state.apps.length} aplicaciones y carpeta local\\)...`
        : 'Cargando catálogo apps.json y carpeta local\\...';
    } else if (pct < 88) {
      stepText = state.apps.length > 0
        ? `Escaneando Windows (${state.env.installedCount || 0} aplicaciones detectadas)...`
        : 'Escaneando aplicaciones instaladas en Windows...';
    } else {
      stepText = '¡Todo listo! Preparando interfaz...';
    }

    if (span && stepText !== lastShownText) {
      lastShownText = stepText;
      span.textContent = stepText;
    }

    if (elapsed >= LOADER_DURATION_MS && (!waitForBackend || loaderState.backendReady)) {
      applyNyanBarPosition(bar, runner, 100);
      loaderState.running = false;
      loaderState.rafId = null;
      setTimeout(() => {
        if (ov) ov.classList.add('hidden');
        if (loaderState.pendingOutdated && loaderState.pendingOutdated.length > 0) {
          const items = loaderState.pendingOutdated;
          loaderState.pendingOutdated = null;
          showOutdatedModal(items, false);
        }
      }, 180);
      return;
    }

    loaderState.rafId = requestAnimationFrame(tick);
  }

  loaderState.rafId = requestAnimationFrame(tick);
}

function replayLoadingScreen() {
  const modal = document.getElementById('settingsBackdrop');
  if (modal) modal.classList.remove('show');
  startSmoothLoader(false);
}

/* ==========================================================================
   HEADER BADGES & THEME SYSTEM
   ========================================================================== */
function updateEnvHeader() {
  const netBadge = document.getElementById('netBadge');
  const netIcon = document.getElementById('netBadgeIcon');
  const diagNet = document.getElementById('diagNetVal');
  if (state.env.hasInternet) {
    const netLabel = state.env.networkLabel ? state.env.networkLabel : 'Internet: Conectado';
    netBadge.className = 'env-badge icon-pill';
    netBadge.title = `${netLabel} (OK)`;
    if (netIcon) netIcon.setAttribute('data-lucide', netLabel.toLowerCase().includes('ethernet') ? 'globe' : 'wifi');
    if (diagNet) diagNet.textContent = state.env.networkLabel ? state.env.networkLabel : 'Conectado (OK)';
  } else {
    netBadge.className = 'env-badge icon-pill offline';
    netBadge.title = 'Sin Internet (Modo Offline)';
    if (netIcon) netIcon.setAttribute('data-lucide', 'wifi-off');
    if (diagNet) diagNet.textContent = 'Sin conexión';
  }

  const wgBadge = document.getElementById('wingetBadge');
  const wgIcon = document.getElementById('wingetBadgeIcon');
  const btnWg = document.getElementById('btnInstallWinget');
  const diagWg = document.getElementById('diagWingetVal');
  if (state.env.wingetAvailable) {
    const wgLabel = state.env.wingetVersion ? `Winget OK (${state.env.wingetVersion})` : 'Winget OK';
    wgBadge.className = 'env-badge icon-pill';
    wgBadge.title = wgLabel;
    if (wgIcon) wgIcon.setAttribute('data-lucide', 'package-check');
    if (diagWg) diagWg.textContent = state.env.wingetVersion || 'Disponible';
    btnWg.style.display = 'none';
  } else {
    wgBadge.className = 'env-badge icon-pill error';
    wgBadge.title = 'Winget no instalado';
    if (wgIcon) wgIcon.setAttribute('data-lucide', 'package-x');
    if (diagWg) diagWg.textContent = 'No instalado';
    btnWg.style.display = state.env.hasInternet ? 'inline-flex' : 'none';
  }
  refreshIcons();
}

function toggleThemeMenu() {
  document.getElementById('themeMenu').classList.toggle('show');
}

function selectTheme(mode) {
  state.themeMode = mode;
  localStorage.setItem('qinstall_theme', mode);
  applyTheme(mode);
  persistPortableProfiles();
  document.getElementById('themeMenu').classList.remove('show');
}

function applyTheme(mode) {
  let effective = mode;
  if (mode === 'system') {
    effective = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  document.documentElement.setAttribute('data-theme', effective);
  postToHost({ action: 'setWindowTheme', dark: effective === 'dark' });

  document.querySelectorAll('.theme-option').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.themeVal === mode);
  });

  const btn = document.getElementById('themeToggleBtn');
  const icon = document.getElementById('currentThemeIcon');
  if (icon) {
    if (mode === 'dark') {
      if (btn) btn.title = 'Tema: Modo Oscuro';
      icon.setAttribute('data-lucide', 'moon');
      icon.style.color = '#60a5fa';
    } else if (mode === 'light') {
      if (btn) btn.title = 'Tema: Modo Claro';
      icon.setAttribute('data-lucide', 'sun');
      icon.style.color = '#f59e0b';
    } else {
      if (btn) btn.title = 'Tema: Sistema';
      icon.setAttribute('data-lucide', 'laptop');
      icon.style.color = '#10b981';
    }
    refreshIcons();
  }
}

/* ==========================================================================
   RENDERING (GRID + DENSE TABLE + ONLINE WINGET SEARCH)
   ========================================================================== */
function getBadgesHtml(app) {
  const src = (app.source || 'winget').toLowerCase();
  if (src === 'powershell') {
    const netBadge = !state.env.hasInternet && app.requiresInternet
      ? { text: 'SIN RED', cls: 'badge-nonet' }
      : (app.requiresInternet
        ? { text: 'ONLINE', cls: 'badge-online' }
        : { text: 'OFFLINE', cls: 'badge-offline' });
    return `<span class="badge-group"><span class="badge badge-tool badge-compact">TOOL</span><span class="badge ${netBadge.cls} badge-compact">${netBadge.text}</span></span>`;
  }

  if (app.installed) return '<span class="badge badge-installed">INSTALADO</span>';
  if (app.outdated) return '<span class="badge badge-update">ACTUALIZAR</span>';
  if (app.hasOffline && !app.requiresInternet) return '<span class="badge badge-offline">DESCARGADO</span>';
  if (!state.env.hasInternet && app.requiresInternet) return '<span class="badge badge-nonet">SIN RED</span>';
  if (src === 'winget') return '<span class="badge badge-online">ONLINE</span>';
  if (src === 'url') return '<span class="badge badge-url">URL</span>';
  return app.requiresInternet
    ? '<span class="badge badge-online">ONLINE</span>'
    : '<span class="badge badge-offline">DESCARGADO</span>';
}

function getAppTooltip(app) {
  if (app.outdated) {
    return `INSTALADOR LOCAL DESACTUALIZADO: tienes v${app.cachedVersion} y existe v${app.latestVersion}. Debes reemplazarlo antes de instalar.`;
  }
  const parts = [];
  if (app.description) parts.push(app.description);
  if (app.offlineBlocked && app.offlineBlockReason) parts.push(`Restricción de espacio: ${app.offlineBlockReason}`);
  if (app.hasOffline && app.cachedVersion) parts.push(`Caché local: v${app.cachedVersion} (funciona sin Internet)`);
  if (app.target) parts.push(`Destino: ${app.target}`);
  if (!app.isOnlineSearch) parts.push('Clic derecho para quitar del catálogo');
  return parts.join('\n');
}

function getFilteredApps() {
  const q = state.searchQuery.toLowerCase().trim();
  if (!q) return state.apps;
  return state.apps.filter(a =>
    (a.name && a.name.toLowerCase().includes(q)) ||
    (a.category && a.category.toLowerCase().includes(q)) ||
    (a.description && a.description.toLowerCase().includes(q)) ||
    (a.target && a.target.toLowerCase().includes(q))
  );
}

function getCategoryFilteredApps(baseList) {
  if (!state.galleryCategory) return [];
  return baseList.filter(a => (a.category || 'Otros') === state.galleryCategory);
}

function getVisibleCatalogApps() {
  return getCategoryFilteredApps(getFilteredApps());
}

function renderCategoryBar(filtered) {
  const catBar = document.getElementById('galleryCatBar');
  if (!catBar) return;

  const combined = [...filtered];
  if (state.searchQuery.trim().length >= 2 && state.onlineResults.length > 0) {
    combined.push(...state.onlineResults);
  }

  const cats = [];
  for (const app of combined) {
    const c = app.category || 'Otros';
    if (!cats.includes(c)) cats.push(c);
  }

  if (cats.length === 0) {
    state.galleryCategory = '';
    catBar.innerHTML = '';
    return;
  }

  if (!cats.includes(state.galleryCategory)) {
    state.galleryCategory = cats[0];
  }

  catBar.innerHTML = cats.map(c => {
    const enc = encodeJsArg(c);
    const active = state.galleryCategory === c ? 'active' : '';
    const iconName = CATEGORY_ICONS[c.toLowerCase()] || 'box';
    return `<button class="gallery-cat-pill ${active}" onclick="selectGalleryCategory(decodeURIComponent('${enc}'))"><i data-lucide="${iconName}" style="width:12px;height:12px;"></i><span>${escapeHtml(c)}</span></button>`;
  }).join('');
}

function renderActiveView() {
  const filtered = getFilteredApps();
  renderCategoryBar(filtered);

  const combined = [...filtered];
  if (state.searchQuery.trim().length >= 2 && state.onlineResults.length > 0) {
    combined.push(...state.onlineResults);
  }
  const visible = getCategoryFilteredApps(combined);

  if (state.viewMode === 'table') {
    renderTableView(visible);
  } else if (state.viewMode === 'gallery') {
    renderGalleryView(visible);
  } else {
    renderCardsView(getCategoryFilteredApps(filtered));
  }
  refreshIcons();
  updateCounter();
}

function selectGalleryCategory(cat) {
  state.galleryCategory = cat;
  renderActiveView();
  const scrollTarget = state.viewMode === 'table'
    ? document.getElementById('appTableContainer')
    : (state.viewMode === 'gallery'
      ? document.getElementById('appGalleryContainer')
      : document.getElementById('appGrid'));
  if (scrollTarget) scrollTarget.scrollTop = 0;
}

function renderGalleryView(items) {
  const grid = document.getElementById('galleryGrid');
  if (!grid) return;

  if (items.length === 0) {
    grid.innerHTML = `<div style="grid-column:1/-1; text-align:center; padding:32px; color:var(--text-muted);">Sin aplicaciones para mostrar.</div>`;
    return;
  }

  grid.innerHTML = items.map(app => {
    const isSel = state.selectedKeys.has(app.key);
    const isOnline = !!app.isOnlineSearch;
    const cat = app.category || 'Otros';
    const catKey = cat.toLowerCase();
    const iconName = CATEGORY_ICONS[catKey] || 'box';
    const [c1, c2] = CATEGORY_GRADIENTS[catKey] || ['#4f46e5', '#8b5cf6'];
    const badgesHtml = getBadgesHtml(app);
    const tip = escapeHtml(getAppTooltip(app));
    const encKey = encodeJsArg(app.key);
    const ctxAttr = isOnline ? '' : `oncontextmenu="openContextMenu(event, decodeURIComponent('${encKey}'))"`;
    const saveBtn = isOnline
      ? `<button class="btn-quick-save" title="Guardar en tu catálogo" onclick="event.stopPropagation(); quickSaveOnline(decodeURIComponent('${encKey}'))">+</button>`
      : '';
    const desc = app.description || app.target || 'Aplicación de Windows';
    const appIconSrc = getAppIconSrc(app);
    const tileHtml = appIconSrc
      ? `<div class="gallery-icon-tile has-app-logo"><img src="${appIconSrc}" alt="" loading="lazy" decoding="async" onerror="this.parentElement.classList.remove('has-app-logo'); this.parentElement.style.background='linear-gradient(135deg, ${c1}, ${c2})'; this.remove();"><i data-lucide="${iconName}" class="gallery-fallback-svg" style="width:24px;height:24px;"></i></div>`
      : `<div class="gallery-icon-tile" style="background: linear-gradient(135deg, ${c1}, ${c2});"><i data-lucide="${iconName}" style="width:24px;height:24px;"></i></div>`;

    return `
      <article class="gallery-card ${isSel ? 'selected' : ''}" data-key="${escapeHtml(app.key)}" title="${tip}"
               onclick="toggleAppSelection(decodeURIComponent('${encKey}'), ${isOnline})" ${ctxAttr}>
        <div class="gallery-banner" style="background: linear-gradient(135deg, ${c1}26, ${c2}1c);">
          ${tileHtml}
          <div class="gallery-check-pos">
            ${saveBtn}
            <div class="custom-checkbox">${CHECK_SVG}</div>
          </div>
        </div>
        <div class="gallery-card-body">
          <div class="gallery-card-title">${escapeHtml(app.name)}</div>
          <div class="gallery-card-desc">${escapeHtml(desc)}</div>
          <div class="gallery-card-footer">
            <span class="gallery-cat-mini">${escapeHtml(cat)}</span>
            ${badgesHtml}
          </div>
        </div>
      </article>`;
  }).join('');
}

function renderAllViews() {
  renderActiveView();
}

function renderCardsView(filtered) {
  const grid = document.getElementById('appGrid');
  const groups = new Map();

  for (const app of filtered) {
    const cat = app.category || 'Otros';
    if (!groups.has(cat)) groups.set(cat, []);
    groups.get(cat).push(app);
  }

  let html = '';
  for (const [catName, list] of groups.entries()) {
    const iconName = CATEGORY_ICONS[catName.toLowerCase()] || 'box';
    html += `
      <section class="category-card">
        <div class="category-header">
          <div class="category-title-wrap">
            <div class="category-icon"><i data-lucide="${iconName}" style="width:15px;height:15px;"></i></div>
            <span class="category-title">${escapeHtml(catName)}</span>
          </div>
          <span class="category-counter">${list.length} ${list.length === 1 ? 'app' : 'apps'}</span>
        </div>
        <div class="app-list">
          ${list.map(app => renderCardRow(app, false)).join('')}
        </div>
      </section>`;
  }

  // Tarjeta de resultados online en Winget cuando el usuario escribe en Buscar:
  const q = state.searchQuery.trim();
  if (q.length >= 2 && state.env.hasInternet && state.env.wingetAvailable) {
    if (state.onlineResults.length > 0) {
      html += `
        <section class="category-card online-search-card">
          <div class="category-header">
            <div class="category-title-wrap">
              <div class="category-icon"><i data-lucide="globe" style="width:15px;height:15px;"></i></div>
              <span class="category-title">Resultados Online (Winget)</span>
            </div>
            <span class="category-counter">${state.onlineResults.length} online</span>
          </div>
          <div class="app-list">
            ${state.onlineResults.map(app => renderCardRow(app, true)).join('')}
          </div>
        </section>`;
    } else if (state.isSearchingOnline) {
      html += `
        <section class="category-card online-search-card">
          <div class="category-header">
            <div class="category-title-wrap">
              <div class="category-icon"><i data-lucide="loader" style="width:15px;height:15px;"></i></div>
              <span class="category-title">Buscando en Winget...</span>
            </div>
          </div>
          <div class="app-list" style="padding:14px; color:var(--text-muted); font-size:12.5px;">
            Consultando "${escapeHtml(q)}" en el repositorio oficial de Winget...
          </div>
        </section>`;
    }
  }

  if (!html) {
    html = `
      <section class="category-card" style="grid-column: 1 / -1; padding: 28px; text-align:center; color:var(--text-muted);">
        Sin coincidencias para "${escapeHtml(q)}".
      </section>`;
  }

  grid.innerHTML = html;
}

function renderCardRow(app, isOnlineSearch) {
  const isSel = state.selectedKeys.has(app.key);
  const badgesHtml = getBadgesHtml(app);
  const tip = escapeHtml(getAppTooltip(app));
  const encodedKey = encodeJsArg(app.key);
  const ctxAttr = isOnlineSearch ? '' : `oncontextmenu="openContextMenu(event, decodeURIComponent('${encodedKey}'))"`;
  const saveBtn = isOnlineSearch
    ? `<button class="btn-quick-save" title="Guardar en tu catálogo" onclick="event.stopPropagation(); quickSaveOnline(decodeURIComponent('${encodedKey}'))">+</button>`
    : '';

  return `
    <div class="app-row ${isSel ? 'selected' : ''}" data-key="${escapeHtml(app.key)}" title="${tip}"
         onclick="toggleAppSelection(decodeURIComponent('${encodedKey}'), ${isOnlineSearch})" ${ctxAttr}>
      <div class="app-left">
        <div class="custom-checkbox">${CHECK_SVG}</div>
        ${renderMiniAppIcon(app)}
        <span class="app-name">${escapeHtml(app.name)}</span>
      </div>
      <div class="app-right">
        ${saveBtn}
        ${badgesHtml}
      </div>
    </div>`;
}

function renderTableView(items) {
  const tbody = document.getElementById('denseTableBody');
  if (!tbody) return;
  if (!items || items.length === 0) {
    tbody.innerHTML = `<tr><td colspan="7" style="text-align:center; padding:24px; color:var(--text-muted);">Sin aplicaciones para mostrar.</td></tr>`;
    return;
  }

  tbody.innerHTML = items.map(app => {
    const isSel = state.selectedKeys.has(app.key);
    const badgesHtml = getBadgesHtml(app);
    const encodedKey = encodeJsArg(app.key);
    const isOnline = !!app.isOnlineSearch;
    const catIcon = CATEGORY_ICONS[(app.category || '').toLowerCase()] || 'box';
    const cacheCol = app.outdated
      ? `v${escapeHtml(app.cachedVersion)} → v${escapeHtml(app.latestVersion)}`
      : (app.cachedVersion ? `v${escapeHtml(app.cachedVersion)}` : (app.hasOffline ? 'Local listo' : '—'));
    const sourceLabel = (app.source || 'winget').toUpperCase();
    const actionLabel = app.outdated ? 'Reemplazar' : (app.installed ? 'Reinstalar' : (isSel ? 'Quitar' : 'Seleccionar'));
    const ctxAttr = isOnline ? '' : `oncontextmenu="openContextMenu(event, decodeURIComponent('${encodedKey}'))"`;

    return `
      <tr class="table-row ${isSel ? 'selected' : ''}" data-key="${escapeHtml(app.key)}"
          onclick="toggleAppSelection(decodeURIComponent('${encodedKey}'), ${isOnline})" ${ctxAttr}>
        <td style="text-align:center;">
          <div class="custom-checkbox" style="margin:auto;">${CHECK_SVG}</div>
        </td>
        <td><div class="table-app-cell">${renderMiniAppIcon(app)}<span class="table-app-name">${escapeHtml(app.name)}</span></div></td>
        <td>
          <span class="table-cat-badge">
            <i data-lucide="${catIcon}" style="width:12px;height:12px;"></i>
            <span>${escapeHtml(app.category || 'Otros')}</span>
          </span>
        </td>
        <td class="table-mono">${cacheCol}</td>
        <td class="table-mono">${escapeHtml(sourceLabel)}</td>
        <td>${badgesHtml}</td>
        <td style="text-align:right;">
          <button class="table-action-btn" onclick="event.stopPropagation(); toggleAppSelection(decodeURIComponent('${encodedKey}'), ${isOnline})">${actionLabel}</button>
        </td>
      </tr>`;
  }).join('');
}

function applySingleAppUpdate(msg) {
  const app = state.apps.find(a => a.key === msg.key);
  if (app) {
    app.installed = !!msg.installed;
    app.hasOffline = !!msg.hasOffline;
    app.requiresInternet = !!msg.requiresInternet;
    app.cachedVersion = msg.cachedVersion || '';
    if (typeof msg.cachedSizeBytes === 'number') app.cachedSizeBytes = msg.cachedSizeBytes;
    app.outdated = !!msg.outdated;
  }
  if (msg.uncheck) {
    state.selectedKeys.delete(msg.key);
    state.selectedOnlineMap.delete(msg.key);
  }
  renderAllViews();
  updateStorageSettingsView();
  updateCounter();
}

/* ==========================================================================
   SELECTION, ESSENTIALS PROFILES & VIEW SWITCHING
   ========================================================================== */
function toggleAppSelection(key, isOnlineSearch) {
  if (state.isBusy) return;
  if (state.selectedKeys.has(key)) {
    state.selectedKeys.delete(key);
    state.selectedOnlineMap.delete(key);
  } else {
    state.selectedKeys.add(key);
    if (isOnlineSearch) {
      const found = state.onlineResults.find(a => a.key === key);
      if (found) state.selectedOnlineMap.set(key, found);
    }
  }
  syncSelectionVisuals();
  updateCounter();
}

function syncSelectionVisuals() {
  document.querySelectorAll('.app-row, .table-row, .gallery-card').forEach(el => {
    const k = el.getAttribute('data-key');
    const isSel = state.selectedKeys.has(k);
    el.classList.toggle('selected', isSel);
    if (el.classList.contains('table-row')) {
      const btn = el.querySelector('.table-action-btn');
      if (btn && (btn.textContent === 'Seleccionar' || btn.textContent === 'Quitar')) {
        btn.textContent = isSel ? 'Quitar' : 'Seleccionar';
      }
    }
  });
}

function updateCounter() {
  document.getElementById('selectedCount').textContent = state.selectedKeys.size;
  const visible = getVisibleCatalogApps();
  const hdrChk = document.getElementById('tableHeaderCheck');
  if (hdrChk) {
    hdrChk.checked = visible.length > 0 && visible.every(a => state.selectedKeys.has(a.key));
  }
}

function switchView(mode, persist = true) {
  state.viewMode = mode;
  localStorage.setItem('qinstall_view', mode);
  document.getElementById('appGrid').style.display = mode === 'cards' ? 'grid' : 'none';
  document.getElementById('appGalleryContainer').style.display = mode === 'gallery' ? 'flex' : 'none';
  document.getElementById('appTableContainer').style.display = mode === 'table' ? 'flex' : 'none';
  document.getElementById('viewBtnCards').classList.toggle('active', mode === 'cards');
  document.getElementById('viewBtnGallery').classList.toggle('active', mode === 'gallery');
  document.getElementById('viewBtnTable').classList.toggle('active', mode === 'table');
  if (state.apps.length > 0) {
    renderActiveView();
    if (persist) persistPortableProfiles();
  }
}

function toggleEssentials() {
  if (state.isBusy) return;
  const btn = document.getElementById('filterEssentials');
  if (btn.classList.contains('active')) {
    clearSelection();
    btn.classList.remove('active');
    return;
  }
  state.selectedKeys.clear();
  state.selectedOnlineMap.clear();
  for (const app of state.apps) {
    if (currentEssentialApps.includes(app.name) && !app.installed) {
      state.selectedKeys.add(app.key);
    }
  }
  btn.classList.add('active');
  syncSelectionVisuals();
  updateCounter();
}

function toggleEssentialsMenu(e) {
  if (e) e.stopPropagation();
  const menu = document.getElementById('essentialsMenu');
  menu.classList.toggle('show');
  if (menu.classList.contains('show')) {
    renderEssentialsPicker();
    updatePresetBadge();
  }
}

function renderEssentialsPicker() {
  const picker = document.getElementById('essentialsPicker');
  const qEl = document.getElementById('essentialsSearchInput');
  const q = qEl ? qEl.value.toLowerCase().trim() : '';
  const list = q
    ? state.apps.filter(a => (a.name && a.name.toLowerCase().includes(q)) || (a.category && a.category.toLowerCase().includes(q)))
    : state.apps;

  picker.innerHTML = list.map(app => {
    const checked = currentEssentialApps.includes(app.name);
    const enc = encodeJsArg(app.name);
    return `
      <label class="essentials-picker-item">
        <input type="checkbox" ${checked ? 'checked' : ''} onchange="togglePickerApp(decodeURIComponent('${enc}'), this.checked)">
        <span>${escapeHtml(app.name)}</span>
      </label>`;
  }).join('');
  document.getElementById('essentialsCountLabel').textContent = `${currentEssentialApps.length} apps`;
}

function togglePickerApp(name, checked) {
  if (checked) {
    if (!currentEssentialApps.includes(name)) currentEssentialApps.push(name);
  } else {
    currentEssentialApps = currentEssentialApps.filter(n => n !== name);
  }
  currentPresetName = 'Personalizado';
  updatePresetBadge();
  document.getElementById('essentialsCountLabel').textContent = `${currentEssentialApps.length} apps`;
}

function applyEssentialPreset(presetKey) {
  if (!ESSENTIAL_PRESETS[presetKey]) return;
  currentPresetName = presetKey;
  currentEssentialApps = [...ESSENTIAL_PRESETS[presetKey]];
  localStorage.setItem('qinstall_active_preset', presetKey);
  localStorage.setItem('qinstall_essentials_list', JSON.stringify(currentEssentialApps));
  persistPortableProfiles();
  updatePresetBadge();
  renderEssentialsPicker();
  document.getElementById('filterEssentials').classList.remove('active');
  toggleEssentials();
  document.getElementById('essentialsMenu').classList.remove('show');
  showToast(`Perfil "${presetKey}" aplicado y guardado en profiles.json (${state.selectedKeys.size} por instalar).`, 'info');
}

function saveCustomEssentials() {
  localStorage.setItem('qinstall_active_preset', currentPresetName);
  localStorage.setItem('qinstall_essentials_list', JSON.stringify(currentEssentialApps));
  persistPortableProfiles();
  document.getElementById('filterEssentials').classList.remove('active');
  toggleEssentials();
  document.getElementById('essentialsMenu').classList.remove('show');
  showToast(`Esenciales guardados en profiles.json (${currentEssentialApps.length} apps).`, 'success');
}

function resetEssentialsToDefault() {
  applyEssentialPreset('standard');
}

function updatePresetBadge() {
  const badge = document.getElementById('currentPresetBadge');
  if (badge) badge.textContent = currentPresetName.charAt(0).toUpperCase() + currentPresetName.slice(1);
  document.querySelectorAll('.btn-preset').forEach(b => {
    b.classList.toggle('active', b.dataset.preset === currentPresetName);
  });
}

function toggleAllFromHeader(chk) {
  if (state.isBusy) return;
  if (chk.checked) {
    const visible = getVisibleCatalogApps();
    for (const app of visible) state.selectedKeys.add(app.key);
    document.getElementById('filterEssentials').classList.remove('active');
    syncSelectionVisuals();
    updateCounter();
  } else {
    clearSelection();
  }
}

function clearSelection() {
  if (state.isBusy) return;
  state.selectedKeys.clear();
  state.selectedOnlineMap.clear();
  document.getElementById('filterEssentials').classList.remove('active');
  syncSelectionVisuals();
  updateCounter();
}

/* ==========================================================================
   HOST ACTIONS (INSTALL, OFFLINE DOWNLOAD, SEARCH, LOCAL, SETTINGS)
   ========================================================================== */
function getSelectionPayload() {
  const keys = [];
  const onlineApps = [];
  for (const k of state.selectedKeys) {
    if (state.selectedOnlineMap.has(k)) {
      onlineApps.push(state.selectedOnlineMap.get(k));
    } else {
      keys.push(k);
    }
  }
  return { keys, onlineApps };
}

function getSelectedAppsInfo() {
  const list = [];
  for (const k of state.selectedKeys) {
    if (state.selectedOnlineMap.has(k)) {
      list.push(state.selectedOnlineMap.get(k));
    } else {
      const found = state.apps.find(a => a.key === k);
      if (found) list.push(found);
    }
  }
  return list;
}

function startOrCancelInstall() {
  if (state.isBusy) {
    postToHost({ action: 'cancel' });
    setStatusText('Cancelando operación...', parseInt(document.getElementById('statusPct').textContent, 10) || 0);
    return;
  }

  const apps = getSelectedAppsInfo();
  if (apps.length === 0) {
    showToast('Selecciona al menos una aplicación para instalar.', 'warn');
    return;
  }

  const silent = document.getElementById('chkSilent').checked;
  const upgrade = document.getElementById('chkUpgrade').checked;
  const rows = apps.map(a => {
    const modeTag = a.hasOffline && !a.requiresInternet ? 'DESCARGADO' : (a.source === 'powershell' ? 'TOOL' : 'ONLINE');
    return `<div class="dialog-row-ok">• ${escapeHtml(a.name)} <span style="opacity:0.65;">[${modeTag}]</span></div>`;
  }).join('');

  openDialogModal(
    'Confirmar instalación',
    `<p style="font-size:13px; color:var(--text-secondary);">¿Estás seguro de que deseas <b>instalar ${apps.length} ${apps.length === 1 ? 'aplicación' : 'aplicaciones'}</b> en este equipo?<br><span style="font-size:11.5px; color:var(--text-muted);">Instalación silenciosa: <b>${silent ? 'Sí' : 'No'}</b> · Actualizar si ya existe: <b>${upgrade ? 'Sí' : 'No'}</b></span></p><div class="dialog-list">${rows}</div>`,
    `<button class="btn-header" onclick="closeDialogModal()">Cancelar</button>
     <button class="btn-install" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="executeConfirmedInstall()">Sí, instalar ahora (${apps.length})</button>`
  );
}

function executeConfirmedInstall() {
  closeDialogModal();
  const sel = getSelectionPayload();
  postToHost({
    action: 'installSelected',
    keys: sel.keys,
    onlineApps: sel.onlineApps,
    silent: document.getElementById('chkSilent').checked,
    upgrade: document.getElementById('chkUpgrade').checked
  });
}

function startOfflineDownload() {
  if (state.isBusy) return;

  const apps = getSelectedAppsInfo();
  if (apps.length === 0) {
    showToast('Marca las aplicaciones que deseas descargar para tu pendrive.', 'warn');
    return;
  }

  let eligibleCount = 0;
  const rows = apps.map(a => {
    const isTool = a.source === 'powershell';
    const isBlocked = !!a.offlineBlocked;
    if (!isTool && !isBlocked) eligibleCount++;
    const cls = (isTool || isBlocked) ? 'dialog-row-warn' : 'dialog-row-ok';
    const note = isBlocked
      ? ` (omitida: ${escapeHtml(a.offlineBlockReason || 'solo instalación online por espacio')})`
      : (isTool ? ' (se omitirá: herramienta interactiva)' : '');
    return `<div class="${cls}">• ${escapeHtml(a.name)}${note}</div>`;
  }).join('');

  const confirmBtn = eligibleCount > 0
    ? `<button class="btn-offline-dl" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="executeConfirmedOfflineDownload()">Sí, descargar offline (${eligibleCount})</button>`
    : '';

  const intro = eligibleCount > 0
    ? `¿Estás seguro de que deseas <b>descargar los instaladores</b> de ${eligibleCount} ${eligibleCount === 1 ? 'aplicación' : 'aplicaciones'} en <b>local\\cache\\</b> para llevarlos offline en tu almacenamiento?<br><span style="font-size:11.5px; color:var(--text-muted);">No se instalarán en esta PC; solo se guardarán los archivos instaladores permitidos.</span>`
    : `Ninguna de las aplicaciones seleccionadas puede guardarse en modo offline en un pendrive por <b>restricción de espacio</b> o por ser interactiva. Puedes instalarlas normalmente con <b>Instalar seleccionadas</b> cuando tengas Internet.`;

  openDialogModal(
    'Confirmar descarga offline',
    `<p style="font-size:13px; color:var(--text-secondary);">${intro}</p><div class="dialog-list">${rows}</div>`,
    `<button class="btn-header" onclick="closeDialogModal()">${eligibleCount > 0 ? 'Cancelar' : 'Entendido'}</button>${confirmBtn}`
  );
}

function executeConfirmedOfflineDownload() {
  closeDialogModal();
  const sel = getSelectionPayload();
  postToHost({
    action: 'downloadOffline',
    keys: sel.keys,
    onlineApps: sel.onlineApps
  });
}

function quickSaveOnline(key) {
  const app = state.onlineResults.find(a => a.key === key);
  if (!app) return;
  state.onlineResults = state.onlineResults.filter(a => a.key !== key);
  state.selectedOnlineMap.delete(key);
  postToHost({ action: 'quickSaveOnline', app });
}

function requestAddLocalInstaller() {
  if (state.isBusy) {
    showToast('Espera a que termine la operación actual.', 'warn');
    return;
  }
  const modal = document.getElementById('addLocalBackdrop');
  if (!modal) return;

  // Reset campos y poblar categorias existentes + Instaladores Locales
  document.getElementById('addLocalFilePath').value = '';
  document.getElementById('addLocalFileMeta').textContent = 'Ningún archivo seleccionado';
  document.getElementById('addLocalName').value = '';
  document.getElementById('addLocalDesc').value = '';
  document.getElementById('addLocalSilentArgs').value = '';
  document.getElementById('addLocalPreset').value = '';
  document.getElementById('addLocalCopyChk').checked = true;
  document.getElementById('addLocalEssential').checked = false;

  const sel = document.getElementById('addLocalCategory');
  const cats = ['Instaladores Locales'];
  for (const app of state.apps) {
    const c = app.category || 'Otros';
    if (!cats.includes(c)) cats.push(c);
  }
  sel.innerHTML = cats.map(c => `<option value="${escapeHtml(c)}">${escapeHtml(c)}</option>`).join('');
  sel.value = 'Instaladores Locales';

  modal.classList.add('show');
  refreshIcons();
}

function closeAddLocalModal() {
  const modal = document.getElementById('addLocalBackdrop');
  if (modal) modal.classList.remove('show');
}

function applySilentArgPreset(val) {
  if (val) document.getElementById('addLocalSilentArgs').value = val;
}

function handleLocalInstallerPicked(msg) {
  if (!msg || !msg.path) return;
  document.getElementById('addLocalFilePath').value = msg.path;
  document.getElementById('addLocalFileMeta').textContent = `${msg.fileName || ''} · ${formatBytes(msg.sizeBytes || 0)}`;
  const nameInp = document.getElementById('addLocalName');
  if (nameInp && !nameInp.value.trim() && msg.suggestedName) {
    nameInp.value = msg.suggestedName;
  }
  const argsInp = document.getElementById('addLocalSilentArgs');
  if (argsInp && !argsInp.value.trim() && msg.suggestedSilentArgs) {
    argsInp.value = msg.suggestedSilentArgs;
  }
}

function submitAddLocalInstaller() {
  const filePath = document.getElementById('addLocalFilePath').value.trim();
  const name = document.getElementById('addLocalName').value.trim();
  const category = document.getElementById('addLocalCategory').value;
  const description = document.getElementById('addLocalDesc').value.trim();
  const silentArgs = document.getElementById('addLocalSilentArgs').value.trim();
  const copyToLocal = document.getElementById('addLocalCopyChk').checked;
  const essential = document.getElementById('addLocalEssential').checked;

  if (!filePath) {
    showToast('Primero selecciona un archivo .exe o .msi con Examinar.', 'warn');
    return;
  }
  if (!name) {
    showToast('Escribe un nombre para mostrar.', 'warn');
    return;
  }

  postToHost({
    action: 'saveLocalInstaller',
    filePath,
    name,
    category,
    description,
    silentArgs,
    copyToLocal,
    essential
  });
}

function handleLocalInstallerSaved(msg) {
  if (msg && msg.success) {
    closeAddLocalModal();
    showToast(msg.message || 'Instalador local agregado correctamente.', 'success');
  } else {
    showToast((msg && msg.message) || 'No se pudo agregar el instalador local.', 'warn');
  }
}
function requestRefresh() { postToHost({ action: 'refresh' }); }
function requestInstallWinget() { postToHost({ action: 'installWinget' }); }
function requestRunAsAdmin() { postToHost({ action: 'runAsAdmin' }); }

const progressSim = {
  rafId: null,
  lastTs: 0,
  displayPct: 0,
  floorPct: 0,
  ceilingPct: 0
};

function renderFooterProgressFrame(pct) {
  const bar = document.getElementById('statusProgress');
  const runner = document.getElementById('nyanRunner');
  const pctEl = document.getElementById('statusPct');
  applyNyanBarPosition(bar, runner, pct);
  if (pctEl) pctEl.textContent = Math.round(pct) + '%';
}

function tickFooterProgress(now) {
  if (!progressSim.lastTs) progressSim.lastTs = now;
  const dt = Math.min(0.1, (now - progressSim.lastTs) / 1000);
  progressSim.lastTs = now;

  // 1. Si el progreso real del backend es mayor que el mostrado, deslizar suavemente hacia arriba
  if (progressSim.displayPct < progressSim.floorPct) {
    const diff = progressSim.floorPct - progressSim.displayPct;
    progressSim.displayPct += Math.max(diff * Math.min(1, dt * 8.5), dt * 22);
    if (progressSim.displayPct > progressSim.floorPct) {
      progressSim.displayPct = progressSim.floorPct;
    }
  } else if (state.isBusy && progressSim.displayPct < progressSim.ceilingPct) {
    // 2. Mientras descarga/instala en segundo plano, avanzar poco a poco (1% -> 2% -> ...) sin quedarse congelado
    const remaining = progressSim.ceilingPct - progressSim.displayPct;
    // Velocidad inicial ~1.5%/seg que desacelera asintóticamente al acercarse al techo del paso actual
    const speed = Math.max(0.22, Math.min(1.6, remaining * 0.055));
    progressSim.displayPct = Math.min(progressSim.ceilingPct, progressSim.displayPct + speed * dt);
  }

  renderFooterProgressFrame(progressSim.displayPct);

  const stillCatchingUp = Math.abs(progressSim.floorPct - progressSim.displayPct) > 0.15;
  if (state.isBusy || stillCatchingUp) {
    progressSim.rafId = requestAnimationFrame(tickFooterProgress);
  } else {
    renderFooterProgressFrame(progressSim.floorPct);
    progressSim.rafId = null;
    progressSim.lastTs = 0;
  }
}

function ensureFooterProgressLoop() {
  if (!progressSim.rafId) {
    progressSim.lastTs = 0;
    progressSim.rafId = requestAnimationFrame(tickFooterProgress);
  }
}

function setBusyState(busy, mode) {
  const wasBusy = state.isBusy;
  state.isBusy = busy;
  const btnInstall = document.getElementById('btnInstall');
  const lbl = document.getElementById('btnInstallLabel');
  const btnDl = document.getElementById('btnDownloadOffline');

  if (busy) {
    if (!wasBusy) {
      progressSim.displayPct = 0;
      progressSim.floorPct = 1;
      progressSim.ceilingPct = 90;
    }
    btnInstall.classList.add('cancel-mode');
    lbl.textContent = 'Cancelar';
    btnDl.style.display = 'none';
    ensureFooterProgressLoop();
  } else {
    btnInstall.classList.remove('cancel-mode');
    lbl.textContent = 'Instalar seleccionadas';
    btnDl.style.display = 'inline-flex';
  }
}

function setStatusText(text, pct) {
  const span = document.getElementById('statusMessageText');
  if (span && text) span.textContent = text;
  if (typeof pct !== 'number') return;

  const safe = Math.max(0, Math.min(100, pct));

  if (!state.isBusy) {
    if (safe === 0) {
      progressSim.displayPct = 0;
      progressSim.floorPct = 0;
      progressSim.ceilingPct = 0;
      if (progressSim.rafId) {
        cancelAnimationFrame(progressSim.rafId);
        progressSim.rafId = null;
      }
      renderFooterProgressFrame(0);
      return;
    }
    progressSim.floorPct = safe;
    progressSim.ceilingPct = safe;
    ensureFooterProgressLoop();
    return;
  }

  // Calcular el tramo del paso actual si el texto incluye "(i/N)" (ej. "Instalando (1/3): ...")
  let stepCeiling = Math.min(94, Math.max(safe + 18, 90));
  const m = String(text || '').match(/\((\d+)\s*\/\s*(\d+)\)/);
  if (m) {
    const idx = parseInt(m[1], 10);
    const total = Math.max(1, parseInt(m[2], 10));
    const sliceStart = ((idx - 1) / total) * 100;
    const sliceEnd = (idx / total) * 100;
    stepCeiling = sliceStart + (sliceEnd - sliceStart) * 0.9;
  }

  // Arrancar al menos en 1% al iniciar una operación para dar feedback visual inmediato
  const effectiveFloor = Math.max(safe, progressSim.displayPct < 1 ? 1 : safe);
  if (effectiveFloor > progressSim.displayPct) {
    progressSim.floorPct = effectiveFloor;
  } else {
    progressSim.floorPct = Math.max(progressSim.floorPct, safe);
  }
  progressSim.ceilingPct = Math.max(progressSim.floorPct, Math.min(96, stepCeiling));
  ensureFooterProgressLoop();
}

function showSilentParamsHelpModal() {
  openDialogModal(
    '¿Qué son los parámetros silenciosos?',
    `<div style="font-size:13px; color:var(--text-secondary); display:flex; flex-direction:column; gap:10px; line-height:1.5;">
      <p>La <b>Instalación silenciosa</b> permite que los programas se instalen automáticamente en segundo plano sin abrir asistentes ni pedirte pulsar <i>«Siguiente → Acepto → Instalar → Finalizar»</i>.</p>
      <div class="dialog-list" style="font-size:11.5px;">
        <div class="dialog-row-ok">• <b>Apps de Winget:</b> qInstall aplica <code>--silent</code> automáticamente (no necesitas configurar nada).</div>
        <div class="dialog-row-ok">• <b>Instaladores descargados offline:</b> qInstall lee el parámetro oficial del manifiesto <code>.yaml</code> automáticamente.</div>
        <div class="dialog-row-warn">• <b>Tus propios archivos .exe / .msi (+ Local):</b> cada fabricante usa un comando distinto:</div>
        <div style="padding-left:12px; color:var(--text-secondary);">
          <code>/S</code> → NSIS (7-Zip, VLC, Notepad++, Dev-C++)<br>
          <code>/VERYSILENT /NORESTART</code> → Inno Setup<br>
          <code>/qn /norestart</code> → Paquetes <code>.msi</code> (qInstall lo pone solo)<br>
          <code>/quiet /norestart</code> → Instaladores Microsoft
        </div>
      </div>
      <p style="font-size:11.5px; color:var(--text-muted);">Si desmarcas <b>Instalación silenciosa</b> abajo, verás las ventanas normales del instalador (útil si quieres cambiar la carpeta de destino de un programa).</p>
    </div>`,
    `<button class="btn-install" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="closeDialogModal()">Entendido</button>`
  );
}

/* ==========================================================================
   CONSOLE, CONTEXT MENU, MODALS & TOASTS
   ========================================================================== */
function toggleConsole() {
  document.getElementById('consoleDrawer').classList.toggle('open');
}

function appendLog(time, message, level) {
  const body = document.getElementById('consoleBody');
  const div = document.createElement('div');
  div.className = level === 'success' ? 'log-success' : (level === 'warn' ? 'log-warn' : 'log-info');
  div.textContent = `[${time}] ${message}`;
  body.appendChild(div);
  while (body.childElementCount > 400) {
    body.removeChild(body.firstChild);
  }
  body.scrollTop = body.scrollHeight;
}

function formatBytes(bytes) {
  const n = Number(bytes || 0);
  if (n <= 0) return '0 MB';
  if (n >= 1024 * 1024 * 1024) return (n / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
  if (n >= 1024 * 1024) return (n / (1024 * 1024)).toFixed(1) + ' MB';
  return Math.max(1, Math.round(n / 1024)) + ' KB';
}

function persistPortableProfiles() {
  postToHost({
    action: 'saveProfiles',
    activePreset: currentPresetName,
    customApps: currentEssentialApps,
    silent: document.getElementById('chkSilent').checked,
    upgrade: document.getElementById('chkUpgrade').checked,
    theme: state.themeMode,
    viewMode: state.viewMode
  });
}

function applyPortableProfiles(p) {
  if (!p) return;
  if (p.activePreset) {
    currentPresetName = p.activePreset;
    localStorage.setItem('qinstall_active_preset', currentPresetName);
  }
  if (Array.isArray(p.customApps) && p.customApps.length > 0) {
    currentEssentialApps = [...p.customApps];
    localStorage.setItem('qinstall_essentials_list', JSON.stringify(currentEssentialApps));
  }
  if (typeof p.silent === 'boolean') document.getElementById('chkSilent').checked = p.silent;
  if (typeof p.upgrade === 'boolean') document.getElementById('chkUpgrade').checked = p.upgrade;
  if (p.theme && (p.theme === 'dark' || p.theme === 'light' || p.theme === 'system')) {
    state.themeMode = p.theme;
    localStorage.setItem('qinstall_theme', p.theme);
    applyTheme(p.theme);
  }
  if (p.viewMode && (p.viewMode === 'cards' || p.viewMode === 'gallery' || p.viewMode === 'table')) {
    switchView(p.viewMode, false);
  }
}

function updateStorageSettingsView() {
  const drvEl = document.getElementById('storageDriveLabel');
  const cacheEl = document.getElementById('storageCacheLabel');
  const freeEl = document.getElementById('storageFreeLabel');
  const fillEl = document.getElementById('storageBarFill');
  const listEl = document.getElementById('cachedAppsManager');
  if (!drvEl || !listEl) return;

  const st = state.storage || {};
  const total = Number(st.driveTotalBytes || 0);
  const free = Number(st.driveFreeBytes || 0);
  const used = Math.max(0, total - free);
  const usedPct = total > 0 ? Math.min(100, Math.round((used / total) * 100)) : 0;

  drvEl.textContent = st.driveName
    ? `${st.driveTypeLabel || 'Unidad'}: ${st.driveName}`
    : 'Unidad local';
  cacheEl.textContent = `Instaladores descargados: ${formatBytes(st.cacheTotalBytes || 0)}`;
  freeEl.textContent = total > 0 ? `Libre: ${formatBytes(free)} de ${formatBytes(total)} (${usedPct}% usado)` : 'Espacio no disponible';
  if (fillEl) fillEl.style.width = usedPct + '%';

  const cachedApps = state.apps.filter(a => a.hasOffline && Number(a.cachedSizeBytes || 0) > 0);
  if (cachedApps.length === 0) {
    listEl.innerHTML = '<div style="font-size:11.5px; color:var(--text-muted); padding:6px;">No hay instaladores descargados en local\\cache\\.</div>';
    return;
  }

  cachedApps.sort((a, b) => (b.cachedSizeBytes || 0) - (a.cachedSizeBytes || 0));
  listEl.innerHTML = cachedApps.map(app => {
    const enc = encodeJsArg(app.key);
    const ver = app.cachedVersion ? `v${escapeHtml(app.cachedVersion)} · ` : '';
    const staleTag = app.outdated ? ' · OBSOLETO' : '';
    return `
      <div class="cached-item">
        <div class="cached-item-info">
          <span class="cached-item-name">${escapeHtml(app.name)}</span>
          <span class="cached-item-sub">${ver}${formatBytes(app.cachedSizeBytes)}${staleTag}</span>
        </div>
        <button class="cached-del-btn" onclick="deleteSingleCachedApp(decodeURIComponent('${enc}'))">
          <i data-lucide="trash-2" style="width:11px;height:11px;"></i> Eliminar
        </button>
      </div>`;
  }).join('');
  refreshIcons();
}

function deleteSingleCachedApp(key) {
  if (state.isBusy) return;
  postToHost({ action: 'deleteSingleCache', key });
}

function confirmUpgradeAll() {
  if (state.isBusy) return;
  if (!state.env.hasInternet) {
    showToast('Se requiere Internet para actualizar las aplicaciones instaladas.', 'warn');
    return;
  }
  if (!state.env.wingetAvailable) {
    showToast('Winget no está disponible en este equipo.', 'warn');
    return;
  }
  openDialogModal(
    'Actualizar todas las aplicaciones',
    `<p style="font-size:13px; color:var(--text-secondary);">¿Deseas buscar y aplicar automáticamente todas las actualizaciones disponibles en este equipo mediante <b>winget upgrade --all</b>?<br><span style="font-size:11.5px; color:var(--text-muted);">Puedes ver el progreso en tiempo real en <b>Ver Registro</b> o cancelar en cualquier momento.</span></p>`,
    `<button class="btn-header" onclick="closeDialogModal()">Cancelar</button>
     <button class="btn-install" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="executeConfirmedUpgradeAll()">Sí, actualizar todo</button>`
  );
}

function executeConfirmedUpgradeAll() {
  closeDialogModal();
  postToHost({
    action: 'upgradeAll',
    silent: document.getElementById('chkSilent').checked
  });
}

function confirmCreateRestorePoint() {
  if (state.isBusy) return;
  document.getElementById('settingsBackdrop').classList.remove('show');
  openDialogModal(
    'Crear punto de restauración',
    `<p style="font-size:13px; color:var(--text-secondary);">Se creará un <b>punto de restauración de Windows</b> antes de instalar drivers u optimizadores (funciona 100% sin Internet).</p>`,
    `<button class="btn-header" onclick="closeDialogModal()">Cancelar</button>
     <button class="btn-install" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="closeDialogModal(); postToHost({ action: 'createRestorePoint' })">Crear ahora</button>`
  );
}

function confirmUninstallApp(key) {
  const app = state.apps.find(a => a.key === key);
  if (!app || state.isBusy) return;
  const enc = encodeJsArg(key);
  openDialogModal(
    'Confirmar desinstalación',
    `<p style="font-size:13px; color:var(--text-secondary);">¿Estás seguro de que deseas <b>desinstalar "${escapeHtml(app.name)}"</b> de este equipo vía Winget?</p>`,
    `<button class="btn-header" onclick="closeDialogModal()">Cancelar</button>
     <button class="btn-install cancel-mode" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="closeDialogModal(); postToHost({ action: 'uninstallApp', key: decodeURIComponent('${enc}'), silent: document.getElementById('chkSilent').checked })">Sí, desinstalar</button>`
  );
}

function openContextMenu(e, key) {
  e.preventDefault();
  if (state.isBusy) return;
  const app = state.apps.find(a => a.key === key);
  if (!app) return;
  state.contextKey = key;

  const canUninstall = !!app.installed && (app.source || 'winget') === 'winget';
  const hasCache = !!app.hasOffline && Number(app.cachedSizeBytes || 0) > 0;

  const unBtn = document.getElementById('ctxUninstallBtn');
  const dcBtn = document.getElementById('ctxDeleteCacheBtn');
  const sep = document.getElementById('ctxExtraSep');

  unBtn.style.display = canUninstall ? 'flex' : 'none';
  if (canUninstall) {
    document.getElementById('ctxUninstallLabel').textContent = `Desinstalar "${app.name}"`;
  }

  dcBtn.style.display = hasCache ? 'flex' : 'none';
  if (hasCache) {
    document.getElementById('ctxDeleteCacheLabel').textContent = `Borrar descargado (${formatBytes(app.cachedSizeBytes)})`;
  }

  sep.style.display = (canUninstall || hasCache) ? 'block' : 'none';
  document.getElementById('ctxRemoveLabel').textContent = `Quitar "${app.name}" del catálogo`;

  const menu = document.getElementById('appContextMenu');
  const menuHeight = 46 + (canUninstall ? 36 : 0) + (hasCache ? 36 : 0);
  menu.style.left = Math.min(e.clientX, window.innerWidth - 260) + 'px';
  menu.style.top = Math.min(e.clientY, window.innerHeight - menuHeight - 12) + 'px';
  menu.classList.add('show');
}

document.getElementById('ctxUninstallBtn').addEventListener('click', () => {
  const k = state.contextKey;
  document.getElementById('appContextMenu').classList.remove('show');
  if (k) confirmUninstallApp(k);
});

document.getElementById('ctxDeleteCacheBtn').addEventListener('click', () => {
  const k = state.contextKey;
  document.getElementById('appContextMenu').classList.remove('show');
  if (k) deleteSingleCachedApp(k);
});

document.getElementById('ctxRemoveBtn').addEventListener('click', () => {
  const k = state.contextKey;
  document.getElementById('appContextMenu').classList.remove('show');
  if (!k) return;
  const app = state.apps.find(a => a.key === k);
  const appName = app ? app.name : 'esta aplicación';
  const enc = encodeJsArg(k);
  openDialogModal(
    'Quitar del catálogo',
    `<p style="font-size:13px; color:var(--text-secondary);">¿Seguro que deseas quitar <b>"${escapeHtml(appName)}"</b> de tu catálogo <b>apps.json</b>?</p>`,
    `<button class="btn-header" onclick="closeDialogModal()">Cancelar</button>
     <button class="btn-install cancel-mode" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="executeRemoveCatalogApp(decodeURIComponent('${enc}'))">Sí, quitar</button>`
  );
});

function executeRemoveCatalogApp(key) {
  closeDialogModal();
  state.selectedKeys.delete(key);
  postToHost({ action: 'removeApp', key });
}

function toggleSettingsModal() {
  const modal = document.getElementById('settingsBackdrop');
  if (!modal.classList.contains('show')) {
    document.getElementById('setSilent').checked = document.getElementById('chkSilent').checked;
    document.getElementById('setUpgrade').checked = document.getElementById('chkUpgrade').checked;
    updateStorageSettingsView();
  }
  modal.classList.toggle('show');
}

function saveSettings() {
  const silent = document.getElementById('setSilent').checked;
  const upgrade = document.getElementById('setUpgrade').checked;
  document.getElementById('chkSilent').checked = silent;
  document.getElementById('chkUpgrade').checked = upgrade;
  localStorage.setItem('qinstall_prefs', JSON.stringify({ silent, upgrade }));
  persistPortableProfiles();
  toggleSettingsModal();
  showToast('Ajustes guardados en profiles.json.', 'success');
}

function confirmPurgeOfflineCache() {
  document.getElementById('settingsBackdrop').classList.remove('show');
  document.getElementById('exportBackdrop').classList.remove('show');
  openDialogModal(
    'Eliminar instaladores descargados',
    `<p style="font-size:13px; color:var(--text-secondary);">Se eliminarán todos los instaladores descargados con <b>Descargar offline</b> en <b>local\\cache\\</b>. Los instaladores que agregaste manualmente en <b>local\\</b> y <b>office\\</b> se conservarán.</p>`,
    `<button class="btn-header" onclick="closeDialogModal()">Cancelar</button>
     <button class="btn-install cancel-mode" style="height:34px;padding:0 16px;font-size:12.5px;" onclick="closeDialogModal(); postToHost({ action: 'purgeCache', includeOfflineCache: true })">Eliminar descargados</button>`
  );
}

function showOutdatedModal(items, isMandatoryBlock, silent, upgrade) {
  if (!items || items.length === 0) return;
  const rows = items.map(it =>
    `<div class="dialog-row-warn">• ${escapeHtml(it.name)}: descargado v${escapeHtml(it.localVersion)} → nueva v${escapeHtml(it.latestVersion)}</div>`
  ).join('');

  const keys = items.map(it => it.key);
  const title = isMandatoryBlock
    ? 'Instaladores desactualizados — Purga requerida'
    : 'Nueva versión detectada para instaladores descargados';
  const desc = isMandatoryBlock
    ? 'Para un buen funcionamiento, <b>debes purgar y reemplazar</b> estos instaladores descargados antiguos antes de instalar:'
    : 'Salió una nueva versión para estos instaladores descargados en tu pendrive. Conviene purgar los antiguos o reemplazarlos ahora para buen funcionamiento:';

  const purgeOnlyBtn = `<button class="btn-header" onclick="closeDialogModal(); postToHost({ action: 'purgeOutdated', keys: ${escapeHtml(JSON.stringify(keys))} })">Purgar antiguos</button>`;

  const actionBtn = isMandatoryBlock
    ? `<button class="btn-install" style="height:34px;padding:0 16px;font-size:12.5px;" onclick="confirmMandatoryReplace(${!!silent}, ${!!upgrade})">Purgar, reemplazar e instalar</button>`
    : `<button class="btn-offline-dl" style="height:34px;padding:0 16px;font-size:12.5px;" onclick="closeDialogModal(); postToHost({ action: 'replaceOutdated', keys: ${escapeHtml(JSON.stringify(keys))} })">Purgar y descargar nuevos</button>`;

  openDialogModal(
    title,
    `<p style="font-size:13px; color:var(--text-secondary);">${desc}</p><div class="dialog-list">${rows}</div>`,
    `<button class="btn-header" onclick="closeDialogModal()">${isMandatoryBlock ? 'Cancelar' : 'Después'}</button>${purgeOnlyBtn}${actionBtn}`
  );
}

function confirmMandatoryReplace(silent, upgrade) {
  closeDialogModal();
  const sel = getSelectionPayload();
  postToHost({
    action: 'installSelected',
    keys: sel.keys,
    onlineApps: sel.onlineApps,
    silent,
    upgrade,
    forceReplace: true
  });
}

function showOfflinePromptModal(needsInternet, offlineCount) {
  const rows = needsInternet.map(n => `<div class="dialog-row-err">• ${escapeHtml(n)} (requiere Internet)</div>`).join('');
  const footer = offlineCount > 0
    ? `<button class="btn-header" onclick="closeDialogModal()">Cancelar</button>
       <button class="btn-install" style="height:34px;padding:0 16px;font-size:12.5px;" onclick="confirmOfflineOnlyInstall()">Instalar solo las ${offlineCount} offline</button>`
    : `<button class="btn-header" onclick="closeDialogModal()">Entendido</button>`;

  openDialogModal(
    'Sin conexión a Internet',
    `<p style="font-size:13px; color:var(--text-secondary);">No se detectó conexión en este equipo. Estas apps requieren Internet y no se pueden instalar offline:</p><div class="dialog-list">${rows}</div>`,
    footer
  );
}

function confirmOfflineOnlyInstall() {
  closeDialogModal();
  const sel = getSelectionPayload();
  postToHost({
    action: 'installSelected',
    keys: sel.keys,
    onlineApps: sel.onlineApps,
    silent: document.getElementById('chkSilent').checked,
    upgrade: document.getElementById('chkUpgrade').checked,
    skipOnline: true
  });
}

function showSummaryModal(title, headline, items, hasFailures) {
  const rows = items.map(it => {
    const cls = it.status === 'ok' || it.status === 'already' ? 'dialog-row-ok' : (it.status === 'cancelled' ? 'dialog-row-warn' : 'dialog-row-err');
    const sym = it.status === 'ok' ? '[OK]' : (it.status === 'already' ? '[=]' : (it.status === 'cancelled' ? '[!]' : '[X]'));
    return `<div class="${cls}">${sym} ${escapeHtml(it.name)}: ${escapeHtml(it.message)}</div>`;
  }).join('');

  openDialogModal(
    title || 'Resumen',
    `<p style="font-size:13px; font-weight:600; color:var(--text-primary);">${escapeHtml(headline)}</p><div class="dialog-list">${rows}</div>`,
    `<button class="btn-install" style="height:34px;padding:0 18px;font-size:12.5px;" onclick="closeDialogModal()">Aceptar</button>`
  );
}

function openDialogModal(title, bodyHtml, footerHtml) {
  document.getElementById('dialogTitle').textContent = title;
  document.getElementById('dialogBody').innerHTML = bodyHtml;
  document.getElementById('dialogFooter').innerHTML = footerHtml;
  document.getElementById('dialogBackdrop').classList.add('show');
  refreshIcons();
}

function openPortableExportModal() {
  if (state.isBusy) {
    showToast('Espera a que termine la operación actual antes de abrir Almacenamiento.', 'warn');
    return;
  }
  const modal = document.getElementById('exportBackdrop');
  if (!modal) return;
  updateStorageSettingsView();
  modal.classList.add('show');
  postToHost({ action: 'listDrives' });
  refreshIcons();
}

function closePortableExportModal() {
  const modal = document.getElementById('exportBackdrop');
  if (modal) modal.classList.remove('show');
}

function renderExportDrivesList(drives) {
  const listEl = document.getElementById('exportDrivesList');
  const inp = document.getElementById('exportDestInput');
  if (!listEl) return;

  if (!drives || drives.length === 0) {
    listEl.innerHTML = '<div style="font-size:12px; color:var(--text-muted); padding:8px;">No se detectaron unidades listas. Conecta tu pendrive o disco externo y pulsa <b>Re-escanear</b>, o usa <b>Examinar...</b></div>';
    return;
  }

  let autoSelected = '';
  const firstUsb = drives.find(d => d.isUsb && !d.isCurrent && d.meetsMin !== false)
    || drives.find(d => !d.isCurrent && d.meetsMin !== false);
  if (firstUsb && (!inp.value || !inp.value.trim())) {
    autoSelected = firstUsb.suggestedPath;
    inp.value = autoSelected;
  } else if (inp && inp.value) {
    autoSelected = inp.value;
  }

  listEl.innerHTML = drives.map(d => {
    const encPath = encodeJsArg(d.suggestedPath);
    const meetsMin = d.meetsMin !== false;
    const meetsRec = d.meetsRecommended !== false;
    const isSel = meetsMin && autoSelected && autoSelected.toLowerCase() === String(d.suggestedPath || '').toLowerCase();
    const iconName = d.isUsb ? 'usb' : 'hard-drive';
    const iconCls = d.isUsb ? 'usb' : 'hdd';
    const badgeCls = !meetsMin ? 'blocked' : (!meetsRec ? 'tight' : (d.isUsb ? 'usb' : 'hdd'));
    const badgeText = !meetsMin
      ? '< 2 GB · NO APTO'
      : (!meetsRec ? 'AJUSTADO (< 4 GB)' : (d.isUsb ? 'PENDRIVE USB' : 'DISCO DURO'));
    const currentNote = d.isCurrent ? ' · Unidad actual' : '';
    const clickAttr = meetsMin
      ? `onclick="selectExportDrive(this, decodeURIComponent('${encPath}'))"`
      : `onclick="showToast('Esta unidad tiene menos de 2 GB requeridos (4 GB recomendados). No se puede instalar qInstall aquí.', 'warn')"`;
    return `
      <div class="drive-card ${isSel ? 'selected' : ''} ${meetsMin ? '' : 'disabled'}" data-path="${escapeHtml(d.suggestedPath)}" ${clickAttr}>
        <div class="drive-card-left">
          <div class="drive-icon-box ${iconCls}"><i data-lucide="${iconName}" style="width:17px;height:17px;"></i></div>
          <div>
            <div class="drive-name">${escapeHtml(d.label)} (${escapeHtml(d.letter)})${currentNote}</div>
            <div class="drive-sub">Libre: ${formatBytes(d.freeBytes)} de ${formatBytes(d.totalBytes)} → ${escapeHtml(d.suggestedPath)}</div>
          </div>
        </div>
        <span class="drive-badge ${badgeCls}">${badgeText}</span>
      </div>`;
  }).join('');
  refreshIcons();
}

function selectExportDrive(el, suggestedPath) {
  document.querySelectorAll('.drive-card').forEach(c => c.classList.remove('selected'));
  if (el) el.classList.add('selected');
  const inp = document.getElementById('exportDestInput');
  if (inp) inp.value = suggestedPath;
}

function startPortableExport() {
  if (state.isBusy) return;
  const inp = document.getElementById('exportDestInput');
  const dest = inp ? inp.value.trim() : '';
  if (!dest) {
    showToast('Selecciona un pendrive, disco externo o carpeta destino.', 'warn');
    return;
  }
  const includeCache = document.getElementById('exportIncludeCache').checked;
  const includeOffice = document.getElementById('exportIncludeOffice').checked;
  closePortableExportModal();
  postToHost({
    action: 'exportPortable',
    destinationPath: dest,
    includeCache,
    includeOffice
  });
}

function handleExportDone(msg) {
  if (msg.success) {
    const encPath = encodeJsArg(msg.path || '');
    openDialogModal(
      'qInstall copiado a la unidad',
      `<p style="font-size:13px; color:var(--text-secondary);"><b>¡Listo para llevar!</b> Se copió la versión portable limpia en:<br><code style="font-family:'Cascadia Code', monospace; color:var(--primary);">${escapeHtml(msg.path)}</code></p><div class="dialog-list"><div class="dialog-row-ok">• Archivos copiados: ${msg.filesCopied || 0} (${formatBytes(msg.totalBytes || 0)})</div><div class="dialog-row-ok">• Incluye: Qinstall.exe, WebView2 DLLs, catálogo, perfiles, íconos offline y carpetas seleccionadas</div></div>`,
      `<button class="btn-header" onclick="closeDialogModal()">Cerrar</button>
       <button class="btn-install" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="closeDialogModal(); postToHost({ action: 'openFolder', path: decodeURIComponent('${encPath}') })">Abrir carpeta</button>`
    );
    showToast('qInstall copiado correctamente a la unidad.', 'success');
  } else {
    openDialogModal(
      'No se pudo copiar a la unidad',
      `<p style="font-size:13px; color:var(--status-nonet-text);">${escapeHtml(msg.message || 'Error desconocido al copiar archivos.')}</p>`,
      `<button class="btn-header" onclick="closeDialogModal()">Entendido</button>`
    );
  }
}

function closeDialogModal() {
  document.getElementById('dialogBackdrop').classList.remove('show');
}

function showToast(message, level = 'info') {
  const cont = document.getElementById('toastContainer');
  const div = document.createElement('div');
  div.className = `toast ${level}`;
  div.textContent = message;
  cont.appendChild(div);
  setTimeout(() => {
    div.style.opacity = '0';
    div.style.transition = 'opacity 0.25s ease';
    setTimeout(() => div.remove(), 260);
  }, 3400);
}

/* ==========================================================================
   SEARCH INPUT & KEYBOARD SHORTCUTS
   ========================================================================== */
const searchInput = document.getElementById('searchInput');
searchInput.addEventListener('input', () => {
  clearTimeout(state.searchTimer);
  state.searchQuery = searchInput.value;
  const q = state.searchQuery.trim();

  if (q.length < 2) {
    state.searchSeq++;
    state.onlineResults = [];
    state.isSearchingOnline = false;
    renderAllViews();
    return;
  }

  state.isSearchingOnline = state.env.hasInternet && state.env.wingetAvailable;
  renderAllViews();

  if (state.isSearchingOnline) {
    state.searchTimer = setTimeout(() => {
      const seq = ++state.searchSeq;
      postToHost({ action: 'searchWinget', query: q, seq });
    }, 380);
  }
});

searchInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') {
    e.preventDefault();
    clearTimeout(state.searchTimer);
    const q = searchInput.value.trim();
    if (q.length >= 2 && state.env.hasInternet && state.env.wingetAvailable) {
      state.isSearchingOnline = true;
      renderAllViews();
      const seq = ++state.searchSeq;
      postToHost({ action: 'searchWinget', query: q, seq });
    }
  } else if (e.key === 'Escape') {
    searchInput.value = '';
    searchInput.dispatchEvent(new Event('input'));
    searchInput.blur();
  }
});

document.addEventListener('keydown', (e) => {
  if (e.key === '/' && document.activeElement.tagName !== 'INPUT' && document.activeElement.tagName !== 'SELECT') {
    e.preventDefault();
    searchInput.focus();
    searchInput.select();
  } else if (e.key === 'Escape') {
    document.getElementById('settingsBackdrop').classList.remove('show');
    document.getElementById('exportBackdrop').classList.remove('show');
    document.getElementById('addLocalBackdrop').classList.remove('show');
    document.getElementById('dialogBackdrop').classList.remove('show');
    document.getElementById('themeMenu').classList.remove('show');
    document.getElementById('essentialsMenu').classList.remove('show');
    document.getElementById('appContextMenu').classList.remove('show');
  }
});

document.addEventListener('click', (e) => {
  if (!document.getElementById('themeDropdownWrap').contains(e.target)) {
    document.getElementById('themeMenu').classList.remove('show');
  }
  if (!document.getElementById('essentialsWrap').contains(e.target)) {
    document.getElementById('essentialsMenu').classList.remove('show');
  }
  document.getElementById('appContextMenu').classList.remove('show');
});

window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
  if (state.themeMode === 'system') applyTheme('system');
});

/* ==========================================================================
   BOOT
   ========================================================================== */
(function boot() {
  try {
    const savedPrefs = JSON.parse(localStorage.getItem('qinstall_prefs') || 'null');
    if (savedPrefs) {
      document.getElementById('chkSilent').checked = savedPrefs.silent !== false;
      document.getElementById('chkUpgrade').checked = savedPrefs.upgrade !== false;
    }
  } catch {}

  applyTheme(state.themeMode);
  switchView(state.viewMode, false);
  refreshIcons();
  startSmoothLoader(true);
  postToHost({ action: 'init' });
})();
