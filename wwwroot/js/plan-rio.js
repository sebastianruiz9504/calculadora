(() => {
  const plan = [
    { id: 1, day: 'Lunes', type: 'Natación técnica', goalMin: 60 },
    { id: 2, day: 'Martes', type: 'Ciclismo intervalos', goalMin: 90 },
    { id: 3, day: 'Miércoles', type: 'Trote Z2', goalMin: 50 },
    { id: 4, day: 'Jueves', type: 'Natación fondo', goalMin: 70 },
    { id: 5, day: 'Viernes', type: 'Fuerza y movilidad', goalMin: 45 },
    { id: 6, day: 'Sábado', type: 'Brick (bici+trote)', goalMin: 120 },
    { id: 7, day: 'Domingo', type: 'Rodaje largo', goalMin: 80 }
  ];

  const storageKey = 'plan-rio-week';
  const state = JSON.parse(localStorage.getItem(storageKey) || '{}');
  const weekGrid = document.getElementById('weekGrid');
  const modalEl = document.getElementById('workoutModal');
  const workoutModal = new bootstrap.Modal(modalEl);

  const getEntry = id => state[id] || { done: false, actualMin: 0, effort: 0, notes: '' };

  const save = () => localStorage.setItem(storageKey, JSON.stringify(state));

  const render = () => {
    weekGrid.innerHTML = plan.map(item => {
      const entry = getEntry(item.id);
      return `<article class="plan-rio__day ${entry.active ? 'is-active' : ''}">
          <h3>${item.day}</h3>
          <div class="plan-rio__meta">${item.type}<br>Meta: ${item.goalMin} min</div>
          <div class="plan-rio__status ${entry.done ? 'done' : 'pending'}">${entry.done ? 'Completado' : 'Pendiente'}</div>
          <div class="d-flex gap-2">
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
    ctx.beginPath(); ctx.moveTo(30, 10); ctx.lineTo(30, h - 25); ctx.lineTo(w - 10, h - 25); ctx.stroke();
    ctx.strokeStyle = color; ctx.lineWidth = 2;
    values.forEach((v, i) => {
      const x = 30 + (i * (w - 45) / Math.max(values.length - 1, 1));
      const y = (h - 25) - ((v / Math.max(maxValue, 1)) * (h - 45));
      if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
    });
    ctx.stroke();
  };

  const drawCharts = () => {
    const completion = plan.map(item => getEntry(item.id).done ? 1 : 0);
    const cumulative = completion.map((_, i) => completion.slice(0, i + 1).reduce((a, b) => a + b, 0));
    const realVolume = plan.map(item => getEntry(item.id).actualMin || 0);
    const goalVolume = plan.map(item => item.goalMin);
    drawLine('completionChart', cumulative, '#0d6efd', 7);
    drawLine('volumeChart', realVolume, '#198754', Math.max(...goalVolume));
  };

  weekGrid.addEventListener('click', ev => {
    const btn = ev.target.closest('button[data-action]');
    if (!btn) return;
    const id = Number(btn.dataset.id);
    if (btn.dataset.action === 'active') {
      Object.keys(state).forEach(k => { if (state[k]) state[k].active = false; });
      const entry = getEntry(id);
      state[id] = { ...entry, active: true };
      save(); render();
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
    localStorage.removeItem(storageKey);
    Object.keys(state).forEach(k => delete state[k]);
    render();
  });

  render();
})();
