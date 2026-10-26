const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
let selectedId = null;
let pollHandle = null;
let candidates = [];

async function request(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail || body.error || `HTTP ${response.status}`);
  }
  return response.json();
}

async function loadQueue() {
  const queue = document.querySelector('#queue');
  try {
    candidates = await request('/api/candidates?status=pending');
    document.querySelector('#queue-count').textContent = String(candidates.length).padStart(2, '0');
    queue.innerHTML = candidates.length ? candidates.map(candidate => `
      <button class="candidate-item ${candidate.id === selectedId ? 'selected' : ''}" onclick="selectCandidate(${candidate.id})">
        <span class="candidate-item-head"><span class="review-pill">${candidate.agentStatus === 'ready' ? 'Готово к разработке' : candidate.agentStatus === 'running' ? 'Анализируется' : candidate.agentStatus === 'failed' ? 'Ошибка анализа' : 'На рассмотрении'}</span><span class="queue-chevron">›</span></span>
        <span class="candidate-title">${escapeHtml(candidate.localizedTitle || 'Перевод заголовка готовится')}</span>
        <span class="candidate-source">Источник: Boy Scout</span>
        <span class="candidate-meta"><span>${escapeHtml(candidate.sourceReportId)}</span><span>${relativeTime(candidate.createdAt)}</span></span>
      </button>`).join('') : '<div class="queue-state"><span class="queue-empty-icon">⌑</span><strong>Других кандидатов нет</strong><small>Новые одобренные кандидаты появятся здесь автоматически.</small></div>';
    return candidates;
  } catch (error) {
    queue.innerHTML = `<div class="queue-state error">Не удалось загрузить очередь: ${escapeHtml(error.message)}</div>`;
    return [];
  }
}

function relativeTime(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const minutes = Math.max(1, Math.floor((Date.now() - date.getTime()) / 60000));
  return minutes < 60 ? `${minutes} мин назад` : minutes < 1440 ? `${Math.floor(minutes / 60)} ч назад` : `${Math.floor(minutes / 1440)} дн назад`;
}

async function selectCandidate(id) {
  clearTimeout(pollHandle);
  selectedId = id;
  await loadQueue();
  const workspace = document.querySelector('#workspace');
  workspace.innerHTML = '<div class="panel loading-panel"><i class="spinner"></i> Загружаю контекст кандидата…</div>';
  try {
    const data = await request(`/api/agent/candidates/${id}`);
    const context = data.review ? {...data.context, exploration: data.review.context.exploration} : data.context;
    render(context, data.review, data.job);
    if (data.job?.status === 'queued' || data.job?.status === 'running') pollJob(id);
  } catch (error) {
    workspace.innerHTML = `<div class="notice">${escapeHtml(error.message)}</div>`;
  }
}

