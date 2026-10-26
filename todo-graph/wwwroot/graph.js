const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
let snapshot = {nodes: [], edges: []};
let selectedKey = null;
let selectedKind = 'all';
let selectedProject = 'all';
let query = '';
let taskCommentRequest = 0;
let graphScale = 1;
let graphOffsetX = 0;
let graphOffsetY = 0;
let graphDirection = 'right';
let fileSearchHits = [];
let fileCache = new Map();
let fileSearchSequence = 0;
let fileSearchTimer = null;
let fileSearchFocusPath = '';
const graphUrl = new URLSearchParams(location.search);
const requestedProjectId = graphUrl.get('projectId');
const requestedTaskId = graphUrl.get('taskId');
const taskExecutionLabels = {idle: 'Не поставлена', queued: 'В очереди', clarifying: 'Агент изучает задачу', awaiting_clarification: 'Ожидает уточнения', ready_for_development: 'Разработка подтверждена', preparing: 'Готовится worktree', retry_worktree: 'Повторная подготовка worktree', prepared: 'Worktree готов', running: 'В процессе разработки', awaiting_development_clarification: 'Нужен ответ по ходу разработки', awaiting_commit: 'Проверки прошли, готов к коммиту', committing: 'В процессе коммита', awaiting_push: 'Ожидает push от человека', awaiting_user_pr: 'Ожидает создания GitHub PR', awaiting_mr_repository_url: 'Ожидает ссылку на репозиторий для MR', pushed: 'Коммит найден в удалённой ветке', creating_mr: 'Создаётся MR', mr_retry_required: 'Требуется сверить MR', code_review: 'MR/PR создан, ожидание review', reviewing_comments: 'Разбор комментариев MR/PR', comments_reviewed: 'Разбор комментариев завершён', blocked: 'Ожидает решения', failed: 'Ошибка выполнения', closed: 'Закрыта'};
const approvalLabels = {unknown: 'Неизвестно', approved: 'Одобрено', awaiting: 'Ожидаются approvals', changes_requested: 'Запрошены изменения', not_required: 'Approvals не требуются'};
const mergeabilityLabels = {unknown: 'Не проверено', mergeable: 'Можно слить', blocked: 'Слияние заблокировано'};

function mergeReadinessHtml(node) {
  if (!node.mergeRequestUrl) return '';
  const approvalReady = ['approved', 'not_required'].includes(node.approvalState);
  const buildReady = node.buildStatus === 'passed';
  const mergeabilityReady = node.mergeability === 'mergeable';
  const fullyReady = node.mergeRequestStatus === 'open' && approvalReady && buildReady && mergeabilityReady;
  const label = fullyReady ? 'Готов к слиянию' : node.mergeRequestStatus !== 'open' ? 'MR/PR закрыт или слит' : 'Готовность к слиянию не подтверждена';
  const approvals = node.approvedCount == null && node.requiredApprovals == null
    ? '' : ` · одобрено: ${node.approvedCount ?? '—'} из ${node.requiredApprovals ?? '—'}`;
  return `<div class="merge-readiness"><strong>${label}</strong><small>Approvals: ${escapeHtml(approvalLabels[node.approvalState] || approvalLabels.unknown)}${approvals}</small><small>Слияние: ${escapeHtml(mergeabilityLabels[node.mergeability] || mergeabilityLabels.unknown)} · Сборка: ${escapeHtml(taskBuildLabels[node.buildStatus] || 'Не запускалась')}</small></div>`;
}
const taskBuildLabels = {not_started: 'Не запускалась', running: 'Собирается', passed: 'Собрана', failed: 'Ошибка сборки'};
const humanActionStatuses = new Set(['awaiting_clarification', 'awaiting_development_clarification', 'awaiting_commit', 'awaiting_push', 'awaiting_user_pr', 'awaiting_mr_repository_url', 'blocked', 'failed', 'mr_retry_required']);

function renderNotifications() {
  const tasks = snapshot.nodes.filter(node => node.kind === 'task' && humanActionStatuses.has(node.executionStatus));
  const list = document.querySelector('#notification-list');
  list.innerHTML = tasks.map(node => `<button type="button" class="notification-row" data-notification-task="${escapeHtml(node.key)}"><strong>${escapeHtml(node.title)}</strong><span>${escapeHtml(taskExecutionLabels[node.executionStatus] || node.executionStatus)}</span></button>`).join('');
  document.querySelector('#notification-empty').hidden = tasks.length > 0;
  document.querySelector('#notifications-toggle i').hidden = tasks.length === 0;
}

async function copyText(text) {
  if (navigator.clipboard?.writeText) {
    try { await navigator.clipboard.writeText(text); return true; } catch { /* use the legacy browser copy command */ }
  }
  const input = document.createElement('textarea');
  input.value = text;
  input.setAttribute('readonly', '');
  input.style.position = 'fixed';
  input.style.opacity = '0';
  document.body.append(input);
  input.select();
  const copied = document.execCommand('copy');
  input.remove();
  return copied;
}

async function loadGraph() {
  const list = document.querySelector('#node-list');
  try {
    snapshot = await fetch('/api/graph').then(async response => {
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    });
    if (!snapshot.nodes.some(node => node.key === selectedKey)) {
      const requested = requestedTaskId && snapshot.nodes.find(node => node.kind === 'task' && String(node.id) === requestedTaskId);
      if (requested) selectedKey = requested.key;
    }
    if (!snapshot.nodes.some(node => node.key === selectedKey)) {
      const connectedCandidate = snapshot.nodes.find(node => node.kind === 'candidate' && snapshot.edges.some(edge => edge.from === node.key || edge.to === node.key));
      selectedKey = connectedCandidate?.key || snapshot.nodes[0]?.key || null;
    }
    const projectFilter = document.querySelector('#project-filter');
    const activeProject = projectFilter.value || 'all';
    const projects = [...new Set(snapshot.nodes.map(node => node.project))].sort((a, b) => a.localeCompare(b, 'ru'));
    projectFilter.innerHTML = '<option value="all">Все проекты</option>' + projects.map(project => `<option value="${escapeHtml(project)}">${escapeHtml(project.split(/[\\/]/).filter(Boolean).pop() || project)}</option>`).join('');
    const requestedProject = requestedProjectId && snapshot.nodes.find(node => String(node.sourceProjectId) === requestedProjectId)?.project;
    projectFilter.value = projects.includes(activeProject) ? activeProject : (projects.includes(requestedProject) ? requestedProject : 'all');
    selectedProject = projectFilter.value;
    document.querySelector('#node-count').textContent = snapshot.nodes.length;
    document.querySelector('#filter-all-count').textContent = snapshot.nodes.length;
    document.querySelector('#filter-candidate-count').textContent = snapshot.nodes.filter(node => node.kind === 'candidate').length;
    document.querySelector('#filter-task-count').textContent = snapshot.nodes.filter(node => node.kind === 'task').length;
    renderGraph();
    renderNotifications();
  } catch (error) {
    list.innerHTML = `<p class="node-list-empty">Не удалось загрузить граф: ${escapeHtml(error.message)}</p>`;
    document.querySelector('#graph-surface').innerHTML = '<div class="canvas-empty">Граф сейчас недоступен</div>';
  }
}

function visibleNodes() {
  const normalized = query.toLocaleLowerCase('ru');
  const relationNodes = new Set();
  if (normalized) snapshot.edges.filter(edge => edge.relation.toLocaleLowerCase('ru').includes(normalized))
    .forEach(edge => { relationNodes.add(edge.from); relationNodes.add(edge.to); });
  return snapshot.nodes.filter(node =>
    (selectedKind === 'all' || node.kind === selectedKind) &&
    (selectedProject === 'all' || node.project === selectedProject) &&
    (!normalized || relationNodes.has(node.key) || [node.title, node.project, node.description, node.sourceReportId, node.sourceTaskId]
      .some(value => String(value ?? '').toLocaleLowerCase('ru').includes(normalized))));
}

