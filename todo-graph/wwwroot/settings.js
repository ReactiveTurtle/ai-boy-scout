const form = document.querySelector('#settings-form');
const status = document.querySelector('#settings-status');

function splitTools(value) {
  return value.split(',').map(tool => tool.trim()).filter(Boolean);
}

function setServer(prefix, server) {
  form.elements[`${prefix}Enabled`].checked = server.enabled;
  form.elements[`${prefix}Executable`].value = server.executable || '';
  form.elements[`${prefix}Arguments`].value = JSON.stringify(server.arguments || [], null, 2);
  form.elements[`${prefix}TokenEnvironment`].value = server.tokenEnvironmentVariable || 'GITLAB_TOKEN';
  form.elements[`${prefix}Tools`].value = (server.tools || []).join(', ');
  form.elements[`${prefix}Token`].value = '';
  document.querySelector(`#${prefix}-secret-state`).textContent = server.hasToken ? 'Токен сохранён в зашифрованном виде.' : 'Токен не задан.';
}

async function loadSettings() {
  try {
    const response = await fetch('/api/settings/code-host-mcp');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const settings = await response.json();
    form.elements.displayName.value = settings.displayName || '';
    setServer('read', settings.read);
    setServer('write', settings.write);
    setServer('github', settings.githubRead);
  } catch (error) {
    status.textContent = `Не удалось загрузить настройки: ${error.message}`;
  }
}

function serverInput(prefix) {
  let argumentsValue;
  try { argumentsValue = JSON.parse(form.elements[`${prefix}Arguments`].value || '[]'); }
  catch { throw new Error(`Аргументы ${prefix} MCP должны быть корректным JSON-массивом.`); }
  if (!Array.isArray(argumentsValue) || argumentsValue.some(value => typeof value !== 'string'))
    throw new Error(`Аргументы ${prefix} MCP должны быть массивом строк.`);
  return {
    enabled: form.elements[`${prefix}Enabled`].checked,
    executable: form.elements[`${prefix}Executable`].value.trim(),
    arguments: argumentsValue,
    tokenEnvironmentVariable: form.elements[`${prefix}TokenEnvironment`].value.trim(),
    token: form.elements[`${prefix}Token`].value,
    clearToken: form.elements[`${prefix}ClearToken`].checked,
    tools: splitTools(form.elements[`${prefix}Tools`].value),
  };
}

form.addEventListener('submit', async event => {
  event.preventDefault();
  status.textContent = 'Сохраняю…';
  try {
    const response = await fetch('/api/settings/code-host-mcp', {
      method: 'PUT',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({
        displayName: form.elements.displayName.value,
        read: serverInput('read'),
        write: serverInput('write'),
        githubRead: serverInput('github'),
      }),
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.error || `HTTP ${response.status}`);
    setServer('read', result.read);
    setServer('write', result.write);
    setServer('github', result.githubRead);
    status.textContent = 'Настройки сохранены. Новые MCP-конфиги применяются к следующим запускам агента.';
  } catch (error) {
    status.textContent = error.message;
  }
});

loadSettings();