function render(context, review, job, notice = null) {
  const workspace = document.querySelector('#workspace');
  const nodes = context.nodes.slice(0, 12);
  const positions = new Map([[context.candidateId, {x:600, y:218}]]);
  const rx = nodes.length > 8 ? 455 : 405;
  const ry = nodes.length > 8 ? 178 : 150;
  nodes.forEach((node, index) => {
    const angle = -Math.PI / 2 + Math.PI * 2 * index / Math.max(nodes.length, 1);
    positions.set(node.id, {x:600 + Math.cos(angle) * rx, y:218 + Math.sin(angle) * ry});
  });
  const edges = context.edges.filter(edge => positions.has(edge.fromId) && positions.has(edge.toId)).map(edge => {
    const from = positions.get(edge.fromId); const to = positions.get(edge.toId);
    return `<line class="relation-${escapeHtml(edge.relation)}" x1="${from.x}" y1="${from.y}" x2="${to.x}" y2="${to.y}" marker-end="url(#arrow)"/>`;
  }).join('');
  const nodeCards = nodes.map(node => {
    const point = positions.get(node.id);
    return `<div class="graph-node" style="left:${point.x / 12}%;top:${point.y / 4.36}%"><span class="node-kicker">УЗЕЛ #${node.id}</span><span class="node-title">${escapeHtml(node.localizedTitle || 'Перевод не готов')}</span><span class="node-project">${escapeHtml(node.project)}</span></div>`;
  }).join('');
  const root = `<div class="graph-node root" style="left:50%;top:50%"><span class="node-kicker">КАНДИДАТ #${context.candidateId}</span><span class="node-title">${escapeHtml(context.localizedCandidateTitle || 'Перевод заголовка готовится')}</span><span class="node-project">источник Boy Scout</span></div>`;
  const emptyGraph = nodes.length ? '' : `<div class="graph-empty"><svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="7" cy="16" r="3"></circle><circle cx="24" cy="7" r="3"></circle><circle cx="24" cy="25" r="3"></circle><path d="m10 15 11-6M10 17l11 6"></path></svg><strong>Связей пока нет</strong><span>${context.availableNodeCount === 0 ? 'После создания задачи здесь появится её окружение в графе.' : 'Агент ещё проверяет подходящие узлы.'}</span></div>`;
  const position = Math.max(1, candidates.findIndex(candidate => candidate.id === context.candidateId) + 1);
  const sourceId = context.sourceReportId || `#${context.candidateId}`;
  const title = context.localizedCandidateTitle || 'Перевод заголовка готовится';
  const description = context.localizedCandidateDescription || 'Русское описание появится после фонового разбора.';

  workspace.innerHTML = `
    ${renderWorkflow(job)}
    <div class="candidate-heading"><span class="eyebrow">Кандидат ${position} из ${candidates.length} · ${escapeHtml(sourceId)}</span><h1>${escapeHtml(title)}</h1>
      <div class="candidate-badges"><span class="source-badge"><svg viewBox="0 0 20 20" aria-hidden="true"><path d="M10 2.5 16 5v4.6c0 3.7-2.5 6.2-6 7.9-3.5-1.7-6-4.2-6-7.9V5z"></path><path d="m7.4 9.8 1.7 1.7 3.6-3.7"></path></svg>Boy Scout</span><span class="candidate-status-badge">Одобренный кандидат</span></div>
    </div>
    ${notice ? `<div class="notice ${notice.kind === 'success' ? 'success' : ''}">${escapeHtml(notice.text)}</div>` : ''}
    <div class="candidate-panels">
      <article class="panel detected-card"><h2><svg viewBox="0 0 20 20" aria-hidden="true"><rect x="3" y="3" width="14" height="14" rx="2"></rect><path d="M6.5 7h7M6.5 10h7M6.5 13h4"></path></svg>Что обнаружено</h2><div class="detected-copy">${escapeHtml(description)}</div>
        <details class="source-original"><summary>Показать исходный текст и детали (Boy Scout)</summary><strong>${escapeHtml(context.candidateTitle)}</strong><p>${escapeHtml(context.candidateDescription)}</p><a href="${escapeHtml(context.sourceUrl || '#')}" target="_blank" rel="noreferrer">Открыть запись ${escapeHtml(sourceId)}</a></details>
      </article>
      <article class="panel recommendation-card"><div class="recommendation-heading"><h2><span class="recommendation-spark">✳</span>Рекомендация агента</h2><span class="agent-ready-badge" data-state="${escapeHtml(job?.status || 'queued')}"><i></i>${escapeHtml(agentStatusLabel(job?.status))}</span></div>
        ${review ? renderRecommendation(review, context) : `<div class="recommendation-pending"><i class="spinner"></i><span>${job?.status === 'running' ? 'Агент проверяет контекст и ищет подходящий узел…' : 'Агент скоро начнёт разбор в фоне.'}</span></div>`}
      </article>
    </div>
    <section class="panel graph-section"><div class="graph-section-heading"><h2><svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="4" cy="10" r="2"></circle><circle cx="15.5" cy="4" r="2"></circle><circle cx="15.5" cy="16" r="2"></circle><path d="m6 9 7.5-4M6 11l7.5 4"></path></svg>Контекст в графе</h2><span class="graph-count">${context.nodes.length} связанных узлов</span></div>
      <div class="graph-card"><div class="graph-scroll"><div class="graph-map"><svg class="graph-lines" viewBox="0 0 1200 436" preserveAspectRatio="none" aria-hidden="true"><defs><marker id="arrow" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto"><path d="M0,0 L0,6 L7,3 z" fill="#91a69a"></path></marker></defs>${edges}</svg>${root}${nodeCards}${emptyGraph}</div></div></div>
      ${renderExploration(context)}
    </section>`;
}