function searchFiles(term) {
  const sequence = ++fileSearchSequence;
  clearTimeout(fileSearchTimer);
  fileSearchHits = [];
  if (term.length < 2) { renderGraph(); return; }
  fileSearchTimer = setTimeout(async () => {
    const results = await Promise.all(snapshot.nodes.map(async node => {
      const key = node.key;
      if (!fileCache.has(key)) {
        const collection = node.kind === 'task' ? 'tasks' : 'candidates';
        fileCache.set(key, fetch(`/api/${collection}/${node.id}/files`).then(response => response.ok ? response.json() : []).catch(() => []));
      }
      return (await fileCache.get(key)).filter(file => file.path.toLocaleLowerCase('ru').includes(term.toLocaleLowerCase('ru')))
        .map(file => ({node, path:file.path, size:file.size}));
    }));
    if (sequence !== fileSearchSequence || term !== query) return;
    fileSearchHits = results.flat().slice(0, 40);
    renderGraph();
  }, 250);
}

function renderGraph() {
  const nodes = visibleNodes();
  const visible = new Set(nodes.map(node => node.key));
  const list = document.querySelector('#node-list');
  const degree = new Map(nodes.map(node => [node.key, snapshot.edges.filter(edge => edge.from === node.key || edge.to === node.key).length]));
  const renderListGroup = (title, groupNodes) => groupNodes.length ? `<section class="node-list-group"><h2>${title}</h2>${groupNodes.map(node => {
    const isTask = node.kind === 'task';
    const kindLabel = isTask ? (node.status === 'in_progress' ? 'В работе' : node.status === 'todo' ? 'К выполнению' : taskExecutionLabels[node.executionStatus] || 'Задача') : 'Кандидат';
    const icon = isTask ? (node.executionStatus === 'failed' || node.executionStatus === 'blocked' ? '⚠' : '♧') : '⬡';
    return `<button class="graph-node-item ${node.key === selectedKey ? 'selected' : ''} ${isTask ? 'task-item' : 'candidate-item'}" data-kind="${node.kind}" onclick="selectNode('${escapeHtml(node.key)}')"><span class="node-kind-icon">${icon}</span><span class="graph-node-copy"><small>${escapeHtml(isTask ? node.sourceTaskId || `Задача #${node.id}` : node.sourceReportId || `BSR-${String(node.id).padStart(6, '0')}`)}</small><strong>${escapeHtml(node.title)}</strong><span class="node-status-chip ${isTask ? 'task-chip' : 'candidate-chip'}">${escapeHtml(kindLabel)}</span><span class="node-item-stats"><span>♧ ${degree.get(node.key) || 0}</span><span>◌ ${node.commentCount || 0}</span></span></span></button>`;
  }).join('')}</section>` : '';
  const linked = nodes.filter(node => degree.get(node.key) > 0);
  const unlinked = nodes.filter(node => !degree.get(node.key));
  const fileHits = fileSearchHits.filter(hit => hit.path.toLocaleLowerCase('ru').includes(query.toLocaleLowerCase('ru')) &&
    (selectedProject === 'all' || hit.node.project === selectedProject) && (selectedKind === 'all' || hit.node.kind === selectedKind));
  const fileResults = fileHits.length ? `<section class="node-list-group file-search-group"><h2>ФАЙЛЫ</h2>${fileHits.map(hit => `<button type="button" class="file-search-result" data-file-search-node="${escapeHtml(hit.node.key)}" data-file-search-path="${escapeHtml(hit.path)}"><span>▤</span><strong>${escapeHtml(hit.path)}</strong><small>${escapeHtml(hit.node.title)}</small></button>`).join('')}</section>` : '';
  list.innerHTML = nodes.length || fileResults
    ? `${renderListGroup('СО СВЯЗЯМИ', linked)}${renderListGroup('БЕЗ СВЯЗЕЙ', unlinked)}${fileResults}`
    : '<div class="node-list-empty">Подходящих узлов нет. Измените фильтр или поисковый запрос.</div>';

  const projects = [...new Set(nodes.map(node => node.project))].sort((a, b) => a.localeCompare(b, 'ru'));
  const grouped = projects.map(project => ({project, nodes: nodes.filter(node => node.project === project)}));
  let width = Math.max(1120, document.querySelector('#graph-viewport').clientWidth + 300);
  if (graphDirection === 'down') width = Math.max(width, ...grouped.map(group => Math.max(group.nodes.filter(node => node.kind === 'candidate').length, group.nodes.filter(node => node.kind === 'task').length) * 300 + 520));
  const rowHeights = grouped.map(group => Math.max(420, Math.max(group.nodes.filter(node => node.kind === 'candidate').length, group.nodes.filter(node => node.kind === 'task').length) * 190 + 120));
  const height = Math.max(700, rowHeights.reduce((sum, item) => sum + item + 44, 20));
  let rowTop = 34;
  const positions = new Map();
  const canvasCards = [];
  grouped.forEach((group, groupIndex) => {
    const y = rowTop;
    const candidates = group.nodes.filter(node => node.kind === 'candidate');
    const tasks = group.nodes.filter(node => node.kind === 'task');
    const candidateY = candidates.map((_, index) => y + 145 + index * 185);
    const taskY = tasks.map((_, index) => y + 100 + index * 185);
    candidates.forEach((node, index) => positions.set(node.key, graphDirection === 'right' ? {x:225, y:candidateY[index]} : {x:260 + index * 300, y:y + 130}));
    tasks.forEach((node, index) => positions.set(node.key, graphDirection === 'right' ? {x:720, y:taskY[index]} : {x:260 + index * 300, y:y + 470}));
    group.nodes.filter(node => node.kind !== 'candidate' && node.kind !== 'task').forEach((node, index) => positions.set(node.key, {x:280, y:y + 145 + index * 185}));
    group.nodes.forEach(node => {
      const point = positions.get(node.key);
      const isTask = node.kind === 'task';
      const source = isTask ? node.sourceTaskId || `BST-${String(node.id).padStart(6, '0')}` : node.sourceReportId || `BSR-${String(node.id).padStart(6, '0')}`;
      const nodeEdges = snapshot.edges.filter(edge => edge.from === node.key || edge.to === node.key).length;
      const taskLabel = isTask ? (node.status === 'closed' ? 'Закрыта' : node.status === 'in_progress' ? 'В работе' : node.status === 'todo' ? 'К выполнению' : taskExecutionLabels[node.executionStatus] || 'Задача') : 'Кандидат';
      const icon = isTask ? (node.executionStatus === 'failed' || node.executionStatus === 'blocked' ? '⚠' : '♧') : '⬡';
      canvasCards.push(`<button class="canvas-node ${node.kind} ${node.key === selectedKey ? 'selected' : ''} ${node.key === selectedKey ? 'root' : ''}" data-kind="${node.kind}" style="left:${point.x}px;top:${point.y}px" onclick="selectNode('${escapeHtml(node.key)}')"><span class="canvas-node-head"><span class="canvas-node-icon">${icon}</span><span class="node-label">${escapeHtml(source)}</span></span><strong>${escapeHtml(node.title)}</strong><span class="canvas-node-status ${isTask ? 'task-status' : 'candidate-status'}">${escapeHtml(taskLabel)}</span><span class="canvas-node-stats"><span>♧ ${nodeEdges} ${nodeEdges === 1 ? 'связь' : 'связи'}</span><span>◌ ${node.commentCount || 0}</span></span></button>`);
    });
    rowTop += rowHeights[groupIndex] + 44;
  });
  const edges = snapshot.edges.filter(edge => visible.has(edge.from) && visible.has(edge.to)).map(edge => {
    const from = positions.get(edge.from); const to = positions.get(edge.to);
    if (!from || !to) return '';
    const fromPoint = graphDirection === 'right' ? {x:from.x + 145, y:from.y} : {x:from.x, y:from.y + 60};
    const toPoint = graphDirection === 'right' ? {x:to.x - 145, y:to.y} : {x:to.x, y:to.y - 60};
    const curve = graphDirection === 'right' ? Math.max(40, (toPoint.x - fromPoint.x) * .42) : Math.max(35, Math.abs(toPoint.y - fromPoint.y) * .35);
    const path = graphDirection === 'right'
      ? `M${fromPoint.x},${fromPoint.y} C${fromPoint.x + curve},${fromPoint.y} ${toPoint.x - curve},${toPoint.y} ${toPoint.x},${toPoint.y}`
      : `M${fromPoint.x},${fromPoint.y} C${fromPoint.x},${fromPoint.y + curve} ${toPoint.x},${toPoint.y - curve} ${toPoint.x},${toPoint.y}`;
    const label = ({source: 'является источником', related: 'связана', duplicate: 'дубликат', depends_on: 'зависит', blocks: 'заблокировано до'})[edge.relation] || edge.relation;
    const labelX = (fromPoint.x + toPoint.x) / 2;
    const labelY = (fromPoint.y + toPoint.y) / 2 - 9;
    return `<path class="graph-edge" data-relation="${escapeHtml(edge.relation)}" d="${path}" marker-end="url(#graph-arrow)"/><text x="${labelX}" y="${labelY}" text-anchor="middle">${escapeHtml(label)}</text>`;
  }).join('');
  const surface = document.querySelector('#graph-surface');
  surface.style.width = `${width}px`;
  surface.style.height = `${height}px`;
  surface.style.transform = `translate(${graphOffsetX}px, ${graphOffsetY}px) scale(${graphScale})`;
  document.querySelector('#zoom-level').textContent = `${Math.round(graphScale * 100)}%`;
  surface.innerHTML = nodes.length
    ? `<div class="project-caption">${grouped.map(group => `<span>${escapeHtml(group.project.split(/[\\/]/).filter(Boolean).pop() || group.project)}</span>`).join('')}</div><svg class="graph-svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" aria-hidden="true"><defs><marker id="graph-arrow" markerWidth="9" markerHeight="9" refX="7" refY="3" orient="auto"><path d="M0,0 L0,6 L8,3 z" fill="#668477"></path></marker></defs>${edges}</svg>${canvasCards.join('')}`
    : '<div class="canvas-empty"><svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="7" cy="16" r="3"></circle><circle cx="24" cy="7" r="3"></circle><circle cx="24" cy="25" r="3"></circle><path d="m10 15 11-6M10 17l11 6"></path></svg><strong>Граф пока пуст</strong><span>Кандидаты появятся после одобрения в Boy Scout.</span></div>';
  renderMiniMap(positions, width, height);
  document.querySelector('#edge-count').textContent = `${snapshot.edges.length} связей`;
  renderInspector();
}

