const projectList = document.querySelector('#project-list');
const sidebarList = document.querySelector('#project-sidebar-list');
const count = document.querySelector('#project-count');
const searchInput = document.querySelector('#project-search');
const createForm = document.querySelector('#create-project-form');
const createStatus = document.querySelector('#create-status');
const repositoryForm = document.querySelector('#repository-form');
const repositoryStatus = document.querySelector('#repository-status');
const selectedSection = document.querySelector('#selected-project');
const inspectorHint = document.querySelector('#inspector-hint');
let projects = [];
let selectedId = null;

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, character => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[character]));
}

function projectName(project) {
  const name = project.name || project.project || project.repositoryPath;
  return name?.split(/[\\/]/).filter(Boolean).pop() || 'Проект без названия';
}

function projectId(project) {
  return project.id ?? project.sourceProjectId;
}

function isAbsolutePath(path) {
  return /^\/[\s\S]*/.test(path) || /^[A-Za-z]:[\\/]/.test(path) || /^\\\\[^\\]+\\[^\\]+/.test(path);
}

function showStatus(element, message, isError = false) {
  element.textContent = message;
  element.classList.toggle('error', isError);
}

async function responseError(response) {
  const result = await response.json().catch(() => ({}));
  const validation = result.errors && Object.values(result.errors).flat().filter(Boolean).join(' ');
  return result.error || validation || result.detail || result.message || result.title || `HTTP ${response.status}`;
}

function renderProjects() {
  const query = searchInput.value.trim().toLocaleLowerCase();
  const visible = projects.filter(project => `${projectName(project)} ${project.repositoryPath || ''}`.toLocaleLowerCase().includes(query));
  count.textContent = String(projects.length);
  if (!projects.length) {
    projectList.innerHTML = '<p class="state-message">Проектов пока нет. Добавьте локальный Git-репозиторий с помощью формы справа.</p>';
    sidebarList.innerHTML = '<p class="state-message">Проектов пока нет.</p>';
    return;
  }
  if (!visible.length) {
    projectList.innerHTML = '<p class="state-message">По этому запросу проекты не найдены.</p>';
    sidebarList.innerHTML = '<p class="state-message">Ничего не найдено.</p>';
    return;
  }
  projectList.innerHTML = visible.map(project => {
    const name = projectName(project);
    const missing = !project.repositoryPath;
    return `<article class="project-card${project.id === selectedId ? ' selected' : ''}" aria-label="${escapeHtml(name)}">
      <div class="project-card-copy"><h3>${escapeHtml(name)}</h3><p class="project-path">${missing ? 'Локальный путь не задан' : escapeHtml(project.repositoryPath)}</p><div class="project-card-meta"><span class="host-badge">${project.codeHost === 'github' ? 'GitHub' : 'GitLab'}</span>${missing ? '<span class="missing-badge">Нет локального пути</span>' : ''}</div></div>
      <a class="bind-action" href="/project.html?projectId=${encodeURIComponent(project.id)}">Открыть проект</a>${missing ? `<button type="button" class="bind-action" data-bind-project="${escapeHtml(project.id)}">Указать путь</button>` : ''}
    </article>`;
  }).join('');
  sidebarList.innerHTML = visible.map(project => `<a class="sidebar-project${project.id === selectedId ? ' selected' : ''}" href="/project.html?projectId=${encodeURIComponent(project.id)}"><span class="sidebar-project-icon" aria-hidden="true">⌂</span><span class="sidebar-project-copy"><strong>${escapeHtml(projectName(project))}</strong><small>${project.repositoryPath ? escapeHtml(project.repositoryPath) : 'Нет локального пути'}</small></span></a>`).join('');
}

async function loadProjects() {
  projectList.innerHTML = '<p class="state-message">Загружаю проекты…</p>';
  sidebarList.innerHTML = '<p class="state-message">Загружаю проекты…</p>';
  try {
    const response = await fetch('/api/projects/repositories');
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json();
    const items = Array.isArray(result) ? result : result.value;
    if (!Array.isArray(items)) throw new Error('Сервер вернул некорректный список проектов.');
    projects = items.map(project => ({...project, id:projectId(project)}));
    renderProjects();
  } catch (error) {
    const message = `Не удалось загрузить проекты: ${error.message}`;
    projectList.innerHTML = `<p class="state-message error">${escapeHtml(message)}</p>`;
    sidebarList.innerHTML = `<p class="state-message error">${escapeHtml(message)}</p>`;
  }
}