function renderWorkflow(job) {
  const active = job?.status === 'queued' ? 0 : job?.status === 'running' || job?.status === 'failed' ? 1 : 3;
  const steps = [['Входящий', 'Новый кандидат', 'inbox'], ['Анализ', 'Проверка контекста', 'search'], ['Маршрут', 'Поиск подходящего узла', 'route'], ['Решение', 'Готово к разработке', 'ready']];
  const retry = job?.status === 'failed' ? '<button class="workflow-retry" onclick="queueAnalysis()">Повторить анализ</button>' : '';
  return `<div class="workflow-steps">${steps.map(([title, subtitle, icon], index) => `
    <div class="workflow-step ${index < active ? 'complete' : index === active ? 'active' : ''}"><span class="workflow-icon">${workflowIcon(icon)}</span><strong>${title}</strong><small>${index === 3 && job?.status === 'failed' ? 'Нужен повтор анализа' : subtitle}</small></div>${index < 3 ? '<span class="workflow-arrow" aria-hidden="true">→</span>' : ''}`).join('')}${retry}</div>`;
}

function workflowIcon(icon) {
  if (icon === 'inbox') return '<svg viewBox="0 0 20 20" aria-hidden="true"><path d="M3 10.5h4l1.5 2h3l1.5-2h4M4 5h12l1 5v6H3v-6z"></path></svg>';
  if (icon === 'search') return '<svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="8.7" cy="8.7" r="5.5"></circle><path d="m13 13 4 4"></path></svg>';
  if (icon === 'route') return '<svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="5" cy="15" r="2"></circle><circle cx="15" cy="5" r="2"></circle><path d="M7 15h3a3 3 0 0 0 3-3V8"></path><path d="m11 10 2-2 2 2"></path></svg>';
  return '<svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="10" cy="10" r="7"></circle><path d="m6.5 10 2.3 2.3 4.8-5"></path></svg>';
}

function renderExploration(context) {
  if (!context.exploration.length) return '';
  return `<details class="exploration-details"><summary>Путь агента · ${context.exploration.length} шагов</summary><div class="trace-list">${context.exploration.map((step, index) => `
    <div class="trace-item"><span class="trace-index">${String(index + 1).padStart(2, '0')}</span><div><div class="trace-label">${step.action === 'search' ? 'Поиск по графу' : 'Раскрытие узла'}</div><div class="trace-query">${escapeHtml(step.request)}</div><div class="trace-results">${step.returnedNodeIds.length ? step.returnedNodeIds.map(id => `#${id}`).join(' · ') : 'совпадений нет'}</div></div></div>`).join('')}</div></details>`;
}

function renderRecommendation(review, context) {
  const suggestions = review.suggestions || [];
  const taskPlan = review.taskPlan;
  if (!taskPlan) {
    return review && (context.availableTaskCount === 0 || context.nodes.some(node => node.kind === 'task'))
      ? `<p class="review-summary">${escapeHtml(review.summary)}</p><div class="recommendation-pending"><span>Разбор этой записи создан до появления плана задачи.</span><button class="action-button" onclick="queueAnalysis()">Сформировать план задачи</button></div>`
      : '<div class="notice">Ожидается план задачи от агента.</div>';
  }
  if (taskPlan?.status === 'rejected') {
    return `<p class="review-summary">${escapeHtml(review.summary)}</p><div class="task-plan rejected"><span class="eyebrow">План отклонён</span><h3>${escapeHtml(taskPlan.title)}</h3><p>${escapeHtml(taskPlan.rationale)}</p></div><div class="human-actions"><button class="action-button" onclick="queueAnalysis()">Попросить агента пересмотреть</button><button class="reject-candidate-action" onclick="triageCandidate('dismissed')">Не брать в работу</button></div>`;
  }
  const taskRecommendation = renderTaskPlan(taskPlan, context);
  const linkRecommendations = suggestions.length
    ? `<div class="recommendation-box"><div class="recommendation-main"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="6" cy="18" r="2.5"></circle><circle cx="18" cy="6" r="2.5"></circle><path d="M8.5 18h3a4 4 0 0 0 4-4V8.5"></path><path d="m13 11 2.5-2.5L18 11"></path></svg><div><h3>Присоединить к существующему узлу</h3><p>Агент нашёл возможные точки связи. Выберите предложения, которые следует добавить в граф.</p></div></div>
      <div class="proposal-list">${suggestions.map(suggestion => `<article class="proposal-card"><div class="proposal-top"><span class="relation-tag">${escapeHtml(relationLabel(suggestion.relation))}</span><span class="proposal-state">${escapeHtml(suggestionStatusLabel(suggestion.status))}</span></div><div class="proposal-target">#${suggestion.candidateId} · ${escapeHtml(suggestion.candidateTitle)}</div><div class="proposal-reason">${escapeHtml(suggestion.rationale)}</div>${suggestion.status === 'pending' ? `<div class="proposal-actions"><button class="action-button" onclick="decide(${suggestion.id}, true)">Подтвердить связь</button><button class="action-button reject" onclick="decide(${suggestion.id}, false)">Отклонить</button></div>` : ''}</article>`).join('')}</div></div>`
    : '';
  return `<p class="review-summary">${escapeHtml(review.summary)}</p>${taskRecommendation}${linkRecommendations}${taskPlan?.status === 'pending' ? '<div class="human-actions"><button class="reject-candidate-action" onclick="triageCandidate(\'dismissed\')">Отклонить кандидата</button></div>' : ''}`;
}

