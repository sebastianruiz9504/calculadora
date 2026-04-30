(() => {
  const allWorkouts = (window.planRioWorkouts || []).map(item => ({
    id: item.id,
    date: item.date || '',
    day: item.day || '',
    weekKey: item.weekKey || '',
    weekLabel: item.weekLabel || window.planRioWeekLabel || 'Semana no disponible',
    type: item.workout || '',
    detail: item.detail || '',
    goalMin: Number(item.goalMin || 0),
    phase: item.phase || '',
    discipline: item.discipline || '',
    volumeObjective: item.volumeObjective || '',
    intensityZone: item.intensityZone || '',
    nutrition: item.nutrition || '',
    objective: item.objective || '',
    status: item.status || ''
  }));
  const weeks = (window.planRioWeeks || []).map(item => ({
    key: item.key || '',
    label: item.label || 'Semana no disponible',
    workoutCount: Number(item.workoutCount || 0),
    isSelected: Boolean(item.isSelected)
  }));

  const storageKey = 'plan-rio-week';
  const selectedWeekStorageKey = 'plan-rio-selected-week';
  const state = JSON.parse(localStorage.getItem(storageKey) || '{}');
  const weekGrid = document.getElementById('weekGrid');
  const weekLabel = document.getElementById('weekLabel');
  const selectedWeekTitle = document.getElementById('selectedWeekTitle');
  const weekSelect = document.getElementById('weekSelect');
  const modalEl = document.getElementById('workoutModal');
  const workoutModal = new bootstrap.Modal(modalEl);

  let selectedWeekKey = localStorage.getItem(selectedWeekStorageKey)
    || weeks.find(item => item.isSelected)?.key
    || weeks[0]?.key
    || '';

  if (weeks.length > 0 && !weeks.some(item => item.key === selectedWeekKey)) {
    selectedWeekKey = weeks[0].key;
  }

  const escapeHtml = value => String(value || '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');

  const formatDate = value => {
    if (!value) return '';
    const [year, month, day] = String(value).split('-');
    return year && month && day ? `${day}/${month}/${year}` : value;
  };

  const getSelectedWeek = () => weeks.find(item => item.key === selectedWeekKey);
  const getVisiblePlan = () => selectedWeekKey
    ? allWorkouts.filter(item => item.weekKey === selectedWeekKey)
    : allWorkouts;
  const getEntry = id => state[id] || { done: false, actualMin: 0, effort: 0, notes: '' };

  const save = () => localStorage.setItem(storageKey, JSON.stringify(state));

  const buildDetailRows = item => {
    const rows = [
      ['Detalle', item.detail],
      ['Zona', item.intensityZone],
      ['Volumen', item.volumeObjective],
      ['Nutrición', item.nutrition],
      ['Objetivo', item.objective]
    ].filter(([, value]) => String(value || '').trim().length > 0);

    if (rows.length === 0) {
      return '<div class="plan-rio__detail-empty">Sin detalle en Dataverse</div>';
    }

    return rows.map(([label, value]) => (
      `<div class="plan-rio__detail-row"><strong>${escapeHtml(label)}</strong><span>${escapeHtml(value)}</span></div>`
    )).join('');
  };

  const syncWeekPanel = () => {
    const selectedWeek = getSelectedWeek();
    const visiblePlan = getVisiblePlan();
    const label = selectedWeek?.label || visiblePlan[0]?.weekLabel || window.planRioWeekLabel || 'Semana no disponible';

    weekLabel.textContent = label;
    selectedWeekTitle.textContent = visiblePlan.length > 0
      ? `${label} · ${visiblePlan.length} entrenos`
      : label;

    weekSelect.innerHTML = weeks.map(item => (
      `<option value="${escapeHtml(item.key)}" ${item.key === selectedWeekKey ? 'selected' : ''}>${escapeHtml(item.label)} (${item.workoutCount})</option>`
    )).join('');
    weekSelect.disabled = weeks.length <= 1;
  };

  const render = () => {
    const plan = getVisiblePlan();
    syncWeekPanel();

    if (plan.length === 0) {
      weekGrid.innerHTML = '<article class="plan-rio__day plan-rio__day--empty">No hay entrenos para mostrar con la fuente actual.</article>';
      drawCharts();
      return;
    }

    weekGrid.innerHTML = plan.map(item => {
      const entry = getEntry(item.id);
      const dateLabel = formatDate(item.date);
      const meta = [
        escapeHtml(item.discipline),
        escapeHtml(item.type),
        escapeHtml(item.phase),
        item.goalMin > 0 ? `Meta: ${item.goalMin} min` : 'Meta no definida'
      ]
        .filter(Boolean)
        .join('<br>');
      const status = item.status || (entry.done ? 'Completado' : 'Pendiente');

      return `<article class="plan-rio__day ${entry.active ? 'is-active' : ''}">
          <div class="plan-rio__tags">
            <span>${escapeHtml(item.weekLabel)}</span>
            ${dateLabel ? `<span>${escapeHtml(dateLabel)}</span>` : ''}
          </div>
          <h3>${escapeHtml(item.day || 'Día sin definir')}</h3>
          <div class="plan-rio__meta">${meta}</div>
          <div class="plan-rio__detail">${buildDetailRows(item)}</div>
          <div class="plan-rio__status ${entry.done ? 'done' : 'pending'}">${escapeHtml(status)}</div>
          <div class="d-flex gap-2 flex-wrap">
            <button class="btn btn-sm btn-outline-primary" data-action="active" data-id="${item.id}">Estoy haciéndolo</button>
            <button class="btn btn-sm btn-primary" data-action="register" data-id="${item.id}">Registrar</button>
          </div>
        </article>`;
    }).join('');
    drawCharts();
  };

  const drawLine = (canvasId, values, color, maxValue) => {
    const canvas = document.getElementById(canvasId);
    const ctx = canvas.getContext('2d');
    const w = canvas.width = canvas.offsetWidth;
    const h = canvas.height;
    ctx.clearRect(0, 0, w, h);
    ctx.strokeStyle = '#e9ecef';
    ctx.beginPath();
    ctx.moveTo(30, 10);
    ctx.lineTo(30, h - 25);
    ctx.lineTo(w - 10, h - 25);
    ctx.stroke();

    if (values.length === 0) return;

    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();
    values.forEach((v, i) => {
      const x = 30 + (i * (w - 45) / Math.max(values.length - 1, 1));
      const y = (h - 25) - ((v / Math.max(maxValue, 1)) * (h - 45));
      if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
    });
    ctx.stroke();
  };

  const drawCharts = () => {
    const plan = getVisiblePlan();
    const completion = plan.map(item => getEntry(item.id).done ? 1 : 0);
    const cumulative = completion.map((_, i) => completion.slice(0, i + 1).reduce((a, b) => a + b, 0));
    const realVolume = plan.map(item => getEntry(item.id).actualMin || 0);
    const goalVolume = plan.map(item => item.goalMin);
    drawLine('completionChart', cumulative, '#0d6efd', Math.max(plan.length, 1));
    drawLine('volumeChart', realVolume, '#198754', Math.max(...goalVolume, 1));
  };

  weekSelect.addEventListener('change', ev => {
    selectedWeekKey = ev.target.value;
    localStorage.setItem(selectedWeekStorageKey, selectedWeekKey);
    render();
  });

  weekGrid.addEventListener('click', ev => {
    const btn = ev.target.closest('button[data-action]');
    if (!btn) return;
    const id = Number(btn.dataset.id);
    if (btn.dataset.action === 'active') {
      Object.keys(state).forEach(k => { if (state[k]) state[k].active = false; });
      const entry = getEntry(id);
      state[id] = { ...entry, active: true };
      save();
      render();
      return;
    }

    const entry = getEntry(id);
    document.getElementById('workoutId').value = String(id);
    document.getElementById('actualDuration').value = String(entry.actualMin || '');
    document.getElementById('effort').value = String(entry.effort || '');
    document.getElementById('notes').value = entry.notes || '';
    workoutModal.show();
  });

  document.getElementById('workoutForm').addEventListener('submit', ev => {
    ev.preventDefault();
    const id = Number(document.getElementById('workoutId').value);
    state[id] = {
      ...getEntry(id),
      done: true,
      active: false,
      actualMin: Number(document.getElementById('actualDuration').value),
      effort: Number(document.getElementById('effort').value),
      notes: document.getElementById('notes').value.trim()
    };
    save();
    workoutModal.hide();
    render();
  });

  document.getElementById('resetWeek').addEventListener('click', () => {
    getVisiblePlan().forEach(item => delete state[item.id]);
    save();
    render();
  });

  render();
})();
