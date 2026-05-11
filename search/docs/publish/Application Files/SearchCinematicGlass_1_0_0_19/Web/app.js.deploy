const $ = (q, root = document) => root.querySelector(q);
const $$ = (q, root = document) => [...root.querySelectorAll(q)];

const state = {
  searchPath: '',
  savePath: '',
  running: false,
  found: 0,
  processed: 0,
  total: 0,
  skipped: 0,
  lastReport: '',
  theme: localStorage.getItem('theme') || 'dark',
  density: localStorage.getItem('density') || 'comfortable'
};

function post(type, payload = {}) {
  if (window.chrome?.webview) window.chrome.webview.postMessage({ type, payload });
}

function toast(message) {
  const el = $('#toast');
  el.textContent = message;
  el.classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => el.classList.remove('show'), 2800);
}

function addLog(message, time = new Date().toLocaleTimeString()) {
  const log = $('#log');
  log.textContent += `[${time}] ${message}\n`;
  log.scrollTop = log.scrollHeight;
}

function setTheme(theme) {
  state.theme = theme;
  localStorage.setItem('theme', theme);
  document.body.classList.toggle('dark', theme === 'dark');
  document.body.classList.toggle('light', theme !== 'dark');
  $('#themeToggle').textContent = theme === 'dark' ? 'Light' : 'Dark';
  $('#themeSelect').value = theme;
}

function setDensity(density) {
  state.density = density;
  localStorage.setItem('density', density);
  document.body.classList.toggle('compact', density === 'compact');
  $('#densitySelect').value = density;
}

function switchTab(tab) {
  $$('.tab-panel').forEach(p => p.classList.toggle('active', p.dataset.panel === tab));
  $$('.nav-item,.dock-btn,.dock-logo').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));

  const titles = {
    dashboard: ['Dashboard', 'Cinematic File Search', 'Поиск файлов и папок по ключевым словам с красивым отчётом.'],
    setup: ['Setup', 'Параметры поиска', 'Выберите папки и настройте поведение приложения.'],
    results: ['Results', 'Результаты', 'Найденные файлы и папки отображаются в адаптивной таблице.'],
    reports: ['Reports', 'Отчёты', 'Открывайте последние отчёты и папку сохранения.'],
    settings: ['Settings', 'Настройки', 'Тема, плотность интерфейса и внешний вид.']
  };
  const t = titles[tab] || titles.dashboard;
  $('#floorName').textContent = t[0];
  $('#pageTitle').textContent = t[1];
  $('#pageSubtitle').textContent = t[2];
}

function updatePaths() {
  $('#searchPathMini').textContent = state.searchPath || 'Не выбрана';
  $('#searchPathFull').textContent = state.searchPath || 'Не выбрана';
  $('#savePathMini').textContent = state.savePath || 'По умолчанию';
  $('#savePathFull').textContent = state.savePath || 'По умолчанию';
}

function updateMetrics() {
  $('#metricFound').textContent = state.found;
  $('#metricProcessed').textContent = state.processed;
  $('#metricTotal').textContent = `из ${state.total}`;
  $('#metricSkipped').textContent = state.skipped;
  $('#badgeFound').textContent = state.found;
  $('#badgeProcessed').textContent = state.processed;
  $('#statFound').textContent = state.found;
  $('#statSkipped').textContent = state.skipped;
}

function setProgress(percent, processed = state.processed, total = state.total, found = state.found) {
  state.processed = processed;
  state.total = total;
  state.found = found;
  $('#progressRing').style.setProperty('--progress', percent);
  $('#progressText').textContent = `${percent}%`;
  $('#progressTitle').textContent = state.running ? 'Поиск выполняется' : 'Готово';
  $('#progressSub').textContent = `${processed} / ${total} элементов, найдено ${found}`;
  updateMetrics();
}

function renderChips() {
  const words = parseWords($('#wordsInput').value);
  $('#wordChips').innerHTML = words.map(w => `<span class="chip">${escapeHtml(w)}</span>`).join('');
}

function parseWords(text) {
  return text.split(/[\s,.;:\n\r\t]+/).map(x => x.trim()).filter(x => x.length >= 2);
}

function renderResults(items = []) {
  const tbody = $('#resultsTable tbody');
  if (!items.length) {
    tbody.innerHTML = '<tr><td colspan="4">Результатов нет.</td></tr>';
    return;
  }
  tbody.innerHTML = items.map(item => `
    <tr title="${escapeHtml(item.fullPath)}">
      <td>${escapeHtml(item.type)}</td>
      <td>${escapeHtml(item.name)}</td>
      <td>${escapeHtml(item.keyword)}</td>
      <td>${escapeHtml(item.fullPath)}</td>
    </tr>`).join('');
}

