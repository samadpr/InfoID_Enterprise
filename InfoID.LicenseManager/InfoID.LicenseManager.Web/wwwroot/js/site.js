// InfoID License Manager - UI behaviour. Everything is progressive enhancement: with scripts blocked the pages still work.
(function () {
    'use strict';

    var root = document.documentElement;
    var $ = function (selector, scope) { return (scope || document).querySelector(selector); };
    var $$ = function (selector, scope) { return Array.prototype.slice.call((scope || document).querySelectorAll(selector)); };
    var store = {
        get: function (key) { try { return localStorage.getItem(key); } catch (e) { return null; } },
        set: function (key, value) { try { localStorage.setItem(key, value); } catch (e) { /* storage blocked */ } }
    };
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // ---- sidebar, drawer, nav groups ------------------------------------------------------------
    function setDrawer(open) {
        if (open) root.setAttribute('data-drawer', 'open'); else root.removeAttribute('data-drawer');
        $$('[data-drawer-toggle]').forEach(function (b) { b.setAttribute('aria-expanded', open ? 'true' : 'false'); });
    }

    $$('[data-drawer-toggle]').forEach(function (button) {
        button.addEventListener('click', function () { setDrawer(root.getAttribute('data-drawer') !== 'open'); });
    });
    var backdrop = $('.drawer-backdrop');
    if (backdrop) backdrop.addEventListener('click', function () { setDrawer(false); });
    $$('.sidebar .nav-link').forEach(function (link) { link.addEventListener('click', function () { setDrawer(false); }); });

    var collapse = $('[data-sidebar-toggle]');
    if (collapse) {
        var syncCollapse = function () {
            var collapsed = root.getAttribute('data-sidebar') === 'collapsed';
            collapse.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
            collapse.setAttribute('aria-label', collapsed ? 'Expand sidebar' : 'Collapse sidebar');
        };
        collapse.addEventListener('click', function () {
            var collapsed = root.getAttribute('data-sidebar') === 'collapsed';
            if (collapsed) root.removeAttribute('data-sidebar'); else root.setAttribute('data-sidebar', 'collapsed');
            store.set('infoid.sidebar', collapsed ? 'expanded' : 'collapsed');
            syncCollapse();
        });
        syncCollapse();
    }

    $$('.nav-group').forEach(function (group) {
        var toggle = $('.nav-group-toggle', group);
        var name = group.getAttribute('data-group');
        if (!toggle || !name) return;
        var activeInside = !!$('.nav-link.is-active', group);
        if (store.get('infoid.nav.' + name) === 'closed' && !activeInside) group.classList.add('is-collapsed');
        toggle.setAttribute('aria-expanded', group.classList.contains('is-collapsed') ? 'false' : 'true');
        toggle.addEventListener('click', function () {
            var closed = group.classList.toggle('is-collapsed');
            toggle.setAttribute('aria-expanded', closed ? 'false' : 'true');
            store.set('infoid.nav.' + name, closed ? 'closed' : 'open');
        });
    });

    // ---- dropdown menus (theme, user) ---------------------------------------------------------
    function closeMenus(except) {
        $$('[data-menu]').forEach(function (menu) {
            if (menu === except) return;
            menu.hidden = true;
            var owner = $('[data-menu-toggle][aria-controls="' + menu.id + '"]');
            if (owner) owner.setAttribute('aria-expanded', 'false');
        });
    }

    $$('[data-menu-toggle]').forEach(function (button) {
        var menu = document.getElementById(button.getAttribute('aria-controls'));
        if (!menu) return;
        button.addEventListener('click', function (event) {
            event.stopPropagation();
            var opening = menu.hidden;
            closeMenus(opening ? menu : null);
            menu.hidden = !opening;
            button.setAttribute('aria-expanded', opening ? 'true' : 'false');
            if (opening) { var first = $('.menu-item', menu); if (first) first.focus(); }
        });
        menu.addEventListener('keydown', function (event) {
            var items = $$('.menu-item', menu);
            var index = items.indexOf(document.activeElement);
            if (event.key === 'ArrowDown') { event.preventDefault(); items[(index + 1) % items.length].focus(); }
            if (event.key === 'ArrowUp') { event.preventDefault(); items[(index - 1 + items.length) % items.length].focus(); }
        });
    });
    document.addEventListener('click', function (event) {
        if (!event.target.closest('[data-menu]')) closeMenus(null);
    });
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') return;
        var open = $$('[data-menu]').filter(function (m) { return !m.hidden; })[0];
        closeMenus(null);
        if (open) { var owner = $('[data-menu-toggle][aria-controls="' + open.id + '"]'); if (owner) owner.focus(); }
        setDrawer(false);
    });

    // ---- theme --------------------------------------------------------------------------------
    function applyTheme(choice) {
        if (choice !== 'light' && choice !== 'dark') choice = 'system';
        root.setAttribute('data-theme', choice);
        store.set('infoid.theme', choice);
        $$('[data-theme-choice]').forEach(function (item) {
            item.setAttribute('aria-checked', item.getAttribute('data-theme-choice') === choice ? 'true' : 'false');
        });
        var label = $('[data-theme-label]');
        if (label) label.textContent = choice === 'system' ? 'Theme: system' : 'Theme: ' + choice;
    }
    $$('[data-theme-choice]').forEach(function (item) {
        item.addEventListener('click', function () { applyTheme(item.getAttribute('data-theme-choice')); closeMenus(null); });
    });
    applyTheme(root.getAttribute('data-theme'));

    // ---- toasts (flash messages arrive as in-page alerts; script turns them into toasts) ----------
    var toastHost = $('.toasts');
    function toast(message, kind) {
        if (!toastHost) return;
        var el = document.createElement('div');
        el.className = 'toast' + (kind === 'error' ? ' error' : '');
        el.setAttribute('role', kind === 'error' ? 'alert' : 'status');
        el.innerHTML = '<svg class="icon" aria-hidden="true"><use href="#i-' + (kind === 'error' ? 'alert' : 'check') + '"/></svg><div class="toast-body"></div><button type="button" aria-label="Dismiss">&times;</button>';
        $('.toast-body', el).textContent = message;
        var remove = function () {
            if (!el.parentNode) return;
            if (reduceMotion) { el.remove(); return; }
            el.classList.add('leaving');
            el.addEventListener('animationend', function () { el.remove(); });
        };
        $('button', el).addEventListener('click', remove);
        toastHost.appendChild(el);
        if (kind !== 'error') setTimeout(remove, 6000);
    }
    window.infoidToast = toast;
    $$('[data-toast]').forEach(function (alert) {
        toast(alert.textContent.trim(), alert.classList.contains('error') ? 'error' : 'ok');
        alert.hidden = true;
    });

    // ---- confirmation dialogs -------------------------------------------------------------------
    var confirmDialog = $('#confirm-dialog');
    function confirmThen(options, onConfirm) {
        if (!confirmDialog || typeof confirmDialog.showModal !== 'function') { if (window.confirm(options.message)) onConfirm(); return; }
        $('[data-confirm-title]', confirmDialog).textContent = options.title || 'Are you sure?';
        $('[data-confirm-message]', confirmDialog).textContent = options.message;
        var ok = $('[data-confirm-ok]', confirmDialog);
        ok.textContent = options.button || 'Confirm';
        ok.className = 'btn ' + (options.tone === 'danger' ? 'danger' : 'primary');
        $('.dialog-head', confirmDialog).classList.toggle('danger', options.tone === 'danger');
        var done = function (result) {
            confirmDialog.close();
            confirmDialog.removeEventListener('cancel', onCancel);
            ok.onclick = null;
            if (result) onConfirm();
        };
        var onCancel = function () { done(false); };
        confirmDialog.addEventListener('cancel', onCancel);
        ok.onclick = function () { done(true); };
        $('[data-confirm-cancel]', confirmDialog).onclick = function () { done(false); };
        confirmDialog.showModal();
        ok.focus();
    }

    // ---- forms: confirm + double-submit protection ------------------------------------------------
    $$('form').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (form.dataset.confirmed === 'true') return; // second pass after the dialog
            var message = form.getAttribute('data-confirm');
            var submitter = event.submitter;
            if (message) {
                event.preventDefault();
                confirmThen({
                    message: message,
                    title: form.getAttribute('data-confirm-title'),
                    button: form.getAttribute('data-confirm-button'),
                    tone: form.getAttribute('data-confirm-tone')
                }, function () {
                    form.dataset.confirmed = 'true';
                    if (form.requestSubmit) form.requestSubmit(submitter || undefined); else form.submit();
                });
                return;
            }
            if (form.method && form.method.toLowerCase() === 'post' && submitter && submitter.classList.contains('btn')) {
                // Disable after the browser has collected the form data, so the value still posts.
                setTimeout(function () { submitter.classList.add('is-busy'); submitter.disabled = true; }, 0);
            }
        });
    });
    window.addEventListener('pageshow', function (event) {
        if (!event.persisted) return;
        $$('.btn.is-busy').forEach(function (b) { b.classList.remove('is-busy'); b.disabled = false; });
        $$('form[data-confirmed]').forEach(function (f) { delete f.dataset.confirmed; });
    });

    // fields that switch each other off (e.g. "Unlimited cards" disables the card limit)
    $$('[data-disables]').forEach(function (box) {
        var targets = $$(box.getAttribute('data-disables'));
        var invert = box.getAttribute('data-disables-when') === 'unchecked';
        var sync = function () { var off = invert ? !box.checked : box.checked; targets.forEach(function (t) { t.disabled = off; }); };
        box.addEventListener('change', sync);
        sync();
    });

    // ---- copy / download --------------------------------------------------------------------------
    function textOf(button) {
        var target = button.getAttribute('data-copy-target') || button.getAttribute('data-download-target');
        if (target) { var el = $(target); return el ? (el.value !== undefined && el.tagName !== 'DIV' ? el.value : el.textContent).trim() : ''; }
        return button.getAttribute('data-copy') || '';
    }
    $$('[data-copy], [data-copy-target]').forEach(function (button) {
        button.addEventListener('click', function () {
            var text = textOf(button);
            var done = function () {
                var label = $('.label', button), previous = label ? label.textContent : '';
                button.classList.add('is-done');
                if (label) label.textContent = 'Copied';
                toast('Copied to clipboard.', 'ok');
                setTimeout(function () { button.classList.remove('is-done'); if (label) label.textContent = previous; }, 1800);
            };
            if (navigator.clipboard && window.isSecureContext) {
                navigator.clipboard.writeText(text).then(done, function () { fallbackCopy(text) && done(); });
            } else if (fallbackCopy(text)) done();
        });
    });
    function fallbackCopy(text) {
        var area = document.createElement('textarea');
        area.value = text; area.setAttribute('readonly', ''); area.style.position = 'fixed'; area.style.opacity = '0';
        document.body.appendChild(area); area.select();
        var ok = false;
        try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
        area.remove();
        return ok;
    }
    $$('[data-download-target]').forEach(function (button) {
        button.addEventListener('click', function () {
            var blob = new Blob([textOf(button) + '\n'], { type: 'text/plain' });
            var link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = button.getAttribute('data-download-name') || 'infoid.txt';
            document.body.appendChild(link); link.click(); link.remove();
            setTimeout(function () { URL.revokeObjectURL(link.href); }, 1000);
        });
    });

    // ---- global search -----------------------------------------------------------------------------
    var searchBox = $('[data-global-search]');
    var searchForm = searchBox ? searchBox.closest('.search') : null;
    document.addEventListener('keydown', function (event) {
        var typing = /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName) || event.target.isContentEditable;
        if (event.key === '/' && !typing && searchBox) { event.preventDefault(); if (searchForm) searchForm.classList.add('is-open'); searchBox.focus(); }
    });
    $$('[data-search-toggle]').forEach(function (button) {
        button.addEventListener('click', function () { if (searchForm) { searchForm.classList.toggle('is-open'); if (searchForm.classList.contains('is-open')) searchBox.focus(); } });
    });

    // ---- greeting and date use the visitor's own clock and locale ---------------------------------------
    $$('[data-greeting]').forEach(function (node) {
        var hour = new Date().getHours();
        var part = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
        var name = node.getAttribute('data-greeting');
        node.textContent = part + (name ? ', ' + name : '');
    });
    $$('[data-today]').forEach(function (node) {
        node.textContent = new Date().toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' });
    });

    // ---- clickable rows ---------------------------------------------------------------------------
    document.addEventListener('click', function (event) {
        var row = event.target.closest('tr[data-href]');
        if (!row || event.target.closest('a, button, input, select, textarea, label')) return;
        window.location.href = row.getAttribute('data-href');
    });

    // ---- detail dialogs (audit events) --------------------------------------------------------------
    var detailDialog = $('#detail-dialog');
    $$('[data-detail-open]').forEach(function (button) {
        button.addEventListener('click', function () {
            if (!detailDialog) return;
            var raw = button.getAttribute('data-detail-json') || '';
            var pretty = raw;
            try { pretty = JSON.stringify(JSON.parse(raw), null, 2); } catch (e) { /* not JSON: show as is */ }
            $('[data-detail-title]', detailDialog).textContent = button.getAttribute('data-detail-title') || 'Details';
            $('[data-detail-meta]', detailDialog).textContent = button.getAttribute('data-detail-meta') || '';
            $('[data-detail-body]', detailDialog).textContent = pretty || 'No details were recorded for this event.';
            detailDialog.showModal();
        });
    });
    $$('dialog [data-dialog-close]').forEach(function (button) {
        button.addEventListener('click', function () { button.closest('dialog').close(); });
    });
    $$('dialog').forEach(function (dialog) {
        dialog.addEventListener('click', function (event) { if (event.target === dialog) dialog.close(); });
    });

    // ---- data tables: quick filter, sort, pagination --------------------------------------------------
    var sortValue = function (cell, kind) {
        var raw = cell ? (cell.getAttribute('data-sort-value') || cell.textContent.trim()) : '';
        if (kind === 'number') { var n = parseFloat(String(raw).replace(/[^0-9.\-]/g, '')); return isNaN(n) ? -Infinity : n; }
        if (kind === 'date') { var t = Date.parse(raw); return isNaN(t) ? -Infinity : t; }
        return String(raw).toLowerCase();
    };

    function enhanceTable(table) {
        var body = table.tBodies[0];
        if (!body) return;
        var rows = $$('tr', body).filter(function (r) { return !r.classList.contains('empty-row'); });
        var card = table.closest('.table-card');
        if (!card || rows.length === 0) return;

        var state = { filter: '', sortIndex: -1, dir: 1, page: 1, size: parseInt(table.getAttribute('data-page-size'), 10) || 25 };
        var searchable = table.getAttribute('data-table') !== 'plain';

        var toolbar = document.createElement('div');
        toolbar.className = 'table-toolbar';
        toolbar.innerHTML = (searchable ? '<div class="search-field"><svg class="icon sm" aria-hidden="true"><use href="#i-search"/></svg><input type="search" placeholder="Filter these rows" aria-label="Filter rows"></div>' : '') +
            '<span class="count" aria-live="polite"></span><span class="grow"></span>' +
            '<label class="sr-only" for="ps-' + Math.random().toString(36).slice(2, 7) + '">Rows per page</label>' +
            '<select aria-label="Rows per page"><option>10</option><option>25</option><option>50</option><option>100</option></select>';
        var wrap = $('.table-wrap', card) || table;
        card.insertBefore(toolbar, wrap);
        var sizeSelect = $('select', toolbar);
        sizeSelect.value = String(state.size);
        if (sizeSelect.value !== String(state.size)) { sizeSelect.add(new Option(String(state.size), String(state.size)), 0); sizeSelect.value = String(state.size); }

        var foot = document.createElement('div');
        foot.className = 'table-foot';
        foot.innerHTML = '<span data-range></span><div class="pager" role="navigation" aria-label="Pagination"></div>';
        card.appendChild(foot);

        var noMatch = document.createElement('tr');
        noMatch.className = 'empty-row';
        noMatch.hidden = true;
        noMatch.innerHTML = '<td colspan="' + table.tHead.rows[0].cells.length + '"><div class="empty"><h3>No rows match</h3><p>Try a different filter.</p></div></td>';
        body.appendChild(noMatch);

        var headers = $$('th', table.tHead.rows[0]);
        headers.forEach(function (th, index) {
            if (!th.hasAttribute('data-sort')) return;
            th.tabIndex = 0;
            th.setAttribute('aria-sort', 'none');
            var activate = function () {
                state.dir = state.sortIndex === index ? -state.dir : 1;
                state.sortIndex = index; state.page = 1;
                headers.forEach(function (h) { if (h.hasAttribute('data-sort')) h.setAttribute('aria-sort', 'none'); });
                th.setAttribute('aria-sort', state.dir === 1 ? 'ascending' : 'descending');
                render();
            };
            th.addEventListener('click', activate);
            th.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); activate(); } });
        });

        var originalOrder = rows.slice();
        function render() {
            var filtered = rows.filter(function (r) { return !state.filter || r.textContent.toLowerCase().indexOf(state.filter) !== -1; });
            if (state.sortIndex >= 0) {
                var kind = headers[state.sortIndex].getAttribute('data-sort');
                filtered.sort(function (a, b) {
                    var x = sortValue(a.cells[state.sortIndex], kind), y = sortValue(b.cells[state.sortIndex], kind);
                    return (x < y ? -1 : x > y ? 1 : 0) * state.dir;
                });
            } else {
                filtered.sort(function (a, b) { return originalOrder.indexOf(a) - originalOrder.indexOf(b); });
            }
            var pages = Math.max(1, Math.ceil(filtered.length / state.size));
            if (state.page > pages) state.page = pages;
            var start = (state.page - 1) * state.size, end = start + state.size;
            rows.forEach(function (r) { r.hidden = true; });
            filtered.forEach(function (r, i) { body.insertBefore(r, noMatch); r.hidden = !(i >= start && i < end); });
            noMatch.hidden = filtered.length !== 0;

            $('.count', toolbar).textContent = filtered.length === rows.length ? rows.length + (rows.length === 1 ? ' row' : ' rows') : filtered.length + ' of ' + rows.length + ' rows';
            $('[data-range]', foot).textContent = filtered.length === 0 ? '' : 'Showing ' + (start + 1) + '-' + Math.min(end, filtered.length) + ' of ' + filtered.length;
            buildPager($('.pager', foot), pages);
            foot.hidden = pages <= 1 && filtered.length <= 10;
        }

        function buildPager(host, pages) {
            host.textContent = '';
            if (pages <= 1) return;
            var add = function (label, page, opts) {
                var b = document.createElement('button');
                b.type = 'button'; b.textContent = label; b.disabled = !!(opts && opts.disabled);
                if (opts && opts.aria) b.setAttribute('aria-label', opts.aria);
                if (opts && opts.current) b.setAttribute('aria-current', 'page');
                b.addEventListener('click', function () { state.page = page; render(); });
                host.appendChild(b);
            };
            add('‹', state.page - 1, { disabled: state.page === 1, aria: 'Previous page' });
            var shown = [];
            for (var p = 1; p <= pages; p++) if (p === 1 || p === pages || Math.abs(p - state.page) <= 1) shown.push(p);
            var last = 0;
            shown.forEach(function (p) {
                if (p - last > 1) { var gap = document.createElement('span'); gap.textContent = '…'; gap.className = 'muted'; host.appendChild(gap); }
                add(String(p), p, { current: p === state.page, aria: 'Page ' + p });
                last = p;
            });
            add('›', state.page + 1, { disabled: state.page === pages, aria: 'Next page' });
        }

        var input = $('input', toolbar);
        if (input) input.addEventListener('input', function () { state.filter = input.value.trim().toLowerCase(); state.page = 1; render(); });
        sizeSelect.addEventListener('change', function () { state.size = parseInt(sizeSelect.value, 10); state.page = 1; render(); });
        render();
    }
    $$('table[data-table]').forEach(enhanceTable);
})();
