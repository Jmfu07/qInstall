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
  galleryCategory: 'Todas',
  themeMode: localStorage.getItem('qinstall_theme') || 'dark',
  contextKey: null
};

const CATEGORY_GRADIENTS = {
  'navegadores': ['#2563eb', '#06b6d4'],
  'utilidades': ['#4f46e5', '#8b5cf6'],
  'optimizacion': ['#d97706', '#f59e0b'],
  'arranque y reparacion': ['#dc2626', '#f97316'],
  'multimedia': ['#db2777', '#ec4899'],
  'comunicacion': ['#059669', '#10b981'],
  'oficina': ['#0284c7', '#3b82f6'],
  'ia y agentes': ['#7c3aed', '#06b6d4'],
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
  'multimedia': 'film',
  'comunicacion': 'message-square',
  'comunicación': 'message-square',
  'oficina': 'file-text',
  'ia y agentes': 'sparkles',
  'desarrollo': 'code-2',
  'juegos': 'gamepad-2',
  'runtimes': 'cpu',
  'runtimes y java': 'cpu',
  'java': 'coffee',
  'instaladores locales': 'folder-down',
  'resultados online (winget)': 'globe'
};

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

function refreshIcons() {
  if (window.lucide && typeof window.lucide.createIcons === 'function') {
    window.lucide.createIcons();
  }
}

