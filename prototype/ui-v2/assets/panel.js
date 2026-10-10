/* Paneles internos del prototipo v2 (Sucursal, Logística, Admin): actores, estructura, almacenes y gráficos. */
(function () {
  const BH = window.BH, ROOT = BH.ROOT, store = BH.store;

  /* ---------- iconos extra ---------- */
  Object.assign(BH.P, {
    chart: 'M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 0 1 3 19.875v-6.75ZM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 0 1-1.125-1.125V8.625ZM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 0 1-1.125-1.125V4.125Z',
    users: 'M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z',
    pencil: 'm16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10',
    home: 'm2.25 12 8.954-8.955c.44-.439 1.152-.439 1.591 0L21.75 12M4.5 9.75v10.125c0 .621.504 1.125 1.125 1.125H9.75v-4.875c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125V21h4.125c.621 0 1.125-.504 1.125-1.125V9.75M8.25 21h8.25',
    list: 'M8.25 6.75h12M8.25 12h12m-12 5.25h12M3.75 6.75h.007v.007H3.75V6.75Zm0 5.25h.007v.007H3.75V12Zm0 5.25h.007v.007H3.75v-.007Z',
    cube: 'm21 7.5-9-5.25L3 7.5m18 0-9 5.25m9-5.25v9l-9 5.25M3 7.5l9 5.25M3 7.5v9l9 5.25m0-9v9',
    up: 'M4.5 19.5l15-15m0 0H8.25m11.25 0v11.25',
    down: 'M4.5 4.5l15 15m0 0V8.25m0 11.25H8.25'
  });

  /* ---------- actores y permisos ---------- */
  BH.actores = {
    encargado: { label: 'Encargado de sucursal', user: 'enc.ayacucho', alcance: 'Ayacucho', home: 'sucursal/index.html', area: 'sucursal' },
    logistica: { label: 'Personal de logística', user: 'log.nacional', alcance: 'Red nacional', home: 'logistica/index.html', area: 'logistica' },
    admin: { label: 'Administrador central', user: 'admin.central', alcance: 'Red nacional', home: 'admin/index.html', area: 'admin' }
  };
  const pa = BH.param('actor');
  if (pa && BH.actores[pa]) { store.set('bh2_actor', pa); const u = new URL(location.href); u.searchParams.delete('actor'); history.replaceState(null, '', u); }
  BH.actor = () => store.get('bh2_actor', 'encargado');
  BH.yo = () => BH.actores[BH.actor()];
  BH.MI_SUC = 'aya';                            // sucursal del encargado (claim branch_id del token)

  /* ---------- auditoría compartida (RF20): solo se añaden eventos ---------- */
  BH.audit = {
    list() { return [...store.get('bh2_audit', []), ...BH.auditSemilla]; },
    add(accion, detalle) {
      const d = new Date(), p = (n) => String(n).padStart(2, '0'), y = BH.yo();
      const fecha = `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
      store.set('bh2_audit', [{ fecha, usuario: y.user, rol: y.label, accion, detalle }, ...store.get('bh2_audit', [])]);
    }
  };

  /* ---------- almacenes en el navegador ---------- */
  const mk = (clave, semilla) => ({ get() { return store.get(clave, semilla()); }, set(v) { store.set(clave, v); } });
  BH.tras = mk('bh2_traslados', () => BH.trasladosSemilla.map((x) => ({ ...x })));
  BH.recojos = mk('bh2_recojos', () => BH.recojosSemilla.map((x) => ({ ...x })));
  BH.guias = mk('bh2_guias', () => BH.guiasSemilla.map((x) => ({ ...x })));
  BH.usuarios = mk('bh2_usuarios', () => BH.usuariosSemilla.map((x) => ({ ...x })));
  BH.anulados = mk('bh2_anulados', () => []);

  /* ---------- etiquetas de estado: texto + icono, nunca solo color ---------- */
  const ET = {
    solicitado: ['Solicitado', 'clock', 'text-info'], aprobado: ['Aprobado', 'check', 'text-success'], transito: ['En tránsito', 'truck', 'text-info'],
    recibido: ['Recibido', 'check', 'text-success'], rechazado: ['Rechazado', 'x', 'text-error'],
    confirmado: ['Confirmado', 'clock', 'text-info'], preparacion: ['En preparación', 'cube', 'text-info'], listo: ['Listo para recojo', 'check', 'text-success'], entregado: ['Entregado', 'check', 'text-success'],
    pendiente: ['Pendiente', 'clock', 'text-warning'], generada: ['Guía generada', 'doc', 'text-info'], reparto: ['En reparto', 'truck', 'text-info'],
    respondido: ['Respondido', 'check', 'text-success'], anulado: ['Anulado', 'x', 'text-error'], activo: ['Activo', 'check', 'text-success'], inactivo: ['Inactivo', 'x', 'text-error'],
    vigente: ['Vigente', 'check', 'text-success'], porvencer: ['Por vencer', 'warn', 'text-warning'], vencida: ['Vencida', 'x', 'text-error']
  };
  BH.chipEstado = (e) => { const [t, ic, c] = ET[e] || [e, 'clock', '']; return `<span class="chip">${BH.icon(ic, 'size-3.5 ' + c)}${t}</span>`; };

  /* ---------- reglas de traslados (RF16), usadas por Sucursal, Logística y Admin ---------- */
  BH.trasladoAcciones = (t, actor) => {
    const saliente = t.origen === BH.MI_SUC, entrante = t.destino === BH.MI_SUC, adm = actor === 'admin';
    const a = [];
    if (t.estado === 'solicitado' && (saliente || adm)) a.push(['aprobado', 'Aprobar'], ['rechazado', 'Rechazar']);
    if (t.estado === 'aprobado' && (saliente || adm)) a.push(['transito', 'Despachar']);
    if (t.estado === 'transito' && (entrante || adm)) a.push(['recibido', 'Recibir']);
    return a;
  };
  BH.aplicarTraslado = (t, a) => {
    const o = BH.obra(t.obra), ruta = `${t.id} ${BH.sucursal(t.origen).nombre} → ${BH.sucursal(t.destino).nombre}`;
    if (a === 'transito') {
      if (BH.vendible(o, t.origen) < t.cant) return { ok: false, msg: `El origen (${BH.sucursal(t.origen).nombre}) no tiene stock vendible suficiente: ${BH.vendible(o, t.origen)} de ${t.cant}.` };
      BH.addDelta(t.obra, t.origen, -t.cant);
    }
    if (a === 'recibido') {
      BH.addDelta(t.obra, t.destino, t.cant);
      const r = BH.recojos.get(); r.forEach((x) => { if (x.espera === t.id) delete x.espera; }); BH.recojos.set(r);
    }
    t.estado = a;
    BH.audit.add({ aprobado: 'Aprobación de traslado', rechazado: 'Rechazo de traslado', transito: 'Despacho de traslado', recibido: 'Recepción de traslado' }[a], ruta);
    return { ok: true, msg: `${t.id}: ${ET[a][0].toLowerCase()}. Se notificó a origen y destino por WhatsApp.` };
  };

  /* ---------- licencias digitales ---------- */
  BH.licenciaEstado = (d) => {
    const v = d.licencia.vence; if (!v) return 'vigente';
    if (v < BH.hoy) return 'vencida';
    const dias = (new Date(v) - new Date(BH.hoy)) / 86400000;
    return dias <= 120 ? 'porvencer' : 'vigente';
  };

  /* ---------- estructura de los paneles ---------- */
  const NAV = {
    sucursal: [['index', 'Resumen', 'sucursal/index.html', 'home'], ['inventario', 'Inventario', 'sucursal/inventario.html', 'cube'], ['ajustes', 'Ajustes', 'sucursal/ajustes.html', 'pencil'], ['recojos', 'Pedidos de recojo', 'sucursal/recojos.html', 'store'], ['traslados', 'Traslados', 'sucursal/traslados.html', 'swap']],
    logistica: [['index', 'Traslados', 'logistica/index.html', 'swap'], ['guias', 'Guías de envío', 'logistica/guias.html', 'truck']],
    admin: [['index', 'Indicadores', 'admin/index.html', 'chart'], ['catalogo', 'Catálogo', 'admin/catalogo.html', 'book'], ['sucursales', 'Sucursales', 'admin/sucursales.html', 'store'], ['usuarios', 'Usuarios y roles', 'admin/usuarios.html', 'users'], ['pedidos', 'Pedidos', 'admin/pedidos.html', 'list'], ['reclamos', 'Reclamos', 'admin/reclamos.html', 'doc'], ['auditoria', 'Auditoría', 'admin/auditoria.html', 'shield']]
  };
  const TITULO = { sucursal: 'Panel de sucursal', logistica: 'Logística', admin: 'Administración central' };

  BH.shellPanel = (content, area, page) => {
    const actor = BH.actor(), yo = BH.yo();
    const roles = (document.body.dataset.roles || '').split(',').filter(Boolean);
    const permitido = !roles.length || roles.includes(actor);
    const items = NAV[area].map(([id, label, href, ic]) => `<li><a href="${ROOT}${href}" class="${page === id ? 'menu-active' : ''}" ${page === id ? 'aria-current="page"' : ''}>${BH.icon(ic, 'size-5')}${label}</a></li>`).join('');
    const opciones = Object.entries(BH.actores).map(([k, v]) => `<option value="${k}" ${k === actor ? 'selected' : ''}>${v.label}</option>`).join('');
    const logo = '<span class="grid place-items-center size-9 rounded-md bg-primary text-primary-content" aria-hidden="true"><svg viewBox="0 0 24 24" class="size-6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20V4h16v14H8V8h8v6h-4"/><circle cx="12" cy="14" r="1" fill="currentColor"/></svg></span>';
    document.body.innerHTML = `
    <a href="#main" class="sr-only focus:not-sr-only focus:absolute focus:z-50 btn btn-primary btn-sm m-2">Saltar al contenido</a>
    <div class="drawer lg:drawer-open">
      <input id="nav-drawer" type="checkbox" class="drawer-toggle" aria-label="Abrir menú">
      <div class="drawer-content flex flex-col min-h-screen bg-base-100 text-base-content">
        <header class="bg-base-100 border-b border-[var(--bh-linea)] sticky top-0 z-30 px-3 sm:px-4 py-2 flex items-center gap-2">
          <label for="nav-drawer" class="btn btn-ghost btn-square lg:hidden" aria-label="Abrir menú">${BH.icon('menu')}</label>
          <div class="flex-1 min-w-0"><span class="font-display text-xl truncate block">${TITULO[area]}</span></div>
          <span class="chip hidden sm:inline-flex" title="Alcance del token">${BH.icon('store', 'size-3.5')} ${yo.alcance}</span>
          <label class="flex items-center gap-1.5 text-xs"><span class="hidden md:inline opacity-70">Actor</span><select id="sel-actor" class="select select-sm w-44" aria-label="Cambiar de actor del prototipo">${opciones}</select></label>
          <button class="btn btn-ghost btn-circle" id="btn-theme" aria-label="Cambiar entre tema claro y oscuro"></button>
        </header>
        <main id="main" class="flex-1 p-4 sm:p-6 w-full max-w-7xl"></main>
      </div>
      <div class="drawer-side z-40">
        <label for="nav-drawer" class="drawer-overlay" aria-label="Cerrar menú"></label>
        <aside class="w-64 bg-neutral text-neutral-content min-h-full flex flex-col">
          <a href="${ROOT}index.html" class="flex items-center gap-2 p-4">${logo}<span class="font-display text-xl">BabelHub</span></a>
          <div class="px-4 pb-2 text-xs opacity-70">${yo.label}<br><b class="opacity-100">${yo.user}</b></div>
          <ul class="menu w-full px-2 gap-0.5 flex-1" aria-label="Navegación del panel">${items}</ul>
          <div class="p-4 text-xs border-t border-white/15 space-y-1"><a class="link" href="${ROOT}tienda/index.html">Ir a la tienda</a><div class="opacity-70">Prototipo · datos ficticios</div></div>
        </aside>
      </div>
    </div>`;
    const main = document.getElementById('main');
    if (permitido) main.append(...content.childNodes);
    else {
      BH.audit.add('Acceso denegado', `${yo.user} intentó abrir ${location.pathname.split('/').slice(-2).join('/')}`);
      const quien = Object.entries(BH.actores).filter(([k]) => roles.includes(k));
      main.innerHTML = `<div class="max-w-lg mx-auto text-center py-16 space-y-3">${BH.icon('lock', 'size-12 mx-auto opacity-60')}<h1 class="font-display text-3xl">Sin permiso</h1><p class="opacity-75">Tu rol (<b>${yo.label}</b>) no tiene acceso a esta pantalla. El intento quedó registrado en la auditoría.</p>
        <div class="flex flex-wrap gap-2 justify-center">${quien.map(([k, v]) => `<a class="btn btn-primary btn-sm" href="${location.pathname.split('/').pop()}?actor=${k}">Entrar como ${v.label.toLowerCase()}</a>`).join('')}<a class="btn btn-ghost btn-sm" href="${ROOT}${yo.home}">Ir a mi inicio</a></div></div>`;
      BH.bloqueado = true;
    }
    document.getElementById('sel-actor').addEventListener('change', (e) => { store.set('bh2_actor', e.target.value); location.href = ROOT + BH.actores[e.target.value].home; });
  };

  /* ---------- gráficos (dataviz): barras, leyenda, etiqueta directa, tooltip y vista de tabla ---------- */
  let tip;
  const mostrarTip = (ev, html) => {
    if (!tip) { tip = document.createElement('div'); tip.className = 'fixed z-[80] pointer-events-none px-3 py-2 text-xs rounded border border-[var(--bh-linea)] shadow-lg bg-base-100 text-base-content'; document.body.append(tip); }
    tip.innerHTML = html; tip.hidden = false;
    const r = ev.currentTarget.getBoundingClientRect();
    tip.style.left = Math.min(innerWidth - 220, Math.max(8, r.left + r.width / 2 - 90)) + 'px'; tip.style.top = Math.max(8, r.top - tip.offsetHeight - 6) + 'px';
  };
  const ocultarTip = () => { if (tip) tip.hidden = true; };

  /* filas: [{etiqueta, valores:[v1,v2,...]}], series: [{nombre, color}] */
  BH.barras = (el, { titulo, filas, series, fmt = (n) => BH.money(n), ancho = '8rem' }) => {
    const apilada = series.length > 1;
    const max = Math.max(...filas.map((f) => f.valores.reduce((a, b) => a + b, 0)));
    let tabla = false;
    const leyenda = apilada ? `<ul class="flex flex-wrap gap-4 text-xs mb-3" aria-label="Leyenda">${series.map((s) => `<li class="flex items-center gap-1.5"><span class="inline-block size-3 rounded-sm" style="background:${s.color}"></span>${s.nombre}</li>`).join('')}</ul>` : '';
    const pintar = () => {
      if (tabla) {
        el.innerHTML = `<div class="flex justify-between items-center mb-2"><h3 class="font-semibold">${titulo}</h3><button class="btn btn-xs btn-ghost" data-v>Ver gráfico</button></div>
          <div class="overflow-x-auto"><table class="table table-sm"><thead><tr><th></th>${series.map((s) => `<th class="text-right">${s.nombre}</th>`).join('')}${apilada ? '<th class="text-right">Total</th>' : ''}</tr></thead><tbody>${filas.map((f) => `<tr><td>${f.etiqueta}</td>${f.valores.map((v) => `<td class="text-right tabular-nums">${fmt(v)}</td>`).join('')}${apilada ? `<td class="text-right tabular-nums">${fmt(f.valores.reduce((a, b) => a + b, 0))}</td>` : ''}</tr>`).join('')}</tbody></table></div>`;
      } else {
        el.innerHTML = `<div class="flex justify-between items-center mb-2"><h3 class="font-semibold">${titulo}</h3><button class="btn btn-xs btn-ghost" data-v>Ver tabla</button></div>${leyenda}
          <div class="space-y-1.5" role="list">${filas.map((f, i) => {
            const tot = f.valores.reduce((a, b) => a + b, 0);
            const segs = f.valores.map((v, k) => `<span style="width:${(v / max) * 100}%;background:${series[k].color};${k === f.valores.length - 1 ? 'border-radius:0 4px 4px 0' : ''}" class="h-5 block"></span>`).join('');
            const desc = f.etiqueta + ': ' + series.map((s, k) => `${s.nombre} ${fmt(f.valores[k])}`).join(', ');
            return `<div role="listitem" tabindex="0" data-i="${i}" aria-label="${desc}" style="grid-template-columns:min(${ancho},38%) 1fr" class="grid items-center gap-2 text-sm py-0.5 rounded hover:bg-base-200 focus:bg-base-200">
              <span class="truncate">${f.etiqueta}</span><span class="flex items-center gap-2 min-w-0"><span class="flex gap-[2px] border-l border-[var(--bh-linea)]" style="width:calc(${(tot / max) * 100}% * .82)">${segs}</span><span class="tabular-nums text-xs shrink-0">${fmt(tot)}</span></span></div>`; }).join('')}</div>`;
        el.querySelectorAll('[data-i]').forEach((r) => {
          const f = filas[r.dataset.i], html = `<b>${f.etiqueta}</b>` + series.map((s, k) => `<div class="flex items-center gap-1.5"><span class="inline-block size-2.5 rounded-sm" style="background:${s.color}"></span>${s.nombre}: <b class="tabular-nums">${fmt(f.valores[k])}</b></div>`).join('');
          r.addEventListener('mouseenter', (ev) => mostrarTip(ev, html)); r.addEventListener('focus', (ev) => mostrarTip(ev, html));
          r.addEventListener('mouseleave', ocultarTip); r.addEventListener('blur', ocultarTip);
        });
      }
      el.querySelector('[data-v]').onclick = () => { tabla = !tabla; ocultarTip(); pintar(); };
    };
    pintar();
  };

  /* Tarjeta de cifra: etiqueta, valor, variación frente a un periodo con nombre (texto + icono) */
  BH.kpi = (label, valor, delta, bueno = true, periodo = 'periodo anterior') => {
    const sube = delta >= 0, ok = sube === bueno;
    return `<div class="border border-[var(--bh-linea)] bg-base-100 p-4"><p class="text-sm opacity-75">${label}</p><p class="text-3xl font-semibold mt-1" style="font-family:Inter">${valor}</p>
      ${delta == null ? '' : `<p class="text-xs mt-1 flex items-center gap-1">${BH.icon(sube ? 'up' : 'down', 'size-3.5 ' + (ok ? 'text-success' : 'text-error'))}<span>${sube ? '+' : '−'}${Math.abs(delta).toLocaleString('es-PE')} % vs. ${periodo}</span></p>`}</div>`;
  };
})();
