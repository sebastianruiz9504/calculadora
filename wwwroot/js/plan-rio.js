(() => {
  const numberFormatter = new Intl.NumberFormat('es-CO', {
    maximumFractionDigits: 2
  });

  const toNumber = value => {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  };

  const toNullableNumber = value => {
    if (value === null || value === undefined || value === '') return null;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  };

  const allWorkouts = (window.planRioWorkouts || []).map(item => ({
    id: item.id,
    recordId: item.recordId || '',
    date: item.date || '',
    day: item.day || '',
    weekKey: item.weekKey || '',
    weekLabel: item.weekLabel || window.planRioWeekLabel || 'Semana no disponible',
    type: item.workout || '',
    detail: item.detail || '',
    goalMin: toNumber(item.goalMin),
    phase: item.phase || '',
    discipline: item.discipline || '',
    volumeObjective: item.volumeObjective || '',
    intensityZone: item.intensityZone || '',
    nutrition: item.nutrition || '',
    objective: item.objective || '',
    status: item.status || '',
    actualMin: toNumber(item.actualMin),
    actualDistance: toNullableNumber(item.actualDistance),
    averageHeartRate: toNumber(item.averageHeartRate),
    averagePower: toNullableNumber(item.averagePower),
    notes: item.notes || ''
  }));

  const weeks = (window.planRioWeeks || []).map(item => ({
    key: item.key || '',
    label: item.label || 'Semana no disponible',
    workoutCount: toNumber(item.workoutCount),
    isSelected: Boolean(item.isSelected)
  }));

  const storageKey = 'plan-rio-week';
  const selectedWeekStorageKey = 'plan-rio-selected-week';
  const weekGrid = document.getElementById('weekGrid');
  const weekLabel = document.getElementById('weekLabel');
  const selectedWeekTitle = document.getElementById('selectedWeekTitle');
  const weekSelect = document.getElementById('weekSelect');
  const modalEl = document.getElementById('workoutModal');
  const workoutModal = new bootstrap.Modal(modalEl);
  const workoutForm = document.getElementById('workoutForm');
  const workoutSubmit = document.getElementById('workoutSubmit');
  const workoutFormStatus = document.getElementById('workoutFormStatus');
  const workoutKeyInput = document.getElementById('workoutKey');
  const actualDurationInput = document.getElementById('actualDuration');
  const actualDistanceInput = document.getElementById('actualDistance');
  const averageHeartRateInput = document.getElementById('averageHeartRate');
  const averagePowerInput = document.getElementById('averagePower');
  const notesInput = document.getElementById('notes');
  const registerUrl = window.planRioRegisterUrl || '/PlanRio/Register';

  const readStoredState = () => {
    try {
      return JSON.parse(localStorage.getItem(storageKey) || '{}') || {};
    } catch {
      return {};
    }
  };

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

  const formatDecimal = value => {
    const parsed = toNullableNumber(value);
    return parsed === null ? '' : numberFormatter.format(parsed);
  };

  const getStateKey = item => item.recordId || String(item.id);
  const hasServerEntry = item => (
    item.actualMin > 0
    || (item.actualDistance ?? 0) > 0
    || item.averageHeartRate > 0
    || (item.averagePower ?? 0) > 0
    || item.notes.trim().length > 0
  );

  const buildInitialState = () => {
    const storedState = readStoredState();
    return allWorkouts.reduce((acc, item) => {
      const key = getStateKey(item);
      const stored = storedState[key] || storedState[String(item.id)] || {};
      const savedInDataverse = hasServerEntry(item);
      const averagePower = item.averagePower ?? toNullableNumber(stored.averagePower);

      acc[key] = {
        done: savedInDataverse || Boolean(stored.done),
        active: savedInDataverse ? false : Boolean(stored.active),
        actualMin: item.actualMin || toNumber(stored.actualMin),
        distance: item.actualDistance ?? toNullableNumber(stored.distance) ?? 0,
        averageHeartRate: item.averageHeartRate || toNumber(stored.averageHeartRate),
        averagePower,
        notes: item.notes || stored.notes || ''
      };

      return acc;
    }, {});
  };

  const state = buildInitialState();

  let selectedWeekKey = localStorage.getItem(selectedWeekStorageKey)
    || weeks.find(item => item.isSelected)?.key
    || weeks[0]?.key
    || '';

  if (weeks.length > 0 && !weeks.some(item => item.key === selectedWeekKey)) {
    selectedWeekKey = weeks[0].key;
  }

  const save = () => localStorage.setItem(storageKey, JSON.stringify(state));
  const getSelectedWeek = () => weeks.find(item => item.key === selectedWeekKey);
  const getVisiblePlan = () => selectedWeekKey
    ? allWorkouts.filter(item => item.weekKey === selectedWeekKey)
    : allWorkouts;
  const getEntry = item => state[getStateKey(item)] || {
    done: false,
    active: false,
    actualMin: 0,
    distance: 0,
    averageHeartRate: 0,
    averagePower: null,
    notes: ''
  };
  const findWorkoutByKey = key => allWorkouts.find(item =>
    getStateKey(item) === key || String(item.id) === key);

  async function fetchJson(url, options = {}) {
    const headers = {
      Accept: 'application/json',
      ...(options.headers || {})
    };

    if (options.body && !headers['Content-Type']) {
      headers['Content-Type'] = 'application/json';
    }

    const response = await fetch(url, {
      method: options.method || 'GET',
      headers,
      body: options.body
    });
    const contentType = response.headers.get('content-type') || '';

    if (!response.ok) {
      const rawBody = await response.text();
      let message = rawBody;

      if (contentType.includes('application/json')) {
        try {
          const payload = rawBody ? JSON.parse(rawBody) : null;
          message = typeof payload === 'string'
            ? payload
            : payload?.message || payload?.detail || payload?.title || rawBody;
        } catch {
          message = rawBody;
        }
      }

      throw new Error(message || 'No fue posible completar la solicitud.');
    }

    if (!contentType.includes('application/json')) {
      return null;
    }

    return response.json();
  }

  const setFormStatus = (type, message) => {
    workoutFormStatus.className = message
      ? `plan-rio__form-status is-visible is-${type}`
      : 'plan-rio__form-status';
    workoutFormStatus.textContent = message || '';
  };

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

  const buildRegisteredRows = entry => {
    const rows = [
      ['Duración', entry.actualMin > 0 ? `${entry.actualMin} min` : ''],
      ['Distancia', entry.distance > 0 ? formatDecimal(entry.distance) : ''],
      ['FC prom.', entry.averageHeartRate > 0 ? `${entry.averageHeartRate} ppm` : ''],
      ['Potencia prom.', entry.averagePower !== null && entry.averagePower !== undefined ? `${entry.averagePower} W` : ''],
      ['Notas', entry.notes]
    ].filter(([, value]) => String(value || '').trim().length > 0);

    if (rows.length === 0) return '';

    return `<div class="plan-rio__registered">
      ${rows.map(([label, value]) => (
        `<div><strong>${escapeHtml(label)}</strong><span>${escapeHtml(value)}</span></div>`
      )).join('')}
    </div>`;
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
      const entry = getEntry(item);
      const key = getStateKey(item);
      const dateLabel = formatDate(item.date);
      const tagsHtml = dateLabel
        ? `<div class="plan-rio__tags"><span>${escapeHtml(dateLabel)}</span></div>`
        : '';
      const meta = [
        escapeHtml(item.discipline),
        escapeHtml(item.type),
        escapeHtml(item.phase),
        item.goalMin > 0 ? `Meta: ${item.goalMin} min` : 'Meta no definida'
      ]
        .filter(Boolean)
        .join('<br>');
      const status = entry.done ? 'Completado' : (item.status || 'Pendiente');

      return `<article class="plan-rio__day ${entry.active ? 'is-active' : ''}">
          ${tagsHtml}
          <h3>${escapeHtml(item.day || 'Día sin definir')}</h3>
          <div class="plan-rio__meta">${meta}</div>
          <div class="plan-rio__detail">${buildDetailRows(item)}</div>
          <div class="plan-rio__status ${entry.done ? 'done' : 'pending'}">${escapeHtml(status)}</div>
          ${buildRegisteredRows(entry)}
          <div class="d-flex gap-2 flex-wrap">
            <button class="btn btn-sm btn-outline-primary" data-action="active" data-key="${escapeHtml(key)}">Estoy haciéndolo</button>
            <button class="btn btn-sm btn-primary" data-action="register" data-key="${escapeHtml(key)}">Registrar</button>
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
    const completion = plan.map(item => getEntry(item).done ? 1 : 0);
    const cumulative = completion.map((_, i) => completion.slice(0, i + 1).reduce((a, b) => a + b, 0));
    const realVolume = plan.map(item => getEntry(item).actualMin || 0);
    const goalVolume = plan.map(item => item.goalMin);
    drawLine('completionChart', cumulative, '#0d6efd', Math.max(plan.length, 1));
    drawLine('volumeChart', realVolume, '#198754', Math.max(...goalVolume, 1));
  };

  const updateWorkoutFromRecord = (item, record, payload) => {
    item.actualMin = toNumber(record?.actualMin ?? payload.durationMinutes);
    item.actualDistance = toNullableNumber(record?.actualDistance ?? payload.distance);
    item.averageHeartRate = toNumber(record?.averageHeartRate ?? payload.averageHeartRate);
    item.averagePower = toNullableNumber(record?.averagePower ?? payload.averagePower);
    item.notes = record?.notes ?? payload.notes;
    item.status = record?.status || item.status;
  };

  weekSelect.addEventListener('change', ev => {
    selectedWeekKey = ev.target.value;
    localStorage.setItem(selectedWeekStorageKey, selectedWeekKey);
    render();
  });

  weekGrid.addEventListener('click', ev => {
    const btn = ev.target.closest('button[data-action]');
    if (!btn) return;

    const key = btn.dataset.key || '';
    const item = findWorkoutByKey(key);
    if (!item) return;

    if (btn.dataset.action === 'active') {
      Object.keys(state).forEach(stateKey => { if (state[stateKey]) state[stateKey].active = false; });
      state[key] = { ...getEntry(item), active: true };
      save();
      render();
      return;
    }

    const entry = getEntry(item);
    workoutKeyInput.value = key;
    actualDurationInput.value = String(entry.actualMin || '');
    actualDistanceInput.value = entry.distance > 0 ? String(entry.distance) : '';
    averageHeartRateInput.value = String(entry.averageHeartRate || '');
    averagePowerInput.value = entry.averagePower !== null && entry.averagePower !== undefined ? String(entry.averagePower) : '';
    notesInput.value = entry.notes || '';
    setFormStatus('', '');
    workoutSubmit.disabled = false;
    workoutModal.show();
  });

  workoutForm.addEventListener('submit', async ev => {
    ev.preventDefault();
    if (!workoutForm.reportValidity()) return;

    const key = workoutKeyInput.value;
    const item = findWorkoutByKey(key);
    if (!item?.recordId) {
      setFormStatus('error', 'No se encontro el registro de Dataverse para actualizar.');
      return;
    }

    const averagePowerRaw = averagePowerInput.value.trim();
    const payload = {
      recordId: item.recordId,
      durationMinutes: toNumber(actualDurationInput.value),
      distance: toNumber(actualDistanceInput.value),
      averageHeartRate: toNumber(averageHeartRateInput.value),
      averagePower: averagePowerRaw ? toNumber(averagePowerRaw) : null,
      notes: notesInput.value.trim()
    };

    try {
      workoutSubmit.disabled = true;
      setFormStatus('saving', 'Guardando...');
      const result = await fetchJson(registerUrl, {
        method: 'POST',
        body: JSON.stringify(payload)
      });
      updateWorkoutFromRecord(item, result?.record, payload);
      state[key] = {
        ...getEntry(item),
        done: true,
        active: false,
        actualMin: payload.durationMinutes,
        distance: payload.distance,
        averageHeartRate: payload.averageHeartRate,
        averagePower: payload.averagePower,
        notes: payload.notes
      };
      save();
      workoutModal.hide();
      render();
    } catch (error) {
      setFormStatus('error', error?.message || 'No fue posible registrar el entreno.');
    } finally {
      workoutSubmit.disabled = false;
    }
  });

  document.getElementById('resetWeek').addEventListener('click', () => {
    getVisiblePlan().forEach(item => {
      const key = getStateKey(item);
      state[key] = { ...getEntry(item), active: false };
    });
    save();
    render();
  });

  render();
})();
