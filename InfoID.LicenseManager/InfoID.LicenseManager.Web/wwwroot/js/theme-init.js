// Runs in <head> before the first paint so the chosen theme and sidebar state never flash. Kept tiny and dependency free.
(function () {
    var root = document.documentElement;
    try {
        var theme = localStorage.getItem('infoid.theme');
        root.setAttribute('data-theme', theme === 'light' || theme === 'dark' ? theme : 'system');
        if (localStorage.getItem('infoid.sidebar') === 'collapsed') root.setAttribute('data-sidebar', 'collapsed');
    } catch (e) {
        root.setAttribute('data-theme', 'system'); // storage blocked: follow the operating system
    }
})();
