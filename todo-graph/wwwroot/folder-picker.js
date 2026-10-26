(() => {
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, character => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[character]));
  const dialog = document.createElement('dialog');
  dialog.className = 'folder-picker-dialog';
  dialog.innerHTML = '<div class="folder-picker-heading"><div><span class="overline">Локальный компьютер</span><h2>Выберите папку</h2></div><button type="button" data-folder-close aria-label="Закрыть">×</button></div><p class="folder-picker-current" data-folder-current></p><div class="folder-picker-actions"><button type="button" data-folder-parent>← Вверх</button><button type="button" data-folder-select disabled>Выбрать эту папку</button></div><p class="folder-picker-status" role="status" aria-live="polite"></p><div class="folder-picker-list" role="list"></div>';
  document.body.append(dialog);
  const current = dialog.querySelector('[data-folder-current]');
  const list = dialog.querySelector('.folder-picker-list');
  const status = dialog.querySelector('.folder-picker-status');
  const select = dialog.querySelector('[data-folder-select]');
  let chosen = null;
  let target = null;
  let parentPath = null;

  async function browse(path) {
    list.innerHTML = '';
    status.textContent = 'Загружаю папки…';
    select.disabled = true;
    try {
      const url = new URL('/api/local-directories', location.origin);
      if (path) url.searchParams.set('path', path);
      const response = await fetch(url);
      const result = await response.json();
      if (!response.ok) throw new Error(result.error || `HTTP ${response.status}`);
      chosen = result.currentPath;
      parentPath = result.parentPath;
      current.textContent = `Выбрано: ${chosen || 'Корни дисков'}`;
      select.disabled = !result.isGitRepository;
      if (!result.entries.length) status.textContent = 'В этой папке нет доступных подпапок.';
      else status.textContent = 'Обычные папки можно открыть; выбрать можно только Git-репозиторий.';
      list.innerHTML = result.entries.map(entry => `<button type="button" class="folder-picker-entry" data-folder-path="${escapeHtml(entry.path)}"><span>📁</span><span>${escapeHtml(entry.name)}</span>${entry.isGitRepository ? '<strong>Git</strong>' : ''}</button>`).join('');
      dialog.querySelector('[data-folder-parent]').disabled = !parentPath;
    } catch (error) {
      status.textContent = `Не удалось открыть папку: ${error.message}`;
      current.textContent = path || 'Корни дисков';
    }
  }

  document.addEventListener('click', event => {
    const button = event.target.closest('[data-folder-picker-target]');
    if (!button) return;
    target = document.querySelector(button.dataset.folderPickerTarget);
    if (!target) return;
    dialog.showModal();
    browse(target.value.trim() || null);
  });
  list.addEventListener('click', event => {
    const entry = event.target.closest('[data-folder-path]');
    if (entry) browse(entry.dataset.folderPath);
  });
  dialog.querySelector('[data-folder-parent]').addEventListener('click', () => { if (parentPath) browse(parentPath); });
  select.addEventListener('click', () => {
    if (!target || !chosen) return;
    target.value = chosen;
    target.dispatchEvent(new Event('change', {bubbles:true}));
    dialog.close();
  });
  dialog.querySelector('[data-folder-close]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
})();
