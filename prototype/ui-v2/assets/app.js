/* Núcleo compartido del prototipo v2: utilidades, portadas, carrito y estructura (shell). */
(function () {
  const BH = (window.BH = window.BH || {});
  const ROOT = document.currentScript.src.replace(/assets\/app\.js.*$/, '');
  BH.ROOT = ROOT;

  /* ---------- almacenamiento (puede fallar: modo privado) ---------- */
  const store = {
    get(k, d) { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : d; } catch (e) { return d; } },
    set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* sin persistencia */ } }
  };
  BH.store = store;

  /* ---------- tema: respeta la preferencia del sistema si el usuario no eligió ---------- */
  const preferido = () => (window.matchMedia && matchMedia('(prefers-color-scheme: dark)').matches ? 'babelhub-dark' : 'babelhub');
  const setTheme = (t, guardar = true) => { document.documentElement.setAttribute('data-theme', t); if (guardar) store.set('bh2_theme', t); };
  setTheme(store.get('bh2_theme', null) || preferido(), false);

  /* ---------- iconos (trazo fino) ---------- */
  const P = {
    search: 'm21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z',
    cart: 'M2.25 3h1.386c.51 0 .955.343 1.087.835l.383 1.437M7.5 14.25a3 3 0 0 0-3 3h15.75m-12.75-3h11.218c1.121-2.3 2.1-4.684 2.924-7.138a60.114 60.114 0 0 0-16.536-1.84M7.5 14.25 5.106 5.272M6 20.25a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Zm12.75 0a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Z',
    sun: 'M12 3v2.25m6.364.386-1.591 1.591M21 12h-2.25m-.386 6.364-1.591-1.591M12 18.75V21m-4.773-4.227-1.591 1.591M5.25 12H3m4.227-4.773L5.636 5.636M15.75 12a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0Z',
    moon: 'M21.752 15.002A9.72 9.72 0 0 1 18 15.75c-5.385 0-9.75-4.365-9.75-9.75 0-1.33.266-2.597.748-3.752A9.753 9.753 0 0 0 3 11.25C3 16.635 7.365 21 12.75 21a9.753 9.753 0 0 0 9.002-5.998Z',
    menu: 'M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5',
    check: 'm4.5 12.75 6 6 9-13.5',
    x: 'M6 18 18 6M6 6l12 12',
    plus: 'M12 4.5v15m7.5-7.5h-15',
    truck: 'M8.25 18.75a1.5 1.5 0 0 1-3 0m3 0a1.5 1.5 0 0 0-3 0m3 0h6m-9 0H3.375a1.125 1.125 0 0 1-1.125-1.125V14.25m17.25 4.5a1.5 1.5 0 0 1-3 0m3 0a1.5 1.5 0 0 0-3 0m3 0h1.125c.621 0 1.129-.504 1.09-1.124a17.902 17.902 0 0 0-3.213-9.193 2.056 2.056 0 0 0-1.58-.86H14.25M16.5 18.75h-2.25m0-11.177v-.958c0-.568-.422-1.048-.987-1.106a48.554 48.554 0 0 0-10.026 0 1.106 1.106 0 0 0-.987 1.106v7.635m12-6.677v6.677m0 4.5v-4.5m0 0h-12',
    store: 'M13.5 21v-7.5a.75.75 0 0 1 .75-.75h3a.75.75 0 0 1 .75.75V21m-4.5 0H2.36m11.14 0H18m0 0h3.64m-1.39 0V9.349M3.75 21V9.349m0 0a3.001 3.001 0 0 0 3.75-.615A2.993 2.993 0 0 0 9.75 9.75c.896 0 1.7-.393 2.25-1.016a2.993 2.993 0 0 0 2.25 1.016c.896 0 1.7-.393 2.25-1.015a3.001 3.001 0 0 0 3.75.614m-16.5 0a3.004 3.004 0 0 1-.621-4.72l1.189-1.19A1.5 1.5 0 0 1 5.378 3h13.243a1.5 1.5 0 0 1 1.06.44l1.19 1.189a3 3 0 0 1-.621 4.72M6.75 18h3.75a.75.75 0 0 0 .75-.75V13.5a.75.75 0 0 0-.75-.75H6.75a.75.75 0 0 0-.75.75v3.75c0 .414.336.75.75.75Z',
    chat: 'M8.625 12a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0H8.25m4.125 0a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0H12m4.125 0a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0h-.375M21 12c0 4.556-4.03 8.25-9 8.25a9.764 9.764 0 0 1-2.555-.337A5.972 5.972 0 0 1 5.41 20.97a5.969 5.969 0 0 1-.474-.065 4.48 4.48 0 0 0 .978-2.025c.09-.457-.133-.901-.467-1.226C3.93 16.178 3 14.189 3 12c0-4.556 4.03-8.25 9-8.25s9 3.694 9 8.25Z',
    warn: 'M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z',
    swap: 'M7.5 21 3 16.5m0 0L7.5 12M3 16.5h13.5m0-13.5L21 7.5m0 0L16.5 12M21 7.5H7.5',
    book: 'M12 6.042A8.967 8.967 0 0 0 6 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 0 1 6 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 0 1 6-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0 0 18 18a8.967 8.967 0 0 0-6 2.292m0-14.25v14.25',
    clock: 'M12 6v6h4.5m4.5 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
    user: 'M15.75 6a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0ZM4.501 20.118a7.5 7.5 0 0 1 14.998 0A17.933 17.933 0 0 1 12 21.75c-2.676 0-5.216-.584-7.499-1.632Z',
    shield: 'M9 12.75 11.25 15 15 9.75m-3-7.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285Z',
    trash: 'm14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0',
    download: 'M3 16.5v2.25A2.25 2.25 0 0 0 5.25 21h13.5A2.25 2.25 0 0 0 21 18.75V16.5M16.5 12 12 16.5m0 0L7.5 12m4.5 4.5V3',
    heart: 'M21 8.25c0-2.485-2.099-4.5-4.688-4.5-1.935 0-3.597 1.126-4.312 2.733-.715-1.607-2.377-2.733-4.313-2.733C5.1 3.75 3 5.765 3 8.25c0 7.22 9 12 9 12s9-4.78 9-12Z',
    lock: 'M16.5 10.5V6.75a4.5 4.5 0 1 0-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 0 0 2.25-2.25v-6.75a2.25 2.25 0 0 0-2.25-2.25H6.75a2.25 2.25 0 0 0-2.25 2.25v6.75a2.25 2.25 0 0 0 2.25 2.25Z',
    bolt: 'm3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z',
    arrow: 'M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3',
    printer: 'M6.72 13.829c-.24.03-.48.062-.72.096m.72-.096a42.415 42.415 0 0 1 10.56 0m-10.56 0L6.34 18m10.94-4.171c.24.03.48.062.72.096m-.72-.096L17.66 18m0 0 .229 2.523a1.125 1.125 0 0 1-1.12 1.227H7.231c-.662 0-1.18-.568-1.12-1.227L6.34 18m11.318 0h1.091A2.25 2.25 0 0 0 21 15.75V9.456c0-1.081-.768-2.015-1.837-2.175a48.055 48.055 0 0 0-1.913-.247M6.34 18H5.25A2.25 2.25 0 0 1 3 15.75V9.456c0-1.081.768-2.015 1.837-2.175a48.041 48.041 0 0 1 1.913-.247m10.5 0a48.536 48.536 0 0 0-10.5 0m10.5 0V3.375c0-.621-.504-1.125-1.125-1.125h-8.25c-.621 0-1.125.504-1.125 1.125v3.659M18 10.5h.008v.008H18V10.5Zm-3 0h.008v.008H15V10.5Z',
    doc: 'M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z',
    tablet: 'M10.5 19.5h3m-6.75 2.25h10.5a2.25 2.25 0 0 0 2.25-2.25v-15a2.25 2.25 0 0 0-2.25-2.25H6.75A2.25 2.25 0 0 0 4.5 4.5v15a2.25 2.25 0 0 0 2.25 2.25Z',
    dice: 'M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0 3.181 3.183a8.25 8.25 0 0 0 13.803-3.7M4.031 9.865a8.25 8.25 0 0 1 13.803-3.7l3.181 3.182m0-4.991v4.99'
  };
  BH.icon = (n, cls = 'size-5') =>
    `<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.6" stroke="currentColor" class="${cls}" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" d="${P[n] || ''}"/></svg>`;

  BH.P = P;                      // los paneles añaden sus propios iconos

  /* ---------- formato y acceso a datos ---------- */
  BH.money = (n) => 'S/ ' + Number(n).toLocaleString('es-PE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  BH.obra = (id) => BH.obras.find((o) => o.id === Number(id));
  BH.sucursal = (id) => BH.sucursales.find((s) => s.id === id);
  BH.param = (k) => new URLSearchParams(location.search).get(k);
  BH.estado = BH.param('estado') || 'normal';
  BH.miSucursal = () => store.get('bh2_sucursal', 'aya');
  BH.fecha = (iso) => new Date(iso + 'T12:00:00').toLocaleDateString('es-PE', { day: 'numeric', month: 'long', year: 'numeric' });

  BH.vendible = (o, sid) => { const s = o.impreso && o.impreso.stock[sid]; return s ? s[0] - s[1] : 0; };
  BH.totalVendible = (o) => (o.impreso ? BH.sucursales.reduce((a, s) => a + BH.vendible(o, s.id), 0) : 0);
  BH.conStock = (o) => BH.sucursales.filter((s) => BH.vendible(o, s.id) > 0);

  /* Sucursales nuevas creadas en el prototipo: son datos, no código (RNF04) */
  store.get('bh2_sucursales_extra', []).forEach((s) => { if (!BH.sucursal(s.id)) BH.sucursales.push(s); });

  /* Ajustes de stock físico persistidos (ajustes, traslados): se reaplican al cargar cada página */
  const aplicarDelta = (obra, suc, d) => {
    const o = BH.obra(obra); if (!o || !o.impreso) return;
    const s = o.impreso.stock[suc] || (o.impreso.stock[suc] = [0, 0]); s[0] = Math.max(0, s[0] + d);
  };
  store.get('bh2_deltas', []).forEach((x) => aplicarDelta(x.obra, x.suc, x.d));
  BH.addDelta = (obra, suc, d) => { const l = store.get('bh2_deltas', []); l.push({ obra, suc, d }); store.set('bh2_deltas', l); aplicarDelta(obra, suc, d); };

  /* Cambios de catálogo hechos desde Administración (obras nuevas, precios, bajas y retiros digitales) */
  store.get('bh2_obras_extra', []).forEach((o) => { if (!BH.obra(o.id)) BH.obras.push(o); });
  const aplicarCambio = (id, c) => {
    const o = BH.obra(id); if (!o) return;
    if (c.pi != null && o.impreso) o.impreso.precio = c.pi;
    if (c.pd != null && o.digital) o.digital.precio = c.pd;
    if (c.sinDigital) o.digital = null;
    if (c.baja) BH.obras.splice(BH.obras.indexOf(o), 1);
  };
  Object.entries(store.get('bh2_obras_cambios', {})).forEach(([id, c]) => aplicarCambio(id, c));
  BH.setCambio = (id, patch) => { const m = store.get('bh2_obras_cambios', {}); m[id] = { ...(m[id] || {}), ...patch }; store.set('bh2_obras_cambios', m); aplicarCambio(id, patch); };
  BH.addObra = (o) => { store.set('bh2_obras_extra', [...store.get('bh2_obras_extra', []), o]); BH.obras.push(o); };

  /* Reclamos: los crea la Tienda y los atiende Administración */
  BH.reclamos = {
    get() { return store.get('bh2_reclamos', BH.reclamosSemilla || []); },
    set(v) { store.set('bh2_reclamos', v); },
    add(r) { this.set([r, ...this.get()]); }
  };

  /* Disponibilidad digital: licencia vigente (RF24). El territorio se asume Perú. */
  BH.digitalEstado = (o) => {
    if (!o.digital) return { ok: false, motivo: 'Esta obra no tiene edición digital.' };
    const l = o.digital.licencia;
    if (l.vence && l.vence < BH.hoy) return { ok: false, motivo: 'La licencia de esta edición digital venció y el titular aún no la renueva.' };
    return { ok: true, motivo: '' };
  };

  /* Chip de disponibilidad: texto + icono; nunca solo color */
  BH.chipImpreso = (o, sid) => {
    if (!o.impreso) return '';
    const t = BH.totalVendible(o);
    if (t === 0) return `<span class="chip">${BH.icon('x', 'size-3.5 text-error')}Impreso agotado</span>`;
    if (BH.vendible(o, sid) > 0) return t <= 3
      ? `<span class="chip">${BH.icon('warn', 'size-3.5 text-warning')}Últimas unidades</span>`
      : `<span class="chip">${BH.icon('check', 'size-3.5 text-success')}En tu sucursal</span>`;
    const n = BH.conStock(o).length;
    return `<span class="chip">${BH.icon('truck', 'size-3.5 text-info')}En ${n} ${n === 1 ? 'sucursal más' : 'sucursales más'}</span>`;
  };
  BH.chipDigital = (o) => {
    if (!o.digital) return '';
    return BH.digitalEstado(o).ok
      ? `<span class="chip">${BH.icon('bolt', 'size-3.5 text-success')}Digital al instante</span>`
      : `<span class="chip">${BH.icon('lock', 'size-3.5 text-error')}Digital no disponible</span>`;
  };
  BH.desde = (o) => {
    const p = [o.impreso && o.impreso.precio, o.digital && BH.digitalEstado(o).ok && o.digital.precio].filter(Boolean);
    return p.length ? Math.min(...p) : (o.impreso ? o.impreso.precio : 0);
  };

  /* ---------- portadas generadas (motivos propios, sin portadas reales) ---------- */
  const rnd = (seed) => { let t = seed + 0x6D2B79F5; return () => { t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; };
  function motivo(o, fg, ac) {
    const r = rnd(o.id * 97 + 13); let s = '';
    switch (o.motivo) {
      case 'laberinto': { let x = 20, y = 150; let d = `M${x} ${y}`; for (let i = 0; i < 26; i++) { const dir = Math.floor(r() * 4); const L = 12 + Math.floor(r() * 3) * 10; if (dir === 0) x = Math.min(180, x + L); if (dir === 1) y = Math.min(290, y + L); if (dir === 2) x = Math.max(20, x - L); if (dir === 3) y = Math.max(110, y - L); d += `L${x} ${y}`; } s = `<path d="${d}" fill="none" stroke="${ac}" stroke-width="3" stroke-linejoin="round"/><circle cx="20" cy="150" r="5" fill="${fg}"/><circle cx="${x}" cy="${y}" r="5" fill="${ac}"/>`; break; }
      case 'orbita': s = [30, 55, 80, 105].map((rad, i) => `<circle cx="100" cy="200" r="${rad}" fill="none" stroke="${i % 2 ? fg : ac}" stroke-width="2" opacity=".9"/>`).join('') + `<circle cx="${100 + 55 * Math.cos(r() * 6)}" cy="${200 + 55 * Math.sin(r() * 6)}" r="8" fill="${ac}"/><circle cx="100" cy="200" r="12" fill="${fg}"/>`; break;
      case 'lineas': for (let i = 0; i < 14; i++) s += `<line x1="${-20 + i * 18}" y1="300" x2="${60 + i * 18}" y2="110" stroke="${i % 3 ? fg : ac}" stroke-width="${i % 3 ? 2 : 4}" opacity=".85"/>`; break;
      case 'rejilla': for (let i = 0; i < 5; i++) for (let j = 0; j < 5; j++) { const f = r() > 0.55; s += `<rect x="${24 + i * 30}" y="${120 + j * 30}" width="24" height="24" fill="${f ? (r() > .5 ? ac : fg) : 'none'}" stroke="${fg}" stroke-width="1.5" rx="2"/>`; } break;
      case 'ondas': for (let k = 0; k < 6; k++) { let d = `M0 ${130 + k * 26}`; for (let x = 0; x <= 200; x += 25) d += ` Q${x + 12} ${130 + k * 26 + (k % 2 ? 14 : -14)} ${x + 25} ${130 + k * 26}`; s += `<path d="${d}" fill="none" stroke="${k % 2 ? fg : ac}" stroke-width="3"/>`; } break;
      case 'arcos': for (let k = 0; k < 6; k++) s += `<path d="M${20 + k * 8} ${290 - k * 22} a${80 - k * 8} ${80 - k * 8} 0 0 1 ${160 - k * 16} 0" fill="none" stroke="${k % 2 ? fg : ac}" stroke-width="5"/>`; break;
      default: s = '';
    }
    return `<svg class="absolute inset-0 w-full h-full" viewBox="0 0 200 300" preserveAspectRatio="xMidYMid slice" aria-hidden="true">${s}</svg>`;
  }
  BH.cover = (o, cls = 'w-full aspect-[2/3]', digital = false) => {
    const [bg, fg, ac] = BH.paletas[o.paleta % BH.paletas.length];
    return `<div class="cover ${cls}" style="background:${bg}" role="img" aria-label="Portada de ${o.titulo}">
      ${motivo(o, fg, ac)}
      <div class="absolute top-0 inset-x-0 p-[7%] pl-[11%]"><div class="font-display leading-[1.05] text-[clamp(.78rem,2.2vw,1.1rem)]" style="color:${fg}">${o.titulo}</div></div>
      <div class="absolute bottom-0 inset-x-0 p-[7%] pl-[11%] text-[.6rem] uppercase tracking-wider font-semibold" style="color:${fg}">${o.autor}</div>
      ${digital ? `<span class="absolute top-0 right-0 px-1.5 py-0.5 text-[.55rem] font-bold tracking-wider" style="background:${fg};color:${bg}">${o.digital ? o.digital.formato : 'DIGITAL'}</span>` : ''}</div>`;
  };

  /* Tarjeta de libro: celda de la cuadrícula; el enlace cubre toda la celda */
  BH.tarjeta = (o, sid) => `
    <article class="bh-celda">
      <a href="${ROOT}tienda/libro.html?id=${o.id}" class="absolute inset-0 z-10" aria-label="${o.titulo}, de ${o.autor}"></a>
      ${BH.cover(o)}
      <h3 class="font-display text-lg leading-tight line-clamp-2 mt-1">${o.titulo}</h3>
      <p class="bh-sub text-sm -mt-1">${o.autor}</p>
      <div class="flex flex-wrap gap-1">${BH.chipImpreso(o, sid)}${BH.chipDigital(o)}</div>
      <div class="mt-auto flex items-baseline justify-between pt-1"><span class="bh-sub text-xs">desde</span><span class="font-display text-xl">${BH.money(BH.desde(o))}</span></div>
    </article>`;

  /* Carritos de ejemplo para recorrer el checkout */
  BH.demoCart = (tipo) => {
    const c = {};
    if (tipo === 'digital') { c['12:digital'] = 1; c['15:digital'] = 1; }
    else if (tipo === 'impreso') { c['1:impreso'] = 1; c['6:impreso'] = 2; }
    else { c['5:impreso'] = 1; c['12:digital'] = 1; }
    store.set('bh2_cart', c);
  };

  /* ---------- sesión de ejemplo ---------- */
  BH.user = () => store.get('bh2_user', { nombre: 'Diego', email: 'diego@ejemplo.pe', tel: '987 654 321', consent: true });
  BH.logueado = () => store.get('bh2_logueado', true);

  /* ---------- carrito: líneas por obra y formato ---------- */
  BH.cart = {
    get() { return store.get('bh2_cart', {}); },
    set(c, bump) { store.set('bh2_cart', c); BH.refreshCart(bump); },
    key(id, f) { return id + ':' + f; },
    add(id, f, q = 1) { const c = this.get(), k = this.key(id, f); c[k] = f === 'digital' ? 1 : (c[k] || 0) + q; this.set(c, true); },
    setQty(k, q) { const c = this.get(); if (q <= 0) delete c[k]; else c[k] = q; this.set(c); },
    lineas() { return Object.entries(this.get()).map(([k, q]) => { const [id, f] = k.split(':'); return { k, obra: BH.obra(id), formato: f, cant: q }; }); },
    count() { return Object.values(this.get()).reduce((a, b) => a + b, 0); },
    clear() { this.set({}); }
  };
  BH.refreshCart = (bump) => document.querySelectorAll('[data-cart-count]').forEach((el) => {
    const n = BH.cart.count(); el.textContent = n; el.classList.toggle('hidden', n === 0);
    if (bump) { el.classList.remove('bump'); void el.offsetWidth; el.classList.add('bump'); }
  });

  /* ---------- biblioteca y pedidos (persisten en el navegador) ---------- */
  BH.biblioteca = {
    get() { return store.get('bh2_biblioteca', BH.bibliotecaSemilla); },
    set(v) { store.set('bh2_biblioteca', v); },
    add(obra) { const b = this.get(); if (!b.find((x) => x.obra === obra)) b.unshift({ obra, fecha: BH.hoy, usadas: 0 }); this.set(b); }
  };
  BH.pedidos = {
    get() { return store.get('bh2_pedidos', BH.pedidosSemilla); },
    add(p) { store.set('bh2_pedidos', [p, ...this.get()]); }
  };

  /* ---------- avisos y estados de pantalla ---------- */
  BH.toast = (msg, type = 'success') => {
    let box = document.getElementById('toasts');
    if (!box) { box = document.createElement('div'); box.id = 'toasts'; box.className = 'toast toast-end toast-top z-[70]'; document.body.append(box); }
    const el = document.createElement('div');
    el.className = `alert alert-${type} shadow aparece`; el.setAttribute('role', 'status'); el.textContent = msg;
    box.append(el); setTimeout(() => el.remove(), 3800);
  };
  BH.skeletons = (n = 8) => Array.from({ length: n }, () =>
    `<div class="bh-celda"><div class="skeleton aspect-[2/3] w-full"></div><div class="skeleton h-4 w-3/4"></div><div class="skeleton h-3 w-1/2"></div><div class="skeleton h-5 w-1/3"></div></div>`).join('');
  BH.emptyState = (icon, titulo, texto, accion = '') =>
    `<div class="bh-laberinto flex flex-col items-center text-center gap-3 py-16 px-4">${BH.icon(icon, 'size-12 opacity-60')}<h2 class="font-display text-2xl">${titulo}</h2><p class="max-w-md opacity-75">${texto}</p>${accion}</div>`;
  BH.errorState = (texto = 'No pudimos cargar la información. Revisa tu conexión e inténtalo otra vez.') =>
    `<div role="alert" class="alert alert-error alert-soft my-6">${BH.icon('warn')}<span>${texto}</span><button class="btn btn-sm" onclick="location.search=''">Reintentar</button></div>`;
  BH.gate = (texto) => `<div class="max-w-md mx-auto text-center py-16 space-y-3">${BH.icon('lock', 'size-12 mx-auto opacity-60')}<h1 class="font-display text-3xl">Inicia sesión</h1><p class="opacity-75">${texto}</p><a class="btn btn-primary" href="${ROOT}tienda/login.html">Ingresar</a></div>`;

  /* ---------- ready ---------- */
  const queue = []; let shellDone = false;
  BH.ready = (fn) => (shellDone ? fn() : queue.push(fn));

  /* ---------- shell de la Tienda ---------- */
  const LOGO = `<span class="grid place-items-center size-9 rounded-md bg-primary text-primary-content" aria-hidden="true"><svg viewBox="0 0 24 24" class="size-6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20V4h16v14H8V8h8v6h-4"/><circle cx="12" cy="14" r="1" fill="currentColor"/></svg></span>`;

  function shell(content, page) {
    const sel = BH.sucursales.map((s) => `<option value="${s.id}">${s.nombre}</option>`).join('');
    const nav = (id, label, href) => `<a href="${ROOT}${href}" class="btn btn-ghost btn-sm ${page === id ? 'btn-active' : ''}" ${page === id ? 'aria-current="page"' : ''}>${label}</a>`;
    const log = BH.logueado();
    document.body.innerHTML = `
    <a href="#main" class="sr-only focus:not-sr-only focus:absolute focus:z-50 btn btn-primary btn-sm m-2">Saltar al contenido</a>
    <div class="min-h-screen flex flex-col bg-base-100 text-base-content">
      <div class="bg-neutral text-neutral-content text-xs">
        <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center justify-between gap-3">
          <span class="hidden sm:flex items-center gap-4"><span class="flex items-center gap-1.5">${BH.icon('store', 'size-4')} Recojo en 6 sucursales</span><span class="flex items-center gap-1.5">${BH.icon('bolt', 'size-4')} Libros digitales al instante</span></span>
          <label class="flex items-center gap-2 ml-auto">Tu sucursal <select id="sel-sucursal" class="select select-xs w-32 bg-base-100 text-base-content">${sel}</select></label>
        </div></div>
      <header class="bg-base-100 border-b border-[var(--bh-linea)] sticky top-0 z-30">
        <div class="max-w-7xl mx-auto px-4 py-2 flex items-center gap-3">
          <a href="${ROOT}tienda/index.html" class="flex items-center gap-2 mr-1" aria-label="BabelHub, inicio">${LOGO}<span class="font-display text-2xl leading-none">BabelHub</span></a>
          <form class="hidden md:flex flex-1 max-w-md" role="search" onsubmit="event.preventDefault();location.href='${ROOT}tienda/catalogo.html?q='+encodeURIComponent(this.q.value)">
            <label class="input w-full"><span class="opacity-60">${BH.icon('search', 'size-4')}</span><input name="q" type="search" placeholder="Título, autor o editorial" aria-label="Buscar libros"></label></form>
          <nav class="ml-auto flex items-center gap-0.5" aria-label="Principal">
            <span class="hidden lg:flex items-center">${nav('inicio', 'Inicio', 'tienda/index.html')}${nav('catalogo', 'Catálogo', 'tienda/catalogo.html')}${nav('biblioteca', 'Mi biblioteca', 'tienda/biblioteca.html')}${nav('pedidos', 'Mis pedidos', 'tienda/pedidos.html')}</span>
            <button class="btn btn-ghost btn-circle" id="btn-theme" aria-label="Cambiar entre tema claro y oscuro"></button>
            <a href="${ROOT}tienda/carrito.html" class="btn btn-ghost btn-circle indicator" aria-label="Carrito">${BH.icon('cart')}<span data-cart-count class="indicator-item badge badge-primary badge-sm hidden">0</span></a>
            ${log ? `<a href="${ROOT}tienda/cuenta.html" class="btn btn-ghost btn-circle" aria-label="Mi cuenta">${BH.icon('user')}</a>`
                  : `<a href="${ROOT}tienda/login.html" class="btn btn-primary btn-sm ml-1">Ingresar</a>`}
          </nav>
        </div>
        <form class="md:hidden px-4 pb-2" role="search" onsubmit="event.preventDefault();location.href='${ROOT}tienda/catalogo.html?q='+encodeURIComponent(this.q.value)">
          <label class="input w-full"><span class="opacity-60">${BH.icon('search', 'size-4')}</span><input name="q" type="search" placeholder="Título, autor o editorial" aria-label="Buscar libros"></label></form>
        <nav class="lg:hidden flex gap-1 px-3 pb-2 overflow-x-auto text-sm" aria-label="Secciones">${nav('inicio', 'Inicio', 'tienda/index.html')}${nav('catalogo', 'Catálogo', 'tienda/catalogo.html')}${nav('biblioteca', 'Mi biblioteca', 'tienda/biblioteca.html')}${nav('pedidos', 'Pedidos', 'tienda/pedidos.html')}</nav>
      </header>
      <main id="main" class="flex-1 w-full"></main>
      <footer class="bh-laberinto border-t border-[var(--bh-linea)] bg-base-200 mt-12">
        <div class="max-w-7xl mx-auto px-4 py-10 grid gap-8 sm:grid-cols-2 lg:grid-cols-4 text-sm">
          <div class="space-y-2"><div class="flex items-center gap-2">${LOGO}<span class="font-display text-xl">BabelHub</span></div><p class="opacity-75 max-w-xs">Un universo cabe entre dos tapas. Libros impresos en tus sucursales y digitales al instante.</p></div>
          <div><h2 class="font-semibold mb-2">Tienda</h2><ul class="space-y-1"><li><a class="link link-hover" href="${ROOT}tienda/catalogo.html">Catálogo</a></li><li><a class="link link-hover" href="${ROOT}tienda/catalogo.html?formato=digital">Libros digitales</a></li><li><a class="link link-hover" href="${ROOT}tienda/indice-infinito.html">El índice infinito</a></li></ul></div>
          <div><h2 class="font-semibold mb-2">Ayuda</h2><ul class="space-y-1"><li><a class="link link-hover" href="${ROOT}tienda/legal.html?doc=reembolso">Reembolsos y devoluciones</a></li><li><a class="link link-hover" href="${ROOT}tienda/legal.html?doc=digital">Cómo funcionan los libros digitales</a></li><li><a class="link link-hover" href="${ROOT}tienda/pedidos.html">Seguir mi pedido</a></li></ul></div>
          <div><h2 class="font-semibold mb-2">Legal</h2><ul class="space-y-1"><li><a class="link link-hover" href="${ROOT}tienda/legal.html?doc=terminos">Términos y condiciones</a></li><li><a class="link link-hover" href="${ROOT}tienda/legal.html?doc=privacidad">Privacidad y datos personales</a></li><li><a class="link link-hover inline-flex items-center gap-1.5 font-semibold" href="${ROOT}tienda/reclamos.html">${BH.icon('book', 'size-4')} Libro de Reclamaciones</a></li></ul></div>
        </div>
        <div class="border-t border-[var(--bh-linea)] text-xs opacity-70"><div class="max-w-7xl mx-auto px-4 py-3 flex flex-wrap justify-between gap-2"><span>Prototipo de interfaz · datos ficticios · no es un sitio real</span><span>Pago seguro con Culqi · Avisos por WhatsApp</span></div></div>
      </footer>
    </div>
    <button id="fab-chat" class="btn btn-primary btn-circle fixed bottom-4 right-4 z-40 shadow-lg size-12 sm:size-14" aria-label="Abrir el asistente de compra">${BH.icon('chat', 'size-6')}</button>
    <dialog id="dlg-chat" class="modal modal-bottom sm:modal-middle">
      <div class="modal-box p-0 flex flex-col h-[32rem] max-h-[85vh]">
        <div class="p-4 border-b border-[var(--bh-linea)] flex items-center justify-between">
          <div><h2 class="font-display text-xl">Asistente de compra</h2><p class="text-xs opacity-70">Responde solo con datos del catálogo e inventario.</p></div>
          <form method="dialog"><button class="btn btn-sm btn-circle btn-ghost" aria-label="Cerrar">${BH.icon('x', 'size-4')}</button></form></div>
        <div id="chat-log" class="flex-1 overflow-y-auto p-4 space-y-3" aria-live="polite"></div>
        <form id="chat-form" class="p-3 border-t border-[var(--bh-linea)] flex gap-2"><input id="chat-in" class="input flex-1" placeholder="Pregunta por un libro o pide una recomendación" aria-label="Mensaje"><button class="btn btn-primary">Enviar</button></form>
      </div><form method="dialog" class="modal-backdrop"><button>cerrar</button></form></dialog>`;
    document.getElementById('main').append(...content.childNodes);
    const s = document.getElementById('sel-sucursal'); s.value = BH.miSucursal();
    s.addEventListener('change', () => { store.set('bh2_sucursal', s.value); location.reload(); });
    chat();
  }

  function chat() {
    const log = document.getElementById('chat-log'), dlg = document.getElementById('dlg-chat');
    const say = (who, html) => { const me = who === 'me'; log.insertAdjacentHTML('beforeend', `<div class="chat ${me ? 'chat-end' : 'chat-start'}"><div class="chat-bubble ${me ? 'chat-bubble-primary' : ''} text-sm">${html}</div></div>`); log.scrollTop = log.scrollHeight; };
    const reply = (q) => {
      const t = q.toLowerCase(), mine = BH.miSucursal();
      const hit = BH.obras.find((o) => t.includes(o.titulo.toLowerCase()) || o.titulo.toLowerCase().split(' ').some((w) => w.length > 5 && t.includes(w)));
      if (/recomi|sugier|leer/.test(t)) {
        const rec = BH.obras.filter((o) => BH.vendible(o, mine) > 0 || BH.digitalEstado(o).ok).slice(0, 3);
        return 'Para ti:<ul class="list-disc pl-4 mt-1">' + rec.map((o) => `<li><a class="link" href="${ROOT}tienda/libro.html?id=${o.id}">${o.titulo}</a> — ${o.autor}</li>`).join('') + '</ul>';
      }
      if (hit) {
        const partes = [];
        if (hit.impreso) { const s = BH.conStock(hit); partes.push(s.length ? `impreso en ${s.map((x) => `${x.nombre} (${BH.vendible(hit, x.id)})`).join(', ')}` : 'impreso agotado'); }
        partes.push(BH.digitalEstado(hit).ok ? 'digital disponible al instante' : 'sin edición digital disponible');
        return `<b>${hit.titulo}</b>: ${partes.join('; ')}. <a class="link" href="${ROOT}tienda/libro.html?id=${hit.id}">Ver libro</a>`;
      }
      if (/pedido|seguim|estado/.test(t)) return `Tu pedido BH-2026-000123 tiene un libro en tránsito entre sucursales. <a class="link" href="${ROOT}tienda/pedidos.html?id=BH-2026-000123">Ver seguimiento</a>`;
      return 'No tengo información suficiente para responder eso. Puedo ayudarte con disponibilidad de libros, recomendaciones y el estado de tu pedido.';
    };
    say('bot', '¡Hola! Pregúntame por la disponibilidad de un libro, pide una recomendación o consulta tu pedido.');
    document.getElementById('fab-chat').addEventListener('click', () => dlg.showModal());
    document.getElementById('chat-form').addEventListener('submit', (e) => { e.preventDefault(); const i = document.getElementById('chat-in'), q = i.value.trim(); if (!q) return; say('me', q.replace(/</g, '&lt;')); i.value = ''; setTimeout(() => say('bot', reply(q)), 350); });
  }

  /* ---------- barra del prototipo ---------- */
  function protoBar() {
    const estados = (document.body.dataset.estados || '').split(',').filter(Boolean);
    const bar = document.createElement('div'); bar.className = 'proto-bar no-print';
    const opts = estados.map((e) => `<option value="${e}" ${e === BH.estado ? 'selected' : ''}>${e}</option>`).join('');
    bar.innerHTML = `<span>Prototipo</span>${estados.length ? `<label>estado <select id="proto-estado">${opts}</select></label>` : ''}${document.body.dataset.area === 'tienda' ? `<label>sesión <select id="proto-sesion"><option value="1" ${BH.logueado() ? 'selected' : ''}>cliente</option><option value="0" ${BH.logueado() ? '' : 'selected'}>visitante</option></select></label>` : ''}<a href="${ROOT}index.html">Índice</a>`;
    document.body.append(bar);
    const s = document.getElementById('proto-estado');
    if (s) s.addEventListener('change', () => { const u = new URL(location.href); if (s.value === 'normal') u.searchParams.delete('estado'); else u.searchParams.set('estado', s.value); location.href = u; });
    const ps = document.getElementById('proto-sesion');
    if (ps) ps.addEventListener('change', (e) => { store.set('bh2_logueado', e.target.value === '1'); location.reload(); });
  }

  /* «Sorpréndeme»: elige una obra disponible al azar y pasa a su ficha con una transición corta */
  BH.sorprendeme = () => {
    const mine = BH.miSucursal();
    const l = BH.obras.filter((o) => BH.vendible(o, mine) > 0 || BH.digitalEstado(o).ok);
    const o = l[Math.floor(Math.random() * l.length)];
    document.body.classList.add('pagina-sale');
    setTimeout(() => (location.href = ROOT + 'tienda/libro.html?id=' + o.id), 260);
  };

  document.addEventListener('DOMContentLoaded', () => {
    const { area, page } = document.body.dataset;
    const content = document.getElementById('page-content');
    if (content && area === 'tienda') shell(content, page);
    else if (content && BH.shellPanel && ['sucursal', 'logistica', 'admin'].includes(area)) BH.shellPanel(content, area, page);
    const tb = document.getElementById('btn-theme');
    const paint = () => { if (tb) tb.innerHTML = BH.icon(document.documentElement.dataset.theme === 'babelhub' ? 'moon' : 'sun'); };
    if (tb) tb.addEventListener('click', () => { setTheme(document.documentElement.dataset.theme === 'babelhub' ? 'babelhub-dark' : 'babelhub'); paint(); });
    paint(); BH.refreshCart(); if (area !== 'hub') protoBar();
    shellDone = true; queue.forEach((fn) => fn());
  });
})();