function renderMiniMap(positions, width, height) {
  const minimap = document.querySelector('#graph-minimap');
  const dots = [...positions.values()].map(point => `<i style="left:${point.x / width * 100}%;top:${point.y / height * 100}%"></i>`).join('');
  minimap.innerHTML = `<div class="minimap-inner" aria-hidden="true">${dots}<b></b></div>`;
  minimap.setAttribute('role', 'button');
  minimap.setAttribute('tabindex', '0');
  minimap.setAttribute('aria-label', 'Переместить область графа по миникарте');
  minimap.dataset.graphWidth = width;
  minimap.dataset.graphHeight = height;
  updateMiniMapViewport();
}

function setGraphZoom(nextScale, anchorX, anchorY) {
  const viewport = document.querySelector('#graph-viewport');
  const oldScale = graphScale;
  const x = anchorX ?? viewport.clientWidth / 2;
  const y = anchorY ?? viewport.clientHeight / 2;
  const logicalX = (x - graphOffsetX) / oldScale;
  const logicalY = (y - graphOffsetY) / oldScale;
  graphScale = Math.max(0.4, Math.min(2, Math.round(nextScale * 100) / 100));
  graphOffsetX = x - logicalX * graphScale;
  graphOffsetY = y - logicalY * graphScale;
  renderGraph();
}

function installGraphBoardControls() {
  const viewport = document.querySelector('#graph-viewport');
  let drag = null;
  let moved = false;
  viewport.addEventListener('pointerdown', event => {
    if (event.button !== 0 || event.target.closest('button,a,input,select,textarea')) return;
    event.preventDefault();
    drag = {pointerId:event.pointerId, x:event.clientX, y:event.clientY, left:graphOffsetX, top:graphOffsetY};
    moved = false;
    viewport.setPointerCapture(event.pointerId);
  });
  viewport.addEventListener('pointermove', event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) {
      moved = true;
      viewport.classList.add('panning');
      event.preventDefault();
    }
    if (moved) {
      graphOffsetX = drag.left + dx;
      graphOffsetY = drag.top + dy;
      document.querySelector('#graph-surface').style.transform = `translate(${graphOffsetX}px, ${graphOffsetY}px) scale(${graphScale})`;
      updateMiniMapViewport();
    }
  });
  const finishDrag = event => {
    if (!drag || event.pointerId !== drag.pointerId) return;
    if (viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
    drag = null;
    viewport.classList.remove('panning');
  };
  viewport.addEventListener('pointerup', finishDrag);
  viewport.addEventListener('pointercancel', finishDrag);
  viewport.addEventListener('wheel', event => {
    event.preventDefault();
    if (event.shiftKey) {
      graphOffsetX -= event.deltaY;
      document.querySelector('#graph-surface').style.transform = `translate(${graphOffsetX}px, ${graphOffsetY}px) scale(${graphScale})`;
      updateMiniMapViewport();
      return;
    }
    const rect = viewport.getBoundingClientRect();
    setGraphZoom(graphScale * (event.deltaY < 0 ? 1.1 : 0.9), event.clientX - rect.left, event.clientY - rect.top);
  }, {passive:false});
  document.querySelector('#zoom-in').addEventListener('click', () => setGraphZoom(graphScale + 0.1));
  document.querySelector('#zoom-out').addEventListener('click', () => setGraphZoom(graphScale - 0.1));
  document.querySelector('#zoom-reset').addEventListener('click', fitGraphToView);
  document.querySelector('#show-all').addEventListener('click', () => {
    query = '';
    selectedKind = 'all';
    selectedProject = 'all';
    fileSearchHits = [];
    document.querySelector('#node-search').value = '';
    document.querySelector('.global-search input').value = '';
    document.querySelector('#project-filter').value = 'all';
    document.querySelectorAll('.node-filters button').forEach(button => button.classList.toggle('active', button.dataset.kind === 'all'));
    renderGraph();
    fitGraphToView();
  });
  document.querySelector('#auto-layout').addEventListener('click', () => { graphScale = 1; renderGraph(); fitGraphToView(); });
  document.querySelector('#graph-direction').addEventListener('change', event => { graphDirection = event.target.value; renderGraph(); });
  const minimap = document.querySelector('#graph-minimap');
  const panFromMinimap = event => {
    const inner = minimap.querySelector('.minimap-inner');
    const rect = inner.getBoundingClientRect();
    const xRatio = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
    const yRatio = Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height));
    graphOffsetX = viewport.clientWidth / 2 - xRatio * Number(minimap.dataset.graphWidth) * graphScale;
    graphOffsetY = viewport.clientHeight / 2 - yRatio * Number(minimap.dataset.graphHeight) * graphScale;
    document.querySelector('#graph-surface').style.transform = `translate(${graphOffsetX}px, ${graphOffsetY}px) scale(${graphScale})`;
    updateMiniMapViewport();
  };
  minimap.addEventListener('click', panFromMinimap);
  minimap.addEventListener('keydown', event => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      const rect = minimap.querySelector('.minimap-inner').getBoundingClientRect();
      panFromMinimap({clientX: rect.left + rect.width / 2, clientY: rect.top + rect.height / 2});
    }
  });
}

function updateMiniMapViewport() {
  const viewport = document.querySelector('#graph-viewport');
  const minimap = document.querySelector('#graph-minimap');
  const rect = minimap.querySelector('.minimap-inner b');
  const width = Number(minimap.dataset.graphWidth) || 1;
  const height = Number(minimap.dataset.graphHeight) || 1;
  rect.style.left = `${-graphOffsetX / graphScale / width * 100}%`;
  rect.style.top = `${-graphOffsetY / graphScale / height * 100}%`;
  rect.style.width = `${Math.min(100, viewport.clientWidth / graphScale / width * 100)}%`;
  rect.style.height = `${Math.min(100, viewport.clientHeight / graphScale / height * 100)}%`;
}

