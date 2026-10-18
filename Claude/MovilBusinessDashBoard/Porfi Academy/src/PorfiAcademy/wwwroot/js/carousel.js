// Carrusel promocional de la portada: cambia solo cada 10 s (data-interval), con flechas, puntos y barra de tiempo.
(function () {
    var root = document.querySelector('.carousel');
    if (!root) return;

    var slides = Array.prototype.slice.call(root.querySelectorAll('.slide'));
    var dots = Array.prototype.slice.call(root.querySelectorAll('.dot'));
    var interval = parseInt(root.getAttribute('data-interval'), 10) || 10000;
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var index = 0, timer = null, hovering = false, counted = {};

    root.style.setProperty('--interval', interval + 'ms');

    function playVideo(slide, on) {
        var v = slide.querySelector('mux-player');
        if (!v) return;
        if (on) {
            // El video se carga recién cuando su diapositiva se muestra (ahorra datos en el celular).
            if (!v.getAttribute('playback-id')) v.setAttribute('playback-id', v.getAttribute('data-pid'));
            if (v.play) { try { var p = v.play(); if (p && p.catch) p.catch(function () {}); } catch (e) {} }
        } else if (v.pause) {
            try { v.pause(); } catch (e) {}
        }
    }

    function show(n) {
        index = (n + slides.length) % slides.length;
        slides.forEach(function (s, k) {
            var on = k === index;
            s.classList.toggle('active', on);
            s.setAttribute('aria-hidden', on ? 'false' : 'true');
            playVideo(s, on);
        });
        dots.forEach(function (d, k) { d.classList.toggle('on', k === index); });

        // Reinicia la barra de tiempo.
        root.classList.remove('run');
        void root.offsetWidth;
        root.classList.add('run');

        // Cuenta una vista por diapositiva y carga de página (las de ejemplo, id 0, no cuentan).
        var id = slides[index].getAttribute('data-id');
        if (id && id !== '0' && !counted[id]) {
            counted[id] = true;
            if (navigator.sendBeacon) navigator.sendBeacon('/promo/view/' + id);
        }
    }

    function start() {
        stop();
        if (reduceMotion || slides.length < 2) return;
        timer = setInterval(function () {
            if (!hovering && !document.hidden) show(index + 1);
        }, interval);
    }

    function stop() { if (timer) { clearInterval(timer); timer = null; } }

    function go(n) { show(n); start(); }

    var prev = root.querySelector('.prev'), next = root.querySelector('.next');
    if (prev) prev.addEventListener('click', function () { go(index - 1); });
    if (next) next.addEventListener('click', function () { go(index + 1); });
    dots.forEach(function (d, k) { d.addEventListener('click', function () { go(k); }); });

    root.addEventListener('mouseenter', function () { hovering = true; root.classList.add('paused'); });
    root.addEventListener('mouseleave', function () { hovering = false; root.classList.remove('paused'); });
    root.addEventListener('focusin', function () { hovering = true; root.classList.add('paused'); });
    root.addEventListener('focusout', function () { hovering = false; root.classList.remove('paused'); });

    // Deslizar con el dedo en el celular.
    var startX = null;
    root.addEventListener('touchstart', function (e) { startX = e.touches[0].clientX; }, { passive: true });
    root.addEventListener('touchend', function (e) {
        if (startX === null) return;
        var dx = e.changedTouches[0].clientX - startX;
        startX = null;
        if (Math.abs(dx) > 40) go(index + (dx < 0 ? 1 : -1));
    }, { passive: true });

    if (reduceMotion) root.classList.add('paused');
    show(0);
    start();
})();