function escapeHtml(str) {
  return String(str || '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
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
        updateEnvHeader();
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

    if (bar) bar.style.width = pct.toFixed(2) + '%';
    if (runner) runner.style.left = `clamp(16px, ${pct.toFixed(2)}%, calc(100% - 16px))`;

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
      if (bar) bar.style.width = '100%';
      if (runner) runner.style.left = '100%';
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
  const netText = document.getElementById('netBadgeText');
  if (state.env.hasInternet) {
    netBadge.className = 'env-badge';
    netText.textContent = 'Internet: OK';
  } else {
    netBadge.className = 'env-badge offline';
    netText.textContent = 'Sin Internet (Offline)';
  }

  const wgBadge = document.getElementById('wingetBadge');
  const wgText = document.getElementById('wingetBadgeText');
  const btnWg = document.getElementById('btnInstallWinget');
  if (state.env.wingetAvailable) {
    wgBadge.className = 'env-badge';
    wgText.textContent = state.env.wingetVersion ? `Winget OK (${state.env.wingetVersion})` : 'Winget OK';
    btnWg.style.display = 'none';
  } else {
    wgBadge.className = 'env-badge error';
    wgText.textContent = 'Sin Winget';
    btnWg.style.display = state.env.hasInternet ? 'inline-flex' : 'none';
  }
}

function toggleThemeMenu() {
  document.getElementById('themeMenu').classList.toggle('show');
}

function selectTheme(mode) {
  state.themeMode = mode;
  localStorage.setItem('qinstall_theme', mode);
  applyTheme(mode);
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

  const label = document.getElementById('currentThemeLabel');
  const icon = document.getElementById('currentThemeIcon');
  if (label && icon) {
    if (mode === 'dark') {
      label.textContent = 'Modo Oscuro';
      icon.setAttribute('data-lucide', 'moon');
      icon.style.color = '#60a5fa';
    } else if (mode === 'light') {
      label.textContent = 'Modo Claro';
      icon.setAttribute('data-lucide', 'sun');
      icon.style.color = '#f59e0b';
    } else {
      label.textContent = 'Sistema';
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

function renderActiveView() {
  const filtered = getFilteredApps();
  if (state.viewMode === 'table') {
    renderTableView(filtered);
  } else if (state.viewMode === 'gallery') {
    renderGalleryView(filtered);
  } else {
    renderCardsView(filtered);
  }
  refreshIcons();
}

function selectGalleryCategory(cat) {
  state.galleryCategory = cat;
  renderActiveView();
}

function renderGalleryView(filtered) {
  const catBar = document.getElementById('galleryCatBar');
  const grid = document.getElementById('galleryGrid');
  if (!catBar || !grid) return;

  const combined = [...filtered];
  if (state.searchQuery.trim().length >= 2 && state.onlineResults.length > 0) {
    combined.push(...state.onlineResults);
  }

  const cats = ['Todas'];
  for (const app of combined) {
    const c = app.category || 'Otros';
    if (!cats.includes(c)) cats.push(c);
  }
  if (!cats.includes(state.galleryCategory)) {
    state.galleryCategory = 'Todas';
  }

  catBar.innerHTML = cats.map(c => {
    const enc = encodeURIComponent(c);
    const active = state.galleryCategory === c ? 'active' : '';
    const iconName = c === 'Todas' ? 'layers' : (CATEGORY_ICONS[c.toLowerCase()] || 'box');
    return `<button class="gallery-cat-pill ${active}" onclick="selectGalleryCategory(decodeURIComponent('${enc}'))"><i data-lucide="${iconName}" style="width:12px;height:12px;"></i><span>${escapeHtml(c)}</span></button>`;
  }).join('');

  const items = state.galleryCategory === 'Todas'
    ? combined
    : combined.filter(a => (a.category || 'Otros') === state.galleryCategory);

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
    const encKey = encodeURIComponent(app.key);
    const ctxAttr = isOnline ? '' : `oncontextmenu="openContextMenu(event, decodeURIComponent('${encKey}'))"`;
    const saveBtn = isOnline
      ? `<button class="btn-quick-save" title="Guardar en tu catálogo" onclick="event.stopPropagation(); quickSaveOnline(decodeURIComponent('${encKey}'))">+</button>`
      : '';
    const desc = app.description || app.target || 'Aplicación de Windows';

    return `
      <article class="gallery-card ${isSel ? 'selected' : ''}" data-key="${escapeHtml(app.key)}" title="${tip}"
               onclick="toggleAppSelection(decodeURIComponent('${encKey}'), ${isOnline})" ${ctxAttr}>
        <div class="gallery-banner" style="background: linear-gradient(135deg, ${c1}26, ${c2}1c);">
          <div class="gallery-icon-tile" style="background: linear-gradient(135deg, ${c1}, ${c2});">
            <i data-lucide="${iconName}" style="width:24px;height:24px;"></i>
          </div>
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
  const encodedKey = encodeURIComponent(app.key);
  const ctxAttr = isOnlineSearch ? '' : `oncontextmenu="openContextMenu(event, decodeURIComponent('${encodedKey}'))"`;
  const saveBtn = isOnlineSearch
    ? `<button class="btn-quick-save" title="Guardar en tu catálogo" onclick="event.stopPropagation(); quickSaveOnline(decodeURIComponent('${encodedKey}'))">+</button>`
    : '';

  return `
    <div class="app-row ${isSel ? 'selected' : ''}" data-key="${escapeHtml(app.key)}" title="${tip}"
         onclick="toggleAppSelection(decodeURIComponent('${encodedKey}'), ${isOnlineSearch})" ${ctxAttr}>
      <div class="app-left">
        <div class="custom-checkbox">${CHECK_SVG}</div>
        <span class="app-name">${escapeHtml(app.name)}</span>
      </div>
      <div class="app-right">
        ${saveBtn}
        ${badgesHtml}
      </div>
    </div>`;
}

function renderTableView(filtered) {
  const tbody = document.getElementById('denseTableBody');
  const combined = [...filtered];
  if (state.searchQuery.trim().length >= 2 && state.onlineResults.length > 0) {
    combined.push(...state.onlineResults);
  }

  tbody.innerHTML = combined.map(app => {
    const isSel = state.selectedKeys.has(app.key);
    const badgesHtml = getBadgesHtml(app);
    const encodedKey = encodeURIComponent(app.key);
    const isOnline = !!app.isOnlineSearch;
    const catIcon = CATEGORY_ICONS[(app.category || '').toLowerCase()] || 'box';
    const cacheCol = app.outdated
      ? `v${escapeHtml(app.cachedVersion)} → v${escapeHtml(app.latestVersion)}`
      : (app.cachedVersion ? `v${escapeHtml(app.cachedVersion)}` : (app.hasOffline ? 'Local listo' : '—'));
    const sourceLabel = (app.source || 'winget').toUpperCase();
    const actionLabel = app.outdated ? 'Reemplazar' : (app.installed ? 'Reinstalar' : (isSel ? 'Quitar' : 'Seleccionar'));

    return `
      <tr class="table-row ${isSel ? 'selected' : ''}" data-key="${escapeHtml(app.key)}"
          onclick="toggleAppSelection(decodeURIComponent('${encodedKey}'), ${isOnline})">
        <td style="text-align:center;">
          <div class="custom-checkbox" style="margin:auto;">${CHECK_SVG}</div>
        </td>
        <td><span class="table-app-name">${escapeHtml(app.name)}</span></td>
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
    app.outdated = !!msg.outdated;
  }
  if (msg.uncheck) {
    state.selectedKeys.delete(msg.key);
    state.selectedOnlineMap.delete(msg.key);
  }
  renderAllViews();
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
    el.classList.toggle('selected', state.selectedKeys.has(k));
  });
}

function updateCounter() {
  document.getElementById('selectedCount').textContent = state.selectedKeys.size;
  const visible = getFilteredApps();
  const hdrChk = document.getElementById('tableHeaderCheck');
  if (hdrChk) {
    hdrChk.checked = visible.length > 0 && visible.every(a => state.selectedKeys.has(a.key));
  }
}

function switchView(mode) {
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
  document.getElementById('filterAll').classList.remove('active');
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
  picker.innerHTML = state.apps.map(app => {
    const checked = currentEssentialApps.includes(app.name);
    const enc = encodeURIComponent(app.name);
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
  updatePresetBadge();
  renderEssentialsPicker();
  document.getElementById('filterEssentials').classList.remove('active');
  toggleEssentials();
  document.getElementById('essentialsMenu').classList.remove('show');
  showToast(`Perfil "${presetKey}" aplicado (${state.selectedKeys.size} por instalar).`, 'info');
}

function saveCustomEssentials() {
  localStorage.setItem('qinstall_active_preset', currentPresetName);
  localStorage.setItem('qinstall_essentials_list', JSON.stringify(currentEssentialApps));
  document.getElementById('filterEssentials').classList.remove('active');
  toggleEssentials();
  document.getElementById('essentialsMenu').classList.remove('show');
  showToast(`Esenciales guardados (${currentEssentialApps.length} apps).`, 'success');
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

function toggleAll() {
  if (state.isBusy) return;
  const visible = getFilteredApps();
  const btn = document.getElementById('filterAll');
  if (btn.classList.contains('active')) {
    clearSelection();
    return;
  }
  for (const app of visible) state.selectedKeys.add(app.key);
  btn.classList.add('active');
  document.getElementById('filterEssentials').classList.remove('active');
  syncSelectionVisuals();
  updateCounter();
}

function toggleAllFromHeader(chk) {
  if (chk.checked) {
    document.getElementById('filterAll').classList.remove('active');
    toggleAll();
  } else {
    clearSelection();
  }
}

function clearSelection() {
  if (state.isBusy) return;
  state.selectedKeys.clear();
  state.selectedOnlineMap.clear();
  document.getElementById('filterAll').classList.remove('active');
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

  const rows = apps.map(a => {
    const isTool = a.source === 'powershell';
    const cls = isTool ? 'dialog-row-warn' : 'dialog-row-ok';
    const note = isTool ? ' (se omitirá: solo interactiva)' : '';
    return `<div class="${cls}">• ${escapeHtml(a.name)}${note}</div>`;
  }).join('');

  openDialogModal(
    'Confirmar descarga offline',
    `<p style="font-size:13px; color:var(--text-secondary);">¿Estás seguro de que deseas <b>descargar los instaladores</b> de estas ${apps.length} ${apps.length === 1 ? 'opción' : 'opciones'} en tu carpeta <b>local\\cache\\</b> para llevarlos offline en el pendrive?<br><span style="font-size:11.5px; color:var(--text-muted);">No se instalarán en esta PC; solo se guardarán los archivos instaladores.</span></p><div class="dialog-list">${rows}</div>`,
    `<button class="btn-header" onclick="closeDialogModal()">Cancelar</button>
     <button class="btn-offline-dl" style="height:36px;padding:0 18px;font-size:12.5px;" onclick="executeConfirmedOfflineDownload()">Sí, descargar offline (${apps.length})</button>`
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

function requestAddLocalInstaller() { postToHost({ action: 'addLocalInstaller' }); }
function requestRefresh() { postToHost({ action: 'refresh' }); }
function requestInstallWinget() { postToHost({ action: 'installWinget' }); }
function requestRunAsAdmin() { postToHost({ action: 'runAsAdmin' }); }

function setBusyState(busy, mode) {
  state.isBusy = busy;
  const btnInstall = document.getElementById('btnInstall');
  const lbl = document.getElementById('btnInstallLabel');
  const btnDl = document.getElementById('btnDownloadOffline');

  if (busy) {
    btnInstall.classList.add('cancel-mode');
    lbl.textContent = 'Cancelar';
    btnDl.style.display = 'none';
  } else {
    btnInstall.classList.remove('cancel-mode');
    lbl.textContent = 'Instalar seleccionadas';
    btnDl.style.display = 'inline-flex';
  }
}

function setStatusText(text, pct) {
  const span = document.getElementById('statusMessageText');
  const bar = document.getElementById('statusProgress');
  const runner = document.getElementById('nyanRunner');
  const pctEl = document.getElementById('statusPct');
  if (span && text) span.textContent = text;
  if (typeof pct === 'number') {
    const safe = Math.max(0, Math.min(100, pct));
    if (bar) bar.style.width = safe + '%';
    if (runner) runner.style.left = `clamp(16px, ${safe}%, calc(100% - 16px))`;
    if (pctEl) pctEl.textContent = safe + '%';
  }
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
  body.scrollTop = body.scrollHeight;
}

function openContextMenu(e, key) {
  e.preventDefault();
  if (state.isBusy) return;
  const app = state.apps.find(a => a.key === key);
  if (!app) return;
  state.contextKey = key;
  document.getElementById('ctxRemoveLabel').textContent = `Quitar "${app.name}" del catálogo`;
  const menu = document.getElementById('appContextMenu');
  menu.style.left = Math.min(e.clientX, window.innerWidth - 220) + 'px';
  menu.style.top = Math.min(e.clientY, window.innerHeight - 50) + 'px';
  menu.classList.add('show');
}

document.getElementById('ctxRemoveBtn').addEventListener('click', () => {
  const k = state.contextKey;
  document.getElementById('appContextMenu').classList.remove('show');
  if (k) {
    state.selectedKeys.delete(k);
    postToHost({ action: 'removeApp', key: k });
  }
});

function toggleSettingsModal() {
  const modal = document.getElementById('settingsBackdrop');
  if (!modal.classList.contains('show')) {
    document.getElementById('setSilent').checked = document.getElementById('chkSilent').checked;
    document.getElementById('setUpgrade').checked = document.getElementById('chkUpgrade').checked;
  }
  modal.classList.toggle('show');
}

function saveSettings() {
  const silent = document.getElementById('setSilent').checked;
  const upgrade = document.getElementById('setUpgrade').checked;
  document.getElementById('chkSilent').checked = silent;
  document.getElementById('chkUpgrade').checked = upgrade;
  localStorage.setItem('qinstall_prefs', JSON.stringify({ silent, upgrade }));
  toggleSettingsModal();
  showToast('Ajustes guardados.', 'success');
}

function confirmPurgeOfflineCache() {
  toggleSettingsModal();
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
  switchView(state.viewMode);
  refreshIcons();
  startSmoothLoader(true);
  postToHost({ action: 'init' });
})();