function fitGraphToView() {
  const viewport = document.querySelector('#graph-viewport');
  const surface = document.querySelector('#graph-surface');
  const logicalWidth = parseFloat(surface.style.width) || 1120;
  const logicalHeight = parseFloat(surface.style.height) || 700;
  const scale = Math.min(1, (viewport.clientWidth - 24) / logicalWidth, (viewport.clientHeight - 24) / logicalHeight);
  setGraphZoom(Math.max(0.4, scale), 0, 0);
  graphOffsetX = (viewport.clientWidth - logicalWidth * graphScale) / 2;
  graphOffsetY = (viewport.clientHeight - logicalHeight * graphScale) / 2;
  renderGraph();
}

function selectNode(key) {
  selectedKey = key;
  renderGraph();
}

function renderInspector() {
  const inspector = document.querySelector('#inspector');
  const node = snapshot.nodes.find(candidate => candidate.key === selectedKey);
  if (!node) {
    inspector.innerHTML = '<div class="inspector-empty"><span>◎</span><strong>Выберите узел</strong><p>Детали выбранной задачи или кандидата появятся здесь.</p></div>';
    return;
  }
  const related = snapshot.edges.filter(edge => edge.from === node.key || edge.to === node.key);
  const sourceId = node.kind === 'task' ? node.sourceTaskId || `BST-${String(node.id).padStart(6, '0')}` : node.sourceReportId || `BSR-${String(node.id).padStart(6, '0')}`;
  const relatedItems = related.map(edge => {
    const otherKey = edge.from === node.key ? edge.to : edge.from;
    const other = snapshot.nodes.find(item => item.key === otherKey);
    return `<button type="button" class="related-node" onclick="selectNode('${escapeHtml(otherKey)}')"><span>${edge.from === node.key ? '→' : '←'}</span><span><strong>${escapeHtml(other?.title || otherKey)}</strong><small>${escapeHtml(edge.relation)} · ${escapeHtml(other?.project || '')}</small></span><span>›</span></button>`;
  }).join('');
      inspector.innerHTML = `<div class="inspector-panel-head"><span class="inspector-head-icon ${node.kind}">${node.kind === 'task' ? '⬡' : '⬡'}</span><span>${escapeHtml(sourceId)}</span><div class="inspector-menu-wrap"><button type="button" class="inspector-overflow" data-inspector-menu-toggle aria-label="Действия" aria-expanded="false">⋮</button><div class="inspector-menu" data-inspector-menu hidden><button type="button" data-copy-node-id="${escapeHtml(node.key)}">Копировать идентификатор</button><button type="button" data-close-inspector>Закрыть инспектор</button></div></div><button type="button" data-close-inspector aria-label="Закрыть">×</button></div><nav class="inspector-tabs" role="tablist"><button class="active" data-inspector-tab="details">Детали</button><button data-inspector-tab="relations">Связи <span>${related.length}</span></button><button data-inspector-tab="files">Файлы <span data-files-count>…</span></button><button data-inspector-tab="history">История <span data-history-count>…</span></button></nav><div class="inspector-scroll"><section data-inspector-panel="details"><div class="inspector-content"><div><h3>${escapeHtml(node.title)}</h3><span class="inspector-type-chip ${node.kind}">${node.kind === 'task' ? 'Задача' : 'Кандидат'}</span><h4 class="inspector-section-title">Описание <button type="button" class="subtle-action" data-edit-description>✎ Изменить</button></h4><p data-node-description>${escapeHtml(node.description)}</p>
      ${node.acceptanceCriteria ? `<p class="inspector-criteria"><strong>Критерии готовности</strong>${escapeHtml(node.acceptanceCriteria)}</p>` : ''}
      <div class="inspector-meta-card"><span>Проект</span><strong>▱ ${escapeHtml(node.project.split(/[\\/]/).filter(Boolean).pop() || node.project)}</strong><span>Тип</span><strong>⬡ ${node.kind === 'task' ? 'Задача' : 'Кандидат'}</strong><span>Статус</span><strong>${escapeHtml(node.kind === 'task' ? taskExecutionLabels[node.executionStatus] || node.status : node.status)}</strong><span>Автор</span><strong>♙ ${escapeHtml(node.createdBy || 'Не указан')}</strong></div><div class="inspector-meta">${escapeHtml(node.project)} · ${escapeHtml(node.status)}</div>
     ${node.kind === 'task' ? `<div class="task-execution-state"><span>Выполнение: <strong>${escapeHtml(taskExecutionLabels[node.executionStatus] || 'Не поставлена')}</strong></span><span>Сборка проекта: <strong>${escapeHtml(taskBuildLabels[node.buildStatus] || 'Не запускалась')}</strong></span></div>${node.executionError ? `<p class="task-action-error">${escapeHtml(node.executionError)}</p>` : ''}${node.sourceProjectId ? `<form class="task-repository-form" onsubmit="saveProjectRepository(event, ${node.sourceProjectId})"><label>Исходный Git-репозиторий проекта<input name="repositoryPath" required value="${escapeHtml(node.repositoryPath || '')}" placeholder="Абсолютный путь к локальному репозиторию"></label><button type="submit">Сохранить путь</button></form>` : '<p class="task-action-note">Для запуска нужна привязка к проекту и его локальному Git-репозиторию.</p>'}<div class="task-execution-actions">${!['closed', 'done'].includes(node.status) ? `<button type="button" data-queue-task="${node.id}" ${['queued', 'clarifying', 'awaiting_clarification'].includes(node.executionStatus) ? 'disabled' : ''}>${node.executionStatus === 'queued' ? 'Уже в очереди' : ['clarifying', 'awaiting_clarification'].includes(node.executionStatus) ? 'Разбор запущен' : 'Поставить в очередь'}</button><button type="button" class="task-reject" data-reject-task="${node.id}">Отвергнуть и закрыть</button>` : '<span>Задача закрыта для постановки в очередь.</span>'}<span id="task-action-status"></span></div><section class="task-preflight" id="task-preflight"><h4>Разбор перед разработкой</h4><p>Загружаю объяснение…</p></section>${node.status !== 'closed' ? `<form class="task-review-form" onsubmit="saveTask(event, ${node.id})"><label>Статус<select name="status"><option value="todo" ${node.status === 'todo' ? 'selected' : ''}>К выполнению</option><option value="in_progress" ${node.status === 'in_progress' ? 'selected' : ''}>В работе</option><option value="code_review" ${node.status === 'code_review' ? 'selected' : ''}>Code Review</option><option value="done" ${node.status === 'done' ? 'selected' : ''}>Готово</option></select></label><label>Ссылка на MR<input name="mergeRequestUrl" type="url" maxlength="2048" value="${escapeHtml(node.mergeRequestUrl || '')}" placeholder="https://gitlab.com/group/project/-/merge_requests/123"></label><div class="task-review-actions"><button type="submit">Сохранить</button><span id="task-save-status"></span></div></form>` : ''}<section class="mr-comments" id="task-comments"><h4>Комментарии MR</h4><p class="mr-comments-empty">Загружаю…</p></section>` : ''}</div>
     <div class="inspector-actions">${node.kind === 'candidate' ? `<button onclick="openCandidate(${node.id})">Открыть разбор</button>` : '<span class="task-chip">Узел задачи</span>'}</div></div>
        ${node.kind === 'task' ? `<div class="mr-state-bar"><strong>${node.codeHost === 'github' ? 'PR' : 'MR'}: ${escapeHtml(({none:'Нет',open:'Открыт',closed:'Закрыт',merged:'Слит'})[node.mergeRequestStatus] || 'Нет')}</strong>${node.mergeRequestSourceBranch ? `<small>Исходная ветка: ${escapeHtml(node.mergeRequestSourceBranch)}</small>` : ''}${node.mergeRequestUrl ? mergeReadinessHtml(node) : ''}${node.mergeRequestUrl && node.status !== 'closed' ? `<form onsubmit="saveMrStatus(event, ${node.id})"><label>Состояние MR/PR<select name="status"><option value="open" ${node.mergeRequestStatus === 'open' ? 'selected' : ''}>Открыт</option><option value="closed" ${node.mergeRequestStatus === 'closed' ? 'selected' : ''}>Закрыт</option><option value="merged" ${node.mergeRequestStatus === 'merged' ? 'selected' : ''}>Слит</option></select></label><button type="submit">Сохранить</button></form>` : ''}${node.worktreePath ? `<small>Worktree: ${escapeHtml(node.worktreePath)} · ветка: ${escapeHtml(node.branchName)} · база: ${escapeHtml(node.baseBranch || 'определяется')}</small>` : ''}${node.executionStatus === 'blocked' && node.worktreePath && node.status !== 'closed' ? node.worktreeReady ? `<button type="button" data-resume-development="${node.id}">Продолжить разработку</button>` : `<button type="button" data-retry-worktree="${node.id}">Повторить подготовку worktree</button>` : ''}</div>` : ''}</section><section class="inspector-tab-panel" data-inspector-panel="relations" hidden><h3>Связи <span>${related.length}</span></h3>${relatedItems || '<p>Связей пока нет.</p>'}</section><section class="inspector-tab-panel" data-inspector-panel="files" hidden><h3>Файлы</h3><div class="inspector-data-state" data-files-list><p>Загружаю файлы…</p></div></section><section class="inspector-tab-panel" data-inspector-panel="history" hidden><h3>История</h3><div class="inspector-data-state" data-history-list><p>Загружаю историю…</p></div></section></div>`;
  const repositoryInput = inspector.querySelector('.task-repository-form [name="repositoryPath"]');
  if (repositoryInput) {
    const pickerId = `task-repository-path-${node.sourceProjectId}`;
    repositoryInput.id = pickerId;
    repositoryInput.readOnly = true;
    repositoryInput.placeholder = 'Выберите папку на этом компьютере';
    const pickerButton = document.createElement('button');
    pickerButton.type = 'button';
    pickerButton.className = 'folder-picker-target';
    pickerButton.textContent = 'Обзор папок…';
    pickerButton.dataset.folderPickerTarget = `#${pickerId}`;
    repositoryInput.closest('label').after(pickerButton);
  }
  const queueButton = inspector.querySelector('[data-queue-task]');
  if (queueButton && (!node.repositoryPath || node.worktreePath || !['idle', 'failed', 'blocked', 'queued'].includes(node.executionStatus))) {
    queueButton.disabled = true;
    queueButton.textContent = !node.repositoryPath ? 'Сначала укажите локальный путь проекта' : taskExecutionLabels[node.executionStatus] || 'Выполнение уже запущено';
  }
  if (node.kind === 'task' && node.executionStatus === 'awaiting_mr_repository_url') {
    inspector.insertAdjacentHTML('beforeend', `<div class="mr-state-bar"><strong>Укажите ссылку на удалённый GitLab-репозиторий</strong><p>Локальный путь проекта уже сохранён. URL нужен сейчас, чтобы найти или создать MR после подтверждённого push.</p><form class="task-remote-repository-form" onsubmit="saveProjectRemoteRepositoryUrl(event, ${node.sourceProjectId})"><label>Ссылка на репозиторий<input type="url" name="remoteRepositoryUrl" required maxlength="2048" value="${escapeHtml(node.remoteRepositoryUrl || '')}" placeholder="https://gitlab.com/group/project"></label><button type="submit">Продолжить создание MR</button></form><span id="task-action-status"></span></div>`);
  }
  if (node.kind === 'task' && node.executionStatus === 'awaiting_push') {
    const followup = node.codeHost === 'github' ? 'После push создайте Pull Request в GitHub и добавьте его ссылку в задачу.' : 'Todo Graph найдёт или создаст MR после проверки push.';
    inspector.insertAdjacentHTML('beforeend', `<div class="mr-state-bar"><strong>Отправьте ветку вручную</strong><p>Откройте worktree ${escapeHtml(node.worktreePath)} и выполните:</p><code>git push -u origin ${escapeHtml(node.branchName)}</code><p>Ожидаемый коммит: <code>${escapeHtml(node.commitSha)}</code>. Todo Graph проверит удалённую ветку и не выполнит push самостоятельно. ${followup}</p></div>`);
  }
  if (node.kind === 'task' && node.executionStatus === 'awaiting_user_pr') {
    inspector.insertAdjacentHTML('beforeend', '<div class="mr-state-bar"><strong>Создайте Pull Request в GitHub</strong><p>После создания добавьте его URL в поле «Ссылка на MR». GitHub MCP только читает PR и комментарии.</p></div>');
  }
  if (node.kind === 'task' && node.executionStatus === 'pushed' && !node.mergeRequestUrl && node.codeHost === 'gitlab') {
    inspector.insertAdjacentHTML('beforeend', '<div class="mr-state-bar">После настройки GitLab MCP <code>gitlab-write</code> агент найдёт или создаст MR для проверенной удалённой ветки.</div>');
  }
  if (node.kind === 'task' && node.executionStatus === 'mr_retry_required' && node.status !== 'closed') {
    inspector.insertAdjacentHTML('beforeend', `<div class="mr-state-bar">Проверьте в GitLab, не создан ли MR для ветки ${escapeHtml(node.branchName)}, затем <button type="button" data-retry-mr="${node.id}">Повторить сверку MR</button>.</div>`);
  }
  if (node.createdBy)
    inspector.querySelector('.inspector-meta')?.insertAdjacentHTML('afterend', `<div class="creator-meta">${node.kind === 'candidate' ? 'Автор/источник' : 'Автор'}: ${escapeHtml(node.createdBy)}</div>`);
  if (node.kind === 'task') {
    const linkTargets = snapshot.nodes.filter(item => item.kind === 'task' && item.key !== node.key && item.project !== node.project);
    const linksPanel = document.createElement('section');
    linksPanel.className = 'task-link-controls';
    linksPanel.innerHTML = `<h4>Связать с задачей другого проекта</h4>${linkTargets.length ? `<form data-task-link data-task-id="${escapeHtml(node.id)}"><label>Задача<select name="relatedTaskId">${linkTargets.map(item => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.title)} — ${escapeHtml(item.project || 'Проект')}</option>`).join('')}</select></label><label>Тип связи<select name="relation"><option value="related">Связана</option><option value="duplicate">Дубликат</option><option value="depends_on">Зависит от</option><option value="blocks">Блокирует</option></select></label><button type="submit">Добавить связь</button><span role="status"></span></form>` : '<p>Других задач пока нет.</p>'}`;
    inspector.querySelector('.inspector-content')?.append(linksPanel);
    if (node.codeHost === 'github') {
      const stateBar = inspector.querySelector('.mr-state-bar');
      const title = stateBar?.querySelector('strong');
      if (title) title.textContent = title.textContent.replace(/^MR:/, 'PR:');
      const label = stateBar?.querySelector('label');
      if (label?.firstChild?.nodeType === Node.TEXT_NODE) label.firstChild.textContent = 'Состояние PR';
      const saveButton = stateBar?.querySelector('form button');
      if (saveButton) saveButton.textContent = 'Сохранить PR';
    }
    const repositoryForm = inspector.querySelector('.task-repository-form');
    if (repositoryForm) {
      const hostLabel = document.createElement('label');
      hostLabel.innerHTML = '<span>Git-хостинг проекта</span><select name="codeHost"><option value="gitlab">GitLab</option><option value="github">GitHub</option></select>';
      hostLabel.querySelector('select').value = node.codeHost || 'gitlab';
      repositoryForm.insertBefore(hostLabel, repositoryForm.firstChild);
    }
    inspector.querySelector('.task-review-form select[name="status"]')?.closest('label').remove();
    loadTaskPreflight(node.id);
    loadTaskComments(node.id);
  }
}

document.addEventListener('submit', async event => {
  const form = event.target.closest('[data-task-link]');
  if (!form) return;
  event.preventDefault();
  const button = form.querySelector('button'), status = form.querySelector('[role="status"]');
  button.disabled = true; status.textContent = 'Сохраняю…';
  try {
    const values = new FormData(form);
    const response = await fetch(`/api/tasks/${encodeURIComponent(form.dataset.taskId)}/links`, {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({relatedTaskId:Number(values.get('relatedTaskId')), relation:values.get('relation')})});
    if (!response.ok) { const body = await response.json().catch(() => ({})); throw new Error(body.error || body.detail || `HTTP ${response.status}`); }
    status.textContent = 'Связь сохранена.'; await loadGraph(); selectedKey = `task:${form.dataset.taskId}`; renderGraph();
  } catch (error) { status.textContent = error.message; }
  finally { button.disabled = false; }
});

async function loadInspectorFiles(node) {
  const panel = document.querySelector('[data-files-list]');
  if (!panel || !node) return;
  panel.innerHTML = '<p>Загружаю список файлов…</p>';
  try {
    const collection = node.kind === 'task' ? 'tasks' : 'candidates';
    const response = await fetch(`/api/${collection}/${node.id}/files`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const files = await response.json();
    if (selectedKey !== node.key || panel !== document.querySelector('[data-files-list]')) return;
    document.querySelector('[data-inspector-tab="files"] span').textContent = files.length;
    panel.innerHTML = files.length ? `<div class="inspector-files-list">${files.map(file => `<article class="inspector-file ${file.path === fileSearchFocusPath ? 'matched-file' : ''}"><span class="file-glyph">▤</span><span><strong>${escapeHtml(file.path)}</strong><small>${formatBytes(file.size)} · ${new Date(file.lastModifiedUtc).toLocaleString('ru-RU')}</small></span></article>`).join('')}</div>`
      : `<p>${node.kind === 'candidate' ? 'Файлы для связанного проекта не найдены.' : node.repositoryPath ? 'В связанном локальном репозитории файлы не найдены.' : 'К проекту не привязан локальный репозиторий.'}</p>`;
  } catch (error) {
    panel.innerHTML = `<p>Не удалось загрузить список файлов: ${escapeHtml(error.message)}</p>`;
  }
}

async function loadInspectorHistory(node) {
  const panel = document.querySelector('[data-history-list]');
  if (!panel || !node) return;
  panel.innerHTML = '<p>Загружаю историю…</p>';
  try {
    let events;
    if (node.kind === 'task') {
      const response = await fetch(`/api/tasks/${node.id}/history`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      events = await response.json();
    } else {
      const [candidateResponse, agentResponse] = await Promise.all([
        fetch(`/api/candidates/${node.id}`), fetch(`/api/agent/candidates/${node.id}`),
      ]);
      if (!candidateResponse.ok || !agentResponse.ok) throw new Error('Не удалось загрузить историю кандидата.');
      const candidate = await candidateResponse.json();
      const agent = await agentResponse.json();
      events = [{eventType: 'Получен', occurredAt: candidate.receivedAt, newValue: candidate.status}];
      if (agent.review) events.push({eventType: 'Разбор агента', occurredAt: agent.review.createdAt, newValue: agent.review.summary});
      events.push({eventType: 'Текущий статус', occurredAt: null, newValue: candidate.triageStatus});
    }
    if (selectedKey !== node.key || panel !== document.querySelector('[data-history-list]')) return;
    document.querySelector('[data-inspector-tab="history"] span').textContent = events.length;
    panel.innerHTML = events.length ? `<ol class="inspector-history-list">${events.map(item => `<li><span class="history-mark"></span><div><strong>${escapeHtml(historyEventLabel(item.eventType))}</strong>${item.occurredAt ? `<time>${new Date(item.occurredAt).toLocaleString('ru-RU')}</time>` : ''}${item.newValue ? `<p>${escapeHtml(item.newValue)}</p>` : ''}</div></li>`).join('')}</ol>` : '<p>История пока пуста.</p>';
  } catch (error) {
    panel.innerHTML = `<p>Не удалось загрузить историю: ${escapeHtml(error.message)}</p>`;
  }
}

function historyEventLabel(type) {
  return ({created: 'Задача создана', status: 'Статус задачи изменён', execution: 'Этап выполнения изменён', build: 'Сборка', content: 'Описание задачи изменено', 'Разбор агента': 'Разбор агента', 'Текущий статус': 'Текущий статус', 'Получен': 'Кандидат получен'})[type] || type;
}

function formatBytes(size) {
  if (size < 1024) return `${size} Б`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} КБ`;
  return `${(size / (1024 * 1024)).toFixed(1)} МБ`;
}

