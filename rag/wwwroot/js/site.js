// Theme toggle (UDS-2/UDS-3): switches data-bs-theme on <html> and persists
// the choice in localStorage['rag-theme']. The inline head script in _Layout
// applies the stored value before first paint (no flash); when JavaScript is
// disabled the server-rendered default (light) remains.

(function () {
  'use strict';

  var toggle = document.getElementById('theme-toggle');
  if (!toggle) {
    return;
  }

  function currentTheme() {
    return document.documentElement.getAttribute('data-bs-theme') === 'dark'
      ? 'dark'
      : 'light';
  }

// Reflect the dark-first default on the button before any interaction.
    toggle.setAttribute('aria-pressed', currentTheme() === 'dark' ? 'true' : 'false');
    toggle.setAttribute('aria-label', currentTheme() === 'dark'
      ? 'Cambiar a modo claro'
      : 'Cambiar a modo oscuro');

toggle.addEventListener('click', function () {
      var next = currentTheme() === 'dark' ? 'light' : 'dark';

      document.documentElement.setAttribute('data-bs-theme', next);
      localStorage.setItem('rag-theme', next);

      var isDark = next === 'dark';
      toggle.setAttribute('aria-pressed', isDark ? 'true' : 'false');
      toggle.setAttribute('aria-label', isDark ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro');
    });
})();

// Collapsible sidebar on narrow screens (add/remove .sidebar-open on <body>).
(function () {
  'use strict';

  var openBtn = document.getElementById('sidebar-toggle');
  var closeBtn = document.getElementById('sidebar-close');
  var backdrop = document.getElementById('sidebar-backdrop');

  if (!openBtn) {
    return;
  }

  function setOpen(open) {
    document.body.classList.toggle('sidebar-open', open);
    if (openBtn) {
      openBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
    }
  }

  openBtn.setAttribute('aria-expanded', 'false');
  openBtn.addEventListener('click', function () { setOpen(true); });

  if (closeBtn) {
    closeBtn.addEventListener('click', function () { setOpen(false); });
  }
  if (backdrop) {
    backdrop.addEventListener('click', function () { setOpen(false); });
  }
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
      setOpen(false);
    }
  });
})();

// Cosmetic "Cuadrícula / Lista" toggle on the documents view.
(function () {
  'use strict';

  var buttons = document.querySelectorAll('[data-view-toggle]');
  if (!buttons.length) {
    return;
  }

  var gridView = document.getElementById('docsViewGrid');
  var listView = document.getElementById('docsViewList');
  if (!gridView || !listView) {
    return;
  }

  function show(mode) {
    var isGrid = mode === 'grid';
    gridView.hidden = !isGrid;
    listView.hidden = isGrid;
    buttons.forEach(function (b) {
      b.setAttribute('aria-pressed', b.getAttribute('data-view-toggle') === mode ? 'true' : 'false');
    });
  }

  buttons.forEach(function (btn) {
    btn.addEventListener('click', function () {
      show(btn.getAttribute('data-view-toggle'));
    });
  });
})();

// Collapsible right chat column (Documents, small laptop / tablet). Toggling
// .chat-collapsed on the docs layout hides the column and shows the floating
// "IA Asistente" pill. State persists in localStorage['rag-chat-collapsed'].
(function () {
  'use strict';

  var layout = document.querySelector('.docs-layout');
  var toggleBtn = document.getElementById('chat-collapse-toggle');
  var floatBtn = document.getElementById('chat-float-open');
  if (!layout || !toggleBtn) {
    return;
  }

  var STORAGE_KEY = 'rag-chat-collapsed';

  function apply(collapsed) {
    layout.classList.toggle('chat-collapsed', collapsed);
    toggleBtn.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
    toggleBtn.setAttribute('aria-label',
      collapsed ? 'Mostrar el chat del asistente' : 'Ocultar el chat del asistente');
    try {
      localStorage.setItem(STORAGE_KEY, collapsed ? '1' : '0');
    } catch (e) { /* storage unavailable: non-functional */ }
  }

  try {
    if (localStorage.getItem(STORAGE_KEY) === '1') {
      apply(true);
    }
  } catch (e) { /* ignore */ }

  toggleBtn.addEventListener('click', function () {
    apply(!layout.classList.contains('chat-collapsed'));
  });
  if (floatBtn) {
    floatBtn.addEventListener('click', function () { apply(false); });
  }
})();

// Drag-over highlight on the Documents center drop zone (link to Upload).
// Visual only — the drop itself is handled by the Upload screen.
(function () {
  'use strict';

  var zone = document.getElementById('docs-dropzone');
  if (!zone) {
    return;
  }

  var title = zone.querySelector('.drop-zone-title');
  var defaultTitle = title ? title.textContent.trim() : '';

  function setActive(active) {
    zone.classList.toggle('dropzone-active', active);
    if (title) {
      title.textContent = active ? 'Suelte para subir' : defaultTitle;
    }
  }

  ['dragenter', 'dragover'].forEach(function (type) {
    zone.addEventListener(type, function (e) {
      e.preventDefault();
      setActive(true);
    });
  });

  ['dragleave', 'drop'].forEach(function (type) {
    zone.addEventListener(type, function (e) {
      e.preventDefault();
      setActive(false);
    });
  });
})();

// Honest busy state for the chat send button: on a real form submit we disable
// the button and show a working spinner (no fake typing / streaming here — the
// answer still arrives via the normal POST full-page render).
(function () {
  'use strict';

  var forms = document.querySelectorAll('.chat-composer form');
  forms.forEach(function (form) {
    form.addEventListener('submit', function () {
      var btn = form.querySelector('.chat-send');
      if (btn) {
        btn.classList.add('is-loading');
        btn.disabled = true;
      }
    });
  });
})();
