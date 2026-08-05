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

  toggle.addEventListener('click', function () {
    var next = currentTheme() === 'dark' ? 'light' : 'dark';

    document.documentElement.setAttribute('data-bs-theme', next);
    localStorage.setItem('rag-theme', next);

    var isDark = next === 'dark';
    toggle.setAttribute('aria-pressed', isDark ? 'true' : 'false');
    toggle.setAttribute('aria-label', isDark ? 'Switch to light theme' : 'Switch to dark theme');
  });
})();
