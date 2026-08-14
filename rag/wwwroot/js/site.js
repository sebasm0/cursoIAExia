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
    // Enter sends (Shift+Enter keeps the newline), mirroring chat conventions.
    var textarea = form.querySelector('textarea');
    if (textarea) {
      textarea.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
          e.preventDefault();
          form.requestSubmit();
        }
      });
    }

    form.addEventListener('submit', function () {
      var btn = form.querySelector('.chat-send');
      if (btn) {
        btn.classList.add('is-loading');
        btn.disabled = true;
      }
    });
  });
})();

// User profile dropdown in the sidebar account widget. Clicking the trigger
// toggles the menu; clicking outside or pressing Escape closes it. Menu and
// trigger stay unlinked until they are both present (no-op otherwise).
(function () {
  'use strict';

  var root = document.querySelector('[data-profile-menu]');
  if (!root) {
    return;
  }

  var trigger = root.querySelector('.profile-trigger');
  var menu = root.querySelector('.profile-menu');
  if (!trigger || !menu) {
    return;
  }

  var focusables = Array.prototype.slice.call(
    menu.querySelectorAll('button, a[href], input, select, textarea'));

  function setOpen(open) {
    menu.hidden = !open;
    trigger.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (open && focusables.length) {
      focusables[0].focus();
    }
  }

  trigger.addEventListener('click', function (e) {
    e.stopPropagation();
    setOpen(menu.hidden);
  });

  document.addEventListener('click', function (e) {
    if (!menu.hidden && !root.contains(e.target)) {
      setOpen(false);
    }
  });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !menu.hidden) {
      setOpen(false);
      trigger.focus();
    }
  });
})();

// DocsChat: the floating chat in Documents submits over fetch to the AskJson
// endpoint (data-ask-json-url) and renders the answer in the chat panel in
// place — no page navigation. The antiforgery token travels as a form field
// (FormData picks up the hidden input), preserving the same
// [ValidateAntiForgeryToken] contract as every other POST. All dynamic text is
// set via textContent (never innerHTML) so user queries and LLM answers are
// rendered as plain text. Enter-to-send / Shift+Enter-newline behavior is
// handled by the generic .chat-composer block above (requestSubmit).
(function () {
  'use strict';

  var form = document.getElementById('docs-chat-form');
  if (!form) {
    return;
  }

  var jsonUrl = form.getAttribute('data-ask-json-url');
  var messages = document.getElementById('docsChatPanel');
  var sendBtn = form.querySelector('.chat-send');
  var textarea = form.querySelector('textarea');
  if (!jsonUrl || !messages || !sendBtn || !textarea) {
    return;
  }

  var emptyState = messages.querySelector('.chat-empty');

  function createBubble(kind, text) {
    var bubble = document.createElement('div');
    bubble.className = 'msg-bubble ' + kind;
    bubble.textContent = text;
    return bubble;
  }

  function assistantRow() {
    var row = document.createElement('div');
    row.className = 'msg-row msg-row-assistant';

    var icon = document.createElement('span');
    icon.className = 'msg-assistant-icon';
    icon.setAttribute('aria-hidden', 'true');
    icon.innerHTML =
      '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">' +
      '<path d="M3.5 6.5A1.5 1.5 0 0 1 5 5h6a1.5 1.5 0 0 1 1.5 1.5v3A1.5 1.5 0 0 1 11 11H5a1.5 1.5 0 0 1-1.5-1.5z"/>' +
      '<path d="M8 10a2 2 0 0 1-2-2V3a2 2 0 1 1 4 0v5a2 2 0 0 1-2 2zM3 13.5a.5.5 0 0 1 .5-.5h9a.5.5 0 0 1 0 1h-9a.5.5 0 0 1-.5-.5z"/></svg>';

    var body = document.createElement('div');
    row.appendChild(icon);
    row.appendChild(body);
    messages.appendChild(row);
    return body;
  }

  function setPending(body) {
    body.textContent = '';
    body.appendChild(createBubble('msg-assistant', 'Generando…'));
  }

  function renderAnswer(body, answer, usedModel) {
    body.textContent = '';
    body.appendChild(createBubble('msg-assistant', answer));
    var credit = document.createElement('div');
    credit.className = 'small text-muted mt-1';
    credit.textContent = 'Generado por ' + usedModel;
    body.appendChild(credit);
  }

  function renderError(body, error) {
    body.textContent = '';
    body.appendChild(createBubble('msg-error', error));
  }

  function setBusy(busy) {
    sendBtn.disabled = busy;
    sendBtn.classList.toggle('is-loading', busy);
  }

  form.addEventListener('submit', function (e) {
    e.preventDefault();

    var query = textarea.value.trim();

    // The server also guards (400 JSON); this keeps a blank bubble off the log.
    if (!query) {
      renderError(assistantRow(), 'Por favor, ingrese una pregunta.');
      return;
    }

    // First interaction replaces the placeholder state.
    if (emptyState) {
      emptyState.remove();
      emptyState = null;
    }

    var userRow = document.createElement('div');
    userRow.className = 'msg-row msg-row-user';
    userRow.appendChild(createBubble('msg-user', query));
    messages.appendChild(userRow);

    var answerBody = assistantRow();
    setPending(answerBody);
    setBusy(true);

    // FormData includes Query, SelectedModelId and the antiforgery token.
    var payload = new URLSearchParams(new FormData(form));

    fetch(jsonUrl, { method: 'POST', body: payload })
      .then(function (response) {
        return response.json().then(function (data) {
          return { ok: response.ok, data: data };
        });
      })
      .then(function (result) {
        if (result.ok && typeof result.data.answer === 'string') {
          renderAnswer(answerBody, result.data.answer, result.data.usedModel || '');
        } else {
          renderError(answerBody, result.data.error || 'No se pudo generar una respuesta.');
        }
      })
      .catch(function () {
        renderError(answerBody, 'No se pudo conectar con el servicio. Intente de nuevo.');
      })
      .finally(function () {
        setBusy(false);
        textarea.value = '';
        textarea.focus();
      });
  });
})();
