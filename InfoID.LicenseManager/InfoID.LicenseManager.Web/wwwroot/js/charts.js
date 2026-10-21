// Small, dependency-free SVG charts for the dashboard. Data comes from the server as JSON in data-chart-data;
// colours come from CSS classes (series-N / fill-N), so light and dark themes need no redraw.
(function () {
    'use strict';

    var NS = 'http://www.w3.org/2000/svg';
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function el(name, attrs, parent) {
        var node = document.createElementNS(NS, name);
        Object.keys(attrs || {}).forEach(function (k) { node.setAttribute(k, attrs[k]); });
        if (parent) parent.appendChild(node);
        return node;
    }
    function fmt(n) { return Number(n).toLocaleString('en-US'); }

    /** A readable top value for the y axis: 1, 2, 5, 10, 20, 50 ... (integers only for counts). */
    function niceMax(max) {
        if (max <= 4) return 4;
        var pow = Math.pow(10, Math.floor(Math.log10(max)));
        var steps = [1, 2, 2.5, 5, 10];
        for (var i = 0; i < steps.length; i++) if (steps[i] * pow >= max) return steps[i] * pow;
        return 10 * pow;
    }

    function tipFor(host) {
        var tip = host.querySelector('.chart-tip');
        if (!tip) { tip = document.createElement('div'); tip.className = 'chart-tip'; host.appendChild(tip); }
        return tip;
    }
    function showTip(host, tip, x, y, title, body) {
        tip.innerHTML = '<strong></strong><span></span>';
        tip.firstChild.textContent = title;
        tip.lastChild.textContent = body;
        tip.style.left = x + 'px';
        tip.style.top = y + 'px';
        tip.classList.add('is-on');
    }

    function empty(host, message) {
        host.classList.add('is-ready');
        host.innerHTML = '<div class="chart-empty"><svg class="icon lg" aria-hidden="true"><use href="#i-activity"/></svg><strong>No data yet</strong><span></span></div>';
        host.querySelector('span').textContent = message || 'Nothing to show for this period.';
    }

    // ---- area / line chart -------------------------------------------------------------------------
    function area(host, data) {
        var points = data.points || [];
        var total = points.reduce(function (s, p) { return s + p.value; }, 0);
        if (points.length === 0 || total === 0) return empty(host, data.emptyText);

        var width = Math.max(280, host.clientWidth), height = 250;
        var m = { top: 14, right: 14, bottom: 28, left: 38 };
        var iw = width - m.left - m.right, ih = height - m.top - m.bottom;
        var max = niceMax(Math.max.apply(null, points.map(function (p) { return p.value; })));
        var x = function (i) { return m.left + (points.length === 1 ? iw / 2 : (i / (points.length - 1)) * iw); };
        var y = function (v) { return m.top + ih - (v / max) * ih; };

        host.innerHTML = '';
        var svg = el('svg', { viewBox: '0 0 ' + width + ' ' + height, role: 'img', 'aria-label': (data.label || 'Chart') + ': ' + fmt(total) + ' ' + (data.unit || 'in total') + ' over ' + points.length + ' days' }, host);

        for (var t = 0; t <= 4; t++) {
            var v = (max / 4) * t, gy = y(v);
            el('line', { x1: m.left, x2: width - m.right, y1: gy, y2: gy, class: 'grid-line' }, svg);
            var label = el('text', { x: m.left - 8, y: gy + 4, 'text-anchor': 'end', class: 'axis-text' }, svg);
            label.textContent = fmt(Math.round(v * 10) / 10);
        }
        var every = Math.max(1, Math.ceil(points.length / Math.max(2, Math.floor(iw / 70))));
        points.forEach(function (p, i) {
            if (i % every !== 0 && i !== points.length - 1) return;
            if (i === points.length - 1 && i % every !== 0 && (points.length - 1 - (Math.floor((points.length - 1) / every) * every)) < every / 2) return;
            var tx = el('text', { x: x(i), y: height - 8, 'text-anchor': i === 0 ? 'start' : i === points.length - 1 ? 'end' : 'middle', class: 'axis-text' }, svg);
            tx.textContent = p.label;
        });

        var line = points.map(function (p, i) { return (i === 0 ? 'M' : 'L') + x(i).toFixed(1) + ' ' + y(p.value).toFixed(1); }).join(' ');
        var base = y(0);
        var defs = el('defs', {}, svg);
        var gradId = 'g' + Math.random().toString(36).slice(2, 8);
        var grad = el('linearGradient', { id: gradId, x1: 0, y1: 0, x2: 0, y2: 1 }, defs);
        el('stop', { offset: '0%', 'stop-opacity': '.28', class: 'stop-1' }, grad);
        el('stop', { offset: '100%', 'stop-opacity': '0', class: 'stop-1' }, grad);
        el('path', { d: line + ' L' + x(points.length - 1).toFixed(1) + ' ' + base + ' L' + x(0).toFixed(1) + ' ' + base + ' Z', fill: 'url(#' + gradId + ')', class: 'area' }, svg);
        var path = el('path', { d: line, class: 'line series-1' }, svg);
        try { path.style.setProperty('--len', Math.ceil(path.getTotalLength()) + 2); } catch (e) { /* not measurable: skip the draw-in */ }

        var hoverLine = el('line', { class: 'hover-line', y1: m.top, y2: m.top + ih, opacity: 0 }, svg);
        var dot = el('circle', { r: 5, class: 'hover-dot fill-1', opacity: 0 }, svg);
        var tip = tipFor(host);
        var overlay = el('rect', { x: m.left, y: m.top, width: iw, height: ih, fill: 'transparent' }, svg);
        function move(event) {
            var box = svg.getBoundingClientRect();
            var scale = width / box.width;
            var px = (event.clientX - box.left) * scale;
            var i = Math.min(points.length - 1, Math.max(0, Math.round(((px - m.left) / iw) * (points.length - 1))));
            hoverLine.setAttribute('x1', x(i)); hoverLine.setAttribute('x2', x(i)); hoverLine.setAttribute('opacity', 1);
            dot.setAttribute('cx', x(i)); dot.setAttribute('cy', y(points[i].value)); dot.setAttribute('opacity', 1);
            showTip(host, tip, (x(i) / width) * box.width, (y(points[i].value) / height) * box.height, points[i].title || points[i].label, fmt(points[i].value) + ' ' + (data.unit || ''));
        }
        overlay.addEventListener('mousemove', move);
        overlay.addEventListener('touchstart', function (e) { move(e.touches[0]); }, { passive: true });
        overlay.addEventListener('mouseleave', function () { hoverLine.setAttribute('opacity', 0); dot.setAttribute('opacity', 0); tip.classList.remove('is-on'); });
        host.classList.add('is-ready');
    }

    // ---- donut ----------------------------------------------------------------------------------------
    function donut(host, data) {
        var items = (data.items || []).filter(function (i) { return i.value > 0; });
        var total = items.reduce(function (s, i) { return s + i.value; }, 0);
        if (total === 0) return empty(host, data.emptyText);

        var size = 190, cx = size / 2, cy = size / 2, outer = 86, inner = 58;
        host.innerHTML = '';
        var svg = el('svg', { viewBox: '0 0 ' + size + ' ' + size, role: 'img', 'aria-label': (data.label || 'Distribution') + ': ' + items.map(function (i) { return i.label + ' ' + fmt(i.value); }).join(', ') }, host);
        var angle = -Math.PI / 2;
        var tip = tipFor(host);
        var slices = [];
        items.forEach(function (item, index) {
            var sweep = (item.value / total) * Math.PI * 2;
            var path;
            if (items.length === 1) {
                path = 'M' + cx + ' ' + (cy - outer) + ' A' + outer + ' ' + outer + ' 0 1 1 ' + (cx - 0.01) + ' ' + (cy - outer) + ' L' + (cx - 0.01) + ' ' + (cy - inner) + ' A' + inner + ' ' + inner + ' 0 1 0 ' + cx + ' ' + (cy - inner) + ' Z';
            } else {
                var a0 = angle, a1 = angle + sweep, large = sweep > Math.PI ? 1 : 0;
                var p = function (r, a) { return (cx + r * Math.cos(a)).toFixed(2) + ' ' + (cy + r * Math.sin(a)).toFixed(2); };
                path = 'M' + p(outer, a0) + ' A' + outer + ' ' + outer + ' 0 ' + large + ' 1 ' + p(outer, a1) + ' L' + p(inner, a1) + ' A' + inner + ' ' + inner + ' 0 ' + large + ' 0 ' + p(inner, a0) + ' Z';
            }
            angle += sweep;
            var slice = el('path', { d: path, class: 'slice fill-' + (item.series || index + 1) }, svg);
            slices.push(slice);
            slice.addEventListener('mousemove', function (e) {
                var box = host.getBoundingClientRect();
                showTip(host, tip, e.clientX - box.left, e.clientY - box.top, item.label, fmt(item.value) + ' (' + Math.round((item.value / total) * 100) + '%)');
                slices.forEach(function (s) { s.classList.toggle('dim', s !== slice); });
            });
            slice.addEventListener('mouseleave', function () { tip.classList.remove('is-on'); slices.forEach(function (s) { s.classList.remove('dim'); }); });
        });
        var big = el('text', { x: cx, y: cy + 6, 'text-anchor': 'middle', class: 'donut-center' }, svg);
        big.textContent = fmt(total);
        var sub = el('text', { x: cx, y: cy + 24, 'text-anchor': 'middle', class: 'donut-center-sub' }, svg);
        sub.textContent = data.totalLabel || 'total';
        host.classList.add('is-ready');
    }

    // ---- vertical bars ----------------------------------------------------------------------------------
    function bars(host, data) {
        var items = data.items || [];
        var total = items.reduce(function (s, i) { return s + i.value; }, 0);
        if (items.length === 0 || total === 0) return empty(host, data.emptyText);

        var width = Math.max(260, host.clientWidth), height = 230;
        var m = { top: 14, right: 10, bottom: 30, left: 34 };
        var iw = width - m.left - m.right, ih = height - m.top - m.bottom;
        var max = niceMax(Math.max.apply(null, items.map(function (i) { return i.value; })));
        var slot = iw / items.length, bw = Math.min(54, slot * 0.58);
        host.innerHTML = '';
        var svg = el('svg', { viewBox: '0 0 ' + width + ' ' + height, role: 'img', 'aria-label': (data.label || 'Chart') + ': ' + items.map(function (i) { return i.label + ' ' + fmt(i.value); }).join(', ') }, host);
        for (var t = 0; t <= 4; t++) {
            var v = (max / 4) * t, gy = m.top + ih - (v / max) * ih;
            el('line', { x1: m.left, x2: width - m.right, y1: gy, y2: gy, class: 'grid-line' }, svg);
            var label = el('text', { x: m.left - 8, y: gy + 4, 'text-anchor': 'end', class: 'axis-text' }, svg);
            label.textContent = fmt(Math.round(v * 10) / 10);
        }
        var tip = tipFor(host);
        items.forEach(function (item, i) {
            var h = (item.value / max) * ih, bx = m.left + slot * i + (slot - bw) / 2, by = m.top + ih - h;
            var bar = el('rect', { x: bx, y: by, width: bw, height: Math.max(h, item.value > 0 ? 2 : 0), rx: 5, class: 'bar fill-' + (item.series || 1) }, svg);
            if (!reduceMotion) bar.style.animationDelay = (i * 70) + 'ms';
            var tx = el('text', { x: bx + bw / 2, y: height - 9, 'text-anchor': 'middle', class: 'axis-text' }, svg);
            tx.textContent = item.label;
            if (item.value > 0) {
                var num = el('text', { x: bx + bw / 2, y: by - 6, 'text-anchor': 'middle', class: 'axis-text strong' }, svg);
                num.textContent = fmt(item.value);
            }
            bar.addEventListener('mousemove', function (e) {
                var box = host.getBoundingClientRect();
                showTip(host, tip, e.clientX - box.left, e.clientY - box.top, item.label, fmt(item.value) + ' ' + (data.unit || ''));
            });
            bar.addEventListener('mouseleave', function () { tip.classList.remove('is-on'); });
        });
        host.classList.add('is-ready');
    }

    var drawers = { area: area, donut: donut, bars: bars };

    function draw(host) {
        var kind = host.getAttribute('data-chart');
        try {
            var data = JSON.parse(host.getAttribute('data-chart-data') || '{}');
            if (!drawers[kind]) throw new Error('Unknown chart type ' + kind);
            drawers[kind](host, data);
        } catch (e) {
            host.classList.add('is-ready');
            host.innerHTML = '<div class="chart-empty"><svg class="icon lg" aria-hidden="true"><use href="#i-alert"/></svg><strong>This chart could not be drawn</strong><span>Reload the page to try again.</span></div>';
            if (window.console) console.error(e);
        }
    }

    var hosts = Array.prototype.slice.call(document.querySelectorAll('[data-chart]'));
    hosts.forEach(draw);

    // Redraw on width changes only (not on every layout tick), so charts stay crisp when the sidebar collapses or the window resizes.
    if (window.ResizeObserver) {
        var widths = new Map(), timer = null;
        hosts.forEach(function (h) { widths.set(h, h.clientWidth); });
        var observer = new ResizeObserver(function (entries) {
            var changed = entries.filter(function (e) { return Math.abs(e.contentRect.width - (widths.get(e.target) || 0)) > 8; });
            if (changed.length === 0) return;
            clearTimeout(timer);
            timer = setTimeout(function () { changed.forEach(function (e) { widths.set(e.target, e.target.clientWidth); draw(e.target); }); }, 120);
        });
        hosts.forEach(function (h) { observer.observe(h); });
    }
})();
