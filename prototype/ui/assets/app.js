/* Utilidades y estructura común (shell) del prototipo. Solo para validar la UI. */
(function () {
  const BH = (window.BH = window.BH || {});
  const ROOT = document.currentScript.src.replace(/assets\/app\.js.*$/, '');

  /* ---------- almacenamiento (puede fallar: modo privado, file://) ---------- */
  const store = {
    get(k, d) { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : d; } catch (e) { return d; } },
    set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* sin persistencia */ } }
  };
  BH.store = store;

  /* ---------- tema ---------- */
  const setTheme = (t) => { document.documentElement.setAttribute('data-theme', t); store.set('bh_theme', t); };
  setTheme(store.get('bh_theme', 'babelhub'));

  /* ---------- iconos (Heroicons outline, 24px) ---------- */
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
    cube: 'm21 7.5-9-5.25L3 7.5m18 0-9 5.25m9-5.25v9l-9 5.25M3 7.5l9 5.25M3 7.5v9l9 5.25m0-9v9',
    shield: 'M9 12.75 11.25 15 15 9.75m-3-7.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285Z',
    trash: 'm14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0'
  };
  BH.icon = (n, cls = 'size-5') =>
    `<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.6" stroke="currentColor" class="${cls}" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" d="${P[n] || ''}"/></svg>`;

  /* ---------- formato y datos ---------- */
  BH.money = (n) => 'S/ ' + Number(n).toLocaleString('es-PE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  BH.libro = (id) => BH.libros.find((b) => b.id === Number(id));
  BH.sucursal = (id) => BH.sucursales.find((s) => s.id === id);
  BH.vendible = (b, sid) => { const s = b.stock[sid]; return s ? s[0] - s[1] : 0; };
  BH.totalVendible = (b) => BH.sucursales.reduce((a, s) => a + BH.vendible(b, s.id), 0);
  BH.conStock = (b) => BH.sucursales.filter((s) => BH.vendible(b, s.id) > 0);
  BH.miSucursal = () => store.get('bh_sucursal', 'aya');
  BH.estado = new URLSearchParams(location.search).get('estado') || 'normal';
  BH.param = (k) => new URLSearchParams(location.search).get(k);

  BH.badge = (b, sid) => {
    const t = BH.totalVendible(b);
    const cls = 'badge badge-soft badge-sm h-auto py-0.5 whitespace-normal text-left';
    if (t === 0) return `<span class="${cls} badge-error">Agotado</span>`;
    if (sid && BH.vendible(b, sid) > 0)
      return t <= 3 ? `<span class="${cls} badge-warning">Últimas unidades</span>` : `<span class="${cls} badge-success">En tu sucursal</span>`;
    const n = BH.conStock(b).length;
    return `<span class="${cls} badge-info">En ${n} ${n === 1 ? 'otra sucursal' : 'otras sucursales'}</span>`;
  };

  BH.cover = (b, cls = 'w-full aspect-[2/3]') =>
    `<div class="${cls} relative rounded-md shadow-md overflow-hidden flex flex-col justify-between p-3 text-white" style="background:linear-gradient(145deg,hsl(${b.hue} 45% 34%),hsl(${b.hue} 50% 18%))">
      <span class="absolute inset-y-0 left-0 w-2 bg-black/25"></span>
      <span class="font-display text-sm sm:text-base leading-tight pl-2">${b.titulo}</span>
      <span class="text-[10px] uppercase tracking-wide opacity-80 pl-2">${b.autor}</span></div>`;

  /* ---------- carrito ---------- */
  BH.cart = {
    get() { return store.get('bh_cart', {}); },
    set(c) { store.set('bh_cart', c); BH.refreshCart(); },
    add(id, q = 1) { const c = this.get(); c[id] = (c[id] || 0) + q; this.set(c); },
    setQty(id, q) { const c = this.get(); if (q <= 0) delete c[id]; else c[id] = q; this.set(c); },
    count() { return Object.values(this.get()).reduce((a, b) => a + b, 0); },
    clear() { this.set({}); }
  };
  BH.refreshCart = () => {
    document.querySelectorAll('[data-cart-count]').forEach((el) => {
      const n = BH.cart.count(); el.textContent = n; el.classList.toggle('hidden', n === 0);
    });
  };

  /* ---------- auditoría (compartida entre páginas por localStorage) ---------- */
  BH.audit = {
    list() { return [...store.get('bh_audit', []), ...BH.auditSemilla]; },
    add(usuario, rol, accion, detalle) {
      const d = new Date(); const p = (n) => String(n).padStart(2, '0');
      const fecha = `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
      store.set('bh_audit', [{ fecha, usuario, rol, accion, detalle }, ...store.get('bh_audit', [])]);
    }
  };

  /* ---------- avisos ---------- */
  BH.toast = (msg, type = 'success') => {
    let box = document.getElementById('toasts');
    if (!box) { box = document.createElement('div'); box.id = 'toasts'; box.className = 'toast toast-end toast-top z-[70]'; document.body.append(box); }
    const el = document.createElement('div');
    el.className = `alert alert-${type} shadow`; el.setAttribute('role', 'status'); el.textContent = msg;
    box.append(el); setTimeout(() => el.remove(), 3500);
  };

  BH.skeletonCards = (n = 8) => Array.from({ length: n }, () =>
    `<div class="card bg-base-100 border border-base-300"><div class="card-body gap-3 p-4"><div class="skeleton aspect-[2/3] w-full"></div><div class="skeleton h-4 w-3/4"></div><div class="skeleton h-3 w-1/2"></div><div class="skeleton h-6 w-1/3"></div></div></div>`).join('');

  BH.emptyState = (icon, titulo, texto, accion = '') =>
    `<div class="flex flex-col items-center text-center gap-3 py-16 text-base-content/70">${BH.icon(icon, 'size-12 opacity-60')}<h3 class="font-display text-xl text-base-content">${titulo}</h3><p class="max-w-md">${texto}</p>${accion}</div>`;

  BH.errorState = (texto = 'No pudimos cargar la información. Revisa tu conexión e inténtalo otra vez.') =>
    `<div role="alert" class="alert alert-error alert-soft my-6">${BH.icon('warn')}<span>${texto}</span><button class="btn btn-sm" onclick="location.search=''">Reintentar</button></div>`;

  /* ---------- ready ---------- */
  const queue = []; let shellDone = false;
  BH.ready = (fn) => (shellDone ? fn() : queue.push(fn));

  /* ---------- shell: tienda ---------- */
  function shellTienda(content, page) {
    const suc = BH.sucursales.map((s) => `<option value="${s.id}">${s.nombre}</option>`).join('');
    const nav = (id, label, href) => `<a href="${ROOT}${href}" class="btn btn-ghost btn-sm ${page === id ? 'btn-active' : ''}">${label}</a>`;
    document.body.innerHTML = `
    <a href="#main" class="sr-only focus:not-sr-only focus:absolute focus:z-50 btn btn-primary btn-sm m-2">Saltar al contenido</a>
    <div class="min-h-screen flex flex-col bg-base-100">
      <div class="bg-neutral text-neutral-content text-xs">
        <div class="max-w-7xl mx-auto px-4 py-1.5 flex items-center justify-between gap-3">
          <span class="flex items-center gap-1.5">${BH.icon('store', 'size-4')} Recojo en tienda en 6 sucursales</span>
          <label class="flex items-center gap-2">Tu sucursal
            <select id="sel-sucursal" class="select select-xs w-36 text-base-content">${suc}</select></label>
        </div>
      </div>
      <header class="bg-base-100 border-b border-base-300 sticky top-0 z-30">
        <div class="max-w-7xl mx-auto px-4 py-2 flex items-center gap-3">
          <a href="${ROOT}tienda/index.html" class="flex items-center gap-2 mr-2">
            <span class="grid place-items-center size-9 rounded-lg bg-primary text-primary-content">${BH.icon('book', 'size-5')}</span>
            <span class="font-display text-xl font-bold leading-none">BabelHub</span></a>
          <form class="hidden md:flex flex-1 max-w-xl" role="search" onsubmit="event.preventDefault();location.href='${ROOT}tienda/index.html?q='+encodeURIComponent(this.q.value)">
            <label class="input w-full"><span class="opacity-60">${BH.icon('search', 'size-4')}</span>
              <input name="q" type="search" placeholder="Título, autor o editorial" aria-label="Buscar libros"></label></form>
          <nav class="ml-auto flex items-center gap-1">
            <span class="hidden lg:flex items-center gap-1">${nav('catalogo', 'Catálogo', 'tienda/index.html')}${nav('pedido', 'Mi pedido', 'tienda/pedido.html')}</span>
            <button class="btn btn-ghost btn-circle" id="btn-theme" aria-label="Cambiar tema claro u oscuro">${BH.icon('moon')}</button>
            <a href="${ROOT}tienda/carrito.html" class="btn btn-ghost btn-circle indicator" aria-label="Carrito">
              ${BH.icon('cart')}<span data-cart-count class="indicator-item badge badge-primary badge-sm hidden">0</span></a>
            <a href="${ROOT}index.html" class="btn btn-ghost btn-circle" aria-label="Cuenta">${BH.icon('user')}</a>
          </nav>
        </div>
        <form class="md:hidden px-4 pb-2" role="search" onsubmit="event.preventDefault();location.href='${ROOT}tienda/index.html?q='+encodeURIComponent(this.q.value)">
          <label class="input w-full"><span class="opacity-60">${BH.icon('search', 'size-4')}</span>
            <input name="q" type="search" placeholder="Título, autor o editorial" aria-label="Buscar libros"></label></form>
      </header>
      <main id="main" class="flex-1 w-full max-w-7xl mx-auto px-4 py-6"></main>
      <footer class="bg-base-200 border-t border-base-300 mt-8">
        <div class="max-w-7xl mx-auto px-4 py-6 text-sm text-base-content/70 flex flex-wrap gap-x-6 gap-y-2 justify-between">
          <span class="font-display text-base text-base-content">BabelHub</span>
          <span>Prototipo de interfaz · datos ficticios · no es un sitio real</span>
          <span>Pago seguro con Culqi · Avisos por WhatsApp</span></div></footer>
    </div>
    <button id="fab-chat" class="btn btn-primary btn-circle btn-lg fixed bottom-4 right-4 z-40 shadow-lg" aria-label="Abrir asistente de compra">${BH.icon('chat', 'size-7')}</button>
    <dialog id="dlg-chat" class="modal modal-bottom sm:modal-middle">
      <div class="modal-box p-0 flex flex-col h-[32rem] max-h-[85vh]">
        <div class="p-4 border-b border-base-300 flex items-center justify-between">
          <div><h3 class="font-display text-lg">Asistente de compra</h3><p class="text-xs text-base-content/60">Responde solo con datos del catálogo e inventario.</p></div>
          <form method="dialog"><button class="btn btn-sm btn-circle btn-ghost" aria-label="Cerrar">${BH.icon('x', 'size-4')}</button></form></div>
        <div id="chat-log" class="flex-1 overflow-y-auto p-4 space-y-3" aria-live="polite"></div>
        <form id="chat-form" class="p-3 border-t border-base-300 flex gap-2">
          <input id="chat-in" class="input flex-1" placeholder="Pregunta por un libro o pide una recomendación" aria-label="Mensaje">
          <button class="btn btn-primary">Enviar</button></form>
      </div>
      <form method="dialog" class="modal-backdrop"><button>cerrar</button></form>
    </dialog>`;
    document.getElementById('main').append(...content.childNodes);
    const sel = document.getElementById('sel-sucursal');
    sel.value = BH.miSucursal();
    sel.addEventListener('change', () => { store.set('bh_sucursal', sel.value); location.reload(); });
    chat();
  }

  function chat() {
    const log = document.getElementById('chat-log'), dlg = document.getElementById('dlg-chat');
    const say = (who, html) => {
      const me = who === 'me';
      log.insertAdjacentHTML('beforeend', `<div class="chat ${me ? 'chat-end' : 'chat-start'}"><div class="chat-bubble ${me ? 'chat-bubble-primary' : ''} text-sm">${html}</div></div>`);
      log.scrollTop = log.scrollHeight;
    };
    const reply = (q) => {
      const t = q.toLowerCase();
      const hit = BH.libros.find((b) => t.includes(b.titulo.toLowerCase()) || b.titulo.toLowerCase().split(' ').some((w) => w.length > 5 && t.includes(w)));
      const mine = BH.miSucursal();
      if (/recomi|sugier|leer/.test(t)) {
        const rec = BH.libros.filter((b) => BH.totalVendible(b) > 0 && (BH.vendible(b, mine) > 0)).slice(0, 3);
        return 'Con stock en tu sucursal te sugiero:<ul class="list-disc pl-4 mt-1">' + rec.map((b) => `<li><a class="link" href="${ROOT}tienda/libro.html?id=${b.id}">${b.titulo}</a> — ${b.autor}</li>`).join('') + '</ul>';
      }
      if (hit) {
        const s = BH.conStock(hit);
        return s.length
          ? `<b>${hit.titulo}</b>: ${BH.totalVendible(hit)} ud. vendibles en la red. Disponible en ${s.map((x) => `${x.nombre} (${BH.vendible(hit, x.id)})`).join(', ')}. <a class="link" href="${ROOT}tienda/libro.html?id=${hit.id}">Ver libro</a>`
          : `<b>${hit.titulo}</b> está agotado en todas las sucursales por ahora.`;
      }
      if (/pedido|seguim|estado/.test(t)) return 'Tu pedido BH-2026-000123 está <b>en tránsito entre sucursales</b>. <a class="link" href="' + ROOT + 'tienda/pedido.html">Ver seguimiento</a>';
      return 'No tengo información suficiente para responder eso. Puedo ayudarte con disponibilidad de libros, recomendaciones y el estado de tu pedido.';
    };
    say('bot', '¡Hola! Pregúntame por la disponibilidad de un libro, pide una recomendación o consulta tu pedido.');
    document.getElementById('fab-chat').addEventListener('click', () => dlg.showModal());
    document.getElementById('chat-form').addEventListener('submit', (e) => {
      e.preventDefault(); const i = document.getElementById('chat-in'); const q = i.value.trim(); if (!q) return;
      say('me', q.replace(/</g, '&lt;')); i.value = ''; setTimeout(() => say('bot', reply(q)), 400);
    });
  }

  /* ---------- shell: panel (sucursal y admin) ---------- */
  function shellPanel(content, area, page) {
    const items = area === 'sucursal'
      ? [['stock', 'Inventario', 'sucursal/stock.html', 'cube'], ['traslados', 'Traslados', 'sucursal/traslados.html', 'swap']]
      : [['catalogo', 'Catálogo', 'admin/catalogo.html', 'book'], ['auditoria', 'Auditoría', 'admin/auditoria.html', 'shield']];
    const soon = area === 'admin' ? ['Sucursales', 'Usuarios y roles', 'Indicadores'] : ['Pedidos de recojo'];
    const who = area === 'sucursal'
      ? { rol: 'Encargado de sucursal', alcance: 'Ayacucho', user: 'enc.ayacucho' }
      : { rol: 'Administrador central', alcance: 'Red nacional', user: 'admin.central' };
    const titulo = area === 'sucursal' ? 'Panel de sucursal' : 'Administración central';
    document.body.innerHTML = `
    <a href="#main" class="sr-only focus:not-sr-only focus:absolute focus:z-50 btn btn-primary btn-sm m-2">Saltar al contenido</a>
    <div class="drawer lg:drawer-open">
      <input id="nav-drawer" type="checkbox" class="drawer-toggle" aria-label="Abrir menú">
      <div class="drawer-content flex flex-col min-h-screen bg-base-100">
        <header class="navbar bg-base-100 border-b border-base-300 sticky top-0 z-30 px-4 gap-2">
          <label for="nav-drawer" class="btn btn-ghost btn-square lg:hidden" aria-label="Abrir menú">${BH.icon('menu')}</label>
          <div class="flex-1 min-w-0"><span class="font-display text-lg truncate">${titulo}</span></div>
          <span class="badge badge-primary badge-soft hidden sm:inline-flex" title="Alcance del token">${BH.icon('store', 'size-3.5')} ${who.alcance}</span>
          <button class="btn btn-ghost btn-circle" id="btn-theme" aria-label="Cambiar tema claro u oscuro">${BH.icon('moon')}</button>
          <div class="hidden sm:block text-right leading-tight"><div class="text-sm font-medium">${who.user}</div><div class="text-xs text-base-content/60">${who.rol}</div></div>
        </header>
        <main id="main" class="flex-1 p-4 sm:p-6 max-w-6xl w-full"></main>
      </div>
      <div class="drawer-side z-40">
        <label for="nav-drawer" class="drawer-overlay" aria-label="Cerrar menú"></label>
        <aside class="w-64 bg-base-200 min-h-full border-r border-base-300 flex flex-col">
          <a href="${ROOT}index.html" class="flex items-center gap-2 p-4">
            <span class="grid place-items-center size-9 rounded-lg bg-primary text-primary-content">${BH.icon('book', 'size-5')}</span>
            <span class="font-display text-xl font-bold">BabelHub</span></a>
          <ul class="menu w-full px-2 gap-0.5 flex-1">
            ${items.map(([id, label, href, ic]) => `<li><a href="${ROOT}${href}" class="${page === id ? 'menu-active' : ''}">${BH.icon(ic, 'size-5')}${label}</a></li>`).join('')}
            <li class="menu-title mt-3">Próximamente</li>
            ${soon.map((l) => `<li class="menu-disabled"><a>${l}</a></li>`).join('')}
          </ul>
          <div class="p-4 text-xs text-base-content/60 border-t border-base-300">Prototipo · datos ficticios<br><a class="link" href="${ROOT}tienda/index.html">Ir a la tienda</a></div>
        </aside>
      </div>
    </div>`;
    document.getElementById('main').append(...content.childNodes);
  }

  /* ---------- barra del prototipo (cambio de estado de pantalla) ---------- */
  function protoBar() {
    const estados = (document.body.dataset.estados || '').split(',').filter(Boolean);
    if (!estados.length && document.body.dataset.area === 'hub') return;
    const bar = document.createElement('div'); bar.className = 'proto-bar';
    const opts = estados.map((e) => `<option value="${e}" ${e === BH.estado ? 'selected' : ''}>${e}</option>`).join('');
    bar.innerHTML = `<span>Prototipo</span>${estados.length ? `<label class="flex items-center gap-1">estado <select class="select select-xs text-base-content w-36" id="proto-estado">${opts}</select></label>` : ''}<a class="link" href="${ROOT}index.html">Índice</a>`;
    document.body.append(bar);
    const s = document.getElementById('proto-estado');
    if (s) s.addEventListener('change', () => {
      const u = new URL(location.href); if (s.value === 'normal') u.searchParams.delete('estado'); else u.searchParams.set('estado', s.value); location.href = u;
    });
  }

  document.addEventListener('DOMContentLoaded', () => {
    const { area, page } = document.body.dataset;
    const content = document.getElementById('page-content');
    if (content && area === 'tienda') shellTienda(content, page);
    else if (content && (area === 'sucursal' || area === 'admin')) shellPanel(content, area, page);
    const themeBtn = document.getElementById('btn-theme');
    const paint = () => { if (themeBtn) themeBtn.innerHTML = BH.icon(document.documentElement.dataset.theme === 'babelhub' ? 'moon' : 'sun'); };
    if (themeBtn) themeBtn.addEventListener('click', () => { setTheme(document.documentElement.dataset.theme === 'babelhub' ? 'babelhub-dark' : 'babelhub'); paint(); });
    paint(); BH.refreshCart(); protoBar();
    shellDone = true; queue.forEach((fn) => fn());
  });
})();