function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]));
}

function bind() {
  $('#dragZone').addEventListener('mousedown', e => {
    if (e.button === 0 && !e.target.closest('button')) post('window.drag');
  });
  $$('[data-window]').forEach(b => b.addEventListener('click', () => post(`window.${b.dataset.window}`)));

  $$('.nav-item,.dock-btn,.dock-logo').forEach(b => b.addEventListener('click', () => switchTab(b.dataset.tab)));

  $('#chooseSearchBtn').onclick = () => post('folder.search.choose');
  $('#chooseSearchBtn2').onclick = () => post('folder.search.choose');
  $('#chooseSaveBtn').onclick = () => post('folder.save.choose');
  $('#chooseSaveBtn2').onclick = () => post('folder.save.choose');
  $('#adminBtn').onclick = () => post('admin.restart');
  $('#startBtn').onclick = () => post('search.start', { words: $('#wordsInput').value });
  $('#cancelBtn').onclick = () => post('search.cancel');
  $('#openReportBtn').onclick = () => post('report.open');
  $('#openReportBtn2').onclick = () => post('report.open');
  $('#openReportFolderBtn').onclick = () => post('report.folder');
  $('#clearLogBtn').onclick = () => $('#log').textContent = '';

  $('#wordsInput').addEventListener('input', renderChips);
  $('#quickSearch').addEventListener('input', filterResults);

  $('#themeToggle').onclick = () => setTheme(state.theme === 'dark' ? 'light' : 'dark');
  $('#themeSelect').onchange = e => setTheme(e.target.value);
  $('#densitySelect').onchange = e => setDensity(e.target.value);
}

function filterResults() {
  const q = $('#quickSearch').value.trim().toLowerCase();
  $$('#resultsTable tbody tr').forEach(row => row.style.display = row.textContent.toLowerCase().includes(q) ? '' : 'none');
}

window.desktopResponse = function (message) {
  const { type, data } = message;
  switch (type) {
    case 'app.state':
      state.searchPath = data.searchFolderPath || '';
      state.savePath = data.saveFolderPath || '';
      state.lastReport = data.lastReportPath || '';
      $('#versionText').textContent = data.appVersion || 'Liquid Glass UI';
      $('#adminState').textContent = data.isRunningAsAdmin ? 'Администратор' : 'Обычный запуск';
      $('#adminCard').classList.toggle('is-admin', !!data.isRunningAsAdmin);
      updatePaths();
      break;

    case 'folder.search.selected':
      state.searchPath = data.path;
      updatePaths();
      if (!data.accessOk) toast('Доступ к папке ограничен. Лучше запустить от администратора.');
      break;

    case 'folder.save.selected':
      state.savePath = data.path;
      updatePaths();
      break;

    case 'search.started':
      state.running = true;
      state.found = 0;
      state.processed = 0;
      state.total = 0;
      state.skipped = 0;
      $('#runState').textContent = 'Running';
      $('#resultSummary').textContent = 'Поиск выполняется...';
      renderResults([]);
      setProgress(0);
      addLog(`Поиск запущен: ${data.words.join(', ')}`);
      break;

    case 'search.progress':
      setProgress(data.percent, data.processed, data.total, data.found);
      break;

    case 'search.log':
      addLog(data.message);
      break;

    case 'search.completed':
      state.running = false;
      state.found = data.foundCount;
      state.total = data.totalItems;
      state.processed = data.totalItems;
      state.skipped = data.accessDeniedCount;
      state.lastReport = data.reportPath;
      $('#runState').textContent = 'Done';
      $('#resultSummary').textContent = `Найдено: ${data.foundCount}, обработано: ${data.totalItems}`;
      $('#lastReportName').textContent = data.reportFileName || 'Отчёт создан';
      $('#statDuration').textContent = `${data.durationSeconds} сек.`;
      setProgress(100, data.totalItems, data.totalItems, data.foundCount);
      renderResults(data.found || []);
      toast('Поиск завершён');
      switchTab('results');
      break;

    case 'search.cancelled':
      state.running = false;
      $('#runState').textContent = 'Cancelled';
      addLog(data.message);
      toast(data.message);
      break;

    case 'app.log':
      addLog(data.message, data.time);
      break;

    case 'app.error':
      toast(data.message || 'Ошибка');
      addLog('Ошибка: ' + (data.message || 'неизвестно'));
      break;
  }
};

window.addEventListener('DOMContentLoaded', () => {
  setTheme(state.theme);
  setDensity(state.density);
  bind();
  renderChips();
  post('app.ready');
});
