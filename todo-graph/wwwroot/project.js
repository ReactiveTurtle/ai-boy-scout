const projectQueryId = new URLSearchParams(location.search).get('projectId');
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let project, tasks = [], candidates = [], allCandidates = [], graphSnapshot = {nodes: [], edges: []};
const errorText = async response => { const data = await response.json().catch(() => ({})); return data.error || data.detail || data.title || `HTTP ${response.status}`; };
const executionLabels = {idle:'Не в очереди',queued:'В очереди',clarifying:'Агент уточняет задачу',awaiting_clarification:'Ждёт ответа',ready_for_development:'Готова к preflight',preparing:'Подготовка worktree',prepared:'Готова к разработке',running:'В работе',awaiting_development_clarification:'Нужен ответ',awaiting_commit:'Ждёт коммита',committing:'Коммит',awaiting_push:'Ждёт push',awaiting_user_pr:'Ждёт GitHub PR',awaiting_mr_repository_url:'Ждёт URL GitLab-репозитория',pushed:'Push подтверждён',creating_mr:'Создаётся MR',code_review:'Code review',reviewing_comments:'Разбор комментариев',comments_reviewed:'Комментарии разобраны',blocked:'Заблокирована',failed:'Ошибка',closed:'Закрыта'};
const statusLabel = task => executionLabels[task.executionStatus] || ({todo:'К выполнению',in_progress:'В работе',done:'Выполнена',closed:'Закрыта'})[task.status] || task.status || 'Новая';
async function loadWorkspace() {
  if (!projectQueryId) { document.querySelector('#project-name').textContent = 'Не указан проект'; return; }
  const list = document.querySelector('#task-list');
  list.textContent = 'Загружаю задачи…';
  try {
    const [projectResponse, taskResponse, candidateResponse, graphResponse] = await Promise.all([fetch('/api/projects/repositories'),fetch(`/api/tasks?projectId=${encodeURIComponent(projectQueryId)}`),fetch(`/api/candidates?projectId=${encodeURIComponent(projectQueryId)}`),fetch('/api/graph')]);
    for (const r of [projectResponse,taskResponse,candidateResponse,graphResponse]) if (!r.ok) throw new Error(await errorText(r));
    const projects = await projectResponse.json();
    project = projects.find(p => String(p.sourceProjectId ?? p.id) === projectQueryId);
    if (!project) throw new Error('Проект не найден.');
    tasks = await taskResponse.json(); allCandidates = await candidateResponse.json(); candidates = allCandidates.filter(candidate => candidate.triageStatus === 'pending'); graphSnapshot = await graphResponse.json();
    document.querySelector('#project-name').textContent = project.project || project.name || 'Проект';
    document.querySelector('#project-path').textContent = project.repositoryPath || 'Локальный путь не задан';
    document.querySelector('#graph-link').href = '/graph.html';
    renderTasks(); renderCandidates(); await populateLinkTargets();
  } catch (e) { list.innerHTML = `<p class="project-error">Не удалось открыть проект: ${esc(e.message)}</p>`; document.querySelector('#candidate-list').innerHTML = `<p class="project-error">${esc(e.message)}</p>`; }
}
function renderTasks() {
  document.querySelector('#task-list').innerHTML = tasks.length ? tasks.map(task => {
    const key = `task:${task.id}`;
    const related = graphSnapshot.edges.filter(edge => edge.from === key || edge.to === key).map(edge => {
      const other = graphSnapshot.nodes.find(node => node.key === (edge.from === key ? edge.to : edge.from));
      return other?.kind === 'task' ? `<a href="/project.html?projectId=${encodeURIComponent(other.sourceProjectId)}" title="${esc(edge.relation)}">${esc(other.title)} <small>· ${esc(other.project)}</small></a>` : '';
    }).filter(Boolean);
    const sourceCandidate = allCandidates.find(candidate => (task.sourceReportIds || []).includes(candidate.sourceReportId));
    const sourceLabel = task.sourceTaskId ? 'Boy Scout' : sourceCandidate?.source === 'user' ? 'Пользователь' : sourceCandidate?.source === 'boyscout' ? 'Boy Scout' : null;
    return `<article class="project-task"><h3>${esc(task.title || task.name || 'Задача')}</h3><p>${esc(task.description || '')}</p><div class="project-meta"><span class="status-chip">${esc(statusLabel(task))}</span>${sourceLabel ? `<span class="source-chip ${sourceLabel === 'Boy Scout' ? 'boyscout' : ''}">${sourceLabel}</span>` : ''}${task.sourceTaskId ? `<span>${esc(task.sourceTaskId)}</span>` : ''}</div><div class="project-task-links">Связанные задачи: ${related.join(' · ') || 'нет'}</div><a class="show-graph-button" href="/graph.html?taskId=${encodeURIComponent(task.id)}">Показать в графе →</a></article>`;
  }).join('') : '<p>В этом проекте пока нет сформированных задач. Сырой запрос можно отправить в очередь ниже.</p>';
}
async function renderCandidates() {
  document.querySelector('#candidate-count').textContent = candidates.length;
  const container = document.querySelector('#candidate-list');
  if (!candidates.length) { container.innerHTML = '<p>Очередь проекта пуста.</p>'; return; }
  container.innerHTML = candidates.map(c => `<article class="project-candidate" data-candidate-id="${esc(c.id)}"><h3>${c.source === 'user' ? 'Сырая задача пользователя' : esc(c.localizedTitle || c.title || 'Кандидат Boy Scout')}</h3><p>${esc(c.rawText || c.observation || c.localizedDescription || '')}</p><div class="project-meta"><span class="source-chip ${c.source === 'boyscout' ? 'boyscout' : ''}">${c.source === 'boyscout' ? 'Boy Scout' : 'Пользователь'}</span><span class="status-chip">${esc(c.agentStatus || c.status || 'Новый')}</span><span>Раундов уточнения: ${c.clarificationRounds?.length || 0}</span></div><div class="review-state">Загружаю вопросы…</div></article>`).join('');
  await Promise.all(candidates.map(async c => {
    const item = container.querySelector(`[data-candidate-id="${CSS.escape(String(c.id))}"] .review-state`);
    try {
      const r = await fetch(`/api/agent/candidates/${encodeURIComponent(c.id)}`); if (!r.ok) throw new Error(await errorText(r));
      const result = await r.json(), review = result.review || result;
      const rounds = c.clarificationRounds || [];
      const lastRound = rounds.at(-1);
      const questions = lastRound && lastRound.answer == null ? (review.questions?.length ? review.questions : lastRound.questions || []) : [];
      const previousRounds = rounds.map(round => `<li>${(round.questions || []).map(question => `<p><strong>Вопрос:</strong> ${esc(question)}</p>`).join('')}${round.answer ? `<p><strong>Ответ:</strong> ${esc(round.answer)}</p>` : ''}</li>`).join('');
      const plan = review.taskPlan;
      const planDecision = plan?.status === 'pending' && plan.action !== 'create_task' ? `<form data-plan-decision data-candidate-id="${esc(c.id)}" data-plan-id="${esc(plan.id)}"><button type="submit" name="accept" value="true">Подтвердить рекомендацию</button><button type="submit" name="accept" value="false" class="secondary">Отклонить</button><span role="status"></span></form>` : '';
      const retryPanel = c.agentStatus === 'failed' ? `<div class="project-analysis-error"><p>${esc(result.job?.error || 'Разбор не завершился. Проверьте настройки агента и повторите попытку.')}</p><button type="button" data-retry-candidate="${esc(c.id)}">Повторить разбор</button><span role="status"></span></div>` : '';
      item.innerHTML = `${previousRounds ? `<details open><summary>Уточнения агента (${rounds.length} раундов)</summary><ol>${previousRounds}</ol></details>` : ''}${questions.length ? `<form data-answers="${esc(c.id)}"><h4>Нужно уточнить</h4><ul>${questions.map(q => `<li>${esc(typeof q === 'string' ? q : q.question || q.text)}</li>`).join('')}</ul><label>Ответьте на вопросы<textarea name="answers" maxlength="10000" required placeholder="Один ответ на все вопросы"></textarea></label><button type="submit">Отправить ответы</button><span role="status"></span></form>` : c.agentStatus === 'queued' || c.agentStatus === 'running' ? '<p>Агент формирует задачу…</p>' : plan ? `<details><summary>План задачи</summary><h4>${esc(plan.title || '')}</h4><p>${esc(plan.description || '')}</p><p><strong>Критерии готовности:</strong> ${esc(plan.acceptanceCriteria || '')}</p></details>${plan.action === 'create_task' && plan.status === 'accepted' ? '<p>Задача сформирована и добавлена в граф.</p>' : planDecision}` : '<p>Агент уточняет задачу перед формированием критериев.</p>'}${retryPanel}`;
    } catch(e) { item.innerHTML = `<p class="project-error">Не удалось загрузить вопросы: ${esc(e.message)}</p>`; }
  }));
}
async function populateLinkTargets() {
  const local = document.querySelector('#task-link-form select[name="taskId"]'), select = document.querySelector('#task-link-form select[name="relatedTaskId"]');
  local.innerHTML = tasks.length ? tasks.map(t => `<option value="${esc(t.id)}">${esc(t.title || t.name || `Задача #${t.id}`)}</option>`).join('') : '<option value="">Сначала создайте задачу</option>';
  local.disabled = !tasks.length;
  try { const r = await fetch('/api/graph'); if (!r.ok) throw new Error(await errorText(r)); const graph = await r.json(); const others = graph.nodes.filter(n => n.kind === 'task' && String(n.sourceProjectId) !== projectQueryId); select.innerHTML = others.length ? others.map(n => `<option value="${esc(n.id)}">${esc(n.title)} — ${esc(n.project || 'Другой проект')}</option>`).join('') : '<option value="">Нет задач из других проектов</option>'; select.disabled = !others.length; document.querySelector('#task-link-form button[type="submit"]').disabled = !others.length || !tasks.length; updateRelatedGraphLink(); } catch(e) { select.innerHTML = `<option value="">Не удалось загрузить задачи: ${esc(e.message)}</option>`; select.disabled = true; }
}
function updateRelatedGraphLink() {
  const select = document.querySelector('#task-link-form select[name="relatedTaskId"]');
  const link = document.querySelector('#related-task-graph-link');
  if (!select || !link || !select.value) { if (link) link.hidden = true; return; }
  link.href = `/graph.html?taskId=${encodeURIComponent(select.value)}`;
  link.hidden = false;
}
document.querySelector('#task-link-form select[name="relatedTaskId"]').addEventListener('change', updateRelatedGraphLink);
document.querySelector('#raw-form').addEventListener('submit', async e => { e.preventDefault(); const form=e.currentTarget, button=form.querySelector('button'), status=form.querySelector('[role=status]'); button.disabled=true; status.textContent='Отправляю…'; status.classList.remove('error'); try { const r=await fetch(`/api/projects/${encodeURIComponent(projectQueryId)}/raw-items`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({text:new FormData(form).get('text')})}); if(!r.ok) throw new Error(await errorText(r)); const result=await r.json().catch(()=>null); form.reset(); status.textContent=`Добавлено в очередь${result?.source ? ' · источник: Пользователь' : ''}.`; await loadWorkspace(); } catch(err) { status.textContent=err.message; status.classList.add('error'); } finally {button.disabled=false;} });
document.addEventListener('submit', async e => { const form=e.target.closest('[data-answers]'); if(form){ e.preventDefault(); const button=form.querySelector('button'), status=form.querySelector('[role=status]'); button.disabled=true; status.textContent='Отправляю…'; try { const r=await fetch(`/api/candidates/${encodeURIComponent(form.dataset.answers)}/answers`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({answers:new FormData(form).get('answers')})}); if(!r.ok) throw new Error(await errorText(r)); status.textContent='Ответы сохранены, следующий раунд поставлен в очередь.'; await loadWorkspace(); } catch(err){status.textContent=err.message;status.classList.add('error');} finally{button.disabled=false;} return; }
  const planForm=e.target.closest('[data-plan-decision]'); if(planForm){ e.preventDefault(); const status=planForm.querySelector('[role=status]'), button=e.submitter; if(!button)return; planForm.querySelectorAll('button').forEach(b=>b.disabled=true); status.textContent='Сохраняю решение…'; try{const r=await fetch(`/api/agent/candidates/${encodeURIComponent(planForm.dataset.candidateId)}/task-plans/${encodeURIComponent(planForm.dataset.planId)}/decision`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({accept:button.value==='true'})});if(!r.ok)throw new Error(await errorText(r));status.textContent='Решение сохранено.';await loadWorkspace();}catch(err){status.textContent=err.message;status.classList.add('error');}finally{planForm.querySelectorAll('button').forEach(b=>b.disabled=false);} return;}
 const link=e.target.closest('#task-link-form'); if(!link)return; e.preventDefault(); const button=link.querySelector('button'), status=link.querySelector('[role=status]'), values=new FormData(link), taskId=values.get('taskId'); if(!taskId){status.textContent='В проекте пока нет задачи для связи.'; return;} button.disabled=true; status.textContent='Сохраняю…'; try{const r=await fetch(`/api/tasks/${encodeURIComponent(taskId)}/links`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({relatedTaskId:Number(values.get('relatedTaskId')),relation:values.get('relation')})});if(!r.ok)throw new Error(await errorText(r));status.textContent='Связь сохранена.';await loadWorkspace();}catch(err){status.textContent=err.message;status.classList.add('error');}finally{button.disabled=false;} });
document.addEventListener('click', async e => {
  const button = e.target.closest('[data-retry-candidate]');
  if (!button) return;
  const panel = button.closest('.project-analysis-error'), status = panel.querySelector('[role="status"]');
  button.disabled = true; status.textContent = 'Ставлю в очередь…';
  try {
    const response = await fetch(`/api/agent/candidates/${encodeURIComponent(button.dataset.retryCandidate)}/analyze`, {method:'POST'});
    if (!response.ok) throw new Error(await errorText(response));
    status.textContent = 'Разбор перезапущен.'; await loadWorkspace();
  } catch (error) { status.textContent = error.message; status.classList.add('error'); }
  finally { button.disabled = false; }
});
document.querySelector('#refresh-project').addEventListener('click',loadWorkspace);
setInterval(() => {
  const active = document.activeElement;
  if (document.hidden || active?.matches('#raw-form textarea, #task-link-form select, [data-answers] textarea, [data-plan-decision] button')) return;
  loadWorkspace();
}, 5000);
loadWorkspace();