function renderTaskPlan(plan, context) {
  if (!plan) return '<div class="notice">Агент не сформировал план задачи.</div>';
  const existing = plan.targetTaskId ? context.nodes.find(node => node.kind === 'task' && node.taskId === plan.targetTaskId) : null;
  const status = plan.status;
  if (status === 'accepted') {
    const actionText = plan.action === 'create_task' ? 'Задача создана в графе' : plan.action === 'update_existing' ? 'Существующая задача обновлена' : 'Кандидат присоединён к задаче';
    return `<div class="task-plan accepted"><div class="task-plan-head"><span class="task-plan-icon">✓</span><div><span class="eyebrow">Подтверждено человеком</span><h3>${actionText}</h3></div></div><strong>${escapeHtml(plan.title)}</strong><p>${escapeHtml(plan.description)}</p><small>Критерии готовности: ${escapeHtml(plan.acceptanceCriteria)}</small></div>`;
  }
  if (status === 'rejected') return `<div class="task-plan rejected"><span class="eyebrow">План отклонён</span><h3>${escapeHtml(plan.title)}</h3><p>${escapeHtml(plan.rationale)}</p></div>`;

  const actionTitle = plan.action === 'create_task' ? 'Создать задачу в графе' : plan.action === 'update_existing' ? 'Предложить изменение существующей задачи' : 'Присоединить кандидата к задаче';
  const taskDetails = plan.action === 'update_existing' && existing
    ? `<div class="task-diff"><div><span>СЕЙЧАС</span><strong>${escapeHtml(existing.localizedTitle || existing.title)}</strong><p>${escapeHtml(existing.localizedDescription || existing.description)}</p><small>${escapeHtml(existing.acceptanceCriteria || '')}</small></div><div><span>ПРЕДЛОЖЕНО</span><strong>${escapeHtml(plan.title)}</strong><p>${escapeHtml(plan.description)}</p><small>${escapeHtml(plan.acceptanceCriteria)}</small></div></div>`
    : plan.action === 'attach_existing' && existing
      ? `<div class="task-draft"><strong>${escapeHtml(existing.localizedTitle || existing.title)}</strong><p>${escapeHtml(existing.localizedDescription || existing.description)}</p><small><b>Критерии готовности</b>${escapeHtml(existing.acceptanceCriteria || '')}</small></div>`
      : `<div class="task-draft"><strong>${escapeHtml(plan.title)}</strong><p>${escapeHtml(plan.description)}</p><small><b>Критерии готовности</b>${escapeHtml(plan.acceptanceCriteria)}</small></div>`;
  return `<div class="task-plan"><div class="task-plan-head"><span class="task-plan-icon">${plan.action === 'create_task' ? '+' : plan.action === 'update_existing' ? '↻' : '↗'}</span><div><span class="eyebrow">Рекомендация агента</span><h3>${actionTitle}</h3></div></div>
    ${existing && plan.action !== 'create_task' ? `<div class="task-target">Связанная задача #${plan.targetTaskId} · ${escapeHtml(existing.localizedTitle || existing.title)}</div>` : ''}
    ${taskDetails}
    <div class="recommendation-why"><strong>Почему так?</strong><p>${escapeHtml(plan.rationale)}</p></div>
    <button class="primary-action task-confirm" onclick="decideTaskPlan(${plan.id}, true)">${plan.action === 'create_task' ? 'Создать задачу в графе' : plan.action === 'update_existing' ? 'Подтвердить изменение' : 'Присоединить к задаче'}</button>
    <button class="task-decline" onclick="decideTaskPlan(${plan.id}, false)">Отклонить предложение</button>
  </div>`;
}

function relationLabel(relation) {
  return ({related: 'связана с', duplicate: 'дубликат', depends_on: 'зависит от', blocks: 'блокирует'})[relation] || relation;
}

function suggestionStatusLabel(status) {
  return ({pending: 'ждёт подтверждения', accepted: 'подтверждено', rejected: 'отклонено'})[status] || status;
}

function agentStatusLabel(status) {
  return ({queued: 'В очереди', running: 'Анализирует', ready: 'Готово к разработке', failed: 'Ошибка анализа'})[status] || 'Ожидает анализа';
}

async function triageCandidate(status) {
  try {
    await request(`/api/candidates/${selectedId}/triage`, {method: 'PATCH', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({status})});
    selectedId = null;
    const remaining = await loadQueue();
    if (remaining.length) await selectCandidate(remaining[0].id);
    else document.querySelector('#workspace').innerHTML = '<div class="panel welcome"><span class="eyebrow">Очередь разобрана</span><h1>Все кандидаты<br>рассмотрены.</h1><p>Новые одобренные кандидаты появятся здесь автоматически.</p></div>';
  } catch (error) {
    document.querySelector('#workspace').insertAdjacentHTML('afterbegin', `<div class="notice">${escapeHtml(error.message)}</div>`);
  }
}

async function queueAnalysis() {
  try {
    await request(`/api/agent/candidates/${selectedId}/analyze`, {method: 'POST'});
    pollJob(selectedId);
  } catch (error) {
    document.querySelector('#workspace').insertAdjacentHTML('afterbegin', `<div class="notice">${escapeHtml(error.message)}</div>`);
  }
}

async function pollJob(candidateId) {
  clearTimeout(pollHandle);
  try {
    const data = await request(`/api/agent/candidates/${candidateId}`);
    if (selectedId !== candidateId) return;
    const context = data.review ? {...data.context, exploration: data.review.context.exploration} : data.context;
    render(context, data.review, data.job);
    await loadQueue();
    if (data.job?.status === 'queued' || data.job?.status === 'running') pollHandle = setTimeout(() => pollJob(candidateId), 1800);
  } catch (error) {
    if (selectedId === candidateId) {
      document.querySelector('#workspace').insertAdjacentHTML('afterbegin', `<div class="notice">Не удалось обновить состояние агента: ${escapeHtml(error.message)}</div>`);
      pollHandle = setTimeout(() => pollJob(candidateId), 4000);
    }
  }
}

async function decide(suggestionId, accept) {
  try {
    const review = await request(`/api/agent/candidates/${selectedId}/suggestions/${suggestionId}/decision`, {method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({accept})});
    const latest = await request(`/api/agent/candidates/${selectedId}`);
    render({...latest.context, exploration: review.context.exploration}, review, latest.job, {kind: 'success', text: accept ? 'Связь добавлена в граф.' : 'Предложение отклонено.'});
  } catch (error) {
    document.querySelector('#workspace').insertAdjacentHTML('afterbegin', `<div class="notice">${escapeHtml(error.message)}</div>`);
  }
}

async function decideTaskPlan(planId, accept) {
  try {
    const review = await request(`/api/agent/candidates/${selectedId}/task-plans/${planId}/decision`, {
      method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({accept}),
    });
    const latest = await request(`/api/agent/candidates/${selectedId}`);
    const context = {...latest.context, exploration: review.context.exploration};
    const result = review.taskPlan?.action === 'create_task'
      ? 'Задача создана в графе.'
      : review.taskPlan?.action === 'update_existing'
        ? 'Существующая задача обновлена в Todo Graph.'
        : 'Кандидат присоединён к существующей задаче.';
    render(context, review, latest.job, {kind: accept ? 'success' : '', text: accept ? result : 'Предложение отклонено; задача и граф не изменены.'});
    await loadQueue();
  } catch (error) {
    document.querySelector('#workspace').insertAdjacentHTML('afterbegin', `<div class="notice">${escapeHtml(error.message)}</div>`);
  }
}

loadQueue().then(() => {
  const candidateId = Number(new URLSearchParams(location.search).get('candidateId'));
  if (candidateId > 0) selectCandidate(candidateId);
});