function selectProject(project) {
  selectedId = project.id;
  if (!project.repositoryPath) {
    document.querySelector('#selected-project-name').textContent = projectName(project);
    repositoryForm.elements.repositoryPath.value = '';
    repositoryForm.elements.codeHost.value = project.codeHost === 'github' ? 'github' : 'gitlab';
    showStatus(repositoryStatus, '');
    selectedSection.hidden = false;
    inspectorHint.hidden = true;
    repositoryForm.elements.repositoryPath.focus();
  } else {
    selectedSection.hidden = true;
    inspectorHint.hidden = false;
  }
  renderProjects();
}

function validatePath(input, statusElement) {
  const path = input.value.trim();
  if (!path) {
    showStatus(statusElement, 'Укажите абсолютный путь к локальному Git-репозиторию.', true);
    input.focus();
    return null;
  }
  if (!isAbsolutePath(path)) {
    showStatus(statusElement, 'Путь должен быть абсолютным (например, C:\\work\\project или /work/project).', true);
    input.focus();
    return null;
  }
  return path;
}

createForm.addEventListener('submit', async event => {
  event.preventDefault();
  const input = createForm.elements.repositoryPath;
  const repositoryPath = validatePath(input, createStatus);
  if (!repositoryPath) return;
  const button = createForm.querySelector('[type="submit"]');
  button.disabled = true;
  showStatus(createStatus, 'Добавляю проект…');
  try {
    const response = await fetch('/api/projects', {
      method: 'POST', headers: {'Content-Type':'application/json'},
      body: JSON.stringify({repositoryPath, codeHost:createForm.elements.codeHost.value}),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const created = await response.json();
    input.value = '';
    showStatus(createStatus, `Проект «${projectName(created)}» добавлен.`);
    await loadProjects();
    const added = projects.find(project => project.id === created.id || project.repositoryPath === created.repositoryPath);
    if (added) selectProject(added);
  } catch (error) {
    showStatus(createStatus, error.message, true);
  } finally {
    button.disabled = false;
  }
});

repositoryForm.addEventListener('submit', async event => {
  event.preventDefault();
  const project = projects.find(item => item.id === selectedId);
  if (!project) return;
  const input = repositoryForm.elements.repositoryPath;
  const repositoryPath = validatePath(input, repositoryStatus);
  if (!repositoryPath) return;
  const button = repositoryForm.querySelector('[type="submit"]');
  button.disabled = true;
  showStatus(repositoryStatus, 'Сохраняю путь…');
  try {
    const response = await fetch(`/api/projects/${encodeURIComponent(project.id)}/repository`, {
      method: 'PUT', headers: {'Content-Type':'application/json'},
      body: JSON.stringify({repositoryPath, codeHost:repositoryForm.elements.codeHost.value}),
    });
    if (!response.ok) throw new Error(await responseError(response));
    showStatus(repositoryStatus, 'Локальный путь сохранён.');
    selectedId = null;
    selectedSection.hidden = true;
    inspectorHint.hidden = false;
    await loadProjects();
  } catch (error) {
    showStatus(repositoryStatus, error.message, true);
  } finally {
    button.disabled = false;
  }
});

document.querySelector('#cancel-selection').addEventListener('click', () => {
  selectedId = null;
  selectedSection.hidden = true;
  inspectorHint.hidden = false;
  renderProjects();
});
document.querySelector('#refresh-projects').addEventListener('click', loadProjects);
searchInput.addEventListener('input', renderProjects);
const globalSearch = document.querySelector('.global-search input');
globalSearch.addEventListener('input', () => {
  searchInput.value = globalSearch.value;
  renderProjects();
});
document.addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault();
    globalSearch.focus();
  }
});
for (const list of [projectList, sidebarList]) {
  list.addEventListener('click', event => {
    const bind = event.target.closest('[data-bind-project]');
    if (bind) { event.preventDefault(); const project = projects.find(item => String(item.id) === bind.dataset.bindProject); if (project) selectProject(project); }
  });
  list.addEventListener('keydown', event => {
    if ((event.key === 'Enter' || event.key === ' ') && event.target.matches('[data-bind-project]')) { event.preventDefault(); const project = projects.find(item => String(item.id) === event.target.dataset.bindProject); if (project) selectProject(project); }
  });
}

loadProjects();