function beginDescriptionEdit() {
  const node = snapshot.nodes.find(candidate => candidate.key === selectedKey);
  const description = document.querySelector('[data-node-description]');
  if (!node || !description) return;
  const form = document.createElement('form');
  form.className = 'description-edit-form';
  form.dataset.descriptionForm = '';
  form.dataset.nodeKind = node.kind;
  form.dataset.nodeId = node.id;
  form.innerHTML = `<label><span class="sr-only">Описание</span><textarea name="description" maxlength="10000" required>${escapeHtml(node.description)}</textarea></label><div><button type="submit">Сохранить</button><button type="button" data-cancel-description>Отмена</button><span role="status"></span></div>`;
  description.replaceWith(form);
  form.querySelector('textarea').focus();
  form.querySelector('textarea').setSelectionRange(form.querySelector('textarea').value.length, form.querySelector('textarea').value.length);
}

function preflightList(title, values) {
  return values?.length ? `<h5>${title}</h5><ul>${values.map(item => `<li>${escapeHtml(item)}</li>`).join('')}</ul>` : '';
}

async function loadTaskPreflight(taskId) {
  const panel = document.querySelector('#task-preflight');
  if (!panel || panel.contains(document.activeElement) && document.activeElement.tagName === 'TEXTAREA') return;
  try {
    const response = await fetch(`/api/tasks/${taskId}/preflight`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const {preflight: review} = await response.json();
    if (selectedKey !== `task:${taskId}` || panel !== document.querySelector('#task-preflight')) return;
    const task = snapshot.nodes.find(node => node.key === `task:${taskId}`);
    panel.innerHTML = review ? `<h4>Разбор перед разработкой</h4><h5>В чём проблема</h5><p>${escapeHtml(review.problem)}</p><h5>Что должно получиться</h5><p>${escapeHtml(review.expectedResult)}</p>${preflightList('Что проверено в проекте', review.evidence)}${review.buildCommand ? `<p><strong>Проверка сборки:</strong> <code>${escapeHtml(review.buildCommand)}</code></p>` : ''}${review.baseBranch ? `<p><strong>Базовая ветка:</strong> <code>${escapeHtml(review.baseBranch)}</code></p>` : ''}${preflightList('Риски', review.risks)}${preflightList('Пограничные случаи', review.cornerCases)}${preflightList('Нужно уточнить', review.questions)}${review.decomposition ? `<h5>Возможное разбиение</h5><p>${escapeHtml(review.decomposition)}</p><small>Это предложение: подзадачи не создаются автоматически.</small>` : ''}${(review.questions?.length && ['awaiting_clarification', 'blocked'].includes(task?.executionStatus) || task?.executionStatus === 'awaiting_development_clarification') ? `<form data-clarification-form data-task-id="${taskId}">${review.answers ? `<p><strong>Ранее уточнено:</strong><br>${escapeHtml(review.answers)}</p>` : ''}<label>${task.executionStatus === 'awaiting_development_clarification' ? 'Ответ на вопрос агента' : 'Ответ на вопросы'}<textarea name="answers" maxlength="4000" required placeholder="Новый ответ для агента"></textarea></label><button type="submit">Сохранить ответ</button><span data-clarification-status></span></form>` : ''}${task?.executionStatus === 'awaiting_clarification' ? `<div class="task-preflight-approval"><p>Подтверждение начинает разработку в отдельном Git worktree. Коммит будет локальным; push сделает человек.</p><button type="button" data-approve-preflight="${taskId}" ${review.questionsPending ? 'disabled' : ''}>К разработке</button></div>` : ''}` : '<h4>Разбор перед разработкой</h4><p>После постановки в очередь агент изучит проект и объяснит задачу, риски и крайние случаи. Код не изменяется на этом этапе.</p>';
  } catch (error) {
    if (panel === document.querySelector('#task-preflight')) panel.innerHTML = `<h4>Разбор перед разработкой</h4><p>${escapeHtml(error.message)}</p>`;
  }
}

document.addEventListener('submit', async event => {
  const form = event.target.closest('[data-clarification-form]');
  if (!form) return;
  event.preventDefault();
  const status = form.querySelector('[data-clarification-status]');
  try {
    const response = await fetch(`/api/tasks/${form.dataset.taskId}/preflight/answers`, {
      method: 'PUT', headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({answers: new FormData(form).get('answers')}),
    });
    if (!response.ok) throw new Error((await response.json().catch(() => ({}))).error || `HTTP ${response.status}`);
    status.textContent = 'Ответы сохранены';
    form.querySelector('textarea').blur();
    await loadGraph();
  } catch (error) { status.textContent = error.message; }
});

document.addEventListener('submit', async event => {
  const form = event.target.closest('[data-description-form]');
  if (!form) return;
  event.preventDefault();
  const status = form.querySelector('[role="status"]');
  const button = form.querySelector('[type="submit"]');
  button.disabled = true;
  status.textContent = 'Сохраняю…';
  try {
    const collection = form.dataset.nodeKind === 'task' ? 'tasks' : 'candidates';
    const response = await fetch(`/api/${collection}/${form.dataset.nodeId}/description`, {
      method: 'PATCH', headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({description: new FormData(form).get('description')}),
    });
    if (!response.ok) throw new Error((await response.json().catch(() => ({}))).error || `HTTP ${response.status}`);
    await loadGraph();
  } catch (error) {
    status.textContent = error.message;
  } finally {
    button.disabled = false;
  }
});

async function loadTaskComments(taskId) {
  const requestId = ++taskCommentRequest;
  const panel = document.querySelector('#task-comments');
  if (!panel) return;
  if (panel.contains(document.activeElement) && ['TEXTAREA', 'INPUT'].includes(document.activeElement.tagName)) return;
  try {
    const response = await fetch(`/api/tasks/${taskId}/mr-comments`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const comments = await response.json();
    if (requestId !== taskCommentRequest || selectedKey !== `task:${taskId}`) return;
    const codeHost = snapshot.nodes.find(node => node.key === `task:${taskId}`)?.codeHost || 'gitlab';
    panel.innerHTML = `<h4>Комментарии ${codeHost === 'github' ? 'PR' : 'MR'} <span>${comments.filter(comment => !comment.reviewedAt).length} новых</span></h4>${comments.length ? comments.map(comment => {
      const replyStatus = comment.replyStatus || '';
      const sending = replyStatus === 'queued' || replyStatus === 'sending';
      const replyPanel = codeHost === 'github'
        ? '<p class="mr-reply-state">GitHub MCP подключён только для чтения. Ответьте на PR напрямую в GitHub.</p>'
        : replyStatus === 'sent'
        ? `<div class="mr-reply-state sent">Ответ опубликован${comment.replyExternalId ? ` · #${escapeHtml(comment.replyExternalId)}` : ''}</div>`
        : sending
          ? `<div class="mr-reply-state">${replyStatus === 'queued' ? 'Ответ ожидает отправки…' : 'Отправляем подтверждённый ответ…'}</div>`
          : `<form class="mr-reply-form" data-reply-form data-task-id="${taskId}" data-comment-id="${escapeHtml(comment.id)}"><label>Черновик ответа<textarea maxlength="10000" data-reply-body placeholder="Напишите ответ для этой дискуссии…">${escapeHtml(comment.replyBody || '')}</textarea></label><div class="mr-reply-actions"><button type="button" data-save-reply>Сохранить черновик</button>${replyStatus === 'draft' ? '<button type="button" class="confirm-reply" data-confirm-reply>Подтвердить и опубликовать</button>' : ''}</div>${replyStatus === 'failed' ? `<p class="mr-reply-error">${escapeHtml(comment.replyError)} Проверьте MR перед повторной отправкой.</p>` : ''}</form>`;
      return `<article class="mr-comment ${comment.reviewedAt ? 'reviewed' : 'unreviewed'}"><div class="mr-comment-meta"><strong>${escapeHtml(comment.author)}</strong><time>${escapeHtml(comment.createdAt)}</time>${comment.url ? `<a href="${escapeHtml(comment.url)}" target="_blank" rel="noopener noreferrer">Открыть</a>` : ''}</div><p>${escapeHtml(comment.body)}</p>${comment.agentHandledAt ? '<span class="mr-comment-state">Учтён в рабочем цикле</span>' : ''}${comment.reviewedAt ? '<span class="mr-comment-state">Просмотрено локально</span>' : `<button type="button" data-mark-comment data-task-id="${taskId}" data-comment-id="${escapeHtml(comment.id)}">Отметить просмотренным</button>`}${replyPanel}</article>`;
    }).join('') : '<p class="mr-comments-empty">Комментариев пока нет. Опрос MR выполняется в фоне.</p>'}`;
  } catch (error) {
    if (requestId === taskCommentRequest && selectedKey === `task:${taskId}`)
      panel.innerHTML = `<h4>Комментарии MR</h4><p class="mr-comments-empty">Не удалось загрузить: ${escapeHtml(error.message)}</p>`;
  }
}

async function markCommentReviewed(taskId, commentId) {
  try {
    const response = await fetch(`/api/tasks/${taskId}/mr-comments/reviewed`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({commentId}),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    await loadTaskComments(taskId);
  } catch (error) {
    const panel = document.querySelector('#task-comments');
    if (panel) panel.insertAdjacentHTML('beforeend', `<p class="mr-comments-error">Не удалось отметить комментарий: ${escapeHtml(error.message)}</p>`);
  }
}

async function saveReplyDraft(taskId, commentId, body) {
  try {
    const response = await fetch(`/api/tasks/${taskId}/mr-comments/reply-draft`, {
      method: 'PUT',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({commentId, body}),
    });
    if (!response.ok) {
      const result = await response.json().catch(() => ({}));
      throw new Error(result.error || `HTTP ${response.status}`);
    }
    await loadTaskComments(taskId);
  } catch (error) {
    showReplyError(taskId, error.message);
  }
}

async function confirmReply(taskId, commentId) {
  if (!window.confirm('Опубликовать этот текст ответом в GitLab? После подтверждения ответ будет отправлен через настроенный MCP.')) return;
  try {
    const response = await fetch(`/api/tasks/${taskId}/mr-comments/reply-confirmation`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({commentId}),
    });
    if (!response.ok) {
      const result = await response.json().catch(() => ({}));
      throw new Error(result.error || `HTTP ${response.status}`);
    }
    await loadTaskComments(taskId);
  } catch (error) {
    showReplyError(taskId, error.message);
  }
}

function showReplyError(taskId, message) {
  const panel = document.querySelector('#task-comments');
  const form = panel?.querySelector(`[data-reply-form][data-task-id="${taskId}"]`);
  if (form) form.insertAdjacentHTML('beforeend', `<p class="mr-reply-error">${escapeHtml(message)}</p>`);
}

document.addEventListener('click', event => {
  const fileHit = event.target.closest('[data-file-search-node]');
  if (fileHit) {
    selectedKey = fileHit.dataset.fileSearchNode;
    fileSearchFocusPath = fileHit.dataset.fileSearchPath;
    renderGraph();
    requestAnimationFrame(() => document.querySelector('[data-inspector-tab="files"]')?.click());
  }
  const inspectorTab = event.target.closest('[data-inspector-tab]');
  if (inspectorTab) {
    const inspector = inspectorTab.closest('#inspector');
    const name = inspectorTab.dataset.inspectorTab;
    inspector.querySelectorAll('[data-inspector-tab]').forEach(tab => tab.classList.toggle('active', tab === inspectorTab));
    inspector.querySelectorAll('[data-inspector-panel]').forEach(panel => { panel.hidden = panel.dataset.inspectorPanel !== name; });
    const node = snapshot.nodes.find(candidate => candidate.key === selectedKey);
    if (name === 'files') loadInspectorFiles(node);
    if (name === 'history') loadInspectorHistory(node);
  }
  if (event.target.closest('[data-edit-description]')) beginDescriptionEdit();
  if (event.target.closest('[data-cancel-description]')) renderInspector();
  if (event.target.closest('[data-close-inspector]')) {
    selectedKey = null;
    renderGraph();
  }
  const menuToggle = event.target.closest('[data-inspector-menu-toggle]');
  if (menuToggle) {
    const menu = menuToggle.parentElement.querySelector('[data-inspector-menu]');
    menu.hidden = !menu.hidden;
    menuToggle.setAttribute('aria-expanded', String(!menu.hidden));
  }
  const copyNodeId = event.target.closest('[data-copy-node-id]');
  if (copyNodeId) {
    const key = copyNodeId.dataset.copyNodeId;
    copyText(key).then(copied => {
      copyNodeId.textContent = copied ? 'Идентификатор скопирован' : `Не удалось скопировать: ${key}`;
    });
  }
  const notificationRow = event.target.closest('[data-notification-task]');
  if (notificationRow) {
    document.querySelector('#notifications-popover').hidden = true;
    document.querySelector('#notifications-toggle').setAttribute('aria-expanded', 'false');
    selectNode(notificationRow.dataset.notificationTask);
  }
  const button = event.target.closest('[data-mark-comment]');
  if (button) markCommentReviewed(Number(button.dataset.taskId), button.dataset.commentId);
  const saveButton = event.target.closest('[data-save-reply]');
  if (saveButton) {
    const form = saveButton.closest('[data-reply-form]');
    saveReplyDraft(Number(form.dataset.taskId), form.dataset.commentId, form.querySelector('[data-reply-body]').value);
  }
  const confirmButton = event.target.closest('[data-confirm-reply]');
  if (confirmButton) {
    const form = confirmButton.closest('[data-reply-form]');
    confirmReply(Number(form.dataset.taskId), form.dataset.commentId);
  }
  const queueButton = event.target.closest('[data-queue-task]');
  if (queueButton) updateExecution(Number(queueButton.dataset.queueTask), 'queue');
  const approveButton = event.target.closest('[data-approve-preflight]');
  if (approveButton) updateTaskAction(`/api/tasks/${approveButton.dataset.approvePreflight}/preflight/approve`, 'POST');
  const retryWorktree = event.target.closest('[data-retry-worktree]');
  if (retryWorktree) updateTaskAction(`/api/tasks/${retryWorktree.dataset.retryWorktree}/worktree/retry`, 'POST');
  const resumeDevelopment = event.target.closest('[data-resume-development]');
  if (resumeDevelopment) updateTaskAction(`/api/tasks/${resumeDevelopment.dataset.resumeDevelopment}/development/resume`, 'POST');
  const retryMr = event.target.closest('[data-retry-mr]');
  if (retryMr) updateTaskAction(`/api/tasks/${retryMr.dataset.retryMr}/mr/retry`, 'POST');
  const rejectButton = event.target.closest('[data-reject-task]');
  if (rejectButton && window.confirm('Закрыть задачу? Она не будет выполнена из очереди.'))
    updateExecution(Number(rejectButton.dataset.rejectTask), 'reject');
});

async function saveProjectRepository(event, projectId) {
  event.preventDefault();
  const values = new FormData(event.currentTarget);
  await updateTaskAction(`/api/projects/${projectId}/repository`, 'PUT', {
    repositoryPath: values.get('repositoryPath'), codeHost: values.get('codeHost') || 'gitlab',
  });
}

async function saveProjectRemoteRepositoryUrl(event, projectId) {
  event.preventDefault();
  const values = new FormData(event.currentTarget);
  await updateTaskAction(`/api/projects/${projectId}/remote-repository-url`, 'PUT', {
    remoteRepositoryUrl: values.get('remoteRepositoryUrl'),
  });
}

async function updateExecution(taskId, action) {
  await updateTaskAction(`/api/tasks/${taskId}/${action}`, 'POST');
}

async function updateTaskAction(url, method, body) {
  const status = document.querySelector('#task-action-status');
  if (status) status.textContent = 'Сохраняю…';
  const buttons = [...document.querySelectorAll('.task-execution-actions button, .task-repository-form button, .task-remote-repository-form button')];
  buttons.forEach(button => { button.disabled = true; });
  try {
    const response = await fetch(url, {method, headers: {'Content-Type': 'application/json'}, ...(body ? {body: JSON.stringify(body)} : {})});
    if (!response.ok) {
      const result = await response.json().catch(() => ({}));
      throw new Error(result.error || `HTTP ${response.status}`);
    }
    await loadGraph();
    const completedStatus = document.querySelector('#task-action-status');
    if (completedStatus) completedStatus.textContent = 'Сохранено';
  } catch (error) {
    const current = document.querySelector('#task-action-status');
    if (current) current.textContent = error.message;
    document.querySelectorAll('.task-execution-actions button, .task-repository-form button, .task-remote-repository-form button').forEach(button => { button.disabled = false; });
  }
}

async function saveTask(event, id) {
  event.preventDefault();
  const form = event.currentTarget;
  const status = form.querySelector('#task-save-status');
  const data = new FormData(form);
  status.textContent = 'Сохраняю…';
  try {
    const response = await fetch(`/api/tasks/${id}`, {
      method: 'PATCH',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({mergeRequestUrl: data.get('mergeRequestUrl') || null}),
    });
    if (!response.ok) {
      const result = await response.json().catch(() => ({}));
      throw new Error(result.error || `HTTP ${response.status}`);
    }
    status.textContent = 'Сохранено';
    await loadGraph();
    selectedKey = `task:${id}`;
    renderGraph();
  } catch (error) {
    status.textContent = error.message;
  }
}

async function saveMrStatus(event, id) {
  event.preventDefault();
  const status = new FormData(event.currentTarget).get('status');
  await updateTaskAction(`/api/tasks/${id}/mr-status`, 'PATCH', {status});
}

function openCandidate(id) {
  window.location.href = `/index.html?candidateId=${encodeURIComponent(id)}`;
}

document.querySelector('#node-search').addEventListener('input', event => {
  query = event.target.value.trim();
  document.querySelector('.global-search input').value = event.target.value;
  renderGraph();
  searchFiles(query);
});
document.querySelector('#notifications-toggle').addEventListener('click', () => {
  const button = document.querySelector('#notifications-toggle');
  const popover = document.querySelector('#notifications-popover');
  popover.hidden = !popover.hidden;
  button.setAttribute('aria-expanded', String(!popover.hidden));
});
document.addEventListener('keydown', event => {
  if (event.key === 'Escape') {
    const popover = document.querySelector('#notifications-popover');
    popover.hidden = true;
    document.querySelector('#notifications-toggle').setAttribute('aria-expanded', 'false');
    const menu = document.querySelector('[data-inspector-menu]');
    if (menu) {
      menu.hidden = true;
      document.querySelector('[data-inspector-menu-toggle]')?.setAttribute('aria-expanded', 'false');
    }
  }
});
document.addEventListener('pointerdown', event => {
  const popover = document.querySelector('#notifications-popover');
  if (!event.target.closest('.notification-control') && !popover.hidden) {
    popover.hidden = true;
    document.querySelector('#notifications-toggle').setAttribute('aria-expanded', 'false');
  }
  const menu = document.querySelector('[data-inspector-menu]');
  if (menu && !event.target.closest('.inspector-menu-wrap')) {
    menu.hidden = true;
    document.querySelector('[data-inspector-menu-toggle]')?.setAttribute('aria-expanded', 'false');
  }
});
document.querySelector('.global-search input').addEventListener('input', event => {
  query = event.target.value.trim();
  document.querySelector('#node-search').value = event.target.value;
  renderGraph();
  searchFiles(query);
});
document.addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault();
    document.querySelector('.global-search input').focus();
  }
});
document.querySelectorAll('.node-filters button').forEach(button => button.addEventListener('click', () => {
  selectedKind = button.dataset.kind;
  document.querySelectorAll('.node-filters button').forEach(item => item.classList.toggle('active', item === button));
  renderGraph();
}));
document.querySelector('#project-filter').addEventListener('change', event => {
  selectedProject = event.target.value;
  renderGraph();
});
document.querySelector('.filter-button').addEventListener('click', () => {
  const filter = document.querySelector('#project-filter');
  filter.hidden = !filter.hidden;
  if (!filter.hidden) filter.focus();
});
document.querySelector('.sidebar-collapse').addEventListener('click', event => {
  const workspace = document.querySelector('.graph-workspace');
  workspace.classList.toggle('sidebar-collapsed');
  event.currentTarget.textContent = workspace.classList.contains('sidebar-collapsed') ? '›' : '‹';
});
installGraphBoardControls();
loadGraph();
setInterval(() => {
  if (selectedKey?.startsWith('task:') && !['TEXTAREA', 'INPUT'].includes(document.activeElement.tagName))
    loadGraph();
}, 15000);
